// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Tags
{
    using System.Collections;
    using System.Collections.Generic;
    using System.Reflection;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Tags;
    using WallstopStudios.UnityHelpers.Tests.Tags.Helpers;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class AttributeUtilitiesTests : TagsTestBase
    {
        [SetUp]
        public void SetUp()
        {
            ResetEffectHandleId();
            ClearAttributeUtilitiesCaches();
        }

        [TearDown]
        public override void TearDown()
        {
            base.TearDown();
            ClearAttributeUtilitiesCaches();
        }

        [Test]
        public void GetAllAttributeNamesIncludesDerivedComponentFields()
        {
            string[] allNames = AttributeUtilities.GetAllAttributeNames();
            CollectionAssert.Contains(allNames, nameof(TestAttributesComponent.health));
            CollectionAssert.Contains(allNames, nameof(TestAttributesComponent.armor));
        }

        [Test]
        public void GetAttributeFieldsCachesPerType()
        {
            Dictionary<string, FieldInfo> first = AttributeUtilities.GetAttributeFields(
                typeof(TestAttributesComponent)
            );
            Dictionary<string, FieldInfo> second = AttributeUtilities.GetAttributeFields(
                typeof(TestAttributesComponent)
            );
            Assert.IsTrue(ReferenceEquals(first, second));
            Assert.IsTrue(first.ContainsKey(nameof(TestAttributesComponent.health)));
        }

        [Test]
        public void HasTagReturnsFalseForNullTargets()
        {
            Object target = null;
            Assert.IsFalse(target.HasTag("Buff"));
        }

        [UnityTest]
        public IEnumerator HasTagReturnsTrueWhenHandlerContainsTag()
        {
            GameObject entity = CreateTrackedGameObject("Tagged", typeof(TagHandler));
            yield return null;
            TagHandler handler = entity.GetComponent<TagHandler>();
            handler.ApplyTag("Buff");
            Assert.IsTrue(entity.HasTag("Buff"));
            Assert.IsFalse(entity.HasTag("Missing"));
        }

        [UnityTest]
        public IEnumerator HasAnyTagChecksEnumerableAndReadOnlyList()
        {
            GameObject entity = CreateTrackedGameObject("Tagged", typeof(TagHandler));
            yield return null;
            TagHandler handler = entity.GetComponent<TagHandler>();
            handler.ApplyTag("Buff");

            Assert.IsTrue(entity.HasAnyTag(new[] { "Missing", "Buff" }));
            Assert.IsFalse(entity.HasAnyTag(new[] { "Missing" }));

            IReadOnlyList<string> list = new List<string> { "Other", "Buff" };
            Assert.IsTrue(entity.HasAnyTag(list));
        }

        [UnityTest]
        public IEnumerator GetActiveTagsExtensionHandlesBuffers()
        {
            GameObject entity = CreateTrackedGameObject("Tagged", typeof(TagHandler));
            yield return null;
            TagHandler handler = entity.GetComponent<TagHandler>();
            handler.ApplyTag("Buff");
            handler.ApplyTag("Shield");

            List<string> initial = entity.GetActiveTags();
            CollectionAssert.AreEquivalent(new[] { "Buff", "Shield" }, initial);

            List<string> reusable = new() { "Sentinel" };
            List<string> reused = entity.GetActiveTags(reusable);
            Assert.AreSame(reusable, reused);
            CollectionAssert.AreEquivalent(new[] { "Buff", "Shield" }, reused);
            Assert.IsFalse(reused.Contains("Sentinel"));
        }

        [UnityTest]
        public IEnumerator GetHandlesWithTagExtensionCreatesBuffers()
        {
            GameObject entity = CreateTrackedGameObject("Entity", typeof(TestAttributesComponent));
            yield return null;
            EffectHandler effectHandler = entity.GetComponent<EffectHandler>();

            AttributeEffect effect = CreateEffect(
                "Buff",
                e =>
                {
                    e.effectTags.Add("Buff");
                }
            );
            EffectHandle handle = effectHandler.ApplyEffect(effect).Value;

            List<EffectHandle> handles = entity.GetHandlesWithTag("Buff");
            Assert.AreEqual(1, handles.Count);
            Assert.AreEqual(handle, handles[0]);

            List<EffectHandle> reusable = new() { EffectHandle.CreateInstance(effect) };
            List<EffectHandle> reused = entity.GetHandlesWithTag("Buff", reusable);
            Assert.AreSame(reusable, reused);
            Assert.AreEqual(1, reused.Count);
            Assert.AreEqual(handle, reused[0]);
        }

        [Test]
        public void AttributeEffectQueryHelpersReturnExpectedResults()
        {
            AttributeEffect effect = CreateEffect(
                "Query",
                e =>
                {
                    e.effectTags.Add("Buff");
                    e.effectTags.Add("Shield");
                    e.modifications.Add(
                        new AttributeModification
                        {
                            attribute = nameof(TestAttributesComponent.health),
                            action = ModificationAction.Addition,
                            value = 5f,
                        }
                    );
                    e.modifications.Add(
                        new AttributeModification
                        {
                            attribute = nameof(TestAttributesComponent.armor),
                            action = ModificationAction.Addition,
                            value = 3f,
                        }
                    );
                    e.modifications.Add(
                        new AttributeModification
                        {
                            attribute = nameof(TestAttributesComponent.health),
                            action = ModificationAction.Multiplication,
                            value = 1.1f,
                        }
                    );
                }
            );

            Assert.IsTrue(effect.HasTag("Buff"));
            Assert.IsFalse(effect.HasTag("Missing"));

            HashSet<string> query = new() { "Missing", "Shield" };
            Assert.IsTrue(effect.HasAnyTag(query));
            IReadOnlyList<string> list = new List<string> { "Other", "Buff" };
            Assert.IsTrue(effect.HasAnyTag(list));
            List<string> none = new() { "Missing" };
            Assert.IsFalse(effect.HasAnyTag(none));

            Assert.IsTrue(effect.ModifiesAttribute(nameof(TestAttributesComponent.health)));
            Assert.IsTrue(effect.ModifiesAttribute(nameof(TestAttributesComponent.armor)));
            Assert.IsFalse(effect.ModifiesAttribute("Speed"));

            List<AttributeModification> buffer = new();
            effect.GetModifications(nameof(TestAttributesComponent.health), buffer);
            Assert.AreEqual(2, buffer.Count);
            Assert.AreEqual(nameof(TestAttributesComponent.health), buffer[0].attribute);
            Assert.AreEqual(nameof(TestAttributesComponent.health), buffer[1].attribute);

            effect.GetModifications("Speed", buffer);
            Assert.AreEqual(0, buffer.Count);
        }

        [UnityTest]
        public IEnumerator ApplyEffectAddsAttributesCosmeticsAndTags()
        {
            GameObject entity = CreateTrackedGameObject("Entity", typeof(TestAttributesComponent));
            yield return null;
            TestAttributesComponent attributes = entity.GetComponent<TestAttributesComponent>();
            TagHandler tagHandler = entity.GetComponent<TagHandler>();

            AttributeEffect effect = CreateEffect(
                "Might",
                e =>
                {
                    e.effectTags.Add("Buff");
                    e.modifications.Add(
                        new AttributeModification
                        {
                            attribute = nameof(TestAttributesComponent.health),
                            action = ModificationAction.Addition,
                            value = 15f,
                        }
                    );
                }
            );

            EffectHandle? handle = entity.ApplyEffect(effect);
            Assert.IsTrue(handle.HasValue);
            Assert.AreEqual(115f, attributes.health.CurrentValue);
            Assert.IsTrue(tagHandler.HasTag("Buff"));
            Assert.AreEqual(1, attributes.notifications.Count);
        }

        [UnityTest]
        public IEnumerator ApplyEffectReusesHandleWhenResetDisabled()
        {
            GameObject entity = CreateTrackedGameObject("Entity", typeof(TestAttributesComponent));
            yield return null;
            TestAttributesComponent attributes = entity.GetComponent<TestAttributesComponent>();

            AttributeEffect effect = CreateEffect(
                "Might",
                e =>
                {
                    e.resetDurationOnReapplication = false;
                    e.modifications.Add(
                        new AttributeModification
                        {
                            attribute = nameof(TestAttributesComponent.health),
                            action = ModificationAction.Addition,
                            value = 5f,
                        }
                    );
                }
            );

            EffectHandle first = entity.ApplyEffect(effect).Value;
            attributes.notifications.Clear();
            EffectHandle second = entity.ApplyEffect(effect).Value;
            Assert.AreEqual(first, second);
            Assert.IsEmpty(attributes.notifications);
        }

        [UnityTest]
        public IEnumerator ApplyEffectsNoAllocPopulatesHandleList()
        {
            GameObject entity = CreateTrackedGameObject("Entity", typeof(TestAttributesComponent));
            yield return null;
            List<AttributeEffect> effects = new()
            {
                CreateEffect(
                    "BuffA",
                    e =>
                    {
                        e.modifications.Add(
                            new AttributeModification
                            {
                                attribute = nameof(TestAttributesComponent.health),
                                action = ModificationAction.Addition,
                                value = 5f,
                            }
                        );
                    }
                ),
                CreateEffect(
                    "BuffB",
                    e =>
                    {
                        e.modifications.Add(
                            new AttributeModification
                            {
                                attribute = nameof(TestAttributesComponent.armor),
                                action = ModificationAction.Addition,
                                value = 10f,
                            }
                        );
                    }
                ),
            };

            List<EffectHandle> handles = new();
            entity.ApplyEffectsNoAlloc(effects, handles);
            Assert.AreEqual(2, handles.Count);
        }

        [UnityTest]
        public IEnumerator ApplyEffectsReturnsHandles()
        {
            GameObject entity = CreateTrackedGameObject("Entity", typeof(TestAttributesComponent));
            yield return null;
            List<AttributeEffect> effects = new()
            {
                CreateEffect(
                    "BuffA",
                    e =>
                    {
                        e.modifications.Add(
                            new AttributeModification
                            {
                                attribute = nameof(TestAttributesComponent.health),
                                action = ModificationAction.Addition,
                                value = 5f,
                            }
                        );
                    }
                ),
                CreateEffect(
                    "BuffB",
                    e =>
                    {
                        e.modifications.Add(
                            new AttributeModification
                            {
                                attribute = nameof(TestAttributesComponent.armor),
                                action = ModificationAction.Addition,
                                value = 10f,
                            }
                        );
                    }
                ),
            };

            List<EffectHandle> handles = entity.ApplyEffects(effects);
            Assert.AreEqual(2, handles.Count);
        }

        [UnityTest]
        public IEnumerator ApplyEffectsNoAllocEnumerableAppliesAll()
        {
            GameObject entity = CreateTrackedGameObject("Entity", typeof(TestAttributesComponent));
            yield return null;
            IEnumerable<AttributeEffect> effects = new[]
            {
                CreateEffect(
                    "BuffA",
                    e =>
                    {
                        e.modifications.Add(
                            new AttributeModification
                            {
                                attribute = nameof(TestAttributesComponent.health),
                                action = ModificationAction.Addition,
                                value = 5f,
                            }
                        );
                    }
                ),
                CreateEffect(
                    "BuffB",
                    e =>
                    {
                        e.modifications.Add(
                            new AttributeModification
                            {
                                attribute = nameof(TestAttributesComponent.armor),
                                action = ModificationAction.Addition,
                                value = 10f,
                            }
                        );
                    }
                ),
            };

            entity.ApplyEffectsNoAlloc(effects);
            TestAttributesComponent attributes = entity.GetComponent<TestAttributesComponent>();
            Assert.AreEqual(105f, attributes.health.CurrentValue);
            Assert.AreEqual(60f, attributes.armor.CurrentValue);
        }

        [UnityTest]
        public IEnumerator RemoveEffectRestoresAttributesAndTags()
        {
            GameObject entity = CreateTrackedGameObject("Entity", typeof(TestAttributesComponent));
            yield return null;
            AttributeEffect effect = CreateEffect(
                "Might",
                e =>
                {
                    e.effectTags.Add("Buff");
                    e.modifications.Add(
                        new AttributeModification
                        {
                            attribute = nameof(TestAttributesComponent.health),
                            action = ModificationAction.Addition,
                            value = 20f,
                        }
                    );
                }
            );

            EffectHandle handle = entity.ApplyEffect(effect).Value;
            entity.RemoveEffect(handle);

            TestAttributesComponent attributes = entity.GetComponent<TestAttributesComponent>();
            Assert.AreEqual(100f, attributes.health.CurrentValue);
            Assert.IsFalse(entity.HasTag("Buff"));
        }

        [UnityTest]
        public IEnumerator RemoveEffectsRemovesAllHandles()
        {
            GameObject entity = CreateTrackedGameObject("Entity", typeof(TestAttributesComponent));
            yield return null;
            List<AttributeEffect> effects = new()
            {
                CreateEffect(
                    "BuffA",
                    e =>
                    {
                        e.modifications.Add(
                            new AttributeModification
                            {
                                attribute = nameof(TestAttributesComponent.health),
                                action = ModificationAction.Addition,
                                value = 5f,
                            }
                        );
                    }
                ),
                CreateEffect(
                    "BuffB",
                    e =>
                    {
                        e.modifications.Add(
                            new AttributeModification
                            {
                                attribute = nameof(TestAttributesComponent.armor),
                                action = ModificationAction.Addition,
                                value = 10f,
                            }
                        );
                    }
                ),
            };

            List<EffectHandle> handles = entity.ApplyEffects(effects);
            entity.RemoveEffects(handles);

            TestAttributesComponent attributes = entity.GetComponent<TestAttributesComponent>();
            Assert.AreEqual(100f, attributes.health.CurrentValue);
            Assert.AreEqual(50f, attributes.armor.CurrentValue);
        }

        [UnityTest]
        public IEnumerator RemoveAllEffectsClearsState()
        {
            GameObject entity = CreateTrackedGameObject("Entity", typeof(TestAttributesComponent));
            yield return null;
            AttributeEffect effect = CreateEffect(
                "Buff",
                e =>
                {
                    e.effectTags.Add("Buff");
                    e.modifications.Add(
                        new AttributeModification
                        {
                            attribute = nameof(TestAttributesComponent.health),
                            action = ModificationAction.Addition,
                            value = 5f,
                        }
                    );
                }
            );

            _ = entity.ApplyEffect(effect);
            entity.RemoveAllEffects();

            TestAttributesComponent attributes = entity.GetComponent<TestAttributesComponent>();
            Assert.AreEqual(100f, attributes.health.CurrentValue);
            Assert.IsFalse(entity.HasTag("Buff"));
        }

        [UnityTest]
        public IEnumerator ExtensionHelpersBridgeTagAndEffectQueries()
        {
            GameObject entity = CreateTrackedGameObject("Entity", typeof(TestAttributesComponent));
            yield return null;

            AttributeEffect effect = CreateEffect(
                "Utility",
                e =>
                {
                    e.duration = 0.3f;
                    e.resetDurationOnReapplication = true;
                    e.effectTags.Add("Buff");
                    e.modifications.Add(
                        new AttributeModification
                        {
                            attribute = nameof(TestAttributesComponent.health),
                            action = ModificationAction.Addition,
                            value = 5f,
                        }
                    );
                }
            );

            List<string> tagsBuffer = new();
            List<EffectHandle> handlesBuffer = new();

            Assert.IsFalse(entity.HasAllTags(new[] { "Buff" }));
            EffectHandle? handle = entity.ApplyEffect(effect);
            Assert.IsTrue(handle.HasValue);

            Assert.IsTrue(entity.HasAllTags(new[] { "Buff" }));
            Assert.IsFalse(entity.HasAllTags(new[] { "Buff", "Missing" }));
            Assert.IsTrue(entity.HasNoneOfTags(new[] { "Missing" }));
            List<string> none = new() { "Buff" };
            Assert.IsFalse(entity.HasNoneOfTags(none));

            Assert.IsTrue(entity.TryGetTagCount("Buff", out int tagCount));
            Assert.AreEqual(1, tagCount);

            Assert.AreNotEqual(0, entity.GetActiveTags(tagsBuffer).Count);
            CollectionAssert.Contains(tagsBuffer, "Buff");

            Assert.AreNotEqual(0, entity.GetHandlesWithTag("Buff", handlesBuffer).Count);
            Assert.AreEqual(1, handlesBuffer.Count);

            Assert.IsTrue(entity.IsEffectActive(effect));
            Assert.AreEqual(1, entity.GetEffectStackCount(effect));

            List<EffectHandle> activeEffects = entity.GetActiveEffects(new List<EffectHandle>());
            Assert.AreEqual(1, activeEffects.Count);

            EffectHandle activeHandle = handlesBuffer[0];
            Assert.IsTrue(entity.TryGetRemainingDuration(activeHandle, out float remaining));
            Assert.Greater(remaining, 0f);

            EffectHandler handler = entity.GetComponent<EffectHandler>();
            handler.RemoveEffect(activeHandle);
            activeHandle = handler.ApplyEffectForTesting(effect, currentTime: -10f).Value;
            Assert.IsTrue(entity.TryGetRemainingDuration(activeHandle, out float beforeNoRefresh));
            Assert.AreEqual(0f, beforeNoRefresh);

            EffectHandle? ensuredNoRefresh = entity.EnsureHandle(effect, refreshDuration: false);
            Assert.IsTrue(ensuredNoRefresh.HasValue);
            Assert.AreEqual(activeHandle, ensuredNoRefresh.Value);
            Assert.IsTrue(entity.TryGetRemainingDuration(activeHandle, out float afterNoRefresh));
            Assert.AreEqual(0f, afterNoRefresh);

            EffectHandle? ensured = entity.EnsureHandle(effect);
            Assert.IsTrue(ensured.HasValue);
            Assert.AreEqual(activeHandle, ensured.Value);
            Assert.IsTrue(entity.TryGetRemainingDuration(activeHandle, out float afterEnsure));
            Assert.Greater(afterEnsure, afterNoRefresh);

            Assert.IsTrue(entity.RefreshEffect(activeHandle));

            entity.RemoveAllEffects();
            tagsBuffer.Clear();
            Assert.AreEqual(0, entity.GetActiveTags(tagsBuffer).Count);
            Assert.IsEmpty(tagsBuffer);
            handlesBuffer.Clear();
            Assert.AreEqual(0, entity.GetHandlesWithTag("Buff", handlesBuffer).Count);
        }

        [UnityTest]
        public IEnumerator ApplyEffectsNoAllocAppendsToProvidedHandlesBuffer()
        {
            GameObject entity = CreateTrackedGameObject("Entity", typeof(TestAttributesComponent));
            yield return null;

            List<AttributeEffect> effects = new() { CreateEffect("BuffA"), CreateEffect("BuffB") };

            EffectHandle sentinel = EffectHandle.CreateInstance(CreateEffect("Sentinel"));
            List<EffectHandle> handles = new() { sentinel };
            entity.ApplyEffectsNoAlloc(effects, handles);

            Assert.AreEqual(effects.Count + 1, handles.Count);
            Assert.AreEqual(sentinel, handles[0]);
        }

        [UnityTest]
        public IEnumerator GetActiveEffectsClearsBufferBeforePopulation()
        {
            GameObject entity = CreateTrackedGameObject("Entity", typeof(TestAttributesComponent));
            yield return null;
            EffectHandler handler = entity.GetComponent<EffectHandler>();

            AttributeEffect effect = CreateEffect("Tracked");
            EffectHandle handle = handler.ApplyEffect(effect).Value;

            EffectHandle sentinel = EffectHandle.CreateInstance(CreateEffect("Sentinel"));
            List<EffectHandle> buffer = new() { sentinel };
            List<EffectHandle> populated = entity.GetActiveEffects(buffer);
            Assert.AreSame(buffer, populated);
            Assert.AreEqual(1, populated.Count);
            Assert.AreEqual(handle, populated[0]);
            Assert.AreNotEqual(sentinel, populated[0]);
            yield return null;
        }

        [UnityTest]
        public IEnumerator RefreshEffectIgnorePolicyExtensionRefreshesWhenDisallowed()
        {
            GameObject entity = CreateTrackedGameObject("Entity", typeof(TestAttributesComponent));
            yield return null;
            AttributeEffect effect = CreateEffect(
                "Policy",
                e =>
                {
                    e.duration = 0.2f;
                    e.resetDurationOnReapplication = false;
                }
            );

            EffectHandler handler = entity.GetComponent<EffectHandler>();
            EffectHandle handle = handler.ApplyEffectForTesting(effect, currentTime: -10f).Value;
            Assert.IsTrue(entity.TryGetRemainingDuration(handle, out float beforeRefresh));
            Assert.AreEqual(0f, beforeRefresh);

            Assert.IsFalse(entity.RefreshEffect(handle));
            Assert.IsTrue(entity.RefreshEffect(handle, ignoreReapplicationPolicy: true));
            Assert.IsTrue(entity.TryGetRemainingDuration(handle, out float afterRefresh));
            Assert.Greater(afterRefresh, beforeRefresh);
        }

        [UnityTest]
        public IEnumerator ApplyEffectsNoAllocSkipsNullTarget()
        {
            Object target = null;
            List<AttributeEffect> effects = new() { CreateEffect("Buff") };
            List<EffectHandle> handles = new();

            target.ApplyEffectsNoAlloc(effects, handles);
            Assert.IsEmpty(handles);
            yield break;
        }

        [UnityTest]
        public IEnumerator ApplyEffectReturnsNullWhenTargetNull()
        {
            Object target = null;
            AttributeEffect effect = CreateEffect("Buff");

            EffectHandle? handle = target.ApplyEffect(effect);
            Assert.IsFalse(handle.HasValue);
            yield break;
        }

        [UnityTest]
        public IEnumerator RemoveEffectsWithEmptyListNoops()
        {
            GameObject entity = CreateTrackedGameObject("Entity", typeof(TestAttributesComponent));
            yield return null;
            entity.RemoveEffects(new List<EffectHandle>());
            Assert.Pass();
        }
    }
}
