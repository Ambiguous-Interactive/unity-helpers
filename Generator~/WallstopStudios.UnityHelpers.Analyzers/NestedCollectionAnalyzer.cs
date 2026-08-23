// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Analyzers
{
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.Diagnostics;

    /// <summary>
    /// Reports a Unity-serialized field that resolves onto a collection of collections, which Unity
    /// drops in full and without a message.
    /// </summary>
    /// <remarks>
    /// The nesting is deliberately not matched syntactically. Almost none of the declarations that
    /// hit this look nested: <c>SerializableDictionary&lt;string, List&lt;Foo&gt;&gt;</c> is one
    /// collection on the page and becomes <c>List&lt;Foo&gt;[]</c> only after its backing
    /// <c>TValueCache[]</c> is substituted, two base classes up. So the walk asks the symbol what
    /// Unity will actually serialize -- the serialized instance fields of the field's own type, and
    /// of theirs -- which covers every adapter this package ships, every adapter it ever adds, and a
    /// consumer's own wrapper, with no table to keep in sync (#548).
    /// </remarks>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class NestedCollectionAnalyzer : DiagnosticAnalyzer
    {
        /// <summary>
        /// Unity refuses to serialize past seven levels of nesting for a non-<c>UnityEngine.Object</c>
        /// type, so a walk deeper than that is describing something Unity already discarded.
        /// </summary>
        private const int MaxSerializationDepth = 8;

        private const string ListMetadataName = "System.Collections.Generic.List`1";
        private const string SerializeFieldAttribute = "UnityEngine.SerializeField";
        private const string NonSerializedAttribute = "System.NonSerializedAttribute";
        private const string SerializableAttribute = "System.SerializableAttribute";
        private const string UnityObject = "UnityEngine.Object";

        /// <inheritdoc />
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(UnityHelpersDiagnostics.NestedCollectionIsNotSerialized);

        /// <inheritdoc />
        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSymbolAction(AnalyzeField, SymbolKind.Field);
        }

        private static void AnalyzeField(SymbolAnalysisContext context)
        {
            IFieldSymbol field = (IFieldSymbol)context.Symbol;
            if (!IsUnitySerializedField(field))
            {
                return;
            }

            HashSet<ITypeSymbol> visiting = new HashSet<ITypeSymbol>(
                SymbolEqualityComparer.Default
            );
            if (
                !TryFindNesting(
                    field.Type,
                    visiting,
                    0,
                    out ITypeSymbol outerSequence,
                    out ITypeSymbol innerSequence
                )
            )
            {
                return;
            }

            foreach (Location location in field.Locations)
            {
                if (!location.IsInSource)
                {
                    continue;
                }

                context.ReportDiagnostic(
                    Diagnostic.Create(
                        UnityHelpersDiagnostics.NestedCollectionIsNotSerialized,
                        location,
                        field.Name,
                        outerSequence.ToDisplayString(),
                        innerSequence.ToDisplayString()
                    )
                );
            }
        }

        /// <summary>
        /// Whether Unity will attempt to serialize this field at all.
        /// </summary>
        /// <remarks>
        /// An explicit <c>[SerializeField]</c> is decisive wherever it appears. A public field is
        /// only decisive on a type Unity itself instantiates, because a public field on a plain
        /// class may never reach Unity's serializer -- and a diagnostic that fires on an ordinary
        /// algorithm's <c>public List&lt;List&lt;int&gt;&gt; grid</c> would be simply wrong. That
        /// gate trades a false positive for a false negative, which is the correct direction for a
        /// rule that is on by default.
        /// </remarks>
        private static bool IsUnitySerializedField(IFieldSymbol field)
        {
            if (field.IsStatic || field.IsConst || field.IsImplicitlyDeclared)
            {
                return false;
            }

            bool hasSerializeField = false;
            foreach (AttributeData attribute in field.GetAttributes())
            {
                string name = attribute.AttributeClass?.ToDisplayString();
                if (name == NonSerializedAttribute)
                {
                    return false;
                }

                if (name == SerializeFieldAttribute)
                {
                    hasSerializeField = true;
                }
            }

            if (hasSerializeField)
            {
                return true;
            }

            return field.DeclaredAccessibility == Accessibility.Public
                && DerivesFromUnityObject(field.ContainingType);
        }

        /// <summary>
        /// Finds the first place where a collection's elements are themselves a collection.
        /// </summary>
        private static bool TryFindNesting(
            ITypeSymbol type,
            HashSet<ITypeSymbol> visiting,
            int depth,
            out ITypeSymbol outerSequence,
            out ITypeSymbol innerSequence
        )
        {
            outerSequence = null;
            innerSequence = null;
            if (type == null || depth >= MaxSerializationDepth)
            {
                return false;
            }

            ITypeSymbol element = SequenceElement(type);
            if (element != null)
            {
                if (SequenceElement(element) != null)
                {
                    outerSequence = type;
                    innerSequence = element;
                    return true;
                }

                return TryFindNesting(
                    element,
                    visiting,
                    depth + 1,
                    out outerSequence,
                    out innerSequence
                );
            }

            if (!(type is INamedTypeSymbol named) || !IsWalkableSerializableType(named))
            {
                return false;
            }

            if (!visiting.Add(named))
            {
                return false;
            }

            try
            {
                for (
                    INamedTypeSymbol current = named;
                    current != null && !IsFrameworkType(current);
                    current = current.BaseType
                )
                {
                    foreach (ISymbol member in current.GetMembers())
                    {
                        if (
                            !(member is IFieldSymbol candidate)
                            || !IsUnitySerializedField(candidate)
                        )
                        {
                            continue;
                        }

                        if (
                            TryFindNesting(
                                candidate.Type,
                                visiting,
                                depth + 1,
                                out outerSequence,
                                out innerSequence
                            )
                        )
                        {
                            return true;
                        }
                    }
                }
            }
            finally
            {
                visiting.Remove(named);
            }

            return false;
        }

        /// <summary>
        /// The element type when <paramref name="type"/> is a shape Unity flattens into a repeated
        /// field: a single-dimension array, or a <see cref="List{T}"/>.
        /// </summary>
        /// <remarks>
        /// A multi-dimensional array is excluded on purpose. Unity does not serialize one at any
        /// nesting, so reporting it here would attribute the loss to the wrong cause, and
        /// <c>int[,]</c> has no inner collection to wrap.
        /// </remarks>
        private static ITypeSymbol SequenceElement(ITypeSymbol type)
        {
            if (type is IArrayTypeSymbol array)
            {
                return array.Rank == 1 ? array.ElementType : null;
            }

            if (
                type is INamedTypeSymbol named
                && named.IsGenericType
                && named.TypeArguments.Length == 1
                && FullMetadataName(named) == ListMetadataName
            )
            {
                return named.TypeArguments[0];
            }

            return null;
        }

        /// <summary>
        /// Whether Unity will inline this type's own fields into the containing asset.
        /// </summary>
        /// <summary>
        /// Namespace-qualified metadata name, so arity is part of the match and a consumer type
        /// that happens to be called <c>List</c> is not mistaken for the BCL one.
        /// </summary>
        private static string FullMetadataName(INamedTypeSymbol type)
        {
            INamespaceSymbol containing = type.ContainingNamespace;
            if (containing == null || containing.IsGlobalNamespace)
            {
                return type.MetadataName;
            }

            return containing.ToDisplayString() + "." + type.MetadataName;
        }

        private static bool IsWalkableSerializableType(INamedTypeSymbol type)
        {
            if (
                type.TypeKind != TypeKind.Class && type.TypeKind != TypeKind.Struct
                || type.SpecialType != SpecialType.None
                || IsFrameworkType(type)
            )
            {
                return false;
            }

            // A UnityEngine.Object field is a reference to a separate asset, so its own fields are
            // serialized over there rather than inlined here.
            if (DerivesFromUnityObject(type))
            {
                return false;
            }

            foreach (AttributeData attribute in type.GetAttributes())
            {
                if (attribute.AttributeClass?.ToDisplayString() == SerializableAttribute)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool DerivesFromUnityObject(INamedTypeSymbol type)
        {
            for (INamedTypeSymbol current = type; current != null; current = current.BaseType)
            {
                if (current.ToDisplayString() == UnityObject)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsFrameworkType(INamedTypeSymbol type)
        {
            string ns = type.ContainingNamespace?.ToDisplayString();
            if (string.IsNullOrEmpty(ns))
            {
                return false;
            }

            return ns == "System"
                || ns.StartsWith("System.", System.StringComparison.Ordinal)
                || ns == "Microsoft"
                || ns.StartsWith("Microsoft.", System.StringComparison.Ordinal);
        }
    }
}
