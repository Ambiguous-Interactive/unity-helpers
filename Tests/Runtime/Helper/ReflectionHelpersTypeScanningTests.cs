// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Helper
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.Core.TestTypes;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class ReflectionHelpersTypeScanningTests : CommonTestBase
    {
        private static IEnumerable<TestCaseData> GetMovedGenericCases()
        {
            yield return new TestCaseData(typeof(List<SerializableType>)).SetName(
                "MovedGeneric.List.Recovers"
            );
            yield return new TestCaseData(
                typeof(Dictionary<string, List<SerializableType[]>>)
            ).SetName("MovedGeneric.NestedArray.Recovers");
            yield return new TestCaseData(typeof(List<SerializableType>[])).SetName(
                "MovedGeneric.OuterArray.Recovers"
            );
        }

        [Test]
        public void GetAllLoadedTypesIncludesTestAssemblyType()
        {
            IEnumerable<Type> types = ReflectionHelpers.GetAllLoadedTypes();
            bool found = types.Any(t => t == typeof(ReflectionHelpersTypeScanningTests));
            Assert.IsTrue(found, "Expected test type to be present in loaded types.");
        }

        [Test]
        public void GetTypesDerivedFromComponentIncludesTester()
        {
            IEnumerable<Type> types = ReflectionHelpers.GetTypesDerivedFrom<Component>(
                includeAbstract: false
            );
            bool found = types.Any(t => t == typeof(PrewarmTesterComponent));
            Assert.IsTrue(found, "Expected PrewarmTesterComponent to be found as a Component.");
        }

        [Test]
        public void TryResolveTypeFindsAssemblyQualifiedName()
        {
            string aqn = typeof(PrewarmTesterComponent).AssemblyQualifiedName;
            Type t = ReflectionHelpers.TryResolveType(aqn);
            Assert.IsTrue(t != null, "Resolution by assembly qualified name returned null.");
            Assert.AreEqual(typeof(PrewarmTesterComponent), t);
        }

        [Test]
        public void TryResolveTypeFindsNonQualifiedName()
        {
            string fullName = typeof(PrewarmTesterComponent).FullName;
            Type t = ReflectionHelpers.TryResolveType(fullName);
            Assert.IsTrue(t != null, "Resolution by full name returned null.");
            Assert.AreEqual(typeof(PrewarmTesterComponent), t);
        }

        [Test]
        [TestCase(typeof(int), TestName = "TypeName.Int32.PreservesRuntimeName")]
        [TestCase(typeof(List<>), TestName = "TypeName.OpenGeneric.PreservesRuntimeName")]
        [TestCase(typeof(List<int>), TestName = "TypeName.ClosedGeneric.PreservesRuntimeName")]
        public void AssemblyQualifiedNameMatchesRuntimeForCommonTypes(Type type)
        {
            Assert.AreEqual(
                type.AssemblyQualifiedName,
                ReflectionHelpers.GetAssemblyQualifiedName(type)
            );
        }

        [Test]
        public void AssemblyQualifiedNameReturnsNullForNullType()
        {
            Assert.IsTrue(ReflectionHelpers.GetAssemblyQualifiedName(null) == null);
        }

        [Test]
        [SkipUnderIL2CPP("The non-SZ array runtime-name oracle requires Mono reflection.")]
        public void AssemblyQualifiedNamePreservesNonZeroBoundArrayArgument()
        {
            Type array = typeof(SerializableType).MakeArrayType(1);
            Type expected = typeof(List<>).MakeGenericType(array);
            string runtimeName = expected.AssemblyQualifiedName;

            Assert.AreSame(expected, Type.GetType(runtimeName, throwOnError: false));
            Assert.AreEqual(runtimeName, AssemblyQualifiedTypeNameBuilder.Build(expected));
            Assert.AreEqual(runtimeName, ReflectionHelpers.GetAssemblyQualifiedName(expected));
        }

        [Test]
        [TestCaseSource(nameof(GetMovedGenericCases))]
        public void TryResolveTypeRecoversMovedGenericArgument(Type expected)
        {
            string original = AssemblyQualifiedTypeNameBuilder.Build(expected);
            Assert.AreSame(expected, Type.GetType(original, throwOnError: false));
            Assert.AreEqual(original, ReflectionHelpers.GetAssemblyQualifiedName(expected));
            string moved = original.Replace(
                typeof(SerializableType).Assembly.FullName,
                "MissingAssembly"
            );
            Assert.IsTrue(Type.GetType(moved, throwOnError: false) == null);

            int cachedNamesBefore = ReflectionHelpers.ResolvedTypeCacheCountForTesting;
            Assert.AreSame(expected, ReflectionHelpers.TryResolveType(moved));
            Assert.AreSame(expected, ReflectionHelpers.TryResolveType(moved));
            Assert.AreEqual(cachedNamesBefore, ReflectionHelpers.ResolvedTypeCacheCountForTesting);
        }

        [Test]
        public void TryResolveTypeRejectsMissingGenericArgument()
        {
            string original = AssemblyQualifiedTypeNameBuilder.Build(
                typeof(List<SerializableType>)
            );
            string missing = original.Replace(typeof(SerializableType).FullName, "Missing.Type");
            string malformed = original.Replace("]]", "]");

            Assert.IsTrue(ReflectionHelpers.TryResolveType(missing) == null);
            Assert.IsTrue(ReflectionHelpers.TryResolveType(malformed) == null);
        }

        /// <summary>
        /// A type name reaching <see cref="ReflectionHelpers.TryResolveType"/> is routinely payload
        /// data -- a serialized <c>Type</c> field naming a class a later build renamed -- so caching
        /// the failures grew the cache without bound on strings the process can never use
        /// (<see href="https://github.com/Ambiguous-Interactive/unity-helpers/issues/643">#643</see>).
        /// </summary>
        [Test]
        public void UnresolvableTypeNamesAreNotCached()
        {
            int before = ReflectionHelpers.ResolvedTypeCacheCountForTesting;

            for (int index = 0; index < 256; ++index)
            {
                Assert.IsTrue(
                    ReflectionHelpers.TryResolveType($"Missing.Type{index}, MissingAssembly")
                        == null
                );
            }

            Assert.AreEqual(
                before,
                ReflectionHelpers.ResolvedTypeCacheCountForTesting,
                "A name no loaded assembly declares must leave no entry behind."
            );
        }

        /// <summary>
        /// The same change also means a name that failed before an assembly was loaded is retried
        /// rather than answered from a stale failure.
        /// </summary>
        [Test]
        public void ASuccessfulResolutionIsStillCached()
        {
            string assemblyQualifiedName = typeof(PrewarmTesterComponent).AssemblyQualifiedName;
            Assert.IsTrue(ReflectionHelpers.TryResolveType(assemblyQualifiedName) != null);

            int afterFirst = ReflectionHelpers.ResolvedTypeCacheCountForTesting;
            Assert.IsTrue(ReflectionHelpers.TryResolveType(assemblyQualifiedName) != null);

            Assert.AreEqual(
                afterFirst,
                ReflectionHelpers.ResolvedTypeCacheCountForTesting,
                "A repeated successful resolution must be served from the cache."
            );
        }

        [Test]
        public void GetTypesFromAssemblyNullReturnsEmpty()
        {
            Type[] types = ReflectionHelpers.GetTypesFromAssembly(null);
            Assert.IsTrue(
                types != null,
                "GetTypesFromAssembly should return non-null array for null input"
            );
            Assert.AreEqual(0, types.Length, "Expected empty array for null assembly.");
        }
    }
}
