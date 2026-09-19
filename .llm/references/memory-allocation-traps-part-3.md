# memory-allocation-traps - Part 3

## Split Content

## Trap 10: Unity API Array Properties

Many Unity properties return new arrays on each access:

```csharp
// ❌ TERRIBLE: 4 array copies per iteration!
for (int i = 0; i < mesh.vertices.Length; i++)
{
    float x = mesh.vertices[i].x;  // New array!
    float y = mesh.vertices[i].y;  // New array!
    float z = mesh.vertices[i].z;  // New array!
}

// ✅ BETTER: Cache array
var vertices = mesh.vertices;  // Single copy
for (int i = 0; i < vertices.Length; i++)
{
    Process(vertices[i]);
}

// ✅ BEST: Non-allocating API
private List<Vector3> _vertices = new List<Vector3>();
mesh.GetVertices(_vertices);  // No allocation!
```

### Array-Returning Properties to Avoid

| Property                   | Non-Allocating Alternative                             |
| -------------------------- | ------------------------------------------------------ |
| `mesh.vertices`            | `mesh.GetVertices(list)`                               |
| `mesh.normals`             | `mesh.GetNormals(list)`                                |
| `mesh.uv`                  | `mesh.GetUVs(channel, list)`                           |
| `mesh.triangles`           | `mesh.GetTriangles(list, submesh)`                     |
| `Input.touches`            | `Input.touchCount` + `Input.GetTouch(i)`               |
| `Animator.parameters`      | `Animator.parameterCount` + `Animator.GetParameter(i)` |
| `Renderer.sharedMaterials` | `Renderer.GetSharedMaterials(list)`                    |

---

## Detection: Finding Hidden Allocations

### Unity Profiler

1. Enable **Deep Profile** for detailed call stacks
2. Sort by **GC Alloc** column
3. Look for allocations in `Update`, `FixedUpdate`, `LateUpdate`

### Search Patterns (Regex)

```regex
# LINQ usage
\.Where\(|\.Select\(|\.Any\(|\.First\(|\.ToList\(|\.ToArray\(

# Collection creation
new List<|new Dictionary<|new HashSet<|new Queue<|new Stack<

# String operations in loops
\+\s*"|\+\s*\w+\.ToString\(\)

# foreach on collections
foreach.*List<|foreach.*Dictionary<|foreach.*HashSet<

# Potential closures (lambdas with external references)
=>\s*[^;]*[a-z_][a-zA-Z0-9_]*[^(]
```

---

## Related Skills

- [high-performance-csharp](../skills/high-performance-csharp.md) — Zero-allocation patterns
- [gc-architecture-unity](../skills/gc-architecture-unity.md) — Why allocations matter
- [refactor-to-zero-alloc](../skills/refactor-to-zero-alloc.md) — Migration patterns
- [unity-performance-patterns](../skills/unity-performance-patterns.md) — Unity-specific patterns
- [performance-audit](../skills/performance-audit.md) — Audit checklist
