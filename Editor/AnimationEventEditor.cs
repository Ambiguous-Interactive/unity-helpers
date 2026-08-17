// MIT License - Copyright (c) 2023 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE
//
// Portions of this file are adapted from SeniaAnimationEventEditor by yujen,
// https://gist.github.com/yujen/5e1cd78e2a341260b38029de08a449da. The design of those portions is
// the original author's. The upstream license is not recorded in this repository.
// See docs/project/third-party-notices.md.

namespace WallstopStudios.UnityHelpers.Editor
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Editor.Core.Helper;
    using WallstopStudios.UnityHelpers.Utils;

    // https://gist.githubusercontent.com/yujen/5e1cd78e2a341260b38029de08a449da/raw/ac60c1002e0e14375de5b2b0a167af00df3f74b4/SeniaAnimationEventEditor.cs
    /// <summary>
    /// Interactive editor window for inspecting, creating, editing, validating, and reordering
    /// <see cref="AnimationEvent"/> entries on an <see cref="AnimationClip"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Problems this solves:
    /// </para>
    /// <para>
    /// - Discoverability: Quickly lists all methods on <see cref="MonoBehaviour"/>s that are valid
    ///   animation event handlers. In Explicit Mode, only methods decorated with
    ///   <c>[AnimationEvent]</c> are shown, keeping choices intentional and type-safe.
    /// </para>
    /// <para>
    /// - Efficiency: Add, duplicate, re-order, and bulk edit events with frame snapping, optional
    ///   precise time control, keyboard shortcuts, and sprite previews for quick visual context.
    /// </para>
    /// <para>
    /// - Safety: Validates method existence, parameter compatibility, and warns for mismatches.
    /// </para>
    /// <para>
    /// How it works:
    /// </para>
    /// <para>
    /// - Uses <see cref="UnityEditor.TypeCache"/> to scan <see cref="MonoBehaviour"/> types and find
    ///   viable event handlers via <see cref="AnimationEventHelpers"/>. In
    ///   Explicit Mode, only methods with <c>[AnimationEvent]</c> are included; it respects
    ///   <c>ignoreDerived</c> to limit inherited exposure.
    /// </para>
    /// <para>
    /// - Presents a searchable clip list from the current Animator, then renders each event with
    ///   method/type selection and parameter editors matching the selected signature.
    /// </para>
    /// <para>
    /// - Supports keyboard shortcuts: Delete (remove), Ctrl+D (duplicate), Up/Down (navigate).
    /// </para>
    /// <para>
    /// Usage scenarios:
    /// </para>
    /// <list type="bullet">
    /// <item>
    /// <description>Authoring complex 2D/3D animation behaviors with strongly-typed methods.</description>
    /// </item>
    /// <item>
    /// <description>Retrofitting existing clips with validated events and consistent frame timing.</description>
    /// </item>
    /// <item>
    /// <description>Bulk reviewing events across clips to catch missing handlers.</description>
    /// </item>
    /// </list>
    /// <para>
    /// Pros:
    /// </para>
    /// <list type="bullet">
    /// <item><description>Fast discovery of valid methods and signatures.</description></item>
    /// <item><description>Explicit Mode reduces accidental misuse.</description></item>
    /// <item><description>Sprite previews aid visual alignment.</description></item>
    /// </list>
    /// <para>
    /// Cons / Caveats:
    /// </para>
    /// <list type="bullet">
    /// <item><description>Only scans assemblies available to the editor domain.</description></item>
    /// <item><description>Methods must have void return and a supported single parameter or none.</description></item>
    /// <item><description>Changing frame rate affects timing; save to persist.</description></item>
    /// </list>
    /// <example>
    /// <![CDATA[
    /// using WallstopStudios.UnityHelpers.Core.Attributes;
    /// using UnityEngine;
    ///
    /// public class EnemyEvents : MonoBehaviour
    /// {
    ///     [AnimationEvent] // Will show in Explicit Mode
    ///     private void Footstep() { /* play SFX */ }
    ///
    ///     [AnimationEvent(ignoreDerived: true)]
    ///     private void AttackFrame(int damageFrame) { /* trigger damage */ }
    ///
    ///     // Supported signatures: (), (int), (float), (string), (Enum), (UnityEngine.Object)
    /// }
    ///
    /// // Open from menu: Tools/Wallstop Studios/Unity Helpers/AnimationEvent Editor
    /// // Or from code:
    /// UnityEditor.EditorWindow.GetWindow<WallstopStudios.UnityHelpers.Editor.AnimationEventEditor>();
    /// ]]>
    /// </example>
    public sealed class AnimationEventEditor : EditorWindow
    {
        private static IReadOnlyDictionary<Type, IReadOnlyList<MethodInfo>> _cachedTypesToMethods;

        private static IReadOnlyDictionary<Type, IReadOnlyList<MethodInfo>> TypesToMethods
        {
            get
            {
                if (_cachedTypesToMethods == null)
                {
                    InitializeTypeCache();
                }
                return _cachedTypesToMethods;
            }
        }

        private static void InitializeTypeCache()
        {
            Dictionary<Type, IReadOnlyList<MethodInfo>> typesToMethods = new();
            IEnumerable<Type> types =
                WallstopStudios.UnityHelpers.Core.Helper.ReflectionHelpers.GetTypesDerivedFrom<MonoBehaviour>(
                    includeAbstract: false
                );
            foreach (Type type in types)
            {
                if (type == null)
                {
                    continue;
                }
                List<MethodInfo> methods = AnimationEventHelpers.GetPossibleAnimatorEventsForType(
                    type
                );
                if (methods is { Count: > 0 })
                {
                    typesToMethods[type] = methods;
                }
            }

            using (
                Buffers<KeyValuePair<Type, IReadOnlyList<MethodInfo>>>.List.Get(
                    out List<KeyValuePair<Type, IReadOnlyList<MethodInfo>>> snapshot
                )
            )
            {
                foreach (KeyValuePair<Type, IReadOnlyList<MethodInfo>> kvp in typesToMethods)
                {
                    snapshot.Add(kvp);
                }
                for (int i = 0; i < snapshot.Count; i++)
                {
                    KeyValuePair<Type, IReadOnlyList<MethodInfo>> entry = snapshot[i];
                    if (entry.Value == null || entry.Value.Count <= 0)
                    {
                        _ = typesToMethods.Remove(entry.Key);
                    }
                }
            }

            _cachedTypesToMethods = typesToMethods;
        }

        [MenuItem("Tools/Wallstop Studios/Unity Helpers/AnimationEvent Editor")]
        private static void AnimationEventEditorMenu()
        {
            GetWindow(typeof(AnimationEventEditor));
        }

        private readonly AnimationEventEditorViewModel _viewModel = new();

        private IReadOnlyDictionary<Type, IReadOnlyList<MethodInfo>> Lookup =>
            _explicitMode ? AnimationEventHelpers.TypesToMethods : TypesToMethods;

        private int MaxFrameIndex =>
            _viewModel.CurrentClip == null
                ? 0
                : (int)Math.Round(_viewModel.FrameRate * _viewModel.CurrentClip.length);

        private Vector2 _scrollPosition;
        private Animator _sourceAnimator;
        private bool _explicitMode = true;
        private bool _controlFrameTime;
        private string _animationSearchString = string.Empty;

        // Cache for sprite previews
        private readonly Dictionary<Sprite, Texture2D> _spriteTextureCache = new();

        private int _selectedFrameIndex = -1;

        // Keyboard shortcut state
        private int _focusedEventIndex = -1;

        private void OnGUI()
        {
            AnimationEventKeyboardShortcuts.Handle(
                Event.current,
                _viewModel,
                ref _focusedEventIndex,
                RecordUndo,
                Repaint
            );

            Animator tmpAnimator =
                EditorGUILayout.ObjectField(
                    "Animator Object",
                    _sourceAnimator,
                    typeof(Animator),
                    true
                ) as Animator;
            if (tmpAnimator == null)
            {
                _sourceAnimator = null;
                RefreshAnimationEvents(null);
                return;
            }

            if (_sourceAnimator != tmpAnimator)
            {
                _sourceAnimator = tmpAnimator;
                RefreshAnimationEvents(null);
            }

            _explicitMode = EditorGUILayout.Toggle(
                new GUIContent(
                    "Explicit Mode",
                    "If true, restricts results to only those that explicitly with [AnimationEvent]"
                ),
                _explicitMode
            );
            _controlFrameTime = EditorGUILayout.Toggle(
                new GUIContent(
                    "Control Frame Time",
                    "Select to edit precise time of animation events instead of snapping to nearest frame"
                ),
                _controlFrameTime
            );

            AnimationClip selectedClip = AnimationEventClipSelector.Draw(
                _sourceAnimator,
                _viewModel,
                ref _animationSearchString,
                () => RefreshAnimationEvents(null)
            );
            if (selectedClip == null)
            {
                return;
            }

            if (_viewModel.CurrentClip != selectedClip)
            {
                RefreshAnimationEvents(selectedClip);
            }

            _selectedFrameIndex = EditorGUILayout.IntField("FrameIndex", _selectedFrameIndex);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Add Event"))
                {
                    if (0 <= _selectedFrameIndex)
                    {
                        AddNewEvent(
                            _viewModel.FrameRate <= 0f
                                ? 0f
                                : _selectedFrameIndex / _viewModel.FrameRate
                        );
                    }
                }

                if (GUILayout.Button("Add Event at Time 0"))
                {
                    AddNewEvent(0f);
                }
            }

            // Frame rate with change detection and undo support
            EditorGUI.BeginChangeCheck();
            float newFrameRate = EditorGUILayout.FloatField("FrameRate", _viewModel.FrameRate);
            if (EditorGUI.EndChangeCheck())
            {
                _viewModel.SetFrameRate(newFrameRate);
            }

            if (_viewModel.FrameRateChanged)
            {
                EditorGUILayout.HelpBox(
                    "Frame rate will be saved when you click 'Save'. Click 'Reset' to revert.",
                    MessageType.Info
                );
            }

            DrawGuiLine(height: 5, color: new Color(0f, 0.5f, 1f, 1f));
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            // Use cached list to avoid allocations
            int stateCount = _viewModel.Count;
            for (int i = 0; i < stateCount; ++i)
            {
                AnimationEventItem item = _viewModel.GetEvent(i);
                AnimationEvent animEvent = item.animationEvent;

                int frame = Mathf.RoundToInt(animEvent.time * _viewModel.FrameRate);

                // Highlight focused event
                Color oldBgColor = GUI.backgroundColor;
                if (i == _focusedEventIndex)
                {
                    GUI.backgroundColor = new Color(0.3f, 0.6f, 1f, 0.3f);
                }

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                GUI.backgroundColor = oldBgColor;

                EditorGUILayout.PrefixLabel("Frame " + frame);

                AnimationEventSpritePreviewRenderer.Draw(item, _viewModel, _spriteTextureCache);

                using (new EditorGUI.IndentLevelScope())
                {
                    RenderAnimationEventItem(item, frame, i);
                    if (i != stateCount - 1)
                    {
                        DrawGuiLine(height: 3, color: new Color(0f, 1f, 0.3f, 1f));
                        EditorGUILayout.Space();
                    }
                }

                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.EndScrollView();

            DrawControlButtons();

            // Show keyboard shortcuts help
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(
                "Shortcuts: Delete (remove event), Ctrl+D (duplicate), Up/Down (navigate)",
                EditorStyles.miniLabel
            );
        }

        private void AddNewEvent(float time)
        {
            RecordUndo("Add Animation Event");
            _viewModel.AddEvent(time);
        }

        private int AnimationEventComparison(AnimationEventItem lhs, AnimationEventItem rhs)
        {
            if (ReferenceEquals(lhs, rhs))
            {
                return 0;
            }

            if (ReferenceEquals(null, rhs))
            {
                return -1;
            }

            if (ReferenceEquals(null, lhs))
            {
                return 1;
            }

            return AnimationEventEqualityComparer.Instance.Compare(
                lhs.animationEvent,
                rhs.animationEvent
            );
        }

        private void DrawControlButtons()
        {
            if (!_viewModel.HasPendingChanges())
            {
                GUILayout.Label("No changes detected...");
                return;
            }

            Color oldColor = GUI.color;
            GUI.color = Color.green;
            if (GUILayout.Button("Save"))
            {
                SaveAnimation();
            }

            GUI.color = oldColor;
            if (GUILayout.Button("Reset"))
            {
                RefreshAnimationEvents();
            }

            if (_viewModel.NeedsReordering())
            {
                if (GUILayout.Button("Re-Order"))
                {
                    RecordUndo("Re-order Animation Events");
                    _viewModel.SortEvents(AnimationEventComparison);
                }
            }
        }

        private void RenderAnimationEventItem(AnimationEventItem item, int frame, int itemIndex)
        {
            int index = itemIndex;
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!_viewModel.CanSwapWithPrevious(index)))
                {
                    if (GUILayout.Button("Move Up") && _viewModel.TrySwapWithPrevious(index))
                    {
                        RecordUndo("Move Animation Event Up");
                        _focusedEventIndex = index - 1;
                    }
                }

                using (new EditorGUI.DisabledScope(!_viewModel.CanSwapWithNext(index)))
                {
                    if (GUILayout.Button("Move Down") && _viewModel.TrySwapWithNext(index))
                    {
                        RecordUndo("Move Animation Event Down");
                        _focusedEventIndex = index + 1;
                    }
                }

                if (GUILayout.Button("Reset") && _viewModel.TryResetToBaseline(item))
                {
                    RecordUndo("Reset Animation Event");
                    item.selectedType = null;
                    item.selectedMethod = null;
                    item.cachedLookup = null;
                }

                if (GUILayout.Button($"Remove Event at frame {frame}"))
                {
                    RecordUndo("Remove Animation Event");
                    _viewModel.RemoveEvent(item);
                    return;
                }

                if (GUILayout.Button("Duplicate", GUILayout.Width(80)))
                {
                    RecordUndo("Duplicate Animation Event");
                    AnimationEvent duplicated = AnimationEventEqualityComparer.Instance.Copy(
                        item.animationEvent
                    );
                    _viewModel.InsertEvent(index + 1, new AnimationEventItem(duplicated));
                }
            }

            IReadOnlyDictionary<Type, IReadOnlyList<MethodInfo>> lookup = _explicitMode
                ? Lookup
                : AnimationEventMethodSelector.FilterLookup(item, Lookup);

            AnimationEventMethodSelector.EnsureSelection(item, lookup);
            AnimationEventMethodSelector.ValidateSelection(item, lookup);

            AnimationEventTimeFieldRenderer.DrawTimeFields(
                item,
                frame,
                _viewModel.FrameRate,
                _viewModel.CurrentClip == null ? 0f : _viewModel.CurrentClip.length,
                _controlFrameTime,
                RecordUndo,
                () => item.texture = null
            );

            AnimationEventFunctionFieldRenderer.DrawFunctionFields(item, _explicitMode, RecordUndo);

            // Show validation status
            if (!item.isValid)
            {
                EditorGUILayout.HelpBox(item.validationMessage, MessageType.Warning);
            }

            if (!AnimationEventMethodSelector.DrawTypeSelector(item, lookup, RecordUndo))
            {
                return;
            }

            if (!AnimationEventMethodSelector.DrawMethodSelector(item, lookup, RecordUndo))
            {
                return;
            }

            AnimationEventParameterRenderer.Render(item, RecordUndo);
        }

        private void RefreshAnimationEvents(AnimationClip clip = null)
        {
            _spriteTextureCache.Clear();
            _focusedEventIndex = -1;
            _viewModel.LoadClip(clip ?? _viewModel.CurrentClip);
            _selectedFrameIndex = _viewModel.CurrentClip == null ? -1 : MaxFrameIndex;
        }

        private void RecordUndo(string operationName)
        {
            Undo.RecordObject(this, operationName);
        }

        private void SaveAnimation()
        {
            AnimationClip clip = _viewModel.CurrentClip;
            if (clip == null)
            {
                return;
            }

            Undo.RecordObject(clip, "Save Animation Events");
            AnimationUtility.SetAnimationEvents(clip, _viewModel.BuildEventArray());

            if (_viewModel.FrameRateChanged)
            {
                clip.frameRate = _viewModel.FrameRate;
                _viewModel.ResetFrameRateChanged();
            }

            EditorUtility.SetDirty(clip);
            AssetDatabase.SaveAssetIfDirty(clip);
            _viewModel.SnapshotBaseline();
        }

        private void DrawGuiLine(int height = 1, Color? color = null)
        {
            Rect rect = EditorGUILayout.GetControlRect(false, height);
            rect.height = height;
            int minusWidth = EditorGUI.indentLevel * 16;
            rect.xMin += minusWidth;
            EditorGUI.DrawRect(rect, color ?? new Color(0.5f, 0.5f, 0.5f, 1f));
        }
    }
#endif
}
