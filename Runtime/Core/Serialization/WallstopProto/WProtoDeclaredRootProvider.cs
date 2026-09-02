// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto
{
    using System;
    using System.Collections.Concurrent;

    /// <summary>
    /// Records which contract answers for a type that has no encoding of its own -- an interface, or
    /// an abstract type carrying no <c>[WProtoContract]</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// There is exactly ONE root per declared type, and this is where it is written down. That is
    /// the property the whole design rests on: a payload does not name the contract that wrote it,
    /// so if two answers were possible for <c>IRandom</c> the reader would have to guess, and a
    /// wrong guess decodes a consumer's save through the wrong chain.
    /// </para>
    /// <para>
    /// <b>Two sources, and the precedence between them is fixed rather than ordered.</b> A
    /// <b>declaration</b> comes from <c>[assembly: WProtoDeclaredRoot]</c> and is registered by
    /// generated code as an assembly loads. A <b>claim</b> comes from
    /// <c>Serializer.RegisterProtobufRoot</c>, a call this program made about itself. A claim always
    /// wins, whichever runs first -- generated registrars all run in the same unordered Unity phase,
    /// so a last-registration-wins rule would make a consumer's override depend on assembly load
    /// order, which is the one thing they cannot control. Releasing a claim restores the
    /// declaration rather than leaving the declared type unserved.
    /// </para>
    /// <para>
    /// Unlike <see cref="WProtoFormatterProvider"/> the declared→root map is keyed by
    /// <see cref="Type"/> rather than by a static generic field, because
    /// <c>Serializer.RegisterProtobufRoot(Type, Type)</c> has only <see cref="Type"/> objects to
    /// offer and closing a generic over them is the <c>MakeGenericType</c> call IL2CPP cannot
    /// compile. The lookup happens once per root serialization of a declared type, never per member.
    /// </para>
    /// <para>
    /// <b>The adapter lives here rather than in <see cref="WProtoFormatterProvider"/>, and that is
    /// load-bearing</b> -- the same reason <see cref="WProtoRootMarshalProvider"/> is separate.
    /// <see cref="WProtoGeneric{T}"/> reads the formatter provider for every member whose type a
    /// closure decides, and it asks no <c>CanServe</c> or <c>CanWrite</c> question. An adapter
    /// registered there made <c>Deque&lt;IRandom&gt;</c> and <c>Box&lt;IRandom&gt;</c> suddenly
    /// encodable -- writing a member as the root contract's message, a shape protobuf-net has no
    /// counterpart for, and silently writing NOTHING for an element outside the root's chain.
    /// Measured, before this registry existed: a two-byte field key and length, no payload, and
    /// success reported. Both shapes declined before and decline again.
    /// </para>
    /// </remarks>
    public static class WProtoDeclaredRootProvider
    {
        private static readonly ConcurrentDictionary<Type, Entry> Roots =
            new ConcurrentDictionary<Type, Entry>();

        /// <summary>
        /// Declares that <typeparamref name="TRoot"/>'s formatter serves
        /// <typeparamref name="TDeclared"/>, and registers the adapter that does it.
        /// </summary>
        /// <typeparam name="TDeclared">The type a value is held as.</typeparam>
        /// <typeparam name="TRoot">The contract whose formatter serves it.</typeparam>
        /// <remarks>
        /// <para>
        /// Called by generated code for every <c>[assembly: WProtoDeclaredRoot]</c> pair, and
        /// callable directly by a consumer whose declared type the generator cannot see. The last
        /// declaration wins, matching <see cref="WProtoFormatterProvider.Register{T}"/>, but a claim
        /// made through <c>Serializer.RegisterProtobufRoot</c> is not overwritten.
        /// </para>
        /// <para>
        /// The adapter is registered even when a claim already names someone else, because the claim
        /// can be released later; it simply declines while the claim stands.
        /// </para>
        /// </remarks>
        public static void Register<TDeclared, TRoot>()
            where TDeclared : class
            where TRoot : TDeclared
        {
            Entry entry = Roots.GetOrAdd(typeof(TDeclared), static _ => new Entry());
            entry.Declared = typeof(TRoot);
            Formatters<TDeclared>.Value = new WProtoDeclaredRootFormatter<TDeclared, TRoot>();
        }

        /// <summary>
        /// Gets the adapter serving <typeparamref name="T"/>, without asking whether it may.
        /// </summary>
        /// <typeparam name="T">The declared type.</typeparam>
        /// <param name="formatter">Receives the adapter, or <c>null</c> when none is declared.</param>
        /// <returns><c>true</c> when one was found.</returns>
        public static bool TryGetFormatter<T>(out IWProtoFormatter<T> formatter)
        {
            formatter = Formatters<T>.Value;
            return formatter != null;
        }

        /// <summary>
        /// Clears the adapter registered for <typeparamref name="T"/>.
        /// </summary>
        /// <typeparam name="T">The declared type.</typeparam>
        /// <remarks>Exists for tests; a declaration is otherwise registered once and kept.</remarks>
        public static void Unregister<T>()
            where T : class
        {
            Formatters<T>.Value = null;
        }

        /// <summary>
        /// Records that <paramref name="root"/> owns <paramref name="declared"/> for this program.
        /// </summary>
        /// <param name="declared">The declared type.</param>
        /// <param name="root">The root type that serves it.</param>
        /// <remarks>
        /// The runtime half of the pair, called by <c>Serializer.RegisterProtobufRoot</c> so the two
        /// registries cannot disagree about who owns a declared type. Ignores <c>null</c> rather
        /// than throwing: it is reached from a public API that validates its own arguments first,
        /// and a serializer registry is not a place to raise a second, differently-worded error.
        /// </remarks>
        public static void Claim(Type declared, Type root)
        {
            if (declared == null || root == null)
            {
                return;
            }

            Roots.GetOrAdd(declared, static _ => new Entry()).Claimed = root;
        }

        /// <summary>
        /// Releases the claim on <paramref name="declared"/>, restoring any declaration.
        /// </summary>
        /// <param name="declared">The declared type.</param>
        public static void ReleaseClaim(Type declared)
        {
            if (declared != null && Roots.TryGetValue(declared, out Entry entry))
            {
                entry.Claimed = null;
            }
        }

        /// <summary>
        /// Releases every claim, restoring the declarations underneath them.
        /// </summary>
        /// <remarks>
        /// The declarations are deliberately kept. They are registered once, by generated code that
        /// runs as an assembly loads, so discarding them would leave every declared type unserved
        /// for the rest of the process with no way to put them back.
        /// </remarks>
        public static void ReleaseAllClaims()
        {
            foreach (Entry entry in Roots.Values)
            {
                entry.Claimed = null;
            }
        }

        /// <summary>
        /// Reports whether <paramref name="root"/> is the root currently serving
        /// <paramref name="declared"/>.
        /// </summary>
        /// <param name="declared">The declared type.</param>
        /// <param name="root">The root type asking.</param>
        /// <returns><c>true</c> when the two are the registered pair.</returns>
        public static bool IsRootFor(Type declared, Type root)
        {
            return TryGetRoot(declared, out Type current) && current == root;
        }

        /// <summary>
        /// Gets the root currently serving <paramref name="declared"/>.
        /// </summary>
        /// <param name="declared">The declared type.</param>
        /// <param name="root">Receives the root, or <c>null</c> when there is none.</param>
        /// <returns><c>true</c> when a root is registered.</returns>
        public static bool TryGetRoot(Type declared, out Type root)
        {
            if (declared != null && Roots.TryGetValue(declared, out Entry entry))
            {
                root = entry.Claimed ?? entry.Declared;
                return root != null;
            }

            root = null;
            return false;
        }

        private static class Formatters<T>
        {
            internal static IWProtoFormatter<T> Value;
        }

        private sealed class Entry
        {
            /*
                Volatile because a claim is a RUNTIME call about a live process --
                Serializer.RegisterProtobufRoot, not a registrar that ran at startup -- so a
                serializing thread must not keep reading the declaration it replaced.
            */
            internal volatile Type Declared;

            internal volatile Type Claimed;
        }
    }
}
