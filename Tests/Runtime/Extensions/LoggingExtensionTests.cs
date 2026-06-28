// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Extensions
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.RegularExpressions;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Core.Helper.Logging;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class LoggingExtensionTests : CommonTestBase
    {
        [Test]
        public void Registration()
        {
            UnityLogTagFormatter formatter = new(createDefaultDecorators: false);
            Assert.AreEqual(
                0,
                formatter.Decorations.Count(),
                $"Found an unexpected number of registered decorations {formatter.Decorations.ToJson()}"
            );

            bool added = formatter.AddDecoration("b", value => $"<b>{value}</b>", "Bold");
            Assert.IsTrue(added);
            ExpectLogContaining("<b>Hello</b>");
            string formatted = formatter.Log($"{"Hello":b}", pretty: false);
            Assert.AreEqual("<b>Hello</b>", formatted);
            Assert.That(Enumerables.Of("Bold"), Is.EqualTo(formatter.Decorations));

            added = formatter.AddDecoration("b", value => $"<c>{value}</c>", "Bold");
            Assert.IsFalse(added);
            ExpectLogContaining("<b>Hello</b>");
            formatted = formatter.Log($"{"Hello":b}", pretty: false);
            Assert.AreEqual("<b>Hello</b>", formatted);
            Assert.That(Enumerables.Of("Bold"), Is.EqualTo(formatter.Decorations));

            added = formatter.AddDecoration("c", value => $"<c>{value}</c>", "Bold");
            Assert.IsFalse(added);
            ExpectLogContaining("<b>Hello</b>");
            formatted = formatter.Log($"{"Hello":b}", pretty: false);
            Assert.AreEqual("<b>Hello</b>", formatted);
            Assert.That(Enumerables.Of("Bold"), Is.EqualTo(formatter.Decorations));

            added = formatter.AddDecoration("c", value => $"<c>{value}</c>", "Bold1");
            Assert.IsTrue(added);
            ExpectLogContaining("<b>Hello</b>");
            formatted = formatter.Log($"{"Hello":b}", pretty: false);
            Assert.AreEqual("<b>Hello</b>", formatted);
            Assert.That(Enumerables.Of("Bold", "Bold1"), Is.EqualTo(formatter.Decorations));
            ExpectLogContaining("<c>Hello</c>");
            formatted = formatter.Log($"{"Hello":c}", pretty: false);
            Assert.AreEqual("<c>Hello</c>", formatted);
            Assert.That(Enumerables.Of("Bold", "Bold1"), Is.EqualTo(formatter.Decorations));

            added = formatter.AddDecoration("b", value => $"<c>{value}</c>", "Bold", force: true);
            Assert.IsTrue(added);
            Assert.That(Enumerables.Of("Bold", "Bold1"), Is.EqualTo(formatter.Decorations));
            ExpectLogContaining("<c>Hello</c>");
            formatted = formatter.Log($"{"Hello":b}", pretty: false);
            Assert.AreEqual("<c>Hello</c>", formatted);

            bool removed = formatter.RemoveDecoration("Bold", out _);
            Assert.IsTrue(removed);
            Assert.That(Enumerables.Of("Bold1"), Is.EqualTo(formatter.Decorations));
            ExpectLogContaining("Hello");
            formatted = formatter.Log($"{"Hello":b}", pretty: false);
            Assert.AreEqual("Hello", formatted);
            ExpectLogContaining("<c>Hello</c>");
            formatted = formatter.Log($"{"Hello":c}", pretty: false);
            Assert.AreEqual("<c>Hello</c>", formatted);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void SimpleLogging(bool pretty)
        {
            // go.Log(...) routes through WallstopStudiosLogger, whose body is compiled out in a
            // non-development player; with no log emitted the logCount assertions below are
            // meaningless, so skip the case there.
            if (!WallstopLoggingCompiledIn)
            {
                Assert.Ignore("Package logging is compiled out in this build.");
            }

            GameObject go = Track(new GameObject(nameof(SimpleLogging), typeof(SpriteRenderer)));

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
                        Assert.IsTrue(message.Contains(nameof(SimpleLogging)), message);
                        Assert.IsTrue(message.Contains(nameof(GameObject)), message);
                        Assert.IsTrue(message.Contains("Hello, world!"), message);
                    }
                    else
                    {
                        Assert.AreEqual("Hello, world!", message);
                    }
                };

                ExpectLogContaining("Hello, world!");
                go.Log($"Hello, world!", pretty: pretty);
                Assert.AreEqual(++expectedLogCount, logCount);
                Assert.IsTrue(exception == null, exception?.ToString());

                SpriteRenderer sr = go.GetComponent<SpriteRenderer>();

                assertion = message =>
                {
                    if (pretty)
                    {
                        Assert.IsTrue(message.Contains(nameof(SimpleLogging)), message);
                        Assert.IsTrue(message.Contains(nameof(SpriteRenderer)), message);
                        Assert.IsTrue(message.Contains("Hello, world!"), message);
                    }
                    else
                    {
                        Assert.AreEqual("Hello, world!", message);
                    }
                };

                ExpectLogContaining("Hello, world!");
                sr.Log($"Hello, world!", pretty: pretty);

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
        public void ColorLogging(bool pretty)
        {
            if (!WallstopLoggingCompiledIn)
            {
                Assert.Ignore("Package logging is compiled out in this build.");
            }

            GameObject go = Track(new GameObject(nameof(ColorLogging), typeof(SpriteRenderer)));

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
                        Assert.IsTrue(message.Contains(nameof(ColorLogging)), message);
                        Assert.IsTrue(message.Contains(nameof(GameObject)), message);
                        Assert.IsTrue(
                            message.Contains("Hello <color=#FF0000FF>world</color>"),
                            message
                        );
                    }
                    else
                    {
                        Assert.AreEqual("Hello <color=#FF0000FF>world</color>", message);
                    }
                };
                ExpectLogContaining("Hello <color=#FF0000FF>world</color>");
                go.Log($"Hello {"world":#red}", pretty: pretty);
                Assert.AreEqual(++expectedLogCount, logCount);
                Assert.IsTrue(exception == null, exception?.ToString());

                assertion = message =>
                {
                    if (pretty)
                    {
                        Assert.IsTrue(message.Contains(nameof(ColorLogging)), message);
                        Assert.IsTrue(message.Contains(nameof(GameObject)), message);
                        Assert.IsTrue(
                            message.Contains("Hello <color=#00FF00FF>world</color>"),
                            message
                        );
                    }
                    else
                    {
                        Assert.AreEqual("Hello <color=#00FF00FF>world</color>", message);
                    }
                };
                ExpectLogContaining("Hello <color=#00FF00FF>world</color>");
                go.Log($"Hello {"world":#green}", pretty: pretty);
                Assert.AreEqual(++expectedLogCount, logCount);
                Assert.IsTrue(exception == null, exception?.ToString());

                assertion = message =>
                {
                    if (pretty)
                    {
                        Assert.IsTrue(message.Contains(nameof(ColorLogging)), message);
                        Assert.IsTrue(message.Contains(nameof(GameObject)), message);
                        Assert.IsTrue(
                            message.Contains("Hello <color=#FFAABB>world</color>"),
                            message
                        );
                    }
                    else
                    {
                        Assert.AreEqual("Hello <color=#FFAABB>world</color>", message);
                    }
                };
                ExpectLogContaining("Hello <color=#FFAABB>world</color>");
                go.Log($"Hello {"world":#FFAABB}", pretty: pretty);
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
        public void BoldLogging(bool pretty)
        {
            if (!WallstopLoggingCompiledIn)
            {
                Assert.Ignore("Package logging is compiled out in this build.");
            }

            GameObject go = Track(new GameObject(nameof(BoldLogging), typeof(SpriteRenderer)));

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
                        Assert.IsTrue(message.Contains(nameof(BoldLogging)), message);
                        Assert.IsTrue(message.Contains(nameof(GameObject)), message);
                        Assert.IsTrue(message.Contains("Hello <b>world</b>"), message);
                    }
                    else
                    {
                        Assert.AreEqual("Hello <b>world</b>", message);
                    }
                };
                ExpectLogContaining("Hello <b>world</b>");
                go.Log($"Hello {"world":b}", pretty: pretty);
                Assert.AreEqual(++expectedLogCount, logCount);
                Assert.IsTrue(exception == null, exception?.ToString());

                ExpectLogContaining("Hello <b>world</b>");
                go.Log($"Hello {"world":bold}", pretty: pretty);
                Assert.AreEqual(++expectedLogCount, logCount);
                Assert.IsTrue(exception == null, exception?.ToString());

                ExpectLogContaining("Hello <b>world</b>");
                go.Log($"Hello {"world":!}", pretty: pretty);
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
        public void JsonLogging(bool pretty)
        {
            if (!WallstopLoggingCompiledIn)
            {
                Assert.Ignore("Package logging is compiled out in this build.");
            }

            GameObject go = Track(new GameObject(nameof(JsonLogging), typeof(SpriteRenderer)));

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
                        Assert.IsTrue(message.Contains(nameof(JsonLogging)), message);
                        Assert.IsTrue(message.Contains(nameof(GameObject)), message);
                        Assert.IsTrue(message.Contains("Hello [\"a\",\"b\",\"c\"]"), message);
                    }
                    else
                    {
                        Assert.AreEqual("Hello [\"a\",\"b\",\"c\"]", message);
                    }
                };

                ExpectLogContaining("Hello [\"a\",\"b\",\"c\"]");
                go.Log($"Hello {new List<string> { "a", "b", "c" }:json}", pretty: pretty);
                Assert.AreEqual(++expectedLogCount, logCount);
                Assert.IsTrue(exception == null, exception?.ToString());

                assertion = message =>
                {
                    if (pretty)
                    {
                        Assert.IsTrue(message.Contains(nameof(JsonLogging)), message);
                        Assert.IsTrue(message.Contains(nameof(GameObject)), message);
                        Assert.IsTrue(message.Contains("Hello {}"), message);
                    }
                    else
                    {
                        Assert.AreEqual("Hello {}", message);
                    }
                };
                ExpectLogContaining("Hello {}");
                go.Log($"Hello {null:json}", pretty: pretty);
                Assert.AreEqual(++expectedLogCount, logCount);
                Assert.IsTrue(exception == null, exception?.ToString());

                assertion = message =>
                {
                    if (pretty)
                    {
                        Assert.IsTrue(message.Contains(nameof(JsonLogging)), message);
                        Assert.IsTrue(message.Contains(nameof(GameObject)), message);
                        Assert.IsTrue(message.Contains("Hello [1,2,3,4]"), message);
                    }
                    else
                    {
                        Assert.AreEqual("Hello [1,2,3,4]", message);
                    }
                };
                ExpectLogContaining("Hello [1,2,3,4]");
                go.Log($"Hello {new[] { 1, 2, 3, 4 }:json}", pretty: pretty);
                Assert.AreEqual(++expectedLogCount, logCount);
                Assert.IsTrue(exception == null, exception?.ToString());

                assertion = message =>
                {
                    if (pretty)
                    {
                        Assert.IsTrue(message.Contains(nameof(JsonLogging)), message);
                        Assert.IsTrue(message.Contains(nameof(GameObject)), message);
                        Assert.IsTrue(message.Contains("Hello {\"key\":\"value\"}"), message);
                    }
                    else
                    {
                        Assert.AreEqual("Hello {\"key\":\"value\"}", message);
                    }
                };
                ExpectLogContaining("Hello {\"key\":\"value\"}");
                go.Log(
                    $"Hello {new Dictionary<string, string> { ["key"] = "value" }:json}",
                    pretty: pretty
                );
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
        public void SizeLogging(bool pretty)
        {
            if (!WallstopLoggingCompiledIn)
            {
                Assert.Ignore("Package logging is compiled out in this build.");
            }

            GameObject go = Track(new GameObject(nameof(SizeLogging), typeof(SpriteRenderer)));

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
                        Assert.IsTrue(message.Contains(nameof(SizeLogging)), message);
                        Assert.IsTrue(message.Contains(nameof(GameObject)), message);
                        Assert.IsTrue(message.Contains("Hello <size=40>world</size>"), message);
                    }
                    else
                    {
                        Assert.AreEqual("Hello <size=40>world</size>", message);
                    }
                };
                ExpectLogContaining("Hello <size=40>world</size>");
                go.Log($"Hello {"world":40}", pretty: pretty);
                Assert.AreEqual(++expectedLogCount, logCount);
                Assert.IsTrue(exception == null, exception?.ToString());

                ExpectLogContaining("Hello <size=40>world</size>");
                go.Log($"Hello {"world":size=40}", pretty: pretty);
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
        public void DateTimeNormalFormatTests(bool pretty)
        {
            if (!WallstopLoggingCompiledIn)
            {
                Assert.Ignore("Package logging is compiled out in this build.");
            }

            GameObject go = Track(
                new GameObject(nameof(DateTimeNormalFormatTests), typeof(SpriteRenderer))
            );
            int logCount = 0;
            Exception exception = null;
            Action<string> assertion = null;
            Application.logMessageReceived += HandleMessageReceived;
            try
            {
                int expectedLogCount = 0;
                DateTime now = DateTime.UtcNow;
                assertion = message =>
                {
                    if (pretty)
                    {
                        Assert.IsTrue(message.Contains(nameof(DateTimeNormalFormatTests)), message);
                        Assert.IsTrue(message.Contains(nameof(GameObject)), message);
                        Assert.IsTrue(message.Contains($"Hello {now:O}"), message);
                    }
                    else
                    {
                        Assert.AreEqual($"Hello {now:O}", message);
                    }
                };

                ExpectLogContaining($"Hello {now:O}");
                go.Log($"Hello {now:O}", pretty: pretty);
                Assert.AreEqual(++expectedLogCount, logCount);
                Assert.IsTrue(exception == null, exception?.ToString());

                assertion = message =>
                {
                    if (pretty)
                    {
                        Assert.IsTrue(message.Contains(nameof(DateTimeNormalFormatTests)), message);
                        Assert.IsTrue(message.Contains(nameof(GameObject)), message);
                        Assert.IsTrue(message.Contains($"Hello <size=40>{now}</size>"), message);
                    }
                    else
                    {
                        Assert.AreEqual($"Hello <size=40>{now}</size>", message);
                    }
                };

                ExpectLogContaining($"Hello <size=40>{now}</size>");
                go.Log($"Hello {now:40}", pretty: pretty);
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
        public void StackedTags(bool pretty)
        {
            if (!WallstopLoggingCompiledIn)
            {
                Assert.Ignore("Package logging is compiled out in this build.");
            }

            GameObject go = Track(new GameObject(nameof(StackedTags), typeof(SpriteRenderer)));
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
                        Assert.IsTrue(message.Contains(nameof(StackedTags)), message);
                        Assert.IsTrue(message.Contains(nameof(GameObject)), message);
                        Assert.IsTrue(message.Contains("Hello <b>[1,2,3]</b>"), message);
                    }
                    else
                    {
                        Assert.AreEqual("Hello <b>[1,2,3]</b>", message);
                    }
                };

                ExpectLogContaining("Hello <b>[1,2,3]</b>");
                go.Log($"Hello {new List<int> { 1, 2, 3 }:json,b}", pretty: pretty);
                Assert.AreEqual(++expectedLogCount, logCount);
                Assert.IsTrue(exception == null, exception?.ToString());

                assertion = message =>
                {
                    if (pretty)
                    {
                        Assert.IsTrue(message.Contains(nameof(StackedTags)), message);
                        Assert.IsTrue(message.Contains(nameof(GameObject)), message);
                        Assert.IsTrue(
                            message.Contains("Hello <color=#FF0000FF><b>[1,2,3]</b></color>"),
                            message
                        );
                    }
                    else
                    {
                        Assert.AreEqual("Hello <color=#FF0000FF><b>[1,2,3]</b></color>", message);
                    }
                };

                ExpectLogContaining("Hello <color=#FF0000FF><b>[1,2,3]</b></color>");
                go.Log($"Hello {new List<int> { 1, 2, 3 }:json,b,color=red}", pretty: pretty);
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
        public void TagsDeduplicate(bool pretty)
        {
            if (!WallstopLoggingCompiledIn)
            {
                Assert.Ignore("Package logging is compiled out in this build.");
            }

            GameObject go = Track(new GameObject(nameof(TagsDeduplicate), typeof(SpriteRenderer)));
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
                        Assert.IsTrue(message.Contains(nameof(TagsDeduplicate)), message);
                        Assert.IsTrue(message.Contains(nameof(GameObject)), message);
                        Assert.IsTrue(message.Contains("Hello <b>[1,2,3]</b>"), message);
                    }
                    else
                    {
                        Assert.AreEqual("Hello <b>[1,2,3]</b>", message);
                    }
                };

                ExpectLogContaining("Hello <b>[1,2,3]</b>");
                go.Log(
                    $"Hello {new List<int> { 1, 2, 3 }:json,b,bold,!,bold,b,!,b,bold}",
                    pretty: pretty
                );
                Assert.AreEqual(++expectedLogCount, logCount);
                Assert.IsTrue(exception == null, exception?.ToString());

                assertion = message =>
                {
                    if (pretty)
                    {
                        Assert.IsTrue(message.Contains(nameof(TagsDeduplicate)), message);
                        Assert.IsTrue(message.Contains(nameof(GameObject)), message);
                        Assert.IsTrue(
                            message.Contains("Hello <color=#FF0000FF><b>[1,2,3]</b></color>"),
                            message
                        );
                    }
                    else
                    {
                        Assert.AreEqual("Hello <color=#FF0000FF><b>[1,2,3]</b></color>", message);
                    }
                };

                ExpectLogContaining("Hello <color=#FF0000FF><b>[1,2,3]</b></color>");
                go.Log(
                    $"Hello {new List<int> { 1, 2, 3 }:json,b,!,color=red,b,b,b,b,b,b,b}",
                    pretty: pretty
                );
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

        private static void ExpectLogContaining(string value)
        {
            LogAssert.Expect(LogType.Log, new Regex(Regex.Escape(value)));
        }
    }
}
