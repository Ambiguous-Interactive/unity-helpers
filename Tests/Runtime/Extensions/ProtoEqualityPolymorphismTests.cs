// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Extensions
{
    using System.Collections.Generic;
    using NUnit.Framework;
    using ProtoBuf;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class ProtoEqualityPolymorphismTests : CommonTestBase
    {
        [Test]
        public void ProtoEqualsAbstractBaseWithSameDerivedReturnsTrue()
        {
            AnimalBase a = new Dog { Name = "Rex", Age = 3 };
            AnimalBase b = new Dog { Name = "Rex", Age = 3 };

            bool equal = a.ProtoEquals(b);
            Assert.IsTrue(
                equal,
                "ProtoEquals should treat same derived instances as equal via abstract base"
            );
        }

        [Test]
        public void ProtoEqualsInterfaceWithSameDerivedReturnsTrue()
        {
            IAnimal a = new Dog { Name = "Luna", Age = 1 };
            IAnimal b = new Dog { Name = "Luna", Age = 1 };

            bool equal = a.ProtoEquals(b);
            Assert.IsTrue(
                equal,
                "ProtoEquals should treat same derived instances as equal via interface"
            );
        }

        [Test]
        public void ProtoEqualsAbstractBaseWithDifferentDerivedReturnsFalse()
        {
            AnimalBase a = new Dog { Name = "Milo", Age = 4 };
            AnimalBase b = new Cat { Name = "Milo", Lives = 9 };

            bool equal = a.ProtoEquals(b);
            Assert.IsFalse(
                equal,
                "ProtoEquals should detect different derived types under same base"
            );
        }

        [Test]
        public void ProtoComparerWorksForAbstractBaseInCollections()
        {
            IEqualityComparer<AnimalBase> comparer =
                ProtoEqualityExtensions.GetProtoComparer<AnimalBase>();
            HashSet<AnimalBase> set = new(comparer)
            {
                new Dog { Name = "Buddy", Age = 2 },
            };
            bool addedDuplicate = set.Add(new Dog { Name = "Buddy", Age = 2 });
            bool addedDifferent = set.Add(new Dog { Name = "Buddy", Age = 3 });

            Assert.IsFalse(
                addedDuplicate,
                "HashSet should reject equivalent derived object via proto comparer"
            );
            Assert.IsTrue(
                addedDifferent,
                "HashSet should accept different derived object via proto comparer"
            );
        }

        public interface IAnimal
        {
            string Name { get; set; }
        }

        [ProtoContract]
        [ProtoInclude(100, typeof(Dog))]
        [ProtoInclude(200, typeof(Cat))]
        public abstract class AnimalBase : IAnimal
        {
            [ProtoMember(1)]
            public string Name { get; set; }
        }

        [ProtoContract]
        public sealed class Dog : AnimalBase
        {
            [ProtoMember(1)]
            public int Age { get; set; }
        }

        [ProtoContract]
        public sealed class Cat : AnimalBase
        {
            [ProtoMember(1)]
            public int Lives { get; set; }
        }
    }
}
