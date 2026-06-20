// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.TestUtils
{
    using System;
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEngine;

    internal enum DrawerVisualRole
    {
        Unknown = 0,
        DictionaryKey,
        DictionaryValue,
        SetElement,
    }

    internal readonly struct DrawerVisualSample
    {
        public DrawerVisualSample(DrawerVisualRole role, int arrayIndex, Rect rect)
        {
            Role = role;
            ArrayIndex = arrayIndex;
            Rect = rect;
        }

        public DrawerVisualRole Role { get; }

        public int ArrayIndex { get; }

        public Rect Rect { get; }
    }

    internal static class DrawerVisualRecorder
    {
        private static readonly List<DrawerVisualSample> Samples = new();
        private static bool _isRecording;

        internal static bool IsRecording => _isRecording;

        internal static void BeginRecording()
        {
            Samples.Clear();
            _isRecording = true;
        }

        internal static DrawerVisualSample[] EndRecording()
        {
            DrawerVisualSample[] snapshot = Samples.ToArray();
            Samples.Clear();
            _isRecording = false;
            return snapshot;
        }

        internal static void Record(DrawerVisualRole role, SerializedProperty property, Rect rect)
        {
            if (!_isRecording)
            {
                return;
            }

            // A single IMGUI repaint internally runs a Layout OnGUI pass before the
            // Repaint pass, and the harness also sends a standalone Layout event, so an
            // unfiltered recorder captures each logical row up to three times. The
            // duplicates collapse onto identical rects and break the row-pitch invariants
            // the regression tests assert (two identical rows read as zero pitch). Sample
            // only the Repaint pass -- the rects that are actually painted -- so every row
            // is recorded exactly once.
            if (Event.current == null || Event.current.type != EventType.Repaint)
            {
                return;
            }

            int arrayIndex = ExtractArrayIndex(property?.propertyPath);
            Samples.Add(new DrawerVisualSample(role, arrayIndex, rect));
        }

        private static int ExtractArrayIndex(string propertyPath)
        {
            if (string.IsNullOrEmpty(propertyPath))
            {
                return -1;
            }

            const string Needle = "Array.data[";
            int needleIndex = propertyPath.LastIndexOf(Needle, StringComparison.Ordinal);
            if (needleIndex < 0)
            {
                return -1;
            }

            int start = needleIndex + Needle.Length;
            int end = propertyPath.IndexOf(']', start);
            if (end < 0)
            {
                return -1;
            }

            string slice = propertyPath.Substring(start, end - start);
            return int.TryParse(slice, out int value) ? value : -1;
        }
    }
}
