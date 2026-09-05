// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System.Collections.Generic;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [Category("Fast")]
    [Category("Serialization")]
    public sealed class WProtoCrossAssemblyTests
    {
        [Test]
        public void AnExtendingAssemblyRoundTripsThroughPrecompiledMembersAndCollections()
        {
            Assert.AreNotEqual(
                typeof(WProtoExtensionBase).Assembly,
                typeof(WProtoExtensionPlasma).Assembly
            );
            WProtoExtensionHolder value = new WProtoExtensionHolder
            {
                Selected = new WProtoExtensionPlasma { Damage = 17, Charge = 29 },
                Weapons = new List<WProtoExtensionBase>
                {
                    new WProtoExtensionPlasma { Damage = 31, Charge = 43 },
                    new WProtoExtensionKnown { Damage = 47, Sharpness = 53 },
                },
            };
            Assert.IsTrue(WProtoFacade.TrySerialize(value, out byte[] bytes));
            Assert.IsTrue(WProtoFacade.TryDeserialize(bytes, out WProtoExtensionHolder read));
            Assert.IsTrue(read != null);
            Assert.IsInstanceOf<WProtoExtensionPlasma>(read.Selected);
            Assert.AreEqual(17, read.Selected.Damage);
            Assert.AreEqual(29, ((WProtoExtensionPlasma)read.Selected).Charge);
            Assert.AreEqual(2, read.Weapons.Count);
            Assert.IsInstanceOf<WProtoExtensionPlasma>(read.Weapons[0]);
            Assert.AreEqual(31, read.Weapons[0].Damage);
            Assert.AreEqual(43, ((WProtoExtensionPlasma)read.Weapons[0]).Charge);
            Assert.IsInstanceOf<WProtoExtensionKnown>(read.Weapons[1]);
            Assert.AreEqual(47, read.Weapons[1].Damage);
            Assert.AreEqual(53, ((WProtoExtensionKnown)read.Weapons[1]).Sharpness);
        }

        [Test]
        public void ExtendingAPreviouslyMergeableBasePreservesItsConstructorSeed()
        {
            byte[] payload = { 0x0A, 0x02, 0x10, 0x02 };
            Assert.IsTrue(WProtoFacade.TryDeserialize(payload, out WProtoExtensionSeedHolder read));
            Assert.IsTrue(read != null);
            Assert.IsTrue(read.Value != null);
            Assert.AreEqual(9, read.Value.First);
            Assert.AreEqual(2, read.Value.Second);
        }

        [Test]
        public void SeededBasePromotionKeepsInheritedMembersAndConsumerFields()
        {
            byte[] payload = { 0x0A, 0x07, 0xC2, 0x0C, 0x02, 0x08, 0x03, 0x10, 0x02 };
            Assert.IsTrue(WProtoFacade.TryDeserialize(payload, out WProtoExtensionSeedHolder read));
            Assert.IsTrue(read != null);
            Assert.IsInstanceOf<WProtoExtensionSeedLeaf>(read.Value);
            Assert.AreEqual(9, read.Value.First);
            Assert.AreEqual(2, read.Value.Second);
            Assert.AreEqual(3, ((WProtoExtensionSeedLeaf)read.Value).Extra);
        }

        [Test]
        public void AConcreteSubtypeEntryPointUsesTheReplacementRootChain()
        {
            WProtoExtensionPlasma value = new WProtoExtensionPlasma { Damage = 17, Charge = 29 };
            Assert.IsTrue(WProtoFacade.TrySerialize(value, out byte[] bytes));
            Assert.IsTrue(WProtoFacade.TryDeserialize(bytes, out WProtoExtensionPlasma read));
            Assert.IsTrue(read != null);
            Assert.AreEqual(17, read.Damage);
            Assert.AreEqual(29, read.Charge);
        }
    }

    [WProtoContract]
    [WProtoSubtype(typeof(WProtoExtensionBase), 200)]
    public sealed partial class WProtoExtensionPlasma : WProtoExtensionBase
    {
        [WProtoMember(1)]
        public int Charge;
    }

    [WProtoContract]
    [WProtoSubtype(typeof(WProtoExtensionSeedBase), 200)]
    public sealed partial class WProtoExtensionSeedLeaf : WProtoExtensionSeedBase
    {
        [WProtoMember(1)]
        public int Extra;
    }
}
