// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

/*

 * Reflex has no NuGet package. Both registration APIs are declared because the REFLEX_14_0_OR_NEWER branches

 * need separate checks; enum members mirror the package to avoid false missing-member errors.

 */
namespace Reflex.Enums
{
    public enum Lifetime
    {
        Transient = 0,
        Singleton = 1,
        Scoped = 2,
    }

    public enum Resolution
    {
        Lazy = 0,
        Eager = 1,
    }
}

namespace Reflex.Core
{
    using System;

    public sealed class Container
    {
        public bool HasBinding<T>()
        {
            return default;
        }

        public T Resolve<T>()
        {
            return default;
        }
    }

    public sealed class ContainerBuilder
    {
        public event Action<Container> OnContainerBuilt;

        public ContainerBuilder RegisterValue(object value, params Type[] contracts)
        {
            return this;
        }

        public ContainerBuilder RegisterType(
            Type concrete,
            Type[] contracts,
            Reflex.Enums.Lifetime lifetime,
            Reflex.Enums.Resolution resolution
        )
        {
            return this;
        }

        public ContainerBuilder AddSingleton(object value, params Type[] contracts)
        {
            return this;
        }

        public ContainerBuilder AddSingleton(Type concrete, params Type[] contracts)
        {
            return this;
        }
    }

    public interface IInstaller
    {
        void InstallBindings(ContainerBuilder builder);
    }
}

namespace Reflex.Extensions
{
    using System;
    using Reflex.Core;

    public static class ContainerBuilderExtensions
    {
        public static bool HasBinding(this ContainerBuilder builder, Type contract)
        {
            return default;
        }
    }
}

namespace Reflex.Injectors
{
    using Reflex.Core;
    using UnityEngine;

    public static class AttributeInjector
    {
        public static void Inject(object instance, Container container) { }
    }

    public static class GameObjectInjector
    {
        public static void InjectRecursive(GameObject gameObject, Container container) { }
    }
}
