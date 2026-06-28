// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core
{
    using System;
    using NUnit.Framework;
    using NUnit.Framework.Interfaces;
    using NUnit.Framework.Internal;

    /// <summary>
    /// Marks a test fixture or test method that exercises reflection-based serialization
    /// (protobuf-net and/or System.Text.Json). Those serializers build their (de)serializers at
    /// runtime via reflection / <c>Type.MakeGenericType</c>, which the IL2CPP AOT compiler cannot
    /// emit, so the tests throw <c>ExecutionEngineException</c> ("no AOT code was generated"),
    /// <c>NotSupportedException</c> (unsupported reflection icall), or
    /// "No serializer defined for ..." in a standalone IL2CPP player. They pass on the Mono
    /// scripting backend (Editor / PlayMode), so the expectation is faithful there.
    ///
    /// This attribute ignores the test ONLY in IL2CPP builds (<c>ENABLE_IL2CPP</c>); on Mono it is a
    /// no-op and the test runs normally. It is an INTERIM measure: remove every usage once the
    /// in-tree, AOT-native, wire-compatible WallstopProto serializer lands (see PLAN.md). Tracking the
    /// usages is how we find the gates to delete.
    /// </summary>
    [AttributeUsage(
        AttributeTargets.Class | AttributeTargets.Method,
        AllowMultiple = false,
        Inherited = true
    )]
    public sealed class SkipUnderIL2CPPAttribute : NUnitAttribute, IApplyToTest
    {
        private const string DefaultReason =
            "Reflection-based serialization (protobuf-net / System.Text.Json) is not AOT-compatible "
            + "under IL2CPP; runs on the Mono backend. Interim gate tracked by WallstopProto (PLAN.md).";

        private readonly string _reason;

        public SkipUnderIL2CPPAttribute(string reason = DefaultReason)
        {
            _reason = string.IsNullOrEmpty(reason) ? DefaultReason : reason;
        }

        /// <summary>The human-readable skip reason (also surfaced as the NUnit skip reason).</summary>
        public string Reason => _reason;

        public void ApplyToTest(Test test)
        {
#if ENABLE_IL2CPP
            // Do not override a test the runner already marked not-runnable (e.g. a compile/discovery error).
            if (test.RunState == RunState.NotRunnable)
            {
                return;
            }

            test.RunState = RunState.Ignored;
            test.Properties.Set(PropertyNames.SkipReason, _reason);
#else
            // No-op on the Mono backend; reference the field so it is not flagged as unused.
            _ = _reason;
#endif
        }
    }
}
