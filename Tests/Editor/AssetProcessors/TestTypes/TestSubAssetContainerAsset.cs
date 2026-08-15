// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if UNITY_INCLUDE_TESTS
namespace WallstopStudios.UnityHelpers.Tests.AssetProcessors
{
    using UnityEngine;

    /// <summary>
    /// Main asset for the <c>AddObjectToAsset</c> sub-asset guard: a ScriptableObject whose type no
    /// watcher matches, so a watcher on the NESTED type can only match it through
    /// <c>HasMatchingSubAsset</c>.
    /// </summary>
    /// <remarks>
    /// It implements <see cref="ITestDetectableContract"/> deliberately. The nested
    /// <see cref="TestDetectableAsset"/> also implements that contract, so the assignable-type
    /// watcher matches this path too — and that watcher's created-assets argument is built from
    /// <c>LoadAssetAtPath(path, typeof(Object))</c>, which returns the MAIN asset. A main asset
    /// outside the contract would be stored into an <c>ITestDetectableContract[]</c> and throw
    /// <see cref="System.InvalidCastException"/> out of the processor, failing this fixture for a
    /// reason that has nothing to do with what it tests.
    /// </remarks>
    internal sealed class TestSubAssetContainerAsset : ScriptableObject, ITestDetectableContract { }
}
#endif
