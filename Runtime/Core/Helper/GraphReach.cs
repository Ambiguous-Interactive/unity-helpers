// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Helper
{
    using System;
    using WallstopStudios.UnityHelpers.Core.DataStructure;
    using WallstopStudios.UnityHelpers.Utils;

    /// <summary>Finds directed graph nodes reachable from a root.</summary>
    public static class GraphReach
    {
        /// <summary>Replaces the result with reachable nodes, returning false for invalid input.</summary>
        /// <remarks>The root reaches itself. Invalid input clears the result. Uses O(V + E) pooled scratch; warm calls allocate no garbage.</remarks>
        public static bool Reachable(
            int nodeCount,
            ReadOnlySpan<(int from, int to)> edges,
            int root,
            BitSet reached
        )
        {
            if (reached == null)
            {
                return false;
            }
            reached.ClearAll();
            if (!IsValid(nodeCount, edges, root))
            {
                return false;
            }
            int edgeCount = edges.Length;
            using PooledArray<int> headsLease = SystemArrayPool<int>.Get(
                nodeCount,
                out int[] heads
            );
            using PooledArray<int> nextLease = SystemArrayPool<int>.Get(
                edges.Length,
                out int[] next
            );
            using PooledArray<int> queueLease = SystemArrayPool<int>.Get(
                nodeCount,
                out int[] queue
            );
            heads.AsSpan(0, nodeCount).Fill(-1);
            for (int i = 0; i < edgeCount; ++i)
            {
                int from = edges[i].from;
                next[i] = heads[from];
                heads[from] = i;
            }
            reached.EnsureCapacity(nodeCount);
            reached.TrySet(root);
            queue[0] = root;
            int read = 0;
            int write = 1;
            while (read < write)
            {
                int node = queue[read];
                ++read;
                for (int edge = heads[node]; 0 <= edge; edge = next[edge])
                {
                    int target = edges[edge].to;
                    if (reached.TryGet(target, out bool visited) && visited)
                    {
                        continue;
                    }
                    reached.TrySet(target);
                    queue[write] = target;
                    ++write;
                }
            }
            return true;
        }

        /// <summary>Returns a reachable-node mask for up to 64 nodes, or zero for invalid input.</summary>
        public static ulong Reachable(
            int nodeCount,
            ReadOnlySpan<(int from, int to)> edges,
            int root
        )
        {
            if (64 < nodeCount || !IsValid(nodeCount, edges, root))
            {
                return 0;
            }
            Span<ulong> adjacency = stackalloc ulong[64];
            adjacency.Clear();
            foreach ((int from, int to) edge in edges)
            {
                adjacency[edge.from] |= 1UL << edge.to;
            }
            ulong reached = 1UL << root;
            ulong pending = reached;
            while (pending != 0)
            {
                int node = BitOps.TrailingZeroCount(pending);
                pending &= pending - 1;
                ulong discovered = adjacency[node] & ~reached;
                reached |= discovered;
                pending |= discovered;
            }
            return reached;
        }

        private static bool IsValid(int nodeCount, ReadOnlySpan<(int from, int to)> edges, int root)
        {
            if (nodeCount <= 0 || root < 0 || nodeCount <= root)
            {
                return false;
            }
            foreach ((int from, int to) edge in edges)
            {
                if (edge.from < 0 || nodeCount <= edge.from || edge.to < 0 || nodeCount <= edge.to)
                {
                    return false;
                }
            }
            return true;
        }
    }
}
