# use-effects-system - Part 3

## Split Content

## Best Practices

### Effect Stacking

```csharp
// Multiple applications of same effect
// Behavior depends on effect configuration:
// - Stack (each application is separate)
// - Refresh (resets duration)
// - Ignore (no effect if already applied)
```

### Cleanup on Destroy

```csharp
private void OnDestroy()
{
    // Effects are automatically cleaned up when AttributesComponent is destroyed
    // But you may want to trigger removal VFX/SFX manually
}
```

### Persistent vs Timed Effects

```csharp
// Timed effect (auto-expires)
[SerializeField]
private AttributeEffect timedBuff;  // Duration > 0

// Persistent effect (manual removal)
[SerializeField]
private AttributeEffect passiveAbility;  // Duration = 0

private EffectHandle passiveHandle;

void EnablePassive()
{
    passiveHandle = attributes.ApplyEffect(passiveAbility);
}

void DisablePassive()
{
    attributes.RemoveEffect(passiveHandle);
    passiveHandle = null;
}
```
