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

        [Test]
        public void ExceptionLoggingFormatsOutput()
        {
            UnityLogTagFormatter formatter = new();
            Exception testException = new("Boom");

            LogAssert.Expect(LogType.Log, new Regex("(?s).*Hello.*Boom.*"));
            string logged = formatter.Log($"Hello", context: null, e: testException, pretty: true);
            Assert.IsTrue(logged.Contains("NO_NAME[NO_TYPE]"), logged);
            Assert.IsTrue(logged.Contains("Hello"), logged);
            Assert.IsTrue(logged.Contains("Boom"), logged);

            LogAssert.Expect(LogType.Warning, new Regex("(?s).*Hello.*Boom.*"));
            string warned = formatter.LogWarn(
                $"Hello",
                context: null,
                e: testException,
                pretty: false
            );
            Assert.IsTrue(warned.Contains("Hello"), warned);
            Assert.IsTrue(warned.Contains("Boom"), warned);

            LogAssert.Expect(LogType.Error, new Regex("(?s).*Hello.*Boom.*"));
            string errored = formatter.LogError(
                $"Hello",
                context: null,
                e: testException,
                pretty: false
            );
            Assert.IsTrue(errored.Contains("Hello"), errored);
            Assert.IsTrue(errored.Contains("Boom"), errored);
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
