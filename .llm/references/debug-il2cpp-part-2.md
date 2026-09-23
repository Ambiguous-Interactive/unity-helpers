# debug-il2cpp - Part 2

## Split Content

## Debugging Techniques

### 1. Check IL2CPP Logs

Build logs contain IL2CPP errors:

- Windows: `%LOCALAPPDATA%\Unity\Editor\Editor.log`
- macOS: `~/Library/Logs/Unity/Editor.log`

Look for:

- `IL2CPP error`
- `Unresolved extern method`
- `GenericInstanceMethod`

### 2. Development Builds

```csharp
// Enable Development Build in Build Settings
// Provides better error messages and stack traces
```

### 3. Managed Stripping Level

In Player Settings > Other Settings > Managed Stripping Level:

- **Minimal**: Less stripping, larger build
- **Low**: Some stripping
- **Medium**: Balanced (default)
- **High**: Aggressive stripping, smallest build

Try **Low** or **Minimal** if experiencing stripping issues.

### 4. Script Debugging

Enable "Script Debugging" in Build Settings for:

- Breakpoints in IL2CPP builds
- Better stack traces
- Slower performance (debug only)

---

## Testing Checklist

### Before Release

1. **Test on actual hardware** — Simulators may hide issues
2. **Test IL2CPP specifically** — Don't assume Mono behavior matches
3. **Check all platforms** — Each has unique constraints
4. **Verify all reflection usage** — Ensure `[Preserve]` is applied
5. **Test with high stripping** — Catches missing preservations

When testing type-name migration in an IL2CPP player, avoid constructing deep generic-array
fixtures by reading `AssemblyQualifiedName` from the closed type. Unity 6000.6 can crash in
`il2cpp::vm::AppendAssemblyNameIfNeeded` / `RuntimeType.getFullName` before the code under test
runs. Build the fixture's canonical name from generic definitions and component assembly names,
then verify it with `Type.GetType` in the player before applying the simulated migration. A missing
NUnit result file or "unexpected log" can be a player crash, not an assertion failure.

### Quick IL2CPP Test

```csharp
#if ENABLE_IL2CPP
    Debug.Log("Running on IL2CPP");
#else
    Debug.Log("Running on Mono");
#endif
```

---

## Preserve Patterns

### Class Level

```csharp
[Preserve]
public class MySerializedClass
{
    public int Value;
}
```

### Assembly Level

```csharp
// In AssemblyInfo.cs
[assembly: Preserve]
```

### link.xml (Fine-Grained)

```xml
<linker>
    <!-- Preserve entire assembly -->
    <assembly fullname="MyAssembly" preserve="all"/>

    <!-- Preserve specific type -->
    <assembly fullname="Assembly-CSharp">
        <type fullname="MyNamespace.MyClass" preserve="all"/>
    </assembly>

    <!-- Preserve specific members -->
    <assembly fullname="Assembly-CSharp">
        <type fullname="MyNamespace.MyClass">
            <method name="MyMethod"/>
            <field name="myField"/>
        </type>
    </assembly>
</linker>
```

---

## Common Error Messages

| Error                           | Likely Cause      | Solution                     |
| ------------------------------- | ----------------- | ---------------------------- |
| `TypeLoadException`             | Type stripped     | Add `[Preserve]` or link.xml |
| `MissingMethodException`        | Method stripped   | Add `[Preserve]`             |
| `ExecutionEngineException`      | Generic AOT issue | Avoid generic virtuals       |
| `PlatformNotSupportedException` | Unsupported API   | Use alternative API          |
| `NotSupportedException: IL2CPP` | Reflection.Emit   | Avoid dynamic code gen       |

---

## Unity Helpers Compatibility

This package is tested on IL2CPP with these considerations:

1. **Serialization**: JSON and Protobuf work correctly
2. **Reflection**: Minimal, with `[Preserve]` where needed
3. **PRNGs**: All implementations are AOT-compatible
4. **Collections**: No dynamic code generation
5. **Spatial structures**: Pure managed code

If you encounter IL2CPP issues with Unity Helpers, check:

1. Proper assembly references in `.asmdef`
2. No accidental use of reflection in your code
3. Stripping level settings
