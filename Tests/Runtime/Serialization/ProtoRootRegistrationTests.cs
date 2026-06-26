// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System;
    using NUnit.Framework;
    using ProtoBuf;
    using WallstopStudios.UnityHelpers.Core.Serialization;
    using Serializer = WallstopStudios.UnityHelpers.Core.Serialization.Serializer;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class ProtoRootRegistrationTests
    {
        public interface IAnimal { }

        [ProtoContract]
        private sealed class Dog : IAnimal
        {
            [ProtoMember(1)]
            public int Age { get; set; }

            [ProtoMember(2)]
            public string Name { get; set; }
        }

        [ProtoContract]
        private sealed class Cat : IAnimal
        {
            [ProtoMember(1)]
            public int Lives { get; set; }

            [ProtoMember(2)]
            public string Color { get; set; }
        }

        private sealed class NoContractAnimal : IAnimal { }

        public interface IInferredAnimal { }

        [ProtoContract]
        [ProtoInclude(100, typeof(InferredDog))]
        private abstract class InferredAnimalBase : IInferredAnimal
        {
            [ProtoMember(1)]
            public int Age { get; set; }
        }

        [ProtoContract]
        private sealed class InferredDog : InferredAnimalBase
        {
            [ProtoMember(2)]
            public string Name { get; set; }
        }

        [ProtoContract]
        private sealed class ExplicitBird : IInferredAnimal
        {
            [ProtoMember(1)]
            public int Wings { get; set; }
        }

        public interface IPlainAbstractAnimal { }

        [ProtoContract]
        private abstract class PlainAbstractAnimalBase : IPlainAbstractAnimal
        {
            [ProtoMember(1)]
            public int Age { get; set; }
        }

        [ProtoContract]
        private sealed class ExplicitPlainAnimal : IPlainAbstractAnimal
        {
            [ProtoMember(1)]
            public int Age { get; set; }
        }

        [SetUp]
        public void SetUp()
        {
            Serializer.ClearProtobufRootCacheForTesting(
                typeof(IAnimal),
                typeof(IInferredAnimal),
                typeof(IPlainAbstractAnimal)
            );
        }

        [Test]
        public void MultipleImplementationsRequireRegistration()
        {
            IAnimal original = new Dog { Age = 5, Name = "Rex" };

            byte[] data = Serializer.ProtoSerialize(original);
            Serializer.RegisterProtobufRoot<IAnimal, Dog>();

            IAnimal round = Serializer.ProtoDeserialize<IAnimal>(data);

            Assert.IsTrue(round != null, "Deserialized instance should not be null");
            Assert.IsInstanceOf<Dog>(round, "Expected registered root type to be used");
            Dog dog = (Dog)round;
            Assert.AreEqual(5, dog.Age, "Age should match");
            Assert.AreEqual("Rex", dog.Name, "Name should match");
        }

        [Test]
        public void RegisteringInvalidRootMissingContractThrows()
        {
            Assert.Throws<ArgumentException>(
                () => Serializer.RegisterProtobufRoot(typeof(IAnimal), typeof(NoContractAnimal)),
                "Missing [ProtoContract] should throw"
            );
        }

        [Test]
        public void RegisteringIncompatibleRootThrows()
        {
            Assert.Throws<ArgumentException>(
                () => Serializer.RegisterProtobufRoot(typeof(IAnimal), typeof(string)),
                "Incompatible root should throw"
            );
        }

        [Test]
        public void ConflictingRegistrationThrows()
        {
            Serializer.RegisterProtobufRoot<IAnimal, Dog>();
            Assert.Throws<InvalidOperationException>(
                () => Serializer.RegisterProtobufRoot<IAnimal, Cat>(),
                "Conflicting root registration should throw"
            );
        }

        [Test]
        public void ExplicitRegistrationOverridesPreviouslyInferredAbstractInterfaceRoot()
        {
            Assert.Catch<SerializationFailureException>(
                () => Serializer.ProtoDeserialize<IInferredAnimal>(new byte[] { 8, 1 }),
                "The first deserialize should infer and cache the abstract root before decode fails."
            );

            Assert.DoesNotThrow(() =>
                Serializer.RegisterProtobufRoot<IInferredAnimal, ExplicitBird>()
            );

            IInferredAnimal original = new ExplicitBird { Wings = 2 };
            byte[] data = Serializer.ProtoSerialize(original);
            IInferredAnimal round = Serializer.ProtoDeserialize<IInferredAnimal>(data);

            Assert.IsInstanceOf<ExplicitBird>(round);
            ExplicitBird bird = (ExplicitBird)round;
            Assert.AreEqual(2, bird.Wings);
        }

        [Test]
        public void InterfaceResolutionIgnoresAbstractBaseWithoutProtoIncludes()
        {
            Assert.Throws<SerializationTypeException>(
                () => Serializer.ProtoDeserialize<IPlainAbstractAnimal>(new byte[] { 8, 1 }),
                "An abstract root without ProtoInclude declarations should not be inferred."
            );

            Assert.DoesNotThrow(() =>
                Serializer.RegisterProtobufRoot<IPlainAbstractAnimal, ExplicitPlainAnimal>()
            );
        }
    }
}
