// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core.TestUtils
{
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
        public void CleanupDestroysAllTransientObjectKindsAndChildren()
        {
            SynchronousEditorTestAsset asset = CreateScriptableObject<SynchronousEditorTestAsset>();
            Texture2D texture = Track(new Texture2D(2, 2));
            GameObject parent = Track(new GameObject(nameof(parent)));
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
