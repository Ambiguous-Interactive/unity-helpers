// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Utils
{
    using System;
    using System.Diagnostics;
    using System.Globalization;
    using System.Reflection;
    using System.Security.Cryptography;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Utils;

    [TestFixture]
    [Category("Performance")]
    public sealed class SpriteArtBoundsTimingTests : CommonTestBase
    {
        [TestCase(0, false)]
        [TestCase(1, false)]
        [TestCase(2, false)]
        [TestCase(0, true)]
        [TestCase(1, true)]
        [TestCase(2, true)]
        public void CaptureBoundedRowScan(int pattern, bool readSprite)
        {
#if ENABLE_IL2CPP
            Assert.Ignore("This capture requires a Mono method-body identity.");
#endif
            Type subjectType = readSprite ? typeof(Sprite) : typeof(SpriteAlphaMask);
            Type[] parameters =
            {
                subjectType,
                typeof(Bounds).MakeByRefType(),
                typeof(string).MakeByRefType(),
            };
            if (readSprite)
            {
                parameters = new[]
                {
                    subjectType,
                    typeof(Bounds).MakeByRefType(),
                    typeof(string).MakeByRefType(),
                    typeof(byte),
                };
            }
            // Reflection identifies loaded IL; a file hash cannot detect a stale editor image.
            MethodInfo subject = typeof(SpriteArtBounds).GetMethod(
                nameof(SpriteArtBounds.TryGetLocalArtBounds),
                parameters
            );
            Assert.IsTrue(subject != null);
            MethodBody body = subject.GetMethodBody();
            Assert.IsTrue(body != null);
            byte[] instructions = body.GetILAsByteArray();
            Assert.IsTrue(instructions != null);
            using SHA256 algorithm = SHA256.Create();
            byte[] digest = algorithm.ComputeHash(instructions);
            string identity = BitConverter.ToString(digest).Replace("-", string.Empty);
            UnityEngine.Debug.Log(
                $"BOUNDS-IDENTITY sprite={readSprite} mvid={subject.Module.ModuleVersionId} il={identity}"
            );
            if (!Stopwatch.IsHighResolution)
            {
                Assert.Ignore("A high-resolution timer is required for this capture.");
            }
            long controlStart = Stopwatch.GetTimestamp();
            long controlEnd = controlStart;
            for (int spin = 0; spin < 10000 && controlEnd == controlStart; ++spin)
            {
                controlEnd = Stopwatch.GetTimestamp();
            }
            Assert.That(controlEnd, Is.GreaterThan(controlStart));

            const int width = 256;
            const int height = 256;
            bool[] visible = new bool[width * height];
            Color32[] pixels = new Color32[visible.Length];
            for (int row = 0; row < height; ++row)
            {
                for (int column = 0; column < width; ++column)
                {
                    int index = row * width + column;
                    visible[index] = pattern == 0 || pattern == 1 && column == width / 2;
                    pixels[index] = new Color32(0, 0, 0, visible[index] ? (byte)255 : (byte)0);
                }
            }
            Texture2D texture = Track(new Texture2D(width, height, TextureFormat.RGBA32, false));
            texture.SetPixels32(pixels);
            texture.Apply();
            Rect rectangle = new(0, 0, width, height);
            Sprite sprite = Track(Sprite.Create(texture, rectangle, Vector2.zero, 1));
            SpriteAlphaMask mask = new(visible, width, height, Vector2.zero, Vector2.one);
            bool expected = pattern != 2;
            Bounds expectedBounds = pattern switch
            {
                0 => new Bounds(new Vector3(128, 128, 0), new Vector3(256, 256, 0)),
                1 => new Bounds(new Vector3(128.5f, 128, 0), new Vector3(1, 256, 0)),
                _ => default,
            };
            for (int warmup = 0; warmup < 64; ++warmup)
            {
                bool found = readSprite
                    ? SpriteArtBounds.TryGetLocalArtBounds(sprite, out Bounds bounds, out _)
                    : SpriteArtBounds.TryGetLocalArtBounds(mask, out bounds, out _);
                Assert.AreEqual(expected, found);
                Assert.AreEqual(expectedBounds, bounds);
            }

            int calls = 1;
            long elapsed;
            do
            {
                elapsed = Measure(mask, sprite, readSprite, calls, expected);
                if (elapsed < Stopwatch.Frequency / 50 && calls < 1048576)
                {
                    calls *= 2;
                }
                else
                {
                    break;
                }
            } while (true);
            Assert.That(elapsed, Is.GreaterThanOrEqualTo(Stopwatch.Frequency / 50));

            double[] samples = new double[21];
            for (int round = 0; round < samples.Length; ++round)
            {
                long ticks = Measure(mask, sprite, readSprite, calls, expected);
                samples[round] = ticks * (1000000000.0 / Stopwatch.Frequency) / calls;
            }
            string[] values = new string[samples.Length];
            for (int index = 0; index < samples.Length; ++index)
            {
                values[index] = samples[index].ToString("R", CultureInfo.InvariantCulture);
            }
            string rounds = string.Join(",", values);
            UnityEngine.Debug.Log(
                $"BOUNDS-ROWS pattern={pattern} sprite={readSprite} calls={calls} nanoseconds={rounds}"
            );
        }

        private static long Measure(
            SpriteAlphaMask mask,
            Sprite sprite,
            bool readSprite,
            int calls,
            bool expected
        )
        {
            int foundCount = 0;
            long start = Stopwatch.GetTimestamp();
            if (readSprite)
            {
                for (int call = 0; call < calls; ++call)
                {
                    foundCount += SpriteArtBounds.TryGetLocalArtBounds(sprite, out _, out _)
                        ? 1
                        : 0;
                }
            }
            else
            {
                for (int call = 0; call < calls; ++call)
                {
                    foundCount += SpriteArtBounds.TryGetLocalArtBounds(mask, out _, out _) ? 1 : 0;
                }
            }
            long elapsed = Stopwatch.GetTimestamp() - start;
            Assert.AreEqual(expected ? calls : 0, foundCount);
            return elapsed;
        }
    }
}
