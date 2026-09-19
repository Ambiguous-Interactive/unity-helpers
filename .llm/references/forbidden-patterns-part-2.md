# Forbidden Patterns Part 2

## Unity-Specific Patterns

### Component Access

| Forbidden                             | Use Instead                     | Reason                           |
| ------------------------------------- | ------------------------------- | -------------------------------- |
| `GetComponent<T>()` in `Update()`     | Cache in `Awake()`              | Expensive lookup every frame     |
| `Camera.main` in `Update()`           | Cache reference in `Awake()`    | Performs `FindGameObjectWithTag` |
| `FindObjectOfType<T>()` in `Update()` | Cache in `Awake()`              | Scans entire scene               |
| `transform` property repeatedly       | Cache `_transform` in `Awake()` | Property access overhead         |

### Array-Returning Properties

| Forbidden                   | Use Instead                                   | Reason                             |
| --------------------------- | --------------------------------------------- | ---------------------------------- |
| `mesh.vertices` repeatedly  | `mesh.GetVertices(list)`                      | Creates new array copy each access |
| `mesh.normals` repeatedly   | `mesh.GetNormals(list)`                       | Creates new array copy each access |
| `mesh.uv` repeatedly        | `mesh.GetUVs(channel, list)`                  | Creates new array copy each access |
| `mesh.triangles` repeatedly | `mesh.GetTriangles(list, submesh)`            | Creates new array copy each access |
| `Input.touches`             | `Input.touchCount` + `Input.GetTouch(i)`      | Creates new array each access      |
| `Animator.parameters`       | `Animator.parameterCount` + `GetParameter(i)` | Creates new array each access      |
| `Renderer.sharedMaterials`  | `Renderer.GetSharedMaterials(list)`           | Creates new array each access      |

### Physics

| Forbidden                      | Use Instead                               | Reason                             |
| ------------------------------ | ----------------------------------------- | ---------------------------------- |
| `Physics.RaycastAll()`         | `Physics.RaycastNonAlloc(buffer)`         | Allocates new array                |
| `Physics.OverlapSphere()`      | `Physics.OverlapSphereNonAlloc(buffer)`   | Allocates new array                |
| `Physics.OverlapBox()`         | `Physics.OverlapBoxNonAlloc(buffer)`      | Allocates new array                |
| `Physics2D.OverlapCircleAll()` | `Physics2D.OverlapCircleNonAlloc(buffer)` | Allocates new array                |
| Non-convex mesh colliders      | Compound primitive colliders              | Extremely slow collision detection |

### Tags, Names, and Strings

| Forbidden                               | Use Instead                    | Reason                       |
| --------------------------------------- | ------------------------------ | ---------------------------- |
| `gameObject.tag == "Tag"`               | `gameObject.CompareTag("Tag")` | `.tag` allocates new string  |
| `gameObject.name == "Name"`             | Cache name in `Awake()`        | `.name` allocates new string |
| String concatenation for UI every frame | Update only on value change    | Allocates every frame        |

### Messaging

| Forbidden            | Use Instead                         | Reason                          |
| -------------------- | ----------------------------------- | ------------------------------- |
| `SendMessage()`      | Direct interface call               | Up to 1000x slower (reflection) |
| `BroadcastMessage()` | Events/delegates or interface calls | Up to 1000x slower (reflection) |

`SendMessage` / `BroadcastMessage` (and anything Unity relays internally through them, including sprite/renderer lifecycle notifications like `OnSpriteRendererBoundsChanged` and `OnValidate`) are **additionally forbidden during `AssetPostprocessor` callbacks**. Calling `AssetDatabase.Load*`, `GetComponentsInChildren`, or user callbacks synchronously from `OnPostprocessAllAssets` triggers these relays and produces `SendMessage cannot be called during Awake, CheckConsistency, or OnValidate` warnings. Defer the work via [AssetPostprocessorDeferral.Schedule](../../Editor/AssetProcessors/AssetPostprocessorDeferral.cs). See [asset-postprocessor-safety](../skills/asset-postprocessor-safety.md).

### Materials

| Forbidden                                 | Use Instead                    | Reason                          |
| ----------------------------------------- | ------------------------------ | ------------------------------- |
| `renderer.material` for changes           | `MaterialPropertyBlock`        | `.material` clones the material |
| `Shader.PropertyToID("_Name")` repeatedly | Cache as `static readonly int` | String lookup overhead          |

### Coroutines

| Forbidden                          | Use Instead                         | Reason                    |
| ---------------------------------- | ----------------------------------- | ------------------------- |
| `new WaitForSeconds()` in loop     | Cache `WaitForSeconds` instance     | Allocates every iteration |
| `new WaitForEndOfFrame()` in loop  | Cache `WaitForEndOfFrame` instance  | Allocates every iteration |
| `new WaitForFixedUpdate()` in loop | Cache `WaitForFixedUpdate` instance | Allocates every iteration |

### Debug and Lifecycle

