// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.OdinMigration.Generator
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Security.Cryptography;
    using System.Text;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.CSharp.Syntax;
    using Microsoft.CodeAnalysis.Text;
    using WallstopStudios.UnityHelpers.Proto.Generator;

    /// <summary>Captures compiler-resolved dictionary declarations for editor migrations.</summary>
    [Generator]
    public sealed class OdinMigrationBindingGenerator : ISourceGenerator
    {
        private const string BindingNamespace =
            "WallstopStudios.UnityHelpers.Core.Serialization.OdinMigration";
        private const string Registry =
            "global::" + BindingNamespace + ".OdinMigrationCompiledBindings";
        private const string Binding =
            "global::" + BindingNamespace + ".OdinMigrationCompiledBinding";
        private const string Source =
            "global::" + BindingNamespace + ".OdinMigrationCompiledSource";
        private const string Field = "global::" + BindingNamespace + ".OdinMigrationCompiledField";
        private const string LiveDictionary =
            "global::" + BindingNamespace + ".OdinMigrationLiveDictionary";

        private static string Literal(string value)
        {
            return SymbolDisplay.FormatLiteral(value, true);
        }

        private static string MetadataName(INamedTypeSymbol type)
        {
            return type.ContainingType != null
                ? MetadataName(type.ContainingType) + "+" + type.MetadataName
                : (
                    type.ContainingNamespace.IsGlobalNamespace
                        ? string.Empty
                        : type.ContainingNamespace.ToDisplayString() + "."
                ) + type.MetadataName;
        }

        private static string RuntimeFullNameExpression(ITypeSymbol type, Compilation compilation)
        {
            if (type is IDynamicTypeSymbol)
            {
                type = compilation.ObjectType;
            }
            if (type is IArrayTypeSymbol array)
            {
                return RuntimeFullNameExpression(array.ElementType, compilation)
                    + " + "
                    + Literal("[" + new string(',', array.Rank - 1) + "]");
            }
            INamedTypeSymbol named = (INamedTypeSymbol)type;
            StringBuilder name = new StringBuilder(Literal(MetadataName(named)));
            Stack<INamedTypeSymbol> owners = new Stack<INamedTypeSymbol>();
            for (INamedTypeSymbol owner = named; owner != null; owner = owner.ContainingType)
            {
                owners.Push(owner);
            }
            bool first = true;
            foreach (INamedTypeSymbol owner in owners)
            {
                foreach (ITypeSymbol argument in owner.TypeArguments)
                {
                    name.Append(" + ").Append(Literal(first ? "[[" : ",[")).Append(" + ");
                    first = false;
                    ITypeSymbol runtimeArgument =
                        argument is IDynamicTypeSymbol ? compilation.ObjectType : argument;
                    if (TypeNaming.IsNameable(runtimeArgument, compilation))
                    {
                        name.Append("typeof(")
                            .Append(
                                runtimeArgument.ToDisplayString(
                                    SymbolDisplayFormat.FullyQualifiedFormat
                                )
                            )
                            .Append(").AssemblyQualifiedName");
                    }
                    else
                    {
                        name.Append(RuntimeFullNameExpression(runtimeArgument, compilation))
                            .Append(" + ")
                            .Append(Literal(", "))
                            .Append(" + ")
                            .Append(AssemblyNameExpression(runtimeArgument, compilation));
                    }
                    name.Append(" + ").Append(Literal("]"));
                }
            }
            if (!first)
            {
                name.Append(" + ").Append(Literal("]"));
            }
            return name.ToString();
        }

        private static string AssemblyNameExpression(ITypeSymbol type, Compilation compilation)
        {
            if (
                TypeNaming.IsNameable(type, compilation)
                && !SymbolEqualityComparer.Default.Equals(
                    type.ContainingAssembly,
                    compilation.Assembly
                )
            )
            {
                return "typeof("
                    + type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                    + ").Assembly.FullName";
            }
            ITypeSymbol owner = type;
            while (owner is IArrayTypeSymbol array)
            {
                owner = array.ElementType;
            }
            return Literal(owner.ContainingAssembly.Identity.ToString());
        }

        private static bool IsUnityObject(ITypeSymbol type)
        {
            for (
                INamedTypeSymbol current = type as INamedTypeSymbol;
                current != null;
                current = current.BaseType
            )
            {
                if (
                    string.Equals(
                        MetadataName(current),
                        "UnityEngine.Object",
                        StringComparison.Ordinal
                    )
                )
                {
                    return true;
                }
            }
            return false;
        }

        private static bool TryDictionary(
            ITypeSymbol type,
            out INamedTypeSymbol key,
            out INamedTypeSymbol element
        )
        {
            key = null;
            element = null;
            if (
                !(type is INamedTypeSymbol dictionary)
                || !string.Equals(
                    MetadataName(dictionary.OriginalDefinition),
                    "System.Collections.Generic.Dictionary`2",
                    StringComparison.Ordinal
                )
                || !(dictionary.TypeArguments[0] is INamedTypeSymbol enumType)
                || enumType.TypeKind != TypeKind.Enum
                || enumType.EnumUnderlyingType.SpecialType != SpecialType.System_Int32
                || !(dictionary.TypeArguments[1] is INamedTypeSymbol list)
                || !string.Equals(
                    MetadataName(list.OriginalDefinition),
                    "System.Collections.Generic.List`1",
                    StringComparison.Ordinal
                )
                || !(list.TypeArguments[0] is INamedTypeSymbol objectType)
                || !IsUnityObject(objectType)
            )
            {
                return false;
            }
            key = enumType;
            element = objectType;
            return true;
        }

        private static string Checksum(SyntaxTree tree)
        {
            using (SHA256 hash = SHA256.Create())
            {
                return Convert.ToBase64String(
                    hash.ComputeHash(Encoding.UTF8.GetBytes(tree.GetText().ToString()))
                );
            }
        }

        private static void CaptureDeclarations(
            INamedTypeSymbol type,
            SortedDictionary<string, SyntaxTree> sources
        )
        {
            for (INamedTypeSymbol current = type; current != null; current = current.ContainingType)
            {
                foreach (SyntaxReference part in current.DeclaringSyntaxReferences)
                {
                    sources[part.SyntaxTree.FilePath] = part.SyntaxTree;
                }
            }
        }

        private static string CaptureExpression(
            IFieldSymbol field,
            INamedTypeSymbol owner,
            Compilation compilation,
            bool insideOwner = false
        )
        {
            if (
                !TypeNaming.IsNameable(owner, compilation)
                || (
                    !insideOwner
                    && (
                        !TypeNaming.IsNameable(field.Type, compilation)
                        || !compilation.IsSymbolAccessibleWithin(field, compilation.Assembly)
                    )
                )
            )
            {
                return "null";
            }
            string type = owner.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            return "(global::UnityEngine.Object value, out "
                + LiveDictionary
                + " captured, out string failure) => { if (!(value is "
                + type
                + " original)) { captured = null; failure = \"The live dictionary owner has the wrong compiled type.\"; return false; } return "
                + LiveDictionary
                + ".TryCapture(original.@"
                + field.Name
                + ", key => (int)key, out captured, out failure); }";
        }

        private static string RestoreExpression(
            IFieldSymbol field,
            INamedTypeSymbol owner,
            Compilation compilation,
            bool insideOwner = false
        )
        {
            if (
                !TypeNaming.IsNameable(owner, compilation)
                || (
                    !insideOwner
                    && (
                        !TypeNaming.IsNameable(field.Type, compilation)
                        || !compilation.IsSymbolAccessibleWithin(field, compilation.Assembly)
                    )
                )
                || !TryDictionary(
                    field.Type,
                    out INamedTypeSymbol key,
                    out INamedTypeSymbol element
                )
            )
            {
                return "null";
            }
            string ownerType = owner.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            string keyType = key.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            string elementType = element.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            string writer =
                "(global::UnityEngine.Object value, global::System.Collections.Generic.IReadOnlyList<int> keys, global::System.Collections.Generic.IReadOnlyList<global::System.Collections.Generic.IReadOnlyList<global::UnityEngine.Object>> values, out string failure) => { if (!(value is "
                + ownerType
                + " original)) { failure = \"The live dictionary owner has the wrong compiled type.\"; return false; } ";
            if (field.IsReadOnly)
            {
                return writer
                    + "return "
                    + LiveDictionary
                    + ".TryRestoreContents<"
                    + keyType
                    + ", "
                    + elementType
                    + ">(original.@"
                    + field.Name
                    + ", keys, values, key => ("
                    + keyType
                    + ")key, out failure); }";
            }
            return writer
                + "if (!"
                + LiveDictionary
                + ".TryRebuild<"
                + keyType
                + ", "
                + elementType
                + ">(keys, values, key => ("
                + keyType
                + ")key, out global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", global::System.Collections.Generic.List<"
                + elementType
                + ">> rebuilt, out failure)) { return false; } original.@"
                + field.Name
                + " = rebuilt; return true; }";
        }

        private static bool CanGenerateOwnerAccess(INamedTypeSymbol owner)
        {
            for (
                INamedTypeSymbol current = owner;
                current != null;
                current = current.ContainingType
            )
            {
                if (current.Arity != 0 || current.DeclaringSyntaxReferences.Length == 0)
                {
                    return false;
                }
                foreach (SyntaxReference part in current.DeclaringSyntaxReferences)
                {
                    if (
                        !(part.GetSyntax() is ClassDeclarationSyntax declaration)
                        || !declaration.Modifiers.Any(SyntaxKind.PartialKeyword)
                    )
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        private static bool HasMemberInHierarchy(INamedTypeSymbol owner, string name)
        {
            for (INamedTypeSymbol current = owner; current != null; current = current.BaseType)
            {
                if (!current.GetMembers(name).IsEmpty)
                {
                    return true;
                }
            }
            return false;
        }

        private static string AccessorName(
            INamedTypeSymbol owner,
            IFieldSymbol field,
            string operation
        )
        {
            string stem =
                "CreateOdinMigration"
                + operation
                + field.Name.Replace("_", string.Empty)
                + "At"
                + field
                    .DeclaringSyntaxReferences[0]
                    .Span.Start.ToString(CultureInfo.InvariantCulture)
                + "From"
                + BitConverter
                    .ToString(
                        Convert.FromBase64String(
                            Checksum(field.DeclaringSyntaxReferences[0].SyntaxTree)
                        )
                    )
                    .Replace("-", string.Empty)
                    .Substring(0, 16);
            string name = stem;
            int suffix = 0;
            while (HasMemberInHierarchy(owner, name))
            {
                name = stem + (++suffix).ToString(CultureInfo.InvariantCulture);
            }
            return name;
        }

        private static string OwnerAccess(
            IFieldSymbol field,
            INamedTypeSymbol owner,
            Compilation compilation,
            SortedDictionary<string, string> accessors,
            bool restore
        )
        {
            string expression = restore
                ? RestoreExpression(field, owner, compilation)
                : CaptureExpression(field, owner, compilation);
            if (
                !string.Equals(expression, "null", StringComparison.Ordinal)
                || !CanGenerateOwnerAccess(owner)
                || !TypeNaming.IsNameable(owner, compilation)
            )
            {
                return expression;
            }
            expression = restore
                ? RestoreExpression(field, owner, compilation, insideOwner: true)
                : CaptureExpression(field, owner, compilation, insideOwner: true);
            if (string.Equals(expression, "null", StringComparison.Ordinal))
            {
                return expression;
            }
            string name = AccessorName(owner, field, restore ? "Restore" : "Capture");
            string qualified = owner.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            string key = qualified + "." + name;
            if (!accessors.ContainsKey(key))
            {
                StringBuilder source = new StringBuilder();
                bool namespaced = !owner.ContainingNamespace.IsGlobalNamespace;
                if (namespaced)
                {
                    source
                        .Append("namespace ")
                        .Append(owner.ContainingNamespace.ToDisplayString())
                        .Append(" {");
                }
                Stack<INamedTypeSymbol> containers = new Stack<INamedTypeSymbol>();
                for (
                    INamedTypeSymbol current = owner;
                    current != null;
                    current = current.ContainingType
                )
                {
                    containers.Push(current);
                }
                int depth = containers.Count;
                foreach (INamedTypeSymbol container in containers)
                {
                    source.Append("partial class @").Append(container.Name).Append(" {");
                }
                source
                    .Append("internal static global::")
                    .Append(BindingNamespace)
                    .Append(
                        restore
                            ? ".OdinMigrationDictionaryRestore "
                            : ".OdinMigrationDictionaryCapture "
                    )
                    .Append(name)
                    .Append("() { return ")
                    .Append(expression)
                    .Append("; }");
                for (int index = 0; index < depth; ++index)
                {
                    source.Append('}');
                }
                if (namespaced)
                {
                    source.Append('}');
                }
                accessors.Add(key, source.ToString());
            }
            return key + "()";
        }

        private static bool HasDictionaryFields(INamedTypeSymbol owner)
        {
            for (
                INamedTypeSymbol current = owner;
                current != null && IsUnityObject(current);
                current = current.BaseType
            )
            {
                foreach (AttributeData attribute in current.ContainingAssembly.GetAttributes())
                {
                    if (
                        attribute.AttributeClass != null
                        && string.Equals(
                            MetadataName(attribute.AttributeClass),
                            BindingNamespace + ".OdinMigrationBindingAttribute",
                            StringComparison.Ordinal
                        )
                        && attribute.ConstructorArguments.Length == 1
                        && attribute.ConstructorArguments[0].Value is INamedTypeSymbol registered
                        && SymbolEqualityComparer.Default.Equals(current, registered)
                    )
                    {
                        return true;
                    }
                }
                foreach (ISymbol member in current.GetMembers())
                {
                    if (
                        member is IFieldSymbol field
                        && !field.IsStatic
                        && !field.IsImplicitlyDeclared
                        && TryDictionary(field.Type, out INamedTypeSymbol _, out INamedTypeSymbol _)
                    )
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static bool IsMigrationRootBase(INamedTypeSymbol type)
        {
            string name = MetadataName(type);
            return string.Equals(name, "UnityEngine.Object", StringComparison.Ordinal)
                || string.Equals(name, "UnityEngine.MonoBehaviour", StringComparison.Ordinal)
                || string.Equals(name, "UnityEngine.ScriptableObject", StringComparison.Ordinal)
                || string.Equals(
                    name,
                    "Sirenix.OdinInspector.SerializedMonoBehaviour",
                    StringComparison.Ordinal
                )
                || string.Equals(
                    name,
                    "Sirenix.OdinInspector.SerializedScriptableObject",
                    StringComparison.Ordinal
                );
        }

        /// <inheritdoc />
        public void Initialize(GeneratorInitializationContext context) { }

        /// <inheritdoc />
        public void Execute(GeneratorExecutionContext context)
        {
            if (
                context.Compilation.GetTypeByMetadataName(
                    BindingNamespace + ".OdinMigrationCompiledBindings"
                ) == null
                || context.Compilation.GetTypeByMetadataName(
                    "UnityEditor.InitializeOnLoadMethodAttribute"
                ) == null
            )
            {
                return;
            }
            bool editor = false;
            foreach (string symbol in context.ParseOptions.PreprocessorSymbolNames)
            {
                if (string.Equals(symbol, "UNITY_EDITOR", StringComparison.Ordinal))
                {
                    editor = true;
                    break;
                }
            }
            if (!editor)
            {
                return;
            }
            HashSet<INamedTypeSymbol> visited = new HashSet<INamedTypeSymbol>(
                SymbolEqualityComparer.Default
            );
            SortedDictionary<string, string> registrations = new SortedDictionary<string, string>(
                StringComparer.Ordinal
            );
            SortedDictionary<string, string> accessors = new SortedDictionary<string, string>(
                StringComparer.Ordinal
            );
            foreach (SyntaxTree tree in context.Compilation.SyntaxTrees)
            {
                SemanticModel model = null;
                foreach (SyntaxNode node in tree.GetRoot().DescendantNodes())
                {
                    if (!(node is ClassDeclarationSyntax declaration))
                    {
                        continue;
                    }
                    model = model ?? context.Compilation.GetSemanticModel(tree);
                    INamedTypeSymbol type = model.GetDeclaredSymbol(declaration);
                    if (
                        type == null
                        || !visited.Add(type)
                        || type.Arity != 0
                        || !IsUnityObject(type)
                        || !TypeNaming.IsNameable(type, context.Compilation)
                    )
                    {
                        continue;
                    }
                    List<string> fields = new List<string>();
                    List<string> baseEdits = new List<string>();
                    SortedSet<string> affectedTypes = new SortedSet<string>(StringComparer.Ordinal);
                    string originalTypeExpression =
                        "typeof("
                        + type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                        + ")";
                    affectedTypes.Add(originalTypeExpression);
                    string inheritedTypeExpression = originalTypeExpression;
                    INamedTypeSymbol inheritedBindingType = null;
                    SortedDictionary<string, SyntaxTree> sources = new SortedDictionary<
                        string,
                        SyntaxTree
                    >(StringComparer.Ordinal);
                    CaptureDeclarations(type, sources);
                    for (
                        INamedTypeSymbol owner = type;
                        owner != null && IsUnityObject(owner);
                        owner = owner.BaseType
                    )
                    {
                        if (
                            !SymbolEqualityComparer.Default.Equals(
                                owner.ContainingAssembly,
                                context.Compilation.Assembly
                            )
                        )
                        {
                            if (
                                !IsMigrationRootBase(owner)
                                && (fields.Count != 0 || HasDictionaryFields(owner))
                            )
                            {
                                inheritedBindingType = owner;
                            }
                            break;
                        }
                        CaptureDeclarations(owner, sources);
                        string ownerTypeExpression = TypeNaming.IsNameable(
                            owner,
                            context.Compilation
                        )
                            ? "typeof("
                                + owner.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                                + ")"
                            : inheritedTypeExpression;
                        int baseEditCount = baseEdits.Count;
                        OdinMigrationRewriteMetadata.CaptureBaseEdits(
                            owner,
                            context.Compilation,
                            baseEdits
                        );
                        if (baseEditCount != baseEdits.Count)
                        {
                            affectedTypes.Add(ownerTypeExpression);
                        }
                        foreach (ISymbol member in owner.GetMembers())
                        {
                            if (
                                !(member is IFieldSymbol field)
                                || field.IsStatic
                                || field.IsImplicitlyDeclared
                                || !TryDictionary(
                                    field.Type,
                                    out INamedTypeSymbol key,
                                    out INamedTypeSymbol element
                                )
                                || field.DeclaringSyntaxReferences.Length != 1
                                || !(
                                    field.DeclaringSyntaxReferences[0].GetSyntax()
                                    is VariableDeclaratorSyntax variable
                                )
                                || !(variable.Parent is VariableDeclarationSyntax original)
                            )
                            {
                                continue;
                            }
                            SyntaxTree fieldTree = original.SyntaxTree;
                            affectedTypes.Add(ownerTypeExpression);
                            List<string> edits = OdinMigrationRewriteMetadata.FieldEdits(
                                field,
                                variable,
                                context.Compilation,
                                out string rewriteFailure
                            );
                            sources[fieldTree.FilePath] = fieldTree;
                            CaptureDeclarations(key, sources);
                            for (
                                INamedTypeSymbol objectOwner = element;
                                objectOwner != null;
                                objectOwner = objectOwner.BaseType
                            )
                            {
                                CaptureDeclarations(objectOwner, sources);
                            }
                            fields.Add(
                                "new "
                                    + Field
                                    + "("
                                    + Literal(field.Name)
                                    + ", "
                                    + RuntimeFullNameExpression(key, context.Compilation)
                                    + ", "
                                    + RuntimeFullNameExpression(element, context.Compilation)
                                    + ", "
                                    + AssemblyNameExpression(key, context.Compilation)
                                    + ", "
                                    + AssemblyNameExpression(element, context.Compilation)
                                    + ", "
                                    + Literal(fieldTree.FilePath)
                                    + ", "
                                    + original.Type.Span.Start.ToString(
                                        CultureInfo.InvariantCulture
                                    )
                                    + ", "
                                    + original.Type.Span.Length.ToString(
                                        CultureInfo.InvariantCulture
                                    )
                                    + ", "
                                    + OdinMigrationRewriteMetadata.ArrayExpression(edits)
                                    + ", "
                                    + (rewriteFailure == null ? "null" : Literal(rewriteFailure))
                                    + ", "
                                    + OwnerAccess(
                                        field,
                                        owner,
                                        context.Compilation,
                                        accessors,
                                        restore: false
                                    )
                                    + ", "
                                    + OwnerAccess(
                                        field,
                                        owner,
                                        context.Compilation,
                                        accessors,
                                        restore: true
                                    )
                                    + ")"
                            );
                        }
                        inheritedTypeExpression += ".BaseType";
                    }
                    if (fields.Count == 0 && inheritedBindingType == null)
                    {
                        continue;
                    }
                    StringBuilder sourceArray = new StringBuilder("new " + Source + "[] {");
                    foreach (KeyValuePair<string, SyntaxTree> source in sources)
                    {
                        sourceArray
                            .Append("new ")
                            .Append(Source)
                            .Append('(')
                            .Append(Literal(source.Key))
                            .Append(", ")
                            .Append(Literal(Checksum(source.Value)))
                            .Append("),");
                    }
                    sourceArray.Append('}');
                    string qualified = type.ToDisplayString(
                        SymbolDisplayFormat.FullyQualifiedFormat
                    );
                    string registration =
                        Registry
                        + ".Register(new "
                        + Binding
                        + "(typeof("
                        + qualified
                        + "), "
                        + sourceArray
                        + ", new "
                        + Field
                        + "[] {"
                        + string.Join(",", fields)
                        + "}, "
                        + OdinMigrationRewriteMetadata.ArrayExpression(baseEdits)
                        + ", new global::System.Type[] {";
                    foreach (string affected in affectedTypes)
                    {
                        registration += affected + ",";
                    }
                    registrations[qualified] =
                        registration
                        + "}, "
                        + (
                            inheritedBindingType == null
                                ? "null"
                                : "typeof("
                                    + inheritedBindingType.ToDisplayString(
                                        SymbolDisplayFormat.FullyQualifiedFormat
                                    )
                                    + ")"
                        )
                        + "));";
                }
            }
            if (registrations.Count == 0)
            {
                return;
            }
            Writer writer = new Writer();
            writer.Line("// <auto-generated />");
            writer.Line("#pragma warning disable");
            foreach (string owner in registrations.Keys)
            {
                writer.Line(
                    "[assembly: global::"
                        + BindingNamespace
                        + ".OdinMigrationBindingAttribute(typeof("
                        + owner
                        + "))]"
                );
            }
            writer.Line("namespace WallstopStudios.UnityHelpers.Generated" + Writer.Open);
            writer.Indent();
            writer.Line("internal static class OdinMigrationGeneratedBindings" + Writer.Open);
            writer.Indent();
            writer.Line("[global::UnityEditor.InitializeOnLoadMethod]");
            writer.Line("internal static void Register()" + Writer.Open);
            writer.Indent();
            foreach (string registration in registrations.Values)
            {
                writer.Line(registration);
            }
            writer.Outdent();
            writer.Line("}");
            writer.Outdent();
            writer.Line("}");
            writer.Outdent();
            writer.Line("}");
            foreach (string accessor in accessors.Values)
            {
                writer.Line(accessor);
            }
            context.AddSource(
                "OdinMigrationGeneratedBindings.g.cs",
                SourceText.From(writer.ToString(), Encoding.UTF8)
            );
        }
    }
}
