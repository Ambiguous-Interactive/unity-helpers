// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Analyzers
{
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.CSharp.Syntax;
    using Microsoft.CodeAnalysis.Diagnostics;

    /// <summary>Reports member ordering and nested-type placement when explicitly enabled.</summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class MemberOrderAnalyzer : DiagnosticAnalyzer
    {
        private const int AccessibilityStride = 8;

        /// <inheritdoc />
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(UnityHelpersDiagnostics.MemberDeclarationsOutOfOrder);

        private static void AnalyzeType(SyntaxNodeAnalysisContext context)
        {
            TypeDeclarationSyntax declaration = (TypeDeclarationSyntax)context.Node;
            List<DirectiveTriviaSyntax> directives = new List<DirectiveTriviaSyntax>();
            foreach (SyntaxTrivia trivia in declaration.DescendantTrivia(descendIntoTrivia: true))
            {
                if (trivia.IsDirective && trivia.GetStructure() is DirectiveTriviaSyntax directive)
                {
                    directives.Add(directive);
                }
            }
            Stack<int> regions = new Stack<int>();
            int directiveIndex = 0;
            int previousRegion = 0;
            int previousRank = -1;
            MemberDeclarationSyntax previous = null;
            bool nestedTypeSeen = false;
            foreach (MemberDeclarationSyntax member in declaration.Members)
            {
                while (
                    directiveIndex < directives.Count
                    && directives[directiveIndex].SpanStart < member.SpanStart
                )
                {
                    DirectiveTriviaSyntax directive = directives[directiveIndex++];
                    if (directive is IfDirectiveTriviaSyntax)
                    {
                        regions.Push(directive.SpanStart + 1);
                    }
                    else if (
                        directive is ElseDirectiveTriviaSyntax
                        || directive is ElifDirectiveTriviaSyntax
                    )
                    {
                        if (0 < regions.Count)
                        {
                            regions.Pop();
                        }
                        regions.Push(directive.SpanStart + 1);
                    }
                    else if (directive is EndIfDirectiveTriviaSyntax && 0 < regions.Count)
                    {
                        regions.Pop();
                    }
                }
                int region = 0 < regions.Count ? regions.Peek() : 0;
                if (region != previousRegion)
                {
                    previousRank = -1;
                    previous = null;
                    previousRegion = region;
                }
                if (member is BaseTypeDeclarationSyntax)
                {
                    nestedTypeSeen = true;
                    continue;
                }
                int rank = RankOf(member);
                if (rank < 0)
                {
                    continue;
                }
                if (nestedTypeSeen || (0 <= previousRank && rank < previousRank))
                {
                    string after = nestedTypeSeen ? "a nested type" : NameOf(previous);
                    context.ReportDiagnostic(
                        Diagnostic.Create(
                            UnityHelpersDiagnostics.MemberDeclarationsOutOfOrder,
                            member.GetFirstToken().GetLocation(),
                            NameOf(member),
                            declaration.Identifier.ValueText,
                            after
                        )
                    );
                }
                previousRank = rank;
                previous = member;
            }
        }

        private static int RankOf(MemberDeclarationSyntax member)
        {
            SyntaxTokenList modifiers;
            int tier;
            if (member is FieldDeclarationSyntax field)
            {
                modifiers = field.Modifiers;
                tier =
                    modifiers.Any(SyntaxKind.ConstKeyword) ? 0
                    : modifiers.Any(SyntaxKind.StaticKeyword) ? 4
                    : 6;
            }
            else if (member is BaseFieldDeclarationSyntax eventField)
            {
                modifiers = eventField.Modifiers;
                tier = 1;
            }
            else if (member is DelegateDeclarationSyntax callback)
            {
                modifiers = callback.Modifiers;
                tier = 2;
            }
            else if (member is BasePropertyDeclarationSyntax property)
            {
                modifiers = property.Modifiers;
                tier =
                    property is EventDeclarationSyntax ? 1
                    : modifiers.Any(SyntaxKind.StaticKeyword) ? 3
                    : 5;
            }
            else if (member is BaseMethodDeclarationSyntax method)
            {
                modifiers = method.Modifiers;
                tier =
                    method is ConstructorDeclarationSyntax || method is DestructorDeclarationSyntax
                        ? 7
                    : modifiers.Any(SyntaxKind.StaticKeyword) ? 8
                    : 9;
            }
            else
            {
                return -1;
            }
            int access =
                modifiers.Any(SyntaxKind.PublicKeyword) ? 0
                : modifiers.Any(SyntaxKind.ProtectedKeyword) ? 1
                : modifiers.Any(SyntaxKind.InternalKeyword) ? 2
                : modifiers.Any(SyntaxKind.PrivateKeyword) ? 3
                : 4;
            return tier * AccessibilityStride + access;
        }

        private static string NameOf(MemberDeclarationSyntax member)
        {
            switch (member)
            {
                case BaseFieldDeclarationSyntax field when field.Declaration.Variables.Count != 0:
                    return field.Declaration.Variables[0].Identifier.ValueText;
                case MethodDeclarationSyntax method:
                    return method.Identifier.ValueText;
                case PropertyDeclarationSyntax property:
                    return property.Identifier.ValueText;
                case EventDeclarationSyntax eventMember:
                    return eventMember.Identifier.ValueText;
                case DelegateDeclarationSyntax callback:
                    return callback.Identifier.ValueText;
                case ConstructorDeclarationSyntax constructor:
                    return constructor.Identifier.ValueText;
                case DestructorDeclarationSyntax destructor:
                    return destructor.Identifier.ValueText;
                default:
                    return member.Kind().ToString();
            }
        }

        /// <inheritdoc />
        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSyntaxNodeAction(
                AnalyzeType,
                SyntaxKind.ClassDeclaration,
                SyntaxKind.StructDeclaration,
                SyntaxKind.InterfaceDeclaration,
                SyntaxKind.RecordDeclaration
            );
        }
    }
}
