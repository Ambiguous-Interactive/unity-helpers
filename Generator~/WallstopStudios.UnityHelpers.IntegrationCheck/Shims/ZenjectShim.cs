// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

// Reference-only shim for the Zenject surface `Runtime/Integrations/Zenject/**` binds (#687).
//
// Zenject ships to this repository as Extenject (com.svermeulen.extenject, pinned at 9.2.0-stcf3 in
// .github/integration-packages.json), an OpenUPM package with no NuGet equivalent -- the same
// position Odin was in for #347.
//
// SIX types, and they are the whole contract:
//
//     DiContainer      DiContainerRelationalExtensions, RelationalComponentsInstaller,
//                      RelationalMemoryPools
//     MonoInstaller    RelationalComponentsInstaller
//     IInitializable   RelationalComponentSceneInitializer, RelationalSceneLoadListener
//     MemoryPool<>     RelationalMemoryPools
//     MemoryPool<,>    RelationalMemoryPools
//     InjectAttribute  RelationalMemoryPools
//
// The fluent binder is the one place where a shim has to declare a shape rather than a member:
// `Bind<T>().To<U>().AsSingle()`, `Bind<T>().FromInstance(x)`, `BindInstance(x)` and
// `BindInterfacesTo<T>().AsSingle()` are four different chains and Zenject spells them across a
// family of binder types. They collapse here into the two links the package actually writes, which
// is the smallest surface that compiles those chains.
//
// Mirrors the real members' shapes. Declaring a member Zenject does not have would let a genuine
// error through, so nothing beyond what those five files name is declared, with two deliberate
// exceptions. `ConcreteBinder` is a TYPE the sources never spell -- it is what the four chains above
// return, so they need it to exist -- and `InjectAttribute` carries Zenject's real
// `AttributeTargets` rather than the `Field` these files use, because a shim that narrowed them
// would answer a constructor injection with a confident RED for a package that allows it. Bodies
// return `default` because this is a type-checker, not a container.
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
