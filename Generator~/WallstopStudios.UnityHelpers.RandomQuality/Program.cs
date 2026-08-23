// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.RandomQuality
{
    using System;
    using System.Globalization;
    using System.IO;
    using WallstopStudios.UnityHelpers.Core.Random;

    internal static class Program
    {
        private const int OutputBufferSize = 64 * 1024;
        private const string DefaultSeed = "00010203-0405-0607-0809-0a0b0c0d0e0f";

        private static readonly string[] GeneratorNames =
        {
            nameof(BlastCircuitRandom),
            nameof(DotNetRandom),
            nameof(FlurryBurstRandom),
            nameof(IllusionFlow),
            nameof(LinearCongruentialGenerator),
            nameof(PcgRandom),
            nameof(PhotonSpinRandom),
            nameof(RomuDuo),
            nameof(SplitMix64),
            nameof(SquirrelRandom),
            nameof(StormDropRandom),
            nameof(SystemRandom),
            nameof(WaveSplatRandom),
            nameof(WDoomRandom),
            nameof(WyRandom),
            nameof(XoroShiroRandom),
            nameof(XorShiftRandom),
            nameof(Xoshiro128StarStar),
            nameof(Xoshiro256StarStar),
        };

        public static int Main(string[] args)
        {
            if (HasFlag(args, "--list"))
            {
                Console.Out.WriteLine(string.Join(Environment.NewLine, GeneratorNames));
                return 0;
            }

            if (!TryOption(args, "--generator", out string generatorName))
            {
                return Fail("Missing --generator. Use --list to see the supported names.");
            }

            string seedText = TryOption(args, "--seed", out string suppliedSeed)
                ? suppliedSeed
                : DefaultSeed;
            if (!Guid.TryParse(seedText, out Guid seed))
            {
                return Fail($"Invalid --seed '{seedText}'. Expected a GUID.");
            }

            if (
                !TryOption(args, "--bytes", out string byteCountText)
                || !long.TryParse(
                    byteCountText,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out long byteCount
                )
                || byteCount < 0
            )
            {
                return Fail("Invalid or missing --bytes. Expected a non-negative integer.");
            }

            if (!TryCreate(generatorName, seed, out IRandom random))
            {
                return Fail($"Unknown generator '{generatorName}'. Use --list for exact names.");
            }

            bool sixtyFourBit = false;
            if (TryOption(args, "--width", out string widthText))
            {
                if (string.Equals(widthText, "64", StringComparison.Ordinal))
                {
                    sixtyFourBit = true;
                }
                else if (!string.Equals(widthText, "32", StringComparison.Ordinal))
                {
                    return Fail($"Invalid --width '{widthText}'. Expected 32 or 64.");
                }
            }

            using Stream destination = Console.OpenStandardOutput();
            Write(random, byteCount, sixtyFourBit, destination);
            return 0;
        }

        private static bool TryCreate(string name, Guid seed, out IRandom random)
        {
            int intSeed = SeedInt32(seed);
            switch (name)
            {
                case nameof(BlastCircuitRandom):
                    random = new BlastCircuitRandom(seed);
                    return true;
                case nameof(DotNetRandom):
                    random = new DotNetRandom(seed);
                    return true;
                case nameof(FlurryBurstRandom):
                    random = new FlurryBurstRandom(seed);
                    return true;
                case nameof(IllusionFlow):
                    random = new IllusionFlow(seed);
                    return true;
                case nameof(LinearCongruentialGenerator):
                    random = new LinearCongruentialGenerator(seed);
                    return true;
                case nameof(PcgRandom):
                    random = new PcgRandom(seed);
                    return true;
                case nameof(PhotonSpinRandom):
                    random = new PhotonSpinRandom(seed);
                    return true;
                case nameof(RomuDuo):
                    random = new RomuDuo(seed);
                    return true;
                case nameof(SplitMix64):
                    random = new SplitMix64(seed);
                    return true;
                case nameof(SquirrelRandom):
                    random = new SquirrelRandom(intSeed);
                    return true;
                case nameof(StormDropRandom):
                    random = new StormDropRandom(seed);
                    return true;
                case nameof(SystemRandom):
                    random = new SystemRandom(intSeed);
                    return true;
                case nameof(WaveSplatRandom):
                    random = new WaveSplatRandom(seed);
                    return true;
                case nameof(WDoomRandom):
                    random = new WDoomRandom(intSeed);
                    return true;
                case nameof(WyRandom):
                    random = new WyRandom(seed);
                    return true;
                case nameof(XoroShiroRandom):
                    random = new XoroShiroRandom(seed);
                    return true;
                case nameof(XorShiftRandom):
                    random = new XorShiftRandom(seed);
                    return true;
                case nameof(Xoshiro128StarStar):
                    random = new Xoshiro128StarStar(seed);
                    return true;
                case nameof(Xoshiro256StarStar):
                    random = new Xoshiro256StarStar(seed);
                    return true;
                default:
                    random = null;
                    return false;
            }
        }

        // The 64-bit width exists because NextUlong is not NextUint rearranged for every generator.
        // Five of them answer it from one raw 64-bit word, so half of that word reaches a caller only
        // through NextDouble and NextLong -- bits no 32-bit stream ever carries. Feed this to
        // `RNG_test stdin64`.
        private static void Write(
            IRandom random,
            long byteCount,
            bool sixtyFourBit,
            Stream destination
        )
        {
            byte[] buffer = new byte[OutputBufferSize];
            int sampleWidth = sixtyFourBit ? sizeof(ulong) : sizeof(uint);
            long remaining = byteCount;
            while (0 < remaining)
            {
                int count = (int)Math.Min(buffer.Length, remaining);
                int offset = 0;
                while (offset < count)
                {
                    ulong sample = sixtyFourBit ? random.NextUlong() : random.NextUint();
                    int sampleBytes = Math.Min(sampleWidth, count - offset);
                    for (int index = 0; index < sampleBytes; index++)
                    {
                        buffer[offset + index] = (byte)(sample >> (index * 8));
                    }

                    offset += sampleBytes;
                }

                destination.Write(buffer, 0, count);
                remaining -= count;
            }
        }

        private static int SeedInt32(Guid seed)
        {
            byte[] bytes = seed.ToByteArray();
            return bytes[0] | (bytes[1] << 8) | (bytes[2] << 16) | (bytes[3] << 24);
        }

        private static bool HasFlag(string[] args, string name)
        {
            for (int index = 0; index < args.Length; index++)
            {
                if (string.Equals(args[index], name, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryOption(string[] args, string name, out string value)
        {
            for (int index = 0; index + 1 < args.Length; index++)
            {
                if (string.Equals(args[index], name, StringComparison.Ordinal))
                {
                    value = args[index + 1];
                    return true;
                }
            }

            value = null;
            return false;
        }

        private static int Fail(string message)
        {
            Console.Error.WriteLine(message);
            return 2;
        }
    }
}
