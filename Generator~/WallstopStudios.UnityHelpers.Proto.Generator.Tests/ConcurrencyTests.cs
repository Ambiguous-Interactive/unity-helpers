// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>
    /// The lazily-resolved statics, exercised from several threads at once.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These caches are read on every field of every message, so they are the one place in the
    /// serializer where a data race is both cheap to introduce and expensive to notice: a thread that
    /// sees "already resolved" alongside a not-yet-written formatter falls through to the message
    /// path and writes a length-delimited field where the oracle writes a varint. Wrong bytes, no
    /// exception, and only under load.
    /// </para>
    /// <para>
    /// The threads spin on a volatile flag rather than meeting at a <c>Barrier</c>. A barrier
    /// releases its participants through the scheduler one at a time, which SCATTERS them and hides
    /// exactly the window being tested -- a lesson this suite has already paid for once.
    /// </para>
    /// </remarks>
    [TestFixture]
    public sealed class ConcurrencyTests
    {
        private const int Threads = 8;
        private const int Iterations = 400;

        [Test]
        public void RacingTheFirstResolveNeverProducesTheWrongEncoding()
        {
            const string Expected = "0801";

            for (int iteration = 0; iteration < Iterations; iteration++)
            {
                WProtoGeneric<int>.Reset();

                bool go = false;
                string[] results = new string[Threads];
                Exception[] failures = new Exception[Threads];
                Thread[] workers = new Thread[Threads];

                for (int index = 0; index < Threads; index++)
                {
                    int slot = index;
                    workers[slot] = new Thread(() =>
                    {
                        while (!Volatile.Read(ref go))
                        {
                            Thread.SpinWait(1);
                        }

                        try
                        {
                            results[slot] = Encode(new Box<int> { Value = 1 });
                        }
                        catch (Exception error)
                        {
                            failures[slot] = error;
                        }
                    });
                    workers[slot].Start();
                }

                Volatile.Write(ref go, true);
                foreach (Thread worker in workers)
                {
                    worker.Join();
                }

                for (int index = 0; index < Threads; index++)
                {
                    Assert.IsNull(
                        failures[index],
                        "iteration " + iteration + " thread " + index + " threw: " + failures[index]
                    );
                    Assert.AreEqual(
                        Expected,
                        results[index],
                        "iteration " + iteration + " thread " + index + " encoded wrongly"
                    );
                }
            }
        }

        [Test]
        public void RacingResolversAgreeOnTheResolvedShape()
        {
            for (int iteration = 0; iteration < Iterations; iteration++)
            {
                WProtoGeneric<int>.Reset();

                bool go = false;
                List<string> observations = new List<string>();
                Thread[] workers = new Thread[Threads];

                for (int index = 0; index < Threads; index++)
                {
                    workers[index] = new Thread(() =>
                    {
                        while (!Volatile.Read(ref go))
                        {
                            Thread.SpinWait(1);
                        }

                        string seen =
                            WProtoGeneric<int>.WireType + "/" + WProtoGeneric<int>.Packable;
                        lock (observations)
                        {
                            observations.Add(seen);
                        }
                    });
                    workers[index].Start();
                }

                Volatile.Write(ref go, true);
                foreach (Thread worker in workers)
                {
                    worker.Join();
                }

                CollectionAssert.AreEqual(
                    Repeat("0/True", Threads),
                    observations,
                    "iteration "
                        + iteration
                        + " saw a torn resolution: "
                        + string.Join(", ", observations)
                );
            }
        }

        private static List<string> Repeat(string value, int count)
        {
            List<string> all = new List<string>(count);
            for (int index = 0; index < count; index++)
            {
                all.Add(value);
            }

            return all;
        }

        private static string Encode<T>(T value)
        {
            IWProtoFormatter<T> formatter = WProtoFormatterProvider.Get<T>();
            byte[] buffer = new byte[formatter.Measure(value)];
            WProtoWriter writer = new WProtoWriter(buffer);
            if (!formatter.Write(ref writer, value))
            {
                return "<write failed>";
            }

            System.Text.StringBuilder hex = new System.Text.StringBuilder(buffer.Length * 2);
            foreach (byte current in buffer)
            {
                hex.Append(current.ToString("X2"));
            }

            return hex.ToString();
        }
    }
}
