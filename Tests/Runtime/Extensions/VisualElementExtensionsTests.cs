// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Extensions
{
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEngine.UIElements;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class VisualElementExtensionsTests : CommonTestBase
    {
        private static VisualElement Chain(int depth, out List<VisualElement> links)
        {
            links = new List<VisualElement>(depth);
            VisualElement previous = null;
            for (int i = 0; i < depth; ++i)
            {
                VisualElement current = new();
                links.Add(current);
                if (previous != null)
                {
                    previous.Add(current);
                }

                previous = current;
            }

            return links[depth - 1];
        }

        [Test]
        public void IsShownNullElementReturnsFalse()
        {
            VisualElement element = null;
            Assert.IsFalse(element.IsShown());
            Assert.IsFalse(element.IsShownResolved());
        }

        [Test]
        public void IsShownUnstyledElementReturnsTrue()
        {
            VisualElement element = new();
            Assert.IsTrue(element.IsShown());
            Assert.IsTrue(element.IsShownResolved());
        }

        [Test]
        public void IsShownSelfHiddenReturnsFalse()
        {
            VisualElement element = new();
            element.style.display = DisplayStyle.None;
            Assert.IsFalse(element.IsShown());
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void IsShownAnyHiddenAncestorHidesTheLeaf(int hiddenIndex)
        {
            VisualElement leaf = Chain(4, out List<VisualElement> links);
            Assert.IsTrue(leaf.IsShown(), "The unstyled chain should start shown.");

            links[hiddenIndex].style.display = DisplayStyle.None;
            Assert.IsFalse(
                leaf.IsShown(),
                $"Hiding link {hiddenIndex} of 4 should hide the leaf beneath it."
            );

            links[hiddenIndex].style.display = DisplayStyle.Flex;
            Assert.IsTrue(leaf.IsShown(), "Restoring the link should show the leaf again.");
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void IsShownResolvedAnyHiddenAncestorHidesTheLeaf(int hiddenIndex)
        {
            VisualElement leaf = Chain(4, out List<VisualElement> links);
            Assert.IsTrue(leaf.IsShownResolved());

            links[hiddenIndex].style.display = DisplayStyle.None;
            Assert.IsFalse(leaf.IsShownResolved());
        }

        [Test]
        public void IsShownWalksTheContentContainerTheParentChainSkips()
        {
            /*
                ScrollView logical parents skip internal containers; effective display depends on the physical
                hierarchy.
            */
            ScrollView scroll = new();
            VisualElement child = new();
            scroll.Add(child);

            VisualElement contentContainer = child.hierarchy.parent;
            Assert.IsTrue(contentContainer != null);
            Assert.AreNotSame(
                scroll,
                contentContainer,
                "This test is meaningless if the ScrollView parents children directly."
            );
            Assert.AreSame(scroll, child.parent, "The logical parent should skip the container.");

            Assert.IsTrue(child.IsShown());
            contentContainer.style.display = DisplayStyle.None;
            Assert.IsFalse(
                child.IsShown(),
                "A container only the hierarchy chain passes through still hides the child."
            );
        }

        [Test]
        public void IsWithinNullArgumentsReturnFalse()
        {
            VisualElement element = new();
            VisualElement none = null;
            Assert.IsFalse(none.IsWithin(element));
            Assert.IsFalse(element.IsWithin(none));
            Assert.IsFalse(none.IsWithin(none));
        }

        [Test]
        public void IsWithinCountsTheScopeItself()
        {
            VisualElement element = new();
            Assert.IsTrue(
                element.IsWithin(element),
                "Unity's own Contains is strict; IsWithin exists to include the scope."
            );
            Assert.IsFalse(
                element.Contains(element),
                "If this ever becomes true, IsWithin's reason for existing is gone."
            );
        }

        [Test]
        public void IsWithinFindsEveryAncestorAndNoOthers()
        {
            VisualElement leaf = Chain(4, out List<VisualElement> links);
            for (int i = 0; i < links.Count; ++i)
            {
                Assert.IsTrue(leaf.IsWithin(links[i]), $"Leaf should be within link {i}.");
            }

            VisualElement stranger = new();
            Assert.IsFalse(leaf.IsWithin(stranger));
            Assert.IsFalse(links[0].IsWithin(leaf), "Containment does not run upwards.");
        }

        [Test]
        public void IsWithinCrossesAContentContainer()
        {
            ScrollView scroll = new();
            VisualElement child = new();
            scroll.Add(child);
            Assert.IsTrue(child.IsWithin(scroll));
        }

        [Test]
        public void FocusedElementOffPanelReturnsNull()
        {
            VisualElement element = new();
            VisualElement none = null;
            Assert.IsTrue(none.FocusedElement() == null);
            Assert.IsTrue(
                element.FocusedElement() == null,
                "A detached element belongs to no panel."
            );
        }

        [Test]
        public void TryFocusOffPanelReportsFailureRatherThanThrowing()
        {
            Button button = new();
            VisualElement none = null;
            Assert.IsFalse(none.TryFocus());
            Assert.IsFalse(
                button.TryFocus(),
                "Focus() is silent off-panel; TryFocus is the report it does not give."
            );
        }
    }
}
