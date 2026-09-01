# Utility Components Guide

## TL;DR: Why Use These

Drop-in MonoBehaviour components that solve common game development problems without writing custom scripts. Add them to GameObjects for instant functionality like motion animation, collision forwarding, transform following, and visual state management.

---

## Contents

- [Oscillator](#oscillator): Automatic circular/elliptical motion
- [ChildSpawner](#childspawner): Conditional prefab instantiation
- [CollisionProxy](#collisionproxy): Event-based collision detection
- [CircleLineRenderer](#circlelinerenderer): Visual circle debugging
- [MatchTransform](#matchtransform): Follow another transform
- [SpriteRendererSync](#spriterenderersync): Mirror sprite renderer state
- [SpriteRendererMetadata](#spriterenderermetadata): Stacked visual modifications
- [CenterPointOffset](#centerpointoffset): Define logical center points
- [AnimatorEnumStateMachine](#animatorenumstatemachine): Type-safe animator control
- [CoroutineHandler](#coroutinehandler): Singleton coroutine host
- [StartTracker](#starttracker): Lifecycle tracking
- [MatchColliderToSprite](#matchcollidertosprite): Auto-sync colliders
- [PolygonCollider2DOptimizer](#polygoncollider2doptimizer): Simplify collider shapes

---

<a id="oscillator"></a>

## Oscillator

**What it does:** Automatically moves a GameObject in a circular or elliptical pattern. Think "floating pickup" or "idle hover animation" without animators.

**Problem it solves:** Creating simple repetitive motion (hovering, bobbing, orbiting) usually requires animation curves or custom update loops. Oscillator handles it with three parameters.

### When to Use

✅ **Use for:**

- Floating/hovering UI elements
- Pickup items that gently bob
- Decorative objects with idle motion
- Circular patrol paths
- Simple pendulum motion

❌ **Don't use for:**

- Complex animation sequences (use Animator)
- Physics-based motion (use Rigidbody)
- Player/enemy movement (too rigid)

### How to Use

1. Add `Oscillator` component to any GameObject
2. Configure three parameters:
   - **speed**: Rotation speed (radians per second)
   - **width**: Horizontal amplitude (X-axis movement range)
   - **height**: Vertical amplitude (Y-axis movement range)

<!-- doc-sample: compiles -->

```csharp
using WallstopStudios.UnityHelpers.Utils;

// Via code
Oscillator osc = gameObject.AddComponent<Oscillator>();
osc.speed = 2f;    // Two radians/second
osc.width = 1f;    // ±1 unit horizontally
osc.height = 0.5f; // ±0.5 units vertically
```

### Examples

**Gentle hover (coin pickup):**

```text
speed = 3
width = 0
height = 0.2
```

**Figure-8 motion:**

```text
speed = 2
width = 1
height = 1
```

**Horizontal sway:**

```text
speed = 1
width = 0.5
height = 0
```

### Important Notes

- Updates `transform.localPosition` in Update()
- Motion is relative to the original local position
- Starts from current time offset (unique per instance)
- Zero allocation per frame
- Works in 2D and 3D (only affects X and Y)

---

<a id="childspawner"></a>

## ChildSpawner

**What it does:** Conditionally instantiates prefabs as children based on environment (editor/development/release) with automatic duplicate prevention.

**Problem it solves:** Managing debug overlays, analytics, or development tools that should only exist in certain builds. Handles deduplication across scene loads and DontDestroyOnLoad scenarios.

### When to Use

✅ **Use for:**

- Debug UI overlays (FPS counters, console)
- Analytics managers (only in release builds)
- Development tools (cheat menus, level select)
- Platform-specific managers
- Scene-independent singleton spawners

❌ **Don't use for:**

- Regular gameplay objects (use Instantiate)
- One-time spawns (just call Instantiate)
- Objects that need complex initialization

### How to Use

Add `ChildSpawner` to a GameObject (often on a scene manager or empty GameObject):

**Inspector configuration:**

- **Prefabs**: Always spawned
- **Editor Only Prefabs**: Only in Unity Editor
- **Development Only Prefabs**: Only in Development builds
- **Spawn Method**: When to spawn (Awake/OnEnable/Start)
- **Dont Destroy On Load**: Persist across scenes

```csharp
using WallstopStudios.UnityHelpers.Utils;

// Via code
ChildSpawner spawner = gameObject.AddComponent<ChildSpawner>();
spawner._prefabs = new[] { analyticsPrefab };
spawner._developmentOnlyPrefabs = new[] { debugMenuPrefab };
spawner._spawnMethod = ChildSpawnMethod.Awake;
spawner._dontDestroyOnLoad = true;
```

### Deduplication Behavior

ChildSpawner prevents duplicate instantiation:

```csharp
// Spawns DebugCanvas once
ChildSpawner spawner1 = obj1.AddComponent<ChildSpawner>();
spawner1._prefabs = new[] { debugCanvasPrefab };

// This will NOT spawn a second DebugCanvas (detects existing instance)
ChildSpawner spawner2 = obj2.AddComponent<ChildSpawner>();
spawner2._prefabs = new[] { debugCanvasPrefab };
```

Deduplication uses prefab asset path matching.

### Spawn Methods

- **Awake**: Spawns before anything else (use for foundational systems)
- **OnEnable**: Spawns when a component is enabled (use for dynamic spawning)
- **Start**: Spawns after all Awake calls (use when dependencies are needed)

### DontDestroyOnLoad

When enabled:

- Spawned objects persist across scene loads
- Deduplication works across scene transitions
- Objects aren't destroyed when loading new scenes

Typical use case:

```text
Scene 1: ChildSpawner spawns AnalyticsManager with DontDestroyOnLoad
Scene 2 loads: Same ChildSpawner detects existing AnalyticsManager, doesn't spawn duplicate
```

---

<a id="collisionproxy"></a>

## CollisionProxy

**What it does:** Exposes Unity's 2D collision callbacks as C# events, enabling composition-based collision handling without inheriting from MonoBehaviour.

**Problem it solves:** To receive collision events in Unity, you traditionally override `OnCollisionEnter2D` etc. in a MonoBehaviour subclass. CollisionProxy lets you subscribe to events instead, supporting multiple listeners and decoupled architectures.

### When to Use

✅ **Use for:**

- Composition over inheritance designs
- Multiple systems reacting to the same collision
- Decoupling collision logic from GameObject code
- Testing collision responses
- Dynamic behavior attachment/detachment

❌ **Don't use for:**

- Simple single-handler cases (override is fine)
- 3D collisions (only supports 2D)
- High-frequency collisions (event overhead)

### How to Use

1. Add `CollisionProxy` to GameObject with Collider2D
2. Subscribe to events from other scripts

<!-- doc-sample: compiles -->

```csharp
using WallstopStudios.UnityHelpers.Utils;

CollisionProxy proxy = gameObject.AddComponent<CollisionProxy>();

// Subscribe to enter event
proxy.OnCollisionEnter += HandleCollision;
proxy.OnTriggerEnter += HandleTrigger;

void HandleCollision(Collision2D collision)
{
    Debug.Log($"Hit {collision.gameObject.name}");
}

void HandleTrigger(Collider2D other)
{
    Debug.Log($"Triggered by {other.gameObject.name}");
}

// Cleanup
void OnDestroy()
{
    proxy.OnCollisionEnter -= HandleCollision;
    proxy.OnTriggerEnter -= HandleTrigger;
}
```

### Available Events

**Collision events** (Collision2D parameter):

- `OnCollisionEnter`
- `OnCollisionStay`
- `OnCollisionExit`

**Trigger events** (Collider2D parameter):

- `OnTriggerEnter`
- `OnTriggerStay`
- `OnTriggerExit`

### Multiple Subscribers Example

```csharp
// Health system subscribes
healthSystem.OnDamageTaken += proxy.OnCollisionEnter;

// Sound system subscribes to same event
soundSystem.PlayImpactSound += proxy.OnCollisionEnter;

// Analytics subscribes
analytics.TrackCollision += proxy.OnCollisionEnter;

// All three systems react to the same collision independently
```

---

<a id="circlelinerenderer"></a>

## CircleLineRenderer

**What it does:** Visualizes CircleCollider2D with a dynamically drawn circle using LineRenderer, with randomized appearance for visual variety.

**Problem it solves:** Seeing collision bounds at runtime for debugging, or creating dynamic range indicators (ability ranges, explosion radii) without pre-made sprites.

### When to Use

✅ **Use for:**

- Debug visualization of collision bounds
- Dynamic range indicators (attack range, detection radius)
- Area-of-effect visualization
- Circular UI elements
- Animated selection rings

❌ **Don't use for:**

- Production graphics (performance overhead)
- Static circles (use a sprite)
- Thousands of circles (expensive)

### How to Use

1. Add `CircleLineRenderer` to GameObject with `CircleCollider2D`
2. Component automatically:
   - Adds LineRenderer if not present
   - Syncs circle size to collider radius
   - Randomizes line width for visual variety

<!-- doc-sample: compiles -->

```csharp
using WallstopStudios.UnityHelpers.Utils;

CircleLineRenderer circleVis = gameObject.AddComponent<CircleLineRenderer>();
circleVis.color = Color.red;
circleVis.minLineWidth = 0.05f;
circleVis.maxLineWidth = 0.15f;
circleVis.updateRateSeconds = 0.5f; // Refresh twice per second
```

### Configuration

- **minLineWidth / maxLineWidth**: Random line thickness range
- **numSegments**: Circle smoothness (more segments = smoother, more expensive)
- **baseSegments**: Minimum segments (scaled by radius)
- **updateRateSeconds**: How often to randomize appearance
- **color**: Line color

### Update Rate

Lower values = more frequent randomization = more visual variety but higher CPU cost

```text
0.1f = Very active (10 updates/sec)
0.5f = Moderate (2 updates/sec)
2.0f = Subtle (0.5 updates/sec)
```

---

<a id="matchtransform"></a>

## MatchTransform

**What it does:** Makes one transform follow another with configurable update timing and offset.

**Problem it solves:** Following transforms (UI name plates, camera targets, position constraints) usually require custom scripts. MatchTransform handles it declaratively.

### When to Use

✅ **Use for:**

- UI name plates following 3D objects
- Camera targets
- Object attachments (weapon to hand)
- Position constraints
- Simple parent-child alternatives

❌ **Don't use for:**

- Smooth following (use Vector3.Lerp in Update)
- Physics-based following (use joints/springs)
- Complex multi-axis constraints (use Unity Constraints)

### How to Use

```csharp
using WallstopStudios.UnityHelpers.Utils;

MatchTransform matcher = uiPlate.AddComponent<MatchTransform>();
matcher.toMatch = enemyTransform;
matcher.localOffset = new Vector3(0, 2, 0); // 2 units above target
matcher.mode = MatchTransform.Mode.LateUpdate; // Update after camera
```

### Update Modes

- **Update**: Standard update timing (most common)
- **FixedUpdate**: For physics-synced following
- **LateUpdate**: After all Updates (best for camera followers)
- **Awake**: Set once at startup, then never update
- **Start**: Set once after Awake, then never update

### Local Offset

```csharp
// Offset is added to target position
matcher.localOffset = new Vector3(1, 0, 0); // 1 unit to the right
```

### Self-Matching

If `toMatch` is the same GameObject, applies offset once then disables:

```csharp
matcher.toMatch = transform; // Self-reference
matcher.localOffset = new Vector3(5, 0, 0);
// GameObject moves 5 units right once, then MatchTransform disables itself
```

---

<a id="spriterenderersyncer"></a>
<a id="spriterenderersync"></a>

## SpriteRendererSync

**What it does:** Mirrors one SpriteRenderer's properties (sprite, color, material, sorting) to another, with selective property matching.

**Problem it solves:** Creating shadow sprites, duplicate renderers for effects, or layered rendering often requires manually keeping multiple SpriteRenderers in sync.

### When to Use

✅ **Use for:**

- Shadow sprites (black silhouette following character)
- Duplicate renderers for effects (outlines, glows)
- Mirrored sprites (reflection effects)
- Synchronized sprite swapping
- VFX layers

❌ **Don't use for:**

- Single sprite rendering
- Particle effects (use ParticleSystem)
- Complex multi-layer rendering (use LayeredImage for UI)

### How to Use

```csharp
using WallstopStudios.UnityHelpers.Utils;

// On the "follower" sprite renderer
SpriteRendererSync syncer = shadowRenderer.AddComponent<SpriteRendererSync>();
syncer.toMatch = characterRenderer;
syncer.matchColor = false; // Don't copy color (shadow should be black)
syncer.matchMaterial = true;
syncer.matchSortingLayer = true;
syncer.matchOrderInLayer = true;
```

### Configuration Options

**What to sync:**

- `matchColor`: Copy color tint
- `matchMaterial`: Copy material
- `matchSortingLayer`: Copy sorting layer
- `matchOrderInLayer`: Copy order in layer
- Sprite, flipX, flipY are always copied

**Dynamic source:**

```csharp
// Change what to match at runtime
syncer.DynamicToMatch = () => GetCurrentWeaponRenderer();
```

**Sorting override:**

```csharp
// Override order in layer dynamically
syncer.DynamicSortingOrderOverride = () => characterRenderer.sortingOrder - 1; // Always behind
```

### Update Timing

Syncs in `LateUpdate()` to ensure source renderer has updated first.

### Example: Shadow Effect

```csharp
// Create shadow GameObject
GameObject shadow = new GameObject("Shadow");
shadow.transform.parent = character.transform;
shadow.transform.localPosition = new Vector3(0.2f, -0.2f, 0); // Offset

SpriteRenderer shadowRenderer = shadow.AddComponent<SpriteRenderer>();
SpriteRendererSync syncer = shadow.AddComponent<SpriteRendererSync>();

syncer.toMatch = character.GetComponent<SpriteRenderer>();
syncer.matchColor = false;
shadowRenderer.color = new Color(0, 0, 0, 0.5f); // Semi-transparent black
```

---

<a id="spriterenderermetadata"></a>

## SpriteRendererMetadata

**What it does:** Stack-based color and material management for SpriteRenderers, allowing multiple systems to modify visuals with automatic priority handling and restoration.

**Problem it solves:** When multiple systems want to modify a sprite's color (damage flash, power-up glow, status effect) simultaneously, manually coordinating who "owns" the color is error-prone. This provides push/pop semantics with component-based ownership.

### When to Use

✅ **Use for:**

- Damage flashes (red tint on hit)
- Status effects (poison = green, frozen = blue)
- Power-up visuals (glow effects)
- Multiple overlapping visual modifiers
- Temporary material swaps

❌ **Don't use for:**

- Single, exclusive color changes (just set color directly)
- Animations (use Animator)
- Permanent changes (just set the property)

### How to Use

```csharp
using WallstopStudios.UnityHelpers.Utils;

SpriteRenderer renderer = GetComponent<SpriteRenderer>();
SpriteRendererMetadata metadata = renderer.GetComponent<SpriteRendererMetadata>();
if (metadata == null)
    metadata = renderer.gameObject.AddComponent<SpriteRendererMetadata>();

// Component A pushes red color
metadata.PushColor(this, Color.red);

// Component B pushes blue color (takes precedence)
metadata.PushColor(otherComponent, Color.blue);
// Renderer is now blue

// Component B pops its color
metadata.PopColor(otherComponent);
// Renderer reverts to red (Component A's color)

// Component A pops its color
metadata.PopColor(this);
// Renderer reverts to original color
```

### Stack Operations

**Push/Pop (LIFO - Last In, First Out):**

```csharp
metadata.PushColor(owner, Color.red);    // Add to top of stack
metadata.PopColor(owner);                 // Remove from top (must match owner)
```

**PushBack (add to bottom, lower priority):**

```csharp
metadata.PushBackColor(owner, Color.yellow);  // Added to bottom, doesn't change current color unless stack is empty
```

### Component Ownership

Each color/material is tagged with the Component that pushed it:

```csharp
public class DamageFlash : MonoBehaviour
{
    void OnDamage()
    {
        metadata.PushColor(this, Color.red);
        Invoke(nameof(RemoveFlash), 0.1f);
    }

    void RemoveFlash()
    {
        metadata.PopColor(this); // Only removes if this component owns top of stack
    }
}
```

This prevents Component A from accidentally removing Component B's color.

### Material Stacking

Works identically for materials:

```csharp
metadata.PushMaterial(this, glowMaterial);
// ... later
metadata.PopMaterial(this);
```

### Original State

```csharp
Color original = metadata.OriginalColor;     // Color before any modifications
Color current = metadata.CurrentColor;       // Current top-of-stack color

Material originalMat = metadata.OriginalMaterial;
Material currentMat = metadata.CurrentMaterial;
```

### Important Notes

- Automatically detects and stores original color/material in `Awake()`
- Survives enable/disable cycles
- Priority is determined by push order (last push wins)
- Cleanup happens automatically when a component is destroyed
- If a non-owner tries to pop, the operation is ignored (defensive)

---

<a id="centerpointoffset"></a>

## CenterPointOffset

**What it does:** Defines a logical center point for a GameObject that's separate from the transform pivot, scaled by the object's local scale.

**Problem it solves:** Sprites with off-center pivots (for animation reasons) need a separate "logical center" for gameplay (rotation point, targeting reticle, etc.). This provides that without changing the transform pivot.

### When to Use

✅ **Use for:**

- Sprites with off-center pivots that need gameplay center
- Rotation pivots different from visual pivot
- Targeting reticles
- AI targeting points
- Center-of-mass definitions

❌ **Don't use for:**

- Centered sprites (just use transform.position)
- Complex multi-point definitions
- Physics center of mass (use Rigidbody2D.centerOfMass)

### How to Use

```csharp
using WallstopStudios.UnityHelpers.Utils;

CenterPointOffset centerDef = gameObject.AddComponent<CenterPointOffset>();
centerDef.offset = new Vector2(0, 0.5f); // Center is 0.5 units above transform
centerDef.spriteUsesOffset = true; // Flag for sprite-specific logic

// Get world-space center point
Vector2 centerInWorld = centerDef.CenterPoint;

// Use for targeting
targetingSystem.AimAt(centerDef.CenterPoint);
```

### Offset Scaling

Offset is multiplied by `transform.localScale`:

```text
transform.position = (0, 0)
offset = (1, 0)
transform.localScale = (2, 2, 2)

CenterPoint = (0, 0) + (1, 0) * (2, 2) = (2, 0)
```

This ensures the center point scales with the object.

### Sprite Flag

`spriteUsesOffset` is a boolean flag you can check in other systems:

```csharp
if (center.spriteUsesOffset)
{
    // Apply sprite-specific logic
}
```

---

<a id="animatorenumstatemachine"></a>

## AnimatorEnumStateMachine

**What it does:** Type-safe, enum-based Animator state control. Maps enum values to Animator boolean parameters for exclusive state control.

**Problem it solves:** Setting Animator bools with magic strings (`animator.SetBool("IsJumping", true)`) is error-prone and hard to refactor. This provides compile-time safety and automatic cleanup of previous states.

### When to Use

✅ **Use for:**

- Complex state machines (player states, enemy AI)
- Type-safe animation control
- State pattern implementations
- Refactor-friendly animation code

❌ **Don't use for:**

- Simple trigger-based animations (use animator.SetTrigger)
- Float/int parameters (only supports bools)
- Blend trees (use animator.SetFloat)

### How to Use

**1. Define an enum matching your Animator parameters:**

<!-- doc-sample: compiles -->

```csharp
public enum PlayerState
{
    Idle,    // Maps to Animator bool "Idle"
    Running, // Maps to Animator bool "Running"
    Jumping, // Maps to Animator bool "Jumping"
    Falling  // Maps to Animator bool "Falling"
}
```

**2. Create the state machine:**

```csharp
using WallstopStudios.UnityHelpers.Utils;

Animator animator = GetComponent<Animator>();
AnimatorEnumStateMachine<PlayerState> stateMachine;

void Awake()
{
    stateMachine = new AnimatorEnumStateMachine<PlayerState>(animator, PlayerState.Idle);
}
```

**3. Set state:**

```csharp
void Jump()
{
    stateMachine.Value = PlayerState.Jumping;
    // Automatically sets Animator bools:
    //   Idle = false
    //   Running = false
    //   Jumping = true
    //   Falling = false
}
```

### Automatic State Management

Setting `stateMachine.Value` automatically:

1. Sets ALL enum-named bools to `false`
2. Sets ONLY the matching bool to `true`

This ensures exclusive state control (only one state active).

### Animator Setup

Your Animator needs bool parameters matching enum names:

```text
Animator parameters:
- Idle (bool)
- Running (bool)
- Jumping (bool)
- Falling (bool)

Transitions:
- Any State → Idle: Idle == true
- Any State → Running: Running == true
- Any State → Jumping: Jumping == true
- Any State → Falling: Falling == true
```

### Serialization

`AnimatorEnumStateMachine<T>` is serializable for debugging in Inspector.

---

<a id="coroutinehandler"></a>

## CoroutineHandler

**What it does:** Singleton MonoBehaviour that provides a global coroutine host for non-MonoBehaviour classes.

**Problem it solves:** Coroutines require a MonoBehaviour to start. Static classes, plain C# objects, and ScriptableObjects can't start coroutines directly.

### When to Use

✅ **Use for:**

- Starting coroutines from static utility classes
- Coroutines in plain C# objects
- ScriptableObjects that need coroutines
- Global/scene-independent coroutines

❌ **Don't use for:**

- MonoBehaviours (just use StartCoroutine)
- Short-lived coroutines (might outlive the object)
- Frame-perfect timing (singleton has overhead)

### How to Use

```csharp
using WallstopStudios.UnityHelpers.Utils;

// From anywhere
CoroutineHandler.Instance.StartCoroutine(MyCoroutine());

IEnumerator MyCoroutine()
{
    yield return new WaitForSeconds(1f);
    Debug.Log("Done!");
}
```

### Lifetime

CoroutineHandler persists across scene loads (`DontDestroyOnLoad`), so coroutines survive scene transitions.

### Stopping Coroutines

```csharp
Coroutine routine = CoroutineHandler.Instance.StartCoroutine(MyCoroutine());
// ... later
CoroutineHandler.Instance.StopCoroutine(routine);
```

---

<a id="starttracker"></a>

## StartTracker

**What it does:** Simple component that tracks whether `MonoBehaviour.Start()` has been called.

**Problem it solves:** Sometimes you need to know if initialization (Start) has completed, especially in the editor or during complex initialization orders.

### When to Use

✅ **Use for:**

- Initialization order checking
- Conditional setup logic
- Editor tools validating scene state
- Testing initialization

❌ **Don't use for:**

- Production gameplay logic (architectural smell)
- Most scenarios (rethink if you need this)

### How to Use

<!-- doc-sample: compiles -->

```csharp
using WallstopStudios.UnityHelpers.Utils;

// Add to GameObject
StartTracker tracker = gameObject.AddComponent<StartTracker>();

// Later, check if Start has been called
if (tracker.Started)
{
    // Initialization complete
}
```

---

<a id="matchcollidertosprite"></a>

## MatchColliderToSprite

Automatically syncs `PolygonCollider2D` shape to sprite's physics shape.

**See:** [Editor Tools Guide - MatchColliderToSprite](../editor-tools/editor-tools-guide.md#matchcollidertosprite-editor)

---

<a id="polygoncollider2doptimizer"></a>

## PolygonCollider2DOptimizer

Reduces PolygonCollider2D point count using Douglas-Peucker simplification.

**See:** [Editor Tools Guide - PolygonCollider2DOptimizer](../editor-tools/editor-tools-guide.md#polygoncollider2doptimizer-editor)

---

## Best Practices

### General

- **One utility per GameObject**: Don't stack unrelated utilities on the same GameObject
- **Configure in Awake/Start**: Set properties before first Update
- **Remove when done**: Disable/destroy utilities that are no longer needed
- **Test in builds**: Some utilities behave differently in editor vs. builds (ChildSpawner)

### Performance

- **CircleLineRenderer**: Use sparingly, each instance updates line vertices
- **SpriteRendererSync**: Updates every LateUpdate, don't use for hundreds of sprites
- **MatchTransform**: Choose an appropriate update mode (FixedUpdate for physics, LateUpdate for camera)

### Architecture

- **CollisionProxy**: Great for composition, but don't overuse events everywhere
- **SpriteRendererMetadata**: Document ownership in team code (who can push/pop)
- **AnimatorEnumStateMachine**: Keep enum names matching Animator parameters

---

## Related Documentation

- [Math & Extensions](../utilities/math-and-extensions.md) - Extension methods used by utilities
- [Editor Tools Guide](../editor-tools/editor-tools-guide.md) - Editor components
- [Helpers Guide](../utilities/helper-utilities.md) - Non-component helper classes
