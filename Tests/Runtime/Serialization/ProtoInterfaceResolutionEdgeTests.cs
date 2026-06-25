// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using NUnit.Framework;
    using ProtoBuf;
    using WallstopStudios.UnityHelpers.Core.Serialization;
    using Serializer = WallstopStudios.UnityHelpers.Core.Serialization.Serializer;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class ProtoInterfaceResolutionEdgeTests
    {
        public interface IWidget { }

        [ProtoContract]
        private sealed class Widget : IWidget
        {
            [ProtoMember(1)]
            public int Id { get; set; }

            [ProtoMember(2)]
            public string Label { get; set; }
        }

        [Test]
        public void SingleImplementationRequiresRegistration()
        {
            IWidget original = new Widget { Id = 3, Label = "ok" };
            byte[] data = Serializer.ProtoSerialize(original);

            Assert.Throws<SerializationTypeException>(
                () => Serializer.ProtoDeserialize<IWidget>(data),
                "Deserializing interface even with a single implementation should require registration"
            );

            // After registration, it should succeed
            Serializer.RegisterProtobufRoot<IWidget, Widget>();
            IWidget round = Serializer.ProtoDeserialize<IWidget>(data);
            Assert.IsTrue(round != null, "Deserialized instance should not be null");
            Assert.IsInstanceOf<Widget>(round);
            Widget w = (Widget)round;
            Assert.AreEqual(3, w.Id);
            Assert.AreEqual("ok", w.Label);
        }

        [ProtoContract]
        private abstract class AbstractBase
        {
            [ProtoMember(1)]
            public int Common { get; set; }
        }

        [ProtoContract]
        private sealed class DerivedA : AbstractBase
        {
            [ProtoMember(2)]
            public string ExtraA { get; set; }
        }

        [ProtoContract]
        private sealed class DerivedB : AbstractBase
        {
            [ProtoMember(2)]
            public string ExtraB { get; set; }
        }

        [Test]
        public void AbstractBaseWithoutRegistrationThrows()
        {
            AbstractBase original = new DerivedA { Common = 9, ExtraA = "x" };
            byte[] data = Serializer.ProtoSerialize(original, forceRuntimeType: true);

            Assert.Throws<SerializationTypeException>(
                () => Serializer.ProtoDeserialize<AbstractBase>(data),
                "Deserializing abstract base with multiple derived types should require registration"
            );
        }

        [ProtoContract]
        private abstract class RegisteredAbstractBase
        {
            [ProtoMember(1)]
            public int Common { get; set; }
        }

        [ProtoContract]
        private sealed class RegisteredDerived : RegisteredAbstractBase
        {
            [ProtoMember(2)]
            public string Extra { get; set; }
        }

        [Test]
        public void AbstractBaseWithRegisteredRootDeserializes()
        {
            RegisteredAbstractBase original = new RegisteredDerived { Common = 12, Extra = "root" };
            byte[] data = Serializer.ProtoSerialize(original, forceRuntimeType: true);

            Serializer.RegisterProtobufRoot<RegisteredAbstractBase, RegisteredDerived>();

            RegisteredAbstractBase round = Serializer.ProtoDeserialize<RegisteredAbstractBase>(
                data
            );

            Assert.IsInstanceOf<RegisteredDerived>(round);
            RegisteredDerived derived = (RegisteredDerived)round;
            Assert.AreEqual(12, derived.Common);
            Assert.AreEqual("root", derived.Extra);
        }
    }
}
