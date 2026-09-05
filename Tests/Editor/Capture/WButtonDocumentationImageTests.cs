// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Capture
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.IO;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using Object = UnityEngine.Object;

    /// <summary>
    /// Drives the capture harness over the WButton documentation images and keeps the catalog,
    /// the repository and the manifest agreeing with each other.
    ///
    /// Regeneration is <see cref="ExplicitAttribute"/>: a normal test run must not rewrite files
    /// under <c>docs/</c>. See the manifest at
    /// <c>docs/images/documentation-image-manifest.md</c> for how to run it.
    /// </summary>
    [TestFixture]
    [Category("Editor")]
    public sealed class WButtonDocumentationImageTests : CommonTestBase
    {
        private const string ManifestRelativePath = "docs/images/documentation-image-manifest.md";
        private const string DocumentationFolder = "docs";

        /// <summary>
        /// Smallest image the catalog should ever produce. An inspector that renders as a sliver
        /// is a regression the file-exists assertions cannot see.
        /// </summary>
        private const int MinimumImageEdge = 80;

        private string _outputDirectory;

        internal static IEnumerable<TestCaseData> CatalogImages()
        {
            List<DocumentationImage> images = WButtonDocumentationImageCatalog.BuildImages();
            foreach (
                WallstopStudios.UnityHelpers.Tests.Editor.Capture.DocumentationImage imagesElement in images
            )
            {
                yield return new TestCaseData(imagesElement.RelativePath).SetName(
                    imagesElement.FileName
                );
            }
        }

        [SetUp]
        public override void BaseSetUp()
        {
            base.BaseSetUp();
            _outputDirectory = Path.Combine(
                Path.GetTempPath(),
                "unity-helpers-documentation-images",
                Guid.NewGuid().ToString("N")
            );
            Directory.CreateDirectory(_outputDirectory);
        }

        [TearDown]
        public override void TearDown()
        {
            EditorSurfaceCaptureHostWindow.CloseLeakedHosts();
            if (!string.IsNullOrEmpty(_outputDirectory) && Directory.Exists(_outputDirectory))
            {
                Directory.Delete(_outputDirectory, true);
            }

            base.TearDown();
        }

        [Test]
        public void CatalogIsNotEmpty()
        {
            Assert.IsNotEmpty(
                WButtonDocumentationImageCatalog.BuildImages(),
                "An empty catalog would let every other test in this fixture pass while "
                    + "measuring nothing."
            );
        }

        [Test]
        public void EveryCatalogTargetRendersEditable()
        {
            /*
                HideAndDontSave includes NotEditable and propagates to components, disabling every captured
                inspector field.
            */
            List<Object> owned = new();
            try
            {
                foreach (DocumentationImage image in WButtonDocumentationImageCatalog.BuildImages())
                {
                    foreach (Type targetType in image.TargetTypes)
                    {
                        Object target = WButtonDocumentationImageCatalog.CreateTarget(
                            targetType,
                            owned
                        );

                        Assert.AreEqual(
                            HideFlags.None,
                            target.hideFlags & HideFlags.NotEditable,
                            $"{targetType.Name} would be captured greyed out."
                        );

                        if (target is Component component)
                        {
                            Assert.AreEqual(
                                HideFlags.None,
                                component.gameObject.hideFlags & HideFlags.NotEditable,
                                $"{targetType.Name}'s host would be captured greyed out."
                            );
                        }
                    }
                }
            }
            finally
            {
                for (int index = owned.Count - 1; 0 <= index; index--)
                {
                    Object created = owned[index];
                    if (created != null)
                    {
                        Object.DestroyImmediate(created); // UNH-SUPPRESS: this test owns what it created
                    }
                }
            }
        }

        [Test]
        public void CatalogHasNoDuplicateOutputPaths()
        {
            List<DocumentationImage> images = WButtonDocumentationImageCatalog.BuildImages();
            HashSet<string> seen = new(StringComparer.Ordinal);
            List<string> duplicates = new();
            foreach (
                WallstopStudios.UnityHelpers.Tests.Editor.Capture.DocumentationImage imagesElement in images
            )
            {
                if (!seen.Add(imagesElement.RelativePath))
                {
                    duplicates.Add(imagesElement.RelativePath);
                }
            }

            Assert.IsEmpty(
                duplicates,
                "Two catalog entries write to the same file, so one silently overwrites the other."
            );
        }

        [Test]
        public void EveryCatalogEntryDeclaresAtLeastOneTarget()
        {
            List<DocumentationImage> images = WButtonDocumentationImageCatalog.BuildImages();
            foreach (DocumentationImage image in images)
            {
                Assert.IsTrue(
                    0 < image.TargetTypes.Length,
                    $"{image.RelativePath} declares no inspector to draw."
                );
                Assert.IsTrue(
                    image.RelativePath.EndsWith(".png", StringComparison.Ordinal),
                    $"{image.RelativePath} must name a PNG; animated formats are out of scope."
                );
            }
        }

        [Test]
        public void EveryCatalogImageExistsInTheRepository()
        {
            List<string> missing = new();
            List<DocumentationImage> images = WButtonDocumentationImageCatalog.BuildImages();
            foreach (
                WallstopStudios.UnityHelpers.Tests.Editor.Capture.DocumentationImage imagesElement in images
            )
            {
                string path = ResolveOutputPathOrSkip(imagesElement);
                if (!File.Exists(path))
                {
                    missing.Add(imagesElement.RelativePath);
                }
            }

            Assert.IsEmpty(
                missing,
                "The catalog names documentation images that are not committed. Regenerate them, "
                    + "or drop the entry."
            );
        }

        [Test]
        public void EveryCatalogImageIsReferencedByDocumentation()
        {
            string packageRoot = ResolvePackageRootOrSkip();
            string documentationRoot = Path.Combine(packageRoot, DocumentationFolder);
            string[] pages = Directory.GetFiles(
                documentationRoot,
                "*.md",
                SearchOption.AllDirectories
            );

            List<DocumentationImage> images = WButtonDocumentationImageCatalog.BuildImages();
            Assert.IsTrue(
                0 < pages.Length,
                $"No documentation page was found under {documentationRoot}."
            );
            Assert.IsTrue(0 < images.Count, "The catalog produced no images to check.");

            List<string> unreferenced = new();
            foreach (
                WallstopStudios.UnityHelpers.Tests.Editor.Capture.DocumentationImage imagesElement in images
            )
            {
                if (!IsReferencedByAnyPage(pages, imagesElement.FileName))
                {
                    unreferenced.Add(imagesElement.RelativePath);
                }
            }

            Assert.IsEmpty(
                unreferenced,
                "The catalog regenerates images no documentation page shows, so nobody would "
                    + "notice if they broke."
            );
        }

        [Test]
        public void ManifestListsEveryCatalogImage()
        {
            string packageRoot = ResolvePackageRootOrSkip();
            string manifestPath = Path.Combine(
                packageRoot,
                ManifestRelativePath.Replace('/', Path.DirectorySeparatorChar)
            );
            Assert.IsTrue(
                File.Exists(manifestPath),
                $"The generated-image manifest is missing at {ManifestRelativePath}. Without it "
                    + "nobody can tell which images are automated and which are still manual."
            );

            string manifest = File.ReadAllText(manifestPath);
            List<DocumentationImage> images = WButtonDocumentationImageCatalog.BuildImages();
            List<string> unlisted = new();
            foreach (
                WallstopStudios.UnityHelpers.Tests.Editor.Capture.DocumentationImage imagesElement in images
            )
            {
                if (manifest.IndexOf(imagesElement.FileName, StringComparison.Ordinal) < 0)
                {
                    unlisted.Add(imagesElement.RelativePath);
                }
            }

            Assert.IsEmpty(
                unlisted,
                "The manifest does not list every generated image, so the remaining manual "
                    + "backlog it records is wrong."
            );
        }

        [Test]
        [TestCaseSource(nameof(CatalogImages))]
        public void CatalogEntryRendersNonBlankInspector(string relativePath)
        {
            SkipWithoutGraphicsDevice();

            Assert.IsTrue(
                WButtonDocumentationImageCatalog.TryFindImage(
                    relativePath,
                    out DocumentationImage image
                ),
                $"The catalog no longer contains {relativePath}."
            );

            EditorSurfaceCaptureResult result = CaptureToleratingCursorRectErrors(
                image,
                Path.Combine(_outputDirectory, image.FileName)
            );

            Assert.AreEqual(
                EditorSurfaceCapture.PngTruecolorWithoutAlpha,
                result.PngColorType,
                $"{image.RelativePath} must be truecolor without alpha."
            );
            Assert.IsTrue(
                1 < result.DistinctColorCount,
                $"{image.RelativePath} rendered a single flat color, which means the inspector "
                    + "drew nothing into the capture."
            );
            Assert.IsTrue(
                MinimumImageEdge <= result.Width && MinimumImageEdge <= result.Height,
                $"{image.RelativePath} rendered {result.Width}x{result.Height}, which is too "
                    + "small to be a real inspector."
            );
            Assert.AreEqual(
                0,
                EditorSurfaceCaptureHostWindow.LiveHostCount,
                $"{image.RelativePath} leaked its capture host window."
            );
            Assert.IsTrue(
                result.OnlyCursorRectErrors,
                $"{image.RelativePath} logged an error the capture technique does not explain: "
                    + result.RenderErrorSummary
            );
        }

        [Test]
        [Explicit(
            "Rewrites committed files under docs/images. Run it deliberately, in an editor with "
                + "a graphics device."
        )]
        [Category("DocumentationCapture")]
        public void RegenerateDocumentationImages()
        {
            SkipWithoutGraphicsDevice();

            List<DocumentationImage> images = WButtonDocumentationImageCatalog.BuildImages();
            foreach (DocumentationImage image in images)
            {
                EditorSurfaceCaptureResult result = CaptureToleratingCursorRectErrors(
                    image,
                    ResolveOutputPathOrSkip(image)
                );
                Assert.IsTrue(
                    1 < result.DistinctColorCount,
                    $"Refusing to accept a blank capture for {image.RelativePath}."
                );
                // The operator running this [Explicit] regeneration needs to see what was written.
                Debug.Log($"[documentation-capture] {result}");
            }
        }

        /// <summary>
        /// Captures one entry while tolerating the single benign error this technique provokes.
        /// Driving a panel outside an editor view makes every drawer that asks for a cursor rect
        /// log one; the Unity Test Framework fails a test on any unexpected error, so the
        /// tolerance is scoped to the capture call and the result is then asserted to contain
        /// nothing but that message.
        /// </summary>
        private static EditorSurfaceCaptureResult CaptureToleratingCursorRectErrors(
            DocumentationImage image,
            string outputPath
        )
        {
            bool previousIgnore = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;
            try
            {
                return WButtonDocumentationImageCatalog.Capture(image, outputPath);
            }
            finally
            {
                LogAssert.ignoreFailingMessages = previousIgnore;
            }
        }

        private static bool IsReferencedByAnyPage(string[] pages, string fileName)
        {
            foreach (string pagesElement in pages)
            {
                string page = File.ReadAllText(pagesElement);
                if (0 <= page.IndexOf(fileName, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static void SkipWithoutGraphicsDevice()
        {
            if (!EditorSurfaceCapture.IsSupported)
            {
                Assert.Ignore(EditorSurfaceCapture.UnsupportedReason);
            }
        }

        private static string ResolvePackageRootOrSkip()
        {
            string packageRoot = WButtonDocumentationImageCatalog.ResolvePackageRoot();
            if (string.IsNullOrEmpty(packageRoot) || !Directory.Exists(packageRoot))
            {
                Assert.Ignore(
                    "The package root could not be located from the test sources, so there is no "
                        + "docs/ tree to compare against."
                );
            }

            return packageRoot;
        }

        private static string ResolveOutputPathOrSkip(DocumentationImage image)
        {
            string path = WButtonDocumentationImageCatalog.ResolveOutputPath(image);
            if (string.IsNullOrEmpty(path))
            {
                Assert.Ignore(
                    "The package root could not be located from the test sources, so there is no "
                        + "docs/ tree to write to."
                );
            }

            return path;
        }
    }
#endif
}
