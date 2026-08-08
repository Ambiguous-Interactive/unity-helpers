// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto
{
    using System;

    /// <summary>
    /// Marks a method to run immediately before the annotated type is read into.
    /// </summary>
    /// <remarks>
    /// This runs after the instance exists and before any member is assigned, which is the only
    /// point at which state left over from a previous life can be cleared.
    /// <para>
    /// The method must take no parameters and return <see langword="void"/>. It may be private:
    /// generated formatters are emitted as a nested type of the contract, so they reach private
    /// members without reflection.
    /// </para>
    /// </remarks>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public sealed class WProtoBeforeDeserializationAttribute : Attribute { }
}
