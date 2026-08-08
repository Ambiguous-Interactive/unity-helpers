// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto
{
    using System;

    /// <summary>
    /// Marks a type as a WallstopProto message, so a formatter is generated for it.
    /// </summary>
    [AttributeUsage(
        AttributeTargets.Class | AttributeTargets.Struct,
        AllowMultiple = false,
        Inherited = false
    )]
    public sealed class WProtoContractAttribute : Attribute
    {
        /// <summary>
        /// The schema name for this message. Defaults to the type's own name when unset.
        /// </summary>
        /// <remarks>Never written to the wire; it names the message in schemas and diagnostics.</remarks>
        public string Name { get; set; }

        /// <summary>
        /// Indicates whether an instance is created without running any constructor.
        /// </summary>
        public bool SkipConstructor { get; set; }

        /// <summary>
        /// Indicates whether a type that implements an enumerable interface is nonetheless written
        /// as a message rather than as a repeated field.
        /// </summary>
        public bool IgnoreListHandling { get; set; }
    }
}
