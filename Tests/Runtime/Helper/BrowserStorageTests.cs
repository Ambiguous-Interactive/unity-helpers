// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Helper
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Helper;
#if !UNITY_WEBGL || UNITY_EDITOR
    using System.Globalization;
    using UnityEngine;
#endif

    [TestFixture]
    [Category("Fast")]
    public sealed class BrowserStorageTests
    {
        private readonly List<(BrowserStorage store, string key)> _ownedKeys = new();
        private string _scope;

        [SetUp]
        public void SetUp()
        {
            _scope = nameof(BrowserStorageTests) + Guid.NewGuid().ToString("N");
        }

        [TearDown]
        public void TearDown()
        {
            foreach ((BrowserStorage store, string key) in _ownedKeys)
            {
                Assert.That(store.TryDeleteKey(key), Is.True);
            }
            _ownedKeys.Clear();
        }

        [TestCase("")]
        [TestCase(" ")]
        [TestCase("ordinary")]
        [TestCase("Unicode: café 雪 😀")]
        [TestCase("line1\nline2\t\"quoted\"\\")]
        public void WritesReadsReplacesAndDeletesExactValue(string value)
        {
            BrowserStorage store = OwnStore(_scope, nameof(value));
            Assert.That(store.IsConfigured, Is.True);
            Assert.That(store.TryGetString(nameof(value), out string missing), Is.False);
            Assert.That(missing, Is.Null);
            Assert.That(store.TrySetString(nameof(value), value), Is.True);
            Assert.That(store.TryGetString(nameof(value), out string actual), Is.True);
            Assert.That(actual, Is.EqualTo(value));
            BrowserStorage sameScope = new(_scope);
            Assert.That(sameScope.TryGetString(nameof(value), out string reopened), Is.True);
            Assert.That(reopened, Is.EqualTo(value));
            Assert.That(store.TrySetString(nameof(value), "replacement"), Is.True);
            Assert.That(store.TryGetString(nameof(value), out string replacement), Is.True);
            Assert.That(replacement, Is.EqualTo("replacement"));
            Assert.That(store.TryDeleteKey(nameof(value)), Is.True);
            Assert.That(store.TryDeleteKey(nameof(value)), Is.True);
            Assert.That(store.TryGetString(nameof(value), out string deleted), Is.False);
            Assert.That(deleted, Is.Null);
        }

        [TestCase(" ")]
        [TestCase("x:y")]
        [TestCase("雪😀")]
        public void KeysRemainLiteral(string key)
        {
            BrowserStorage store = OwnStore(_scope, key);
            Assert.That(store.TrySetString(key, nameof(key)), Is.True);
            Assert.That(store.TryGetString(key, out string value), Is.True);
            Assert.That(value, Is.EqualTo(nameof(key)));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase(" ")]
        [TestCase("scope\0tail")]
        public void InvalidScopeRejectsEveryOperation(string scope)
        {
            BrowserStorage store = new(scope);
            Assert.That(store.IsConfigured, Is.False);
            Assert.That(store.TrySetString(nameof(scope), "value"), Is.False);
            Assert.That(store.TryDeleteKey(nameof(scope)), Is.False);
            Assert.That(store.TryGetString(nameof(scope), out string value), Is.False);
            Assert.That(value, Is.Null);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("key\0tail")]
        public void InvalidKeysRejectEveryOperation(string key)
        {
            BrowserStorage store = new(_scope);
            Assert.That(store.TrySetString(key, "value"), Is.False);
            Assert.That(store.TryDeleteKey(key), Is.False);
            Assert.That(store.TryGetString(key, out string value), Is.False);
            Assert.That(value, Is.Null);
        }

        [TestCase(null)]
        [TestCase("bad\0value")]
        public void InvalidValuePreservesExistingValue(string value)
        {
            BrowserStorage store = OwnStore(_scope, nameof(value));
            Assert.That(store.TrySetString(nameof(value), "original"), Is.True);
            Assert.That(store.TrySetString(nameof(value), value), Is.False);
            Assert.That(store.TryGetString(nameof(value), out string actual), Is.True);
            Assert.That(actual, Is.EqualTo("original"));
        }

        [TestCase(0xD800)]
        [TestCase(0xDC00)]
        public void UnpairedSurrogateRejectsScopesKeysAndValues(int codeUnit)
        {
            string invalid = "head" + (char)codeUnit + "tail";
            Assert.That(invalid[4], Is.EqualTo((char)codeUnit));
            InvalidScopeRejectsEveryOperation(invalid);
            InvalidKeysRejectEveryOperation(invalid);
            InvalidValuePreservesExistingValue(invalid);
        }

        [Test]
        public void ScopeSeparatorsCannotAliasKeys()
        {
            BrowserStorage first = OwnStore(_scope, "child:key");
            BrowserStorage second = OwnStore(_scope + ":child", "key");
            Assert.That(first.TrySetString("child:key", "first"), Is.True);
            Assert.That(second.TrySetString("key", "second"), Is.True);
            Assert.That(first.TryGetString("child:key", out string firstValue), Is.True);
            Assert.That(second.TryGetString("key", out string secondValue), Is.True);
            Assert.That(firstValue, Is.EqualTo("first"));
            Assert.That(secondValue, Is.EqualTo("second"));
            Assert.That(first.TryDeleteKey("child:key"), Is.True);
            Assert.That(second.TryGetString("key", out string remaining), Is.True);
            Assert.That(remaining, Is.EqualTo("second"));
        }

#if !UNITY_WEBGL || UNITY_EDITOR
        [TestCase(9)]
        [TestCase(10)]
        [TestCase(99)]
        [TestCase(100)]
        [TestCase(999)]
        [TestCase(1000)]
        public void ReadsLegacyPlayerPrefsScopeLengthFormat(int scopeLength)
        {
            string scope = Guid.NewGuid()
                .ToString("N")
                .PadRight(scopeLength, 's')
                .Substring(0, scopeLength);
            Assert.That(scope.Length, Is.EqualTo(scopeLength));
            string key = nameof(scopeLength);
            BrowserStorage store = OwnStore(scope, key);
            string legacyKey =
                "WallstopBrowserStorage:"
                + scopeLength.ToString(CultureInfo.InvariantCulture)
                + ":"
                + scope
                + ":"
                + key;
            PlayerPrefs.SetString(legacyKey, "legacy-value");
            try
            {
                Assert.That(store.TryGetString(key, out string value), Is.True);
                Assert.That(value, Is.EqualTo("legacy-value"));
                Assert.That(store.TrySetString(key, "replacement"), Is.True);
                Assert.That(PlayerPrefs.GetString(legacyKey), Is.EqualTo("replacement"));
            }
            finally
            {
                PlayerPrefs.DeleteKey(legacyKey);
            }
        }
#endif

        private BrowserStorage OwnStore(string scope, string key)
        {
            BrowserStorage store = new(scope);
            _ownedKeys.Add((store, key));
            return store;
        }
    }
}
