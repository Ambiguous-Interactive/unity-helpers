// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto
{
    using System;

    /// <summary>
    /// Marks a method to run immediately before the annotated type is written.
    /// </summary>
    /// <remarks>
    /// This is where a type projects its live state into the members it serializes -- the point at
    /// which a ring buffer flattens itself into an ordered list, for example.
    /// <para>
    /// The method must take no parameters and return <see langword="void"/>. It may be private:
    /// generated formatters are emitted as a nested type of the contract, so they reach private
    /// members without reflection.
    /// </para>
    /// </remarks>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public sealed class WProtoBeforeSerializationAttribute : Attribute { }
}
