# integrate-optional-dependency - Part 2

## Split Content

## Testing Optional Dependencies

### Test File Organization

For each optional dependency integration, create tests that:

1. Use the same conditional compilation
2. Are located in `Tests/Editor/CustomDrawers/{DependencyName}/` or similar
3. Have test types in separate files under `Tests/Editor/TestTypes/{DependencyName}/`

### Test Type Extraction

Test helper MonoBehaviours and ScriptableObjects **MUST** be in separate files:

**Correct**:

```text
Tests/Editor/
├── TestTypes/
│   └── Odin/
│       ├── OdinEnumToggleButtonsTarget.cs      # Each test SO/MB in own file
│       ├── OdinEnumToggleButtonsMonoBehaviour.cs
│       ├── OdinShowIfBoolTarget.cs
│       └── ...
├── CustomDrawers/
│   └── Odin/
│       ├── WEnumToggleButtonsOdinDrawerTests.cs  # Test class only
│       └── WShowIfOdinDrawerTests.cs
```

**Incorrect** (embedded test types):

```csharp
// WEnumToggleButtonsOdinDrawerTests.cs
[TestFixture]
public sealed class WEnumToggleButtonsOdinDrawerTests
{
    [Test]
    public void TestSomething() { }

    // WRONG - These should be in separate files!
    private sealed class OdinEnumToggleButtonsTarget : SerializedScriptableObject
    {
        public TestEnum testField;
    }

    private enum TestEnum { A, B, C }  // WRONG - Should be shared
}
```

### Shared Test Enums

Common enums used across multiple test files should be centralized:

```csharp
// Tests/Editor/TestTypes/SharedTestEnums.cs
namespace WallstopStudios.UnityHelpers.Tests.Editor.TestTypes
{
    public enum TestModeEnum { ModeA, ModeB, ModeC }

    [Flags]
    public enum TestFlagsEnum { None = 0, Flag1 = 1, Flag2 = 2, Flag3 = 4 }

    public enum SmallTestEnum { One, Two, Three }
}
```

---

## Checklist for New Optional Dependency Integration

1. [ ] Create dedicated folder: `Editor/CustomDrawers/{DependencyName}/`
2. [ ] Place `#if` directives **inside** namespace
3. [ ] One class per file
4. [ ] Extract shared logic to helper classes
5. [ ] Extract shared caches to `EditorCacheHelper` or similar
6. [ ] Create test folder: `Tests/Editor/CustomDrawers/{DependencyName}/`
7. [ ] Create test types folder: `Tests/Editor/TestTypes/{DependencyName}/`
8. [ ] Extract all test MonoBehaviours/ScriptableObjects to separate files
9. [ ] Share test enums in `SharedTestEnums.cs`
10. [ ] Generate `.meta` files for all new files

---

## Anti-Patterns to Avoid

| Anti-Pattern              | Why It's Wrong                               | Correct Approach        |
| ------------------------- | -------------------------------------------- | ----------------------- |
| `#if` outside namespace   | Inconsistent with project style              | Put inside namespace    |
| Multiple classes per file | Hard to navigate, Unity serialization issues | One class per file      |
| Duplicate caches          | Memory waste, maintenance burden             | Centralize in helper    |
| Duplicate shared logic    | DRY violation, bug divergence                | Extract to helper class |
| Embedded test types       | Unity serialization errors                   | Separate files          |
| Duplicate test enums      | Maintenance burden                           | Shared enum file        |

---

## VContainer Integration Pattern

```csharp
namespace WallstopStudios.UnityHelpers.Runtime.Integrations.VContainer
{
#if VCONTAINER
    using global::VContainer;
    using global::VContainer.Unity;

    public sealed class UnityHelpersInstaller : IInstaller
    {
        public void Install(IContainerBuilder builder)
        {
            // Register services
            builder.Register<IMyService, MyServiceImplementation>(Lifetime.Singleton);
        }
    }
#endif
}
```

---

## Zenject Integration Pattern

```csharp
namespace WallstopStudios.UnityHelpers.Runtime.Integrations.Zenject
{
#if ZENJECT
    using global::Zenject;

    public sealed class UnityHelpersInstaller : MonoInstaller
    {
        public override void InstallBindings()
        {
            Container.Bind<IMyService>()
                .To<MyServiceImplementation>()
                .AsSingle();
        }
    }
#endif
}
```

---

## Reflex Integration Pattern

```csharp
namespace WallstopStudios.UnityHelpers.Runtime.Integrations.Reflex
{
#if REFLEX
    using global::Reflex.Core;

    public sealed class UnityHelpersInstaller : IInstaller
    {
        public void InstallBindings(ContainerBuilder builder)
        {
            builder.AddSingleton<IMyService, MyServiceImplementation>();
        }
    }
#endif
}
```

---
