// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if UNITY_6000_4_OR_NEWER
#define UNH_HAS_ENTITY_ID_TO_ULONG
#endif

namespace WallstopStudios.UnityHelpers.Tests.Extensions
{
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using Object = UnityEngine.Object;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class UnityObjectExtensionsTests : CommonTestBase
    {
        [Test]
        public void GetUnityObjectIdReturnsZeroForNullObject()
        {
            Object unityObject = null;

            Assert.AreEqual(0L, unityObject.GetUnityObjectId());
        }

        [Test]
        public void GetUnityObjectIdMatchesCurrentUnityObjectIdentifier()
        {
            GameObject gameObject = Track(new GameObject(nameof(UnityObjectExtensionsTests)));

            Assert.AreEqual(GetExpectedObjectId(gameObject), gameObject.GetUnityObjectId());
        }

        [TestCase(false)]
        [TestCase(true)]
        public void FindObjectsOfTypeShimGenericRespectsInactiveFlag(bool includeInactive)
        {
            GameObject activeObject = Track(new GameObject(nameof(activeObject)));
            GameObject inactiveObject = Track(new GameObject(nameof(inactiveObject)));
            inactiveObject.SetActive(false);

            GameObject[] found = UnityObjectExtensions.FindObjectsOfTypeShim<GameObject>(
                includeInactive
            );

            Assert.Contains(activeObject, found);
            Assert.AreEqual(includeInactive, Contains(found, inactiveObject));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void FindObjectsOfTypeShimTypedRespectsInactiveFlag(bool includeInactive)
        {
            GameObject activeObject = Track(new GameObject(nameof(activeObject)));
            GameObject inactiveObject = Track(new GameObject(nameof(inactiveObject)));
            inactiveObject.SetActive(false);

            Object[] found = UnityObjectExtensions.FindObjectsOfTypeShim(
                typeof(GameObject),
                includeInactive
            );

            Assert.Contains(activeObject, found);
            Assert.AreEqual(includeInactive, Contains(found, inactiveObject));
        }

#if UNH_HAS_ENTITY_ID_TO_ULONG
        private static long GetExpectedObjectId(Object unityObject)
        {
            return unchecked((long)EntityId.ToULong(unityObject.GetEntityId()));
        }
#else
        private static long GetExpectedObjectId(Object unityObject)
        {
            return unityObject.GetInstanceID();
        }
#endif

        private static bool Contains(Object[] objects, Object expected)
        {
            for (int i = 0; i < objects.Length; i++)
            {
                if (objects[i] == expected)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
