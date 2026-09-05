// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto
{
    using System;

    /// <summary>Identifies a generated replacement owner for compilation and player build arbitration.</summary>
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public sealed class WProtoReplacementAttribute : Attribute
    {
        /// <summary>Records the contract this assembly replaces.</summary>
        public WProtoReplacementAttribute(Type contract)
        {
            Contract = contract;
        }

        /// <summary>The contract whose complete subtype chain this assembly supplies.</summary>
        public Type Contract { get; }
    }
}
