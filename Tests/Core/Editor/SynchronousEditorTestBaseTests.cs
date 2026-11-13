// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core.TestUtils
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.TestTools;
    using Object = UnityEngine.Object;

    [TestFixture]
    public sealed class SynchronousEditorTestBaseTests : SynchronousEditorTestBase
    {
        private static IEnumerable<Texture2D> Enumerate(Texture2D texture)
        {
            yield return texture;
        }

        [Test]
        public void DisposablesRunOnceInReverseOrderBeforeObjectsAreDestroyed()
        {
            Texture2D texture = Track(new Texture2D(2, 2));
            List<int> order = new();
            SynchronousScopeDisposable first = TrackDisposable(
                new SynchronousScopeDisposable(() =>
                {
                    Assert.IsTrue(texture != null);
                    order.Add(1);
                })
            );
            SynchronousScopeDisposable second = TrackDisposable(
                new SynchronousScopeDisposable(() =>
                {
                    Assert.IsTrue(texture != null);
                    order.Add(2);
                })
            );
            Assert.AreNotSame(first, second);
            Assert.IsTrue(first.Equals(second));
            Assert.AreSame(first, TrackDisposable(first));
            Assert.IsTrue(TrackDisposable<SynchronousScopeDisposable>(null) == null);
            EndObjectTrackingScope();
            CollectionAssert.AreEqual(new[] { 2, 1 }, order);
            Assert.That(first.DisposeCount, Is.EqualTo(1));
            Assert.That(second.DisposeCount, Is.EqualTo(1));
            Assert.IsTrue(texture == null);
            EndObjectTrackingScope();
            Assert.That(first.DisposeCount, Is.EqualTo(1));
        }

        [Test]
        public void DisposalFailuresStillCleanEveryResourceAndAllowANewScope()
        {
            Texture2D texture = Track(new Texture2D(2, 2));
            SynchronousScopeDisposable first = TrackDisposable(new SynchronousScopeDisposable());
            SynchronousScopeDisposable failed = TrackDisposable(
                new SynchronousScopeDisposable(static () =>
                    throw new InvalidOperationException(
                        nameof(DisposalFailuresStillCleanEveryResourceAndAllowANewScope)
                    )
                )
            );
            SynchronousScopeDisposable last = TrackDisposable(new SynchronousScopeDisposable());
            AssertionException failure = Assert.Throws<AssertionException>(EndObjectTrackingScope);
            StringAssert.Contains(
                nameof(DisposalFailuresStillCleanEveryResourceAndAllowANewScope),
                failure.Message
            );
            Assert.That(first.DisposeCount, Is.EqualTo(1));
            Assert.That(failed.DisposeCount, Is.EqualTo(1));
            Assert.That(last.DisposeCount, Is.EqualTo(1));
            Assert.IsTrue(texture == null);
            Assert.DoesNotThrow(EndObjectTrackingScope);
            BeginObjectTrackingScope();
            Texture2D next = Track(new Texture2D(2, 2));
            EndObjectTrackingScope();
            Assert.IsTrue(next == null);
        }

        [Test]
        public void DisposalCannotReenterCleanupOrRegisterAnUnownedResource()
        {
            SynchronousScopeDisposable unowned = new();
            int callbacks = 0;
            SynchronousScopeDisposable resource = TrackDisposable(
                new SynchronousScopeDisposable(() =>
                {
                    if (1 < ++callbacks)
                    {
                        return;
                    }
                    Assert.DoesNotThrow(EndObjectTrackingScope);
                    Assert.Throws<AssertionException>(() => TrackDisposable(unowned));
                    Assert.Throws<AssertionException>(() => Track<GameObject>(null));
                })
            );
            EndObjectTrackingScope();
            Assert.That(resource.DisposeCount, Is.EqualTo(1));
            Assert.That(unowned.DisposeCount, Is.Zero);
            Assert.Throws<AssertionException>(() => TrackDisposable(unowned));
        }

        [Test]
        public void CleanupDestroysAllTransientObjectKindsAndChildren()
        {
            SynchronousEditorTestAsset asset = CreateScriptableObject<SynchronousEditorTestAsset>();
            Texture2D texture = Track(new Texture2D(2, 2));
            GameObject parent = NewGameObject(nameof(parent));
            GameObject child = Track(new GameObject(nameof(child)));
            child.transform.SetParent(parent.transform);
            EndObjectTrackingScope();
            Assert.IsTrue(asset == null);
            Assert.IsTrue(texture == null);
            Assert.IsTrue(parent == null);
            Assert.IsTrue(child == null);
            Assert.DoesNotThrow(EndObjectTrackingScope);
        }

        [Test]
        public void CleanupHandlesDuplicateAndAlreadyDestroyedObjects()
        {
            SynchronousEditorTestAsset asset = CreateScriptableObject<SynchronousEditorTestAsset>();
            Track(asset);
            Object.DestroyImmediate(asset);
            Assert.DoesNotThrow(EndObjectTrackingScope);
            Assert.IsTrue(asset == null);
        }

        [Test]
        public void NullTrackingDoesNotCreateCleanupWork()
        {
            Assert.IsTrue(Track<GameObject>(null) == null);
            Assert.DoesNotThrow(EndObjectTrackingScope);
        }

        [Test]
        public void TrackingRequiresAnActiveScope()
        {
            EndObjectTrackingScope();
            Assert.Throws<AssertionException>(() => Track<GameObject>(null));
            Assert.Throws<AssertionException>(() => NewGameObject("Refused"));
            BeginObjectTrackingScope();
            Texture2D texture = Track(new Texture2D(2, 2));
            EndObjectTrackingScope();
            Assert.IsTrue(texture == null);
        }

        [Test]
        public void RangeTrackingCleansIndexedAndEnumeratedInputs()
        {
            Texture2D first = new Texture2D(2, 2); // UNH-SUPPRESS UNH002: TrackRange registers this instance below.
            Texture2D second = new Texture2D(2, 2); // UNH-SUPPRESS UNH002: TrackRange registers this instance below.
            TrackRange<Texture2D>(null);
            TrackRange(new[] { first, null });
            TrackRange(Enumerate(second));
            EndObjectTrackingScope();
            Assert.IsTrue(first == null);
            Assert.IsTrue(second == null);
        }

        [Test]
        public void ObjectScopePreservesTheCallersLogPolicy()
        {
            bool previous = LogAssert.ignoreFailingMessages;
            try
            {
                LogAssert.ignoreFailingMessages = true;
                EndObjectTrackingScope();
                Assert.IsTrue(LogAssert.ignoreFailingMessages);
                BeginObjectTrackingScope();
                Assert.IsTrue(LogAssert.ignoreFailingMessages);
            }
            finally
            {
                LogAssert.ignoreFailingMessages = previous;
            }
        }

        [Test]
        public void PersistentAssetRefusalLeavesTheAssetIntact()
        {
            SynchronousEditorTestAsset asset =
                ScriptableObject.CreateInstance<SynchronousEditorTestAsset>();
            string path = AssetDatabase.GenerateUniqueAssetPath(
                "Assets/SynchronousEditorScope.asset"
            );
            try
            {
                AssetDatabase.CreateAsset(asset, path);
                Assert.IsTrue(EditorUtility.IsPersistent(asset));
                Assert.Throws<AssertionException>(() => Track(asset));
                EndObjectTrackingScope();
                Assert.IsTrue(asset != null);
                Assert.IsTrue(EditorUtility.IsPersistent(asset));
            }
            finally
            {
                AssetDatabase.DeleteAsset(path);
                if (asset != null)
                {
                    Object.DestroyImmediate(asset);
                }
            }
        }
    }
}
