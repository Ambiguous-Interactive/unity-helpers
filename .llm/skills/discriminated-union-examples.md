# Skill: Discriminated Union Examples

<!-- trigger: oneof example, union state machine, FastOneOf example | Worked OneOf/FastOneOf examples | Feature -->

**Trigger**: When you want a full worked example of a discriminated union rather than the rules.
The API surface, `Match`/`Switch`/`TryGet` semantics and the mistakes to avoid are in
[use-discriminated-union](./use-discriminated-union.md).

---

## Complete Example: State Machine

```csharp
using WallstopStudios.UnityHelpers.Core.OneOf;
using UnityEngine;

public class EnemyAI : MonoBehaviour
{
    // States as simple structs
    public readonly struct Idle { }
    public readonly struct Patrolling
    {
        public Vector3 Target { get; }
        public Patrolling(Vector3 target) => Target = target;
    }
    public readonly struct Chasing
    {
        public Transform Target { get; }
        public Chasing(Transform target) => Target = target;
    }
    public readonly struct Attacking
    {
        public Transform Target { get; }
        public float Cooldown { get; }
        public Attacking(Transform target, float cooldown)
        {
            Target = target;
            Cooldown = cooldown;
        }
    }

    private FastOneOf<Idle, Patrolling, Chasing, Attacking> _state = new Idle();

    private void Update()
    {
        // Handle current state
        _state.Switch(
            idle => UpdateIdle(),
            patrolling => UpdatePatrolling(patrolling),
            chasing => UpdateChasing(chasing),
            attacking => UpdateAttacking(attacking)
        );
    }

    private void UpdateIdle()
    {
        if (ShouldStartPatrolling())
        {
            _state = new Patrolling(GetNextWaypoint());
        }
    }

    private void UpdatePatrolling(Patrolling state)
    {
        Transform player = FindPlayer();
        if (player != null && IsInRange(player.position))
        {
            _state = new Chasing(player);
            return;
        }

        MoveToward(state.Target);
        if (ReachedTarget(state.Target))
        {
            _state = new Idle();
        }
    }

    private void UpdateChasing(Chasing state)
    {
        if (state.Target == null)
        {
            _state = new Idle();
            return;
        }

        if (IsInAttackRange(state.Target.position))
        {
            _state = new Attacking(state.Target, 0f);
            return;
        }

        MoveToward(state.Target.position);
    }

    private void UpdateAttacking(Attacking state)
    {
        if (state.Target == null)
        {
            _state = new Idle();
            return;
        }

        // Would need to track cooldown differently since structs are immutable
        PerformAttack(state.Target);
        _state = new Chasing(state.Target);
    }

    // Helper methods would be implemented here...
    private bool ShouldStartPatrolling() => Random.value < 0.01f;
    private Vector3 GetNextWaypoint() => Vector3.zero;
    private Transform FindPlayer() => null;
    private bool IsInRange(Vector3 pos) => false;
    private bool IsInAttackRange(Vector3 pos) => false;
    private void MoveToward(Vector3 target) { }
    private bool ReachedTarget(Vector3 target) => false;
    private void PerformAttack(Transform target) { }
}
```

---

## Related Skills

- [use-discriminated-union](./use-discriminated-union.md) - The rules and API surface
- [defensive-programming](./defensive-programming.md) - Never throw, handle every case
- [high-performance-csharp](./high-performance-csharp.md) - Allocation-free patterns
