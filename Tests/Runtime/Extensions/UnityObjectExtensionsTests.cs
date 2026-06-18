// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

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

            Assert.AreEqual(0, unityObject.GetUnityObjectId());
        }

        [Test]
        public void GetUnityObjectIdMatchesCurrentUnityObjectIdentifier()
        {
            GameObject gameObject = Track(new GameObject(nameof(UnityObjectExtensionsTests)));

            Assert.AreEqual(GetExpectedObjectId(gameObject), gameObject.GetUnityObjectId());
        }

#if UNITY_6000_0_OR_NEWER
        private static int GetExpectedObjectId(Object unityObject)
        {
            return unityObject.GetEntityId();
        }
#else
        private static int GetExpectedObjectId(Object unityObject)
        {
            return unityObject.GetInstanceID();
        }
#endif
    }
}
