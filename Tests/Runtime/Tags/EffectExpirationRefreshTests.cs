// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Tags
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Tags;
    using WallstopStudios.UnityHelpers.Tests.Tags.Helpers;

    [TestFixture]
    [Category("Fast")]
    public sealed class EffectExpirationRefreshTests : TagsTestBase
    {
        [TestCase(0, false)]
        [TestCase(1, false)]
        [TestCase(2, false)]
        [TestCase(0, true)]
        [TestCase(1, true)]
        [TestCase(2, true)]
        public void RemovalCallbackRefreshPreservesOtherExpiredHandles(int refreshMode, bool throws)
        {
            GameObject entity = CreateTrackedGameObject(
                nameof(EffectExpirationRefreshTests),
                typeof(EffectHandler)
            );
            EffectHandler handler = entity.GetComponent<EffectHandler>();
            List<EffectHandle> handles = new();
            for (int i = 0; i < 3; ++i)
            {
                AttributeEffect effect = CreateEffect(
                    "Expiring" + i,
                    authored =>
                    {
                        authored.duration = 5f;
                        authored.resetDurationOnReapplication = true;
                    }
                );
                handles.Add(handler.ApplyEffect(effect, 0f).Value);
            }

            int removedCount = 0;
            handler.OnEffectRemoved += removed =>
            {
                ++removedCount;
                if (removedCount != 1)
                {
                    return;
                }
                foreach (EffectHandle handle in handles)
                {
                    if (handle.id == removed.id)
                    {
                        continue;
                    }
                    if (refreshMode == 0)
                    {
                        Assert.IsTrue(handler.RefreshEffect(handle, false, 10f));
                    }
                    else if (refreshMode == 1)
                    {
                        Assert.AreEqual(
                            handle,
                            handler.EnsureHandle(handle.effect, true, 10f).Value
                        );
                    }
                    else
                    {
                        Assert.AreEqual(handle, handler.ApplyEffect(handle.effect, 10f).Value);
                    }
                }
                if (throws)
                {
                    throw new InvalidOperationException("Removal callback failure");
                }
            };

            if (throws)
            {
                Assert.Throws<InvalidOperationException>(() =>
                    handler.ProcessEffectExpirations(10f)
                );
            }
            else
            {
                handler.ProcessEffectExpirations(10f);
            }
            Assert.AreEqual(1, removedCount);
            Assert.AreEqual(2, handler.GetActiveEffects().Count);
            handler.ProcessEffectExpirations(14f);
            Assert.AreEqual(2, handler.GetActiveEffects().Count);
            handler.ProcessEffectExpirations(15f);
            Assert.AreEqual(3, removedCount);
            Assert.IsEmpty(handler.GetActiveEffects());
        }
    }
}
