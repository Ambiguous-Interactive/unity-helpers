// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.CustomDrawers
{
#if UNITY_EDITOR && ODIN_INSPECTOR
    using System;
    using Sirenix.OdinInspector.Editor;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Editor.CustomDrawers.Utils;
    using WallstopStudios.UnityHelpers.Editor.Internal;
    using Object = UnityEngine.Object;

    /// <summary>
    /// Odin Inspector attribute drawer for <see cref="WInLineEditorAttribute"/>.
    /// Embeds the referenced object's inspector directly beneath the object field.
    /// </summary>
    /// <remarks>
    /// This drawer ensures WInLineEditor works correctly when Odin Inspector is installed
    /// and classes derive from SerializedMonoBehaviour or SerializedScriptableObject,
    /// where Unity's standard PropertyDrawer system is bypassed.
    /// </remarks>
    public sealed class WInLineEditorOdinDrawer : OdinAttributeDrawer<WInLineEditorAttribute>
    {
        protected override void DrawPropertyLayout(GUIContent label)
        {
            WInLineEditorAttribute inlineAttribute = Attribute;
            if (inlineAttribute == null)
            {
                CallNextDrawer(label);
                return;
            }

            if (Property == null || Property.ValueEntry == null)
            {
                CallNextDrawer(label);
                return;
            }

            Type valueType = Property.ValueEntry.TypeOfValue;
            if (valueType == null || !typeof(Object).IsAssignableFrom(valueType))
            {
                CallNextDrawer(label);
                return;
            }

            object weakValue = Property.ValueEntry.WeakSmartValue;
            Object objectValue = weakValue as Object;

            if (inlineAttribute.DrawObjectField)
            {
                CallNextDrawer(label);
            }
            else
            {
                if (label != null && label != GUIContent.none)
                {
                    EditorGUILayout.LabelField(label);
                }
            }

            if (objectValue == null)
            {
                return;
            }

            WInLineEditorMode resolvedMode = InLineEditorShared.ResolveMode(inlineAttribute);
            string foldoutKey = BuildFoldoutKey();
            bool isAlwaysExpanded = resolvedMode == WInLineEditorMode.AlwaysExpanded;
            bool foldoutState = InLineEditorShared.GetFoldoutState(foldoutKey, resolvedMode);

            bool showHeader =
                InLineEditorShared.ShouldDrawStandaloneHeader(inlineAttribute)
                && (inlineAttribute.DrawHeader || !isAlwaysExpanded);
            bool showBody = isAlwaysExpanded || foldoutState;

            if (showHeader)
            {
                EditorGUILayout.Space(InLineEditorShared.Spacing);
                foldoutState = DrawHeader(
                    objectValue,
                    label,
                    !isAlwaysExpanded,
                    foldoutState,
                    foldoutKey
                );
            }

            if (showBody)
            {
                EditorGUILayout.Space(InLineEditorShared.Spacing);
                DrawInlineInspector(objectValue, inlineAttribute);
            }

            if (inlineAttribute.DrawPreview && showBody)
            {
                EditorGUILayout.Space(InLineEditorShared.Spacing);
                DrawPreview(objectValue, inlineAttribute.PreviewHeight);
            }
        }

        private string BuildFoldoutKey()
        {
            InspectorProperty property = Property;
            if (property == null)
            {
                return string.Empty;
            }

            object parent = property.Parent?.ValueEntry?.WeakSmartValue;
            long parentId = 0;
            if (parent is Object unityObject)
            {
                parentId = unityObject.GetUnityObjectId();
            }
            else if (parent != null)
            {
                parentId = parent.GetHashCode();
            }

            string path = property.Path;
            return InLineEditorShared.BuildFoldoutKey(parentId, path);
        }

        private string BuildScrollKey()
        {
            return InLineEditorShared.BuildScrollKey(BuildFoldoutKey());
        }

        private static bool DrawHeader(
            Object value,
            GUIContent label,
            bool showFoldoutToggle,
            bool foldoutState,
            string foldoutKey
        )
        {
            Rect rect = EditorGUILayout.GetControlRect(false, InLineEditorShared.HeaderHeight);

            float pingWidth = InLineEditorShared.GetPingButtonWidth();
            bool showPingButton = InLineEditorShared.ShouldShowPingButton(value);
            float headerSpacing = 0f;
            float headerRightMargin = 0f;

            if (showPingButton)
            {
                headerSpacing = InLineEditorShared.HeaderPingSpacing;
                headerRightMargin = InLineEditorShared.PingButtonRightMargin;
                bool hasSpace =
                    rect.width - pingWidth - headerSpacing - headerRightMargin
                    >= InLineEditorShared.MinimumFoldoutLabelWidth;
                if (!hasSpace)
                {
                    showPingButton = false;
                    headerSpacing = 0f;
                    headerRightMargin = 0f;
                }
            }

            float labelWidth = showPingButton
                ? Mathf.Max(0f, rect.width - pingWidth - headerSpacing - headerRightMargin)
                : rect.width;
            Rect labelRect = new Rect(rect.x, rect.y, labelWidth, rect.height);
            Rect pingRect = new Rect(
                rect.x + labelWidth + (showPingButton ? headerSpacing : 0f),
                rect.y,
                pingWidth,
                rect.height
            );

            GUIContent headerContent = InLineEditorShared.PrepareHeaderContent(value, label);

            if (showFoldoutToggle)
            {
                bool newState = EditorGUI.Foldout(labelRect, foldoutState, headerContent, true);
                if (newState != foldoutState)
                {
                    foldoutState = newState;
                    InLineEditorShared.SetFoldoutState(foldoutKey, foldoutState);
                }
            }
            else
            {
                EditorGUI.LabelField(labelRect, headerContent, EditorStyles.boldLabel);
            }

            if (showPingButton)
            {
                using (new EditorGUI.DisabledScope(value == null))
                {
                    if (
                        GUI.Button(
                            pingRect,
                            InLineEditorShared.PingButtonContent,
                            EditorStyles.miniButton
                        )
                    )
                    {
                        EditorGUIUtility.PingObject(value);
                    }
                }
            }

            return foldoutState;
        }

        private void DrawInlineInspector(Object value, WInLineEditorAttribute inlineAttribute)
        {
            Editor editor = InLineEditorShared.GetOrCreateEditor(value);
            if (editor == null)
            {
                return;
            }

            float inspectorHeight = inlineAttribute.InspectorHeight;
            string scrollKey = BuildScrollKey();
            Vector2 scrollPosition = InLineEditorShared.GetScrollPosition(scrollKey);

            bool enableScrolling = inlineAttribute.EnableScrolling;
            bool scrollViewStarted = false;
            bool verticalGroupStarted = false;

            try
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                verticalGroupStarted = true;

                if (enableScrolling)
                {
                    scrollPosition = EditorGUILayout.BeginScrollView(
                        scrollPosition,
                        GUILayout.Height(inspectorHeight)
                    );
                    scrollViewStarted = true;
                }

                using (InlineInspectorContext.Enter())
                {
                    editor.serializedObject.UpdateIfRequiredOrScript();
                    InLineEditorShared.DrawSerializedObject(editor.serializedObject);
                }

                if (scrollViewStarted)
                {
                    EditorGUILayout.EndScrollView();
                    InLineEditorShared.SetScrollPosition(scrollKey, scrollPosition);
                    scrollViewStarted = false;
                }

                EditorGUILayout.EndVertical();
                verticalGroupStarted = false;
            }
            catch (Exception e)
            {
                Debug.LogError($"Exception drawing inline inspector {e}.");
            }
            finally
            {
                if (scrollViewStarted)
                {
                    EditorGUILayout.EndScrollView();
                    InLineEditorShared.SetScrollPosition(scrollKey, scrollPosition);
                }

                if (verticalGroupStarted)
                {
                    EditorGUILayout.EndVertical();
                }
            }
        }

        private static void DrawPreview(Object value, float previewHeight)
        {
            if (value == null)
            {
                return;
            }

            Texture2D preview = AssetPreview.GetAssetPreview(value);
            if (preview == null)
            {
                preview = AssetPreview.GetMiniThumbnail(value);
            }

            if (preview == null)
            {
                return;
            }

            Rect previewRect = EditorGUILayout.GetControlRect(false, previewHeight);
            float aspectRatio = (float)preview.width / preview.height;
            float previewWidth = Mathf.Min(previewRect.width, previewHeight * aspectRatio);
            Rect centeredRect = new Rect(
                previewRect.x + (previewRect.width - previewWidth) * 0.5f,
                previewRect.y,
                previewWidth,
                previewHeight
            );

            GUI.DrawTexture(centeredRect, preview, ScaleMode.ScaleToFit);
        }

        /// <summary>
        /// Clears cached editors and state. Primarily for testing purposes.
        /// </summary>
        internal static void ClearCachedStateForTesting()
        {
            InLineEditorShared.ClearCachedStateForTesting();
        }

        /// <summary>
        /// Test hook to set the foldout state for a given key.
        /// </summary>
        internal static void SetFoldoutStateForTesting(string key, bool expanded)
        {
            InLineEditorShared.SetFoldoutStateForTesting(key, expanded);
        }

        /// <summary>
        /// Test hook to get the foldout state for a given key.
        /// </summary>
        internal static bool GetFoldoutStateForTesting(string key)
        {
            return InLineEditorShared.GetFoldoutStateForTesting(key);
        }
    }
#endif
}
