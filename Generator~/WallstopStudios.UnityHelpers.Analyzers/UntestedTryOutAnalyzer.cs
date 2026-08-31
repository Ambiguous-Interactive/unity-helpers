// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Analyzers
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.Diagnostics;
    using Microsoft.CodeAnalysis.Operations;

    /// <summary>
    /// Reports a read of a <c>TryXxx</c> <c>out</c> variable that was bound by a call whose
    /// <c>bool</c> result nobody looked at.
    /// </summary>
    /// <remarks>
    /// The signal is the READ, not the discard. <c>_ = set.TryAdd(x, out Thing unused);</c> is a
    /// legitimate fire-and-forget, and matching the discard alone would report every one of those
    /// in the tree and make the rule unusable (#629).
    /// <para>
    /// Precision limitation: the pairing of a read to the binding that reaches it is done by SOURCE
    /// POSITION within one operation block, not by a control-flow graph. A read is reported only
    /// when the nearest binding of that symbol at or before it in source order is an untested
    /// <c>TryXxx</c> call, so an intervening ordinary assignment or a tested rebinding silences it.
    /// The approximation is sound for straight-line code and deliberately conservative elsewhere: a
    /// read inside a loop that PRECEDES the untested call in source order reaches that call on the
    /// second iteration, and is not reported. Reporting it would need a CFG, and the shape the
    /// issue is about -- call, then read, in order -- does not need one.
    /// </para>
    /// </remarks>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UntestedTryOutAnalyzer : DiagnosticAnalyzer
    {
        private const string TryPrefix = "Try";

        /// <inheritdoc />
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(UnityHelpersDiagnostics.UntestedTryOutValueIsRead);

        /// <inheritdoc />
        public override void Initialize(AnalysisContext context)
        {
            context.EnableConcurrentExecution();
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.RegisterOperationBlockStartAction(OnOperationBlockStart);
        }

        private static void OnOperationBlockStart(OperationBlockStartAnalysisContext context)
        {
            // One method body is the unit here: a binding and the read it reaches have to be
            // compared against each other, which no per-invocation action can do.
            BlockState state = new BlockState();
            context.RegisterOperationAction(state.OnInvocation, OperationKind.Invocation);
            context.RegisterOperationAction(
                state.OnSimpleAssignment,
                OperationKind.SimpleAssignment
            );
            context.RegisterOperationAction(
                state.OnVariableDeclarator,
                OperationKind.VariableDeclarator
            );
            context.RegisterOperationAction(
                state.OnReference,
                OperationKind.LocalReference,
                OperationKind.ParameterReference,
                OperationKind.FieldReference
            );
            context.RegisterOperationBlockEndAction(state.Flush);
        }

        /// <summary>
        /// The <c>TryXxx</c> contract itself, with no type allow-list: a <c>bool</c> return, a
        /// <c>Try</c> name and an <c>out</c> to fill covers the BCL, this package and a consumer's
        /// own API alike.
        /// </summary>
        private static bool IsTryWithOutParameter(IMethodSymbol method)
        {
            if (method == null || method.ReturnType == null)
            {
                return false;
            }

            if (method.ReturnType.SpecialType != SpecialType.System_Boolean)
            {
                return false;
            }

            if (!method.Name.StartsWith(TryPrefix, StringComparison.Ordinal))
            {
                return false;
            }

            foreach (IParameterSymbol parameter in method.Parameters)
            {
                if (parameter.RefKind == RefKind.Out)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ResultIsDiscarded(IInvocationOperation invocation)
        {
            IOperation current = invocation;
            IOperation parent = current.Parent;
            while (parent is IConversionOperation)
            {
                current = parent;
                parent = current.Parent;
            }

            if (parent is IExpressionStatementOperation)
            {
                return true;
            }

            return parent is ISimpleAssignmentOperation assignment
                && assignment.Target is IDiscardOperation
                && assignment.Value == current;
        }

        /// <summary>
        /// The local, parameter or field <paramref name="operation"/> names, when that symbol
        /// identifies one storage slot on its own.
        /// </summary>
        /// <remarks>
        /// A field qualifies only when it is static or reached through <c>this</c>. The field SYMBOL
        /// is shared by every instance, so tracking <c>a.Field</c> and <c>b.Field</c> under it pairs
        /// a binding on one object with a read of another -- <c>TryFill(out a.Field); return
        /// b.Field;</c> reported a value nothing had touched.
        /// </remarks>
        private static bool TryGetTrackedSymbol(IOperation operation, out ISymbol symbol)
        {
            symbol = null;
            IOperation current = operation;
            while (current is IConversionOperation conversion)
            {
                current = conversion.Operand;
            }

            if (current is IDeclarationExpressionOperation declaration)
            {
                current = declaration.Expression;
            }

            if (current is ILocalReferenceOperation local)
            {
                symbol = local.Local;
            }
            else if (current is IParameterReferenceOperation parameter)
            {
                symbol = parameter.Parameter;
            }
            else if (current is IFieldReferenceOperation field)
            {
                if (field.Instance != null && !(field.Instance is IInstanceReferenceOperation))
                {
                    return false;
                }

                symbol = field.Field;
            }

            return symbol != null;
        }

        /// <summary>
        /// Whether <paramref name="reference"/> is being written rather than read.
        /// </summary>
        /// <remarks>
        /// A <c>ref</c> argument counts as a write here even though the callee may also read it,
        /// because <see cref="BlockState.OnInvocation"/> has already recorded that same position as
        /// a BINDING. Counting it as a read too would double-count one token and land the
        /// diagnostic on the rebinding call instead of on the use that consumes the value.
        /// </remarks>
        private static bool IsWriteTarget(IOperation reference)
        {
            IOperation current = reference;
            IOperation parent = current.Parent;

            // `out Thing thing` wraps the reference in a declaration expression, so the argument is
            // one level further up than it is for `out thing`.
            if (parent is IDeclarationExpressionOperation)
            {
                current = parent;
                parent = current.Parent;
            }

            if (parent is ISimpleAssignmentOperation assignment && assignment.Target == current)
            {
                return true;
            }

            return parent is IArgumentOperation argument
                && argument.Parameter != null
                && (
                    argument.Parameter.RefKind == RefKind.Out
                    || argument.Parameter.RefKind == RefKind.Ref
                );
        }

        /// <summary>
        /// A write to a tracked symbol, recorded at the source position by which it has happened.
        /// </summary>
        private sealed class Binding
        {
            public Binding(ISymbol symbol, int writtenAt, string untestedTryMethodName)
            {
                Symbol = symbol;
                WrittenAt = writtenAt;
                UntestedTryMethodName = untestedTryMethodName;
            }

            /// <summary>The local, parameter or field this write binds.</summary>
            public ISymbol Symbol { get; }

            /// <summary>End of the syntax that performs the write.</summary>
            public int WrittenAt { get; }

            /// <summary>
            /// The called method's name when this binding came from an untested <c>TryXxx</c>, and
            /// <c>null</c> for every other kind of write.
            /// </summary>
            public string UntestedTryMethodName { get; }

            /// <summary>
            /// One mistake is one warning: the first read this binding reaches sets this, so the
            /// remaining reads of the same value stay quiet.
            /// </summary>
            public bool Reported { get; set; }
        }

        /// <summary>
        /// A read of a tracked symbol, and the location the diagnostic is reported at.
        /// </summary>
        private sealed class ReadSite
        {
            public ReadSite(ISymbol symbol, int readAt, Location location)
            {
                Symbol = symbol;
                ReadAt = readAt;
                Location = location;
            }

            /// <summary>The local, parameter or field being read.</summary>
            public ISymbol Symbol { get; }

            /// <summary>Start of the syntax that performs the read.</summary>
            public int ReadAt { get; }

            /// <summary>Where the diagnostic is reported.</summary>
            public Location Location { get; }
        }

        /// <summary>
        /// Everything one operation block contributes, collected as the block is walked and
        /// resolved once at its end.
        /// </summary>
        private sealed class BlockState
        {
            private readonly object gate = new object();
            private readonly List<Binding> bindings = new List<Binding>();
            private readonly List<ReadSite> reads = new List<ReadSite>();

            public void OnInvocation(OperationAnalysisContext context)
            {
                IInvocationOperation invocation = (IInvocationOperation)context.Operation;
                IMethodSymbol target = invocation.TargetMethod;
                bool untested = IsTryWithOutParameter(target) && ResultIsDiscarded(invocation);
                int writtenAt = invocation.Syntax.Span.End;

                foreach (IArgumentOperation argument in invocation.Arguments)
                {
                    RefKind refKind =
                        argument.Parameter == null ? RefKind.None : argument.Parameter.RefKind;
                    if (refKind != RefKind.Out && refKind != RefKind.Ref)
                    {
                        continue;
                    }

                    if (!TryGetTrackedSymbol(argument.Value, out ISymbol symbol))
                    {
                        continue;
                    }

                    string methodName = untested && refKind == RefKind.Out ? target.Name : null;
                    Add(new Binding(symbol, writtenAt, methodName));
                }
            }

            public void OnSimpleAssignment(OperationAnalysisContext context)
            {
                ISimpleAssignmentOperation assignment = (ISimpleAssignmentOperation)
                    context.Operation;
                if (TryGetTrackedSymbol(assignment.Target, out ISymbol symbol))
                {
                    Add(new Binding(symbol, assignment.Syntax.Span.End, null));
                }
            }

            public void OnVariableDeclarator(OperationAnalysisContext context)
            {
                IVariableDeclaratorOperation declarator = (IVariableDeclaratorOperation)
                    context.Operation;
                if (declarator.Initializer == null || declarator.Symbol == null)
                {
                    return;
                }

                Add(new Binding(declarator.Symbol, declarator.Syntax.Span.End, null));
            }

            public void OnReference(OperationAnalysisContext context)
            {
                IOperation reference = context.Operation;
                if (reference is ILocalReferenceOperation local && local.IsDeclaration)
                {
                    return;
                }

                if (!TryGetTrackedSymbol(reference, out ISymbol symbol))
                {
                    return;
                }

                if (IsWriteTarget(reference))
                {
                    return;
                }

                lock (gate)
                {
                    reads.Add(
                        new ReadSite(
                            symbol,
                            reference.Syntax.Span.Start,
                            reference.Syntax.GetLocation()
                        )
                    );
                }
            }

            public void Flush(OperationBlockAnalysisContext context)
            {
                List<Binding> written;
                List<ReadSite> ordered;
                lock (gate)
                {
                    written = new List<Binding>(bindings);
                    ordered = new List<ReadSite>(reads);
                }

                bool anyUntested = false;
                foreach (Binding binding in written)
                {
                    if (binding.UntestedTryMethodName != null)
                    {
                        anyUntested = true;
                        break;
                    }
                }

                if (!anyUntested || ordered.Count == 0)
                {
                    return;
                }

                Dictionary<ISymbol, List<Binding>> bySymbol = new Dictionary<
                    ISymbol,
                    List<Binding>
                >(SymbolEqualityComparer.Default);
                foreach (Binding binding in written)
                {
                    if (!bySymbol.TryGetValue(binding.Symbol, out List<Binding> forSymbol))
                    {
                        forSymbol = new List<Binding>();
                        bySymbol.Add(binding.Symbol, forSymbol);
                    }

                    forSymbol.Add(binding);
                }

                foreach (List<Binding> forSymbol in bySymbol.Values)
                {
                    forSymbol.Sort((left, right) => left.WrittenAt.CompareTo(right.WrittenAt));
                }

                ordered.Sort((left, right) => left.ReadAt.CompareTo(right.ReadAt));

                foreach (ReadSite read in ordered)
                {
                    if (!bySymbol.TryGetValue(read.Symbol, out List<Binding> forSymbol))
                    {
                        continue;
                    }

                    // The nearest binding at or before the read is the one that reaches it.
                    Binding nearest = null;
                    foreach (Binding binding in forSymbol)
                    {
                        if (binding.WrittenAt <= read.ReadAt)
                        {
                            nearest = binding;
                            continue;
                        }

                        break;
                    }

                    if (
                        nearest == null
                        || nearest.UntestedTryMethodName == null
                        || nearest.Reported
                    )
                    {
                        continue;
                    }

                    nearest.Reported = true;
                    context.ReportDiagnostic(
                        Diagnostic.Create(
                            UnityHelpersDiagnostics.UntestedTryOutValueIsRead,
                            read.Location,
                            nearest.UntestedTryMethodName,
                            read.Symbol.Name
                        )
                    );
                }
            }

            private void Add(Binding binding)
            {
                lock (gate)
                {
                    bindings.Add(binding);
                }
            }
        }
    }
}
