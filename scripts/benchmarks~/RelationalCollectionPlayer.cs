// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Benchmarks
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Core.DataStructure;
    using WallstopStudios.UnityHelpers.Tests.Core.TestTypes;

    internal static class RelationalCollectionPlayer
    {
        private static readonly List<string> Samples = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Run()
        {
            try
            {
                string[] arguments = Environment.GetCommandLineArgs();
                int outputIndex = Array.IndexOf(arguments, "-uhBenchmarkOutput");
                if (outputIndex < 0 || arguments.Length <= outputIndex + 1)
                {
                    throw new InvalidOperationException("Missing benchmark output path.");
                }
                Stopwatch control = Stopwatch.StartNew();
                System.Threading.Thread.Sleep(20);
                control.Stop();
                if (!Stopwatch.IsHighResolution || control.Elapsed.TotalMilliseconds < 10)
                {
                    throw new InvalidOperationException("The timer control failed.");
                }
#if ENABLE_IL2CPP
                const string backend = "IL2CPP";
#else
                const string backend = "Mono";
#endif
                Samples.Add(
                    $"environment unity={Application.unityVersion} backend={backend} frequency={Stopwatch.Frequency} sleepMilliseconds={control.Elapsed.TotalMilliseconds.ToString("R", CultureInfo.InvariantCulture)}"
                );
                foreach (bool sealedType in new[] { false, true })
                {
                    foreach (bool deep in new[] { false, true })
                    {
                        foreach (int count in new[] { 0, 1, 4, 16, 64, 500 })
                        {
                            MeasureChildren(count, deep, sealedType);
                        }
                    }
                }
                foreach (
                    (int hint, int count) in new[]
                    {
                        (64, 1),
                        (64, 7),
                        (64, 8),
                        (64, 64),
                        (1024, 1),
                        (1024, 64),
                        (1024, 1024),
                        (16384, 1),
                        (16384, 64),
                        (16384, 1024),
                    }
                )
                {
                    IntMap<int> map = new(hint);
                    Measure(
                        () => Refill(map, count),
                        $"intmap hint={hint} count={count} capacity={map.Capacity}"
                    );
                    if (map.Count != count)
                    {
                        throw new InvalidOperationException("Map cardinality changed.");
                    }
                    for (int key = 1; key <= count; ++key)
                    {
                        if (!map.TryGet(key, out int value) || value != key)
                        {
                            throw new InvalidOperationException("Map contents changed.");
                        }
                    }
                }
                File.WriteAllLines(arguments[outputIndex + 1], Samples);
                UnityEngine.Debug.Log($"Benchmark passed: {Samples.Count - 1} samples.");
                Application.Quit(0);
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogException(exception);
                Application.Quit(1);
            }
        }

        private static void MeasureChildren(int count, bool deep, bool sealedType)
        {
            GameObject root = new("RelationalCollectionPlayer");
            try
            {
                RelationalChildCollectionTarget target = sealedType
                    ? null
                    : root.AddComponent<RelationalChildCollectionTarget>();
                RelationalChildSealedCollectionTarget sealedTarget = sealedType
                    ? root.AddComponent<RelationalChildSealedCollectionTarget>()
                    : null;
                Component owner = sealedType ? sealedTarget : target;
                Type elementType = sealedType ? typeof(SpriteRenderer) : typeof(BoxCollider);
                Transform parent = root.transform;
                List<Component> expected = new();
                for (int index = 0; index < count; ++index)
                {
                    GameObject child = new($"Child{index}", elementType);
                    child.transform.SetParent(parent);
                    expected.Add(child.GetComponent(elementType));
                    if (deep)
                    {
                        parent = child.transform;
                    }
                }
                Measure(
                    () => owner.AssignChildComponents(),
                    $"child count={count} deep={deep} sealed={sealedType}"
                );
                for (int index = 0; index < count; ++index)
                {
                    Component arrayValue = sealedType
                        ? sealedTarget.array[index]
                        : target.array[index];
                    Component listValue = sealedType
                        ? sealedTarget.list[index]
                        : target.list[index];
                    if (arrayValue != expected[index] || listValue != expected[index])
                    {
                        throw new InvalidOperationException(
                            "Collection order or identity changed."
                        );
                    }
                }
                int arrayCount = sealedType ? sealedTarget.array.Length : target.array.Length;
                int listCount = sealedType ? sealedTarget.list.Count : target.list.Count;
                int setCount = sealedType ? sealedTarget.set.Count : target.set.Count;
                if (arrayCount != count || listCount != count || setCount != count)
                {
                    throw new InvalidOperationException("Collection cardinality changed.");
                }
                foreach (Component component in expected)
                {
                    bool contains = sealedType
                        ? sealedTarget.set.Contains((SpriteRenderer)component)
                        : target.set.Contains((BoxCollider)component);
                    if (!contains)
                    {
                        throw new InvalidOperationException("Set contents changed.");
                    }
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void Refill(IntMap<int> map, int count)
        {
            map.Clear();
            for (int key = 1; key <= count; ++key)
            {
                if (!map.TrySet(key, key))
                {
                    throw new InvalidOperationException("Insertion failed.");
                }
            }
        }

        private static void Measure(Action operation, string label)
        {
            for (int warmup = 0; warmup < 64; ++warmup)
            {
                operation();
            }
            Stopwatch timer = new();
            int batch = 1;
            while (true)
            {
                timer.Restart();
                for (int iteration = 0; iteration < batch; ++iteration)
                {
                    operation();
                }
                timer.Stop();
                if (20 <= timer.Elapsed.TotalMilliseconds)
                {
                    break;
                }
                if (16_777_216 <= batch)
                {
                    throw new InvalidOperationException(
                        "Calibration did not exceed the timer floor."
                    );
                }
                batch *= 2;
            }
            for (int round = 0; round < 7; ++round)
            {
                timer.Restart();
                for (int iteration = 0; iteration < batch; ++iteration)
                {
                    operation();
                }
                timer.Stop();
                double nanoseconds = timer.Elapsed.TotalSeconds * 1_000_000_000 / batch;
                Samples.Add(
                    $"{label} round={round} batch={batch} ns={nanoseconds.ToString("R", CultureInfo.InvariantCulture)}"
                );
            }
        }
    }
}
