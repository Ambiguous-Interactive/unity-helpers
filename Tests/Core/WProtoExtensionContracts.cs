// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core
{
    using System.Collections.Generic;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    [WProtoContract]
    [WProtoInclude(100, typeof(WProtoExtensionKnown))]
    public partial class WProtoExtensionBase
    {
        public int Damage
        {
            get => damage;
            set => damage = value;
        }

        [WProtoMember(1)]
        private int damage;
    }

    [WProtoContract]
    public sealed partial class WProtoExtensionKnown : WProtoExtensionBase
    {
        [WProtoMember(1)]
        public int Sharpness;
    }

    [WProtoContract]
    public sealed partial class WProtoExtensionHolder
    {
        [WProtoMember(1)]
        public WProtoExtensionBase Selected;

        [WProtoMember(2)]
        public List<WProtoExtensionBase> Weapons;
    }

    [WProtoContract]
    public partial class WProtoExtensionSeedBase
    {
        [WProtoMember(1)]
        public int First;

        [WProtoMember(2)]
        public int Second;
    }

    [WProtoContract]
    public sealed partial class WProtoExtensionSeedHolder
    {
        [WProtoMember(1)]
        public WProtoExtensionSeedBase Value = new WProtoExtensionSeedBase { First = 9 };
    }

    [WProtoContract]
    public partial class WProtoClassificationBase { }
}
