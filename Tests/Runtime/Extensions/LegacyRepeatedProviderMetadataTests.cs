// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Extensions
{
#if !WALLSTOP_PROTO_ONLY
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Reflection;
    using NUnit.Framework;
    using ProtoBuf.Meta;
    using ProtoBuf.Serializers;
    using WallstopStudios.UnityHelpers.Core.DataStructure;

    [TestFixture]
    [Category("Fast")]
    [Category("Serialization")]
    public sealed class LegacyRepeatedProviderMetadataTests
    {
        [TestCase(typeof(int[]))]
        [TestCase(typeof(string[]))]
        [TestCase(typeof(int?[]))]
        public void VectorIdentitySurvivesElementTypeReconstruction(Type vectorType)
        {
            Type elementType = vectorType.GetElementType();
            Assert.IsTrue(elementType != null);
            Type reconstructed = elementType.MakeArrayType();
            TestContext.WriteLine(
                $"Vector={vectorType}; Element={elementType}; Reconstructed={reconstructed}; Equal={vectorType == reconstructed}"
            );
            Assert.AreEqual(1, vectorType.GetArrayRank());
            Assert.AreEqual(vectorType, reconstructed);
        }

        [TestCase(nameof(RepeatedSerializer.CreateVector), 1)]
        [TestCase(nameof(RepeatedSerializer.CreateList), 1)]
        [TestCase(nameof(RepeatedSerializer.CreateList), 2)]
        public void RepeatedFactoryMetadataRetainsGenericDefinitions(string name, int arity)
        {
            MethodInfo[] methods = typeof(RepeatedSerializer).GetMethods(
                BindingFlags.Static | BindingFlags.Public
            );
            bool found = false;
            foreach (MethodInfo method in methods)
            {
                if (!string.Equals(method.Name, name, StringComparison.Ordinal))
                {
                    continue;
                }
                int argumentCount = method.GetGenericArguments().Length;
                TestContext.WriteLine(
                    $"Factory={method}; Generic={method.IsGenericMethod}; Definition={method.IsGenericMethodDefinition}; Arity={argumentCount}"
                );
                if (argumentCount == arity && method.IsGenericMethodDefinition)
                {
                    found = true;
                }
            }
            Assert.IsTrue(found, $"Missing reflected generic factory {name} with arity {arity}.");
        }

        [TestCase(typeof(Deque<int>), 5, 1)]
        [TestCase(typeof(CyclicBuffer<int>), 4, 3)]
        public void RuntimeModelDiscoversCollectionCallbacksAndItemMember(
            Type collectionType,
            int memberCount,
            int itemField
        )
        {
            RuntimeTypeModel model = RuntimeTypeModel.Create();
            model.AutoCompile = false;
            MetaType metadata = model.Add(collectionType, true);
            ValueMember[] members = metadata.GetFields();
            TestContext.WriteLine(
                $"Contract={collectionType}; Members={members.Length}; Before={metadata.Callbacks.BeforeSerialize != null}; After={metadata.Callbacks.AfterSerialize != null}"
            );
            bool itemMemberFound = false;
            foreach (ValueMember member in members)
            {
                TestContext.WriteLine($"Field={member.FieldNumber}; Type={member.MemberType}");
                if (member.FieldNumber == itemField && member.MemberType == typeof(List<int>))
                {
                    itemMemberFound = true;
                }
            }
            Assert.AreEqual(memberCount, members.Length);
            Assert.IsTrue(metadata.Callbacks.BeforeSerialize != null);
            Assert.IsTrue(metadata.Callbacks.AfterSerialize != null);
            Assert.IsTrue(itemMemberFound);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ReflectionSerializerRetainsCollectionItemsAndFieldFraming(bool cyclic)
        {
            RuntimeTypeModel model = RuntimeTypeModel.Create();
            model.AutoCompile = false;
            object value = cyclic
                ? new CyclicBuffer<int>(2, new[] { 42 })
                : new Deque<int>(new[] { 42 });
            byte[] expected = cyclic
                ? new byte[] { 8, 2, 16, 1, 24, 42, 32, 1 }
                : new byte[] { 8, 42, 24, 1, 32, 1, 40, 16 };
            using MemoryStream destination = new();
            model.Serialize(destination, value);
            byte[] bytes = destination.ToArray();
            TestContext.WriteLine(
                $"Contract={value.GetType()}; Bytes={BitConverter.ToString(bytes)}"
            );
            CollectionAssert.AreEqual(expected, bytes);
        }
    }
#endif
}
