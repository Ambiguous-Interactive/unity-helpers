// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Extensions
{
    using System;
    using System.Diagnostics.CodeAnalysis;
    using System.Text.RegularExpressions;
    using System.Threading;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Core.Helper.Logging;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    [SuppressMessage("ReSharper", "AccessToModifiedClosure")]
    public sealed class UnityLogTagFormatterEdgeTests : CommonTestBase
    {
        [TestCase(true)]
        [TestCase(false)]
        public void UnknownTagFallsBack(bool pretty)
        {
            // go.Log(...) routes through WallstopStudiosLogger, whose body is compiled out in a
            // non-development player; with no log emitted the logCount assertions below are
            // meaningless, so skip the case there.
            if (!WallstopLoggingCompiledIn)
            {
                Assert.Ignore("Package logging is compiled out in this build.");
            }

            GameObject go = Track(
                new GameObject(nameof(UnknownTagFallsBack), typeof(SpriteRenderer))
            );

            int logCount = 0;
            Exception exception = null;
            Action<string> assertion = null;
            Application.logMessageReceived += HandleMessageReceived;

            try
            {
                int expectedLogCount = 0;
                assertion = message =>
                {
                    if (pretty)
                    {
                        Assert.IsTrue(message.Contains(nameof(UnknownTagFallsBack)), message);
                        Assert.IsTrue(message.Contains(nameof(GameObject)), message);
                        Assert.IsTrue(message.Contains("Hello world"), message);
                    }
                    else
                    {
                        Assert.AreEqual("Hello world", message);
                    }
                };

                ExpectLogContaining("Hello world");
                go.Log($"Hello {"world":does_not_exist}", pretty: pretty);
                Assert.AreEqual(++expectedLogCount, logCount);
                Assert.IsTrue(exception == null, exception?.ToString());
            }
            finally
            {
                Application.logMessageReceived -= HandleMessageReceived;
            }

            return;

            void HandleMessageReceived(string message, string stackTrace, LogType type)
            {
                ++logCount;
                try
                {
                    assertion?.Invoke(message);
                }
                catch (Exception e)
                {
                    exception = e;
                    throw;
                }
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public void RepeatedSeparatorsAreIgnored(bool pretty)
        {
            if (!WallstopLoggingCompiledIn)
            {
                Assert.Ignore("Package logging is compiled out in this build.");
            }

            GameObject go = Track(
                new GameObject(nameof(RepeatedSeparatorsAreIgnored), typeof(SpriteRenderer))
            );

            int logCount = 0;
            Exception exception = null;
            Action<string> assertion = null;
            Application.logMessageReceived += HandleMessageReceived;

            try
            {
                int expectedLogCount = 0;
                assertion = message =>
                {
                    if (pretty)
                    {
                        Assert.IsTrue(
                            message.Contains(nameof(RepeatedSeparatorsAreIgnored)),
                            message
                        );
                        Assert.IsTrue(message.Contains(nameof(GameObject)), message);
                        Assert.IsTrue(message.Contains("<b>value</b>"), message);
                    }
                    else
                    {
                        Assert.AreEqual("<b>value</b>", message);
                    }
                };

                ExpectLogContaining("<b>value</b>");
                go.Log($"{"value":b,,,,,}", pretty: pretty);
                Assert.AreEqual(++expectedLogCount, logCount);
                Assert.IsTrue(exception == null, exception?.ToString());
            }
            finally
            {
                Application.logMessageReceived -= HandleMessageReceived;
            }

            return;

            void HandleMessageReceived(string message, string stackTrace, LogType type)
            {
                ++logCount;
                try
                {
                    assertion?.Invoke(message);
                }
                catch (Exception e)
                {
                    exception = e;
                    throw;
                }
            }
        }

        /// <remarks>
        /// The stack trace is the entire cost of a repeated diagnostic: Unity captures one for every
        /// log whose type is configured ScriptOnly, and that capture measured 178.4 us of a 178.4 us
        /// call on 6000.4.6f1, against 13.3 us for the same message without it (#564). What must not
        /// change is the message, so both halves are asserted: the rendered text is identical, and
        /// only the delivered stack differs.
        /// The default case is the CONTROL, and it decides whether this platform can be measured at
        /// all -- a build with stack traces switched off delivers an empty stack for BOTH cases, and
        /// asserting the subject there would be the absence of a measurement rather than a pass.
        /// </remarks>
        [Test]
        public void SuppressingTheStackTraceKeepsTheMessage()
        {
            UnityLogTagFormatter formatter = new();
            string message = nameof(SuppressingTheStackTraceKeepsTheMessage);

            string capturedStack = null;
            string capturedCondition = null;
            void Capture(string condition, string stackTrace, LogType type)
            {
                if (type == LogType.Error && condition.Contains(message))
                {
                    capturedCondition = condition;
                    capturedStack = stackTrace;
                }
            }

            Application.logMessageReceived += Capture;
            try
            {
                ExpectError(LogType.Error, new Regex($"(?s).*{message}.*"));
                string withStack = formatter.LogError(
                    $"{message}",
                    context: null,
                    e: null,
                    pretty: false
                );
                string controlStack = capturedStack;
                string controlCondition = capturedCondition;

                if (string.IsNullOrEmpty(controlStack))
                {
                    Assert.Ignore(
                        "This build delivers no stack trace for an error log, so suppressing one is not observable here."
                    );
                }

                capturedStack = null;
                capturedCondition = null;

                ExpectError(LogType.Error, new Regex($"(?s).*{message}.*"));
                string withoutStack = formatter.LogError(
                    $"{message}",
                    context: null,
                    e: null,
                    pretty: false,
                    stackTrace: false
                );

                Assert.AreEqual(withStack, withoutStack, "The rendered message must not change.");
                Assert.AreEqual(
                    controlCondition,
                    capturedCondition,
                    "The console text must not change."
                );
                Assert.IsTrue(
                    string.IsNullOrEmpty(capturedStack),
                    $"Expected no stack trace, got: {capturedStack}"
                );
            }
            finally
            {
                Application.logMessageReceived -= Capture;
            }
        }

        [Test]
        public void ExceptionLoggingFormatsOutput()
        {
            UnityLogTagFormatter formatter = new();
            string message = nameof(ExceptionLoggingFormatsOutput);
            Exception testException = new($"{nameof(ExceptionLoggingFormatsOutput)}Boom");

            LogAssert.Expect(LogType.Log, new Regex($"(?s).*{message}.*{testException.Message}.*"));
            string logged = formatter.Log(
                $"{message}Log",
                context: null,
                e: testException,
                pretty: true
            );
            Assert.IsTrue(logged.Contains("NO_NAME[NO_TYPE]"), logged);
            Assert.IsTrue(logged.Contains($"{message}Log"), logged);
            Assert.IsTrue(logged.Contains(testException.Message), logged);

            ExpectError(LogType.Warning, new Regex($"(?s).*{message}.*{testException.Message}.*"));
            string warned = formatter.LogWarn(
                $"{message}Warning",
                context: null,
                e: testException,
                pretty: false
            );
            Assert.IsTrue(warned.Contains($"{message}Warning"), warned);
            Assert.IsTrue(warned.Contains(testException.Message), warned);

            ExpectError(LogType.Error, new Regex($"(?s).*{message}.*{testException.Message}.*"));
            string errored = formatter.LogError(
                $"{message}Error",
                context: null,
                e: testException,
                pretty: false
            );
            Assert.IsTrue(errored.Contains($"{message}Error"), errored);
            Assert.IsTrue(errored.Contains(testException.Message), errored);

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void CustomDecorationPriorityControlsOrder()
        {
            UnityLogTagFormatter formatter = new(createDefaultDecorators: false);

            formatter.AddDecoration(
                predicate: x => string.Equals(x, "x", StringComparison.OrdinalIgnoreCase),
                format: (_, v) => $"<A>{v}</A>",
                tag: "A",
                priority: 10,
                editorOnly: false,
                force: true
            );

            formatter.AddDecoration(
                predicate: x => string.Equals(x, "x", StringComparison.OrdinalIgnoreCase),
                format: (_, v) => $"<B>{v}</B>",
                tag: "B",
                priority: -10,
                editorOnly: false,
                force: true
            );

            ExpectLogContaining("<A><B>value</B></A>");
            string formatted = formatter.Log($"{"value":x}", pretty: false);
            Assert.AreEqual("<A><B>value</B></A>", formatted);
        }

        [Test]
        public void ForceOverrideMovesPriority()
        {
            UnityLogTagFormatter formatter = new(createDefaultDecorators: false);

            formatter.AddDecoration(
                match: "demo",
                format: v => $"<P5>{v}</P5>",
                tag: "Demo",
                priority: 5,
                editorOnly: false,
                force: true
            );
            ExpectLogContaining("<P5>value</P5>");
            string formatted = formatter.Log($"{"value":demo}", pretty: false);
            Assert.AreEqual("<P5>value</P5>", formatted);

            formatter.AddDecoration(
                match: "demo",
                format: v => $"<P1>{v}</P1>",
                tag: "Demo",
                priority: 1,
                editorOnly: false,
                force: true
            );
            ExpectLogContaining("<P1>value</P1>");
            formatted = formatter.Log($"{"value":demo}", pretty: false);
            Assert.AreEqual("<P1>value</P1>", formatted);
        }

        [Test]
        public void ForceOverrideAtSamePriorityReplacesFormatter()
        {
            UnityLogTagFormatter formatter = new(createDefaultDecorators: false);

            formatter.AddDecoration(
                match: "demo",
                format: value => $"<Initial>{value}</Initial>",
                tag: "Demo",
                priority: 3,
                editorOnly: false,
                force: true
            );
            formatter.AddDecoration(
                match: "demo",
                format: value => $"<Updated>{value}</Updated>",
                tag: "Demo",
                priority: 3,
                editorOnly: false,
                force: true
            );

            ExpectLogContaining("<Updated>value</Updated>");
            string formatted = formatter.Log($"{"value":demo}", pretty: false);
            Assert.AreEqual("<Updated>value</Updated>", formatted);
        }

        [Test]
        public void DuplicateTagWithoutForceReturnsFalse()
        {
            UnityLogTagFormatter formatter = new(createDefaultDecorators: false);

            bool firstResult = formatter.AddDecoration(
                match: "demo",
                format: value => $"<Initial>{value}</Initial>",
                tag: "Demo",
                priority: 0,
                editorOnly: false,
                force: false
            );
            Assert.IsTrue(firstResult);

            bool secondResult = formatter.AddDecoration(
                match: "demo",
                format: value => $"<Ignored>{value}</Ignored>",
                tag: "Demo",
                priority: 10,
                editorOnly: false,
                force: false
            );
            Assert.IsFalse(secondResult);

            ExpectLogContaining("<Initial>value</Initial>");
            string formatted = formatter.Log($"{"value":demo}", pretty: false);
            Assert.AreEqual("<Initial>value</Initial>", formatted);
        }

        [Test]
        public void RemoveDecorationRemovesFormatter()
        {
            UnityLogTagFormatter formatter = new(createDefaultDecorators: false);

            formatter.AddDecoration(
                match: "demo",
                format: value => $"<Removed>{value}</Removed>",
                tag: "Demo",
                priority: 0,
                editorOnly: false,
                force: true
            );

            bool removed = formatter.RemoveDecoration(
                "Demo",
                out DecorationEntry removedDecoration
            );

            Assert.IsTrue(removed);
            Assert.AreEqual("Demo", removedDecoration.Tag);

            ExpectLogContaining("value");
            string formatted = formatter.Log($"{"value":demo}", pretty: false);
            Assert.AreEqual("value", formatted);
        }

        [Test]
        public void PrettyLogOmitsMainThreadMetadata()
        {
            UnityLogTagFormatter formatter = new();

            ExpectLogContaining("Hello");
            string logged = formatter.Log($"Hello", pretty: true);

            StringAssert.DoesNotMatch(@"\|(unity|editor)-main#\d+\|", logged);
            StringAssert.IsMatch(@"^\d+(\.\d+)?\|NO_NAME\[NO_TYPE\]\|Hello$", logged);
        }

        [Test]
        public void PrettyLogIncludesWorkerThreadMetadata()
        {
            using ManualResetEventSlim completed = new(false);

            string loggedMessage = null;
            int workerThreadId = -1;
            ExpectLogContaining("Worker");
            Thread worker = new(() =>
            {
                UnityLogTagFormatter workerFormatter = new();
                workerThreadId = Thread.CurrentThread.ManagedThreadId;
                loggedMessage = workerFormatter.Log($"Worker", pretty: true);
                completed.Set();
            })
            {
                IsBackground = true,
            };

            try
            {
                worker.Start();
                Assert.IsTrue(
                    completed.Wait(TimeSpan.FromSeconds(5)),
                    "Timed out waiting for worker thread log."
                );
                worker.Join();

                Assert.IsTrue(loggedMessage != null, "Worker log was not captured.");
                StringAssert.Contains($"worker#{workerThreadId}", loggedMessage);
            }
            finally
            {
                if (worker.IsAlive)
                {
                    worker.Join();
                }
            }
        }

        private static void ExpectLogContaining(string value)
        {
            LogAssert.Expect(LogType.Log, new Regex(Regex.Escape(value)));
        }
    }
}
