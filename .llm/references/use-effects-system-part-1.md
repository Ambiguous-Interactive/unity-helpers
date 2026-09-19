# use-effects-system - Part 1

## Split Content

**Trigger**: When implementing buffs, debuffs, status effects, or stat modifications.

---

## Core Components

| Component             | Purpose                                                                 |
| --------------------- | ----------------------------------------------------------------------- |
| `AttributeEffect`     | ScriptableObject defining stat modifications, tags, cosmetics, duration |
| `AttributesComponent` | Base class exposing modifiable `Attribute` fields                       |
| `TagHandler`          | Reference-counted tag queries                                           |
| `EffectHandle`        | Unique ID for tracking/removing specific effect instances               |
| `CosmeticEffectData`  | VFX/SFX prefab data for effects                                         |

---

## Creating an Attributes Component

### Define Your Stats

```csharp
using WallstopStudios.UnityHelpers.Runtime.Tags;

public class PlayerAttributes : AttributesComponent
{
    [SerializeField]
    private Attribute maxHealth = new Attribute(100f);

    [SerializeField]
    private Attribute moveSpeed = new Attribute(5f);

    [SerializeField]
    private Attribute attackDamage = new Attribute(10f);

    [SerializeField]
    private Attribute defense = new Attribute(0f);

    public float MaxHealth => maxHealth.Value;
    public float MoveSpeed => moveSpeed.Value;
    public float AttackDamage => attackDamage.Value;
    public float Defense => defense.Value;
}
```

### Attribute Class

`Attribute` handles base value + modifiers:

```csharp
// Base value: 100
// With +20% modifier: 120
// With +50 flat modifier: 150
```

---

## Creating an AttributeEffect

### Via ScriptableObject Menu

1. Right-click in Project window
2. Create > Wallstop Studios > Unity Helpers > Attribute Effect

### Effect Configuration

```csharp
[CreateAssetMenu(fileName = "NewEffect", menuName = "Effects/Custom Effect")]
public class CustomEffect : AttributeEffect
{
    // Configured in inspector:
    // - Duration (0 = permanent until removed)
    // - Stat modifiers (additive, multiplicative)
    // - Tags to apply
    // - Cosmetic effects (VFX, SFX)
}
```

---

## Applying Effects

### Basic Application

```csharp
public class EffectApplier : MonoBehaviour
{
    [SerializeField]
    private AttributeEffect speedBoostEffect;

    [SerializeField]
    private AttributeEffect poisonEffect;

    public void ApplySpeedBoost(PlayerAttributes target)
    {
        target.ApplyEffect(speedBoostEffect);
    }

    public void ApplyPoison(PlayerAttributes target)
    {
        target.ApplyEffect(poisonEffect);
    }
}
```

### Tracking Effect Instances

```csharp
public class EffectManager : MonoBehaviour
{
    private Dictionary<AttributesComponent, List<EffectHandle>> activeEffects =
        new Dictionary<AttributesComponent, List<EffectHandle>>();

    public EffectHandle ApplyEffect(AttributesComponent target, AttributeEffect effect)
    {
        EffectHandle handle = target.ApplyEffect(effect);

        if (!activeEffects.TryGetValue(target, out List<EffectHandle> handles))
        {
            handles = new List<EffectHandle>();
            activeEffects[target] = handles;
        }

        handles.Add(handle);
        return handle;
    }

    public void RemoveEffect(AttributesComponent target, EffectHandle handle)
    {
        target.RemoveEffect(handle);

        if (activeEffects.TryGetValue(target, out List<EffectHandle> handles))
        {
            handles.Remove(handle);
        }
    }

    public void RemoveAllEffects(AttributesComponent target)
    {
        if (activeEffects.TryGetValue(target, out List<EffectHandle> handles))
        {
            foreach (EffectHandle handle in handles)
            {
                target.RemoveEffect(handle);
            }

            handles.Clear();
        }
    }
}
```

---
