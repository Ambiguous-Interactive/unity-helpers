// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.TestUtils
{
#if UNITY_EDITOR
    using System.IO;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Tests.Core;

    /// <summary>
    /// Base class for tests that use golden file comparisons for verification.
    /// Golden files are pre-generated expected outputs that tests compare against.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Golden file testing is useful for:
    /// </para>
    /// <list type="bullet">
    /// <item>Verifying complex output structures (JSON, generated code, metadata)</item>
    /// <item>Detecting unintended changes in output format</item>
    /// <item>Creating human-reviewable expected outputs</item>
    /// </list>
    /// <para>
    /// To regenerate golden files, run the <see cref="GenerateGoldenFiles"/> test method
    /// marked with <c>[Explicit]</c>. This should only be done when the expected output
    /// has intentionally changed.
    /// </para>
    /// </remarks>
    public abstract class GoldenFileTestBase : CommonTestBase
    {
        /// <summary>
        /// The root directory for all golden output files (relative to project root).
        /// </summary>
        protected const string GoldenOutputRootDir =
            "Packages/com.wallstop-studios.unity-helpers/Tests/Editor/TestAssets/GoldenFiles";

        /// <summary>
        /// Gets the subdirectory name for golden files for this test class.
        /// Override to provide a custom subdirectory name.
        /// </summary>
        /// <remarks>
        /// By default, returns the test class name without the "Tests" suffix.
        /// For example, "SpriteExtractorTests" would return "SpriteExtractor".
        /// </remarks>
        protected virtual string GoldenFileSubdirectory
        {
            get
            {
                string className = GetType().Name;
                if (className.EndsWith("Tests"))
                {
                    return className.Substring(0, className.Length - 5);
                }
                return className;
            }
        }

        /// <summary>
        /// Gets the full path to the golden files directory for this test class.
        /// </summary>
        protected string GoldenFileDirectory => GoldenOutputRootDir + "/" + GoldenFileSubdirectory;

        private static string GetAbsolutePath(string unityPath)
        {
            string projectRoot = Application.dataPath.Substring(
                0,
                Application.dataPath.Length - "Assets".Length
            );
            return Path.Combine(projectRoot, unityPath).SanitizePath();
        }

        /// <summary>
        /// Generates golden files for this test class. Override this method to create
        /// the expected outputs for your tests.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This method is marked with <c>[Test]</c> and <c>[Explicit]</c>, meaning it
        /// will only run when explicitly selected in the test runner.
        /// </para>
        /// <para>
        /// <strong>Important:</strong> Only run this when you intentionally want to update
        /// the expected outputs. Review the generated files carefully before committing.
        /// </para>
        /// </remarks>
        [Test]
        [Explicit("Run manually to regenerate golden files")]
        public virtual void GenerateGoldenFiles()
        {
            Assert.Inconclusive(
                $"No golden file generation implemented for {GetType().Name}. "
                    + "Override GenerateGoldenFiles() to generate expected outputs."
            );
        }

        /// <summary>
        /// Loads golden metadata from a JSON file in the golden files directory.
        /// </summary>
        /// <typeparam name="T">The type to deserialize the JSON into.</typeparam>
        /// <param name="fileName">The file name (with extension) in the golden files directory.</param>
        /// <returns>The deserialized object, or default(T) if the file doesn't exist or fails to deserialize.</returns>
        protected T LoadGoldenMetadata<T>(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
            {
                return default;
            }

            string path = GoldenFileDirectory + "/" + fileName;
            TextAsset asset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            if (asset == null)
            {
                Debug.LogWarning(
                    $"[{nameof(GoldenFileTestBase)}] Golden file not found: {path}. "
                        + "Run GenerateGoldenFiles() to create it."
                );
                return default;
            }

            try
            {
                return JsonUtility.FromJson<T>(asset.text);
            }
            catch (System.Exception ex)
            {
                Debug.LogError(
                    $"[{nameof(GoldenFileTestBase)}] Failed to deserialize golden file {path}: {ex.Message}"
                );
                return default;
            }
        }

        /// <summary>
        /// Loads raw text from a golden file.
        /// </summary>
        /// <param name="fileName">The file name (with extension) in the golden files directory.</param>
        /// <returns>The file contents, or null if the file doesn't exist.</returns>
        protected string LoadGoldenText(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
            {
                return null;
            }

            string path = GoldenFileDirectory + "/" + fileName;
            TextAsset asset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            if (asset == null)
            {
                Debug.LogWarning(
                    $"[{nameof(GoldenFileTestBase)}] Golden file not found: {path}. "
                        + "Run GenerateGoldenFiles() to create it."
                );
                return null;
            }

            return asset.text;
        }

        /// <summary>
        /// Saves metadata to a golden file. Use this in <see cref="GenerateGoldenFiles"/> to create expected outputs.
        /// </summary>
        /// <typeparam name="T">The type to serialize to JSON.</typeparam>
        /// <param name="fileName">The file name (with extension) in the golden files directory.</param>
        /// <param name="metadata">The metadata to serialize.</param>
        /// <param name="prettyPrint">Whether to format the JSON with indentation (default: true).</param>
        protected void SaveGoldenMetadata<T>(string fileName, T metadata, bool prettyPrint = true)
        {
            if (string.IsNullOrEmpty(fileName) || metadata == null)
            {
                return;
            }

            EnsureGoldenDirectory();

            string json = JsonUtility.ToJson(metadata, prettyPrint);
            string path = GoldenFileDirectory + "/" + fileName;
            string fullPath = GetAbsolutePath(path);

            try
            {
                File.WriteAllText(fullPath, json);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            }
            catch (System.Exception ex)
            {
                Debug.LogError(
                    $"[{nameof(GoldenFileTestBase)}] Failed to save golden file {path}: {ex.Message}"
                );
            }
        }

        /// <summary>
        /// Saves raw text to a golden file. Use this in <see cref="GenerateGoldenFiles"/> to create expected outputs.
        /// </summary>
        /// <param name="fileName">The file name (with extension) in the golden files directory.</param>
        /// <param name="content">The text content to save.</param>
        protected void SaveGoldenText(string fileName, string content)
        {
            if (string.IsNullOrEmpty(fileName) || content == null)
            {
                return;
            }

            EnsureGoldenDirectory();

            string path = GoldenFileDirectory + "/" + fileName;
            string fullPath = GetAbsolutePath(path);

            try
            {
                File.WriteAllText(fullPath, content);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            }
            catch (System.Exception ex)
            {
                Debug.LogError(
                    $"[{nameof(GoldenFileTestBase)}] Failed to save golden file {path}: {ex.Message}"
                );
            }
        }

        /// <summary>
        /// Compares actual content against a golden file and asserts equality.
        /// </summary>
        /// <param name="fileName">The golden file name to compare against.</param>
        /// <param name="actualContent">The actual content to compare.</param>
        /// <param name="message">Optional assertion message.</param>
        protected void AssertMatchesGoldenFile(
            string fileName,
            string actualContent,
            string message = null
        )
        {
            string expected = LoadGoldenText(fileName);
            Assert.IsTrue(
                expected != null,
                $"Golden file '{fileName}' not found. Run GenerateGoldenFiles() to create it."
            );
            Assert.AreEqual(expected, actualContent, message ?? $"Content mismatch for {fileName}");
        }

        private void EnsureGoldenDirectory()
        {
            if (AssetDatabase.IsValidFolder(GoldenFileDirectory))
            {
                return;
            }

            string[] parts = GoldenFileDirectory.Split('/');
            string currentPath = parts[0];

            for (int i = 1; i < parts.Length; i++)
            {
                string nextPath = currentPath + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(nextPath))
                {
                    AssetDatabase.CreateFolder(currentPath, parts[i]);
                }
                currentPath = nextPath;
            }
        }
    }
#endif
}
