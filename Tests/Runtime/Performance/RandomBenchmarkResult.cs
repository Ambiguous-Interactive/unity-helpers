// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Runtime.Performance
{
    using System;
    using WallstopStudios.UnityHelpers.Core.Random;

    internal sealed class RandomBenchmarkResult
    {
        public Type RandomType { get; }

        public string DisplayName => RandomType.Name;

        public RandomGeneratorMetadata Metadata { get; }

        public double NextBoolPerSecond { get; }

        public double NextIntPerSecond { get; }

        public double NextUintPerSecond { get; }

        public double NextFloatPerSecond { get; }

        public double NextDoublePerSecond { get; }

        public double NextUintRangePerSecond { get; }

        public double NextIntRangePerSecond { get; }

        public RandomSpeedBucket SpeedBucket { get; set; } = RandomSpeedBucket.Unknown;

        public double SpeedRatio { get; set; }

        public int SpeedBucketSortValue => (int)SpeedBucket;

        public string SpeedBucketLabel => SpeedBucket.ToLabel();

        public string QualityLabel => Metadata.QualityLabel;

        public int QualitySortValue => Metadata.QualitySortValue;

        public RandomBenchmarkResult(
            Type randomType,
            double nextBoolPerSecond,
            double nextIntPerSecond,
            double nextUintPerSecond,
            double nextFloatPerSecond,
            double nextDoublePerSecond,
            double nextUintRangePerSecond,
            double nextIntRangePerSecond,
            RandomGeneratorMetadata metadata
        )
        {
            RandomType = randomType ?? throw new ArgumentNullException(nameof(randomType));
            Metadata = metadata;
            NextBoolPerSecond = nextBoolPerSecond;
            NextIntPerSecond = nextIntPerSecond;
            NextUintPerSecond = nextUintPerSecond;
            NextFloatPerSecond = nextFloatPerSecond;
            NextDoublePerSecond = nextDoublePerSecond;
            NextUintRangePerSecond = nextUintRangePerSecond;
            NextIntRangePerSecond = nextIntRangePerSecond;
        }
    }
}
