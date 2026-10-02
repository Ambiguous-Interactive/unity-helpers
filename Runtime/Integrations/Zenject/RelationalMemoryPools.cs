// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if ZENJECT_PRESENT
namespace WallstopStudios.UnityHelpers.Integrations.Zenject
{
    using global::Zenject;
    using UnityEngine;

    /// <summary>
    /// MemoryPool that assigns relational fields for spawned items automatically.
    /// </summary>
    /// <typeparam name="TValue">Component type managed by the pool.</typeparam>
    public class RelationalMemoryPool<TValue> : MemoryPool<TValue>
        where TValue : Component
    {
        [Inject]
        internal DiContainer _container = null;

        protected override void OnSpawned(TValue item)
        {
            base.OnSpawned(item);
            _container.AssignRelationalComponents(item);
        }

        internal void InternalOnSpawned(TValue item)
        {
            OnSpawned(item);
        }
    }

    /// <summary>
    /// MemoryPool with one spawn parameter that assigns relational fields for spawned items.
    /// </summary>
    /// <typeparam name="TParam1">Spawn parameter type.</typeparam>
    /// <typeparam name="TValue">Component type managed by the pool.</typeparam>
    public class RelationalMemoryPool<TParam1, TValue> : MemoryPool<TParam1, TValue>
        where TValue : Component
    {
        [Inject]
        internal DiContainer _container = null;

        protected override void Reinitialize(TParam1 p1, TValue item)
        {
            base.Reinitialize(p1, item);
            _container.AssignRelationalComponents(item);
        }
    }
}
#endif
