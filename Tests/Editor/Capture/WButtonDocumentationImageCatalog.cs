// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Capture
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.IO;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Tests.Capture.Targets;
    using Object = UnityEngine.Object;

    /// <summary>
    /// The documentation images this repository generates rather than hand-captures.
    ///
    /// The group is the WButton inspector surfaces under <c>docs/images/inspector/buttons/</c>.
    /// They were chosen because every one of them is a real inspector this package draws for an
    /// object it owns, so the harness can reproduce them end to end from a target type and
    /// nothing else. The three project-settings images in the same folder are a
    /// <c>SettingsProvider</c> surface rather than an inspector, and the animated files are out of
    /// scope for a still-image harness; both remain manual and are listed in the manifest at
    /// <c>docs/images/documentation-image-manifest.md</c>.
    /// </summary>
    internal static class WButtonDocumentationImageCatalog
    {
        /// <summary>
        /// One inspector column, wide enough that the guide's longest button labels do not wrap.
        /// </summary>
        private const float InspectorColumnWidth = 620f;

        /// <summary>Half-width columns, for the images that show two inspectors side by side.</summary>
        private const float ComparisonColumnWidth = 340f;

        private const float InspectorLabelWidth = 160f;

        /// <summary>
        /// A canvas tall enough for the longest inspector in the catalog. The image is cropped to
        /// the surface, so slack here costs nothing but video memory during the capture.
        /// </summary>
        private const int TallCanvasHeight = 1100;

        private const int StandardCanvasWidth = 760;
        private const int ComparisonCanvasWidth = 900;

        /// <summary>The <c>docs/images</c> folder, relative to the package root.</summary>
        private const string DocumentationImageFolder = "docs/images";

        internal static List<DocumentationImage> BuildImages()
        {
            List<DocumentationImage> images = new(13)
            {
                Single("inspector/buttons/button-overview.png", typeof(WButtonOverviewExample)),
                Single("inspector/buttons/button-order.png", typeof(ButtonPositioning)),
                Single("inspector/buttons/button-groupings.png", typeof(ButtonGrouping)),
                Single("inspector/buttons/button-colorings.png", typeof(ButtonColoring)),
                Single(
                    "inspector/buttons/inspector-button-draw-order.png",
                    typeof(PlayerController)
                ),
                Single("inspector/buttons/inspector-button-groups.png", typeof(GameManager)),
                Single(
                    "inspector/buttons/inspector-button-group-priority.png",
                    typeof(ActionPanel)
                ),
                Single(
                    "inspector/buttons/inspector-button-group-placement.png",
                    typeof(MixedPlacementExample)
                ),
                Single(
                    "inspector/buttons/inspector-button-advanced-layout.png",
                    typeof(AdvancedButtonLayout)
                ),
                Single(
                    "inspector/buttons/inspector-button-complete-example.png",
                    typeof(LevelManager)
                ),
                Single("inspector/buttons/player-debug.png", typeof(PlayerDebug)),
                Single(
                    "inspector/buttons/level-generator-with-parameters.png",
                    typeof(LevelGenerator)
                ),
                new DocumentationImage(
                    "inspector/buttons/inspector-button-display-names.png",
                    new[] { typeof(MethodNameButtons), typeof(DisplayNameButtons) },
                    ComparisonColumnWidth,
                    InspectorLabelWidth,
                    ComparisonCanvasWidth,
                    TallCanvasHeight
                ),
            };

            return images;
        }

        /// <summary>
        /// Finds the catalog entry that writes <paramref name="relativePath"/>. Data-driven tests
        /// pass the path rather than the entry, because a public test method cannot take an
        /// internal parameter type.
        /// </summary>
        internal static bool TryFindImage(string relativePath, out DocumentationImage image)
        {
            List<DocumentationImage> images = BuildImages();
            for (int index = 0; index < images.Count; index++)
            {
                if (
                    string.Equals(
                        images[index].RelativePath,
                        relativePath,
                        StringComparison.Ordinal
                    )
                )
                {
                    image = images[index];
                    return true;
                }
            }

            image = default;
            return false;
        }

        /// <summary>
        /// Absolute path the image belongs at, or an empty string when the package root cannot be
        /// located (which is what happens if the sources are read from a compiled package rather
        /// than a working tree).
        /// </summary>
        internal static string ResolveOutputPath(DocumentationImage image)
        {
            string packageRoot = ResolvePackageRoot();
            if (string.IsNullOrEmpty(packageRoot))
            {
                return string.Empty;
            }

            return Path.Combine(
                packageRoot,
                DocumentationImageFolder.Replace('/', Path.DirectorySeparatorChar),
                image.RelativePath.Replace('/', Path.DirectorySeparatorChar)
            );
        }

        internal static string ResolvePackageRoot()
        {
            return DirectoryHelper.FindPackageRootPath(DirectoryHelper.GetCallerScriptDirectory());
        }

        /// <summary>
        /// Builds the inspectors for one catalog entry, captures them, and destroys everything it
        /// created -- including on the failure path, so a refused capture cannot leave a hidden
        /// game object behind in the developer's scene.
        /// </summary>
        internal static EditorSurfaceCaptureResult Capture(
            DocumentationImage image,
            string outputPath
        )
        {
            List<Object> targets = new(image.TargetTypes.Length);
            List<Object> owned = new(image.TargetTypes.Length * 2);
            InspectorSurface surface = null;
            try
            {
                for (int index = 0; index < image.TargetTypes.Length; index++)
                {
                    targets.Add(CreateTarget(image.TargetTypes[index], owned));
                }

                surface = InspectorSurface.Create(
                    targets,
                    image.ColumnWidth,
                    image.LabelWidth,
                    drawHeader: true
                );

                return EditorSurfaceCapture.Capture(
                    surface.Root,
                    image.CanvasWidth,
                    image.CanvasHeight,
                    outputPath
                );
            }
            finally
            {
                surface?.Dispose();
                for (int index = owned.Count - 1; 0 <= index; index--)
                {
                    Object created = owned[index];
                    if (created != null)
                    {
                        Object.DestroyImmediate(created); // UNH-SUPPRESS: the catalog owns what it created
                    }
                }
            }
        }

        private static DocumentationImage Single(string relativePath, Type targetType)
        {
            return new DocumentationImage(
                relativePath,
                new[] { targetType },
                InspectorColumnWidth,
                InspectorLabelWidth,
                StandardCanvasWidth,
                TallCanvasHeight
            );
        }

        /// <summary>
        /// Throwaway, but still editable.
        /// </summary>
        /// <remarks>
        /// <c>HideFlags.HideAndDontSave</c> is 61 and includes <c>NotEditable</c> (measured), and
        /// Unity draws a <c>NotEditable</c> object's inspector greyed out. A capture host wants the
        /// hiding and the not-saving, and emphatically not the third thing: every field in every
        /// generated screenshot came out looking disabled, which is not what a reader gets.
        /// </remarks>
        private const HideFlags CaptureHostFlags =
            HideFlags.HideAndDontSave & ~HideFlags.NotEditable;

        internal static Object CreateTarget(Type targetType, List<Object> owned)
        {
            if (typeof(ScriptableObject).IsAssignableFrom(targetType))
            {
                ScriptableObject asset = ScriptableObject.CreateInstance(targetType);
                asset.name = ObjectNamesFor(targetType);
                asset.hideFlags = CaptureHostFlags;
                owned.Add(asset);
                return asset;
            }

            GameObject host = new(ObjectNamesFor(targetType)) { hideFlags = CaptureHostFlags };
            owned.Add(host);
            Component component = host.AddComponent(targetType);
            if (component == null)
            {
                throw new InvalidOperationException(
                    $"Could not add {targetType.FullName} to a capture host game object."
                );
            }

            return component;
        }

        private static string ObjectNamesFor(Type targetType)
        {
            return UnityEditor.ObjectNames.NicifyVariableName(targetType.Name);
        }
    }
#endif
}
