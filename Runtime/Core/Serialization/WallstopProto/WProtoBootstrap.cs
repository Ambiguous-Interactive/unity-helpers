// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto
{
    using UnityEngine;
#if UNITY_EDITOR
    using UnityEditor;
#endif

    /// <summary>
    /// Registers this package's built-in WallstopProto formatters as the player and the editor start.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the only file under <c>WallstopProto/</c> that references <c>UnityEngine</c>, and
    /// deliberately so: the rest of the serializer compiles outside Unity, which is what lets the
    /// wire core and every formatter be differential-tested against protobuf-net in a plain
    /// <c>dotnet</c> harness.
    /// </para>
    /// <para>
    /// <b>Why <see cref="RuntimeInitializeOnLoadMethodAttribute"/> rather than a module
    /// initializer.</b> Unity's managed stripping treats a <c>[RuntimeInitializeOnLoadMethod]</c> as
    /// a root. A <c>[ModuleInitializer]</c> on a registrar nothing else references can be stripped
    /// along with the registrar, and auto-registration that silently does not happen is worse than
    /// no auto-registration at all: it fails only in a shipped IL2CPP player, as a thrown
    /// <c>InvalidOperationException</c> from the first save or load, and never in the editor.
    /// </para>
    /// <para>
    /// <b>Why the earliest phase.</b> <see cref="WProtoFormatterProvider.Register{T}"/> is
    /// last-wins, which is what lets a consumer replace a formatter this package ships.
    /// <see cref="RuntimeInitializeLoadType.SubsystemRegistration"/> is the first phase Unity runs,
    /// so consumer registration from any later phase -- including the default,
    /// <see cref="RuntimeInitializeLoadType.BeforeSceneLoad"/> -- always wins.
    /// </para>
    /// </remarks>
    internal static class WProtoBootstrap
    {
#if UNITY_EDITOR
        [InitializeOnLoadMethod]
        private static void RegisterInEditor()
        {
            WProtoBuiltInFormatters.RegisterAll();
        }
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void RegisterInPlayer()
        {
            WProtoBuiltInFormatters.RegisterAll();
        }
    }
}
