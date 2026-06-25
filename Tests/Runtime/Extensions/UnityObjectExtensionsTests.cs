// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if !UNITY_2021 && !UNITY_2022 && !UNITY_2023
#define UNH_HAS_ENTITY_ID
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

#if UNH_HAS_ENTITY_ID
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
    }
}
