// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Extension
{
    using UnityEngine.UIElements;

    /// <summary>
    /// Extension methods for <see cref="VisualElement"/> that answer questions UI Toolkit leaves to
    /// the caller: whether an element is actually drawn, and where keyboard focus currently sits.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Thread Safety: none of these are thread-safe. UI Toolkit is main-thread only.
    /// </para>
    /// </remarks>
    public static class VisualElementExtensions
    {
        /// <summary>
        /// Determines whether this element and every one of its ancestors are laid out, according to
        /// the inline <see cref="IStyle.display"/> values currently assigned.
        /// </summary>
        /// <param name="element">The element to test. A null element is not shown.</param>
        /// <returns>
        /// True when neither this element nor any ancestor has an inline display of
        /// <see cref="DisplayStyle.None"/>; otherwise, false.
        /// </returns>
        /// <remarks>
        /// <para>
        /// The walk to the root is the point. <c>display: None</c> removes the whole subtree, so an
        /// element with its own display of <see cref="DisplayStyle.Flex"/> under a hidden ancestor
        /// is not drawn, and asking only the element answers true.
        /// </para>
        /// <para>
        /// It walks <see cref="VisualElement.hierarchy"/> rather than
        /// <see cref="VisualElement.parent"/>: the latter skips content containers, which is a
        /// different tree from the one display applies down.
        /// </para>
        /// <para>
        /// Inline style rather than <see cref="VisualElement.resolvedStyle"/>, because
        /// <c>resolvedStyle</c> needs a layout pass: a tree shown this frame has not had one, so it
        /// still answers with the previous frame's value. Use <see cref="IsShownResolved"/> when the
        /// caller is past layout and wants USS taken into account. An element nobody styled reads as
        /// <see cref="DisplayStyle.Flex"/>, which is the correct default.
        /// </para>
        /// <para>
        /// Null Handling: returns false for a null element.
        /// Performance: O(depth). Allocations: none.
        /// </para>
        /// </remarks>
        public static bool IsShown(this VisualElement element)
        {
            if (element == null)
            {
                return false;
            }

            for (VisualElement walked = element; walked != null; walked = walked.hierarchy.parent)
            {
                if (walked.style.display.value == DisplayStyle.None)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Determines whether this element and every one of its ancestors are laid out, according to
        /// <see cref="VisualElement.resolvedStyle"/>, which includes USS as well as inline styles.
        /// </summary>
        /// <param name="element">The element to test. A null element is not shown.</param>
        /// <returns>
        /// True when neither this element nor any ancestor resolves to a display of
        /// <see cref="DisplayStyle.None"/>; otherwise, false.
        /// </returns>
        /// <remarks>
        /// <para>
        /// This is the answer for callers who want USS and not just what they assigned themselves.
        /// <c>resolvedStyle</c> is produced by the panel's style pass, so a change made during the
        /// current frame is not guaranteed to be visible here until that pass runs;
        /// <see cref="IsShown"/> reads the inline value the caller assigned and is immediate.
        /// Measured on 6000.4.6f1, an inline <c>display</c> assignment does reach
        /// <c>resolvedStyle</c> straight away on a detached element -- but that is an
        /// implementation detail of one version, not a contract to lean on.
        /// </para>
        /// <para>
        /// Like <see cref="IsShown"/> this walks to the root, because <c>resolvedStyle.display</c>
        /// is NOT inherited: measured, hiding an ancestor leaves every descendant still reporting
        /// <see cref="DisplayStyle.Flex"/>, so asking only the element answers true for a subtree
        /// nobody can see.
        /// </para>
        /// <para>
        /// Null Handling: returns false for a null element.
        /// Performance: O(depth). Allocations: none.
        /// </para>
        /// </remarks>
        public static bool IsShownResolved(this VisualElement element)
        {
            if (element == null)
            {
                return false;
            }

            for (VisualElement walked = element; walked != null; walked = walked.hierarchy.parent)
            {
                if (walked.resolvedStyle.display == DisplayStyle.None)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Determines whether this element is <paramref name="scope"/> itself, or sits anywhere
        /// beneath it in the <see cref="VisualElement.hierarchy"/> tree.
        /// </summary>
        /// <param name="element">The element to locate. A null element is within nothing.</param>
        /// <param name="scope">The subtree to test against. A null scope contains nothing.</param>
        /// <returns>
        /// True when <paramref name="element"/> is <paramref name="scope"/> or a descendant of it;
        /// otherwise, false.
        /// </returns>
        /// <remarks>
        /// <para>
        /// This exists because UI Toolkit's <see cref="VisualElement.Contains(VisualElement)"/> is
        /// STRICT and reversed: measured on 6000.4.6f1, <c>scope.Contains(scope)</c> is false. The
        /// question callers actually ask -- "is the panel's focused element one of mine" -- has to
        /// answer yes when the focused element IS the one they own, so the strict form silently
        /// drops the most common case. Use <c>Contains</c> directly when strict descent is wanted.
        /// </para>
        /// <para>
        /// It walks <see cref="VisualElement.hierarchy"/>, the physical tree, so an element parented
        /// into a composite control's content container is still found inside that control.
        /// </para>
        /// <para>
        /// Reference identity is deliberate: <see cref="VisualElement"/> is not a
        /// <see cref="UnityEngine.Object"/>, so <c>==</c> is already reference equality, and saying
        /// so explicitly is what stops it being "fixed" into a null-tolerant compare.
        /// </para>
        /// <para>
        /// Null Handling: returns false when either argument is null.
        /// Performance: O(depth). Allocations: none.
        /// </para>
        /// </remarks>
        public static bool IsWithin(this VisualElement element, VisualElement scope)
        {
            if (element == null || scope == null)
            {
                return false;
            }

            for (VisualElement walked = element; walked != null; walked = walked.hierarchy.parent)
            {
                if (ReferenceEquals(walked, scope))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Gets the element that currently holds keyboard focus in the panel this element belongs to.
        /// </summary>
        /// <param name="element">Any element in the panel to query.</param>
        /// <returns>
        /// The focused element, or null when this element is not attached to a panel, when nothing
        /// is focused, or when the focused <see cref="Focusable"/> is not a
        /// <see cref="VisualElement"/> (an IMGUI container's inner control, for instance).
        /// </returns>
        /// <remarks>
        /// <para>
        /// Focus is panel-wide: the answer can be an element from anywhere in the panel, so callers
        /// deciding "is it one of mine" should pair this with
        /// <see cref="VisualElement.Contains(VisualElement)"/>.
        /// </para>
        /// <para>
        /// Null Handling: returns null for a null element and for an element off any panel.
        /// Performance: O(1). Allocations: none.
        /// </para>
        /// </remarks>
        public static VisualElement FocusedElement(this VisualElement element)
        {
            if (element == null)
            {
                return null;
            }

            IPanel panel = element.panel;
            if (panel == null)
            {
                return null;
            }

            FocusController controller = panel.focusController;
            if (controller == null)
            {
                return null;
            }

            return controller.focusedElement as VisualElement;
        }

        /// <summary>
        /// Requests keyboard focus for this element and reports whether the panel's focus actually
        /// landed on it or inside it.
        /// </summary>
        /// <param name="element">The element to focus.</param>
        /// <returns>
        /// True when this element, or a descendant it delegated focus to, holds focus after the
        /// call; otherwise, false.
        /// </returns>
        /// <remarks>
        /// <para>
        /// <see cref="Focusable.Focus"/> is silent about failure: called on an element with no
        /// focus controller — one detached from a panel, or one whose
        /// <see cref="Focusable.focusable"/> is false — it returns having done nothing, so "did the
        /// cursor move" is only answerable by reading the panel back. That read is what this does.
        /// </para>
        /// <para>
        /// A descendant counts because <see cref="Focusable.delegatesFocus"/> makes a container
        /// hand focus to a child, and a caller focusing the container got what it asked for. The
        /// test is <see cref="IsWithin"/> rather than
        /// <see cref="VisualElement.Contains(VisualElement)"/> precisely because the latter is
        /// strict: measured, <c>element.Contains(element)</c> is false, so the ordinary case of an
        /// element focusing itself would report failure.
        /// </para>
        /// <para>
        /// Caveat worth knowing: a focus requested while an event is being dispatched can be queued
        /// rather than applied, in which case the panel still reports the previous focus and this
        /// returns false. It reports the panel's state at the moment of the call, which is the only
        /// thing that can be reported honestly.
        /// </para>
        /// <para>
        /// Null Handling: returns false for a null element.
        /// Performance: O(1). Allocations: none.
        /// </para>
        /// </remarks>
        public static bool TryFocus(this VisualElement element)
        {
            if (element == null)
            {
                return false;
            }

            element.Focus();
            VisualElement focused = element.FocusedElement();
            return focused.IsWithin(element);
        }
    }
}
