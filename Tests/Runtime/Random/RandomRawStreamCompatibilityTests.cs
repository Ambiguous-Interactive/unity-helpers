// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Runtime.Random
{
    using System;
    using System.Security.Cryptography;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Random;

    [TestFixture]
    [Category("Fast")]
    public sealed class RandomRawStreamCompatibilityTests
    {
        private const int StreamBytes = 1024 * 1024;

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

        // The host gate checks these frozen hashes against raw-stream-vectors.json; never regenerate them from the candidate.
        [TestCase(
            nameof(BlastCircuitRandom),
            32,
            "00010203-0405-0607-0809-0a0b0c0d0e0f",
            "f69beb2f87d3f55600d3cc731735752e56f73e84d6825dc8a0c3eee610e66c9f"
        )]
        [TestCase(
            nameof(BlastCircuitRandom),
            32,
            "12345678-1234-1234-1234-123456789012",
            "067aa58f406c6904197b9fb73125554cc0c7ba1eef291a353da5f9da8b1745dc"
        )]
        [TestCase(
            nameof(BlastCircuitRandom),
            64,
            "00010203-0405-0607-0809-0a0b0c0d0e0f",
            "77573e1ed283dcf2058615b1c0783778f2728631873d3b236cd46bff26c836ad"
        )]
        [TestCase(
            nameof(BlastCircuitRandom),
            64,
            "12345678-1234-1234-1234-123456789012",
            "b5656d450287b3c57cc707fd772c40dde5508804856acf424431e6fc05b4369b"
        )]
        [TestCase(
            nameof(DotNetRandom),
            32,
            "00010203-0405-0607-0809-0a0b0c0d0e0f",
            "319c5db84e7ab7cdcc94ecc4b887d7654fe92bb47ba756e159c9f8ba2f378c43"
        )]
        [TestCase(
            nameof(DotNetRandom),
            32,
            "12345678-1234-1234-1234-123456789012",
            "397a7ec914aa010cb74dc3a36d792b09bc4d6baab48364ddfee3b1ab4a7ddc81"
        )]
        [TestCase(
            nameof(DotNetRandom),
            64,
            "00010203-0405-0607-0809-0a0b0c0d0e0f",
            "8c87b0835704418fb6a9d6b054ef731ad6e08c54408e71a5ab782d2af38182fa"
        )]
        [TestCase(
            nameof(DotNetRandom),
            64,
            "12345678-1234-1234-1234-123456789012",
            "3673774a95f2f5d73bbbed10aca0448925019f7de1c9ed744cdadea47f47a508"
        )]
        [TestCase(
            nameof(FlurryBurstRandom),
            32,
            "00010203-0405-0607-0809-0a0b0c0d0e0f",
            "4b2d711e1ed00ec5114ef3dbc2c4ddc0e5638c68fc6276456c71ce73b1337193"
        )]
        [TestCase(
            nameof(FlurryBurstRandom),
            32,
            "12345678-1234-1234-1234-123456789012",
            "fdab67939ab4a5bebb6bb176d1910a00bb0bf30d7b901d8264fea905cfb26e88"
        )]
        [TestCase(
            nameof(FlurryBurstRandom),
            64,
            "00010203-0405-0607-0809-0a0b0c0d0e0f",
            "1794576cdae9bae668768e04e52c3340fc27ada822771d70687a4227a4814ad9"
        )]
        [TestCase(
            nameof(FlurryBurstRandom),
            64,
            "12345678-1234-1234-1234-123456789012",
            "8df9c108b0968ec000573bef28749694bf88e967c203044105b354744983e21f"
        )]
        [TestCase(
            nameof(IllusionFlow),
            32,
            "00010203-0405-0607-0809-0a0b0c0d0e0f",
            "8999a430a85e310b659ede5edacd0619089e40ffec3becd35361a5d815395eed"
        )]
        [TestCase(
            nameof(IllusionFlow),
            32,
            "12345678-1234-1234-1234-123456789012",
            "f712a3b32b4f002eb680a8d6bd338b0b7972b74788199d715b52e16b4b23deef"
        )]
        [TestCase(
            nameof(IllusionFlow),
            64,
            "00010203-0405-0607-0809-0a0b0c0d0e0f",
            "491221de56af4fcbb7977678286d01bf523518f3c755b3ea8d9bd14c9d555bb7"
        )]
        [TestCase(
            nameof(IllusionFlow),
            64,
            "12345678-1234-1234-1234-123456789012",
            "ea8b53279d9369b01837156ea93aa25f7c39aacf8c88c36c4e5fc27c0199351c"
        )]
        [TestCase(
            nameof(LinearCongruentialGenerator),
            32,
            "00010203-0405-0607-0809-0a0b0c0d0e0f",
            "27bf23ba7feac744c52e959837c3beacfe7edfa5ef027347601c4ac90c2425ea"
        )]
        [TestCase(
            nameof(LinearCongruentialGenerator),
            32,
            "12345678-1234-1234-1234-123456789012",
            "ff37c23885a8b7070bc1f61508e51d0e861848e55ae22e30cf8a04d62bd351b6"
        )]
        [TestCase(
            nameof(LinearCongruentialGenerator),
            64,
            "00010203-0405-0607-0809-0a0b0c0d0e0f",
            "ca631aa1e0dcb32f268e95b084672f2f390f5bbb749acc313f0ad0a723059d77"
        )]
        [TestCase(
            nameof(LinearCongruentialGenerator),
            64,
            "12345678-1234-1234-1234-123456789012",
            "dc460effe866a4995d10f1ca0953e7ef556961c7334fcb92fe46291c12a1036c"
        )]
        [TestCase(
            nameof(PcgRandom),
            32,
            "00010203-0405-0607-0809-0a0b0c0d0e0f",
            "11aab2b8e699ef14867699c0d313dbbce22106975b43a61e54cbba1ec3b2e123"
        )]
        [TestCase(
            nameof(PcgRandom),
            32,
            "12345678-1234-1234-1234-123456789012",
            "c6f350197a86b748c62f49eccc05002f187bf72be433c6f19df83b27e2ca54fe"
        )]
        [TestCase(
            nameof(PcgRandom),
            64,
            "00010203-0405-0607-0809-0a0b0c0d0e0f",
            "1c5507b299539910a7dd4c619ed7020a5b23f036c5a8e8212e0b2a130c87b7be"
        )]
        [TestCase(
            nameof(PcgRandom),
            64,
            "12345678-1234-1234-1234-123456789012",
            "d03083de83dc05cd073f9a65f008b809ed0471f856cff25d46d9325a0e0c7018"
        )]
        [TestCase(
            nameof(PhotonSpinRandom),
            32,
            "00010203-0405-0607-0809-0a0b0c0d0e0f",
            "1860e6dfab8763829926cb7b94f49b66c069681fd8916e6c646293617a7790b8"
        )]
        [TestCase(
            nameof(PhotonSpinRandom),
            32,
            "12345678-1234-1234-1234-123456789012",
            "806262d375e31b81d49bdf5cf811186fec64d7b3c6c384b7be5b7cca9ca48087"
        )]
        [TestCase(
            nameof(PhotonSpinRandom),
            64,
            "00010203-0405-0607-0809-0a0b0c0d0e0f",
            "ff9f5d50232181a7028fbc1315846f1d58e8539409d06e6420a7590f3f01fdfa"
        )]
        [TestCase(
            nameof(PhotonSpinRandom),
            64,
            "12345678-1234-1234-1234-123456789012",
            "f660617bd05f4e236965f1d69f58c3234f3eb4f45348f33f2b6480b382ff20ad"
        )]
        [TestCase(
            nameof(RomuDuo),
            32,
            "00010203-0405-0607-0809-0a0b0c0d0e0f",
            "a7acd97e74a5073b8ee6ed804bf31b53fab5983a0a331312e9be2725808da164"
        )]
        [TestCase(
            nameof(RomuDuo),
            32,
            "12345678-1234-1234-1234-123456789012",
            "a31e5272a08c6ccdbc8b4aca3aa8992d4e59be967a2aa40e6dcf59f0fed9d3e7"
        )]
        [TestCase(
            nameof(RomuDuo),
            64,
            "00010203-0405-0607-0809-0a0b0c0d0e0f",
            "f43409409a28c05d65d39cb996adf41c8806e7592ff24d63d686c112bd9a37bc"
        )]
        [TestCase(
            nameof(RomuDuo),
            64,
            "12345678-1234-1234-1234-123456789012",
            "b0f3f5f8f54636cad93bf1adf730c91db4f6d842fda8fc15c482df1404c5a52c"
        )]
        [TestCase(
            nameof(Sfc64Random),
            32,
            "00010203-0405-0607-0809-0a0b0c0d0e0f",
            "a7abe3bc608eb5539e5fc2e49de10d1b58796fe770c9592f9d99dc518119a136"
        )]
        [TestCase(
            nameof(Sfc64Random),
            32,
            "12345678-1234-1234-1234-123456789012",
            "45f105d13a69c0ebd90acdd191d2ec7e2b3b0725a899b59ef391082d00a58043"
        )]
        [TestCase(
            nameof(Sfc64Random),
            64,
            "00010203-0405-0607-0809-0a0b0c0d0e0f",
            "c16e3c5b22145cdbc33929084473c4633927f385fa0c5eb93531b3a7f675fc25"
        )]
        [TestCase(
            nameof(Sfc64Random),
            64,
            "12345678-1234-1234-1234-123456789012",
            "911e32ffd67048fe47d0514f52f4851e87dff8d0f645a9bdcb2d145a020052a3"
        )]
        [TestCase(
            nameof(SplitMix64),
            32,
            "00010203-0405-0607-0809-0a0b0c0d0e0f",
            "68c10f88c109a3c0738f178bdcd5c341bbda86f049c39ac20e55f7219fe442a2"
        )]
        [TestCase(
            nameof(SplitMix64),
            32,
            "12345678-1234-1234-1234-123456789012",
            "9c956658f43f44a8e64351dbb60e9167f1ec772b21d7c7af85cffa67ac41ded7"
        )]
        [TestCase(
            nameof(SplitMix64),
            64,
            "00010203-0405-0607-0809-0a0b0c0d0e0f",
            "a505b1fa22c24c7ec1b548eb4536b66f4d3bed7f649830d2bcbc6b7b939db718"
        )]
        [TestCase(
            nameof(SplitMix64),
            64,
            "12345678-1234-1234-1234-123456789012",
            "55c00c7448f1933eb17e16dd6611b1b2db6ad8e1ebf3d1d15900e3339d64728d"
        )]
        [TestCase(
            nameof(SquirrelRandom),
            32,
            "00010203-0405-0607-0809-0a0b0c0d0e0f",
            "fa8b334eace226d7daa313c536647b5b2cbb199ed1491d1fbe7755b64a8b3d6f"
        )]
        [TestCase(
            nameof(SquirrelRandom),
            32,
            "12345678-1234-1234-1234-123456789012",
            "6bfdb226be96e78070a73b0436eb025e1dd36a3006e9b3b15fc6a0d50f663870"
        )]
        [TestCase(
            nameof(SquirrelRandom),
            64,
            "00010203-0405-0607-0809-0a0b0c0d0e0f",
            "76bdddf6797d9805038531b277d74505b29b1d3df0a2680e1a203f348a36cf58"
        )]
        [TestCase(
            nameof(SquirrelRandom),
            64,
            "12345678-1234-1234-1234-123456789012",
            "3771bcfaac404d50e20b46fa0433ff52c90620e2cd35d4ef983da571ab8b82ba"
        )]
        [TestCase(
            nameof(StormDropRandom),
            32,
            "00010203-0405-0607-0809-0a0b0c0d0e0f",
            "eec7e630762440443d1cad27070750e53960fa3c71726e7d014836c4ead4da5c"
        )]
        [TestCase(
            nameof(StormDropRandom),
            32,
            "12345678-1234-1234-1234-123456789012",
            "c072142ec11e9d8069b0e1578ce1244677bbc13ebfc56993622726f9ff238dfd"
        )]
        [TestCase(
            nameof(StormDropRandom),
            64,
            "00010203-0405-0607-0809-0a0b0c0d0e0f",
            "9ccd800fa0ebeb2448d705cf33e50f784e579043a8a9a9ab6d7c6de85f6e2ab7"
        )]
        [TestCase(
            nameof(StormDropRandom),
            64,
            "12345678-1234-1234-1234-123456789012",
            "35c65bccc4f52a421f09f9448cbf1317aa4f43acd946d5cc0b2b6f51e509711d"
        )]
        [TestCase(
            nameof(SystemRandom),
            32,
            "00010203-0405-0607-0809-0a0b0c0d0e0f",
            "c75052d27c16a98aa7e2b7c2c5f3df8534e94bf487f2bffa36a4ad7684f0062b"
        )]
        [TestCase(
            nameof(SystemRandom),
            32,
            "12345678-1234-1234-1234-123456789012",
            "be5e278d99984d9be31f9538595f3ff7a24054198d67c5a0418c17f64367d0dc"
        )]
        [TestCase(
            nameof(SystemRandom),
            64,
            "00010203-0405-0607-0809-0a0b0c0d0e0f",
            "0c879763fb92d66c3f59242b4b6971c6d6ddb49b48cf9187d93629617b913df3"
        )]
        [TestCase(
            nameof(SystemRandom),
            64,
            "12345678-1234-1234-1234-123456789012",
            "bbba483a82331856a146bad80ec1d393d4d9c2ca4bb67e7653c2f4098b2a7f03"
        )]
        [TestCase(
            nameof(WaveSplatRandom),
            32,
            "00010203-0405-0607-0809-0a0b0c0d0e0f",
            "9638be189df075b1a8c6176ffcb9789934b4e9e256dbecadeaaf8df6475ec62d"
        )]
        [TestCase(
            nameof(WaveSplatRandom),
            32,
            "12345678-1234-1234-1234-123456789012",
            "acd2e54fd641f95479e50ddf392d7e0b641a490fbc204ab31516572cbe4164f5"
        )]
        [TestCase(
            nameof(WaveSplatRandom),
            64,
            "00010203-0405-0607-0809-0a0b0c0d0e0f",
            "421313dcefaefe1441a4dc732db542f49e07be550c73344547d454fff521c288"
        )]
        [TestCase(
            nameof(WaveSplatRandom),
            64,
            "12345678-1234-1234-1234-123456789012",
            "fd265484a488e4150b0ad2344c495b2dc5c824899c1043fdeee90f0a961125e5"
        )]
        [TestCase(
            nameof(WDoomRandom),
            32,
            "00010203-0405-0607-0809-0a0b0c0d0e0f",
            "da4aec9dbcf9a0360b905e6aeda4e86b1fbbebb2cc287946b9456eebe4b300c8"
        )]
        [TestCase(
            nameof(WDoomRandom),
            32,
            "12345678-1234-1234-1234-123456789012",
            "a6f6e68ff0ed255b63f81e6d90b171e945969a752a86fadf5871d30140a7aeb2"
        )]
        [TestCase(
            nameof(WDoomRandom),
            64,
            "00010203-0405-0607-0809-0a0b0c0d0e0f",
            "9d7416c0d8ac2a98d2c4195f38e763179dce36d029f1825f31b6e12ba6a3efee"
        )]
        [TestCase(
            nameof(WDoomRandom),
            64,
            "12345678-1234-1234-1234-123456789012",
            "5e1be916efe539b65b852735ea9ba45608a2dba7c95fed53b9432aab1c95963e"
        )]
        [TestCase(
            nameof(WyRandom),
            32,
            "00010203-0405-0607-0809-0a0b0c0d0e0f",
            "0ddeccfde49feb91ffbdef40c29f891b25f6efb4a5a98a5df570a7a7a8bd51f8"
        )]
        [TestCase(
            nameof(WyRandom),
            32,
            "12345678-1234-1234-1234-123456789012",
            "18306baecb97a46fdf9eabc15c7581b3e67bcce1b8a6296ce3b3c9f4eeb95265"
        )]
        [TestCase(
            nameof(WyRandom),
            64,
            "00010203-0405-0607-0809-0a0b0c0d0e0f",
            "0693b4002d2786f2614aaf3ed954d78dc09629031c5fc37653d19462cbe74c1e"
        )]
        [TestCase(
            nameof(WyRandom),
            64,
            "12345678-1234-1234-1234-123456789012",
            "64a9a2c6a4c9f61f06608f030c094334ff996515877e2050ce74826f4ff68cc5"
        )]
        [TestCase(
            nameof(XoroShiroRandom),
            32,
            "00010203-0405-0607-0809-0a0b0c0d0e0f",
            "3d2714b2e6bb322903142d08c1a99ba2b2e210def7338ff688f03335d9b27511"
        )]
        [TestCase(
            nameof(XoroShiroRandom),
            32,
            "12345678-1234-1234-1234-123456789012",
            "ac455fbe03ace2d2f7f53d36ad7b51ab7808d0f85e745f9f6408a18a679e6926"
        )]
        [TestCase(
            nameof(XoroShiroRandom),
            64,
            "00010203-0405-0607-0809-0a0b0c0d0e0f",
            "71364097953f46d3a5ed91cfb3670bfb3b4623c92fdb5e3e6d786869f97e3988"
        )]
        [TestCase(
            nameof(XoroShiroRandom),
            64,
            "12345678-1234-1234-1234-123456789012",
            "9777a9e198e4761bff5ab89f618262c760aebb7c3c53e0bfc40f6ee75c8aad09"
        )]
        [TestCase(
            nameof(XorShiftRandom),
            32,
            "00010203-0405-0607-0809-0a0b0c0d0e0f",
            "98574c025e9532f508443d3b71e04e4d5a39252e6ef0c41f3a6ef3c06a627cb5"
        )]
        [TestCase(
            nameof(XorShiftRandom),
            32,
            "12345678-1234-1234-1234-123456789012",
            "d23e7d7a655f54753e9832dbef671d957bbdce7e703d816412a05f365f8bd36e"
        )]
        [TestCase(
            nameof(XorShiftRandom),
            64,
            "00010203-0405-0607-0809-0a0b0c0d0e0f",
            "d5ed2948816f848a50f9ce88b0afdd6edc0fce650887b99bc53d769142125116"
        )]
        [TestCase(
            nameof(XorShiftRandom),
            64,
            "12345678-1234-1234-1234-123456789012",
            "9f679bd194ce17befc473c1b61fc394a8be41515e36a81e7f392c361cc9b759d"
        )]
        [TestCase(
            nameof(Xoshiro128StarStar),
            32,
            "00010203-0405-0607-0809-0a0b0c0d0e0f",
            "118fd406f2c74f1263a08b7d61020957e7bec9c9483e47b98aadedb267f23711"
        )]
        [TestCase(
            nameof(Xoshiro128StarStar),
            32,
            "12345678-1234-1234-1234-123456789012",
            "4641c67aa8b8b3bd391a4411c397735f7396db08191adbc67002259d0ea41400"
        )]
        [TestCase(
            nameof(Xoshiro128StarStar),
            64,
            "00010203-0405-0607-0809-0a0b0c0d0e0f",
            "4e5703cc4efcf832fc4e1f19209b6016b152e3270b6df13fbbeb43020899d9e3"
        )]
        [TestCase(
            nameof(Xoshiro128StarStar),
            64,
            "12345678-1234-1234-1234-123456789012",
            "1f957f29e62baf90efbbf47110d79b6f4e70b031da8215365dd12a238422e84b"
        )]
        [TestCase(
            nameof(Xoshiro256StarStar),
            32,
            "00010203-0405-0607-0809-0a0b0c0d0e0f",
            "8072f6ae33e090af9973253501481062e698ab7ab3805c3e7d34be46b79dd6fb"
        )]
        [TestCase(
            nameof(Xoshiro256StarStar),
            32,
            "12345678-1234-1234-1234-123456789012",
            "ed746d25042efc202f05193e54dbce6de7f552934ed3746189a7c2df736c5c6b"
        )]
        [TestCase(
            nameof(Xoshiro256StarStar),
            64,
            "00010203-0405-0607-0809-0a0b0c0d0e0f",
            "50b674ded19fc804629a2b70bfb07d6b7c522a74a430b7f81b93af5e5dee2c9d"
        )]
        [TestCase(
            nameof(Xoshiro256StarStar),
            64,
            "12345678-1234-1234-1234-123456789012",
            "0a470a15bf2d97b06abd6244ca3256089720f9702a6b6c61f46751891a9131a5"
        )]
        public void RawStreamMatchesFrozenBaseline(
            string generator,
            int width,
            string seedText,
            string expectedHash
        )
        {
            Guid seed = new(seedText);
            IRandom random = Create(generator, seed);
            byte[] bytes = new byte[StreamBytes];
            int sampleBytes = width / 8;
            for (int offset = 0; offset < bytes.Length; offset += sampleBytes)
            {
                ulong sample = width == 32 ? random.NextUint() : random.NextUlong();
                for (int index = 0; index < sampleBytes; ++index)
                {
                    bytes[offset + index] = unchecked((byte)(sample >> (index * 8)));
                }
            }

            using SHA256 hash = SHA256.Create();
            Assert.IsTrue(hash != null);
            string actualHash = BitConverter
                .ToString(hash.ComputeHash(bytes))
                .Replace("-", "")
                .ToLowerInvariant();
            Assert.AreEqual(expectedHash, actualHash, $"{generator}/{width}/{seedText}");
        }
    }
}
