# use-relational-attributes - Part 2

## Split Content

## DI Framework Integration

Relational attributes work alongside dependency injection frameworks. Use them for hierarchy-based component references while DI handles service injection:

```csharp
using UnityEngine;
using WallstopStudios.UnityHelpers.Core.Attributes;
using VContainer;  // or Zenject, Reflex

public class Enemy : MonoBehaviour
{
    // Injected by DI framework
    [Inject] private IGameManager _gameManager;
    [Inject] private IPoolService _poolService;

    // Found via hierarchy
    [SiblingComponent] private Animator _animator;
    [ChildComponent(OnlyDescendants = true)] private Collider2D[] _hitboxes;
    [ParentComponent(OnlyAncestors = true, MaxDepth = 1)] private Transform _parentContainer;

    private void Awake()
    {
        // Called after DI injection
        this.AssignRelationalComponents();
    }
}
```

---

## Complete Example

```csharp
using System.Collections.Generic;
using UnityEngine;
using WallstopStudios.UnityHelpers.Core.Attributes;

public class CharacterController : MonoBehaviour
{
    // --- Sibling Components (same GameObject) ---
    [SiblingComponent]
    private Animator animator;

    [SiblingComponent]
    private Rigidbody2D rb;

    [SiblingComponent(Optional = true)]
    private AudioSource audioSource;

    // --- Parent Components (up the hierarchy) ---
    [ParentComponent(OnlyAncestors = true, MaxDepth = 1)]
    private Transform parentContainer;

    [ParentComponent(OnlyAncestors = true, TagFilter = "Manager")]
    private Component managerComponent;

    // --- Child Components (down the hierarchy) ---
    [ChildComponent(OnlyDescendants = true, MaxDepth = 1)]
    private SpriteRenderer[] immediateChildSprites;

    [ChildComponent(OnlyDescendants = true, NameFilter = "Hitbox")]
    private List<Collider2D> hitboxColliders;

    [ChildComponent(OnlyDescendants = true, TagFilter = "VFX", IncludeInactive = true)]
    private HashSet<ParticleSystem> vfxSystems;

    [ChildComponent(OnlyDescendants = true, MaxCount = 5)]
    private Transform[] firstFiveChildren;

    private void Awake()
    {
        // Wire up all relational fields
        this.AssignRelationalComponents();

        // Now use the components
        animator.Play("Idle");
        rb.gravityScale = 1f;

        foreach (Collider2D hitbox in hitboxColliders)
        {
            hitbox.enabled = true;
        }
    }
}
```

---

## Filter Behavior

- **TagFilter** and **NameFilter** can be combined – both must match (AND logic)
- When `IncludeInactive = false`, inactive components are filtered out _before_ tag/name filters
- **MaxCount** is applied last, after all other filters
- **NameFilter** is case-sensitive substring match
- **TagFilter** uses `GameObject.CompareTag()` for efficient exact matching

---

## Notes

- Fields are populated at **runtime**, not during Unity serialization
- Call assignment methods in `Awake()` or `OnEnable()` before dependent code runs
- Child search is **breadth-first** – closer descendants found before distant ones
- For performance-critical scenarios, consider using `RelationalComponentInitializer.Initialize()` during loading to pre-cache reflection data
