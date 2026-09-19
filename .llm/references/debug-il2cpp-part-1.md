# debug-il2cpp - Part 1

## Split Content

**Trigger**: When debugging IL2CPP build issues, platform-specific problems, or AOT compilation errors.

---

## Common IL2CPP Issues

### 1. Code Stripping

IL2CPP strips unused code. Reflection targets may be removed.

**Symptoms**:

- `TypeLoadException` at runtime
- Missing methods/types in builds
- Works in Editor, fails in build

**Solutions**:

```csharp
// Mark types accessed via reflection
[Preserve]
public class MyReflectedClass
{
    [Preserve]
    public void ReflectedMethod() { }
}
```

Or use `link.xml`:

```xml
<linker>
    <assembly fullname="Assembly-CSharp">
        <type fullname="MyNamespace.MyClass" preserve="all"/>
    </assembly>
</linker>
```

### 2. Generic Virtual Methods

**Symptoms**:

- `ExecutionEngineException`
- Missing method exceptions for generic calls

**Solution**: Avoid generic virtual methods, or ensure concrete instantiations exist:

```csharp
// ❌ Problematic
public virtual T GetValue<T>() { ... }

// ✅ Better - use non-generic
public virtual object GetValue(Type type) { ... }

// ✅ Or ensure instantiations exist
private void EnsureGenericInstantiations()
{
    GetValue<int>();    // Forces AOT compilation
    GetValue<string>();
    GetValue<float>();
}
```

### 3. Reflection.Emit

**Symptoms**:

- `PlatformNotSupportedException`
- Dynamic code generation failures

**Solution**: IL2CPP doesn't support `System.Reflection.Emit`. Use alternatives:

```csharp
// ❌ Not supported
DynamicMethod method = new DynamicMethod(...);

// ✅ Use expression trees (limited support)
Expression<Func<int, int>> expr = x => x * 2;
Func<int, int> func = expr.Compile();

// ✅ Or use source generators (compile-time)
```

---

## Forbidden C# Features

These cause IL2CPP compilation failures or runtime issues:

| Feature                              | Issue                     |
| ------------------------------------ | ------------------------- |
| Nullable reference types (`string?`) | Compilation failures      |
| `#nullable enable`                   | Not supported             |
| Null-forgiving operator (`!`)        | Requires nullable context |
| `required` modifier                  | C# 11, not available      |
| `init` accessors                     | Limited support           |
| File-scoped types                    | C# 11, not available      |
| Raw string literals                  | C# 11, not available      |
| Generic attributes                   | C# 11, not available      |
| Static abstract interface members    | Limited support           |

---

## Platform-Specific Constraints

### WebGL

```csharp
// ❌ No threading
Task.Run(() => { ... });
new Thread(() => { ... });

// ❌ No file system
File.ReadAllText(path);
Directory.GetFiles(path);

// ✅ Use Unity APIs
UnityWebRequest.Get(url);
PlayerPrefs.GetString(key);
```

### iOS (AOT)

```csharp
// ❌ No runtime code generation
Activator.CreateInstance(type);  // May fail for some types

// ✅ Use factory methods
public static T Create<T>() where T : new() => new T();

// ✅ Register types explicitly
[Preserve]
private static void RegisterTypes()
{
    // Force AOT compilation
    var _ = new MyClass();
}
```

### Android

```csharp
// 64-bit requirements - ensure all native plugins support arm64

// JNI limitations - be careful with AndroidJavaObject
using (AndroidJavaClass jc = new AndroidJavaClass("com.example.MyClass"))
{
    // Keep references short-lived
}
```

---
