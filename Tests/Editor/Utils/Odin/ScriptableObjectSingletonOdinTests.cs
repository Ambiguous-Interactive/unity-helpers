// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if UNITY_EDITOR && WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
namespace WallstopStudios.UnityHelpers.Tests.Utils.Odin
{
    using NUnit.Framework;
    using Sirenix.OdinInspector;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Utils;

    [TestFixture]
    public sealed class ScriptableObjectSingletonOdinTests : CommonTestBase
    {
        [Test]
        public void ScriptableObjectSingletonUsesOdinSerializedBaseWhenOdinPresent()
        {
            OdinScriptableObjectSingletonTestTarget instance = Track(
                ScriptableObject.CreateInstance<OdinScriptableObjectSingletonTestTarget>()
            );

            Assert.That(instance, Is.InstanceOf<SerializedScriptableObject>());
        }

        [ExcludeFromSingletonCreation]
        private sealed class OdinScriptableObjectSingletonTestTarget
            : ScriptableObjectSingleton<OdinScriptableObjectSingletonTestTarget> { }
    }
}
#endif
