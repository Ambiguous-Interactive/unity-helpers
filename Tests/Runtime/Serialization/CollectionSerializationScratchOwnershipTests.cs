// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using NUnit.Framework;
    using ProtoBuf.Meta;
    using WallstopStudios.UnityHelpers.Core.Serialization;
    using WallstopStudios.UnityHelpers.Utils;

    [TestFixture]
    [Category("Serialization")]
    public sealed class CollectionSerializationScratchOwnershipTests
    {
        private static IEnumerable<TestCaseData> OwnershipCases()
        {
            foreach (bool deque in new[] { true, false })
            {
                for (int mode = 0; mode <= 4; ++mode)
                {
                    for (int depth = 1; depth <= 3; ++depth)
                    {
                        foreach (bool innerFailure in new[] { false, true })
                        {
                            yield return new TestCaseData(deque, mode, depth, innerFailure).SetName(
                                $"Scratch.Frames.{CollectionSerializationScratchTests.FamilyName(deque)}.{CollectionSerializationScratchTests.ModeName(mode)}.Depth{depth}.InnerFailure{innerFailure}"
                            );
                        }
                    }
                }
            }
        }

        [TestCaseSource(nameof(OwnershipCases))]
        public void ReentrantFramesReturnEveryLeaseAndRestoreEachEnclosingWrite(
            bool deque,
            int mode,
            int writeDepth,
            bool innerFailure
        )
        {
            RuntimeTypeModel model = CollectionSerializationScratchTests.CreateModel(mode);
            CollectionScratchCallbackState state = new CollectionScratchCallbackState();
            IReadOnlyList<CollectionScratchItem> values =
                CollectionSerializationScratchTests.CreateValues(deque, 4, state, wrapped: true);
            state.FailingValue = values[1].Value;
            int currentDepth = 1;
            Action nestedWrite = null;
            nestedWrite = () =>
            {
                ++currentDepth;
                state.BeforeAction = currentDepth < writeDepth ? nestedWrite : null;
                bool fails = innerFailure && currentDepth == writeDepth;
                state.FailBefore = fails;
                using MemoryStream output = new MemoryStream();
                try
                {
                    if (fails)
                    {
                        Exception failure = Assert.Catch(() =>
                            CollectionSerializationScratchTests.Write(
                                mode,
                                model,
                                values,
                                deque,
                                output
                            )
                        );
                        Assert.AreSame(
                            state.Failure,
                            CollectionSerializationScratchTests.Innermost(failure)
                        );
                    }
                    else
                    {
                        CollectionSerializationScratchTests.Write(
                            mode,
                            model,
                            values,
                            deque,
                            output
                        );
                        CollectionSerializationScratchTests.AssertContents(
                            values,
                            CollectionSerializationScratchTests.Read(mode, model, output, deque)
                        );
                    }
                }
                finally
                {
                    state.FailBefore = false;
                    --currentDepth;
                }
            };
            state.BeforeAction = writeDepth > 1 ? nestedWrite : null;
            state.FailBefore = innerFailure && writeDepth == 1;
            PoolStatistics itemBefore = Buffers<CollectionScratchItem>.List.GetStatistics();
            PoolStatistics frameBefore = Buffers<
                SerializationScratchFrame<CollectionScratchItem>
            >.List.GetStatistics();
            using MemoryStream outer = new MemoryStream();
            try
            {
                if (innerFailure && writeDepth == 1)
                {
                    Exception failure = Assert.Catch(() =>
                        CollectionSerializationScratchTests.Write(mode, model, values, deque, outer)
                    );
                    Assert.AreSame(
                        state.Failure,
                        CollectionSerializationScratchTests.Innermost(failure)
                    );
                }
                else
                {
                    CollectionSerializationScratchTests.Write(mode, model, values, deque, outer);
                    CollectionSerializationScratchTests.AssertContents(
                        values,
                        CollectionSerializationScratchTests.Read(mode, model, outer, deque)
                    );
                }
            }
            finally
            {
                state.FailBefore = false;
                state.BeforeAction = null;
            }
            Assert.AreEqual(1, currentDepth);
            CollectionSerializationScratchTests.AssertPoolBalance<CollectionScratchItem>(
                itemBefore,
                writeDepth
            );
            CollectionSerializationScratchTests.AssertPoolBalance<
                SerializationScratchFrame<CollectionScratchItem>
            >(frameBefore, writeDepth == 1 ? 0 : 1);
            itemBefore = Buffers<CollectionScratchItem>.List.GetStatistics();
            frameBefore = Buffers<
                SerializationScratchFrame<CollectionScratchItem>
            >.List.GetStatistics();
            using MemoryStream retry = new MemoryStream();
            CollectionSerializationScratchTests.Write(mode, model, values, deque, retry);
            CollectionSerializationScratchTests.AssertContents(
                values,
                CollectionSerializationScratchTests.Read(mode, model, retry, deque)
            );
            CollectionSerializationScratchTests.AssertPoolBalance<CollectionScratchItem>(
                itemBefore,
                1
            );
            CollectionSerializationScratchTests.AssertPoolBalance<
                SerializationScratchFrame<CollectionScratchItem>
            >(frameBefore, 0);
        }
    }
}
