// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Reflection;
    using NUnit.Framework;
    using ProtoBuf.Meta;
    using WallstopStudios.UnityHelpers.Core.DataStructure;
    using WallstopStudios.UnityHelpers.Utils;
    using PackageSerializer = WallstopStudios.UnityHelpers.Core.Serialization.Serializer;

    [TestFixture]
    [Category("Serialization")]
    public sealed class CollectionSerializationScratchTests
    {
        private const int Interpreted = 0;
        private const int AutoCompiled = 1;
        private const int CompiledInPlace = 2;
        private const int ForeignSerializer = 3;
        private const int PackageFacade = 4;

        internal static IEnumerable<TestCaseData> SuccessCases()
        {
            foreach (bool deque in new[] { true, false })
            {
                for (int mode = Interpreted; mode <= PackageFacade; ++mode)
                {
                    foreach (int count in new[] { 0, 1, 5, 10000 })
                    {
                        yield return new TestCaseData(deque, mode, count, false).SetName(
                            $"Scratch.Success.{FamilyName(deque)}.{ModeName(mode)}.Count{count}"
                        );
                    }
                    yield return new TestCaseData(deque, mode, 4, true).SetName(
                        $"Scratch.Success.{FamilyName(deque)}.{ModeName(mode)}.Wrapped"
                    );
                }
            }
        }

        internal static IEnumerable<TestCaseData> FailureCases()
        {
            foreach (bool deque in new[] { true, false })
            {
                for (int mode = Interpreted; mode <= PackageFacade; ++mode)
                {
                    for (int phase = 0; phase < (mode == PackageFacade ? 2 : 4); ++phase)
                    {
                        yield return new TestCaseData(deque, mode, phase).SetName(
                            $"Scratch.Failure.{FamilyName(deque)}.{ModeName(mode)}.{FailureName(phase)}"
                        );
                    }
                }
            }
        }

        internal static IEnumerable<TestCaseData> ReentrantCases()
        {
            foreach (bool deque in new[] { true, false })
            {
                for (int mode = Interpreted; mode <= PackageFacade; ++mode)
                {
                    foreach (bool innerFailure in new[] { false, true })
                    {
                        yield return new TestCaseData(deque, mode, innerFailure).SetName(
                            $"Scratch.Reentrant.{FamilyName(deque)}.{ModeName(mode)}.InnerFailure{innerFailure}"
                        );
                    }
                }
            }
        }

        internal static IEnumerable<TestCaseData> GoldenCases()
        {
            foreach (bool deque in new[] { true, false })
            {
                for (int mode = Interpreted; mode <= ForeignSerializer; ++mode)
                {
                    for (int layout = 0; layout < 4; ++layout)
                    {
                        yield return new TestCaseData(deque, mode, layout).SetName(
                            $"Scratch.HistoricalBytes.{FamilyName(deque)}.{ModeName(mode)}.Layout{layout}"
                        );
                    }
                }
            }
        }

        internal static IEnumerable<TestCaseData> ParentCases()
        {
            for (int mode = Interpreted; mode <= PackageFacade; ++mode)
            {
                for (int phase = 0; phase < (mode == PackageFacade ? 2 : 4); ++phase)
                {
                    yield return new TestCaseData(mode, phase).SetName(
                        $"Scratch.ParentCallbacks.{ModeName(mode)}.{FailureName(phase)}"
                    );
                }
            }
        }

        internal static string FamilyName(bool deque)
        {
            return deque ? nameof(Deque<int>) : nameof(CyclicBuffer<int>);
        }

        internal static string ModeName(int mode)
        {
            switch (mode)
            {
                case Interpreted:
                    return nameof(Interpreted);
                case AutoCompiled:
                    return nameof(AutoCompiled);
                case CompiledInPlace:
                    return nameof(CompiledInPlace);
                case ForeignSerializer:
                    return nameof(ForeignSerializer);
                default:
                    return nameof(PackageFacade);
            }
        }

        internal static string FailureName(int phase)
        {
            switch (phase)
            {
                case 0:
                    return "BeforeCallback";
                case 1:
                    return "AfterCallback";
                case 2:
                    return "LateFlush";
                default:
                    return "MidWrite";
            }
        }

        internal static RuntimeTypeModel CreateModel(int mode)
        {
            if (ForeignSerializer <= mode)
            {
                return RuntimeTypeModel.Default;
            }
            RuntimeTypeModel model = RuntimeTypeModel.Create();
            model.AutoCompile = false;
            model.Add(typeof(CollectionScratchItem), true);
            model.Add(typeof(Deque<CollectionScratchItem>), true);
            model.Add(typeof(CyclicBuffer<CollectionScratchItem>), true);
            model.Add(typeof(CollectionScratchParent), true);
            model.Add(typeof(Deque<int>), true);
            model.Add(typeof(CyclicBuffer<int>), true);
#if ENABLE_IL2CPP
            if (mode == AutoCompiled || mode == CompiledInPlace)
            {
                Assert.Ignore("This model mode emits dynamic IL and is unsupported by IL2CPP.");
            }
#else
            if (mode == AutoCompiled)
            {
                model.AutoCompile = true;
            }
            if (mode == CompiledInPlace)
            {
                model.CompileInPlace();
            }
#endif
            return model;
        }

        internal static IReadOnlyList<CollectionScratchItem> CreateValues(
            bool deque,
            int count,
            CollectionScratchCallbackState state,
            bool wrapped = false,
            bool largePayload = false
        )
        {
            int capacity = wrapped ? 4 : Math.Max(16, count);
            Deque<CollectionScratchItem> queue = deque
                ? new Deque<CollectionScratchItem>(capacity)
                : null;
            CyclicBuffer<CollectionScratchItem> ring = deque
                ? null
                : new CyclicBuffer<CollectionScratchItem>(capacity);
            int writes = wrapped ? 6 : count;
            for (int value = 1; value <= writes; ++value)
            {
                if (wrapped && deque && value == 5)
                {
                    Assert.IsTrue(queue.TryPopFront(out _));
                    Assert.IsTrue(queue.TryPopFront(out _));
                }
                CollectionScratchItem item = new CollectionScratchItem
                {
                    Value = value,
                    Payload = largePayload ? new byte[65536] : new byte[] { (byte)value, 0, 255 },
                    State = state,
                };
                if (deque)
                {
                    queue.PushBack(item);
                }
                else
                {
                    ring.Add(item);
                }
            }
            return deque ? (IReadOnlyList<CollectionScratchItem>)queue : ring;
        }

        internal static CollectionScratchParent Wrap(object values, bool deque)
        {
            CollectionScratchParent parent = values as CollectionScratchParent;
            if (parent != null)
            {
                return parent;
            }
            return new CollectionScratchParent
            {
                Deque = deque ? (Deque<CollectionScratchItem>)values : null,
                Cyclic = deque ? null : (CyclicBuffer<CollectionScratchItem>)values,
            };
        }

        internal static void Write(
            int mode,
            RuntimeTypeModel model,
            object value,
            bool deque,
            Stream output
        )
        {
            if (mode == PackageFacade)
            {
                CollectionScratchParent parent = Wrap(value, deque);
                byte[] bytes = PackageSerializer.ProtoSerialize(parent);
                output.Write(bytes, 0, bytes.Length);
            }
            else if (mode == ForeignSerializer)
            {
                ProtoBuf.Serializer.NonGeneric.Serialize(output, value);
            }
            else
            {
                model.Serialize(output, value);
            }
        }

        internal static IReadOnlyList<CollectionScratchItem> Read(
            int mode,
            RuntimeTypeModel model,
            MemoryStream input,
            bool deque
        )
        {
            input.Position = 0;
            if (mode == PackageFacade)
            {
                CollectionScratchParent parent =
                    PackageSerializer.ProtoDeserialize<CollectionScratchParent>(input.ToArray());
                return deque ? (IReadOnlyList<CollectionScratchItem>)parent.Deque : parent.Cyclic;
            }
            Type type = deque
                ? typeof(Deque<CollectionScratchItem>)
                : typeof(CyclicBuffer<CollectionScratchItem>);
            return (IReadOnlyList<CollectionScratchItem>)model.Deserialize(input, null, type);
        }

        internal static void AssertContents(
            IReadOnlyList<CollectionScratchItem> expected,
            IReadOnlyList<CollectionScratchItem> actual
        )
        {
            Assert.AreEqual(expected.Count, actual.Count);
            for (int i = 0; i < expected.Count; ++i)
            {
                Assert.AreEqual(expected[i].Value, actual[i].Value);
                CollectionAssert.AreEqual(expected[i].Payload, actual[i].Payload);
            }
            if (expected is Deque<CollectionScratchItem> queue)
            {
                Assert.AreEqual(queue.Capacity, ((Deque<CollectionScratchItem>)actual).Capacity);
            }
            else
            {
                Assert.AreEqual(
                    ((CyclicBuffer<CollectionScratchItem>)expected).Capacity,
                    ((CyclicBuffer<CollectionScratchItem>)actual).Capacity
                );
            }
        }

        internal static void AssertPoolBalance<T>(PoolStatistics before, int expectedRentals)
        {
            PoolStatistics after = Buffers<T>.List.GetStatistics();
            Assert.AreEqual(
                expectedRentals,
                after.RentCount - before.RentCount,
                "Expected real scratch rentals."
            );
            Assert.AreEqual(
                expectedRentals,
                after.ReturnCount - before.ReturnCount,
                "Every scratch rental must be returned before the writer exits."
            );
        }

        internal static Exception Innermost(Exception error)
        {
            while (error is TargetInvocationException wrapper)
            {
                error = wrapper.InnerException;
            }
            return error;
        }

        internal static void Clear(IReadOnlyList<CollectionScratchItem> values)
        {
            if (values is Deque<CollectionScratchItem> queue)
            {
                queue.Clear();
            }
            else
            {
                ((CyclicBuffer<CollectionScratchItem>)values).Clear();
            }
        }

        [TestCaseSource(nameof(SuccessCases))]
        public void SuccessfulWritesReleaseScratchAndPreserveOrder(
            bool deque,
            int mode,
            int count,
            bool wrapped
        )
        {
            RuntimeTypeModel model = CreateModel(mode);
            CollectionScratchCallbackState state = new CollectionScratchCallbackState();
            IReadOnlyList<CollectionScratchItem> values = CreateValues(
                deque,
                count,
                state,
                wrapped
            );
            PoolStatistics before = Buffers<CollectionScratchItem>.List.GetStatistics();
            using MemoryStream output = new MemoryStream();
            Write(mode, model, values, deque, output);
            AssertPoolBalance<CollectionScratchItem>(before, values.Count == 0 ? 0 : 1);
            Assert.AreEqual(values.Count, state.BeforeCalls);
            Assert.AreEqual(values.Count, state.AfterCalls);
            AssertContents(values, Read(mode, model, output, deque));
        }

        [TestCaseSource(nameof(FailureCases))]
        public void FailedWritesReleaseScratchBeforeClearAndRetry(bool deque, int mode, int phase)
        {
            RuntimeTypeModel model = CreateModel(mode);
            CollectionScratchCallbackState state = new CollectionScratchCallbackState
            {
                FailBefore = phase == 0,
                FailAfter = phase == 1,
            };
            IReadOnlyList<CollectionScratchItem> values = CreateValues(
                deque,
                3,
                state,
                largePayload: phase == 3
            );
            PoolStatistics before = Buffers<CollectionScratchItem>.List.GetStatistics();
            using Stream output =
                2 <= phase
                    ? (Stream)new CollectionScratchFailureStream(state.Failure)
                    : new MemoryStream();
            Exception failure = Assert.Catch(() => Write(mode, model, values, deque, output));
            Assert.AreSame(state.Failure, Innermost(failure));
            if (2 <= phase)
            {
                Assert.AreSame(state.Failure, failure, "Stream exceptions retain their identity.");
            }
            else if (mode == Interpreted)
            {
                Assert.That(failure, Is.TypeOf<TargetInvocationException>());
            }
            AssertPoolBalance<CollectionScratchItem>(before, 1);
            if (phase <= 1)
            {
                Assert.AreEqual(2, state.BeforeCalls);
                Assert.AreEqual(phase == 0 ? 1 : 2, state.AfterCalls);
            }
            if (phase == 2)
            {
                Assert.AreEqual(3, state.BeforeCalls);
                Assert.AreEqual(
                    3,
                    state.AfterCalls,
                    "Successful callbacks run before the eventual top-level flush."
                );
            }
            PoolStatistics completed = Buffers<CollectionScratchItem>.List.GetStatistics();
            Clear(values);
            AssertPoolBalance<CollectionScratchItem>(completed, 0);
            Assert.AreEqual(0, values.Count);
            state.FailBefore = false;
            state.FailAfter = false;
            for (int value = 1; value <= 3; ++value)
            {
                CollectionScratchItem item = new CollectionScratchItem
                {
                    Value = value,
                    State = state,
                };
                if (deque)
                {
                    ((Deque<CollectionScratchItem>)values).PushBack(item);
                }
                else
                {
                    ((CyclicBuffer<CollectionScratchItem>)values).Add(item);
                }
            }
            IReadOnlyList<CollectionScratchItem> retryValues = values;
            before = Buffers<CollectionScratchItem>.List.GetStatistics();
            using MemoryStream retry = new MemoryStream();
            Write(mode, model, retryValues, deque, retry);
            AssertPoolBalance<CollectionScratchItem>(before, 1);
            AssertContents(retryValues, Read(mode, model, retry, deque));
        }

        [TestCaseSource(nameof(ReentrantCases))]
        public void NestedWritesRestoreTheSameCollectionScratch(
            bool deque,
            int mode,
            bool innerFailure
        )
        {
            RuntimeTypeModel model = CreateModel(mode);
            CollectionScratchCallbackState state = new CollectionScratchCallbackState();
            IReadOnlyList<CollectionScratchItem> values = CreateValues(
                deque,
                3,
                state,
                wrapped: true
            );
            state.FailingValue = values[1].Value;
            state.BeforeAction = () =>
            {
                state.FailBefore = innerFailure;
                using MemoryStream nested = new MemoryStream();
                try
                {
                    if (innerFailure)
                    {
                        Exception failure = Assert.Catch(() =>
                            Write(mode, model, values, deque, nested)
                        );
                        Assert.AreSame(state.Failure, Innermost(failure));
                    }
                    else
                    {
                        Write(mode, model, values, deque, nested);
                        AssertContents(values, Read(mode, model, nested, deque));
                    }
                }
                finally
                {
                    state.FailBefore = false;
                }
            };
            PoolStatistics before = Buffers<CollectionScratchItem>.List.GetStatistics();
            using MemoryStream outer = new MemoryStream();
            Write(mode, model, values, deque, outer);
            AssertPoolBalance<CollectionScratchItem>(before, 2);
            AssertContents(values, Read(mode, model, outer, deque));
        }

        [TestCaseSource(nameof(GoldenCases))]
        public void ContractWritesRetainHistoricalBytes(bool deque, int mode, int layout)
        {
            RuntimeTypeModel model = CreateModel(mode);
            object values;
            byte[] expected;
            if (deque)
            {
                Deque<int> queue = new Deque<int>(layout == 3 ? 4 : 16);
                if (layout == 1)
                {
                    queue.PushBack(42);
                }
                if (layout == 2 || layout == 3)
                {
                    for (int value = 1; value <= (layout == 3 ? 6 : 3); ++value)
                    {
                        if (layout == 3 && value == 5)
                        {
                            Assert.IsTrue(queue.TryPopFront(out _));
                            Assert.IsTrue(queue.TryPopFront(out _));
                        }
                        queue.PushBack(value);
                    }
                }
                values = queue;
                expected =
                    layout == 0 ? new byte[] { 40, 16 }
                    : layout == 1 ? new byte[] { 8, 42, 24, 1, 32, 1, 40, 16 }
                    : layout == 2 ? Convert.FromBase64String("CAEIAggDGAMgAygQ")
                    : new byte[] { 8, 3, 8, 4, 8, 5, 8, 6, 16, 2, 24, 2, 32, 4, 40, 4 };
            }
            else
            {
                CyclicBuffer<int> ring = new CyclicBuffer<int>(layout == 3 ? 3 : 16);
                if (layout == 1)
                {
                    ring.Add(42);
                }
                if (layout == 2 || layout == 3)
                {
                    for (int value = 1; value <= (layout == 3 ? 5 : 3); ++value)
                    {
                        ring.Add(value);
                    }
                }
                values = ring;
                expected =
                    layout == 0 ? new byte[] { 8, 16 }
                    : layout == 1 ? new byte[] { 8, 16, 16, 1, 24, 42, 32, 1 }
                    : layout == 2 ? Convert.FromBase64String("CBAQAxgBGAIYAyAD")
                    : new byte[] { 8, 3, 16, 3, 24, 3, 24, 4, 24, 5, 32, 2 };
            }
            PoolStatistics before = Buffers<int>.List.GetStatistics();
            using MemoryStream output = new MemoryStream();
            Write(mode, model, values, deque, output);
            AssertPoolBalance<int>(before, layout == 0 ? 0 : 1);
            CollectionAssert.AreEqual(expected, output.ToArray());
        }

        [TestCaseSource(nameof(ParentCases))]
        public void ParentCallbacksKeepSuccessOnlyAfterSemantics(int mode, int phase)
        {
            RuntimeTypeModel model = CreateModel(mode);
            CollectionScratchCallbackState state = new CollectionScratchCallbackState
            {
                FailParentBefore = phase == 0,
                FailParentAfter = phase == 1,
            };
            CollectionScratchParent parent = new CollectionScratchParent
            {
                Deque = (Deque<CollectionScratchItem>)CreateValues(true, 3, state),
                Payload = phase == 3 ? new byte[65536] : null,
                State = state,
            };
            PoolStatistics before = Buffers<CollectionScratchItem>.List.GetStatistics();
            using Stream output =
                2 <= phase
                    ? (Stream)new CollectionScratchFailureStream(state.Failure)
                    : new MemoryStream();
            Exception failure = Assert.Catch(() => Write(mode, model, parent, true, output));
            Assert.AreSame(state.Failure, Innermost(failure));
            Assert.AreEqual(1, state.ParentBeforeCalls);
            Assert.AreEqual(phase == 1 || phase == 2 ? 1 : 0, state.ParentAfterCalls);
            AssertPoolBalance<CollectionScratchItem>(before, phase == 0 ? 0 : 1);
        }
    }
}
