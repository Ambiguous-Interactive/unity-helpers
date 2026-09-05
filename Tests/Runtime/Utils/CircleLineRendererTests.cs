// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Utils
{
    using System.Collections;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Utils;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class CircleLineRendererTests : CommonTestBase
    {
        [UnityTest]
        public IEnumerator UpdateSyncsEnabledWithCollider()
        {
            GameObject go = Track(
                new GameObject(
                    "Circle",
                    typeof(LineRenderer),
                    typeof(CircleCollider2D),
                    typeof(CircleLineRenderer)
                )
            );
            LineRenderer lr = go.GetComponent<LineRenderer>();
            CircleCollider2D col = go.GetComponent<CircleCollider2D>();
            CircleLineRenderer clr = go.GetComponent<CircleLineRenderer>();

            clr.SendMessage("Awake");

            col.enabled = true;
            clr.SendMessage("Update");
            yield return null;
            Assert.IsTrue(lr.enabled);

            col.enabled = false;
            clr.SendMessage("Update");
            yield return null;
            Assert.IsFalse(lr.enabled);
        }

        [Test]
        public void OnValidateWarnsOnInvalidValues()
        {
            GameObject go = Track(
                new GameObject(
                    "Circle",
                    typeof(LineRenderer),
                    typeof(CircleCollider2D),
                    typeof(CircleLineRenderer)
                )
            );
            CircleLineRenderer clr = go.GetComponent<CircleLineRenderer>();

            /*
                Stop the OnEnable coroutine before injecting invalid values so it cannot race validation;
                OnValidate remains callable while disabled.
            */
            clr.enabled = false;

            clr.numSegments = 2;
            /*
                The package logger is compiled out in non-development players; ExpectWallstopLog skips those
                expectations.
            */
            ExpectWallstopLog(
                LogType.Warning,
                new System.Text.RegularExpressions.Regex(".*Invalid number of segments.*")
            );
            clr.SendMessage("OnValidate");

            // Restore each field before the next check so validation emits only the warning under test.
            clr.numSegments = 4;
            clr.updateRateSeconds = 0;
            ExpectWallstopLog(
                LogType.Warning,
                new System.Text.RegularExpressions.Regex(".*Invalid update rate.*")
            );
            clr.SendMessage("OnValidate");

            clr.updateRateSeconds = 0.1f;
            clr.minLineWidth = 1f;
            clr.maxLineWidth = 0.5f;
            ExpectWallstopLog(
                LogType.Warning,
                new System.Text.RegularExpressions.Regex(".*MaxLineWidth.*MinLineWidth.*")
            );
            clr.SendMessage("OnValidate");
        }

        /*
            Drive Render directly to cover reversed widths and invalid segment counts without a background-
            coroutine race.
        */
        [Test]
        public void RenderNeverThrowsOnInvalidInspectorValues()
        {
            GameObject go = Track(
                new GameObject(
                    "Circle",
                    typeof(LineRenderer),
                    typeof(CircleCollider2D),
                    typeof(CircleLineRenderer)
                )
            );
            CircleLineRenderer clr = go.GetComponent<CircleLineRenderer>();

            // Stop the background coroutine before driving pathological configurations directly.
            clr.enabled = false;
            clr.SendMessage("Awake");

            (int segments, float min, float max)[] pathological =
            {
                (4, 1f, 0.5f),
                (2, 1f, 0.5f),
                (0, 0.005f, 0.02f),
                (-5, 0.02f, 0.005f),
                (int.MaxValue, 0.005f, 0.02f),
                (8, 0.01f, 0.01f),
            };

            foreach ((int segments, float min, float max) in pathological)
            {
                clr.numSegments = segments;
                clr.minLineWidth = min;
                clr.maxLineWidth = max;
                Assert.DoesNotThrow(
                    () => clr.SendMessage("Render"),
                    $"Render() threw for numSegments={segments}, min={min}, max={max}"
                );
            }

            /*
                An uncaught coroutine exception previously wedged the runner; check every configuration for
                unexpected logs.
            */
            LogAssert.NoUnexpectedReceived();
        }
    }
}
