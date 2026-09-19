# create-test - Part 2

## Split Content

## Critical Rules Summary

For complete naming rules, see [test-naming-conventions](../skills/test-naming-conventions.md).

### Naming

1. **NO underscores in test method names** — Use PascalCase only
2. **Data-driven test names use dot notation** — `TestName = "Input.Null.ReturnsFalse"`

### Code Structure

1. **NEVER use `#region`**
2. **One file per MonoBehaviour/ScriptableObject** — Even in tests
3. **No `async Task` test methods** — Use `IEnumerator` with `[UnityTest]`
4. **No `Assert.ThrowsAsync`** — Not available in Unity's NUnit
5. **Unity ships NUnit 3.5** — a member added in a later NUnit does not exist in any editor. `npm run typecheck:tests` compiles against 3.5.0 exactly, so it fails locally rather than in all eight Unity legs; do not "fix" that by raising the version

### Unity-Specific

> **WARNING: UNH005 Lint Check Enforced**
>
> The pre-commit hook, `npm run agent:preflight`, `npm run validate:local`, and CI enforce UNH005, which flags `Assert.IsNull` and `Assert.IsNotNull` usage.
> These assertions use `ReferenceEquals` internally, which bypasses Unity's custom `==` operator and fails to detect Unity's "fake null" (destroyed objects that are not yet garbage collected).

1. **Unity object null checks** — Use `== null` / `!= null`, never `Assert.IsNull` / `Assert.IsNotNull`
2. **Interface/base types still use Unity null checks** — If the static type is an interface, `Component`, or `object`, still use Unity's `==` operator

**Why this matters:**

- Unity's `==` operator for `UnityEngine.Object` performs special "fake null" checking
- When a Unity object is destroyed, it becomes a "fake null" — the C# reference still exists, but Unity considers it null
- `Assert.IsNull` / `Assert.IsNotNull` use `ReferenceEquals`, which bypasses this check entirely
- This can cause tests to pass when they should fail (or vice versa)

```csharp
// CORRECT - Uses Unity's == operator
Assert.IsTrue(gameObject != null);
Assert.IsFalse(component == null);

// CORRECT - Interface/base typed UnityEngine.Object
ITestInterface interfaceField = gameObject.GetComponent<TestInterfaceComponent>();
Assert.IsTrue(interfaceField != null);

// NEVER USE - Bypasses Unity's null check (flagged by UNH005)
Assert.IsNull(gameObject);
Assert.IsNotNull(component);
```

**Auto-fix**: `pwsh -NoProfile -File scripts/lint-tests.ps1 -FixNullChecks -Paths <changed-test-files>`

### Documentation

1. **Self-documenting tests** — No `// Arrange`, `// Act`, `// Assert` comments
2. **No `[Description]` annotations**
3. **No file paths in docstrings**

---

## Test Quality Requirements (Prevent Flaky Tests)

### Determinism

| Anti-Pattern                      | Required Pattern                              |
| --------------------------------- | --------------------------------------------- |
| `DateTime.Now` in assertions      | Inject time provider or use fixed values      |
| `Random` without seed             | Use seeded PRNG: `new PcgRandom(fixedSeed)`   |
| Depending on dictionary/set order | Sort before comparing or use ordered types    |
| Floating-point exact equality     | Use tolerance: `Assert.AreEqual(a, b, 0.001)` |

### Isolation

| Anti-Pattern                       | Required Pattern                            |
| ---------------------------------- | ------------------------------------------- |
| Static mutable state between tests | Reset in `[TearDown]` or use instance state |
| Shared fixtures without reset      | `[SetUp]` creates fresh state each test     |
| Tests affecting each other         | Each test must be completely independent    |

---

## Editor Integration Tests

### Shared Fixture Pattern

For tests requiring Unity assets (textures, prefabs, etc.):

1. Use `[OneTimeSetUp]`/`[OneTimeTearDown]` for asset lifecycle
2. Create shared output directory once, delete once at end
3. Use per-test subdirectories via `TestContext.CurrentContext.Test.Name`

### AssetDatabase Batching

Wrap slow operations in `AssetDatabaseBatchHelper.BeginBatch()`:

- Store scope: `_batchScope = AssetDatabaseBatchHelper.BeginBatch(refreshOnDispose: true)` in `[OneTimeSetUp]`
- Dispose scope: `_batchScope?.Dispose()` in `[OneTimeTearDown]`
- Defers ALL imports until scope is disposed

### Golden File Metadata Pattern

For tests that would require slow extraction/generation:

1. Create JSON metadata files with expected outputs
2. Commit to `Tests/Editor/{Feature}/Assets/GoldenOutput/`
3. Add `[Explicit]` utility test to regenerate when logic changes
4. Verification tests read JSON and assert against expected values

### Filesystem vs AssetDatabase Verification

Prefer `System.IO` over `AssetDatabase` for verification:

- `Directory.GetFiles("*.png")` instead of `AssetDatabase.FindAssets`
- `File.Exists()` instead of `AssetDatabase.LoadAssetAtPath`
- Only use AssetDatabase when testing actual Unity asset behavior

See also: [test-parallelization-rules](../skills/test-parallelization-rules.md)

---

## Assertion Best Practices

### Prefer Specific Assertions

```csharp
// GOOD - Specific, clear failure message
Assert.AreEqual(42, result);
Assert.IsTrue(collection.Contains("key"));
Assert.Throws<ArgumentNullException>(() => Method(null));

// AVOID - Generic, unclear failure
Assert.That(result == 42);
```

### Collection Assertions

```csharp
// GOOD - Collection-specific assertions
Assert.AreEqual(5, list.Count);
CollectionAssert.AreEqual(expected, actual);
CollectionAssert.AreEquivalent(expected, actual); // Order-independent
CollectionAssert.IsEmpty(collection);
```

---
