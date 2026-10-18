// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Runtime.Performance
{
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Tests.Serialization;

    [Category("Performance")]
    public sealed partial class ProtoSerializationPerformanceTests
    {
        [TestCase(WProtoBenchmarkCorpus.ReadMemory)]
        [TestCase(WProtoBenchmarkCorpus.ReadStream)]
        [TestCase(WProtoBenchmarkCorpus.ReadFinite)]
        [TestCase(WProtoBenchmarkCorpus.ReadDirect)]
        [TestCase(WProtoBenchmarkCorpus.ReadPublic)]
        [TestCase(WProtoBenchmarkCorpus.WriteBuffer)]
        [TestCase(WProtoBenchmarkCorpus.WriteStream)]
        [TestCase(WProtoBenchmarkCorpus.WriteArray)]
        [TestCase(WProtoBenchmarkCorpus.WriteDirect)]
        [TestCase(WProtoBenchmarkCorpus.WritePublic)]
        [Explicit("Run the full calibrated corpus as a preregistered benchmark campaign.")]
        [Timeout(1_800_000)]
        public void CompareFrozenSevenFamilyCorpus(string operation)
        {
            RequireObservableClock();
            WProtoBenchmarkCorpus.Measure(operation);
        }
    }
}
