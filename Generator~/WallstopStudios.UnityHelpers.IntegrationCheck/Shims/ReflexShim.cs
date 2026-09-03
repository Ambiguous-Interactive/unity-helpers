// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

// Reference-only shim for the Reflex surface `Runtime/Integrations/Reflex/**` binds (#687).
//
// Reflex (com.gustavopsantos.reflex, pinned at 14.3.1 in .github/integration-packages.json) is an
// OpenUPM package with no NuGet equivalent, which is the same position Odin was in for #347 and the
// reason `Runtime/Integrations/**` was excluded from every check project until now.
//
// FIVE types, and they are the whole contract. Measured by compiling the four Reflex sources
// against nothing and reading what the compiler asked for:
//
//     Container            ContainerRelationalExtensions, RelationalReflexSceneBootstrapper,
//                          RelationalComponentsInstaller (the OnContainerBuilt callback)
//     ContainerBuilder     RelationalComponentsInstaller
//     IInstaller           RelationalComponentsInstaller
//     AttributeInjector    ContainerRelationalExtensions
//     GameObjectInjector   ContainerRelationalExtensions
//     Lifetime/Resolution  RelationalComponentsInstaller, under REFLEX_14_0_OR_NEWER only
//
// Mirroring the real members' shapes matters: declaring a member Reflex does not have would let a
// genuine error through, so nothing beyond what those four files name is declared, with ONE
// deliberate exception -- the two enums carry their real members rather than only `Singleton` and
// `Lazy`. A shim enum trimmed to what today's call sites use answers the next call site with a
// confident RED for a package that has the member, and nothing in `CS0117` says which of the two
// was wrong. Bodies return `default` because this is a type-checker; behaviour is what the Unity
// integration legs assert, against the real package.
//
// The registration API SPLIT at Reflex 14.0.0 and the asmdef's `REFLEX_14_0_OR_NEWER` versionDefine
// selects between them, so BOTH shapes are declared here unconditionally and the two configurations
// (`typecheck:integrations` and `:legacy-reflex`) each compile one of them. Declaring only the one
// the default configuration uses would leave the fallback branch compiled by nothing, which is the
// hole this project exists to close.
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
