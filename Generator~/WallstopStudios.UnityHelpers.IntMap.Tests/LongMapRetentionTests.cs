// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.IntMap.Tests
{
    using System;
    using System.Runtime.CompilerServices;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.DataStructure;

    [TestFixture]
    public sealed class LongMapRetentionTests
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference AddReference(LongMap<object> map, ulong key)
        {
            object value = new();
            map[key] = value;
            return new WeakReference(value);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference RetireReference(LongMap<object> map, int operation)
        {
            WeakReference reference = AddReference(map, 0);
            switch (operation)
            {
                case 0:
                    map.Remove(0, out _);
                    break;
                case 1:
                    map.Clear();
                    break;
                case 2:
                    map[0] = new object();
                    break;
                default:
                    object marker = new();
                    for (ulong key = 1; key < 128; ++key)
                    {
                        map[key] = marker;
                    }
                    map.Remove(0, out _);
                    break;
            }
            return reference;
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void RetiredValuesAreCollectibleWhileTheMapStaysAlive(int operation)
        {
            LongMap<object> map = new();
            WeakReference retired = RetireReference(map, operation);
            WeakReference retained = AddReference(map, ulong.MaxValue);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Assert.That(retained.IsAlive, Is.True, "The map must retain its live entry.");
            Assert.That(retired.IsAlive, Is.False, "The map must release the retired reference.");
            GC.KeepAlive(map);
        }
    }
}
