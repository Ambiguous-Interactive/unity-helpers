// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Helper
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Helper;

    [TestFixture]
    [Category("Fast")]
    public sealed class ObjectsTypeTraitsTests
    {
        private static IEnumerable<TestCaseData> DeclaredTypeCases()
        {
            yield return TraitCase<bool>("Boolean", true, false, false);
            yield return TraitCase<int>("Integer", true, false, false);
            yield return TraitCase<int?>("NullableInteger", true, false, false);
            yield return TraitCase<DayOfWeek>("Enum", true, false, false);
            yield return TraitCase<DayOfWeek?>("NullableEnum", true, false, false);
            yield return TraitCase<Vector2>("Struct", true, false, false);
            yield return TraitCase<Vector2?>("NullableStruct", true, false, false);
            yield return TraitCase<object>("SystemObject", false, true, false);
            yield return TraitCase<string>("String", false, false, false);
            yield return TraitCase<IComparable>("Interface", false, false, false);
            yield return TraitCase<ValueType>("ValueTypeBase", false, false, false);
            yield return TraitCase<Enum>("EnumBase", false, false, false);
            yield return TraitCase<Action>("Delegate", false, false, false);
            yield return TraitCase<int[]>("ValueArray", false, false, false);
            yield return TraitCase<object[]>("ObjectArray", false, false, false);
            yield return TraitCase<GameObject[]>("UnityObjectArray", false, false, false);
            yield return TraitCase<List<GameObject>>("UnityObjectList", false, false, false);
            yield return TraitCase<UnityEngine.Object>("UnityObject", false, false, true);
            yield return TraitCase<GameObject>("GameObject", false, false, true);
            yield return TraitCase<Component>("Component", false, false, true);
            yield return TraitCase<Transform>("Transform", false, false, true);
            yield return TraitCase<MonoBehaviour>("MonoBehaviour", false, false, true);
            yield return TraitCase<ScriptableObject>("ScriptableObject", false, false, true);
            yield return TraitCase<Texture2D>("Texture", false, false, true);
        }

        private static TestCaseData TraitCase<T>(
            string caseName,
            bool expectedValueType,
            bool expectedObjectType,
            bool expectedUnityObject
        )
        {
            Func<(bool IsValueType, bool IsObjectType, bool IsUnityObject)> readTraits =
                ReadTraits<T>;
            return new TestCaseData(
                readTraits,
                expectedValueType,
                expectedObjectType,
                expectedUnityObject
            ).SetName($"{nameof(ObjectsTypeTraitsTests)}.{caseName}.ClassifiesDeclaredType");
        }

        private static ValueTuple<bool, bool, bool> ReadTraits<T>()
        {
            return (
                ObjectsTypeTraits<T>.IsValueType,
                ObjectsTypeTraits<T>.IsObjectType,
                ObjectsTypeTraits<T>.IsUnityObject
            );
        }

        [TestCaseSource(nameof(DeclaredTypeCases))]
        public void ClassifiesDeclaredType(
            Func<(bool IsValueType, bool IsObjectType, bool IsUnityObject)> readTraits,
            bool expectedValueType,
            bool expectedObjectType,
            bool expectedUnityObject
        )
        {
            (bool IsValueType, bool IsObjectType, bool IsUnityObject) actual = readTraits();

            Assert.AreEqual(
                expectedValueType,
                actual.IsValueType,
                nameof(ObjectsTypeTraits<object>.IsValueType)
            );
            Assert.AreEqual(
                expectedObjectType,
                actual.IsObjectType,
                nameof(ObjectsTypeTraits<object>.IsObjectType)
            );
            Assert.AreEqual(
                expectedUnityObject,
                actual.IsUnityObject,
                nameof(ObjectsTypeTraits<object>.IsUnityObject)
            );
            Assert.AreEqual(
                actual,
                readTraits(),
                "Repeated reads retain the same classifications."
            );
        }
    }
}
