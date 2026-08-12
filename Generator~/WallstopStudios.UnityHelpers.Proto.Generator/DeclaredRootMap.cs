// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator
{
    using System;
    using System.Collections.Generic;
    using Microsoft.CodeAnalysis;

    /// <summary>
    /// The declared-root pairs an assembly declares, and the registrations they produce.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Built from <c>[assembly: WProtoDeclaredRoot(declared, root)]</c> on the compilation's OWN
    /// assembly, which is where it differs from <see cref="MarshalMap"/> and
    /// <see cref="SurrogateMap"/>. Those two are re-read from every reference because a marshal has
    /// to be closed over a consumer's element type and a surrogate has to be substituted while a
    /// consumer's members are emitted. A declared root closes nothing and substitutes nothing: both
    /// types are already closed, so the assembly that declares the pair registers it once and every
    /// other assembly in the process sees that registration. Referenced assemblies are inspected
    /// only to warn when this assembly declares a different root for the same type; their
    /// registrations are never emitted again.
    /// </para>
    /// <para>
    /// The registration names no formatter. <see cref="Register"/> resolves the root's formatter at
    /// call time through the provider, so a root whose assembly was compiled without this generator
    /// declines at run time rather than failing a build with a name that is not there.
    /// </para>
    /// </remarks>
    internal static class DeclaredRootMap
    {
        private const string DeclaredRootAttribute =
            "WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto.WProtoDeclaredRootAttribute";

        private const string ContractAttribute =
            "WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto.WProtoContractAttribute";

        /// <summary>
        /// Reports every pair declared by this compilation that cannot work.
        /// </summary>
        /// <param name="compilation">The compilation being generated for.</param>
        /// <param name="report">Receives one diagnostic per unusable pair.</param>
        internal static void Validate(Compilation compilation, Action<Diagnostic> report)
        {
            HashSet<ITypeSymbol> seen = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
            Dictionary<ITypeSymbol, List<ReferencedDeclaration>> referenced =
                ReferencedDeclarations(compilation, report);

            foreach (Pair pair in Pairs(compilation.Assembly))
            {
                Location location =
                    pair.Attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation()
                    ?? Location.None;

                if (pair.Declared == null || pair.Root == null)
                {
                    // `typeof()` cannot be written, but `null` can, and dropping the pair here left
                    // an attribute that neither registered nor reported anything.
                    report(
                        Diagnostic.Create(
                            WProtoDiagnostics.DeclaredRootNotAssignable,
                            location,
                            pair.Declared?.ToDisplayString() ?? "<missing>",
                            pair.Root?.ToDisplayString() ?? "<missing>"
                        )
                    );
                    continue;
                }

                if (!seen.Add(pair.Declared))
                {
                    report(
                        Diagnostic.Create(
                            WProtoDiagnostics.DuplicateDeclaredRoot,
                            location,
                            pair.Declared.ToDisplayString(),
                            pair.Root.ToDisplayString()
                        )
                    );
                    continue;
                }

                if (Open(pair.Declared) || Open(pair.Root))
                {
                    report(
                        Diagnostic.Create(
                            WProtoDiagnostics.GenericDeclaredRoot,
                            location,
                            pair.Declared.ToDisplayString(),
                            pair.Root.ToDisplayString()
                        )
                    );
                    continue;
                }

                if (SymbolEqualityComparer.Default.Equals(pair.Declared, pair.Root))
                {
                    report(
                        Diagnostic.Create(
                            WProtoDiagnostics.SelfDeclaredRoot,
                            location,
                            pair.Declared.ToDisplayString()
                        )
                    );
                    continue;
                }

                // Ordered narrowest-cause-first, because more than one of these is true at once for
                // most mistakes and only the first is reported. A declared type that is already a
                // contract is also instantiable, and the useful sentence is the one about
                // [WProtoInclude]; a value type is also unassignable, and the useful sentence is
                // the one about what a declared root is for.
                if (IsContract(pair.Declared))
                {
                    report(
                        Diagnostic.Create(
                            WProtoDiagnostics.DeclaredRootOnContract,
                            location,
                            pair.Declared.ToDisplayString(),
                            pair.Root.ToDisplayString()
                        )
                    );
                    continue;
                }

                if (Instantiable(pair.Declared))
                {
                    report(
                        Diagnostic.Create(
                            WProtoDiagnostics.DeclaredRootOnInstantiableType,
                            location,
                            pair.Declared.ToDisplayString(),
                            pair.Root.ToDisplayString()
                        )
                    );
                    continue;
                }

                if (!Assignable(pair.Root, pair.Declared))
                {
                    report(
                        Diagnostic.Create(
                            WProtoDiagnostics.DeclaredRootNotAssignable,
                            location,
                            pair.Declared.ToDisplayString(),
                            pair.Root.ToDisplayString()
                        )
                    );
                    continue;
                }

                if (
                    referenced.TryGetValue(
                        pair.Declared,
                        out List<ReferencedDeclaration> declarations
                    )
                )
                {
                    foreach (ReferencedDeclaration declaration in declarations)
                    {
                        if (SymbolEqualityComparer.Default.Equals(pair.Root, declaration.Root))
                        {
                            continue;
                        }

                        report(
                            Diagnostic.Create(
                                WProtoDiagnostics.ConflictingReferencedDeclaredRoot,
                                location,
                                pair.Declared.ToDisplayString(),
                                pair.Root.ToDisplayString(),
                                declaration.Root.ToDisplayString(),
                                compilation.AssemblyName,
                                declaration.AssemblyName
                            )
                        );
                        break;
                    }
                }
            }
        }

        private static Dictionary<ITypeSymbol, List<ReferencedDeclaration>> ReferencedDeclarations(
            Compilation compilation,
            Action<Diagnostic> report
        )
        {
            Dictionary<ITypeSymbol, List<ReferencedDeclaration>> declarations = new Dictionary<
                ITypeSymbol,
                List<ReferencedDeclaration>
            >(SymbolEqualityComparer.Default);
            HashSet<ITypeSymbol> reported = new HashSet<ITypeSymbol>(
                SymbolEqualityComparer.Default
            );

            foreach (MetadataReference reference in compilation.References)
            {
                if (!(compilation.GetAssemblyOrModuleSymbol(reference) is IAssemblySymbol assembly))
                {
                    continue;
                }

                HashSet<ITypeSymbol> seen = new HashSet<ITypeSymbol>(
                    SymbolEqualityComparer.Default
                );
                foreach (Pair pair in Pairs(assembly))
                {
                    if (pair.Declared == null || !seen.Add(pair.Declared) || !Usable(pair))
                    {
                        continue;
                    }

                    if (
                        !declarations.TryGetValue(
                            pair.Declared,
                            out List<ReferencedDeclaration> roots
                        )
                    )
                    {
                        roots = new List<ReferencedDeclaration>();
                        declarations.Add(pair.Declared, roots);
                    }
                    else
                    {
                        ReferencedDeclaration existing = roots[0];
                        if (
                            !SymbolEqualityComparer.Default.Equals(existing.Root, pair.Root)
                            && reported.Add(pair.Declared)
                        )
                        {
                            report(
                                Diagnostic.Create(
                                    WProtoDiagnostics.ConflictingReferencedDeclaredRoot,
                                    Location.None,
                                    pair.Declared.ToDisplayString(),
                                    existing.Root.ToDisplayString(),
                                    pair.Root.ToDisplayString(),
                                    existing.AssemblyName,
                                    assembly.Identity.Name
                                )
                            );
                        }
                    }

                    roots.Add(new ReferencedDeclaration(pair.Root, assembly.Identity.Name));
                }
            }

            return declarations;
        }

        private static bool Usable(Pair pair)
        {
            return pair.Declared != null
                && pair.Root != null
                && !Open(pair.Declared)
                && !Open(pair.Root)
                && !SymbolEqualityComparer.Default.Equals(pair.Declared, pair.Root)
                && !IsContract(pair.Declared)
                && !Instantiable(pair.Declared)
                && Assignable(pair.Root, pair.Declared);
        }

        /// <summary>
        /// Returns one type-argument list per pair this compilation can register.
        /// </summary>
        /// <param name="compilation">The compilation being generated for.</param>
        /// <returns>
        /// Strings of the form <c>&lt;global::Declared, global::Root&gt;</c>, ready to append to the
        /// provider's <c>Register</c> call.
        /// </returns>
        internal static IEnumerable<string> Registrations(
            Compilation compilation,
            Action<Diagnostic> report,
            HashSet<string> announced
        )
        {
            HashSet<ITypeSymbol> seen = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
            List<string> registrations = new List<string>();

            foreach (Pair pair in Pairs(compilation.Assembly))
            {
                if (
                    pair.Declared == null
                    || pair.Root == null
                    || !seen.Add(pair.Declared)
                    || Open(pair.Declared)
                    || Open(pair.Root)
                    || SymbolEqualityComparer.Default.Equals(pair.Declared, pair.Root)
                    || IsContract(pair.Declared)
                    || Instantiable(pair.Declared)
                    || !Assignable(pair.Root, pair.Declared)
                )
                {
                    // Each of these has a diagnostic beside it in Validate. Emitting anyway would
                    // turn a message naming the attribute into a compiler error inside generated
                    // code, which is the failure mode these pairs exist to avoid.
                    continue;
                }

                // Nameability is the one skip Validate cannot phrase as a refusal, so it is
                // announced here instead -- the same reporter the closure scans use, so a pair that
                // silently gets no registration cannot exist in any of the four.
                Location location =
                    pair.Attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation()
                    ?? Location.None;
                if (
                    TypeNaming.ReportIfUnnameable(
                        pair.Declared,
                        compilation,
                        location,
                        report,
                        announced
                    )
                    || TypeNaming.ReportIfUnnameable(
                        pair.Root,
                        compilation,
                        location,
                        report,
                        announced
                    )
                )
                {
                    continue;
                }

                registrations.Add(
                    "<"
                        + pair.Declared.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                        + ", "
                        + pair.Root.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                        + ">"
                );
            }

            return registrations;
        }

        /// <summary>
        /// Reports whether <paramref name="root"/> can be handed to a parameter of type
        /// <paramref name="declared"/>.
        /// </summary>
        /// <param name="root">The root type.</param>
        /// <param name="declared">The declared type.</param>
        /// <returns><c>false</c> when the generated constraint would not be satisfied.</returns>
        /// <remarks>
        /// The adapter is declared <c>where TDeclared : class</c> and <c>where TRoot : TDeclared</c>,
        /// so both halves are checked: a value type as the declared type is <c>CS0452</c> in
        /// generated code, and a root that does not derive from it is <c>CS0311</c>. Neither names
        /// the attribute that caused it.
        /// </remarks>
        private static bool Assignable(ITypeSymbol root, ITypeSymbol declared)
        {
            if (!declared.IsReferenceType)
            {
                return false;
            }

            if (declared.TypeKind == TypeKind.Interface)
            {
                foreach (INamedTypeSymbol implemented in root.AllInterfaces)
                {
                    if (SymbolEqualityComparer.Default.Equals(implemented, declared))
                    {
                        return true;
                    }
                }

                return false;
            }

            for (
                INamedTypeSymbol current = root.BaseType;
                current != null;
                current = current.BaseType
            )
            {
                if (SymbolEqualityComparer.Default.Equals(current, declared))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Reports whether a value's runtime type can be <paramref name="declared"/> itself.
        /// </summary>
        /// <param name="declared">The declared type.</param>
        /// <returns><c>true</c> when the type is not an interface and not abstract.</returns>
        /// <remarks>
        /// A declared root exists for a type with no encoding of its own. Given one that can be
        /// instantiated, <see cref="WProtoFacade"/> short-circuits its exact-type match before
        /// asking the adapter whether it can write the value, the adapter fails to narrow it to the
        /// root, and the value encodes to nothing -- measured as a populated instance writing zero
        /// bytes and reading back as the root.
        /// </remarks>
        private static bool Instantiable(ITypeSymbol declared)
        {
            return declared.TypeKind != TypeKind.Interface && !declared.IsAbstract;
        }

        /// <summary>
        /// Reports whether <paramref name="type"/> is a construction with nothing to register.
        /// </summary>
        /// <param name="type">The declared or root type.</param>
        /// <returns><c>true</c> when it is unbound or still holds a type parameter.</returns>
        /// <remarks>
        /// Arity is the wrong question and asking it was a bug: <c>IThing&lt;int&gt;</c> has arity
        /// one exactly as <c>IThing&lt;&gt;</c> does, so the check rejected the closed pair
        /// <c>WPROTO026</c>'s own message offers as the remedy. Only an unbound construction --
        /// <c>typeof(IThing&lt;&gt;)</c>, the only open form an attribute argument can spell -- has
        /// nothing to register.
        /// </remarks>
        private static bool Open(ITypeSymbol type)
        {
            return TypeNaming.IsOpen(type)
                || (type is INamedTypeSymbol named && named.IsUnboundGenericType);
        }

        private static bool IsContract(ITypeSymbol type)
        {
            foreach (AttributeData attribute in type.GetAttributes())
            {
                if (attribute.AttributeClass?.ToDisplayString() == ContractAttribute)
                {
                    return true;
                }
            }

            return false;
        }

        private static IEnumerable<Pair> Pairs(IAssemblySymbol assembly)
        {
            foreach (AttributeData attribute in assembly.GetAttributes())
            {
                if (
                    attribute.AttributeClass == null
                    || attribute.AttributeClass.ToDisplayString() != DeclaredRootAttribute
                    || attribute.ConstructorArguments.Length < 2
                )
                {
                    continue;
                }

                // ITypeSymbol rather than INamedTypeSymbol: `typeof(IThing[])` is an array symbol,
                // and dropping it here would be a pair that neither registers nor reports.
                yield return new Pair(
                    attribute.ConstructorArguments[0].Value as ITypeSymbol,
                    attribute.ConstructorArguments[1].Value as ITypeSymbol,
                    attribute
                );
            }
        }

        private readonly struct Pair
        {
            internal Pair(ITypeSymbol declared, ITypeSymbol root, AttributeData attribute)
            {
                Declared = declared;
                Root = root;
                Attribute = attribute;
            }

            internal ITypeSymbol Declared { get; }

            internal ITypeSymbol Root { get; }

            internal AttributeData Attribute { get; }
        }

        private readonly struct ReferencedDeclaration
        {
            internal ReferencedDeclaration(ITypeSymbol root, string assemblyName)
            {
                Root = root;
                AssemblyName = assemblyName;
            }

            internal ITypeSymbol Root { get; }

            internal string AssemblyName { get; }
        }
    }
}
