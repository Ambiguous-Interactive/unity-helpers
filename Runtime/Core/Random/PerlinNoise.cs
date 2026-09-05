// MIT License - Copyright (c) 2024 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE
//
// The algorithm in this file is Perlin noise (Improved Noise), by Ken Perlin. The default permutation
// table and the fade and gradient functions come from his reference implementation. This is an
// adaptation of that work; the design is the original author's. The upstream license is not recorded
// in this repository. See docs/project/third-party-notices.md.

namespace WallstopStudios.UnityHelpers.Core.Random
{
    using System;
    using Extension;
    using UnityEngine;

    public sealed class PerlinNoise
    {
        private static readonly int[] DefaultPermutations =
        {
            151,
            160,
            137,
            91,
            90,
            15,
            131,
            13,
            201,
            95,
            96,
            53,
            194,
            233,
            7,
            225,
            140,
            36,
            103,
            30,
            69,
            142,
            8,
            99,
            37,
            240,
            21,
            10,
            23,
            190,
            6,
            148,
            247,
            120,
            234,
            75,
            0,
            26,
            197,
            62,
            94,
            252,
            219,
            203,
            117,
            35,
            11,
            32,
            57,
            177,
            33,
            88,
            237,
            149,
            56,
            87,
            174,
            20,
            125,
            136,
            171,
            168,
            68,
            175,
            74,
            165,
            71,
            134,
            139,
            48,
            27,
            166,
            77,
            146,
            158,
            231,
            83,
            111,
            229,
            122,
            60,
            211,
            133,
            230,
            220,
            105,
            92,
            41,
            55,
            46,
            245,
            40,
            244,
            102,
            143,
            54,
            65,
            25,
            63,
            161,
            1,
            216,
            80,
            73,
            209,
            76,
            132,
            187,
            208,
            89,
            18,
            169,
            200,
            196,
            135,
            130,
            116,
            188,
            159,
            86,
            164,
            100,
            109,
            198,
            173,
            186,
            3,
            64,
            52,
            217,
            226,
            250,
            124,
            123,
            5,
            202,
            38,
            147,
            118,
            126,
            255,
            82,
            85,
            212,
            207,
            206,
            59,
            227,
            47,
            16,
            58,
            17,
            182,
            189,
            28,
            42,
            223,
            183,
            170,
            213,
            119,
            248,
            152,
            2,
            44,
            154,
            163,
            70,
            221,
            153,
            101,
            155,
            167,
            43,
            172,
            9,
            129,
            22,
            39,
            253,
            19,
            98,
            108,
            110,
            79,
            113,
            224,
            232,
            178,
            185,
            112,
            104,
            218,
            246,
            97,
            228,
            251,
            34,
            242,
            193,
            238,
            210,
            144,
            12,
            191,
            179,
            162,
            241,
            81,
            51,
            145,
            235,
            249,
            14,
            239,
            107,
            49,
            192,
            214,
            31,
            181,
            199,
            106,
            157,
            184,
            84,
            204,
            176,
            115,
            121,
            50,
            45,
            127,
            4,
            150,
            254,
            138,
            236,
            205,
            93,
            222,
            114,
            67,
            29,
            24,
            72,
            243,
            141,
            128,
            195,
            78,
            66,
            215,
            61,
            156,
            180,
        };

        public static readonly PerlinNoise Instance = new();

        private readonly int[] _permutations = new int[DefaultPermutations.Length];

        // Duplicate the permutation so wrapped neighbor lookups need no second mask.
        private readonly int[] _doubledPermutations = new int[DefaultPermutations.Length * 2];

        public PerlinNoise()
            : this(null) { }

        public PerlinNoise(IRandom random)
        {
            Array.Copy(DefaultPermutations, 0, _permutations, 0, DefaultPermutations.Length);
            if (random != null)
            {
                _permutations.Shuffle(random);
            }
            for (int i = 0; i < _doubledPermutations.Length; ++i)
            {
                _doubledPermutations[i] = _permutations[i % _permutations.Length];
            }
        }

        // Perlin fade smooths interpolation at integer-coordinate boundaries.
        public static float Fade(float t)
        {
            return t * t * t * (t * (t * 6 - 15) + 10);
        }

        public static float Lerp(float t, float a, float b)
        {
            return a + t * (b - a);
        }

        public static float Grad(int hash, float x, float y)
        {
            int h = hash & 7;
            float u = h < 4 ? x : y;
            float v = h < 4 ? y : x;
            return ((h & 1) == 0 ? u : -u) + ((h & 2) == 0 ? v : -v);
        }

        public float Noise(float x, float y)
        {
            int clampedX = (int)Mathf.Floor(x) & 255;
            int clampedY = (int)Mathf.Floor(y) & 255;

            x -= Mathf.Floor(x);
            y -= Mathf.Floor(y);

            float u = Fade(x);
            float v = Fade(y);

            int aa = _doubledPermutations[_doubledPermutations[clampedX] + clampedY];
            int ab = _doubledPermutations[_doubledPermutations[clampedX] + clampedY + 1];
            int ba = _doubledPermutations[_doubledPermutations[clampedX + 1] + clampedY];
            int bb = _doubledPermutations[_doubledPermutations[clampedX + 1] + clampedY + 1];

            float res = Lerp(
                v,
                Lerp(u, Grad(aa, x, y), Grad(ba, x - 1, y)),
                Lerp(u, Grad(ab, x, y - 1), Grad(bb, x - 1, y - 1))
            );

            return (res + 1.0f) / 2.0f;
        }

        public float OctaveNoise(float x, float y, int octaves, float persistence)
        {
            float total = 0;
            float frequency = 1;
            float amplitude = 1;
            float maxValue = 0;

            for (int i = 0; i < octaves; ++i)
            {
                total += Noise(x * frequency, y * frequency) * amplitude;

                maxValue += amplitude;

                amplitude *= persistence;
                frequency *= 2;
            }

            return total / maxValue;
        }
    }
}
