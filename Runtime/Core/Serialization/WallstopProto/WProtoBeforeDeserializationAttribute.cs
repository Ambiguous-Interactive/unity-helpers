// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto
{
    using System;

    /// <summary>
    /// Marks a method to run during deserialization before the after-deserialization hook.
    /// </summary>
    /// <remarks>
    /// On mutable contracts this runs after the instance exists and before decoded members are
    /// assigned. On immutable contracts it runs after construction has assigned the decoded members.
    /// <para>
    /// The method must take no parameters and return <see langword="void"/>. It may be private:
    /// generated formatters are emitted as a nested type of the contract, so they reach private
    /// members without reflection.
    /// </para>
    /// </remarks>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public sealed class WProtoBeforeDeserializationAttribute : Attribute { }
}
