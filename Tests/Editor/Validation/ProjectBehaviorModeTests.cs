// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Validation
{
    using NUnit.Framework;
    using UnityEditor;

    /// <summary>
    /// Tripwire guarding the project's 2D Default Behavior Mode. unity-helpers is a 2D
    /// sprite-tooling package whose entire texture/sprite test suite is authored and validated
    /// in 2D mode. A 3D-mode project imports fresh PNGs as
    /// <see cref="TextureImporterType.Default"/> with
    /// <see cref="TextureImporterNPOTScale.ToNearest"/> -- rounding non-power-of-two dimensions
    /// (e.g. 10x6 to 8x8) and omitting the Sprite sub-asset -- which silently breaks fixtures
    /// such as <c>TextureResizerWizardTests</c>, <c>FitTextureSizeWindowTests</c>, and
    /// <c>SpriteSettingsApplierWindowTests</c> with cryptic dimension/null-sprite mismatches.
    /// The CI and devcontainer ephemeral projects seed
    /// <see cref="EditorSettings.defaultBehaviorMode"/> = <see cref="EditorBehaviorMode.Mode2D"/>;
    /// this fails LOUDLY with a precise pointer if that seed ever regresses, instead of letting
    /// unrelated texture tests absorb the blame.
    /// </summary>
    [TestFixture]
    [Category("Fast")]
    [Category("Validation")]
    public sealed class ProjectBehaviorModeTests
    {
        [Test]
        public void ProjectUsesTwoDimensionalDefaultBehaviorModeForDeterministicTextureImport()
        {
            Assert.AreEqual(
                EditorBehaviorMode.Mode2D,
                EditorSettings.defaultBehaviorMode,
                "The test project must run in 2D Default Behavior Mode so fresh PNGs import as "
                    + "sprites with npotScale=None (preserving exact non-power-of-two dimensions). "
                    + "In 3D mode they import as TextureImporterType.Default with "
                    + "npotScale=ToNearest, silently breaking TextureResizerWizardTests, "
                    + "FitTextureSizeWindowTests, and SpriteSettingsApplierWindowTests. Re-seed "
                    + "EditorSettings.defaultBehaviorMode = Mode2D in "
                    + "scripts/unity/run-ci-tests.ps1 (Initialize-EphemeralProject) and "
                    + "scripts/unity/create-test-project.sh (Step 3b)."
            );
        }
    }
}
