// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Runtime.Random
{
    using System;
    using System.Reflection;
    using System.Security.Cryptography;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Random;

    [TestFixture]
    [Category("Fast")]
    public sealed class RandomDispatchCompatibilityTests
    {
        private const int DoubleSampleCount = 100_000;

        private static IRandom Create(string generator, Guid seed)
        {
            byte[] seedBytes = seed.ToByteArray();
            int integerSeed =
                seedBytes[0] | (seedBytes[1] << 8) | (seedBytes[2] << 16) | (seedBytes[3] << 24);
            switch (generator)
            {
                case nameof(BlastCircuitRandom):
                    return new BlastCircuitRandom(seed);
                case nameof(DotNetRandom):
                    return new DotNetRandom(seed);
                case nameof(FlurryBurstRandom):
                    return new FlurryBurstRandom(seed);
                case nameof(IllusionFlow):
                    return new IllusionFlow(seed);
                case nameof(LinearCongruentialGenerator):
                    return new LinearCongruentialGenerator(seed);
                case nameof(PcgRandom):
                    return new PcgRandom(seed);
                case nameof(PhotonSpinRandom):
                    return new PhotonSpinRandom(seed);
                case nameof(RomuDuo):
                    return new RomuDuo(seed);
                case nameof(Sfc64Random):
                    return new Sfc64Random(seed);
                case nameof(SplitMix64):
                    return new SplitMix64(seed);
                case nameof(SquirrelRandom):
                    return new SquirrelRandom(integerSeed);
                case nameof(StormDropRandom):
                    return new StormDropRandom(seed);
                case nameof(SystemRandom):
                    return new SystemRandom(integerSeed);
                case nameof(WaveSplatRandom):
                    return new WaveSplatRandom(seed);
                case nameof(WDoomRandom):
                    return new WDoomRandom(integerSeed);
                case nameof(WyRandom):
                    return new WyRandom(seed);
                case nameof(XoroShiroRandom):
                    return new XoroShiroRandom(seed);
                case nameof(XorShiftRandom):
                    return new XorShiftRandom(seed);
                case nameof(Xoshiro128StarStar):
                    return new Xoshiro128StarStar(seed);
                case nameof(Xoshiro256StarStar):
                    return new Xoshiro256StarStar(seed);
                default:
                    Assert.Fail($"Missing constructor for frozen stream {generator}.");
                    return null;
            }
        }

        // Recorded from the pre-override sources; these hashes must never be regenerated from a candidate.
        [TestCase(
            nameof(BlastCircuitRandom),
            "00010203-0405-0607-0809-0a0b0c0d0e0f",
            "da09969ac6c7e7e2a915cf3a34946aa2d052a2cb2e5f1d6a2c1c9ca075bdd447"
        )]
        [TestCase(
            nameof(BlastCircuitRandom),
            "12345678-1234-1234-1234-123456789012",
            "964aa6311fed92d2863fa20194d2fd55b3890394bda8b772ac7d78010d480f21"
        )]
        [TestCase(
            nameof(DotNetRandom),
            "00010203-0405-0607-0809-0a0b0c0d0e0f",
            "b81233d421bc33f5d28cae61484079889dc90ba0cd636453aa5f96e703318137"
        )]
        [TestCase(
            nameof(DotNetRandom),
            "12345678-1234-1234-1234-123456789012",
            "52917fcb8ad5cd52995994e4698f214d05b1b4337745c36462f73f413b67d5cc"
        )]
        [TestCase(
            nameof(FlurryBurstRandom),
            "00010203-0405-0607-0809-0a0b0c0d0e0f",
            "2fd6beb50bee585428f82f5fa7edbc3feab362855a911cca69087cc29f7367f4"
        )]
        [TestCase(
            nameof(FlurryBurstRandom),
            "12345678-1234-1234-1234-123456789012",
            "684ba1b6bd3f756080438aacee7521165c6dfb9ebaa793c54ece7a18811a06a6"
        )]
        [TestCase(
            nameof(IllusionFlow),
            "00010203-0405-0607-0809-0a0b0c0d0e0f",
            "9c144fc9af3f94c005aba25888a923a6fb6e880dd053f4c63d360dcaf67b06db"
        )]
        [TestCase(
            nameof(IllusionFlow),
            "12345678-1234-1234-1234-123456789012",
            "953c9a792bd8557d4aaf41364142ff45358cac266d12f3e1bbaf62ec09b780e1"
        )]
        [TestCase(
            nameof(LinearCongruentialGenerator),
            "00010203-0405-0607-0809-0a0b0c0d0e0f",
            "b9a849cc302f48d955d7788753267e0df4f75f8fa440bd7f0a1b88f89f02032b"
        )]
        [TestCase(
            nameof(LinearCongruentialGenerator),
            "12345678-1234-1234-1234-123456789012",
            "276604e15b6709e46b7760499f02520ce58ab5367d3264402b206368f093b658"
        )]
        [TestCase(
            nameof(PcgRandom),
            "00010203-0405-0607-0809-0a0b0c0d0e0f",
            "5a3677d47252d60f5221a7edaa14eb24a40315a1b51313c07f64f74749521abc"
        )]
        [TestCase(
            nameof(PcgRandom),
            "12345678-1234-1234-1234-123456789012",
            "7892c5c1ea630c4ebefb0bf356e184bdbfca4d914709365364394d522d8588ab"
        )]
        [TestCase(
            nameof(PhotonSpinRandom),
            "00010203-0405-0607-0809-0a0b0c0d0e0f",
            "4c82a4e0fb7aa361ec26c0ac51a8e3d9194c761843c58aa1d3e00f3c46bf3f40"
        )]
        [TestCase(
            nameof(PhotonSpinRandom),
            "12345678-1234-1234-1234-123456789012",
            "a18087de91f5866b6347804493be7de3820702147c36da882345e98f7a8f71fd"
        )]
        [TestCase(
            nameof(RomuDuo),
            "00010203-0405-0607-0809-0a0b0c0d0e0f",
            "9c3a4616a63bd2a9f068e3798f791764b66f9edd408ec97c03976d192f1aface"
        )]
        [TestCase(
            nameof(RomuDuo),
            "12345678-1234-1234-1234-123456789012",
            "17ec80a92e214b26b4f3f4d32ef7188a424b90e57eeefa9eb81982b96ec4a701"
        )]
        [TestCase(
            nameof(Sfc64Random),
            "00010203-0405-0607-0809-0a0b0c0d0e0f",
            "e01f3aa26569f32f058aa45abaa850733d73b55ca1c4690e84e1c040e814a984"
        )]
        [TestCase(
            nameof(Sfc64Random),
            "12345678-1234-1234-1234-123456789012",
            "092d23d7a4f3c1d94273ddc24dc16307b2b9b411451973ea03c60a643afd9685"
        )]
        [TestCase(
            nameof(SplitMix64),
            "00010203-0405-0607-0809-0a0b0c0d0e0f",
            "bf99bc287289bab70d34ae4e35eeb15bec361a13752e09ed3161ea1fb6c4b528"
        )]
        [TestCase(
            nameof(SplitMix64),
            "12345678-1234-1234-1234-123456789012",
            "4e555e7df15270ee8df3c0516d4a983744f1cea1b9162b8f5d92ab48e7fb0e46"
        )]
        [TestCase(
            nameof(SquirrelRandom),
            "00010203-0405-0607-0809-0a0b0c0d0e0f",
            "2ca6b0dd69bdf944c911b5cdc997c621cab494ed446ca007bf67fea69d3d5d36"
        )]
        [TestCase(
            nameof(SquirrelRandom),
            "12345678-1234-1234-1234-123456789012",
            "85edeeccc72ca590c96125eef082f5e705afa142bb5d4deed8b8abf9119e6afa"
        )]
        [TestCase(
            nameof(StormDropRandom),
            "00010203-0405-0607-0809-0a0b0c0d0e0f",
            "8a46e6e8eba70309f9f8d61499c6f83e2b77a6123dff79c95fad1571dd55a460"
        )]
        [TestCase(
            nameof(StormDropRandom),
            "12345678-1234-1234-1234-123456789012",
            "2c9838088fe9a796d81d1bea45d9031a60f942183e6cbc9cec6c268a644548b7"
        )]
        [TestCase(
            nameof(SystemRandom),
            "00010203-0405-0607-0809-0a0b0c0d0e0f",
            "434fde3a9db7e5dbd1b2c99b5fd5e42e5485a957b8b77702c0247761f1db83b0"
        )]
        [TestCase(
            nameof(SystemRandom),
            "12345678-1234-1234-1234-123456789012",
            "c6dbc8f47444373f409bda54b898f3452ea4958eb8eb19a2528ff48a1c37a0e0"
        )]
        [TestCase(
            nameof(WaveSplatRandom),
            "00010203-0405-0607-0809-0a0b0c0d0e0f",
            "cf850549498f86e1a7445af9576339158cc6680ccffe670fd9d2880ad29fd5d5"
        )]
        [TestCase(
            nameof(WaveSplatRandom),
            "12345678-1234-1234-1234-123456789012",
            "98fd143019a11b6feb5c1f0bfa56bdb1d0705ee516074195132cf40a304a7302"
        )]
        [TestCase(
            nameof(WDoomRandom),
            "00010203-0405-0607-0809-0a0b0c0d0e0f",
            "db6a1dbf4a141d3131f3532bae8e30b9ac44d6388e8b44d6d863379030e6c7ef"
        )]
        [TestCase(
            nameof(WDoomRandom),
            "12345678-1234-1234-1234-123456789012",
            "80b96804accc83d57fa52f8619106129eaa7ca1729201f69cedf59a7cea57080"
        )]
        [TestCase(
            nameof(WyRandom),
            "00010203-0405-0607-0809-0a0b0c0d0e0f",
            "bf80233b750c421e4ff2b80ba2afa701a893e5576c89da96fd521cd9e2209aa7"
        )]
        [TestCase(
            nameof(WyRandom),
            "12345678-1234-1234-1234-123456789012",
            "e7bc64be212471795cbf7058404584e6a258a3498590d6efdab81bfc57eb4d22"
        )]
        [TestCase(
            nameof(XoroShiroRandom),
            "00010203-0405-0607-0809-0a0b0c0d0e0f",
            "d29015bf2052c4b7887240d39e6f6e8e7dbc96e764ab16760e63bd9448cea32c"
        )]
        [TestCase(
            nameof(XoroShiroRandom),
            "12345678-1234-1234-1234-123456789012",
            "1bc2e773706a2c9d39286b7d058305ed9871088b04e0d541aeb7d3da97554dd4"
        )]
        [TestCase(
            nameof(XorShiftRandom),
            "00010203-0405-0607-0809-0a0b0c0d0e0f",
            "fdb6d7fc1ab704a3f56e9d6ee32ad26474d1cb43108b12d3dcbccd5d708422e4"
        )]
        [TestCase(
            nameof(XorShiftRandom),
            "12345678-1234-1234-1234-123456789012",
            "6adf16e3a12f8fb47f1b215f5d886152d497103e9362c445c66f94603fe27ac5"
        )]
        [TestCase(
            nameof(Xoshiro128StarStar),
            "00010203-0405-0607-0809-0a0b0c0d0e0f",
            "cce958f3ee625fb8acd477ab1d2f32a1498a03847a3cc4f04a399684f758606a"
        )]
        [TestCase(
            nameof(Xoshiro128StarStar),
            "12345678-1234-1234-1234-123456789012",
            "382bd8afd8094b7eb0e8934f1fac9297e2c346638a4aa54824a75cf6e7d71e5d"
        )]
        [TestCase(
            nameof(Xoshiro256StarStar),
            "00010203-0405-0607-0809-0a0b0c0d0e0f",
            "2676f0d3206054236383566649869bc60f8acdef6bad820a2c3254b18c298175"
        )]
        [TestCase(
            nameof(Xoshiro256StarStar),
            "12345678-1234-1234-1234-123456789012",
            "e4388ba67b1cc0202905039713e8b2dce3d8f6d360ab9d38018a9f1d4e07bb3a"
        )]
        public void NextDoubleMatchesFrozenBaseline(
            string generator,
            string seedText,
            string expectedHash
        )
        {
            IRandom random = Create(generator, new Guid(seedText));
            byte[] bytes = new byte[DoubleSampleCount * sizeof(double)];
            for (int index = 0; index < DoubleSampleCount; ++index)
            {
                long bits = BitConverter.DoubleToInt64Bits(random.NextDouble());
                int offset = index * sizeof(double);
                for (int byteIndex = 0; byteIndex < sizeof(double); ++byteIndex)
                {
                    bytes[offset + byteIndex] = unchecked((byte)(bits >> (byteIndex * 8)));
                }
            }

            using SHA256 hash = SHA256.Create();
            Assert.IsTrue(hash != null);
            string actualHash = BitConverter
                .ToString(hash.ComputeHash(bytes))
                .Replace("-", "")
                .ToLowerInvariant();
            Assert.AreEqual(expectedHash, actualHash, $"{generator}/{seedText}");
        }

        [TestCase(typeof(BlastCircuitRandom))]
        [TestCase(typeof(DotNetRandom))]
        [TestCase(typeof(FlurryBurstRandom))]
        [TestCase(typeof(IllusionFlow))]
        [TestCase(typeof(LinearCongruentialGenerator))]
        [TestCase(typeof(PcgRandom))]
        [TestCase(typeof(PhotonSpinRandom))]
        [TestCase(typeof(RomuDuo))]
        [TestCase(typeof(Sfc64Random))]
        [TestCase(typeof(SplitMix64))]
        [TestCase(typeof(SquirrelRandom))]
        [TestCase(typeof(StormDropRandom))]
        [TestCase(typeof(SystemRandom))]
        [TestCase(typeof(UnityRandom))]
        [TestCase(typeof(WaveSplatRandom))]
        [TestCase(typeof(WDoomRandom))]
        [TestCase(typeof(WyRandom))]
        [TestCase(typeof(XoroShiroRandom))]
        [TestCase(typeof(XorShiftRandom))]
        [TestCase(typeof(Xoshiro128StarStar))]
        [TestCase(typeof(Xoshiro256StarStar))]
        public void SealedGeneratorsDeclareDispatchOverrides(Type generatorType)
        {
            MethodInfo nextDouble = generatorType.GetMethod(
                nameof(IRandom.NextDouble),
                Type.EmptyTypes
            );
            MethodInfo nextUlong = generatorType.GetMethod(
                nameof(IRandom.NextUlong),
                Type.EmptyTypes
            );
            Assert.IsTrue(nextDouble != null);
            Assert.IsTrue(nextUlong != null);
            Assert.AreEqual(generatorType, nextDouble.DeclaringType, nextDouble.Name);
            Assert.AreEqual(generatorType, nextUlong.DeclaringType, nextUlong.Name);
        }
    }
}
