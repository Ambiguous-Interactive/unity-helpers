// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto
{
    using System;

    /// <summary>
    /// Names the contract that answers when a value is held as an interface, or as an abstract type
    /// that carries no contract of its own.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A declared type with no members has no encoding. <c>IRandom</c> is the declared type this
    /// package's own documentation recommends, and nothing about the interface says which contract
    /// should read a payload written for it, so without a declaration WallstopProto has to decline
    /// and every such call travels the protobuf-net path -- the one that cannot run under IL2CPP.
    /// This is the missing sentence: for <c>IRandom</c>, the answer is <c>AbstractRandom</c>.
    /// </para>
    /// <para>
    /// <b>It is not a guess and not a new encoding.</b> The bytes are the root contract's, which is
    /// exactly what <c>Serializer.ProtoDeserialize&lt;IRandom&gt;</c> already produces:
    /// <c>ResolveProtobufRootType</c> scans the interface's declaring assembly for a unique abstract
    /// <c>[ProtoContract]</c> base and hands protobuf-net that type. Declaring the pair states the
    /// same answer ahead of time, so the reflection scan is not needed to find it.
    /// </para>
    /// <para>
    /// <b>Unlike <see cref="WProtoRootMarshalAttribute"/>, this applies in every position.</b> A
    /// marshal exists because its types have two encodings chosen by position -- a wrapper at the
    /// root, an ordinary repeated field as a member -- so it lives in a registry the member path
    /// cannot see. A declared root has one encoding: a member typed <c>IRandom</c> and a root typed
    /// <c>IRandom</c> are both the root contract's message, which is what protobuf-net writes for
    /// each. So the adapter is registered in
    /// <see cref="WProtoFormatterProvider"/> like any other formatter.
    /// </para>
    /// <para>
    /// <b>A consumer's explicit registration still wins.</b>
    /// <c>Serializer.RegisterProtobufRoot(declared, root)</c> names a different root for the same
    /// declared type, and it is a runtime call about this program rather than a declaration shipped
    /// in a package, so it takes precedence whichever runs first --
    /// <see cref="WProtoDeclaredRootProvider"/> keeps the two apart rather than letting registration
    /// order decide. The adapter then declines and protobuf-net serves the call exactly as it does
    /// today.
    /// </para>
    /// <example>
    /// <code>
    /// [assembly: WProtoDeclaredRoot(typeof(IRandom), typeof(AbstractRandom))]
    /// </code>
    /// </example>
    /// </remarks>
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
    public sealed class WProtoDeclaredRootAttribute : Attribute
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="WProtoDeclaredRootAttribute"/> class.
        /// </summary>
        /// <param name="declaredType">
        /// The type a value is held as -- an interface, or an abstract type with no contract.
        /// </param>
        /// <param name="rootType">
        /// The <c>[WProtoContract]</c> whose formatter serves it. Must be assignable to
        /// <paramref name="declaredType"/>.
        /// </param>
        public WProtoDeclaredRootAttribute(Type declaredType, Type rootType)
        {
            DeclaredType = declaredType;
            RootType = rootType;
        }

        /// <summary>The type a value is held as.</summary>
        public Type DeclaredType { get; }

        /// <summary>The contract whose formatter serves it.</summary>
        public Type RootType { get; }
    }
}
