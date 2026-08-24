// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Sprites
{
#if UNITY_EDITOR
    using UnityEditor;
    using UnityEngine;

    [CustomPropertyDrawer(typeof(SpriteSettings))]
    public sealed class SpriteSettingsDrawer : PropertyDrawer
    {
        private const float CheckboxWidth = 18f;
        private const float HorizontalSpacing = 5f;

        private readonly (string apply, string val, string label)[] _settingPairs =
        {
            (
                nameof(SpriteSettings.applyPixelsPerUnit),
                nameof(SpriteSettings.pixelsPerUnit),
                "Pixels Per Unit"
            ),
            (nameof(SpriteSettings.applyPivot), nameof(SpriteSettings.pivot), "Pivot"),
            (
                nameof(SpriteSettings.applySpriteMode),
                nameof(SpriteSettings.spriteMode),
                "Sprite Mode"
            ),
            (
                nameof(SpriteSettings.applyGenerateMipMaps),
                nameof(SpriteSettings.generateMipMaps),
                "Generate Mip Maps"
            ),
            (
                nameof(SpriteSettings.applyAlphaIsTransparency),
                nameof(SpriteSettings.alphaIsTransparency),
                "Alpha Is Transparency"
            ),
            (
                nameof(SpriteSettings.applyReadWriteEnabled),
                nameof(SpriteSettings.readWriteEnabled),
                "Read/Write Enabled"
            ),
            (
                nameof(SpriteSettings.applyExtrudeEdges),
                nameof(SpriteSettings.extrudeEdges),
                "Extrude Edges"
            ),
            (nameof(SpriteSettings.applyWrapMode), nameof(SpriteSettings.wrapMode), "Wrap Mode"),
            (
                nameof(SpriteSettings.applyFilterMode),
                nameof(SpriteSettings.filterMode),
                "Filter Mode"
            ),
            (
                nameof(SpriteSettings.applyCrunchCompression),
                nameof(SpriteSettings.useCrunchCompression),
                "Use Crunch Compression"
            ),
            (
                nameof(SpriteSettings.applyCompression),
                nameof(SpriteSettings.compressionLevel),
                "Compression Level"
            ),
        };

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            SerializedProperty nameProp = property.FindPropertyRelative(
                nameof(SpriteSettings.name)
            );

            Rect currentRect = new(
                position.x,
                position.y,
                position.width,
                EditorGUIUtility.singleLineHeight
            );
            EditorGUI.PropertyField(
                currentRect,
                nameProp,
                new GUIContent("Profile Name (Optional)")
            );

            currentRect.y +=
                EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;

            // Matching UI
            SerializedProperty matchByProp = property.FindPropertyRelative(
                nameof(SpriteSettings.matchBy)
            );
            SerializedProperty matchPatternProp = property.FindPropertyRelative(
                nameof(SpriteSettings.matchPattern)
            );
            SerializedProperty priorityProp = property.FindPropertyRelative(
                nameof(SpriteSettings.priority)
            );

            EditorGUI.LabelField(currentRect, "Matching", EditorStyles.boldLabel);
            currentRect.y +=
                EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;

            Rect rowRect = new(
                position.x,
                currentRect.y,
                position.width,
                EditorGUIUtility.singleLineHeight
            );
            Rect matchByRect = new(rowRect.x, rowRect.y, rowRect.width * 0.35f, rowRect.height);
            // Draw a compact label+field for Match By to prevent label width from eating field space
            const float matchByLabelWidth = 70f;
            Rect matchByLabelRect = new(
                matchByRect.x,
                matchByRect.y,
                matchByLabelWidth,
                matchByRect.height
            );
            Rect matchByFieldRect = new(
                matchByLabelRect.x + matchByLabelRect.width + HorizontalSpacing,
                matchByRect.y,
                Mathf.Max(0f, matchByRect.width - matchByLabelWidth - HorizontalSpacing),
                matchByRect.height
            );
            EditorGUI.LabelField(matchByLabelRect, "Match By");
            EditorGUI.PropertyField(matchByFieldRect, matchByProp, GUIContent.none);
            SpriteSettings.MatchMode mode = (SpriteSettings.MatchMode)matchByProp.enumValueIndex;
#pragma warning disable CS0618 // Type or member is obsolete
            if (mode != SpriteSettings.MatchMode.Any && mode != SpriteSettings.MatchMode.None)
#pragma warning restore CS0618 // Type or member is obsolete
            {
                Rect patternRect = new(
                    matchByRect.x + matchByRect.width + HorizontalSpacing,
                    rowRect.y,
                    rowRect.width - matchByRect.width - HorizontalSpacing - 80f,
                    rowRect.height
                );
                EditorGUI.PropertyField(patternRect, matchPatternProp, new GUIContent("Pattern"));
            }
            Rect priorityRect = new(
                rowRect.x + rowRect.width - 80f,
                rowRect.y,
                80f,
                rowRect.height
            );
            // Priority area is tight: draw label+field manually to avoid label consuming all width
            const float priorityLabelWidth = 50f;
            Rect priorityLabelRect = new(
                priorityRect.x,
                priorityRect.y,
                priorityLabelWidth,
                priorityRect.height
            );
            Rect priorityFieldRect = new(
                priorityLabelRect.x + priorityLabelRect.width + HorizontalSpacing,
                priorityRect.y,
                Mathf.Max(0f, priorityRect.width - priorityLabelWidth - HorizontalSpacing),
                priorityRect.height
            );
            EditorGUI.LabelField(priorityLabelRect, "Priority");
            EditorGUI.PropertyField(priorityFieldRect, priorityProp, GUIContent.none);
            currentRect.y +=
                EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;

            foreach ((string apply, string val, string label) pair in _settingPairs)
            {
                SerializedProperty applyProp = property.FindPropertyRelative(pair.apply);
                SerializedProperty valueProp = property.FindPropertyRelative(pair.val);

                if (applyProp == null || valueProp == null)
                {
                    EditorGUI.LabelField(currentRect, $"Error finding properties for {pair.label}");
                    currentRect.y +=
                        EditorGUIUtility.singleLineHeight
                        + EditorGUIUtility.standardVerticalSpacing;
                    continue;
                }

                float labelLineHeight = EditorGUIUtility.singleLineHeight;
                Rect labelLineRect = new(
                    position.x,
                    currentRect.y,
                    position.width,
                    labelLineHeight
                );
                Rect checkboxRect = new(
                    labelLineRect.x + labelLineRect.width - CheckboxWidth,
                    labelLineRect.y,
                    CheckboxWidth,
                    labelLineHeight
                );
                Rect labelRect = new(
                    labelLineRect.x,
                    labelLineRect.y,
                    labelLineRect.width - CheckboxWidth - HorizontalSpacing,
                    labelLineHeight
                );

                EditorGUI.LabelField(labelRect, pair.label);
                EditorGUI.PropertyField(checkboxRect, applyProp, GUIContent.none);
                currentRect.y += labelLineHeight + EditorGUIUtility.standardVerticalSpacing;

                if (applyProp.boolValue)
                {
                    float valuePropHeight = EditorGUI.GetPropertyHeight(valueProp, true);
                    Rect valueRect = new(
                        position.x,
                        currentRect.y,
                        position.width,
                        valuePropHeight
                    );

                    using (new EditorGUI.IndentLevelScope())
                    {
                        EditorGUI.PropertyField(valueRect, valueProp, GUIContent.none, true);
                    }

                    currentRect.y += valueRect.height + EditorGUIUtility.standardVerticalSpacing;
                }
            }

            // Enforce Texture Type UI
            SerializedProperty applyTextureTypeProp = property.FindPropertyRelative(
                nameof(SpriteSettings.applyTextureType)
            );
            SerializedProperty textureTypeProp = property.FindPropertyRelative(
                nameof(SpriteSettings.textureType)
            );

            float labelLineHeight2 = EditorGUIUtility.singleLineHeight;
            Rect labelLineRect2 = new(position.x, currentRect.y, position.width, labelLineHeight2);
            Rect checkboxRect2 = new(
                labelLineRect2.x + labelLineRect2.width - CheckboxWidth,
                labelLineRect2.y,
                CheckboxWidth,
                labelLineHeight2
            );
            Rect labelRect2 = new(
                labelLineRect2.x,
                labelLineRect2.y,
                labelLineRect2.width - CheckboxWidth - HorizontalSpacing,
                labelLineHeight2
            );
            EditorGUI.LabelField(labelRect2, "Enforce Texture Type");
            EditorGUI.PropertyField(checkboxRect2, applyTextureTypeProp, GUIContent.none);
            currentRect.y += labelLineHeight2 + EditorGUIUtility.standardVerticalSpacing;
            if (applyTextureTypeProp.boolValue)
            {
                float valuePropHeight = EditorGUI.GetPropertyHeight(textureTypeProp, true);
                Rect valueRect = new(position.x, currentRect.y, position.width, valuePropHeight);
                using (new EditorGUI.IndentLevelScope())
                {
                    EditorGUI.PropertyField(valueRect, textureTypeProp, GUIContent.none, true);
                }
                currentRect.y += valueRect.height + EditorGUIUtility.standardVerticalSpacing;
            }

            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float totalHeight = 0f;

            SerializedProperty nameProp = property.FindPropertyRelative(
                nameof(SpriteSettings.name)
            );
            if (nameProp != null)
            {
                totalHeight +=
                    EditorGUI.GetPropertyHeight(nameProp)
                    + EditorGUIUtility.standardVerticalSpacing;
            }

            // Matching header + row (matchBy, pattern, priority)
            totalHeight +=
                EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing; // header
            totalHeight +=
                EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing; // row

            foreach ((string apply, string val, string label) pair in _settingPairs)
            {
                SerializedProperty applyProp = property.FindPropertyRelative(pair.apply);
                SerializedProperty valueProp = property.FindPropertyRelative(pair.val);

                if (applyProp == null || valueProp == null)
                {
                    continue;
                }

                totalHeight +=
                    EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;

                if (applyProp.boolValue)
                {
                    totalHeight +=
                        EditorGUI.GetPropertyHeight(valueProp, true)
                        + EditorGUIUtility.standardVerticalSpacing;
                }
            }

            // Texture type enforcement height
            SerializedProperty applyTextureTypeProp = property.FindPropertyRelative(
                nameof(SpriteSettings.applyTextureType)
            );
            SerializedProperty textureTypeProp = property.FindPropertyRelative(
                nameof(SpriteSettings.textureType)
            );
            totalHeight +=
                EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
            if (applyTextureTypeProp is { boolValue: true })
            {
                totalHeight +=
                    EditorGUI.GetPropertyHeight(textureTypeProp, true)
                    + EditorGUIUtility.standardVerticalSpacing;
            }

            if (0 < totalHeight)
            {
                totalHeight -= EditorGUIUtility.standardVerticalSpacing;
            }

            return totalHeight;
        }
    }
#endif
}
