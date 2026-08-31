// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Helper.Logging
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using Extension;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Utils;
    using Debug = UnityEngine.Debug;
    using Object = UnityEngine.Object;
#if UNITY_EDITOR
    using UnityEditor;
#endif

    public readonly struct DecorationEntry
    {
        internal DecorationEntry(
            string tag,
            bool editorOnly,
            Func<string, bool> predicate,
            Func<string, object, string> formatter
        )
        {
            Tag = tag;
            EditorOnly = editorOnly;
            Predicate = predicate;
            Formatter = formatter;
        }

        internal string Tag { get; }
        internal bool EditorOnly { get; }
        internal Func<string, bool> Predicate { get; }
        internal Func<string, object, string> Formatter { get; }
    }

    /// <summary>
    /// Default supported formats:
    ///     b -> Bold text
    ///     bold -> Bold text
    ///     ! -> Bold text
    ///     i -> Italic text
    ///     italic -> Italic text
    ///     _ -> Italic text
    ///     json -> format as JSON
    ///     #color-hex -> Colored text
    ///     #color-name -> Colored text
    ///     color=value -> Colored text
    ///     1-100 -> Sized text
    ///     size=1-100 -> Sized text
    /// </summary>
    public sealed class UnityLogTagFormatter : IFormatProvider, ICustomFormatter
    {
        public const char Separator = ',';

        private static readonly string NewLine = Environment.NewLine;

        private static readonly Dictionary<string, string> ColorNamesToHex = ReflectionHelpers
            .LoadStaticPropertiesForType<Color>()
            .Where(kvp => kvp.Value.PropertyType == typeof(Color))
            .Select(kvp => (kvp.Key, ((Color)kvp.Value.GetValue(null)).ToHex()))
            .ToDictionary(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// All currently registered decorations by tag.
        /// </summary>
        public IEnumerable<string> Decorations =>
            _matchingDecorations.Values.SelectMany(x => x).Select(value => value.Tag);

        public IReadOnlyCollection<IReadOnlyList<DecorationEntry>> MatchingDecorations =>
            _matchingDecorations.Values;

        private readonly SortedDictionary<int, List<DecorationEntry>> _matchingDecorations = new();
        private readonly Dictionary<
            int,
            PooledResource<List<DecorationEntry>>
        > _matchingDecorationLeases = new();
        private readonly Dictionary<string, (int priority, int index)> _decorationLookup = new(
            StringComparer.OrdinalIgnoreCase
        );
        private readonly StringBuilder _cachedStringBuilder = new();
        private readonly List<string> _cachedDecorators = new();
        private readonly HashSet<string> _appliedTags = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Stopwatch FallbackStopwatch = Stopwatch.StartNew();
        private static int _unityMainThreadId;
        private static int _mainThreadCaptured;

        public UnityLogTagFormatter()
            : this(true) { }

        /// <summary>
        /// Creates a new UnityLogTagFormatter.
        /// </summary>
        /// <param name="createDefaultDecorators">If true, applies default decorators (bold, italic, color, size, and json).</param>
        public UnityLogTagFormatter(bool createDefaultDecorators)
        {
            if (!createDefaultDecorators)
            {
                return;
            }

            AddDecoration(
                format =>
                    string.Equals(format, "b", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(format, "bold", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(format, "!", StringComparison.OrdinalIgnoreCase),
                format: (_, value) => $"<b>{value}</b>",
                tag: "Bold",
                editorOnly: true,
                force: true
            );

            AddDecoration(
                format =>
                    string.Equals(format, "i", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(format, "italic", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(format, "_", StringComparison.OrdinalIgnoreCase),
                format: (_, value) => $"<i>{value}</i>",
                tag: "Italic",
                editorOnly: true,
                force: true
            );

            AddDecoration(
                match: "json",
                format: value => value?.ToJson() ?? "{}",
                tag: "JSON",
                editorOnly: false,
                force: true
            );

            const char colorCharCheck = '#';
            const string colorStringCheck = "color=";
            AddDecoration(
                format =>
                    format.StartsWith(colorCharCheck)
                    || format.StartsWith(colorStringCheck, StringComparison.OrdinalIgnoreCase),
                format: (format, value) =>
                {
                    string baseColor = format.StartsWith(
                        colorStringCheck,
                        StringComparison.OrdinalIgnoreCase
                    )
                        ? format.Substring(colorStringCheck.Length)
                        : format;

                    string hexCode = ColorNamesToHex.GetValueOrDefault(
                        format.StartsWith(colorCharCheck) ? format.Substring(1) : baseColor,
                        baseColor
                    );

                    // A colour Unity cannot parse produces a tag it renders as literal text, so the
                    // message reads "<color=notacolor>whatever</color>" instead of the value. Emitting
                    // the value undecorated loses the colour and keeps the message.
                    return ColorUtility.TryParseHtmlString(hexCode, out Color _)
                        ? $"<color={hexCode}>{value}</color>"
                        : $"{value}";
                },
                tag: "Color",
                editorOnly: true,
                force: true
            );

            const string sizeCheck = "size=";
            AddDecoration(
                format =>
                    format.StartsWith(sizeCheck, StringComparison.OrdinalIgnoreCase)
                        && int.TryParse(format.Substring(sizeCheck.Length), out _)
                    || int.TryParse(format, out _),
                format: (format, value) =>
                {
                    if (!int.TryParse(format, out int size))
                    {
                        size = int.Parse(format.Substring(sizeCheck.Length));
                    }
                    return $"<size={size}>{value}</size>";
                },
                tag: "Size",
                editorOnly: true,
                force: true
            );
        }

        [HideInCallstack]
        public object GetFormat(Type formatType)
        {
            return formatType.IsAssignableFrom(typeof(ICustomFormatter)) ? this : null;
        }

        [HideInCallstack]
        private static string ToSafeString(object arg)
        {
            if (arg is Object unityObj)
            {
                return unityObj != null ? unityObj.ToString() : string.Empty;
            }
            return arg?.ToString() ?? string.Empty;
        }

        [HideInCallstack]
        public string Format(string format, object arg, IFormatProvider formatProvider)
        {
            if (string.IsNullOrWhiteSpace(format))
            {
                return ToSafeString(arg);
            }

            _cachedDecorators.Clear();
            if (!format.Contains(Separator))
            {
                _cachedDecorators.Add(format);
            }
            else
            {
                _cachedStringBuilder.Clear();
                foreach (char element in format)
                {
                    if (element == Separator)
                    {
                        if (0 < _cachedStringBuilder.Length)
                        {
                            _cachedDecorators.Add(_cachedStringBuilder.ToString());
                            _cachedStringBuilder.Clear();
                        }
                    }
                    else
                    {
                        _cachedStringBuilder.Append(element);
                    }
                }
                if (0 < _cachedStringBuilder.Length)
                {
                    _cachedDecorators.Add(_cachedStringBuilder.ToString());
                    _cachedStringBuilder.Clear();
                }
            }

            _appliedTags.Clear();
            object formatted = arg;
            foreach (string key in _cachedDecorators)
            {
                foreach (List<DecorationEntry> matchingDecoration in _matchingDecorations.Values)
                {
                    foreach (DecorationEntry entry in matchingDecoration)
                    {
                        if (
                            (Application.isEditor || !entry.EditorOnly)
                            && entry.Predicate(key)
                            && _appliedTags.Add(entry.Tag)
                        )
                        {
                            formatted = entry.Formatter(key, formatted);
                        }
                    }
                }
            }

            if (0 < _appliedTags.Count)
            {
                return formatted.ToString();
            }

            if (arg is not string && arg is IFormattable formattable)
            {
                return formattable.ToString(format, this);
            }

            return ToSafeString(arg);
        }

        /// <summary>
        /// Renders and emits an informational log.
        /// </summary>
        /// <param name="message">The interpolated message. Formatter tags such as <c>:json</c> apply.</param>
        /// <param name="context">The object the log is attributed to, so clicking it pings the object.</param>
        /// <param name="e">An optional exception to append.</param>
        /// <param name="pretty">When <see langword="true"/>, prefixes the timestamp and context.</param>
        /// <param name="stackTrace">
        /// Pass <see langword="false"/> for a diagnostic that repeats. Unity captures a managed stack
        /// trace for every log whose type is configured <c>ScriptOnly</c> - the default for all three -
        /// measured on 6000.4.6f1 at 178.4 us against 13.3 us without it.
        /// </param>
        /// <returns>The rendered message, as it was written to the console.</returns>
        [HideInCallstack]
        public string Log(
            FormattableString message,
            Object context = null,
            Exception e = null,
            bool pretty = true,
            bool stackTrace = true
        )
        {
            string rendered = Render(message, context, e, pretty);
            if (!stackTrace)
            {
                // Unity captures a managed stack trace for every log whose type is configured
                // ScriptOnly, which is the default for all three. Measured on 6000.4.6f1 that
                // capture is 178.4 us of a 178.4 us call, against 13.3 us for the same message
                // with LogOption.NoStacktrace -- 13.4x, and it is paid per call, not per frame.
                // The message is passed as an ARGUMENT rather than as the format: a rendered
                // message containing a brace would otherwise throw FormatException from inside
                // the logger.
                Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, context, "{0}", rendered);
                return rendered;
            }

            if (context != null)
            {
                Debug.Log(rendered, context);
            }
            else
            {
                Debug.Log(rendered);
            }

            return rendered;
        }

        /// <summary>
        /// Renders and emits a warning.
        /// </summary>
        /// <param name="message">The interpolated message. Formatter tags such as <c>:json</c> apply.</param>
        /// <param name="context">The object the log is attributed to, so clicking it pings the object.</param>
        /// <param name="e">An optional exception to append.</param>
        /// <param name="pretty">When <see langword="true"/>, prefixes the timestamp and context.</param>
        /// <param name="stackTrace">
        /// Pass <see langword="false"/> for a diagnostic that repeats. Unity captures a managed stack
        /// trace for every log whose type is configured <c>ScriptOnly</c> - the default for all three -
        /// measured on 6000.4.6f1 at 178.4 us against 13.3 us without it.
        /// </param>
        /// <returns>The rendered message, as it was written to the console.</returns>
        [HideInCallstack]
        public string LogWarn(
            FormattableString message,
            Object context = null,
            Exception e = null,
            bool pretty = true,
            bool stackTrace = true
        )
        {
            string rendered = Render(message, context, e, pretty);
            if (!stackTrace)
            {
                // Unity captures a managed stack trace for every log whose type is configured
                // ScriptOnly, which is the default for all three. Measured on 6000.4.6f1 that
                // capture is 178.4 us of a 178.4 us call, against 13.3 us for the same message
                // with LogOption.NoStacktrace -- 13.4x, and it is paid per call, not per frame.
                // The message is passed as an ARGUMENT rather than as the format: a rendered
                // message containing a brace would otherwise throw FormatException from inside
                // the logger.
                Debug.LogFormat(LogType.Warning, LogOption.NoStacktrace, context, "{0}", rendered);
                return rendered;
            }

            if (context != null)
            {
                Debug.LogWarning(rendered, context);
            }
            else
            {
                Debug.LogWarning(rendered);
            }

            return rendered;
        }

        /// <summary>
        /// Renders and emits an error.
        /// </summary>
        /// <param name="message">The interpolated message. Formatter tags such as <c>:json</c> apply.</param>
        /// <param name="context">The object the log is attributed to, so clicking it pings the object.</param>
        /// <param name="e">An optional exception to append.</param>
        /// <param name="pretty">When <see langword="true"/>, prefixes the timestamp and context.</param>
        /// <param name="stackTrace">
        /// Pass <see langword="false"/> for a diagnostic that repeats. Unity captures a managed stack
        /// trace for every log whose type is configured <c>ScriptOnly</c> - the default for all three -
        /// measured on 6000.4.6f1 at 178.4 us against 13.3 us without it.
        /// </param>
        /// <returns>The rendered message, as it was written to the console.</returns>
        [HideInCallstack]
        public string LogError(
            FormattableString message,
            Object context = null,
            Exception e = null,
            bool pretty = true,
            bool stackTrace = true
        )
        {
            string rendered = Render(message, context, e, pretty);
            if (!stackTrace)
            {
                // Unity captures a managed stack trace for every log whose type is configured
                // ScriptOnly, which is the default for all three. Measured on 6000.4.6f1 that
                // capture is 178.4 us of a 178.4 us call, against 13.3 us for the same message
                // with LogOption.NoStacktrace -- 13.4x, and it is paid per call, not per frame.
                // The message is passed as an ARGUMENT rather than as the format: a rendered
                // message containing a brace would otherwise throw FormatException from inside
                // the logger.
                Debug.LogFormat(LogType.Error, LogOption.NoStacktrace, context, "{0}", rendered);
                return rendered;
            }

            if (context != null)
            {
                Debug.LogError(rendered, context);
            }
            else
            {
                Debug.LogError(rendered);
            }

            return rendered;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void CaptureRuntimeMainThread()
        {
            CaptureUnityMainThread(Thread.CurrentThread);
        }

#if UNITY_EDITOR
        [InitializeOnLoadMethod]
        private static void CaptureEditorMainThread()
        {
            if (Application.isPlaying)
            {
                return;
            }

            CaptureUnityMainThread(Thread.CurrentThread);
        }
#endif

        private static void EnsureMainThreadCaptured()
        {
            if (_mainThreadCaptured == 1)
            {
                return;
            }

            CaptureUnityMainThread(Thread.CurrentThread);
        }

        private static void CaptureUnityMainThread(Thread thread)
        {
            if (thread == null)
            {
                return;
            }

            _unityMainThreadId = thread.ManagedThreadId;
            Interlocked.Exchange(ref _mainThreadCaptured, 1);
        }

        private static string BuildThreadLabel()
        {
            EnsureMainThreadCaptured();
            int currentId = Thread.CurrentThread.ManagedThreadId;

            if (_mainThreadCaptured == 1 && currentId == _unityMainThreadId)
            {
                return string.Empty;
            }

            string threadName = Thread.CurrentThread.Name;
            if (!string.IsNullOrWhiteSpace(threadName))
            {
                return $"{threadName}#{currentId}";
            }

            return $"worker#{currentId}";
        }

        private static float GetTimestamp()
        {
            if (UnityMainThreadGuard.IsMainThread)
            {
                return Time.time;
            }

            return (float)FallbackStopwatch.Elapsed.TotalSeconds;
        }

        /// <summary>
        /// Attempts to add a decoration.
        /// </summary>
        /// <param name="match">An exact match for tag ("a" would correspond to ${value:a})</param>
        /// <param name="format">A formatter to apply to the matched object (typically something like <c>value =&gt; $"&lt;newFormat&gt;{value}&lt;/newFormat&gt;"</c>).</param>
        /// <param name="tag">A descriptive, unique identifier for the decoration (for example, "Bold", or "Color")</param>
        /// <param name="priority">The priority to register the decoration at. Lower values will be evaluated first.</param>
        /// <param name="editorOnly">If true, will only be applied when the game is running in the Unity Editor.</param>
        /// <param name="force">
        ///     If true, will override any existing decorations for the same tag, regardless of priority.
        ///     If false, decorations with the same tag (compared OrdinalIgnoreCase) will cause the registration to fail.
        /// </param>
        /// <returns>True if the decoration was added, false if the decoration was not added.</returns>
        public bool AddDecoration(
            string match,
            Func<object, string> format,
            string tag = null,
            int priority = 0,
            bool editorOnly = false,
            bool force = false
        )
        {
            return AddDecoration(
                check => string.Equals(check, match, StringComparison.OrdinalIgnoreCase),
                format: (_, value) => format(value),
                tag: tag ?? match,
                priority: priority,
                editorOnly: editorOnly,
                force: force
            );
        }

        /// <summary>
        /// Attempts to add a decoration.
        /// </summary>
        /// <param name="predicate">
        ///     Tag matcher. Can be as complex as you want. For example, the default color matcher
        ///     is implemented something like tag => tag.StartsWith('#') || tag.StartsWith("color=")
        /// </param>
        /// <param name="format">
        ///     Custom formatting function. Takes in both the matched tag as well the current object to format. In
        ///     the same case of color matching, the implementation needs to be smart enough to handle the case where
        ///     the tag is "#red" or "color=red" or "color=#FF0000".
        /// </param>
        /// <param name="tag">A descriptive, unique identifier for the decoration (for example, "Bold", or "Color")</param>
        /// <param name="priority">The priority to register the decoration at. Lower values will be evaluated first.</param>
        /// <param name="editorOnly">If true, will only be applied when the game is running in the Unity Editor.</param>
        /// <param name="force">
        ///     If true, will override any existing decorations for the same tag, regardless of priority.
        ///     If false, decorations with the same tag (compared OrdinalIgnoreCase) will cause the registration to fail.
        /// </param>
        /// <returns>True if the decoration was added, false if the decoration was not added.</returns>
        public bool AddDecoration(
            Func<string, bool> predicate,
            Func<string, object, string> format,
            string tag,
            int priority = 0,
            bool editorOnly = false,
            bool force = false
        )
        {
            if (_decorationLookup.TryGetValue(tag, out (int priority, int index) existing))
            {
                if (!force)
                {
                    return false;
                }

                if (
                    existing.priority == priority
                    && _matchingDecorations.TryGetValue(
                        priority,
                        out List<DecorationEntry> decorationsAtPriority
                    )
                )
                {
                    decorationsAtPriority[existing.index] = new DecorationEntry(
                        tag,
                        editorOnly,
                        predicate,
                        format
                    );
                    _decorationLookup[tag] = (priority, existing.index);
                    return true;
                }

                TryRemoveDecorationInternal(existing.priority, existing.index, out _);
            }

            if (
                !_matchingDecorations.TryGetValue(
                    priority,
                    out List<DecorationEntry> matchingDecorations
                )
            )
            {
                PooledResource<List<DecorationEntry>> lease = Buffers<DecorationEntry>.List.Get(
                    out matchingDecorations
                );
                _matchingDecorations[priority] = matchingDecorations;
                _matchingDecorationLeases[priority] = lease;
            }

            int indexToInsert = matchingDecorations.Count;
            matchingDecorations.Add(new DecorationEntry(tag, editorOnly, predicate, format));
            _decorationLookup[tag] = (priority, indexToInsert);
            return true;
        }

        /// <summary>
        /// Attempts to remove a decoration by its tag.
        /// </summary>
        /// <param name="tag">Tag for the decoration ("Bold", "Color", etc.)</param>
        /// <param name="decoration">The removed decoration, if one was found.</param>
        /// <returns>True if a decoration was found for that tag and removed, false otherwise.</returns>
        public bool RemoveDecoration(string tag, out DecorationEntry decoration)
        {
            if (!_decorationLookup.TryGetValue(tag, out (int priority, int index) existing))
            {
                decoration = default;
                return false;
            }

            return TryRemoveDecorationInternal(existing.priority, existing.index, out decoration);
        }

        private bool TryRemoveDecorationInternal(
            int priority,
            int index,
            out DecorationEntry decoration
        )
        {
            if (
                !_matchingDecorations.TryGetValue(
                    priority,
                    out List<DecorationEntry> decorationsAtPriority
                )
            )
            {
                decoration = default;
                return false;
            }

            DecorationEntry removed = decorationsAtPriority[index];

            decorationsAtPriority.RemoveAt(index);
            _decorationLookup.Remove(removed.Tag);

            for (int i = index; i < decorationsAtPriority.Count; ++i)
            {
                DecorationEntry entry = decorationsAtPriority[i];
                _decorationLookup[entry.Tag] = (priority, i);
            }

            if (decorationsAtPriority.Count == 0)
            {
                _matchingDecorations.Remove(priority);
                ReleasePriorityList(priority);
            }

            decoration = removed;
            return true;
        }

        private void ReleasePriorityList(int priority)
        {
            if (
                _matchingDecorationLeases.TryGetValue(
                    priority,
                    out PooledResource<List<DecorationEntry>> lease
                )
            )
            {
                lease.Dispose();
                _matchingDecorationLeases.Remove(priority);
            }
        }

        [HideInCallstack]
        private string Render(
            FormattableString message,
            Object unityObject,
            Exception e,
            bool pretty
        )
        {
            if (!pretty)
            {
                return e != null
                    ? $"{message.ToString(this)}{NewLine}    {e}"
                    : message.ToString(this);
            }

            float now = GetTimestamp();
            string threadLabel = BuildThreadLabel();
            bool hasThreadLabel = !string.IsNullOrEmpty(threadLabel);
            string componentType;
            string gameObjectName;
            if (unityObject != null)
            {
                componentType = unityObject.GetType().Name;
                gameObjectName = unityObject.name;
            }
            else
            {
                componentType = "NO_TYPE";
                gameObjectName = "NO_NAME";
            }

            string contextLabel = $"{gameObjectName}[{componentType}]";
            string prefix = hasThreadLabel
                ? $"{now}|{threadLabel}|{contextLabel}"
                : $"{now}|{contextLabel}";

            return e != null
                ? $"{prefix}|{message.ToString(this)}{NewLine}    {e}"
                : $"{prefix}|{message.ToString(this)}";
        }
    }
}
