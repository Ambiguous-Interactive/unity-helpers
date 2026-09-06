// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Validation
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEditorInternal;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;
    using WallstopStudios.UnityHelpers.Editor.Styles;
    using WallstopStudios.UnityHelpers.Editor.Validation.Continuous;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.Editor.Capture;

    [TestFixture]
    public sealed class SentinelSurfaceCaptureTests : CommonTestBase
    {
        [UnityTest]
        public IEnumerator CaptureBothActualEditorSkins()
        {
            string token = Environment.GetEnvironmentVariable("WALLSTOP_SENTINEL_CAPTURE_TOKEN");
            if (string.IsNullOrEmpty(token))
                Assert.Ignore("Requires the owned Sentinel capture campaign.");
            Assert.IsTrue(Application.isBatchMode);
            Assert.IsTrue(Guid.TryParseExact(token, "N", out Guid _));
            string project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            Assert.AreEqual(
                project,
                Environment.GetEnvironmentVariable("WALLSTOP_SENTINEL_CAPTURE_PROJECT")
            );
            Assert.AreEqual(
                token,
                File.ReadAllText(Path.Combine(project, ".sentinel-capture-disposable"))
            );
            Assert.IsTrue(EditorSurfaceCapture.IsSupported, EditorSurfaceCapture.UnsupportedReason);
            Assert.IsTrue(
                EditorTheme.Load("EditorTheme.uss") != null,
                "The shared theme must load from the consumed package."
            );
            Assert.IsTrue(
                EditorTheme.Load("ValidationWindow.uss") != null,
                "The workspace stylesheet must load from the consumed package."
            );
            string directory = Environment.GetEnvironmentVariable(
                "WALLSTOP_SENTINEL_CAPTURE_OUTPUT"
            );
            Assert.IsFalse(string.IsNullOrWhiteSpace(directory));
            Directory.CreateDirectory(directory);
            bool previous = EditorGUIUtility.isProSkin;
            try
            {
                foreach (bool dark in new[] { true, false })
                {
                    if (EditorGUIUtility.isProSkin != dark)
                    {
                        InternalEditorUtility.SwitchSkinAndRepaintAllViews();
                        yield return null;
                    }
                    Assert.AreEqual(
                        dark,
                        EditorGUIUtility.isProSkin,
                        "Both-skin evidence requires the requested actual editor skin."
                    );
                    string skin = dark ? "dark" : "light";
                    AssertRenderControl(directory, skin, "red", Color.red);
                    AssertRenderControl(directory, skin, "green", Color.green);
                    CaptureCurrentSkin(directory);
                    Debug.Log(
                        "UH_SENTINEL_CAPTURE unity="
                            + Application.unityVersion
                            + " graphics="
                            + SystemInfo.graphicsDeviceType
                            + " skin="
                            + skin
                            + " images=6"
                    );
                }
            }
            finally
            {
                if (EditorGUIUtility.isProSkin != previous)
                    InternalEditorUtility.SwitchSkinAndRepaintAllViews();
            }
            yield return null;
            Assert.AreEqual(previous, EditorGUIUtility.isProSkin);
        }

        /// <summary>Captures each actual validation view through the editor's offscreen panel renderer.</summary>
        private void CaptureCurrentSkin(string directory)
        {
            bool previousAuto = ValidationAutoRun.Enabled;
            bool previouslyChecked = ValidationResults.HasRun;
            ValidationAutoRun.Enabled = false;
            string folder = "Assets/SentinelCapture" + Guid.NewGuid().ToString("N");
            string guid = string.Empty;
            List<ValidationWorkspaceSettings.RuleDefinition> definitions =
                new List<ValidationWorkspaceSettings.RuleDefinition>();
            GameObject subject = null;
            try
            {
                Assert.IsNotEmpty(
                    AssetDatabase.CreateFolder("Assets", folder.Substring("Assets/".Length))
                );
                subject = Track(new GameObject("Capture subject"));
                subject.AddComponent<Rigidbody>().mass = 20;
                string path = folder + "/Heavy body.prefab";
                Assert.IsTrue(PrefabUtility.SaveAsPrefabAsset(subject, path) != null);
                guid = AssetDatabase.AssetPathToGUID(path);
                List<IValidationRule> rules = new List<IValidationRule>();
                foreach (
                    ValidationSeverity severity in new[]
                    {
                        ValidationSeverity.Error,
                        ValidationSeverity.Warning,
                        ValidationSeverity.Info,
                    }
                )
                {
                    ValidationWorkspaceSettings.RuleDefinition definition =
                        new ValidationWorkspaceSettings.RuleDefinition
                        {
                            id = "project.capture." + severity,
                            name = "Body mass " + severity,
                            pathFilter = folder,
                            severity = severity,
                            message =
                                "Rigidbody mass exceeds the " + severity + " review threshold",
                            fix = "Remove component",
                            checks = new List<ValidationWorkspaceSettings.RuleCondition>
                            {
                                new ValidationWorkspaceSettings.RuleCondition
                                {
                                    property = "Rigidbody.mass",
                                    comparison = ">",
                                    value = "10",
                                },
                            },
                        };
                    definitions.Add(definition);
                    ValidationWorkspaceSettings.instance.projectRules.Add(definition);
                    rules.Add(new ValidationProjectRule(definition));
                }
                ValidationRun run = new ValidationRun(
                    rules,
                    new[] { new ValidationTarget(guid, path, typeof(GameObject)) }
                );
                while (!run.Step(double.MaxValue)) { }
                Assert.IsEmpty(run.Failures);
                Assert.AreEqual(3, run.Findings.Count);
                ValidationResults.MergeScopedRun(run);
                CapturePreparedCurrentSkin(directory);
            }
            finally
            {
                foreach (ValidationWorkspaceSettings.RuleDefinition definition in definitions)
                    ValidationWorkspaceSettings.instance.projectRules.Remove(definition);
                ValidationResults.Forget(guid);
                if (!previouslyChecked)
                    ValidationResults.Clear();
                if (subject != null)
                    UnityEngine.Object.DestroyImmediate(subject); // UNH-SUPPRESS UNH001: remove this skin's subject before deleting its owned assets.
                AssetDatabase.DeleteAsset(folder);
                ValidationAutoRun.Enabled = previousAuto;
            }
        }

        private void CapturePreparedCurrentSkin(string directory)
        {
            string skin = EditorGUIUtility.isProSkin ? "dark" : "light";
            Directory.CreateDirectory(directory);
            ValidationWindow window = Track(ScriptableObject.CreateInstance<ValidationWindow>());
            EditorSurfaceCaptureHostWindow host = null;
            try
            {
                host = EditorSurfaceCaptureHostWindow.Create(1280, 720);
                host.rootVisualElement.Add(window.rootVisualElement);
                Assert.IsTrue(window.rootVisualElement.panel != null);
                foreach (string view in new[] { "Issues", "Rules", "Builder", "Settings" })
                {
                    window.PrepareCapture(view);
                    CaptureRoot(
                        window.rootVisualElement,
                        Path.Combine(
                            directory,
                            "after-" + view.ToLowerInvariant() + "-" + skin + ".png"
                        )
                    );
                }
                window.PrepareCapture("Builder");
                DropdownField fix = window
                    .rootVisualElement.Query<DropdownField>()
                    .ToList()
                    .Single(field => field.label == "Auto-fix");
                fix.value = ValidationWorkspaceSettings.RenameToPatternFix;
                TextField fixValue = window.rootVisualElement.Q<TextField>("builder-fix-value");
                Assert.IsTrue(fixValue != null);
                Assert.IsFalse(fixValue.ClassListContains("dx-hidden"));
                fixValue.value = "Reviewed-{name}";
                CaptureRoot(
                    window.rootVisualElement,
                    Path.Combine(directory, "after-builder-fix-" + skin + ".png")
                );
                window.PrepareCapture("Builder", true);
                CaptureRoot(
                    window.rootVisualElement,
                    Path.Combine(directory, "after-graph-" + skin + ".png")
                );
            }
            finally
            {
                window.rootVisualElement.RemoveFromHierarchy();
                EditorSurfaceCaptureHostWindow.CloseHost(host);
                if (window != null)
                    UnityEngine.Object.DestroyImmediate(window); // UNH-SUPPRESS UNH001: this batch window has no native parent to close.
            }
        }

        private void AssertRenderControl(string directory, string skin, string name, Color expected)
        {
            VisualElement control = new VisualElement();
            control.style.width = 64;
            control.style.height = 64;
            control.style.backgroundColor = expected;
            string path = Path.Combine(directory, "control-" + name + "-" + skin + ".png");
            EditorSurfaceCaptureResult result = EditorSurfaceCapture.Capture(control, 64, 64, path);
            Assert.AreEqual(0, result.RenderErrorCount, result.RenderErrorSummary);
            Texture2D decoded = Track(new Texture2D(2, 2, TextureFormat.RGB24, false));
            try
            {
                Assert.IsTrue(ImageConversion.LoadImage(decoded, File.ReadAllBytes(path)));
                Color pixel = decoded.GetPixel(decoded.width / 2, decoded.height / 2);
                Assert.That(pixel.r, Is.EqualTo(expected.r).Within(1f / 255f));
                Assert.That(pixel.g, Is.EqualTo(expected.g).Within(1f / 255f));
                Assert.That(pixel.b, Is.EqualTo(expected.b).Within(1f / 255f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(decoded); // UNH-SUPPRESS UNH001: release each readback before capturing the next surface.
            }
        }

        private static void CaptureRoot(VisualElement root, string path)
        {
            VisualElement surface = new VisualElement();
            surface.style.width = 1280;
            surface.style.height = 720;
            surface.style.paddingLeft = root.style.paddingLeft;
            surface.style.paddingRight = root.style.paddingRight;
            surface.style.paddingTop = root.style.paddingTop;
            surface.style.paddingBottom = root.style.paddingBottom;
            surface.style.backgroundColor = root.style.backgroundColor;
            foreach (string className in root.GetClasses())
                surface.AddToClassList(className);
            for (int index = 0; index < root.styleSheets.count; index++)
                surface.styleSheets.Add(root.styleSheets[index]);
            while (0 < root.childCount)
                surface.Add(root.ElementAt(0));
            EditorSurfaceCaptureResult result = EditorSurfaceCapture.Capture(
                surface,
                1280,
                720,
                path
            );
            Assert.AreEqual(0, result.RenderErrorCount, result.RenderErrorSummary);
            Assert.IsTrue(File.Exists(path));
            Assert.IsTrue(
                16 < result.DistinctColorCount,
                "The tool must draw more than a blank or control surface."
            );
        }
    }
}
