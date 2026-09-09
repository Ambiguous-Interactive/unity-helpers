// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto
{
    using System;

    /// <summary>Preserves every generated field number for extension validation across assemblies.</summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public sealed class WProtoDispatchFieldAttribute : Attribute
    {
        /// <summary>The contract owning this field number.</summary>
        public Type Contract { get; }

        /// <summary>The committed field number.</summary>
        public int Tag { get; }

        /// <summary>The named member or subtype holding the number.</summary>
        public string Owner { get; }

        /// <summary>The subtype, or null for an ordinary member.</summary>
        public Type Subtype { get; }

        /// <summary>Records a member or subtype field emitted on a contract.</summary>
        public WProtoDispatchFieldAttribute(Type contract, int tag, string owner, Type subtype)
        {
            Contract = contract;
            Tag = tag;
            Owner = owner;
            Subtype = subtype;
        }
    }
}
