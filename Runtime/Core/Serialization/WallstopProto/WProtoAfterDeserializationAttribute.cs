// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto
{
    using System;

    /// <summary>
    /// Marks a method to run immediately after the annotated type has been read.
    /// </summary>
    /// <remarks>
    /// This is where a type rebuilds whatever it did not serialize -- a dictionary from parallel
    /// key and value arrays, a cached hash, a reconciled capacity. Skipping it does not throw; it
    /// silently yields a half-built object, which is why the generator treats a hook it cannot
    /// reach as an error rather than a warning.
    /// <para>
    /// The method must take no parameters and return <see langword="void"/>. It may be private:
    /// generated formatters are emitted as a nested type of the contract, so they reach private
    /// members without reflection.
    /// </para>
    /// </remarks>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public sealed class WProtoAfterDeserializationAttribute : Attribute { }
}
