// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

/*

 * VContainer has no NuGet package. Preserve its namespaces and complete enum members so the shim cannot hide

 * missing imports or invent missing APIs. LifetimeScope exists here for XML references.

 */
namespace VContainer
{
    using System;

    public enum Lifetime
    {
        Transient = 0,
        Singleton = 1,
        Scoped = 2,
    }

    public interface IObjectResolver
    {
        void Inject(object instance);
    }

    public sealed class RegistrationBuilder
    {
        public RegistrationBuilder As<TInterface>()
        {
            return this;
        }

        public RegistrationBuilder AsSelf()
        {
            return this;
        }
    }

    /*
     * Only the extension registration surface is used; adding unrelated interface members could hide a
     * missing extension import.
     */
    public interface IContainerBuilder { }

    public static class ObjectResolverExtensions
    {
        public static bool TryResolve<T>(this IObjectResolver resolver, out T resolved)
        {
            resolved = default;
            return default;
        }
    }

    public static class ContainerBuilderExtensions
    {
        public static RegistrationBuilder Register<TImplementation>(
            this IContainerBuilder builder,
            Lifetime lifetime
        )
        {
            return default;
        }

        public static RegistrationBuilder Register<TImplementation>(
            this IContainerBuilder builder,
            Func<IObjectResolver, TImplementation> factory,
            Lifetime lifetime
        )
        {
            return default;
        }

        public static RegistrationBuilder RegisterInstance<TInterface>(
            this IContainerBuilder builder,
            TInterface instance
        )
        {
            return default;
        }
    }

    public static class RegistrationBuilderExtensions
    {
        public static RegistrationBuilder WithParameter<TParam>(
            this RegistrationBuilder builder,
            TParam value
        )
        {
            return builder;
        }
    }
}

namespace VContainer.Unity
{
    using UnityEngine;

    public interface IInitializable
    {
        void Initialize();
    }

    public abstract class LifetimeScope : MonoBehaviour { }

    public static class ObjectResolverUnityExtensions
    {
        public static void InjectGameObject(
            this IObjectResolver resolver,
            GameObject gameObject
        ) { }
    }

    public static class ContainerBuilderUnityExtensions
    {
        public static RegistrationBuilder RegisterEntryPoint<TEntryPoint>(
            this IContainerBuilder builder
        )
        {
            return default;
        }
    }
}
