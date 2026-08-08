// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using System.Text;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using DebuggableAttribute = System.Diagnostics.DebuggableAttribute;

    /// <summary>
    /// Pins the "every CI leg compiles this package with optimizations enabled" contract.
    /// </summary>
    /// <remarks>
    /// The workflow passes <c>-releaseCodeOptimization</c> and builds non-development players,
    /// but nothing verified that the compiler honored either. Roslyn records the answer in the
    /// assembly-level <see cref="DebuggableAttribute"/>: <c>-optimize+</c> emits
    /// <c>IgnoreSymbolStoreSequencePoints</c> alone, while <c>-optimize-</c> adds
    /// <c>DisableOptimizations</c>. Reading it back from the assemblies the run actually loaded
    /// covers both compilation paths -- PlayMode exercises the editor-compiled
    /// <c>Library/ScriptAssemblies</c>, standalone exercises the player build.
    /// </remarks>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    [NUnit.Framework.Category("Validation")]
    public sealed class ReleaseCompilationContractTests
    {
        private const string PackageAssemblyPrefix = "WallstopStudios.UnityHelpers";

        [Test]
        public void PackageAssembliesAreCompiledWithOptimizationsEnabled()
        {
            List<string> optimized = new();
            List<string> unoptimized = new();

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                string name;
                try
                {
                    name = assembly.GetName().Name;
                }
                catch (Exception)
                {
                    continue;
                }

                if (
                    string.IsNullOrEmpty(name)
                    || !name.StartsWith(PackageAssemblyPrefix, StringComparison.Ordinal)
                )
                {
                    continue;
                }

                if (IsOptimizerDisabled(assembly))
                {
                    unoptimized.Add(name);
                }
                else
                {
                    optimized.Add(name);
                }
            }

            optimized.Sort(StringComparer.Ordinal);
            unoptimized.Sort(StringComparer.Ordinal);

            // A probe that inspected nothing is not a passing contract.
            Assert.Greater(
                optimized.Count + unoptimized.Count,
                0,
                $"No loaded assembly is named '{PackageAssemblyPrefix}*', so this contract "
                    + "inspected nothing."
            );

            if (!Helpers.IsRunningInContinuousIntegration)
            {
                Assert.Ignore(Describe(optimized, unoptimized, enforced: false));
            }

            Assert.IsEmpty(unoptimized, Describe(optimized, unoptimized, enforced: true));
        }

        private static bool IsOptimizerDisabled(Assembly assembly)
        {
            object[] attributes;
            try
            {
                attributes = assembly.GetCustomAttributes(typeof(DebuggableAttribute), false);
            }
            catch (Exception)
            {
                return false;
            }

            // An absent attribute is the JIT's optimized default, so only an attribute that
            // explicitly disables the optimizer counts against the contract.
            foreach (object attribute in attributes)
            {
                if (
                    attribute is DebuggableAttribute debuggable
                    && debuggable.IsJITOptimizerDisabled
                )
                {
                    return true;
                }
            }

            return false;
        }

        private static string Describe(
            IReadOnlyList<string> optimized,
            IReadOnlyList<string> unoptimized,
            bool enforced
        )
        {
            StringBuilder message = new();
            if (enforced)
            {
                message.Append(
                    "Compiled with the optimizer DISABLED. Unity CI must pass "
                        + "-releaseCodeOptimization for editor compilation and omit "
                        + "BuildOptions.Development for player builds. Offending assemblies: "
                );
            }
            else
            {
                message.Append(
                    "Reported, not enforced: this run is not under CI. "
                        + $"{optimized.Count} optimized, {unoptimized.Count} unoptimized"
                );
                if (unoptimized.Count == 0)
                {
                    return message.Append('.').ToString();
                }

                message.Append(" -- ");
            }

            return message.Append(string.Join(", ", unoptimized)).Append('.').ToString();
        }
    }
}
