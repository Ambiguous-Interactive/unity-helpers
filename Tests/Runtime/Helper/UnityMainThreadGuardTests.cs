// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Helper
{
    using System;
    using System.Collections;
    using System.Threading;
    using NUnit.Framework;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class UnityMainThreadGuardTests : CommonTestBase
    {
        private static InvalidOperationException CaptureFailureOnWorker(
            string context,
            string memberName
        )
        {
            UnityMainThreadGuard.Capture(Thread.CurrentThread);
            InvalidOperationException captured = null;
            Thread thread = new(() =>
            {
                try
                {
                    UnityMainThreadGuard.EnsureMainThread(
                        context,
                        memberName,
                        "GuardSource.cs",
                        42
                    );
                }
                catch (InvalidOperationException exception)
                {
                    captured = exception;
                }
            })
            {
                IsBackground = true,
            };
            thread.Start();
            Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(5)), "Worker guard did not complete.");
            Assert.IsTrue(captured != null, "Worker guard must reject access off the main thread.");
            return captured;
        }

        [TestCase(null, TestName = "BlankMember.Null.UsesFileLabel")]
        [TestCase("", TestName = "BlankMember.Empty.UsesFileLabel")]
        [TestCase(" ", TestName = "BlankMember.Space.UsesFileLabel")]
        [TestCase("\t\r\n", TestName = "BlankMember.ControlWhitespace.UsesFileLabel")]
        [TestCase("\u2003\u00a0", TestName = "BlankMember.UnicodeWhitespace.UsesFileLabel")]
        public void BlankMemberUsesFileLabel(string memberName)
        {
            InvalidOperationException captured = CaptureFailureOnWorker(null, memberName);
            StringAssert.StartsWith("GuardSource must be accessed", captured.Message);
            StringAssert.Contains("GuardSource.cs:42", captured.Message);
        }

        [TestCase(null, TestName = "BlankContext.Null.Omitted")]
        [TestCase("", TestName = "BlankContext.Empty.Omitted")]
        [TestCase(" ", TestName = "BlankContext.Space.Omitted")]
        [TestCase("\t\r\n", TestName = "BlankContext.ControlWhitespace.Omitted")]
        [TestCase("\u2003\u00a0", TestName = "BlankContext.UnicodeWhitespace.Omitted")]
        public void BlankContextIsOmitted(string context)
        {
            InvalidOperationException captured = CaptureFailureOnWorker(
                context,
                nameof(BlankContextIsOmitted)
            );
            StringAssert.StartsWith(
                $"GuardSource.{nameof(BlankContextIsOmitted)} must be accessed",
                captured.Message
            );
        }

        [TestCase(" member ", " context ", TestName = "Diagnostic.PaddedLabels.Preserved")]
        [TestCase("member", "before\tafter", TestName = "Diagnostic.InteriorWhitespace.Preserved")]
        public void NonblankDiagnosticLabelsPreserveLiteralText(string memberName, string context)
        {
            InvalidOperationException captured = CaptureFailureOnWorker(context, memberName);
            StringAssert.StartsWith(
                $"GuardSource.{memberName} ({context}) must be accessed",
                captured.Message
            );
        }

        [UnityTest]
        public IEnumerator EnsureMainThreadThrowsWhenOffThread()
        {
            InvalidOperationException captured = null;
            using (ManualResetEventSlim done = new(false))
            {
                Thread thread = new(() =>
                {
                    try
                    {
                        UnityMainThreadGuard.EnsureMainThread();
                    }
                    catch (InvalidOperationException ex)
                    {
                        captured = ex;
                    }
                    finally
                    {
                        done.Set();
                    }
                })
                {
                    IsBackground = true,
                };

                thread.Start();

                while (!done.IsSet)
                {
                    yield return null;
                }
            }

            Assert.IsTrue(
                captured != null,
                "Exception should have been captured from background thread"
            );
            StringAssert.Contains(nameof(EnsureMainThreadThrowsWhenOffThread), captured.Message);
        }

        [Test]
        public void EnsureMainThreadNoOpWhenOnMainThread()
        {
            Assert.DoesNotThrow(() => UnityMainThreadGuard.EnsureMainThread());
        }

#if UNITY_EDITOR
        [Test]
        public void EditorInitializeDoesNotThrow()
        {
            Assert.DoesNotThrow(() => UnityMainThreadGuard.CaptureEditorThread());
        }
#endif
    }
}
