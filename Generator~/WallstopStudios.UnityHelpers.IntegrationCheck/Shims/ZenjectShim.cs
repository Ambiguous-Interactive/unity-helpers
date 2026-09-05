// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

/*

 * Extenject has no NuGet package. This signature-only shim preserves the used binder chains and the real

 * InjectAttribute targets.

 */
namespace Zenject
{
    using System;
    using UnityEngine;

    [AttributeUsage(
        AttributeTargets.Field
            | AttributeTargets.Property
            | AttributeTargets.Parameter
            | AttributeTargets.Constructor
            | AttributeTargets.Method,
        AllowMultiple = false
    )]
    public sealed class InjectAttribute : Attribute { }

    public interface IInitializable
    {
        void Initialize();
    }

    public class ConcreteBinder
    {
        public ConcreteBinder To<TConcrete>()
        {
            return this;
        }

        public ConcreteBinder FromInstance(object instance)
        {
            return this;
        }

        public ConcreteBinder AsSingle()
        {
            return this;
        }
    }

    public class DiContainer
    {
        public void Inject(object injectable) { }

        public void InjectGameObject(GameObject gameObject) { }

        public bool HasBinding(Type contractType)
        {
            return default;
        }

        public TContract Resolve<TContract>()
        {
            return default;
        }

        public TComponent InstantiatePrefabForComponent<TComponent>(
            UnityEngine.Object prefab,
            Transform parentTransform
        )
        {
            return default;
        }

        public GameObject InstantiatePrefab(UnityEngine.Object prefab, Transform parentTransform)
        {
            return default;
        }

        public ConcreteBinder Bind<TContract>()
        {
            return default;
        }

        public ConcreteBinder BindInstance<TContract>(TContract instance)
        {
            return default;
        }

        public ConcreteBinder BindInterfacesTo<TConcrete>()
        {
            return default;
        }
    }

    public abstract class MonoInstaller : MonoBehaviour
    {
        protected DiContainer Container => default;

        public abstract void InstallBindings();
    }

    public class MemoryPool<TValue>
    {
        protected virtual void OnSpawned(TValue item) { }
    }

    public class MemoryPool<TParam1, TValue>
    {
        protected virtual void Reinitialize(TParam1 parameter, TValue item) { }
    }
}
