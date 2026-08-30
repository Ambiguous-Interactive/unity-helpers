// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Validation.Continuous
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.IO;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.UIElements;
    using Object = UnityEngine.Object;

    /// <summary>
    /// A dockable view of what is currently wrong with the project, and the button that finds out.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Holds no result state of its own. Everything shown comes from
    /// <see cref="ValidationResults"/>, so an incremental re-check triggered by an import updates
    /// the list without the window knowing an import happened, and closing the window loses
    /// nothing.
    /// </para>
    /// <para>
    /// Every decision with a right answer -- what the filter keeps, how the summary reads -- lives
    /// in <see cref="ValidationResultFilter"/>, which a fixture can assert. What is left here is
    /// element construction, which an EditMode test cannot drive anyway.
    /// </para>
    /// <para>
    /// Built from the elements that exist in every editor this package supports. A row reports its
    /// own click through <see cref="VisualElement.userData"/> rather than through the list's
    /// selection event, whose name changed between 2021.3 and 2022.2; and progress is a label
    /// rather than a <c>ProgressBar</c>, which moved namespace over the same range.
    /// </para>
    /// </remarks>
    public sealed class ValidationWindow : EditorWindow
    {
        private const string WindowTitle = "Asset Validation";

        /// <summary>
        /// Where the suppression file is read and written, matching the path the headless run
        /// documents. Relative, because the editor's working directory is the project root.
        /// </summary>
        private const string DefaultSuppressionsPath = "ValidationSuppressions.txt";

        private readonly List<ValidationFinding> _visible = new List<ValidationFinding>();

        private ValidationSeverity _minimum = ValidationSeverity.Info;
        private string _query = string.Empty;
        private bool _includeSuppressed = true;
        private ValidationSuppressions _suppressions = ValidationSuppressions.Empty;
        private int _selected = -1;

        private Label _summary;
        private Label _progress;
        private ListView _list;
        private Button _run;
        private Button _severity;

        /// <summary>Opens, or focuses, the validation window.</summary>
        [MenuItem("Tools/Wallstop Studios/Unity Helpers/Asset Validation")]
        public static void Open()
        {
            ValidationWindow window = GetWindow<ValidationWindow>();
            window.titleContent = new GUIContent(WindowTitle);
            window.minSize = new Vector2(460f, 240f);
            window.Show();
        }

        private void OnEnable()
        {
            ValidationResults.Changed += Refresh;
            EditorApplication.update += TrackProgress;
        }

        private void OnDisable()
        {
            ValidationResults.Changed -= Refresh;
            EditorApplication.update -= TrackProgress;
        }

        private void CreateGUI()
        {
            VisualElement root = rootVisualElement;
            root.style.paddingLeft = 6f;
            root.style.paddingRight = 6f;
            root.style.paddingTop = 6f;
            root.style.paddingBottom = 6f;

            VisualElement toolbar = Row(root);
            _run = new Button(RunOrCancel) { text = "Validate Project" };
            toolbar.Add(_run);

            Toggle auto = new Toggle("Re-check on import") { value = ValidationAutoRun.Enabled };
            auto.tooltip =
                "Re-checks only the assets an import touched, a few milliseconds per editor tick. Stored per user.";
            auto.RegisterValueChangedCallback(changed =>
                ValidationAutoRun.Enabled = changed.newValue
            );
            toolbar.Add(auto);

            _progress = new Label();
            _progress.style.paddingLeft = 8f;
            toolbar.Add(_progress);

            VisualElement filters = Row(root);
            TextField search = new TextField("Search") { value = _query };
            search.style.flexGrow = 1f;
            search.RegisterValueChangedCallback(changed =>
            {
                _query = changed.newValue;
                Refresh();
            });
            filters.Add(search);

            _severity = new Button(CycleSeverity);
            _severity.tooltip = "The least severe level shown. Click to cycle.";
            filters.Add(_severity);

            Toggle suppressed = new Toggle("Show suppressed") { value = _includeSuppressed };
            suppressed.tooltip =
                "Suppressed findings are marked rather than hidden, so a suppression file cannot look like a clean project.";
            suppressed.RegisterValueChangedCallback(changed =>
            {
                _includeSuppressed = changed.newValue;
                Refresh();
            });
            filters.Add(suppressed);

            _summary = new Label();
            _summary.style.paddingTop = 4f;
            _summary.style.paddingBottom = 4f;
            root.Add(_summary);

            _list = new ListView
            {
                fixedItemHeight = 20f,
                selectionType = SelectionType.Single,
                itemsSource = _visible,
                makeItem = MakeRow,
                bindItem = BindRow,
            };
            _list.style.flexGrow = 1f;
            root.Add(_list);

            VisualElement footer = Row(root);
            footer.Add(new Button(SuppressSelected) { text = "Suppress Selected" });
            footer.Add(new Button(ReloadSuppressions) { text = "Reload Suppressions" });

            ReloadSuppressions();
        }

        /// <summary>
        /// Steps the severity floor through its three levels.
        /// </summary>
        /// <remarks>
        /// A cycling button rather than a dropdown, and deliberately: <c>PopupField&lt;T&gt;</c>
        /// and <c>DropdownField</c> both moved between <c>UnityEditor.UIElements</c> and
        /// <c>UnityEngine.UIElements</c> across the editors this package supports, and an
        /// <c>EnumField</c> would offer <see cref="ValidationSeverity.Unknown"/>, which is
        /// <c>[Obsolete]</c> and means nothing here. Three states do not need a list.
        /// </remarks>
        private void CycleSeverity()
        {
            _minimum =
                _minimum == ValidationSeverity.Info ? ValidationSeverity.Warning
                : _minimum == ValidationSeverity.Warning ? ValidationSeverity.Error
                : ValidationSeverity.Info;
            Refresh();
        }

        private static VisualElement Row(VisualElement parent)
        {
            VisualElement row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            parent.Add(row);
            return row;
        }

        private VisualElement MakeRow()
        {
            Label row = new Label();
            row.style.unityTextAlign = TextAnchor.MiddleLeft;
            row.style.paddingLeft = 4f;
            // Registered once per recycled element and read back from userData, so the callback
            // does not capture an index the list will later rebind to a different finding.
            row.RegisterCallback<ClickEvent>(_ => Select(row.userData as int?));
            return row;
        }

        private void BindRow(VisualElement element, int index)
        {
            if (!(element is Label row) || index < 0 || _visible.Count <= index)
            {
                return;
            }

            ValidationFinding finding = _visible[index];
            bool suppressed = _suppressions.IsSuppressed(in finding);
            row.userData = index;
            row.text = (suppressed ? "(suppressed) " : string.Empty) + finding;
            row.tooltip = finding.Id;
            row.style.color = suppressed ? Color.gray : ColorFor(finding.Severity);
        }

        private static Color ColorFor(ValidationSeverity severity)
        {
            switch (severity)
            {
                case ValidationSeverity.Error:
                {
                    return new Color(0.94f, 0.42f, 0.38f);
                }
                case ValidationSeverity.Warning:
                {
                    return new Color(0.95f, 0.77f, 0.35f);
                }
                default:
                {
                    return new Color(0.72f, 0.76f, 0.80f);
                }
            }
        }

        private void Select(int? index)
        {
            if (index == null || index.Value < 0 || _visible.Count <= index.Value)
            {
                return;
            }

            _selected = index.Value;
            Reveal(_visible[_selected]);
        }

        private static void Reveal(ValidationFinding finding)
        {
            // TryGetTarget rather than the field: the reference was captured while the run held the
            // asset loaded, and a reimport since then leaves a live managed reference over a dead
            // native pointer.
            if (finding.TryGetTarget(out Object target))
            {
                Selection.activeObject = target;
                EditorGUIUtility.PingObject(target);
                return;
            }

            Object reloaded = AssetDatabase.LoadMainAssetAtPath(finding.AssetPath);
            if (reloaded != null)
            {
                Selection.activeObject = reloaded;
                EditorGUIUtility.PingObject(reloaded);
            }
        }

        private void RunOrCancel()
        {
            if (ValidationScheduler.IsRunning)
            {
                ValidationScheduler.Stop();
                return;
            }

            List<string> problems = new List<string>();
            List<IValidationRule> rules = ValidationBatch.DiscoverRules(problems);
            for (int index = 0; index < problems.Count; index++)
            {
                Debug.LogWarning("[Asset Validation] " + problems[index]);
            }

            ValidationRun run = new ValidationRun(rules, ValidationTargets.Enumerate());
            if (
                !ValidationScheduler.TryStart(
                    run,
                    ValidationScheduler.DefaultBudgetMilliseconds,
                    Complete
                )
            )
            {
                return;
            }

            _run.text = "Cancel";
            Refresh();
        }

        private void Complete(ValidationRun run)
        {
            // A cancelled run walked a prefix of the project, so recording it as the whole answer
            // would drop every asset it never reached and present the rest as the project's state.
            if (!run.IsCancelled)
            {
                ValidationResults.RecordRun(run);
            }

            _run.text = "Validate Project";
            _progress.text = run.IsCancelled ? "Cancelled." : string.Empty;
            Refresh();
        }

        private void TrackProgress()
        {
            if (_progress == null)
            {
                return;
            }

            ValidationRun run = ValidationScheduler.Active;
            if (run == null)
            {
                return;
            }

            _progress.text = run.ProcessedCount + " / " + run.TotalCount;
        }

        private void ReloadSuppressions()
        {
            _suppressions = ValidationSuppressions.Parse(ReadOrEmpty(DefaultSuppressionsPath));
            Refresh();
        }

        private static string ReadOrEmpty(string path)
        {
            try
            {
                return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
            }
            catch (IOException)
            {
                return string.Empty;
            }
            catch (UnauthorizedAccessException)
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Adds the selected finding to the suppression file, keeping every entry already there.
        /// </summary>
        /// <remarks>
        /// Rendered from the findings rather than appended as text, so the reviewable comment above
        /// each entry is regenerated and cannot drift into describing something the identity no
        /// longer points at. An entry whose finding this run did not reproduce is preserved by
        /// identity, because dropping it would silently un-suppress a decision about an asset
        /// nobody looked at.
        /// </remarks>
        private void SuppressSelected()
        {
            if (_selected < 0 || _visible.Count <= _selected)
            {
                return;
            }

            ValidationFinding chosen = _visible[_selected];
            List<ValidationFinding> keep = new List<ValidationFinding>();
            HashSet<string> kept = new HashSet<string>(StringComparer.Ordinal);
            List<ValidationFinding> known = ValidationResults.Snapshot();
            for (int index = 0; index < known.Count; index++)
            {
                ValidationFinding finding = known[index];
                bool wanted =
                    _suppressions.IsSuppressed(in finding)
                    || string.Equals(finding.Id, chosen.Id, StringComparison.Ordinal);
                if (wanted && kept.Add(finding.Id))
                {
                    keep.Add(finding);
                }
            }

            try
            {
                File.WriteAllText(DefaultSuppressionsPath, ValidationSuppressions.Render(keep));
            }
            catch (Exception thrown)
            {
                Debug.LogWarning(
                    "[Asset Validation] could not write "
                        + DefaultSuppressionsPath
                        + ": "
                        + thrown.Message
                );
                return;
            }

            ReloadSuppressions();
        }

        private void Refresh()
        {
            if (_list == null || _summary == null)
            {
                return;
            }

            List<ValidationFinding> known = ValidationResults.Snapshot();
            _visible.Clear();
            _visible.AddRange(
                ValidationResultFilter.Apply(
                    known,
                    _minimum,
                    _query,
                    _includeSuppressed,
                    _suppressions
                )
            );
            _selected = -1;
            if (_severity != null)
            {
                _severity.text = "At least: " + _minimum;
            }

            _summary.text = ValidationResultFilter.Summarize(
                ValidationResults.HasRun,
                ValidationResults.CheckedAssetCount,
                known
            );
            _list.itemsSource = _visible;
            _list.Rebuild();
        }
    }
#endif
}
