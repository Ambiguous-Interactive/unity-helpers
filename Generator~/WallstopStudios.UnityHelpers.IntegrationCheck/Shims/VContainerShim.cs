// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

// Reference-only shim for the VContainer surface `Runtime/Integrations/VContainer/**` binds (#687).
//
// VContainer (jp.hadashikick.vcontainer, pinned at 1.18.0 in .github/integration-packages.json) is
// an OpenUPM package with no NuGet equivalent, the same position Odin was in for #347.
//
// SEVEN names, and they are the whole contract:
//
//     VContainer.IObjectResolver          ObjectResolverRelationalExtensions, RelationalObjectPools
//     VContainer.IContainerBuilder        RelationalComponentsBuilderExtensions
//     VContainer.RegistrationBuilder      the fluent result of Register/RegisterInstance/RegisterEntryPoint
//     VContainer.Lifetime                 RelationalComponentsBuilderExtensions
//     VContainer.Unity.IInitializable     RelationalComponentEntryPoint, RelationalSceneLoadListener
//     VContainer.Unity.LifetimeScope      RelationalComponentsBuilderExtensions, in a `see cref`
//     the Inject/TryResolve/InjectGameObject/Register/RegisterEntryPoint/WithParameter extensions
//
// `LifetimeScope` is named by NOTHING but a `<see cref="LifetimeScope"/>` in the builder
// extensions' `<remarks>`. It is here anyway, and deliberately: this project turns
// `GenerateDocumentationFile` on for the same reason TypeCheck and EditorCheck do, the package
// ships `Runtime/**` as SOURCE, and that cref is what a consumer's IDE resolves. Dropping it would
// trade a real dead-cref check for one less shim declaration. Same reasoning as EditorCheck's
// `Shims/AnimationEventEditorShim.cs`, which exists only to keep one cref resolvable.
//
// Which NAMESPACE each member lives in is load-bearing rather than cosmetic, because the sources
// import the two namespaces separately: `RelationalComponentEntryPoint` and
// `RelationalSceneLoadListener` import `VContainer.Unity` ALONE, so `IInitializable` must be there
// and not in `VContainer`; `InjectGameObject` and `RegisterEntryPoint` are reached only from files
// that import both. A shim that flattened them into one namespace would compile a file Unity
// refuses for a missing `using`, which is precisely the class of defect these gates exist to catch.
//
// Mirrors the real members' shapes. Declaring a member VContainer does not have would let a genuine
// error through, so nothing beyond what those five files name is declared, with two deliberate
// exceptions. `Lifetime` carries its real members rather than only `Singleton`, because a shim enum
// trimmed to today's call sites answers the next one with a confident RED for a package that has the
// member. And `RegistrationBuilder` is a TYPE the sources never spell: it is what
// `Register(...).As<T>().AsSelf()` returns, so the chain needs it to exist. Bodies return `default`
// because this is a type-checker, not a container.
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
        Every registration the package writes goes through an extension method below, so the
        interface itself carries nothing. Real VContainer declares a `Register(RegistrationBuilder)`
        here; adding it would declare surface no source names, which is the one way a shim can hide
        a genuine error.
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
