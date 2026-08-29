// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

// Reference-only stand-in for the package's OWN `AnimationEventEditor`, the second shim here that
// does not stand in for a third party. Read the reason before touching it.
//
// `Editor/AnimationEventEditor.cs` calls `AssetDatabase.SaveAssetIfDirty`, added in Unity 2021.2 and
// absent from the `Unity3D.SDK` 2021.1.14 reference assembly, so the real file cannot compile here
// and is excluded (see the csproj's enumerated exclusion list).
//
// `Editor/AnimationEventEditorViewModel.cs` is NOT excluded, and its type summary carries a
// `<see cref="AnimationEventEditor"/>` that a consumer's IDE resolves against the shipped source.
// Once XML doc validation is on, that correct cref reads as CS1574 here purely because the target
// file is dropped. Degrading the comment to satisfy the harness would make the shipped docs worse
// to make a gate green, which is backwards -- so the type is stood in for instead, exactly as
// `ValidationSharedShim` stands in for `ValidationShared`.
//
// Nothing compiled here binds a MEMBER of this type, only the name, so the body is deliberately
// empty. A type-checker asserts surface, never behaviour.
namespace WallstopStudios.UnityHelpers.Editor
{
    using UnityEditor;

    public sealed class AnimationEventEditor : EditorWindow { }
}
