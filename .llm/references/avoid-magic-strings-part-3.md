# avoid-magic-strings - Part 3

## Split Content

## Quick Reference

| Scenario              | Magic String          | Compile-Time Safe                           |
| --------------------- | --------------------- | ------------------------------------------- |
| Field name            | `"_myField"`          | `nameof(_myField)`                          |
| Property name         | `"MyProperty"`        | `nameof(MyProperty)`                        |
| Method name           | `"DoSomething"`       | `nameof(DoSomething)`                       |
| Parameter name        | `"value"`             | `nameof(value)`                             |
| Type name             | `"MyClass"`           | `nameof(MyClass)` or `typeof(MyClass).Name` |
| Full type name        | `"Namespace.MyClass"` | `typeof(MyClass).FullName`                  |
| Unity internals       | `"m_Script"`          | ✅ String OK (external)                     |
| Third-party internals | `"internalField"`     | ✅ String OK (document why)                 |

---

## See Also

- [Serialized Property Names](../skills/serialized-property-names.md) - Reaching serialized properties from tests without literals
- [Avoid Reflection](../skills/avoid-reflection.md) - Related rules for avoiding reflection and using `InternalsVisibleTo`
- [Defensive Programming](../skills/defensive-programming.md) - General defensive coding practices
- [Create Tests](../skills/create-test.md) - Test creation guidelines including naming conventions
