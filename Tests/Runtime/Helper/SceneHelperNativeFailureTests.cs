// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Helper
{
#if UNITY_EDITOR
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.IO;
    using System.Runtime.CompilerServices;
    using System.Text.RegularExpressions;
    using System.Threading.Tasks;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.SceneManagement;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [Category("Fast")]
    public sealed class SceneHelperNativeFailureTests : CommonTestBase
    {
        private const string PackedScenePath =
            "Packages/com.wallstop-studios.unity-helpers/Tests/Runtime/Scenes/Test1.unity";

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static (
            SceneHelper.SceneLoadScope scope,
            WeakReference payload,
            int[] callbacks
        ) CreateCapturedScope(string path)
        {
            object payload = new object();
            int[] callbacks = new int[1];
            SceneHelper.SceneLoadScope scope = new(
                path,
                (scene, mode) =>
                {
                    ++callbacks[0];
                    GC.KeepAlive(payload);
                }
            );
            return (scope, new WeakReference(payload), callbacks);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference CreateFailedScopeReference(string path)
        {
            SceneHelper.SceneLoadScope scope = new(path, null);
            Assert.IsTrue(scope.LoadTask.IsFaulted);
            Assert.IsTrue(scope.LoadTask.Exception != null);
            return new WeakReference(scope);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference CreateUnrootedPayload()
        {
            return new WeakReference(new object());
        }

        [UnityTest]
        public IEnumerator NativeLoadFailureSettlesAndReleasesCallbackAndHandler()
        {
            Assert.IsTrue(Application.isPlaying);
            Scene original = SceneManager.GetActiveScene();
            Scene[] existing = new Scene[SceneManager.sceneCount];
            for (int index = 0; index < existing.Length; ++index)
            {
                existing[index] = SceneManager.GetSceneAt(index);
            }
            object rootedControl = new object();
            WeakReference rooted = new(rootedControl);
            WeakReference released = CreateUnrootedPayload();
            for (int iteration = 0; iteration < 3; ++iteration)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                yield return null;
            }
            Assert.IsTrue(rooted.IsAlive);
            Assert.IsFalse(released.IsAlive);
            string folder = "Assets/SceneFailureProbe" + Guid.NewGuid().ToString("N");
            EnsureFolder(folder);
            string assetPath = folder + "/Owned.unity";
            TrackAssetPath(assetPath);
            UnityEditor.PackageManager.PackageInfo package =
                UnityEditor.PackageManager.PackageInfo.FindForAssetPath(PackedScenePath);
            Assert.IsTrue(package != null);
            string source = Path.Combine(package.resolvedPath, "Tests/Runtime/Scenes/Test1.unity");
            Assert.IsTrue(TryCopyAssetSilent(source, assetPath));
            Assert.AreEqual(typeof(SceneAsset), AssetDatabase.GetMainAssetTypeAtPath(assetPath));
            string absolute = Path.Combine(Path.GetDirectoryName(Application.dataPath), assetPath);
            string backup = absolute + ".held";
            List<string> errors = new();
            Application.LogCallback observe = (message, trace, type) =>
            {
                if (
                    (type == LogType.Error || type == LogType.Exception)
                    && message.Contains(assetPath)
                )
                {
                    errors.Add(message);
                }
            };
            Application.logMessageReceived += observe;
            string expectedError =
                "LoadSceneInPlayMode expects a valid full path. The provided path was '"
                + assetPath
                + "'.";
            AssetDatabase.DisallowAutoRefresh();
            SceneHelper.SceneLoadScope scope;
            WeakReference callbackPayload;
            WeakReference failedScope;
            int[] callbacks;
            try
            {
                File.Move(absolute, backup);
                Assert.AreEqual(
                    typeof(SceneAsset),
                    AssetDatabase.GetMainAssetTypeAtPath(assetPath),
                    "The helper must reach Unity's load call, not reject an unavailable input."
                );
                LogAssert.Expect(LogType.Error, new Regex("^" + Regex.Escape(expectedError) + "$"));
                (scope, callbackPayload, callbacks) = CreateCapturedScope(assetPath);
                TrackAsyncDisposal(async () =>
                {
                    try
                    {
                        await scope.DisposeAsync();
                    }
                    catch (Exception exception)
                        when (ReferenceEquals(exception, scope.LoadTask.Exception?.InnerException))
                    { }
                });
                Assert.IsTrue(
                    scope.LoadTask.IsFaulted,
                    "The native missing-source load must fault."
                );
                LogAssert.Expect(LogType.Error, new Regex("^" + Regex.Escape(expectedError) + "$"));
                failedScope = CreateFailedScopeReference(assetPath);
            }
            finally
            {
                try
                {
                    try
                    {
                        if (File.Exists(backup))
                        {
                            File.Move(backup, absolute);
                        }
                    }
                    finally
                    {
                        AssetDatabase.AllowAutoRefresh();
                    }
                    AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
                }
                finally
                {
                    Application.logMessageReceived -= observe;
                }
            }
            Exception loadFailure = scope.LoadTask.Exception.InnerException;
            Assert.IsTrue(loadFailure != null);
            Assert.AreEqual(0, callbacks[0]);
            Task disposal = scope.DisposeAsync().AsTask();
            Assert.IsTrue(disposal.IsFaulted);
            Assert.AreSame(loadFailure, disposal.Exception.InnerException);
            Assert.AreSame(disposal, scope.DisposeAsync().AsTask());
            for (int iteration = 0; iteration < 3; ++iteration)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                yield return null;
            }
            Assert.IsFalse(
                callbackPayload.IsAlive,
                "A retained failed scope must release its callback capture."
            );
            Assert.IsFalse(
                failedScope.IsAlive,
                "The failed scope must not remain rooted by sceneLoaded before disposal."
            );
            Assert.AreEqual(0, callbacks[0]);
            Assert.AreEqual(original, SceneManager.GetActiveScene());
            Assert.AreEqual(existing.Length, SceneManager.sceneCount);
            foreach (Scene scene in existing)
            {
                Assert.IsTrue(scene.isLoaded);
            }
            TestContext.WriteLine(
                "Native scene-load failure: "
                    + loadFailure.Message
                    + "; owned errors: "
                    + string.Join(" | ", errors)
            );
            GC.KeepAlive(scope);
            GC.KeepAlive(rootedControl);
        }
    }
#endif
}