| Forbidden                                           | Use Instead                           | Reason                           |
| --------------------------------------------------- | ------------------------------------- | -------------------------------- |
| `Debug.Log()` in production builds                  | `#if UNITY_EDITOR` or `[Conditional]` | String allocation even in builds |
| Empty `Update()` / `FixedUpdate()` / `LateUpdate()` | Remove entirely                       | Managed/native boundary overhead |
| `Instantiate`/`Destroy` spam                        | Object pooling                        | GC spikes and fragmentation      |

---

## Reflection Patterns

| Forbidden                                         | Use Instead                        | Reason                       |
| ------------------------------------------------- | ---------------------------------- | ---------------------------- |
| `Type.GetField()` on our code                     | Make field `internal`              | Slow, no compile-time safety |
| `Type.GetProperty()` on our code                  | Make property `internal`           | Slow, no compile-time safety |
| `Type.GetMethod()` on our code                    | Make method `internal`             | Slow, no compile-time safety |
| `FieldInfo.GetValue()`/`SetValue()`               | Direct field access via `internal` | Slow, no compile-time safety |
| `MethodInfo.Invoke()`                             | Direct method call via `internal`  | Slow, no compile-time safety |
| `Activator.CreateInstance()` with non-public ctor | Make constructor `internal`        | Slow, no compile-time safety |

### Acceptable Reflection

- Accessing Unity internal members (unavoidable)
- Accessing third-party library internals (document why)
- Testing reflection utilities themselves

---

## Magic String Patterns

| Forbidden                                 | Use Instead                               | Reason                 |
| ----------------------------------------- | ----------------------------------------- | ---------------------- |
| `"fieldName"` for our field names         | `nameof(fieldName)`                       | No compile-time safety |
| `"PropertyName"` for our properties       | `nameof(PropertyName)`                    | No compile-time safety |
| `"MethodName"` for our methods            | `nameof(MethodName)`                      | No compile-time safety |
| `"ClassName"` for our types               | `nameof(ClassName)` or `typeof().Name`    | No compile-time safety |
| `"Namespace.ClassName"` for full names    | `typeof(ClassName).FullName`              | No compile-time safety |
| `GetProperty("PropertyName")`             | `nameof()` + internal visibility          | No compile-time safety |
| `serializedObject.FindProperty("_field")` | `nameof(_field)` (field must be internal) | No compile-time safety |

### Acceptable Magic Strings

- Unity internal properties (`m_Script`, `m_LocalPosition`, etc.)
- Third-party library internals (document why)
- User-facing display strings
- Configuration/data keys (JSON properties, PlayerPrefs, etc.)
- File paths and resource names

---

## Update Method Anti-Patterns

| Anti-Pattern                        | Solution                   | Reason                               |
| ----------------------------------- | -------------------------- | ------------------------------------ |
| Physics operations in `Update()`    | Use `FixedUpdate()`        | Inconsistent at different framerates |
| Input handling in `FixedUpdate()`   | Use `Update()`             | May miss input events                |
| Heavy logic every frame             | Spread work across frames  | Frame rate drops                     |
| Many MonoBehaviours with `Update()` | Centralized update manager | Managed/native boundary overhead     |

---

## CLI Option Injection Patterns

When passing file arguments to CLI tools, a `--` (end-of-options) separator MUST appear before all file/glob arguments. Without this, attacker-controlled filenames (e.g., `--plugin=./evil.js`) are interpreted as CLI flags.

| Forbidden                                                                               | Use Instead                                                                                | Reason                                       |
| --------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------ | -------------------------------------------- |
| `node scripts/run-prettier.js --write "**/*.md"`                                        | `node scripts/run-prettier.js --write -- "**/*.md"`                                        | Glob results could contain option-like names |
| `node scripts/run-node-bin.js markdownlint "**/*.md" --config .markdownlint.json --fix` | `node scripts/run-node-bin.js markdownlint --config .markdownlint.json --fix -- "**/*.md"` | Options must precede `--`                    |
| `third-party-formatter --write "**/*.{yml,yaml}"`                                       | `node scripts/run-prettier.js --write -- "**/*.{yml,yaml}"`                                | Use the repo-pinned local tool               |
| `yamllint -c .yamllint.yaml "${FILES[@]}"`                                              | `yamllint -c .yamllint.yaml -- "${FILES[@]}"`                                              | Array expansion can contain malicious names  |
| `lychee --no-progress "**/*.md"`                                                        | `lychee --no-progress -- "**/*.md"`                                                        | Any tool accepting file lists is vulnerable  |

### Key Rules

1. ALL options/flags MUST come BEFORE `--`
2. ALL file paths/globs MUST come AFTER `--`
3. This applies to: `prettier`, `markdownlint`, `yamllint`, `eslint`, `lychee`, `cspell`, and any tool accepting file arguments
4. This applies in ALL contexts: shell scripts, GitHub Actions workflows, npm scripts, PowerShell scripts

### Where This Is Enforced

- Pre-commit hook (`.githooks/pre-commit`) — validated by existing tests
- Pre-push hook (`.githooks/pre-push`) — validated by tests
- GitHub Actions workflows — validated by tests
- npm scripts in `package.json` — validated by tests
- PowerShell wrapper scripts — validated by tests

---
