// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core.TestUtils
{
#if UNITY_EDITOR

    using System;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Editor.Utils;

    /// <summary>
    ///     Unit tests for <see cref="CommonTestBase.ExecuteWithImmediateImport"/> methods.
    ///     These tests verify both the Action and Func overloads work correctly with and without
    ///     active batch scopes.
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Editor")]
    public sealed class ExecuteWithImmediateImportTests : CommonTestBase
    {
        [OneTimeSetUp]
        public void FixtureSetUp()
        {
            AssetDatabaseBatchHelper.ResetCountersOnly();
        }

        [SetUp]
        public override void BaseSetUp()
        {
            base.BaseSetUp();
            AssetDatabaseBatchHelper.ResetBatchDepth();
        }

        [TearDown]
        public override void TearDown()
        {
            base.TearDown();
            AssetDatabaseBatchHelper.ResetBatchDepth();
        }

        [Test]
        public void ExecuteWithImmediateImportWhenActionIsNullDoesNotThrow()
        {
            Assert.DoesNotThrow(() => ExecuteWithImmediateImport(null));
        }

        [Test]
        public void ExecuteWithImmediateImportWhenActionIsNullWithRefreshAfterDoesNotThrow()
        {
            Assert.DoesNotThrow(() => ExecuteWithImmediateImport(null, refreshAfter: true));
        }

        [Test]
        public void ExecuteWithImmediateImportGenericWhenFuncIsNullReturnsDefaultInt()
        {
            Func<int> nullFunc = null;
            int result = ExecuteWithImmediateImport(nullFunc);
            Assert.That(result, Is.EqualTo(0), "Should return default(int) when func is null");
        }

        [Test]
        public void ExecuteWithImmediateImportGenericWhenFuncIsNullReturnsDefaultString()
        {
            Func<string> nullFunc = null;
            string result = ExecuteWithImmediateImport(nullFunc);
            Assert.That(result, Is.Null, "Should return default(string) which is null");
        }

        [Test]
        public void ExecuteWithImmediateImportGenericWhenFuncIsNullReturnsDefaultReferenceType()
        {
            Func<object> nullFunc = null;
            object result = ExecuteWithImmediateImport(nullFunc);
            Assert.That(result, Is.Null, "Should return default(object) which is null");
        }

        [Test]
        public void ExecuteWithImmediateImportGenericWhenFuncIsNullWithRefreshAfterReturnsDefault()
        {
            Func<int> nullFunc = null;
            int result = ExecuteWithImmediateImport(nullFunc, refreshAfter: true);
            Assert.That(
                result,
                Is.EqualTo(0),
                "Should return default when func is null even with refreshAfter"
            );
        }

        [Test]
        public void ExecuteWithImmediateImportGenericReturnsValueType()
        {
            int result = ExecuteWithImmediateImport(() => 42);
            Assert.That(result, Is.EqualTo(42), "Should return the value from the func");
        }

        [Test]
        public void ExecuteWithImmediateImportGenericReturnsReferenceType()
        {
            string expected = "test value";
            string result = ExecuteWithImmediateImport(() => expected);
            Assert.That(result, Is.EqualTo(expected), "Should return the reference from the func");
        }

        [Test]
        public void ExecuteWithImmediateImportGenericReturnsNewObject()
        {
            object result = ExecuteWithImmediateImport(() => new object());
            Assert.That(result, Is.Not.Null, "Should return a new object from the func");
        }

        [Test]
        public void ExecuteWithImmediateImportActionExecutes()
        {
            bool actionExecuted = false;
            ExecuteWithImmediateImport(() => actionExecuted = true);
            Assert.That(actionExecuted, Is.True, "Action should have been executed");
        }

        [Test]
        public void ExecuteWithImmediateImportActionWithRefreshAfterExecutes()
        {
            bool actionExecuted = false;
            ExecuteWithImmediateImport(() => actionExecuted = true, refreshAfter: true);
            Assert.That(
                actionExecuted,
                Is.True,
                "Action should have been executed with refreshAfter"
            );
        }

        [Test]
        public void ExecuteWithImmediateImportGenericFuncExecutes()
        {
            bool funcExecuted = false;
            int result = ExecuteWithImmediateImport(() =>
            {
                funcExecuted = true;
                return 100;
            });
            Assert.That(funcExecuted, Is.True, "Func should have been executed");
            Assert.That(result, Is.EqualTo(100), "Should return correct value");
        }

        [Test]
        public void ExecuteWithImmediateImportGenericFuncWithRefreshAfterExecutes()
        {
            bool funcExecuted = false;
            int result = ExecuteWithImmediateImport(
                () =>
                {
                    funcExecuted = true;
                    return 200;
                },
                refreshAfter: true
            );
            Assert.That(funcExecuted, Is.True, "Func should have been executed with refreshAfter");
            Assert.That(result, Is.EqualTo(200), "Should return correct value");
        }

        [Test]
        public void ExecuteWithImmediateImportOutsideBatchMaintainsZeroDepth()
        {
            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "Pre-condition: should start at depth 0"
            );

            ExecuteWithImmediateImport(() => { });

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "Should remain at depth 0 after ExecuteWithImmediateImport"
            );
        }

        [Test]
        public void ExecuteWithImmediateImportGenericOutsideBatchMaintainsZeroDepth()
        {
            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "Pre-condition: should start at depth 0"
            );

            int result = ExecuteWithImmediateImport(() => 42);

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "Should remain at depth 0 after ExecuteWithImmediateImport<T>"
            );
            Assert.That(result, Is.EqualTo(42), "Should return correct value");
        }

        [Test]
        public void ExecuteWithImmediateImportInsideBatchMaintainsOuterScope()
        {
            using (AssetDatabaseBatchHelper.BeginBatch(refreshOnDispose: false))
            {
                Assert.That(
                    AssetDatabaseBatchHelper.CurrentBatchDepth,
                    Is.EqualTo(1),
                    "Pre-condition: should be at depth 1 inside outer scope"
                );

                ExecuteWithImmediateImport(() => { });

                Assert.That(
                    AssetDatabaseBatchHelper.CurrentBatchDepth,
                    Is.EqualTo(1),
                    "Should return to depth 1 after ExecuteWithImmediateImport"
                );
            }

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "Depth should be 0 after outer scope exits"
            );
        }

        [Test]
        public void ExecuteWithImmediateImportGenericInsideBatchMaintainsOuterScope()
        {
            using (AssetDatabaseBatchHelper.BeginBatch(refreshOnDispose: false))
            {
                Assert.That(
                    AssetDatabaseBatchHelper.CurrentBatchDepth,
                    Is.EqualTo(1),
                    "Pre-condition: should be at depth 1 inside outer scope"
                );

                int result = ExecuteWithImmediateImport(() => 42);

                Assert.That(
                    AssetDatabaseBatchHelper.CurrentBatchDepth,
                    Is.EqualTo(1),
                    "Should return to depth 1 after ExecuteWithImmediateImport<T>"
                );
                Assert.That(result, Is.EqualTo(42), "Should return correct value");
            }

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "Depth should be 0 after outer scope exits"
            );
        }

        [Test]
        [TestCase(2, TestName = "ExecuteWithImmediateImportInsideNestedBatch.Depth2")]
        [TestCase(3, TestName = "ExecuteWithImmediateImportInsideNestedBatch.Depth3")]
        [TestCase(5, TestName = "ExecuteWithImmediateImportInsideNestedBatch.Depth5")]
        public void ExecuteWithImmediateImportInsideNestedBatchMaintainsCorrectDepth(int depth)
        {
            System.Collections.Generic.List<AssetDatabaseBatchScope> scopes =
                new System.Collections.Generic.List<AssetDatabaseBatchScope>();
            for (int i = 0; i < depth; i++)
            {
                scopes.Add(AssetDatabaseBatchHelper.BeginBatch(refreshOnDispose: false));
            }

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(depth),
                $"Pre-condition: should be at depth {depth}"
            );

            ExecuteWithImmediateImport(() => { });

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(depth),
                $"Should return to original depth {depth}"
            );

            for (int i = scopes.Count - 1; i >= 0; i--)
            {
                scopes[i].Dispose();
            }

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "Final depth should be 0 after all scopes disposed"
            );
        }

        [Test]
        [TestCase(2, TestName = "ExecuteWithImmediateImportGenericInsideNestedBatch.Depth2")]
        [TestCase(3, TestName = "ExecuteWithImmediateImportGenericInsideNestedBatch.Depth3")]
        [TestCase(5, TestName = "ExecuteWithImmediateImportGenericInsideNestedBatch.Depth5")]
        public void ExecuteWithImmediateImportGenericInsideNestedBatchMaintainsCorrectDepth(
            int depth
        )
        {
            System.Collections.Generic.List<AssetDatabaseBatchScope> scopes =
                new System.Collections.Generic.List<AssetDatabaseBatchScope>();
            for (int i = 0; i < depth; i++)
            {
                scopes.Add(AssetDatabaseBatchHelper.BeginBatch(refreshOnDispose: false));
            }

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(depth),
                $"Pre-condition: should be at depth {depth}"
            );

            int result = ExecuteWithImmediateImport(() => 42);

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(depth),
                $"Should return to original depth {depth}"
            );
            Assert.That(result, Is.EqualTo(42), "Should return correct value");

            for (int i = scopes.Count - 1; i >= 0; i--)
            {
                scopes[i].Dispose();
            }

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "Final depth should be 0 after all scopes disposed"
            );
        }

        [Test]
        public void ExecuteWithImmediateImportMultipleCallsInSequenceOutsideBatch()
        {
            int counter = 0;

            ExecuteWithImmediateImport(() => counter++);
            ExecuteWithImmediateImport(() => counter++);
            ExecuteWithImmediateImport(() => counter++);

            Assert.That(counter, Is.EqualTo(3), "All three actions should have executed");
            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "Depth should remain 0 after multiple calls"
            );
        }

        [Test]
        public void ExecuteWithImmediateImportGenericMultipleCallsInSequenceOutsideBatch()
        {
            int sum = 0;

            sum += ExecuteWithImmediateImport(() => 10);
            sum += ExecuteWithImmediateImport(() => 20);
            sum += ExecuteWithImmediateImport(() => 30);

            Assert.That(
                sum,
                Is.EqualTo(60),
                "All three funcs should have executed and returned values"
            );
            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "Depth should remain 0 after multiple calls"
            );
        }

        [Test]
        public void ExecuteWithImmediateImportMultipleCallsInsideBatch()
        {
            using (AssetDatabaseBatchHelper.BeginBatch(refreshOnDispose: false))
            {
                int counter = 0;

                ExecuteWithImmediateImport(() => counter++);
                Assert.That(
                    AssetDatabaseBatchHelper.CurrentBatchDepth,
                    Is.EqualTo(1),
                    "Depth should be 1 after first call"
                );

                ExecuteWithImmediateImport(() => counter++);
                Assert.That(
                    AssetDatabaseBatchHelper.CurrentBatchDepth,
                    Is.EqualTo(1),
                    "Depth should be 1 after second call"
                );

                ExecuteWithImmediateImport(() => counter++);
                Assert.That(
                    AssetDatabaseBatchHelper.CurrentBatchDepth,
                    Is.EqualTo(1),
                    "Depth should be 1 after third call"
                );

                Assert.That(counter, Is.EqualTo(3), "All three actions should have executed");
            }

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "Depth should be 0 after outer scope exits"
            );
        }

        [Test]
        public void ExecuteWithImmediateImportGenericMultipleCallsInsideBatch()
        {
            using (AssetDatabaseBatchHelper.BeginBatch(refreshOnDispose: false))
            {
                int sum = 0;

                sum += ExecuteWithImmediateImport(() => 10);
                Assert.That(
                    AssetDatabaseBatchHelper.CurrentBatchDepth,
                    Is.EqualTo(1),
                    "Depth should be 1 after first call"
                );

                sum += ExecuteWithImmediateImport(() => 20);
                Assert.That(
                    AssetDatabaseBatchHelper.CurrentBatchDepth,
                    Is.EqualTo(1),
                    "Depth should be 1 after second call"
                );

                sum += ExecuteWithImmediateImport(() => 30);
                Assert.That(
                    AssetDatabaseBatchHelper.CurrentBatchDepth,
                    Is.EqualTo(1),
                    "Depth should be 1 after third call"
                );

                Assert.That(sum, Is.EqualTo(60), "All three funcs should have executed");
            }

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "Depth should be 0 after outer scope exits"
            );
        }

        [Test]
        public void ExecuteWithImmediateImportGenericReturnsNullableStruct()
        {
            int? result = ExecuteWithImmediateImport<int?>(() => 42);
            Assert.That(result, Is.EqualTo(42), "Should return nullable value");
        }

        [Test]
        public void ExecuteWithImmediateImportGenericReturnsNullForNullableWhenFuncIsNull()
        {
            Func<int?> nullFunc = null;
            int? result = ExecuteWithImmediateImport(nullFunc);
            Assert.That(result, Is.Null, "Should return null for nullable when func is null");
        }

        [Test]
        public void ExecuteWithImmediateImportGenericReturnsTuple()
        {
            (int a, string b) result = ExecuteWithImmediateImport(() => (1, "test"));
            Assert.That(result.a, Is.EqualTo(1), "Should return correct tuple item 1");
            Assert.That(result.b, Is.EqualTo("test"), "Should return correct tuple item 2");
        }
    }

#endif
}
