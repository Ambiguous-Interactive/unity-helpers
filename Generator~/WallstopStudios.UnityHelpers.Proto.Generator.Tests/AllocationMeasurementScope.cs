// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator.Tests
{
    using System;
    using System.Runtime;
    using NUnit.Framework;

    /// <summary>Prevents collection during one host allocation measurement.</summary>
    internal sealed class AllocationMeasurementScope : IDisposable
    {
        private const long AllocationBudgetBytes = 128 * 1024 * 1024;

        private bool _ownsRegion;

        internal AllocationMeasurementScope(long allocationBudget)
        {
            Assert.AreNotEqual(
                GCLatencyMode.NoGCRegion,
                GCSettings.LatencyMode,
                "An allocation measurement cannot borrow another owner's no-GC region."
            );
            Assert.IsTrue(
                GC.TryStartNoGCRegion(allocationBudget, disallowFullBlockingGC: false),
                "Host allocation measurements require an available no-GC region."
            );
            _ownsRegion = true;
        }

        internal static AllocationMeasurementScope Start()
        {
            return new AllocationMeasurementScope(AllocationBudgetBytes);
        }

        /// <summary>Ends the owned region and rejects a measurement interrupted by collection.</summary>
        public void Dispose()
        {
            if (!_ownsRegion)
            {
                return;
            }

            _ownsRegion = false;
            GC.EndNoGCRegion();
        }
    }
}
