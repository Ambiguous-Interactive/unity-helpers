// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.SyntaxPolicy
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Reflection.Metadata;
    using System.Reflection.PortableExecutable;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.CSharp.Syntax;
    using Microsoft.CodeAnalysis.Text;

    internal static class Program
    {
        private static readonly string[] SourceRoots =
        {
            "Runtime",
            "Editor",
            "Tests",
            "Generator~",
        };

        private static int Main(string[] arguments)
        {
            try
            {
                if (
                    arguments.Length > 1
                    || (
                        arguments.Length == 1
                        && !string.Equals(arguments[0], "--fix", StringComparison.Ordinal)
                    )
                )
                {
                    Console.Error.WriteLine(
                        "Usage: SyntaxPolicy [--fix], from the repository root."
                    );
                    return 2;
                }

                RunControls();
                RunEquivalenceControls();
                Stopwatch stopwatch = Stopwatch.StartNew();
                bool fix = arguments.Length == 1;
                int files = 0;
                int methods = 0;
                int increments = 0;
                foreach (string sourceRoot in SourceRoots)
                {
                    if (!Directory.Exists(sourceRoot))
                    {
                        throw new InvalidOperationException(
                            "Missing required source root: " + sourceRoot
                        );
                    }
                    int rootFiles = 0;
                    foreach (string path in EnumerateSources(sourceRoot))
                    {
                        ++rootFiles;
                        ++files;
                        string source = File.ReadAllText(path);
                        Dictionary<TextSpan, string> methodEdits = FindEdits(source, true);
                        methods += methodEdits.Count;
                        if (fix && methodEdits.Count > 0)
                        {
                            source = ApplyEdits(source, methodEdits);
                        }
                        Dictionary<TextSpan, string> incrementEdits = FindEdits(source, false);
                        increments += incrementEdits.Count;
                        if (fix && incrementEdits.Count > 0)
                        {
                            source = ApplyEdits(source, incrementEdits);
                        }
                        if (methodEdits.Count + incrementEdits.Count == 0)
                        {
                            continue;
                        }
                        if (fix)
                        {
                            File.WriteAllText(path, source);
                            Console.WriteLine("Fixed " + path);
                        }
                        else
                        {
                            ReportEdits(path, source, methodEdits, "brace-bodied named method");
                            ReportEdits(
                                path,
                                source,
                                incrementEdits,
                                "prefix increment when its old value is unused"
                            );
                        }
                    }
                    if (rootFiles == 0)
                    {
                        throw new InvalidOperationException(
                            "No C# subjects in required source root: " + sourceRoot
                        );
                    }
                }
                Console.WriteLine(
                    $"Syntax policy: {files} files, {methods} arrow methods, {increments} discarded postfix increments; {stopwatch.Elapsed.TotalSeconds:F2}s. All conditional symbol combinations inspected."
                );
                return fix || methods + increments == 0 ? 0 : 1;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine("Syntax policy failed: " + error.Message);
                return 2;
            }
        }

        private static IEnumerable<string> EnumerateSources(string directory)
        {
            foreach (string path in Directory.EnumerateFiles(directory, "*.cs"))
            {
                yield return path;
            }
            foreach (string child in Directory.EnumerateDirectories(directory))
            {
                string name = Path.GetFileName(child);
                if (
                    string.Equals(name, "obj", StringComparison.Ordinal)
                    || string.Equals(name, "bin", StringComparison.Ordinal)
                    || name.StartsWith(".", StringComparison.Ordinal)
                )
                {
                    continue;
                }
                foreach (string path in EnumerateSources(child))
                {
                    yield return path;
                }
            }
        }

        private static Dictionary<TextSpan, string> FindEdits(string source, bool methods)
        {
            List<string> symbols = FindSymbols(source);
            if (symbols.Count > 12)
            {
                throw new InvalidOperationException(
                    "More than twelve conditional symbols require an explicit scan strategy."
                );
            }
            Dictionary<TextSpan, string> edits = new();
            int combinations = 1 << symbols.Count;
            for (int combination = 0; combination < combinations; ++combination)
            {
                List<string> enabled = new();
                for (int index = 0; index < symbols.Count; ++index)
                {
                    if ((combination & (1 << index)) != 0)
                    {
                        enabled.Add(symbols[index]);
                    }
                }
                SyntaxNode root = CSharpSyntaxTree
                    .ParseText(
                        source,
                        new CSharpParseOptions(LanguageVersion.Latest, preprocessorSymbols: enabled)
                    )
                    .GetRoot();
                foreach (Diagnostic diagnostic in root.GetDiagnostics())
                {
                    if (
                        diagnostic.Severity == DiagnosticSeverity.Error
                        && !string.Equals(diagnostic.Id, "CS1029", StringComparison.Ordinal)
                    )
                    {
                        throw new InvalidOperationException(
                            "C# parser cannot certify this source: " + diagnostic
                        );
                    }
                }
                foreach (SyntaxNode node in root.DescendantNodes())
                {
                    if (methods)
                    {
                        ArrowExpressionClauseSyntax arrow = null;
                        TypeSyntax returnType = null;
                        SyntaxTokenList modifiers = default;
                        SyntaxToken semicolon = default;
                        if (node is MethodDeclarationSyntax method)
                        {
                            arrow = method.ExpressionBody;
                            returnType = method.ReturnType;
                            modifiers = method.Modifiers;
                            semicolon = method.SemicolonToken;
                        }
                        else if (node is LocalFunctionStatementSyntax local)
                        {
                            arrow = local.ExpressionBody;
                            returnType = local.ReturnType;
                            modifiers = local.Modifiers;
                            semicolon = local.SemicolonToken;
                        }
                        if (arrow == null)
                        {
                            continue;
                        }
                        if (HasInteriorDirectives(arrow))
                        {
                            throw new InvalidOperationException(
                                "An arrow body contains directives; convert it manually before scanning."
                            );
                        }
                        ExpressionSyntax expression = arrow.Expression.WithLeadingTrivia(
                            arrow.ArrowToken.TrailingTrivia.AddRange(
                                arrow.Expression.GetLeadingTrivia()
                            )
                        );
                        StatementSyntax statement;
                        if (expression is ThrowExpressionSyntax throwing)
                        {
                            statement = SyntaxFactory
                                .ThrowStatement(throwing.Expression)
                                .WithThrowKeyword(throwing.ThrowKeyword)
                                .WithLeadingTrivia(expression.GetLeadingTrivia());
                        }
                        else if (IsVoidReturn(returnType, modifiers))
                        {
                            statement = SyntaxFactory.ExpressionStatement(expression);
                        }
                        else
                        {
                            statement = SyntaxFactory.ReturnStatement(expression);
                        }
                        string block = SyntaxFactory
                            .Block(statement)
                            .NormalizeWhitespace()
                            .ToFullString();
                        edits[TextSpan.FromBounds(arrow.SpanStart, semicolon.Span.End)] = block;
                    }
                    else if (
                        node is PostfixUnaryExpressionSyntax postfix
                        && postfix.IsKind(SyntaxKind.PostIncrementExpression)
                        && IsDiscarded(postfix)
                    )
                    {
                        if (HasInteriorDirectives(postfix))
                        {
                            throw new InvalidOperationException(
                                "A postfix increment contains directives; convert it manually: "
                                    + postfix.ToFullString()
                            );
                        }
                        string prefix =
                            "++"
                            + source.Substring(
                                postfix.Operand.SpanStart,
                                postfix.OperatorToken.SpanStart - postfix.Operand.SpanStart
                            );
                        edits[postfix.Span] = prefix;
                    }
                }
            }
            return edits;
        }

        private static List<string> FindSymbols(string source)
        {
            HashSet<string> names = new(StringComparer.Ordinal);
            SyntaxNode root = CSharpSyntaxTree.ParseText(source).GetRoot();
            foreach (SyntaxTrivia trivia in root.DescendantTrivia(descendIntoTrivia: true))
            {
                ExpressionSyntax condition = null;
                if (trivia.GetStructure() is IfDirectiveTriviaSyntax conditional)
                {
                    condition = conditional.Condition;
                }
                else if (trivia.GetStructure() is ElifDirectiveTriviaSyntax alternative)
                {
                    condition = alternative.Condition;
                }
                if (condition == null)
                {
                    continue;
                }
                foreach (SyntaxNode node in condition.DescendantNodesAndSelf())
                {
                    if (node is IdentifierNameSyntax identifier)
                    {
                        names.Add(identifier.Identifier.ValueText);
                    }
                }
            }
            List<string> symbols = new(names);
            symbols.Sort(StringComparer.Ordinal);
            return symbols;
        }

        private static bool HasInteriorDirectives(SyntaxNode node)
        {
            foreach (SyntaxTrivia trivia in node.DescendantTrivia())
            {
                if (trivia.IsDirective && node.Span.Contains(trivia.Span))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsVoidReturn(TypeSyntax returnType, SyntaxTokenList modifiers)
        {
            if (
                returnType is PredefinedTypeSyntax predefined
                && predefined.Keyword.IsKind(SyntaxKind.VoidKeyword)
            )
            {
                return true;
            }
            bool asynchronous = false;
            foreach (SyntaxToken modifier in modifiers)
            {
                asynchronous |= modifier.IsKind(SyntaxKind.AsyncKeyword);
            }
            if (!asynchronous)
            {
                return false;
            }
            SimpleNameSyntax name = returnType as SimpleNameSyntax;
            if (returnType is QualifiedNameSyntax qualified)
            {
                name = qualified.Right;
            }
            return name is IdentifierNameSyntax
                && (
                    string.Equals(name.Identifier.ValueText, "Task", StringComparison.Ordinal)
                    || string.Equals(
                        name.Identifier.ValueText,
                        "ValueTask",
                        StringComparison.Ordinal
                    )
                );
        }

        private static bool IsDiscarded(PostfixUnaryExpressionSyntax expression)
        {
            SyntaxNode outer = expression;
            while (outer.Parent is ParenthesizedExpressionSyntax)
            {
                outer = outer.Parent;
            }
            if (outer.Parent is ExpressionStatementSyntax)
            {
                return true;
            }
            if (outer.Parent is ForStatementSyntax loop)
            {
                foreach (ExpressionSyntax initializer in loop.Initializers)
                {
                    if (initializer == outer)
                    {
                        return true;
                    }
                }
                foreach (ExpressionSyntax incrementor in loop.Incrementors)
                {
                    if (incrementor == outer)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static string ApplyEdits(string source, Dictionary<TextSpan, string> edits)
        {
            List<TextChange> changes = new();
            foreach (KeyValuePair<TextSpan, string> edit in edits)
            {
                changes.Add(new TextChange(edit.Key, edit.Value));
            }
            return SourceText.From(source).WithChanges(changes).ToString();
        }

        private static void ReportEdits(
            string path,
            string source,
            Dictionary<TextSpan, string> edits,
            string policy
        )
        {
            SourceText text = SourceText.From(source);
            foreach (TextSpan span in edits.Keys)
            {
                int line = text.Lines.GetLineFromPosition(span.Start).LineNumber + 1;
                Console.Error.WriteLine($"{path}:{line}: Require {policy}.");
            }
        }

        private static void RunEquivalenceControls()
        {
            string[] cases =
            {
                "public static class A { public static int M() { int i = 0; for (; i < 4; i++) {} i++; return i; } }",
                "public static class A { public static int M() { int i = 0; for (i++; i < 4; i++) {} return i; } }",
                "public static class A { public static int M(int i) => i++; }",
                "public static class A { public static void M() => N(); private static void N() {} }",
                "public static class A { public static int M() => throw /* keep */ new System.InvalidOperationException(); }",
                "public static class A { private static int x; public static ref int M() => ref x; }",
                "public static class A { public static async System.Threading.Tasks.Task M() => await System.Threading.Tasks.Task.Delay(1); }",
                "public static class A { public static async System.Threading.Tasks.Task<int> M() => await System.Threading.Tasks.Task.FromResult(1); }",
                "public static class A { public static int M() { int N(int i) => i + 1; return N(2); } }",
                "public static class A { public static int M() { int i = 0; i /* keep */ ++; return i; } }",
                "public static class A { public static int M() /* before */ => /* after */ 1 /* end */ ; }",
                "public struct A { public int Value; public static A operator ++(A a) { a.Value += 2; return a; } public static int M() { A a = default; a++; return a.Value; } }",
            };
            string platformAssemblies = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
            List<MetadataReference> references = new();
            foreach (string path in platformAssemblies.Split(Path.PathSeparator))
            {
                references.Add(MetadataReference.CreateFromFile(path));
            }
            foreach (string source in cases)
            {
                string fixedSource = ApplyEdits(source, FindEdits(source, true));
                fixedSource = ApplyEdits(fixedSource, FindEdits(fixedSource, false));
                List<byte[]> originalBodies = CompileBodies(source, references);
                List<byte[]> fixedBodies = CompileBodies(fixedSource, references);
                if (originalBodies.Count != fixedBodies.Count)
                {
                    throw new InvalidOperationException(
                        "Migration changed the emitted method count."
                    );
                }
                for (int index = 0; index < originalBodies.Count; ++index)
                {
                    if (!originalBodies[index].AsSpan().SequenceEqual(fixedBodies[index]))
                    {
                        throw new InvalidOperationException(
                            "Migration changed emitted control IL: " + source
                        );
                    }
                }
                foreach (
                    string marker in new[]
                    {
                        "/* before */",
                        "/* after */",
                        "/* end */",
                        "/* keep */",
                    }
                )
                {
                    if (
                        source.Contains(marker, StringComparison.Ordinal)
                        && !fixedSource.Contains(marker, StringComparison.Ordinal)
                    )
                    {
                        throw new InvalidOperationException("Migration lost a comment: " + marker);
                    }
                }
            }
            Console.WriteLine($"Compiled method-body equivalence controls passed: {cases.Length}.");
        }

        private static List<byte[]> CompileBodies(
            string source,
            IReadOnlyList<MetadataReference> references
        )
        {
            CSharpCompilation compilation = CSharpCompilation.Create(
                "MigrationControl",
                new[] { CSharpSyntaxTree.ParseText(source) },
                references,
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    optimizationLevel: OptimizationLevel.Release
                )
            );
            using MemoryStream output = new();
            Microsoft.CodeAnalysis.Emit.EmitResult result = compilation.Emit(output);
            if (!result.Success)
            {
                throw new InvalidOperationException(
                    "Migration control did not compile: " + string.Join("; ", result.Diagnostics)
                );
            }
            output.Position = 0;
            using PEReader reader = new(output);
            MetadataReader metadata = reader.GetMetadataReader();
            List<byte[]> bodies = new();
            foreach (MethodDefinitionHandle handle in metadata.MethodDefinitions)
            {
                MethodDefinition method = metadata.GetMethodDefinition(handle);
                if (method.RelativeVirtualAddress != 0)
                {
                    bodies.Add(reader.GetMethodBody(method.RelativeVirtualAddress).GetILBytes());
                }
            }
            return bodies;
        }

        private static void RunControls()
        {
            (string source, int methods, int increments)[] cases =
            {
                ("class A { void M() { int i = 0; i++; for (; i < 4; i++) {} } }", 0, 2),
                (
                    "class A { int M(int i) { return i++; } void N(int i) { int x = i++; N(i++); } }",
                    0,
                    0
                ),
                ("class A { void M() { int i = 0; for (i++; i < 4; i++) {} } }", 0, 2),
                (
                    "class A { A() => N(); static void N() {} public static A operator +(A a, A b) => a; }",
                    0,
                    0
                ),
                ("class A { int M() => 1; void N() => M(); }", 2, 0),
                ("class A { void M() { int N() => 1; } }", 1, 0),
                (
                    "class A { int P => 1; int Q { get => 1; } System.Func<int,int> F = x => x++; }",
                    0,
                    0
                ),
                ("class A { string M() { return \"i++; void M() => 1;\"; } /* i++; */ }", 0, 0),
                ("class A { int\nM(\nint x\n)\n=> x; }", 1, 0),
                ("class A { void M() { int i = 0; for (; i < 4; (i++), ++i) {} } }", 0, 1),
                (
                    "#if A\nclass X { void M() { int i = 0; i++; } }\n#elif B\nclass X { int M() => 2; }\n#else\nclass X { int M() => 3; }\n#endif",
                    2,
                    1
                ),
                ("#if A\n#if B\nclass X { int M() => 1; }\n#endif\n#endif", 1, 0),
            };
            foreach ((string source, int methods, int increments) control in cases)
            {
                Dictionary<TextSpan, string> methodEdits = FindEdits(control.source, true);
                Dictionary<TextSpan, string> incrementEdits = FindEdits(control.source, false);
                if (
                    methodEdits.Count != control.methods
                    || incrementEdits.Count != control.increments
                )
                {
                    throw new InvalidOperationException(
                        "A syntax reporting control failed: " + control.source
                    );
                }
                string fixedSource = ApplyEdits(control.source, methodEdits);
                fixedSource = ApplyEdits(fixedSource, FindEdits(fixedSource, false));
                if (
                    FindEdits(fixedSource, true).Count != 0
                    || FindEdits(fixedSource, false).Count != 0
                )
                {
                    throw new InvalidOperationException("A syntax migration control failed.");
                }
            }
            Console.WriteLine($"Syntax reporting and migration controls passed: {cases.Length}.");
        }
    }
}
