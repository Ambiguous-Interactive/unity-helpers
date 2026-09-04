// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Validation
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Editor.Validation;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.Core.TestTypes;

    /// <summary>
    /// Pins what a selection resolves to before anything is constructed.
    /// </summary>
    /// <remarks>
    /// Split from <c>SerializedFieldValidatorTests</c> because it needs a real
    /// <c>ScriptableObject</c> to select, and a fixture that creates Unity objects has to inherit
    /// <see cref="CommonTestBase"/> so teardown can dispose of them.
    /// </remarks>
    [TestFixture]
    public sealed class SerializedFieldSelectionTests : CommonTestBase
    {
        [Test]
        public void AnAssetResolvesToItsOwnTypeExactlyOnce()
        {
            List<Type> types = new();
            DroppedSerializedFieldAsset asset = Track(
                ScriptableObject.CreateInstance<DroppedSerializedFieldAsset>()
            );

            SerializedFieldValidatorMenu.Collect(asset, types);
            CollectionAssert.AreEqual(new[] { typeof(DroppedSerializedFieldAsset) }, types);

            /*
                Selecting a prefab and a component on it is ordinary, so the same type arriving
                twice must not mean constructing it twice.
            */
            SerializedFieldValidatorMenu.Collect(asset, types);
            Assert.AreEqual(1, types.Count);
        }

        [Test]
        public void NothingSelectableIsQuietRatherThanThrown()
        {
            List<Type> types = new();
            DroppedSerializedFieldAsset asset = Track(
                ScriptableObject.CreateInstance<DroppedSerializedFieldAsset>()
            );

            SerializedFieldValidatorMenu.Collect(null, types);
            SerializedFieldValidatorMenu.Collect(asset, null);
            Assert.IsEmpty(types);
        }
    }
}
