// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Extension
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Text;

    public static class JesseParityHarness
    {
        private const string CandidateHash = "CANDIDATE_SHA_PLACEHOLDER";
        private const string BaselineHash = "BASELINE_SHA_PLACEHOLDER";
        private const string HelpersHash = "HELPERS_SHA_PLACEHOLDER";
        private const string HarnessTemplateHash = "HARNESS_SHA_PLACEHOLDER";
        private const string HarnessMarker = "JesseParityCalibratedV3";
        private const string SourcePin = "1bf1f3d5b719c869880d98443050a836a65f47c1";

        public static string Main(
            string shape,
            int count,
            bool useList,
            int firstBatch,
            int batchCount
        )
        {
            int[] input = MakeInput(shape, count);
            int[] expected = (int[])input.Clone();
            Array.Sort(expected);
            double fastest = double.MaxValue;
            foreach (char arm in "ABCD")
            {
                double warmed = 0;
                while (warmed < 100)
                {
                    double perSort = MeasureCalibrated(
                        input,
                        expected,
                        useList,
                        arm,
                        out int repetitions,
                        out double total
                    );
                    warmed += total;
                    fastest = Math.Min(fastest, perSort);
                }
            }
            int maximumRepetitions = (int)
                Math.Max(1, Math.Min(1048576L, 134217728L / Math.Max(128L, (long)count * 4 + 64)));
            int fixedRepetitions = Math.Max(
                1,
                Math.Min(maximumRepetitions, (int)Math.Ceiling(40 / fastest))
            );
            MeasureCalibrated(
                input,
                expected,
                useList,
                'E',
                out int controlRepetitions,
                out double controlLoop,
                true,
                fixedRepetitions
            );
            StringBuilder output = new();
            output
                .Append("{\"pin\":\"")
                .Append(SourcePin)
                .Append("\",\"candidateSha\":\"")
                .Append(CandidateHash)
                .Append("\",\"harness\":\"")
                .Append(HarnessMarker)
                .Append("\",\"shape\":\"")
                .Append(shape)
                .Append("\",\"count\":")
                .Append(count)
                .Append(",\"list\":")
                .Append(useList ? "true" : "false")
                .Append(",\"baselineSha\":\"")
                .Append(BaselineHash)
                .Append("\",\"helpersSha\":\"")
                .Append(HelpersHash)
                .Append("\",\"harnessTemplateSha\":\"")
                .Append(HarnessTemplateHash)
                .Append("\",\"memoryCapRepetitions\":")
                .Append(maximumRepetitions)
                .Append(",\"fixedRepetitions\":")
                .Append(fixedRepetitions)
                .Append(",\"frequency\":")
                .Append(Stopwatch.Frequency)
                .Append(",\"emptyLoopMs\":")
                .Append(
                    controlLoop.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
                )
                .Append(",\"samples\":[");
            string sequence = "ABBABAABCD";
            bool separator = false;
            for (int batch = firstBatch; batch < firstBatch + batchCount; ++batch)
            {
                for (int position = 0; position < sequence.Length; ++position)
                {
                    char arm = sequence[position];
                    double milliseconds = MeasureCalibrated(
                        input,
                        expected,
                        useList,
                        arm,
                        out int repetitions,
                        out double total,
                        true,
                        fixedRepetitions
                    );
                    if (separator)
                    {
                        output.Append(',');
                    }
                    separator = true;
                    output
                        .Append("{\"batch\":")
                        .Append(batch)
                        .Append(",\"position\":")
                        .Append(position)
                        .Append(",\"arm\":\"")
                        .Append(arm)
                        .Append("\",\"repetitions\":")
                        .Append(repetitions)
                        .Append(",\"slotMs\":")
                        .Append(
                            total.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
                        )
                        .Append(",\"ms\":")
                        .Append(
                            milliseconds.ToString(
                                "R",
                                System.Globalization.CultureInfo.InvariantCulture
                            )
                        )
                        .Append('}');
                }
            }
            output.Append("]}");
            return output.ToString();
        }

        public static string Check()
        {
            int cases = 0;
            string[] shapes =
            {
                "Random",
                "Duplicates",
                "Sorted",
                "Reverse",
                "Equal",
                "OrganPipe",
                "Sawtooth",
                "Alternating",
                "Noise1",
                "Noise5",
                "Noise10",
                "Block32",
                "Block64",
                "Mixed",
                "Runs33",
                "Runs38",
                "Runs39",
            };
            int[] lengths =
            {
                2,
                3,
                4,
                31,
                32,
                33,
                127,
                1023,
                1024,
                1025,
                8191,
                8192,
                10000,
                49999,
                50000,
                100000,
            };
            foreach (string shape in shapes)
            {
                foreach (int length in lengths)
                {
                    int[] input = MakeInput(shape, length);
                    int[] expected = (int[])input.Clone();
                    Array.Sort(expected);
                    Measure(input, expected, false, true);
                    Measure(input, expected, true, true);
                    ++cases;
                }
            }
            return "Verified candidate array/list output in "
                + cases
                + " corpus cases; source "
                + SourcePin;
        }

        private static double Measure(int[] input, int[] expected, bool useList, bool candidate)
        {
            return MeasureCalibrated(
                input,
                expected,
                useList,
                candidate ? 'B' : 'A',
                out int repetitions,
                out double total,
                false
            );
        }

        private static double MeasureCalibrated(
            int[] input,
            int[] expected,
            bool useList,
            char arm,
            out int repetitions,
            out double totalMilliseconds,
            bool calibrate = true,
            int fixedRepetitions = 0
        )
        {
            int maximumRepetitions = (int)
                Math.Max(
                    1,
                    Math.Min(1048576L, 134217728L / Math.Max(128L, (long)input.Length * 4 + 64))
                );
            repetitions =
                fixedRepetitions == 0 ? 1 : Math.Min(fixedRepetitions, maximumRepetitions);
            long minimumTicks = calibrate ? Stopwatch.Frequency / 50 : 1;
            long elapsed;
            do
            {
                IList<int>[] subjects = new IList<int>[repetitions];
                for (int repetition = 0; repetition < repetitions; ++repetition)
                {
                    subjects[repetition] = useList ? new List<int>(input) : (int[])input.Clone();
                }
                Stopwatch timer = Stopwatch.StartNew();
                switch (arm)
                {
                    case 'A':
                        foreach (IList<int> subject in subjects)
                        {
                            JesseBenchmarkOld.JesseSort(subject, Comparer<int>.Default);
                        }
                        break;
                    case 'B':
                        foreach (IList<int> subject in subjects)
                        {
                            JesseBenchmarkCandidate.JesseSort(subject, Comparer<int>.Default);
                        }
                        break;
                    case 'C':
                        if (useList)
                        {
                            foreach (IList<int> subject in subjects)
                            {
                                ((List<int>)subject).Sort(Comparer<int>.Default);
                            }
                        }
                        else
                        {
                            foreach (IList<int> subject in subjects)
                            {
                                Array.Sort((int[])subject, Comparer<int>.Default);
                            }
                        }
                        break;
                    case 'D':
                        foreach (IList<int> subject in subjects)
                        {
                            JesseBenchmarkCandidate.IpnSort(subject, Comparer<int>.Default);
                        }
                        break;
                    case 'E':
                        int totalCount = 0;
                        foreach (IList<int> subject in subjects)
                        {
                            totalCount += subject.Count;
                        }
                        GC.KeepAlive(totalCount);
                        break;
                }
                timer.Stop();
                elapsed = timer.ElapsedTicks;
                if (arm != 'E')
                {
                    foreach (IList<int> subject in subjects)
                    {
                        for (int index = 0; index < expected.Length; ++index)
                        {
                            if (subject[index] != expected[index])
                            {
                                throw new InvalidOperationException(
                                    "Incorrect " + arm + " at " + index
                                );
                            }
                        }
                    }
                }
                if (
                    fixedRepetitions != 0
                    || !calibrate
                    || minimumTicks <= elapsed
                    || maximumRepetitions <= repetitions
                )
                {
                    break;
                }
                repetitions = Math.Min(maximumRepetitions, repetitions * 2);
            } while (true);
            totalMilliseconds = (double)elapsed * 1000 / Stopwatch.Frequency;
            return totalMilliseconds / repetitions;
        }

        private static int[] MakeInput(string shape, int count)
        {
            int[] data = new int[count];
            uint state = 747;
            for (int index = 0; index < count; ++index)
            {
                state = unchecked(state * 1664525U + 1013904223U);
                data[index] = shape switch
                {
                    "Random" => unchecked((int)state),
                    "Duplicates" => (int)(state % 32),
                    "Reverse" => count - index,
                    "Equal" => 7,
                    "OrganPipe" => Math.Min(index, count - index),
                    "Sawtooth" => index % 127,
                    "Alternating" => ((index / 256) & 1) == 0 ? index : count - index,
                    "Mixed" => (index < count / 3) ? index
                    : (index < count * 2 / 3) ? unchecked((int)state)
                    : index % 8,
                    "Runs33" => NaturalRunValue(index, count, 33),
                    "Runs38" => NaturalRunValue(index, count, 38),
                    "Runs39" => NaturalRunValue(index, count, 39),
                    "OverlapRuns33" => OverlapRunValue(index, count, 33),
                    "OverlapRuns38" => OverlapRunValue(index, count, 38),
                    "OverlapRuns39" => OverlapRunValue(index, count, 39),
                    _ => index,
                };
            }
            int percent =
                shape == "Noise1" ? 1
                : shape == "Noise5" ? 5
                : shape == "Noise10" ? 10
                : 0;
            for (int swap = 0; swap < count * percent / 100; ++swap)
            {
                state = unchecked(state * 1664525U + 1013904223U);
                int left = (int)(state % (uint)count);
                state = unchecked(state * 1664525U + 1013904223U);
                int right = (int)(state % (uint)count);
                (data[left], data[right]) = (data[right], data[left]);
            }
            if (shape == "Block32" || shape == "Block64")
            {
                int block = shape == "Block32" ? 32 : 64;
                for (int begin = 0; begin + 2 * block <= count; begin += 2 * block)
                {
                    for (int offset = 0; offset < block; ++offset)
                    {
                        (data[begin + offset], data[begin + block + offset]) = (
                            data[begin + block + offset],
                            data[begin + offset]
                        );
                    }
                }
            }
            return data;
        }

        private static int OverlapRunValue(int index, int count, int runs)
        {
            int length = Math.Max(1, (count + runs - 1) / runs);
            return (index / length) * (length / 2) + index % length;
        }

        private static int NaturalRunValue(int index, int count, int runs)
        {
            int length = Math.Max(1, (count + runs - 1) / runs);
            int run = index / length;
            return (runs - run) * length + index % length;
        }
    }
}
