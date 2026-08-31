// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.DataStructures
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.DataStructure;
    using WallstopStudios.UnityHelpers.Tests.TestUtils;

    /// <summary>
    /// The R-trees are the only structures whose elements have extent, so they are the only place
    /// <c>GetElementsInBounds</c> can mean two things. It means one: an element is returned when its
    /// box touches the query box. <c>RTree3D</c> used to filter by the element's center instead, so
    /// a system ported from 2D to 3D silently stopped seeing straddling elements
    /// (<see href="https://github.com/Ambiguous-Interactive/unity-helpers/issues/658">#658</see>).
    /// Both families run the same corpus and the same queries here, so they cannot drift apart
    /// again.
    /// </summary>
    /// <remarks>
    /// Every coordinate sits on a half-unit grid. <c>RTree3D</c> stores element boxes as
    /// <see cref="BoundingBox3D"/>, whose constructor widens a degenerate axis by 1e-6 to keep it
    /// non-empty, so an element within an ULP-scale band of a query face answers differently there
    /// than in the closed-interval 2D path. That band is a separate question from this one, and
    /// staying off it is what keeps this suite about the semantics it names.
    /// </remarks>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class RTreeBoundsSemanticsTests
    {
        [Test]
        public void BoundsQueriesReturnEveryTouchingElement()
        {
            Bounds[] extents = Extents();
            List<int> elements = Elements(extents.Length);
            RTree2D<int> tree2D = new(elements, index => extents[index]);
            RTree3D<int> tree3D = new(elements, index => extents[index]);

            foreach (BoxQuery query in Queries())
            {
                Bounds bounds = query.ToBounds();
                List<int> actual2D = new() { Sentinel };
                List<int> actual3D = new() { Sentinel };

                tree2D.GetElementsInBounds(bounds, actual2D);
                tree3D.GetElementsInBounds(bounds, actual3D);

                CollectionAssert.AreEquivalent(
                    SpatialQueryOracle.TouchingBox2D(extents, query.minimum, query.maximum),
                    actual2D,
                    "RTree2D / {0}",
                    query.name
                );
                CollectionAssert.AreEquivalent(
                    SpatialQueryOracle.TouchingBox3D(extents, query.minimum, query.maximum),
                    actual3D,
                    "RTree3D / {0}",
                    query.name
                );
            }
        }

        [Test]
        public void CenterQueriesReturnEveryCenteredElement()
        {
            Bounds[] extents = Extents();
            List<int> elements = Elements(extents.Length);
            RTree2D<int> tree2D = new(elements, index => extents[index]);
            RTree3D<int> tree3D = new(elements, index => extents[index]);
            SpatialQueryOracle.Sample[] centers = Centers(extents);

            foreach (BoxQuery query in Queries())
            {
                Bounds bounds = query.ToBounds();
                List<int> actual2D = new() { Sentinel };
                List<int> actual3D = new() { Sentinel };

                tree2D.GetElementsWithCentersInBounds(bounds, actual2D);
                tree3D.GetElementsWithCentersInBounds(bounds, actual3D);

                CollectionAssert.AreEquivalent(
                    SpatialQueryOracle.InsideBox2D(centers, query.minimum, query.maximum),
                    actual2D,
                    "RTree2D / {0}",
                    query.name
                );
                CollectionAssert.AreEquivalent(
                    SpatialQueryOracle.InsideBox3D(centers, query.minimum, query.maximum),
                    actual3D,
                    "RTree3D / {0}",
                    query.name
                );
            }
        }

        /// <summary>
        /// The regression pin. <see cref="StraddlingElement"/> reaches into the query box while its
        /// center stays outside, which is exactly the shape the two families used to answer
        /// differently.
        /// </summary>
        [Test]
        public void BothFamiliesReturnAStraddlingElementFromABoundsQuery()
        {
            Bounds[] extents = Extents();
            List<int> elements = Elements(extents.Length);
            RTree2D<int> tree2D = new(elements, index => extents[index]);
            RTree3D<int> tree3D = new(elements, index => extents[index]);
            Bounds bounds = StraddlingQuery.ToBounds();

            List<int> touching2D = new();
            List<int> touching3D = new();
            List<int> centered2D = new();
            List<int> centered3D = new();

            tree2D.GetElementsInBounds(bounds, touching2D);
            tree3D.GetElementsInBounds(bounds, touching3D);
            tree2D.GetElementsWithCentersInBounds(bounds, centered2D);
            tree3D.GetElementsWithCentersInBounds(bounds, centered3D);

            CollectionAssert.Contains(touching2D, StraddlingElement, "RTree2D bounds query");
            CollectionAssert.Contains(touching3D, StraddlingElement, "RTree3D bounds query");
            CollectionAssert.DoesNotContain(centered2D, StraddlingElement, "RTree2D center query");
            CollectionAssert.DoesNotContain(centered3D, StraddlingElement, "RTree3D center query");
            CollectionAssert.AreEquivalent(touching2D, touching3D, "The families disagree");
        }

        [Test]
        public void CenterQueriesRejectANullDestination()
        {
            Bounds[] extents = Extents();
            List<int> elements = Elements(extents.Length);
            RTree2D<int> tree2D = new(elements, index => extents[index]);
            RTree3D<int> tree3D = new(elements, index => extents[index]);
            Bounds bounds = StraddlingQuery.ToBounds();

            Assert.Throws<ArgumentNullException>(() =>
                tree2D.GetElementsWithCentersInBounds(bounds, null)
            );
            Assert.Throws<ArgumentNullException>(() =>
                tree3D.GetElementsWithCentersInBounds(bounds, null)
            );
        }

        private const int Sentinel = -1;
        private const int StraddlingElement = 1;

        private static BoxQuery StraddlingQuery =>
            new("straddling", new Vector3(2.5f, -0.5f, -0.5f), new Vector3(3.5f, 0.5f, 0.5f));

        private static Bounds[] Extents()
        {
            return new[]
            {
                new Bounds(Vector3.zero, new Vector3(2f, 2f, 2f)),
                new Bounds(new Vector3(2f, 0f, 0f), new Vector3(2f, 2f, 2f)),
                new Bounds(new Vector3(4f, 0f, 0f), new Vector3(2f, 2f, 2f)),
                new Bounds(new Vector3(0f, 4f, 0f), new Vector3(4f, 4f, 4f)),
                new Bounds(new Vector3(6f, 6f, 6f), Vector3.zero),
                new Bounds(Vector3.zero, Vector3.zero),
                new Bounds(Vector3.zero, new Vector3(2f, 2f, 2f)),
                new Bounds(new Vector3(-4f, -4f, -4f), new Vector3(2f, 2f, 2f)),
            };
        }

        private static List<int> Elements(int count)
        {
            List<int> elements = new(count);
            for (int i = 0; i < count; ++i)
            {
                elements.Add(i);
            }

            return elements;
        }

        private static SpatialQueryOracle.Sample[] Centers(Bounds[] extents)
        {
            SpatialQueryOracle.Sample[] centers = new SpatialQueryOracle.Sample[extents.Length];
            for (int i = 0; i < extents.Length; ++i)
            {
                centers[i] = new SpatialQueryOracle.Sample(extents[i].center, i, i);
            }

            return centers;
        }

        private static IEnumerable<BoxQuery> Queries()
        {
            yield return new BoxQuery(
                "unit cube",
                new Vector3(-1f, -1f, -1f),
                new Vector3(1f, 1f, 1f)
            );
            yield return StraddlingQuery;
            yield return new BoxQuery("origin point", Vector3.zero, Vector3.zero);
            yield return new BoxQuery(
                "shared face",
                new Vector3(1f, -0.5f, -0.5f),
                new Vector3(1f, 0.5f, 0.5f)
            );
            yield return new BoxQuery(
                "everything",
                new Vector3(-64f, -64f, -64f),
                new Vector3(64f, 64f, 64f)
            );
            yield return new BoxQuery(
                "elsewhere",
                new Vector3(40f, 40f, 40f),
                new Vector3(50f, 50f, 50f)
            );
            yield return new BoxQuery(
                "z only",
                new Vector3(-0.5f, -0.5f, 8f),
                new Vector3(0.5f, 0.5f, 9f)
            );
            yield return new BoxQuery(
                "nan edge",
                new Vector3(float.NaN, -1f, -1f),
                new Vector3(1f, 1f, 1f)
            );
            yield return new BoxQuery(
                "inverted",
                new Vector3(2f, 2f, 2f),
                new Vector3(-2f, -2f, -2f)
            );
        }

        private readonly struct BoxQuery
        {
            internal readonly string name;
            internal readonly Vector3 minimum;
            internal readonly Vector3 maximum;

            internal BoxQuery(string name, Vector3 minimum, Vector3 maximum)
            {
                this.name = name;
                this.minimum = minimum;
                this.maximum = maximum;
            }

            internal Bounds ToBounds()
            {
                Bounds bounds = default;
                bounds.SetMinMax(minimum, maximum);
                return bounds;
            }
        }
    }
}
