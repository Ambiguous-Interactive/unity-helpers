// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator
{
    using System.Collections.Generic;
    using System.Text;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.Text;

    public sealed partial class WProtoGenerator
    {
        private static List<string> EmitReplacements(
            GeneratorExecutionContext context,
            SubtypeMap subtypes,
            HashSet<INamedTypeSymbol> emittedContracts
        )
        {
            List<string> registrations = new List<string>();
            List<INamedTypeSymbol> bases = new List<INamedTypeSymbol>(subtypes.Bases);
            bases.Sort(
                (left, right) =>
                    string.CompareOrdinal(left.ToDisplayString(), right.ToDisplayString())
            );
            foreach (INamedTypeSymbol contract in bases)
            {
                if (
                    SymbolEqualityComparer.Default.Equals(
                        contract.ContainingAssembly,
                        context.Compilation.Assembly
                    )
                )
                {
                    continue;
                }
                bool allEmitted = true;
                foreach (Include declaration in subtypes.For(contract))
                {
                    allEmitted &= emittedContracts.Contains(declaration.SubType);
                }
                if (!allEmitted || !SubtypeMap.SupportsReplacement(contract))
                {
                    continue;
                }
                List<Include> includes = new List<Include>();
                Dictionary<int, string> spent = new Dictionary<int, string>();
                foreach (
                    AttributeData field in contract
                        .GetTypeMembers("WProtoFormatter")[0]
                        .GetAttributes()
                )
                {
                    if (
                        field.AttributeClass?.ToDisplayString()
                            != AttributeNamespace + ".WProtoDispatchFieldAttribute"
                        || field.ConstructorArguments.Length != 4
                        || !SymbolEqualityComparer.Default.Equals(
                            field.ConstructorArguments[0].Value as INamedTypeSymbol,
                            contract
                        )
                    )
                    {
                        continue;
                    }
                    int tag = (int)field.ConstructorArguments[1].Value;
                    spent[tag] = (string)field.ConstructorArguments[2].Value;
                    if (field.ConstructorArguments[3].Value is INamedTypeSymbol subtype)
                    {
                        includes.Add(new Include(tag, subtype));
                    }
                }
                bool failed = false;
                foreach (
                    IAssemblySymbol reference in context
                        .Compilation
                        .SourceModule
                        .ReferencedAssemblySymbols
                )
                {
                    foreach (AttributeData attribute in reference.GetAttributes())
                    {
                        if (
                            attribute.AttributeClass?.ToDisplayString()
                                == AttributeNamespace + ".WProtoReplacementAttribute"
                            && attribute.ConstructorArguments.Length == 1
                            && SymbolEqualityComparer.Default.Equals(
                                attribute.ConstructorArguments[0].Value as INamedTypeSymbol,
                                contract
                            )
                        )
                        {
                            foreach (Include declaration in subtypes.For(contract))
                            {
                                ReportReplacementProblem(
                                    context,
                                    declaration,
                                    "assembly '"
                                        + reference.Name
                                        + "' already replaces '"
                                        + contract.ToDisplayString()
                                        + "'; all extensions of one base must belong to one assembly"
                                );
                            }
                            failed = true;
                        }
                    }
                }
                ReservedMap reserved = ReservedMap.Build(contract);
                foreach (Include declaration in subtypes.For(contract))
                {
                    string problem = null;
                    if (spent.TryGetValue(declaration.Tag, out string owner))
                    {
                        problem =
                            "field number "
                            + declaration.Tag
                            + " on '"
                            + contract.ToDisplayString()
                            + "' is already held by '"
                            + owner
                            + "'";
                    }
                    else if (reserved.ReservesNumber(declaration.Tag))
                    {
                        problem = ReservedMap.ReservedProblem(declaration.Tag, contract.Name);
                    }
                    else
                    {
                        foreach (
                            AttributeData attribute in contract.ContainingAssembly.GetAttributes()
                        )
                        {
                            if (
                                (
                                    attribute.AttributeClass?.ToDisplayString()
                                        == SubtypeTagManifest.RetiredAttribute
                                    || attribute.AttributeClass?.ToDisplayString()
                                        == SubtypeTagManifest.TagAttribute
                                )
                                && attribute.ConstructorArguments.Length == 3
                                && SymbolEqualityComparer.Default.Equals(
                                    attribute.ConstructorArguments[1].Value as INamedTypeSymbol,
                                    contract
                                )
                                && (int)attribute.ConstructorArguments[2].Value == declaration.Tag
                            )
                            {
                                problem =
                                    "field number "
                                    + declaration.Tag
                                    + " is held by upstream manifest entry '"
                                    + attribute.ConstructorArguments[0].Value
                                    + "'";
                                break;
                            }
                        }
                    }
                    if (problem != null)
                    {
                        ReportReplacementProblem(context, declaration, problem);
                        failed = true;
                        continue;
                    }
                    spent.Add(declaration.Tag, declaration.Qualified);
                    includes.Add(declaration);
                }
                foreach (Include include in includes)
                {
                    if (
                        !context.Compilation.IsSymbolAccessibleWithin(
                            include.SubType,
                            context.Compilation.Assembly
                        )
                    )
                    {
                        foreach (Include declaration in subtypes.For(contract))
                        {
                            ReportReplacementProblem(
                                context,
                                declaration,
                                "upstream subtype '"
                                    + include.Qualified
                                    + "' is inaccessible from this assembly; a complete static chain must name every subtype"
                            );
                        }
                        failed = true;
                    }
                }
                if (failed || !SubtypeMap.SupportsReplacement(contract))
                {
                    continue;
                }
                includes.Sort((left, right) => left.Tag.CompareTo(right.Tag));
                string name = "WProtoReplacement" + registrations.Count;
                string qualified = contract.ToDisplayString(
                    SymbolDisplayFormat.FullyQualifiedFormat
                );
                Writer writer = new Writer();
                writer.Line("// <auto-generated />");
                writer.Line("#pragma warning disable");
                writer.Line(
                    "[assembly: " + Proto + ".WProtoReplacement(typeof(" + qualified + "))]"
                );
                writer.Line("namespace WallstopStudios.UnityHelpers.Generated" + Writer.Open);
                writer.Indent();
                writer.Line("#if UNITY_5_3_OR_NEWER");
                writer.Line("[global::UnityEngine.Scripting.Preserve]");
                writer.Line("#endif");
                writer.Line(
                    "internal sealed class "
                        + name
                        + " : "
                        + Proto
                        + ".IWProtoReplacementFormatter<"
                        + qualified
                        + ">, "
                        + Proto
                        + ".IWProtoMergeFormatter<"
                        + qualified
                        + ">"
                        + Writer.Open
                );
                writer.Indent();
                writer.Line("internal static readonly " + name + " Instance = new " + name + "();");
                EmitCanWrite(writer, qualified, includes, false);
                writer.Line(
                    "public int Measure(in "
                        + qualified
                        + " value) => "
                        + qualified
                        + ".WProtoFormatter.Instance.MeasureWithSubtypes(value, default(Dispatch));"
                );
                writer.Line(
                    "public bool Write(ref "
                        + Proto
                        + ".WProtoWriter writer, in "
                        + qualified
                        + " value) => "
                        + qualified
                        + ".WProtoFormatter.Instance.WriteWithSubtypes(ref writer, value, default(Dispatch));"
                );
                writer.Line(
                    "public bool TryRead(ref "
                        + Proto
                        + ".WProtoReader reader, out "
                        + qualified
                        + " value) => "
                        + qualified
                        + ".WProtoFormatter.Instance.TryReadWithSubtypes(ref reader, out value, default(Dispatch));"
                );
                writer.Line(
                    "public bool TryReadInto(ref "
                        + Proto
                        + ".WProtoReader reader, in "
                        + qualified
                        + " seed, out "
                        + qualified
                        + " value) => "
                        + qualified
                        + ".WProtoFormatter.Instance.TryReadWithSubtypes(ref reader, out value, new Dispatch(seed), seed);"
                );
                writer.Line(
                    "private struct Dispatch : "
                        + Proto
                        + ".IWProtoSubtypeDispatch<"
                        + qualified
                        + ">"
                        + Writer.Open
                );
                writer.Indent();
                writer.Line("private " + qualified + " seed;");
                writer.Line("internal Dispatch(" + qualified + " seed) { this.seed = seed; }");

                writer.Line("public int MeasureSubtype(in " + qualified + " value)" + Writer.Open);
                writer.Indent();
                writer.Line("int size = 0;");
                EmitIncludeDispatch(
                    writer,
                    contract,
                    qualified,
                    includes,
                    include =>
                        "size += "
                        + Proto
                        + ".WProtoSizes.TagSize("
                        + include.Tag
                        + ") + "
                        + Proto
                        + ".WProtoSizes.MessageSize("
                        + include.Formatter
                        + ", "
                        + include.Local
                        + ");"
                );
                writer.Line("return size;");
                writer.Outdent();
                writer.Line("}");
                writer.Line(
                    "public bool WriteSubtype(ref "
                        + Proto
                        + ".WProtoWriter writer, in "
                        + qualified
                        + " value)"
                        + Writer.Open
                );
                writer.Indent();
                EmitIncludeDispatch(
                    writer,
                    contract,
                    qualified,
                    includes,
                    include =>
                        "if (!writer.TryWriteMessage("
                        + include.Tag
                        + ", "
                        + include.Formatter
                        + ", "
                        + include.Local
                        + "))"
                );
                writer.Line("return true;");
                writer.Outdent();
                writer.Line("}");
                writer.Line(
                    "public bool TryReadSubtype(ref "
                        + Proto
                        + ".WProtoReader reader, int fieldNumber, int wireType, out "
                        + qualified
                        + " value, out bool handled)"
                        + Writer.Open
                );
                writer.Indent();
                writer.Line("handled = true;");
                writer.Line("switch (fieldNumber)" + Writer.Open);
                writer.Indent();
                foreach (Include include in includes)
                {
                    writer.Line(
                        "case "
                            + include.Tag
                            + " when wireType == "
                            + Proto
                            + ".WProtoWireType.LengthDelimited:"
                    );
                    writer.Indent();
                    writer.Line(
                        "if (!reader.TryReadBytes(out System.ReadOnlySpan<byte> payload"
                            + include.Tag
                            + ")) { value = default; return false; }"
                    );
                    writer.Line(
                        "bool success"
                            + include.Tag
                            + " = reader.TryReadMessage(payload"
                            + include.Tag
                            + ", "
                            + include.Formatter
                            + ", seed as "
                            + include.Qualified
                            + ", out "
                            + include.Qualified
                            + " "
                            + include.Local
                            + ");"
                    );
                    writer.Line("seed = " + include.Local + ";");
                    writer.Line("value = " + include.Local + ";");
                    writer.Line("return success" + include.Tag + ";");
                    writer.Outdent();
                }
                writer.Line("default: handled = false; value = default; return true;");
                writer.Outdent();
                writer.Line("}");
                writer.Outdent();
                writer.Line("}");
                writer.Outdent();
                writer.Line("}");
                writer.Outdent();
                writer.Line("}");
                writer.Outdent();
                writer.Line("}");
                context.AddSource(
                    name + ".g.cs",
                    SourceText.From(writer.ToString(), Encoding.UTF8)
                );
                registrations.Add(
                    "global::WallstopStudios.UnityHelpers.Generated." + name + ".Instance"
                );
            }
            return registrations;
        }

        private static void ReportReplacementProblem(
            GeneratorExecutionContext context,
            Include declaration,
            string problem
        )
        {
            context.ReportDiagnostic(
                Diagnostic.Create(
                    WProtoDiagnostics.BadSubtype,
                    declaration.SubType.Locations.Length == 0
                        ? Location.None
                        : declaration.SubType.Locations[0],
                    declaration.SubType.Name,
                    SubtypeMap.Written(
                        declaration.SubType.BaseType,
                        declaration.Tag,
                        declaration.TagFromManifest
                    ),
                    problem
                )
            );
        }
    }
}
