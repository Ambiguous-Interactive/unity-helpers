// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Tags
{
    using System.Collections;
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Tags;
    using WallstopStudios.UnityHelpers.Tests.Tags.Helpers;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class CosmeticEffectDataTests : TagsTestBase
    {
        [UnityTest]
        public IEnumerator RequiresInstancingTrueWhenAnyComponentRequestsIt()
        {
            GameObject cosmetic = CreateTrackedGameObject("Cosmetic", typeof(CosmeticEffectData));
            yield return null;
            ProbeCosmeticComponent component = cosmetic.AddComponent<ProbeCosmeticComponent>();
            component.requiresInstance = true;

            CosmeticEffectData data = cosmetic.GetComponent<CosmeticEffectData>();
            Assert.IsTrue(data.RequiresInstancing);
        }

        [UnityTest]
        public IEnumerator EqualsReturnsTrueWhenNamesAndComponentsMatch()
        {
            GameObject first = CreateTrackedGameObject("CosmeticA", typeof(CosmeticEffectData));
            yield return null;
            _ = first.AddComponent<ProbeCosmeticComponent>();

            GameObject second = CreateTrackedGameObject("CosmeticB", typeof(CosmeticEffectData));
            yield return null;
            second.name = first.name;
            _ = second.AddComponent<ProbeCosmeticComponent>();

            CosmeticEffectData firstData = first.GetComponent<CosmeticEffectData>();
            CosmeticEffectData secondData = second.GetComponent<CosmeticEffectData>();
            Assert.IsTrue(firstData.Equals(secondData));
            Assert.AreEqual(firstData.GetHashCode(), secondData.GetHashCode());
        }

        [UnityTest]
        public IEnumerator EqualsReturnsFalseWhenComponentSetsDiffer()
        {
            GameObject first = CreateTrackedGameObject("Cosmetic", typeof(CosmeticEffectData));
            yield return null;
            _ = first.AddComponent<ProbeCosmeticComponent>();

            GameObject second = CreateTrackedGameObject("Cosmetic", typeof(CosmeticEffectData));
            yield return null;
            _ = second.AddComponent<ProbeCosmeticComponent>();
            _ = second.AddComponent<SecondaryProbeCosmeticComponent>();

            CosmeticEffectData firstData = first.GetComponent<CosmeticEffectData>();
            CosmeticEffectData secondData = second.GetComponent<CosmeticEffectData>();
            Assert.IsFalse(firstData.Equals(secondData));
        }

        [UnityTest]
        public IEnumerator RequiresInstancingReturnsFalseWithNoComponents()
        {
            GameObject cosmetic = CreateTrackedGameObject("Cosmetic", typeof(CosmeticEffectData));
            yield return null;

            CosmeticEffectData data = cosmetic.GetComponent<CosmeticEffectData>();
            Assert.IsFalse(data.RequiresInstancing);
        }

        [UnityTest]
        public IEnumerator RequiresInstancingReturnsFalseWhenAllComponentsDoNotRequireInstance()
        {
            GameObject cosmetic = CreateTrackedGameObject("Cosmetic", typeof(CosmeticEffectData));
            yield return null;

            ProbeCosmeticComponent component1 = cosmetic.AddComponent<ProbeCosmeticComponent>();
            component1.requiresInstance = false;
            ProbeCosmeticComponent component2 = cosmetic.AddComponent<ProbeCosmeticComponent>();
            component2.requiresInstance = false;
            _ = cosmetic.AddComponent<SecondaryProbeCosmeticComponent>();

            CosmeticEffectData data = cosmetic.GetComponent<CosmeticEffectData>();
            Assert.IsFalse(data.RequiresInstancing);
        }

        [UnityTest]
        public IEnumerator RequiresInstancingDetectsNewComponentsAddedAtRuntime()
        {
            GameObject cosmetic = CreateTrackedGameObject("Cosmetic", typeof(CosmeticEffectData));
            yield return null;

            CosmeticEffectData data = cosmetic.GetComponent<CosmeticEffectData>();
            Assert.IsFalse(data.RequiresInstancing);

            ProbeCosmeticComponent component = cosmetic.AddComponent<ProbeCosmeticComponent>();
            component.requiresInstance = true;

            Assert.IsTrue(data.RequiresInstancing);
        }

        [UnityTest]
        public IEnumerator RequiresInstancingHandlesDestroyedComponents()
        {
            GameObject cosmetic = CreateTrackedGameObject("Cosmetic", typeof(CosmeticEffectData));
            yield return null;

            ProbeCosmeticComponent component = cosmetic.AddComponent<ProbeCosmeticComponent>();
            component.requiresInstance = true;

            CosmeticEffectData data = cosmetic.GetComponent<CosmeticEffectData>();
            Assert.IsTrue(data.RequiresInstancing);

            Object.Destroy(component); // UNH-SUPPRESS UNH001: Intentionally testing destroyed component behavior
            yield return null;

            Assert.IsFalse(data.RequiresInstancing);
        }

        [UnityTest]
        public IEnumerator EqualsReflectsCurrentStateWhenComponentsChange()
        {
            GameObject first = CreateTrackedGameObject("Cosmetic", typeof(CosmeticEffectData));
            yield return null;
            _ = first.AddComponent<ProbeCosmeticComponent>();

            GameObject second = CreateTrackedGameObject("Cosmetic", typeof(CosmeticEffectData));
            yield return null;
            _ = second.AddComponent<ProbeCosmeticComponent>();

            CosmeticEffectData firstData = first.GetComponent<CosmeticEffectData>();
            CosmeticEffectData secondData = second.GetComponent<CosmeticEffectData>();
            Assert.IsTrue(firstData.Equals(secondData));

            _ = first.AddComponent<SecondaryProbeCosmeticComponent>();

            Assert.IsFalse(firstData.Equals(secondData));
        }

        [UnityTest]
        public IEnumerator GetHashCodeReflectsCurrentComponentTypeSet()
        {
            GameObject cosmetic = CreateTrackedGameObject("Cosmetic", typeof(CosmeticEffectData));
            yield return null;

            CosmeticEffectData data = cosmetic.GetComponent<CosmeticEffectData>();
            int initialHashCode = data.GetHashCode();

            _ = cosmetic.AddComponent<ProbeCosmeticComponent>();

            int newHashCode = data.GetHashCode();
            Assert.AreNotEqual(initialHashCode, newHashCode);

            /*
                A second copy of a type already present leaves the type set -- and so the hash --
                exactly where it was, which is what equality compares.
            */
            _ = cosmetic.AddComponent<ProbeCosmeticComponent>();
            Assert.AreEqual(newHashCode, data.GetHashCode());
        }

        [UnityTest]
        public IEnumerator DuplicateComponentsKeepEqualInstancesInTheSameBucket()
        {
            GameObject single = CreateTrackedGameObject("Cosmetic", typeof(CosmeticEffectData));
            yield return null;
            _ = single.AddComponent<ProbeCosmeticComponent>();

            GameObject doubled = CreateTrackedGameObject("Cosmetic", typeof(CosmeticEffectData));
            yield return null;
            _ = doubled.AddComponent<ProbeCosmeticComponent>();
            _ = doubled.AddComponent<ProbeCosmeticComponent>();

            CosmeticEffectData singleData = single.GetComponent<CosmeticEffectData>();
            CosmeticEffectData doubledData = doubled.GetComponent<CosmeticEffectData>();

            /*
                CosmeticEffectComponent carries no [DisallowMultipleComponent], so one copy and two
                copies expose the same deduplicated type set. Equality has always said these are
                equal; the hash used to count components and disagreed.
            */
            Assert.IsTrue(singleData.Equals(doubledData));
            Assert.IsTrue(doubledData.Equals(singleData));
            Assert.AreEqual(singleData.GetHashCode(), doubledData.GetHashCode());

            HashSet<CosmeticEffectData> bucket = new() { singleData };
            Assert.IsTrue(bucket.Contains(doubledData));
        }

        [UnityTest]
        public IEnumerator EqualsWithDestroyedComponent()
        {
            GameObject first = CreateTrackedGameObject("Cosmetic", typeof(CosmeticEffectData));
            yield return null;
            ProbeCosmeticComponent firstComponent = first.AddComponent<ProbeCosmeticComponent>();

            GameObject second = CreateTrackedGameObject("Cosmetic", typeof(CosmeticEffectData));
            yield return null;
            _ = second.AddComponent<ProbeCosmeticComponent>();

            CosmeticEffectData firstData = first.GetComponent<CosmeticEffectData>();
            CosmeticEffectData secondData = second.GetComponent<CosmeticEffectData>();
            Assert.IsTrue(firstData.Equals(secondData));

            Object.Destroy(firstComponent); // UNH-SUPPRESS UNH001: Intentionally testing destroyed component behavior
            yield return null;

            Assert.IsFalse(firstData.Equals(secondData));
        }

        [UnityTest]
        public IEnumerator EqualsAndGetHashCodeAnswerForADestroyedInstance()
        {
            GameObject first = CreateTrackedGameObject("Cosmetic", typeof(CosmeticEffectData));
            yield return null;
            ProbeCosmeticComponent firstComponent = first.AddComponent<ProbeCosmeticComponent>();
            firstComponent.requiresInstance = true;

            GameObject second = CreateTrackedGameObject("Cosmetic", typeof(CosmeticEffectData));
            yield return null;
            _ = second.AddComponent<ProbeCosmeticComponent>();

            CosmeticEffectData firstData = first.GetComponent<CosmeticEffectData>();
            CosmeticEffectData secondData = second.GetComponent<CosmeticEffectData>();
            Assert.IsTrue(firstData.Equals(secondData));

            /*
                ProbeCosmeticComponent requires CosmeticEffectData, so Unity refuses to remove the
                subject while the probe is still attached -- and logs an error the Test Framework
                fails on. The probe goes first so the subject is genuinely destroyed rather than
                left alive behind an expected-log suppression, which would assert nothing.
            */
            Object.Destroy(firstComponent); // UNH-SUPPRESS UNH001: clearing the RequireComponent dependency
            yield return null;

            Object.Destroy(firstData); // UNH-SUPPRESS UNH001: the destroyed instance is the subject
            yield return null;

            Assert.IsTrue(firstData == null, "The probe must have destroyed its subject");

            /*
                GetComponents raises MissingReferenceException once the native side is gone, and a
                hash is computed on every probe of every collection this instance is in.
            */
            int hash = firstData.GetHashCode();
            Assert.AreEqual(
                hash,
                firstData.GetHashCode(),
                "A destroyed instance must hash to a stable value"
            );
            Assert.IsFalse(firstData.Equals(secondData));
            Assert.IsFalse(secondData.Equals(firstData), "Inequality must hold in both directions");
            Assert.IsTrue(
                firstData.Equals(firstData),
                "A destroyed instance must still be equal to itself"
            );
            Assert.IsFalse(firstData.RequiresInstancing);
        }

        [UnityTest]
        public IEnumerator ADictionaryStillResolvesTheDestroyedKeyItStored()
        {
            GameObject first = CreateTrackedGameObject("Cosmetic", typeof(CosmeticEffectData));
            yield return null;
            GameObject second = CreateTrackedGameObject("Cosmetic", typeof(CosmeticEffectData));
            yield return null;

            CosmeticEffectData firstData = first.GetComponent<CosmeticEffectData>();
            CosmeticEffectData secondData = second.GetComponent<CosmeticEffectData>();

            Object.Destroy(firstData); // UNH-SUPPRESS UNH001: the destroyed instance is the subject
            Object.Destroy(secondData); // UNH-SUPPRESS UNH001: the destroyed instance is the subject
            yield return null;

            /*
                Every destroyed instance hashes alike, so both land in one bucket. That is a
                collision, not a claim they are the same cosmetic: a caller that stored one must
                still get its own value back rather than the other's.
            */
            Dictionary<CosmeticEffectData, string> stored = new() { [firstData] = "first" };
            Assert.IsTrue(stored.TryGetValue(firstData, out string value));
            Assert.AreEqual("first", value);
            Assert.IsFalse(stored.ContainsKey(secondData));
            Assert.IsFalse(firstData.Equals(secondData));
        }

        [UnityTest]
        public IEnumerator RequiresInstancingHandlesComponentRemovalAtRuntime()
        {
            GameObject cosmetic = CreateTrackedGameObject("Cosmetic", typeof(CosmeticEffectData));
            yield return null;

            ProbeCosmeticComponent component = cosmetic.AddComponent<ProbeCosmeticComponent>();
            component.requiresInstance = true;

            CosmeticEffectData data = cosmetic.GetComponent<CosmeticEffectData>();
            Assert.IsTrue(data.RequiresInstancing);

            Object.Destroy(component); // UNH-SUPPRESS UNH001: Intentionally testing destroyed component behavior
            yield return null;

            Assert.IsFalse(data.RequiresInstancing);
        }
    }
}
