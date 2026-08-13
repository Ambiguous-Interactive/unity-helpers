// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Runtime.Performance
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using NUnit.Framework;
    using ProtoBuf;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    [Category("Performance")]
    public sealed partial class ProtoSerializationPerformanceTests
    {
        private const int StartupRounds = 9;

        private static readonly Type[] StartupContractClosures =
        {
            typeof(StartupContract<StartupWarmupMarker>),
            typeof(StartupContract<StartupSerializeMarker01>),
            typeof(StartupContract<StartupSerializeMarker02>),
            typeof(StartupContract<StartupSerializeMarker03>),
            typeof(StartupContract<StartupSerializeMarker04>),
            typeof(StartupContract<StartupSerializeMarker05>),
            typeof(StartupContract<StartupSerializeMarker06>),
            typeof(StartupContract<StartupSerializeMarker07>),
            typeof(StartupContract<StartupSerializeMarker08>),
            typeof(StartupContract<StartupSerializeMarker09>),
            typeof(StartupContract<StartupWallstopDeserializeMarker01>),
            typeof(StartupContract<StartupWallstopDeserializeMarker02>),
            typeof(StartupContract<StartupWallstopDeserializeMarker03>),
            typeof(StartupContract<StartupWallstopDeserializeMarker04>),
            typeof(StartupContract<StartupWallstopDeserializeMarker05>),
            typeof(StartupContract<StartupWallstopDeserializeMarker06>),
            typeof(StartupContract<StartupWallstopDeserializeMarker07>),
            typeof(StartupContract<StartupWallstopDeserializeMarker08>),
            typeof(StartupContract<StartupWallstopDeserializeMarker09>),
            typeof(StartupContract<StartupProtobufDeserializeMarker01>),
            typeof(StartupContract<StartupProtobufDeserializeMarker02>),
            typeof(StartupContract<StartupProtobufDeserializeMarker03>),
            typeof(StartupContract<StartupProtobufDeserializeMarker04>),
            typeof(StartupContract<StartupProtobufDeserializeMarker05>),
            typeof(StartupContract<StartupProtobufDeserializeMarker06>),
            typeof(StartupContract<StartupProtobufDeserializeMarker07>),
            typeof(StartupContract<StartupProtobufDeserializeMarker08>),
            typeof(StartupContract<StartupProtobufDeserializeMarker09>),
        };

        [Test, Timeout(0)]
        public void CompareGeneratedRegistrationAndFirstApiUse()
        {
            Assert.AreEqual(28, StartupContractClosures.Length);
            Assert.IsTrue(
                global::WallstopStudios
                    .UnityHelpers
                    .Generated
                    .WProtoGeneratedRegistrar
                    .HasRecordedFirstRegistration
            );
            Assert.Greater(
                global::WallstopStudios
                    .UnityHelpers
                    .Generated
                    .WProtoGeneratedRegistrar
                    .FirstRegistrationElapsedTimestampTicks,
                0
            );
            PrimeSharedStartupPaths<StartupWarmupMarker>();

            double[] wallstopProtoSerialize = new double[StartupRounds];
            double[] protobufNetSerialize = new double[StartupRounds];
            double[] wallstopProtoDeserialize = new double[StartupRounds];
            double[] protobufNetDeserialize = new double[StartupRounds];

            MeasureStartupRound<
                StartupSerializeMarker01,
                StartupWallstopDeserializeMarker01,
                StartupProtobufDeserializeMarker01
            >(
                0,
                wallstopProtoSerialize,
                protobufNetSerialize,
                wallstopProtoDeserialize,
                protobufNetDeserialize
            );
            MeasureStartupRound<
                StartupSerializeMarker02,
                StartupWallstopDeserializeMarker02,
                StartupProtobufDeserializeMarker02
            >(
                1,
                wallstopProtoSerialize,
                protobufNetSerialize,
                wallstopProtoDeserialize,
                protobufNetDeserialize
            );
            MeasureStartupRound<
                StartupSerializeMarker03,
                StartupWallstopDeserializeMarker03,
                StartupProtobufDeserializeMarker03
            >(
                2,
                wallstopProtoSerialize,
                protobufNetSerialize,
                wallstopProtoDeserialize,
                protobufNetDeserialize
            );
            MeasureStartupRound<
                StartupSerializeMarker04,
                StartupWallstopDeserializeMarker04,
                StartupProtobufDeserializeMarker04
            >(
                3,
                wallstopProtoSerialize,
                protobufNetSerialize,
                wallstopProtoDeserialize,
                protobufNetDeserialize
            );
            MeasureStartupRound<
                StartupSerializeMarker05,
                StartupWallstopDeserializeMarker05,
                StartupProtobufDeserializeMarker05
            >(
                4,
                wallstopProtoSerialize,
                protobufNetSerialize,
                wallstopProtoDeserialize,
                protobufNetDeserialize
            );
            MeasureStartupRound<
                StartupSerializeMarker06,
                StartupWallstopDeserializeMarker06,
                StartupProtobufDeserializeMarker06
            >(
                5,
                wallstopProtoSerialize,
                protobufNetSerialize,
                wallstopProtoDeserialize,
                protobufNetDeserialize
            );
            MeasureStartupRound<
                StartupSerializeMarker07,
                StartupWallstopDeserializeMarker07,
                StartupProtobufDeserializeMarker07
            >(
                6,
                wallstopProtoSerialize,
                protobufNetSerialize,
                wallstopProtoDeserialize,
                protobufNetDeserialize
            );
            MeasureStartupRound<
                StartupSerializeMarker08,
                StartupWallstopDeserializeMarker08,
                StartupProtobufDeserializeMarker08
            >(
                7,
                wallstopProtoSerialize,
                protobufNetSerialize,
                wallstopProtoDeserialize,
                protobufNetDeserialize
            );
            MeasureStartupRound<
                StartupSerializeMarker09,
                StartupWallstopDeserializeMarker09,
                StartupProtobufDeserializeMarker09
            >(
                8,
                wallstopProtoSerialize,
                protobufNetSerialize,
                wallstopProtoDeserialize,
                protobufNetDeserialize
            );

            double registrationMicroseconds = TimestampTicksToMicroseconds(
                global::WallstopStudios
                    .UnityHelpers
                    .Generated
                    .WProtoGeneratedRegistrar
                    .FirstRegistrationElapsedTimestampTicks
            );
            double wallstopProtoSerializeApiMedian = Median(wallstopProtoSerialize);
            double protobufNetSerializeMedian = Median(protobufNetSerialize);
            double wallstopProtoDeserializeApiMedian = Median(wallstopProtoDeserialize);
            double protobufNetDeserializeMedian = Median(protobufNetDeserialize);
            double registrationAndSerializeMedian =
                registrationMicroseconds + wallstopProtoSerializeApiMedian;
            double registrationAndDeserializeMedian =
                registrationMicroseconds + wallstopProtoDeserializeApiMedian;

            UnityEngine.Debug.Log(
                $"One-time generated assembly registration: {registrationMicroseconds:0.00} us. "
                    + $"API values are medians of {StartupRounds} fresh generic contract closures; "
                    + "process startup and shared JIT warmup are excluded."
            );
            UnityEngine.Debug.Log(
                $"WallstopProto median first API use: serialize {wallstopProtoSerializeApiMedian:0.00} us; "
                    + $"deserialize {wallstopProtoDeserializeApiMedian:0.00} us."
            );
            UnityEngine.Debug.Log(
                "| Operation | WallstopProto (us) | protobuf-net (us) | Speedup |"
            );
            UnityEngine.Debug.Log(
                "| --------- | ------------------:| ----------------:| -------:|"
            );
            UnityEngine.Debug.Log(
                $"| One-time assembly registration + median first serialize | {registrationAndSerializeMedian, 18:0.00} | {protobufNetSerializeMedian, 16:0.00} | {protobufNetSerializeMedian / registrationAndSerializeMedian, 7:0.00}x |"
            );
            UnityEngine.Debug.Log(
                $"| One-time assembly registration + median first deserialize | {registrationAndDeserializeMedian, 18:0.00} | {protobufNetDeserializeMedian, 16:0.00} | {protobufNetDeserializeMedian / registrationAndDeserializeMedian, 7:0.00}x |"
            );
        }

        private static void PrimeSharedStartupPaths<TMarker>()
        {
            MeasureStartupSerializePair<TMarker>(true, out _, out _);
            _ = MeasureRegistrationAndWallstopDeserialize<TMarker>();
            _ = MeasureFirstProtobufDeserialize<TMarker>();
        }

        private static void MeasureStartupRound<TSerialize, TWallstopRead, TProtobufRead>(
            int round,
            double[] wallstopProtoSerialize,
            double[] protobufNetSerialize,
            double[] wallstopProtoDeserialize,
            double[] protobufNetDeserialize
        )
        {
            bool wallstopFirst = (round & 1) == 0;
            MeasureStartupSerializePair<TSerialize>(
                wallstopFirst,
                out wallstopProtoSerialize[round],
                out protobufNetSerialize[round]
            );

            if (wallstopFirst)
            {
                wallstopProtoDeserialize[round] =
                    MeasureRegistrationAndWallstopDeserialize<TWallstopRead>();
                protobufNetDeserialize[round] = MeasureFirstProtobufDeserialize<TProtobufRead>();
            }
            else
            {
                protobufNetDeserialize[round] = MeasureFirstProtobufDeserialize<TProtobufRead>();
                wallstopProtoDeserialize[round] =
                    MeasureRegistrationAndWallstopDeserialize<TWallstopRead>();
            }
        }

        private static void MeasureStartupSerializePair<TMarker>(
            bool wallstopFirst,
            out double wallstopProtoMicroseconds,
            out double protobufNetMicroseconds
        )
        {
            StartupContract<TMarker> sample = MakeStartupContract<TMarker>();
            byte[] wallstopProtoBuffer = new byte[4096];
            using MemoryStream protobufNetBuffer = new MemoryStream(4096);

            if (wallstopFirst)
            {
                wallstopProtoMicroseconds = MeasureRegistrationAndWallstopSerialize(
                    sample,
                    ref wallstopProtoBuffer
                );
                protobufNetMicroseconds = MeasureFirstProtobufSerialize(sample, protobufNetBuffer);
            }
            else
            {
                protobufNetMicroseconds = MeasureFirstProtobufSerialize(sample, protobufNetBuffer);
                wallstopProtoMicroseconds = MeasureRegistrationAndWallstopSerialize(
                    sample,
                    ref wallstopProtoBuffer
                );
            }
        }

        private static double MeasureRegistrationAndWallstopSerialize<TMarker>(
            StartupContract<TMarker> sample,
            ref byte[] buffer
        )
        {
            long started = Stopwatch.GetTimestamp();
            WProtoWriteResult result = WProtoFacade.Serialize(sample, ref buffer);
            long elapsed = Stopwatch.GetTimestamp() - started;

            Assert.IsTrue(result.Served);
            Assert.Greater(result.Length, 0);
            Assert.IsFalse(result.Resized);
            return TimestampTicksToMicroseconds(elapsed);
        }

        private static double MeasureFirstProtobufSerialize<TMarker>(
            StartupContract<TMarker> sample,
            MemoryStream destination
        )
        {
            long started = Stopwatch.GetTimestamp();
            Serializer.Serialize(destination, sample);
            long elapsed = Stopwatch.GetTimestamp() - started;

            Assert.Greater(destination.Length, 0);
            return TimestampTicksToMicroseconds(elapsed);
        }

        private static double MeasureRegistrationAndWallstopDeserialize<TMarker>()
        {
            StartupContract<TMarker> sample = MakeStartupContract<TMarker>();
            using MemoryStream source = new MemoryStream(4096);
            Serializer.Serialize(source, sample);
            byte[] payload = source.ToArray();

            long started = Stopwatch.GetTimestamp();
            bool served = WProtoFacade.TryDeserialize(
                payload,
                out StartupContract<TMarker> restored
            );
            long elapsed = Stopwatch.GetTimestamp() - started;

            Assert.IsTrue(served);
            AssertStartupContractEqual(sample, restored);
            return TimestampTicksToMicroseconds(elapsed);
        }

        private static double MeasureFirstProtobufDeserialize<TMarker>()
        {
            StartupContract<TMarker> sample = MakeStartupContract<TMarker>();
            byte[] payload = new byte[4096];
            WProtoWriteResult prepared = WProtoFacade.Serialize(sample, ref payload);
            Assert.IsTrue(prepared.Served);
            Assert.Greater(prepared.Length, 0);
            using MemoryStream source = new MemoryStream(
                payload,
                0,
                prepared.Length,
                writable: false,
                publiclyVisible: true
            );

            long started = Stopwatch.GetTimestamp();
            StartupContract<TMarker> restored = Serializer.Deserialize<StartupContract<TMarker>>(
                source
            );
            long elapsed = Stopwatch.GetTimestamp() - started;

            AssertStartupContractEqual(sample, restored);
            return TimestampTicksToMicroseconds(elapsed);
        }

        private static void AssertStartupContractEqual<TMarker>(
            StartupContract<TMarker> expected,
            StartupContract<TMarker> actual
        )
        {
            Assert.IsTrue(actual != null);
            Assert.AreEqual(expected.Id, actual.Id);
            Assert.AreEqual(expected.Label, actual.Label);
            CollectionAssert.AreEqual(expected.Values, actual.Values);
            CollectionAssert.AreEquivalent(expected.Scores, actual.Scores);
            Assert.IsTrue(actual.Child != null);
            Assert.AreEqual(expected.Child.Sequence, actual.Child.Sequence);
            Assert.AreEqual(expected.Child.Name, actual.Child.Name);
        }

        private static double Median(double[] measurements)
        {
            Array.Sort(measurements);
            return measurements[measurements.Length / 2];
        }

        private static double TimestampTicksToMicroseconds(long elapsed) =>
            elapsed * (1_000_000d / Stopwatch.Frequency);

        private static StartupContract<TMarker> MakeStartupContract<TMarker>() =>
            new StartupContract<TMarker>
            {
                Id = 42,
                Label = "startup",
                Values = new[] { 3, 20, 37, 54, 71, 88, 105, 122 },
                Scores = new Dictionary<string, int>
                {
                    { "alpha", 11 },
                    { "beta", 29 },
                    { "gamma", 47 },
                },
                Child = new StartupChild { Sequence = 987_654_321L, Name = "nested" },
            };

        [ProtoContract]
        [WProtoContract]
        internal sealed partial class StartupContract<TMarker>
        {
            [ProtoMember(1)]
            [WProtoMember(1)]
            public int Id;

            [ProtoMember(2)]
            [WProtoMember(2)]
            public string Label;

            [ProtoMember(3)]
            [WProtoMember(3)]
            public int[] Values;

            [ProtoMember(4)]
            [WProtoMember(4)]
            public Dictionary<string, int> Scores;

            [ProtoMember(5)]
            [WProtoMember(5)]
            public StartupChild Child;
        }

        [ProtoContract]
        [WProtoContract]
        internal sealed partial class StartupChild
        {
            [ProtoMember(1)]
            [WProtoMember(1)]
            public long Sequence;

            [ProtoMember(2)]
            [WProtoMember(2)]
            public string Name;
        }

        internal sealed class StartupWarmupMarker { }

        internal sealed class StartupSerializeMarker01 { }

        internal sealed class StartupSerializeMarker02 { }

        internal sealed class StartupSerializeMarker03 { }

        internal sealed class StartupSerializeMarker04 { }

        internal sealed class StartupSerializeMarker05 { }

        internal sealed class StartupSerializeMarker06 { }

        internal sealed class StartupSerializeMarker07 { }

        internal sealed class StartupSerializeMarker08 { }

        internal sealed class StartupSerializeMarker09 { }

        internal sealed class StartupWallstopDeserializeMarker01 { }

        internal sealed class StartupWallstopDeserializeMarker02 { }

        internal sealed class StartupWallstopDeserializeMarker03 { }

        internal sealed class StartupWallstopDeserializeMarker04 { }

        internal sealed class StartupWallstopDeserializeMarker05 { }

        internal sealed class StartupWallstopDeserializeMarker06 { }

        internal sealed class StartupWallstopDeserializeMarker07 { }

        internal sealed class StartupWallstopDeserializeMarker08 { }

        internal sealed class StartupWallstopDeserializeMarker09 { }

        internal sealed class StartupProtobufDeserializeMarker01 { }

        internal sealed class StartupProtobufDeserializeMarker02 { }

        internal sealed class StartupProtobufDeserializeMarker03 { }

        internal sealed class StartupProtobufDeserializeMarker04 { }

        internal sealed class StartupProtobufDeserializeMarker05 { }

        internal sealed class StartupProtobufDeserializeMarker06 { }

        internal sealed class StartupProtobufDeserializeMarker07 { }

        internal sealed class StartupProtobufDeserializeMarker08 { }

        internal sealed class StartupProtobufDeserializeMarker09 { }
    }
}
