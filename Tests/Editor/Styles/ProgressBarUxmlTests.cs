// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Styles
{
    using System.IO;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.UIElements;
    using WallstopStudios.UnityHelpers.Editor.Utils;
    using WallstopStudios.UnityHelpers.Styles.Elements.Progress;

    [TestFixture]
    [Category("Integration")]
    public sealed class ProgressBarUxmlTests
    {
        private const string AssetFolder = "Assets/TempProgressBarUxmlTests";
        private const string AssetPath = AssetFolder + "/ProgressBars.uxml";

        [SetUp]
        public void SetUp()
        {
            AssetDatabase.DeleteAsset(AssetFolder);
            Assert.That(AssetDatabaseBatchHelper.EnsureAssetFolder(AssetFolder), Is.True);
        }

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(AssetFolder);
        }

        [Test]
        public void EveryProgressBarLoadsItsAttributesFromUxml()
        {
            string absolutePath = Path.Combine(
                Application.dataPath,
                "TempProgressBarUxmlTests/ProgressBars.uxml"
            );
            File.WriteAllText(
                absolutePath,
                "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\" "
                    + "xmlns:progress=\"WallstopStudios.UnityHelpers.Styles.Elements.Progress\">"
                    + "<progress:RegularProgressBar name=\"regular\" progress=\"0.11\" />"
                    + "<progress:CircularProgressBar name=\"circular\" radius=\"31\" />"
                    + "<progress:ArcedProgressBar name=\"arced\" rounded-caps=\"false\" />"
                    + "<progress:LiquidProgressBar name=\"liquid\" animation-speed=\"3.5\" />"
                    + "<progress:MarchingAntsProgressBar name=\"marching\" dash-on=\"7\" />"
                    + "<progress:WigglyProgressBar name=\"wiggly\" segments-per-wavelength=\"9\" />"
                    + "<progress:GlitchProgressBar name=\"glitch\" progress=\"0.77\" "
                    + "glitch-duration-frames=\"6\" />"
                    + "</ui:UXML>"
            );
            AssetDatabase.ImportAsset(AssetPath, ImportAssetOptions.ForceSynchronousImport);
            VisualTreeAsset asset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(AssetPath);

            Assert.That(asset, Is.Not.Null);
            TemplateContainer root = asset.Instantiate();
            RegularProgressBar regular = root.Q<RegularProgressBar>("regular");
            CircularProgressBar circular = root.Q<CircularProgressBar>("circular");
            ArcedProgressBar arced = root.Q<ArcedProgressBar>("arced");
            LiquidProgressBar liquid = root.Q<LiquidProgressBar>("liquid");
            MarchingAntsProgressBar marching = root.Q<MarchingAntsProgressBar>("marching");
            WigglyProgressBar wiggly = root.Q<WigglyProgressBar>("wiggly");
            GlitchProgressBar glitch = root.Q<GlitchProgressBar>("glitch");

            Assert.That(regular, Is.Not.Null);
            Assert.That(circular, Is.Not.Null);
            Assert.That(arced, Is.Not.Null);
            Assert.That(liquid, Is.Not.Null);
            Assert.That(marching, Is.Not.Null);
            Assert.That(wiggly, Is.Not.Null);
            Assert.That(glitch, Is.Not.Null);
            Assert.That(regular.Progress, Is.EqualTo(0.11f));
            Assert.That(circular.Radius, Is.EqualTo(31f));
            Assert.That(arced.RoundedCaps, Is.False);
            Assert.That(liquid.AnimationSpeed, Is.EqualTo(3.5f));
            Assert.That(marching.DashOnLength, Is.EqualTo(7f));
            Assert.That(wiggly.SegmentsPerWavelength, Is.EqualTo(9));
            Assert.That(glitch.Progress, Is.EqualTo(0.77f));
            Assert.That(glitch.glitchDurationFrames, Is.EqualTo(6));
            Assert.That(liquid.style.width.value.value, Is.EqualTo(200f));
            Assert.That(liquid.style.height.value.value, Is.EqualTo(22f));
            Assert.That(marching.style.width.value.value, Is.EqualTo(200f));
            Assert.That(marching.style.height.value.value, Is.EqualTo(20f));
            Assert.That(glitch.style.width.value.value, Is.EqualTo(200f));
            Assert.That(glitch.style.height.value.value, Is.EqualTo(20f));
        }
    }
}
