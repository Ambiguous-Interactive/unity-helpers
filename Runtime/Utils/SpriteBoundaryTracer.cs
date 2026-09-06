// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Utils
{
    using System;
    using System.Collections.Generic;
    using UnityEngine;

    /// <summary>Traces exact pixel-corner contours with eight-connected opaque regions.</summary>
    public static class SpriteBoundaryTracer
    {
        /// <summary>Builds collinear-merged sprite-local contours, discarding paths below a pixel area.</summary>
        /// <param name="mask">Caller-owned pixels and their coordinate mapping.</param>
        /// <param name="paths">New contours, without duplicate closing points; empty on failure.</param>
        /// <param name="minimumArea">A finite nonnegative area in mask pixels, applied to islands and holes.</param>
        /// <returns>Whether the mask and settings were valid, including an entirely transparent mask.</returns>
        /// <remarks>Outer contours run counterclockwise, holes clockwise; diagonal opaque pixels share a corner.</remarks>
        public static bool TryTrace(
            SpriteAlphaMask mask,
            out List<Vector2[]> paths,
            float minimumArea = 0
        )
        {
            List<Vector2[]> tracedPaths = new();
            if (
                mask.Pixels == null
                || mask.Width <= 0
                || mask.Height <= 0
                || (long)mask.Width * mask.Height != mask.Pixels.Length
                || !IsFinite(mask.Pivot.x)
                || !IsFinite(mask.Pivot.y)
                || !IsFinite(mask.UnitsPerPixel.x)
                || !IsFinite(mask.UnitsPerPixel.y)
                || mask.UnitsPerPixel.x <= 0
                || mask.UnitsPerPixel.y <= 0
                || !IsFinite(minimumArea)
                || minimumArea < 0
            )
            {
                paths = tracedPaths;
                return false;
            }

            using PooledResource<List<Edge>> edgeLease = Buffers<Edge>.List.Get(
                out List<Edge> edges
            );
            using PooledResource<Dictionary<Vector2Int, EdgePair>> outgoingLease = DictionaryBuffer<
                Vector2Int,
                EdgePair
            >.Dictionary.Get(out Dictionary<Vector2Int, EdgePair> outgoing);
            for (int y = 0; y < mask.Height; ++y)
            {
                for (int x = 0; x < mask.Width; ++x)
                {
                    if (!mask.Pixels[y * mask.Width + x])
                    {
                        continue;
                    }
                    if (y == 0 || !mask.Pixels[(y - 1) * mask.Width + x])
                    {
                        AddEdge(new Vector2Int(x, y), new Vector2Int(x + 1, y), 0, edges, outgoing);
                    }
                    if (x == mask.Width - 1 || !mask.Pixels[y * mask.Width + x + 1])
                    {
                        AddEdge(
                            new Vector2Int(x + 1, y),
                            new Vector2Int(x + 1, y + 1),
                            1,
                            edges,
                            outgoing
                        );
                    }
                    if (y == mask.Height - 1 || !mask.Pixels[(y + 1) * mask.Width + x])
                    {
                        AddEdge(
                            new Vector2Int(x + 1, y + 1),
                            new Vector2Int(x, y + 1),
                            2,
                            edges,
                            outgoing
                        );
                    }
                    if (x == 0 || !mask.Pixels[y * mask.Width + x - 1])
                    {
                        AddEdge(new Vector2Int(x, y + 1), new Vector2Int(x, y), 3, edges, outgoing);
                    }
                }
            }

            using PooledArray<bool> visitedLease = SystemArrayPool<bool>.Get(
                edges.Count,
                out bool[] visited
            );
            Array.Clear(visited, 0, edges.Count);
            using PooledResource<List<Vector2Int>> cornerLease = Buffers<Vector2Int>.List.Get(
                out List<Vector2Int> corners
            );
            for (int start = 0; start < edges.Count; ++start)
            {
                if (visited[start])
                {
                    continue;
                }
                corners.Clear();
                int current = start;
                double twiceArea = 0;
                do
                {
                    Edge edge = edges[current];
                    visited[current] = true;
                    twiceArea += (double)edge.From.x * edge.To.y - (double)edge.To.x * edge.From.y;
                    if (!outgoing.TryGetValue(edge.To, out EdgePair candidates))
                    {
                        tracedPaths.Clear();
                        paths = tracedPaths;
                        return false;
                    }
                    int next = candidates.First;
                    if (0 <= candidates.Second)
                    {
                        int clockwise = (edge.Direction + 3) % 4;
                        next =
                            edges[candidates.First].Direction == clockwise
                                ? candidates.First
                                : candidates.Second;
                    }
                    if (edges[next].Direction != edge.Direction)
                    {
                        corners.Add(edge.To);
                    }
                    current = next;
                    if (visited[current] && current != start)
                    {
                        tracedPaths.Clear();
                        paths = tracedPaths;
                        return false;
                    }
                } while (current != start);

                if (corners.Count < 3 || Math.Abs(twiceArea) * 0.5 < minimumArea)
                {
                    continue;
                }
                Vector2[] path = new Vector2[corners.Count];
                for (int index = 0; index < corners.Count; ++index)
                {
                    Vector2Int corner = corners[index];
                    Vector2 point = new(
                        (corner.x - mask.Pivot.x) * mask.UnitsPerPixel.x,
                        (corner.y - mask.Pivot.y) * mask.UnitsPerPixel.y
                    );
                    if (!IsFinite(point.x) || !IsFinite(point.y))
                    {
                        tracedPaths.Clear();
                        paths = tracedPaths;
                        return false;
                    }
                    path[index] = point;
                }
                Vector2 previous = path[path.Length - 1];
                foreach (Vector2 point in path)
                {
                    if (point.x == previous.x && point.y == previous.y)
                    {
                        tracedPaths.Clear();
                        paths = tracedPaths;
                        return false;
                    }
                    previous = point;
                }
                tracedPaths.Add(path);
            }
            paths = tracedPaths;
            return true;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static void AddEdge(
            Vector2Int from,
            Vector2Int to,
            int direction,
            List<Edge> edges,
            Dictionary<Vector2Int, EdgePair> outgoing
        )
        {
            int index = edges.Count;
            edges.Add(new Edge(from, to, direction));
            if (outgoing.TryGetValue(from, out EdgePair existing))
            {
                outgoing[from] = new EdgePair(existing.First, index);
            }
            else
            {
                outgoing.Add(from, new EdgePair(index, -1));
            }
        }

        private readonly struct Edge
        {
            internal readonly Vector2Int From;
            internal readonly Vector2Int To;
            internal readonly int Direction;

            internal Edge(Vector2Int from, Vector2Int to, int direction)
            {
                From = from;
                To = to;
                Direction = direction;
            }
        }

        private readonly struct EdgePair
        {
            internal readonly int First;
            internal readonly int Second;

            internal EdgePair(int first, int second)
            {
                First = first;
                Second = second;
            }
        }
    }
}
