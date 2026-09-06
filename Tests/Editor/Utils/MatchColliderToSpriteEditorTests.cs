// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Utils
{
#if UNITY_EDITOR
    using System.Collections;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Editor.CustomEditors;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Utils;

    [TestFixture]
    public sealed class MatchColliderToSpriteEditorTests : CommonTestBase
    {
        [TearDown]
        public override void TearDown()
        {
            Undo.ClearAll();
            base.TearDown();
            CleanupTrackedFoldersAndAssets();
        }

        [Test]
        public void InspectorSettingsRecordColliderPrefabOverrides()
        {
            const string folder = "Assets/Temp/MatchColliderToSpriteEditorTests";
            EnsureFolder(folder);
            string path = folder + "/Collider.prefab";
            TrackAssetPath(path);
            GameObject source = Track(
                new GameObject("Sprite prefab", typeof(PolygonCollider2D), typeof(SpriteRenderer))
            );
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(source, path);
            Assert.IsTrue(prefab != null);
            GameObject instance = Track(PrefabUtility.InstantiatePrefab(prefab) as GameObject);
            Assert.IsTrue(instance != null);
            Texture2D texture = Track(new Texture2D(2, 2, TextureFormat.RGBA32, false));
            texture.SetPixels32(
                new[] { new Color32(255, 255, 255, 255), default, default, default }
            );
            texture.Apply();
            Sprite sprite = Track(Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.zero, 1));
            instance.GetComponent<SpriteRenderer>().sprite = sprite;
            MatchColliderToSprite matcher = instance.AddComponent<MatchColliderToSprite>();
            matcher.RebuildCollider();
            MatchColliderToSpriteEditor editor =
                Track(UnityEditor.Editor.CreateEditor(matcher)) as MatchColliderToSpriteEditor;
            Assert.IsTrue(editor != null);
            editor.serializedObject.Update();
            editor
                .serializedObject.FindProperty(nameof(MatchColliderToSprite.traceExactly))
                .boolValue = true;
            editor.ApplyInspectorProperties();
            PropertyModification[] modifications = PrefabUtility.GetPropertyModifications(instance);
            Assert.IsTrue(modifications != null);
            bool colliderRecorded = false;
            foreach (PropertyModification modification in modifications)
            {
                if (modification.target is PolygonCollider2D)
                {
                    colliderRecorded = true;
                }
            }
            Assert.IsTrue(colliderRecorded);
            CollectionAssert.AreEquivalent(
                new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up },
                matcher.polygonCollider.GetPath(0)
            );
        }

        [UnityTest]
        public IEnumerator SerializedSettingsPreserveGeometryUntilExplicitRebuild()
        {
            Texture2D texture = Track(new Texture2D(1, 1, TextureFormat.RGBA32, false));
            texture.SetPixel(0, 0, Color.white);
            texture.Apply();
            Sprite sprite = Track(Sprite.Create(texture, new Rect(0, 0, 1, 1), Vector2.zero, 1));
            GameObject gameObject = Track(
                new GameObject(
                    "Explicit collider rebuild",
                    typeof(PolygonCollider2D),
                    typeof(SpriteRenderer)
                )
            );
            gameObject.GetComponent<SpriteRenderer>().sprite = sprite;
            MatchColliderToSprite matcher = gameObject.AddComponent<MatchColliderToSprite>();
            matcher.OnValidate();
            Vector2[] previous = { new(-3, -3), new(-2, -3), new(-3, -2) };
            matcher.polygonCollider.pathCount = 1;
            matcher.polygonCollider.SetPath(0, previous);
            using SerializedObject serialized = new(matcher);
            serialized.FindProperty(nameof(MatchColliderToSprite.traceExactly)).boolValue = true;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            matcher.OnValidate();
            CollectionAssert.AreEqual(previous, matcher.polygonCollider.GetPath(0));
            yield return null;
            CollectionAssert.AreEqual(previous, matcher.polygonCollider.GetPath(0));
            matcher.RebuildCollider();
            CollectionAssert.AreEquivalent(
                new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up },
                matcher.polygonCollider.GetPath(0)
            );
        }

        [UnityTest]
        public IEnumerator InspectorSettingsUndoRestoresActualPreviousCollider()
        {
            foreach (
                (bool changeCollider, int editCount) in new[] { (false, 1), (true, 1), (false, 2) }
            )
            {
                Texture2D texture = Track(new Texture2D(4, 4, TextureFormat.RGBA32, false));
                Color32[] pixels = new Color32[16];
                pixels[5] = new Color32(255, 255, 255, 255);
                texture.SetPixels32(pixels);
                texture.Apply();
                Sprite sprite = Track(
                    Sprite.Create(texture, new Rect(0, 0, 4, 4), Vector2.zero, 1)
                );
                sprite.name = "UndoSpriteArt";
                GameObject gameObject = Track(
                    new GameObject("Sprite undo", typeof(PolygonCollider2D), typeof(SpriteRenderer))
                );
                gameObject.GetComponent<SpriteRenderer>().sprite = sprite;
                MatchColliderToSprite matcher = gameObject.AddComponent<MatchColliderToSprite>();
                matcher.RebuildCollider();
                PolygonCollider2D originalCollider = matcher.polygonCollider;
                PolygonCollider2D editedCollider = originalCollider;
                if (changeCollider)
                {
                    editedCollider = Track(
                            new GameObject("Target collider", typeof(PolygonCollider2D))
                        )
                        .GetComponent<PolygonCollider2D>();
                }
                Vector2[] previous = { new(-3, -3), new(-2, -3), new(-3, -2) };
                editedCollider.pathCount = 1;
                editedCollider.SetPath(0, previous);
                MatchColliderToSpriteEditor editor =
                    Track(UnityEditor.Editor.CreateEditor(matcher)) as MatchColliderToSpriteEditor;
                Assert.IsTrue(editor != null);
                Undo.IncrementCurrentGroup();
                int group = Undo.GetCurrentGroup();
                editor.serializedObject.Update();
                editor
                    .serializedObject.FindProperty(nameof(MatchColliderToSprite.traceExactly))
                    .boolValue = true;
                editor
                    .serializedObject.FindProperty(nameof(MatchColliderToSprite.polygonCollider))
                    .objectReferenceValue = editedCollider;
                editor.ApplyInspectorProperties();
                if (editCount == 2)
                {
                    Undo.IncrementCurrentGroup();
                    editor.serializedObject.Update();
                    editor
                        .serializedObject.FindProperty(
                            nameof(MatchColliderToSprite.minimumTracedArea)
                        )
                        .floatValue = 2f;
                    editor.ApplyInspectorProperties();
                }
                Vector2[] exact = { new(1, 1), new(2, 1), new(2, 2), new(1, 2) };
                int expectedPaths = editCount == 2 ? 0 : 1;
                AssertFinalGeometry(editedCollider, expectedPaths, exact, "After inspector apply");
                Undo.FlushUndoRecordObjects();
                Undo.CollapseUndoOperations(group);
                Undo.PerformUndo();
                Assert.IsFalse(matcher.traceExactly);
                Assert.AreEqual(0f, matcher.minimumTracedArea, "Area after undo");
                Assert.AreSame(originalCollider, matcher.polygonCollider);
                Assert.AreEqual(1, editedCollider.pathCount, "After undo");
                CollectionAssert.AreEqual(previous, editedCollider.GetPath(0));
                matcher.OnValidate();
                matcher.OnValidate();
                yield return null;
                Assert.AreEqual(
                    1,
                    editedCollider.pathCount,
                    "After repeated validation and delayed undo"
                );
                CollectionAssert.AreEqual(previous, editedCollider.GetPath(0));
                Undo.PerformRedo();
                Assert.IsTrue(matcher.traceExactly);
                Assert.AreEqual(
                    editCount == 2 ? 2f : 0f,
                    matcher.minimumTracedArea,
                    "Area after redo"
                );
                Assert.AreSame(editedCollider, matcher.polygonCollider);
                AssertFinalGeometry(editedCollider, expectedPaths, exact, "After redo");
                matcher.OnValidate();
                matcher.OnValidate();
                yield return null;
                AssertFinalGeometry(
                    editedCollider,
                    expectedPaths,
                    exact,
                    "After repeated validation and delayed redo"
                );
                Undo.PerformUndo();
                CollectionAssert.AreEqual(previous, editedCollider.GetPath(0));
                matcher.RebuildCollider();
                Assert.AreEqual(
                    sprite.GetPhysicsShapeCount(),
                    originalCollider.pathCount,
                    "Explicit rebuild after undo"
                );
                Undo.ClearAll();
            }
        }

        private static void AssertFinalGeometry(
            PolygonCollider2D collider,
            int expectedPaths,
            Vector2[] exact,
            string phase
        )
        {
            Assert.AreEqual(expectedPaths, collider.pathCount, phase);
            if (expectedPaths != 0)
            {
                CollectionAssert.AreEquivalent(exact, collider.GetPath(0), phase);
            }
        }
    }
#endif
}
