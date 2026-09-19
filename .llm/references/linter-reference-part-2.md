# linter-reference - Part 2

## Split Content

## Prettier

### Commands

```bash
# Check formatting
node scripts/run-prettier.js --check -- .
node scripts/run-prettier.js --check -- <file>

# Fix formatting
node scripts/run-prettier.js --write -- .
node scripts/run-prettier.js --write -- <file>
```

### Configuration

Located at `.prettierrc.json`:

```json
{
  "proseWrap": "always",
  "printWidth": 100,
  "tabWidth": 2,
  "trailingComma": "es5"
}
```

### File Coverage

Prettier handles these file types:

- Markdown (`.md`)
- JSON (`.json`, `.asmdef`)
- YAML (`.yml`, `.yaml`)
- JavaScript (`.js`)

### Ignored Files

See `.prettierignore` for files excluded from formatting.

## CSharpier

### Command

```bash
dotnet tool run csharpier format .
```

### Configuration

Located at `.csharpierrc.json`:

```json
{
  "printWidth": 100,
  "useTabs": false,
  "tabWidth": 4
}
```

### Important Notes

- CSharpier only formats C# files
- Always run after ANY `.cs` file modification
- Cannot fix naming convention violations (use manual fixes)

## C# Naming Linter

### Command

```bash
npm run lint:csharp-naming
```

### What It Checks

| Pattern           | Requirement              | Example         |
| ----------------- | ------------------------ | --------------- |
| Public methods    | PascalCase               | `GetValue()`    |
| Private methods   | PascalCase               | `ProcessData()` |
| Public properties | PascalCase               | `Count`         |
| Private fields    | `_camelCase` with prefix | `_myField`      |
| Constants         | PascalCase or UPPER_CASE | `MaxValue`      |
| Parameters        | camelCase                | `itemCount`     |
| Type parameters   | `T` or `T` + PascalCase  | `T`, `TValue`   |

## YAML Linter

### Command

```bash
npm run lint:yaml
```

### What It Checks

- YAML syntax validity
- Proper indentation
- Duplicate keys

### For GitHub Workflow Files

```bash
# Additional check for workflow files
actionlint
```

## Test Lifecycle Linter

### Command

```bash
npm run lint:tests
```

### What It Checks

1. **Allowlist path validation** (on startup): All paths in `$allowedHelperFiles` must exist on disk. Fails immediately with exit code 1 if any path is stale (file moved/renamed/deleted).
2. **UNH001**: Direct `Destroy`/`DestroyImmediate` calls without `Track()`
3. **UNH002**: Untracked Unity object allocation (`new GameObject(...)` etc.)
4. **UNH003**: Test classes missing `CommonTestBase` inheritance
5. **UNH004**: Underscores in test names
6. **UNH005**: `Assert.IsNull`/`Assert.IsNotNull` (should use `Assert.IsTrue` for Unity null checks)
7. **UNH011**: Editor-only references (`UnityEditor`, `WallstopStudios.UnityHelpers.Editor`) in
   player-compiled test code (anything under `Tests/` except `Tests/Editor/`) must be inside
   `#if UNITY_EDITOR`. Otherwise the editor assembly is stripped from the standalone player build
   and the leg fails with `CS0234` before any test runs. Add `// UNH-SUPPRESS UNH011` to opt out.
8. **UNH012**: A test must never `yield return` a `WaitForEndOfFrame` (e.g.
   `yield return new WaitForEndOfFrame();` or `yield return Buffers.WaitForEndOfFrame;`). Under
   `-batchmode -nographics` (the headless CI legs) there is no end-of-frame callback, so the yield
   never resumes: the PlayMode run hangs until it is force-killed and emits a misleading `total=0`
   `results.xml` that aborts the whole leg. Use `yield return null` (the production helper is
   batchmode-safe). A bare reference without `yield return` is not flagged. Add `// UNH-SUPPRESS`
   to opt out.
9. **UNH013**: Non-performance `Tests/Runtime/Tags` tests must not construct
   `WaitForSeconds` or `WaitForSecondsRealtime`; use deterministic `EffectHandler` clock/tick seams.
   Add `// UNH-SUPPRESS UNH013` only for tests that intentionally verify real Unity lifecycle timing.

All Unity object creation in tests must use `Track()`:

```csharp
// ✅ CORRECT: Track() ensures cleanup
GameObject obj = Track(new GameObject("Test"));
MyComponent comp = Track(obj.AddComponent<MyComponent>());

// ❌ WRONG: Untracked objects may leak
GameObject obj = new GameObject("Test");
```

### Tests

```bash
pwsh -NoProfile -File scripts/tests/test-lint-tests.ps1
```

Tests cover allowlist path existence, UNH error detection, clean file acceptance, and helper file allowlisting.

## Line Ending Linter

### Command

```bash
npm run eol:check
```

### Requirements

- All files must use CRLF line endings
- No UTF-8 BOM (Byte Order Mark)

### Fixing Line Endings

```bash
npm run eol:fix
```
