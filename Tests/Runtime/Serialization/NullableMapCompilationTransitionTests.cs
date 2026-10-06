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

    [TestFixture]
    [Category("Serialization")]
    public sealed class NullableMapCompilationTransitionTests
    {
        private static byte[] Write<T>(RuntimeTypeModel model, T value, bool nested)
            where T : new()
        {
            using MemoryStream stream = new MemoryStream();
            if (nested)
            {
                model.Serialize(stream, new NullableMapContainer<T> { Entries = value });
            }
            else
            {
                model.Serialize(stream, value);
            }
            return stream.ToArray();
        }

        private static Exception Capture<T>(RuntimeTypeModel model, T value, bool nested)
            where T : new()
        {
            return Assert.Catch(() => Write(model, value, nested));
        }

        private static void AssertCallbackParity(
            RuntimeTypeModel model,
            Dictionary<int, NullableMapCallbackValue> direct,
            Dictionary<int, NullableMapCallbackValue?> nullable,
            bool nested,
            bool after,
            bool compiled
        )
        {
            Exception sentinel = new InvalidOperationException("Nullable callback failure");
            if (after)
            {
                NullableMapCallbackState.AfterFailure = sentinel;
            }
            else
            {
                NullableMapCallbackState.BeforeFailure = sentinel;
            }
            try
            {
                Exception directException = Capture(model, direct, nested);
                Exception nullableException = Capture(model, nullable, nested);
                if (compiled)
                {
                    Assert.AreSame(sentinel, directException);
                    Assert.AreSame(sentinel, nullableException);
                }
                else
                {
                    Assert.IsInstanceOf<TargetInvocationException>(directException);
                    Assert.IsInstanceOf<TargetInvocationException>(nullableException);
                    Assert.AreSame(sentinel, directException.InnerException);
                    Assert.AreSame(sentinel, nullableException.InnerException);
                }
            }
            finally
            {
                NullableMapCallbackState.BeforeFailure = null;
                NullableMapCallbackState.AfterFailure = null;
            }
        }

        [TearDown]
        public void ResetCallbackFailures()
        {
            NullableMapCallbackState.BeforeFailure = null;
            NullableMapCallbackState.AfterFailure = null;
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void WarmModelCompilationPreservesNullableCallbackSemantics(bool nested, bool after)
        {
#if ENABLE_IL2CPP
            Assert.Ignore("CompileInPlace emits dynamic IL and is unsupported by IL2CPP.");
#endif
            RuntimeTypeModel model = RuntimeTypeModel.Create();
            model.AutoCompile = false;
            model.Add(
                nested
                    ? typeof(NullableMapContainer<Dictionary<int, NullableMapCallbackValue>>)
                    : typeof(Dictionary<int, NullableMapCallbackValue>),
                true
            );
            model.Add(
                nested
                    ? typeof(NullableMapContainer<Dictionary<int, NullableMapCallbackValue?>>)
                    : typeof(Dictionary<int, NullableMapCallbackValue?>),
                true
            );
            Dictionary<int, NullableMapCallbackValue> direct = new Dictionary<
                int,
                NullableMapCallbackValue
            >
            {
                {
                    7,
                    new NullableMapCallbackValue { Number = 19 }
                },
            };
            Dictionary<int, NullableMapCallbackValue?> nullable = new Dictionary<
                int,
                NullableMapCallbackValue?
            >
            {
                {
                    7,
                    new NullableMapCallbackValue { Number = 19 }
                },
            };
            byte[] directBefore = Write(model, direct, nested);
            byte[] nullableBefore = Write(model, nullable, nested);
            CollectionAssert.AreEqual(directBefore, nullableBefore);
            AssertCallbackParity(model, direct, nullable, nested, after, false);
            model.CompileInPlace();
            CollectionAssert.AreEqual(directBefore, Write(model, direct, nested));
            CollectionAssert.AreEqual(nullableBefore, Write(model, nullable, nested));
            AssertCallbackParity(model, direct, nullable, nested, after, true);
        }
    }
}
