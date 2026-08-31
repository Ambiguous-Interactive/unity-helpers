// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.CustomDrawers.TestTypes
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;

    /// <summary>
    /// Test asset whose fields are the package's serializable stand-ins for a standard-library
    /// value, each fed options of the type it stands in for. Their <c>Equals(object)</c> refuses the
    /// authored type, so only the drawer's own conversion can match them.
    /// </summary>
    [Serializable]
    internal sealed class WValueDropDownConvertibleOptionAsset : ScriptableObject
    {
        [WValueDropDown(
            typeof(WValueDropDownConvertibleOptionSource),
            nameof(WValueDropDownConvertibleOptionSource.GetGuidOptions),
            typeof(Guid)
        )]
        public WGuid selectedGuid;

        [WValueDropDown(
            typeof(WValueDropDownConvertibleOptionSource),
            nameof(WValueDropDownConvertibleOptionSource.GetIntOptions),
            typeof(int)
        )]
        public SerializableNullable<int> selectedNullable;

        [WValueDropDown(
            typeof(WValueDropDownConvertibleOptionSource),
            nameof(WValueDropDownConvertibleOptionSource.GetPairOptions),
            typeof(ValueTuple<int, float>)
        )]
        public SerializableValueTuple<int, float> selectedPair;

        [WValueDropDown(
            typeof(WValueDropDownConvertibleOptionSource),
            nameof(WValueDropDownConvertibleOptionSource.GetTripleOptions),
            typeof(ValueTuple<int, float, string>)
        )]
        public SerializableValueTuple<int, float, string> selectedTriple;
    }

    /// <summary>
    /// Supplies the authored options for <see cref="WValueDropDownConvertibleOptionAsset"/>, each of
    /// the standard-library type the matching field wraps.
    /// </summary>
    internal static class WValueDropDownConvertibleOptionSource
    {
        /// <summary>First authored GUID option.</summary>
        public static readonly Guid FirstGuid = new("6f9619ff-8b86-4d11-b42d-00c04fc964ff");

        /// <summary>Second authored GUID option, the one the fixtures select.</summary>
        public static readonly Guid SecondGuid = new("2f9619ff-8b86-4d11-b42d-00c04fc964ff");

        /// <summary>Returns the authored <see cref="Guid"/> options.</summary>
        /// <returns>Two version-four GUIDs.</returns>
        public static IEnumerable<Guid> GetGuidOptions()
        {
            yield return FirstGuid;
            yield return SecondGuid;
        }

        /// <summary>Returns the authored <see cref="int"/> options.</summary>
        /// <returns>Three integers.</returns>
        public static IEnumerable<int> GetIntOptions()
        {
            yield return 3;
            yield return 5;
            yield return 7;
        }

        /// <summary>Returns the authored two-component tuple options.</summary>
        /// <returns>Two value tuples.</returns>
        public static IEnumerable<ValueTuple<int, float>> GetPairOptions()
        {
            yield return (1, 0.5f);
            yield return (7, 1.5f);
        }

        /// <summary>Returns the authored three-component tuple options.</summary>
        /// <returns>Two value tuples.</returns>
        public static IEnumerable<ValueTuple<int, float, string>> GetTripleOptions()
        {
            yield return (1, 0.5f, "scrap");
            yield return (7, 1.5f, "loot");
        }
    }
#endif
}
