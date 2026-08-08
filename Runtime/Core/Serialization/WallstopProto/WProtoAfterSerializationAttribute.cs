// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto
{
    using System;

    /// <summary>
    /// Marks a method to run immediately after the annotated type has been written.
    /// </summary>
    /// <remarks>
    /// This is where a type releases whatever the before-serialization hook staged, so a pooled
    /// scratch list does not outlive the write that needed it.
    /// <para>
    /// The method must take no parameters and return <see langword="void"/>. It may be private:
    /// generated formatters are emitted as a nested type of the contract, so they reach private
    /// members without reflection.
    /// </para>
    /// </remarks>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public sealed class WProtoAfterSerializationAttribute : Attribute { }
}
