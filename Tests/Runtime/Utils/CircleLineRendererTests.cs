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

            // Creating the GameObject active ran OnEnable, which starts a background
            // Render() coroutine. Stop it before we set the deliberately-invalid values
            // below: otherwise the coroutine observes that transient invalid state on its
            // next tick and logs an unexpected exception -- a race that is flaky in
            // EditMode/PlayMode and previously wedged the SINGLE_THREADED PlayMode runner
            // for the full step timeout. OnValidate is delivered by SendMessage regardless
            // of enabled state, so disabling the component does not affect these asserts.
            clr.enabled = false;

            clr.numSegments = 2;
            LogAssert.Expect(
                LogType.Warning,
                new System.Text.RegularExpressions.Regex(".*Invalid number of segments.*")
            );
            clr.SendMessage("OnValidate");

            // Reset each field back to a valid value before exercising the next one, so
            // every OnValidate() call emits exactly the single warning it is asserting.
            clr.numSegments = 4;
            clr.updateRateSeconds = 0;
            LogAssert.Expect(
                LogType.Warning,
                new System.Text.RegularExpressions.Regex(".*Invalid update rate.*")
            );
            clr.SendMessage("OnValidate");

            clr.updateRateSeconds = 0.1f;
            clr.minLineWidth = 1f;
            clr.maxLineWidth = 0.5f;
            LogAssert.Expect(
                LogType.Warning,
                new System.Text.RegularExpressions.Regex(".*MaxLineWidth.*MinLineWidth.*")
            );
            clr.SendMessage("OnValidate");
        }

        // Regression guard for the SINGLE_THREADED PlayMode hang: Render() runs every tick
        // from a background coroutine, so it must never throw on a value a user can set in
        // the inspector. The dangerous configurations below: minLineWidth > maxLineWidth fed
        // PRNG.NextFloat a reversed range (the exact ArgumentException that wedged the
        // runner), and a negative or absurdly large numSegments drove an invalid
        // (negative-size or memory-exhausting) Vector3[] allocation; the remaining rows
        // (zero/equal values) are defensive. Drive Render() directly so it is deterministic.
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

            // Stop the OnEnable-started coroutine and (re)assign sibling components, then
            // drive Render() ourselves with each pathological configuration.
            clr.enabled = false;
            clr.SendMessage("Awake");

            (int segments, float min, float max)[] pathological =
            {
                (4, 1f, 0.5f), // valid segment count but min > max -> the original throw
                (2, 1f, 0.5f), // too few segments AND reversed widths
                (0, 0.005f, 0.02f), // zero segments
                (-5, 0.02f, 0.005f), // negative segments + reversed widths
                (int.MaxValue, 0.005f, 0.02f), // absurd count -> would exhaust memory without the clamp
                (8, 0.01f, 0.01f), // min == max
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

            // A thrown-and-logged coroutine exception is exactly what wedged the runner;
            // assert none of the rows produced one.
            LogAssert.NoUnexpectedReceived();
        }
    }
}
