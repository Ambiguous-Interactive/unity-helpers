// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Utils
{
    using System.Collections;
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Utils;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class PolygonCollider2DOptimizerTests : CommonTestBase
    {
        [UnityTest]
        public IEnumerator SimplifiesAndResetsPaths()
        {
            GameObject go = Track(
                new GameObject(
                    "Poly",
                    typeof(PolygonCollider2D),
                    typeof(PolygonCollider2DOptimizer)
                )
            );
            PolygonCollider2D collider = go.GetComponent<PolygonCollider2D>();
            PolygonCollider2DOptimizer optimizer = go.GetComponent<PolygonCollider2DOptimizer>();

            List<Vector2> path = new()
            {
                new(0, 0),
                new(1, 0),
                new(2, 0),
                new(2, 1),
                new(0, 1),
                new(0, 0),
            };
            collider.SetPath(0, path.ToArray());

            optimizer.tolerance = 0.5;
            optimizer.Refresh();
            yield return null;

            int simplifiedCount = collider.GetPath(0).Length;
            Assert.Less(simplifiedCount, path.Count);

            optimizer.tolerance = 0;
            optimizer.Refresh();
            yield return null;

            int resetCount = collider.GetPath(0).Length;
            Assert.AreEqual(path.Count, resetCount);
        }
    }
}
