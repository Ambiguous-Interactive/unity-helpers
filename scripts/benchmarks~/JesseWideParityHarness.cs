// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Extension
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Text;
    using WideRecord = System.ValueTuple<long, long, long, long, long>;

    public static partial class JesseWideParityHarness
    {
        private const string CandidateHash = "CANDIDATE_SHA_PLACEHOLDER";
        private const string BaselineHash = "BASELINE_SHA_PLACEHOLDER";
        private const string HelpersHash = "HELPERS_SHA_PLACEHOLDER";
        private const string HarnessTemplateHash = "HARNESS_SHA_PLACEHOLDER";
        private const string HarnessMarker = "JesseWideParityCalibratedV3";
        private const string SourcePin = "1bf1f3d5b719c869880d98443050a836a65f47c1";
        private static readonly IComparer<WideRecord> WideComparer = new WideRecordComparer();

        public static string Main(
            string shape,
            int count,
            bool useList,
            int firstBatch,
            int batchCount
        )
        {
            WideRecord[] input = MakeInput(shape, count);
            WideRecord[] expected = (WideRecord[])input.Clone();
            Array.Sort(expected, WideComparer);
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
                Math.Max(1, Math.Min(1048576L, 134217728L / Math.Max(128L, (long)count * 40 + 64)));
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

        private static double MeasureCalibrated(
            WideRecord[] input,
            WideRecord[] expected,
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
                    Math.Min(1048576L, 134217728L / Math.Max(128L, (long)input.Length * 40 + 64))
                );
            repetitions =
                fixedRepetitions == 0 ? 1 : Math.Min(fixedRepetitions, maximumRepetitions);
            long minimumTicks = calibrate ? Stopwatch.Frequency / 50 : 1;
            long elapsed;
            do
            {
                IList<WideRecord>[] subjects = new IList<WideRecord>[repetitions];
                for (int repetition = 0; repetition < repetitions; ++repetition)
                {
                    subjects[repetition] = useList
                        ? new List<WideRecord>(input)
                        : (WideRecord[])input.Clone();
                }
                Stopwatch timer = Stopwatch.StartNew();
                switch (arm)
                {
                    case 'A':
                        foreach (IList<WideRecord> subject in subjects)
                        {
                            JesseBenchmarkOld.JesseSort(subject, WideComparer);
                        }
                        break;
                    case 'B':
                        foreach (IList<WideRecord> subject in subjects)
                        {
                            JesseBenchmarkCandidate.JesseSort(subject, WideComparer);
                        }
                        break;
                    case 'C':
                        if (useList)
                        {
                            foreach (IList<WideRecord> subject in subjects)
                            {
                                ((List<WideRecord>)subject).Sort(WideComparer);
                            }
                        }
                        else
                        {
                            foreach (IList<WideRecord> subject in subjects)
                            {
                                Array.Sort((WideRecord[])subject, WideComparer);
                            }
                        }
                        break;
                    case 'D':
                        foreach (IList<WideRecord> subject in subjects)
                        {
                            JesseBenchmarkCandidate.IpnSort(subject, WideComparer);
                        }
                        break;
                    case 'E':
                        int totalCount = 0;
                        foreach (IList<WideRecord> subject in subjects)
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
                    foreach (IList<WideRecord> subject in subjects)
                    {
                        bool[] identities = new bool[input.Length];
                        for (int index = 0; index < expected.Length; ++index)
                        {
                            WideRecord value = subject[index];
                            if (value.Item2 < 0 || input.Length <= value.Item2)
                            {
                                throw new InvalidOperationException("Wide identity range invalid");
                            }
                            int identity = (int)value.Item2;
                            if (identity < 0 || input.Length <= identity || identities[identity])
                            {
                                throw new InvalidOperationException("Wide identity lost");
                            }
                            identities[identity] = true;
                            if (
                                !EqualityComparer<WideRecord>.Default.Equals(value, input[identity])
                            )
                            {
                                throw new InvalidOperationException(
                                    "Wide complete tuple association lost"
                                );
                            }
                            if (value.Item1 != expected[index].Item1)
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

        private static WideRecord[] MakeInput(string shape, int count)
        {
            WideRecord[] data = new WideRecord[count];
            uint state = 747;
            for (int index = 0; index < count; ++index)
            {
                state = unchecked(state * 1664525U + 1013904223U);
                long key =
                    shape == "Random"
                        ? unchecked((int)state)
                        : ((index & 1) == 0 ? state % 7 : state % 7 + 64);
                data[index] = new WideRecord(key, index, ~index, state, index * 13L);
            }
            return data;
        }
    }
}
