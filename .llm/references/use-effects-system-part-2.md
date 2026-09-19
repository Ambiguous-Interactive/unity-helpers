# use-effects-system - Part 2

## Split Content

## Tag System

### Checking Tags

```csharp
public class CombatSystem : MonoBehaviour
{
    public void ProcessDamage(AttributesComponent target, float damage)
    {
        TagHandler tags = target.Tags;

        // Check for immunity
        if (tags.HasTag("Invulnerable"))
        {
            return;
        }

        // Check for damage modifiers
        if (tags.HasTag("Vulnerable"))
        {
            damage *= 1.5f;
        }

        if (tags.HasTag("Armored"))
        {
            damage *= 0.5f;
        }

        // Apply damage
        ApplyDamage(target, damage);
    }
}
```

### Tag Reference Counting

Tags are reference-counted—multiple effects can add the same tag:

```csharp
// Effect A adds "Burning" tag
target.ApplyEffect(burnEffectA);  // Tags: ["Burning" (count: 1)]

// Effect B also adds "Burning" tag
target.ApplyEffect(burnEffectB);  // Tags: ["Burning" (count: 2)]

// Remove Effect A
target.RemoveEffect(handleA);     // Tags: ["Burning" (count: 1)]

// "Burning" still present until Effect B removed
target.Tags.HasTag("Burning");    // true
```

---

## Cosmetic Effects

### CosmeticEffectData

```csharp
[System.Serializable]
public class CosmeticEffectData
{
    public GameObject vfxPrefab;      // Visual effect prefab
    public AudioClip sfxClip;         // Sound effect
    public float duration;            // How long to play
    public bool attachToTarget;       // Parent to target transform
}
```

### Configuring in AttributeEffect

In the inspector, configure:

- **On Apply VFX/SFX**: Played when effect is applied
- **On Remove VFX/SFX**: Played when effect is removed
- **Persistent VFX**: Stays active while effect is active

---

## Complete Example

### Effect ScriptableObjects

Create these in the editor:

**SpeedBoost.asset**:

- Duration: 10 seconds
- Move Speed: +50%
- Tags: ["SpeedBoosted"]
- On Apply VFX: SpeedBoostParticles

**Poison.asset**:

- Duration: 5 seconds
- Tags: ["Poisoned", "DamageOverTime"]
- Tick Damage: 5 per second

**Shield.asset**:

- Duration: 0 (permanent until removed)
- Defense: +50
- Tags: ["Shielded", "Armored"]

### Usage

```csharp
public class Player : MonoBehaviour
{
    [SerializeField]
    private PlayerAttributes attributes;

    [SerializeField]
    private AttributeEffect speedBoost;

    [SerializeField]
    private AttributeEffect shield;

    private EffectHandle shieldHandle;

    public void UseSpeedBoost()
    {
        attributes.ApplyEffect(speedBoost);
        // Auto-expires after duration
    }

    public void ActivateShield()
    {
        if (shieldHandle == null)
        {
            shieldHandle = attributes.ApplyEffect(shield);
        }
    }

    public void DeactivateShield()
    {
        if (shieldHandle != null)
        {
            attributes.RemoveEffect(shieldHandle);
            shieldHandle = null;
        }
    }

    public bool IsSpeedBoosted()
    {
        return attributes.Tags.HasTag("SpeedBoosted");
    }
}
```

---
