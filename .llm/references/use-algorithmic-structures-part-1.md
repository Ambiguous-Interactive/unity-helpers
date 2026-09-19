# use-algorithmic-structures - Part 1

## Split Content

**Trigger**: When implementing connectivity queries, string prefix operations, dense boolean flags, or time-based caching.

---

## When to Use This Skill

- Checking connectivity between elements (islands, clusters, networks)
- Implementing autocomplete or command prefix matching
- Managing dense boolean state flags efficiently
- Caching expensive computations with expiration

---

## Available Structures

| Structure         | Best For                                      |
| ----------------- | --------------------------------------------- |
| `DisjointSet`     | Union-find, connectivity, clustering          |
| `Trie`            | Prefix search, autocomplete, command matching |
| `TimedCache<T>`   | Expiring cached computations                  |
| `BitSet`          | Dense boolean flags, state masks, layer flags |
| `ImmutableBitSet` | Read-only bit operations                      |

---

## DisjointSet

Union-find data structure with path compression and union by rank. Near-constant time O(alpha(n)) operations for connectivity queries.

### API

```csharp
DisjointSet set = new DisjointSet(elementCount);

set.TryFind(x, out int root);           // Find set representative
set.TryUnion(x, y);                     // Merge two sets
set.TryIsConnected(x, y, out bool c);   // Check if same set
set.Count;                              // Total elements
set.SetCount;                           // Number of distinct sets
set.GetSetSize(x, out int size);        // Size of set containing x
set.GetAllSets();                       // Get all sets as lists
```

### Example: Procedural Island Detection

```csharp
public class IslandDetector
{
    public int CountIslands(bool[,] grid)
    {
        int width = grid.GetLength(0);
        int height = grid.GetLength(1);
        DisjointSet islands = new DisjointSet(width * height);

        int ToIndex(int x, int y) => y * width + x;

        // Connect adjacent land cells
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (!grid[x, y]) continue;

                int current = ToIndex(x, y);

                // Connect to right neighbor
                if (x + 1 < width && grid[x + 1, y])
                    islands.TryUnion(current, ToIndex(x + 1, y));

                // Connect to bottom neighbor
                if (y + 1 < height && grid[x, y + 1])
                    islands.TryUnion(current, ToIndex(x, y + 1));
            }
        }

        // Count unique land regions
        HashSet<int> uniqueRoots = new HashSet<int>();
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (grid[x, y] && islands.TryFind(ToIndex(x, y), out int root))
                {
                    uniqueRoots.Add(root);
                }
            }
        }

        return uniqueRoots.Count;
    }
}
```

### Example: Dynamic Connectivity

```csharp
public class NetworkConnectivity
{
    private readonly DisjointSet _nodes;

    public NetworkConnectivity(int nodeCount)
    {
        _nodes = new DisjointSet(nodeCount);
    }

    public void Connect(int nodeA, int nodeB)
    {
        _nodes.TryUnion(nodeA, nodeB);
    }

    public bool AreConnected(int nodeA, int nodeB)
    {
        return _nodes.TryIsConnected(nodeA, nodeB, out bool connected) && connected;
    }

    public int GetNetworkCount() => _nodes.SetCount;

    public int GetNetworkSize(int node)
    {
        return _nodes.GetSetSize(node, out int size) ? size : 0;
    }
}
```

---
