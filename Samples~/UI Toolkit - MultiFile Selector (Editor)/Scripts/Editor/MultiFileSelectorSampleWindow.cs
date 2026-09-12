// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace Samples.UnityHelpers.UIToolkit.Editor
{
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.UIElements;
    using WallstopStudios.UnityHelpers.Visuals.UIToolkit;

    /// <summary>
    /// Minimal EditorWindow that hosts the MultiFileSelectorElement.
    /// </summary>
    public sealed class MultiFileSelectorSampleWindow : EditorWindow
    {
        [MenuItem("Window/Unity Helpers/MultiFile Selector Sample")]
        public static void Open()
        {
            MultiFileSelectorSampleWindow window = GetWindow<MultiFileSelectorSampleWindow>();
            window.titleContent = new GUIContent("MultiFile Selector Sample");
#pragma warning disable WPROTO028 // Editor-window layout is never serialized by WallstopProto.
            window.minSize = new Vector2(640f, 420f);
#pragma warning restore WPROTO028
        }

        private void CreateGUI()
        {
            MultiFileSelectorElement selector = new MultiFileSelectorElement(
                initialPath: "Assets",
                filterExtensions: new[] { ".png", ".jpg", ".cs" },
                persistenceKey: "MultiFileSelectorSampleWindow"
            );
            selector.OnFilesSelectedReadOnly += OnFilesSelected;
            selector.OnCancelled += OnCancelled;
            rootVisualElement.Add(selector);
        }

        private void OnFilesSelected(IReadOnlyCollection<string> files)
        {
            string first = "<none>";
            foreach (string file in files)
            {
                first = file ?? "<none>";
                break;
            }
            Debug.Log($"Selected {files.Count} files. First: {first}");
        }

        private void OnCancelled()
        {
            Debug.Log("Selection cancelled.");
        }
    }
}
