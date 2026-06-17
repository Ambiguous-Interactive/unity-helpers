// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if REFLEX_PRESENT
namespace WallstopStudios.UnityHelpers.Tests.Integrations.Reflex.Runtime
{
    using System;
    using System.Collections.Generic;
    using global::Reflex.Core;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.SceneManagement;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Integrations.Reflex;
    using WallstopStudios.UnityHelpers.Tags;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class RelationalComponentsReflexTests : CommonTestBase
    {
        [Test]
        public void ContainerExtensionsUseBoundAssigner()
        {
            ContainerBuilder builder = new();
            RecordingAssigner assigner = new();
            RegisterInstance(builder, assigner, typeof(IRelationalComponentAssigner));
            RegisterInstance(builder, CreateCacheFor(typeof(ReflexRelationalTester)));
            Container container = builder.Build();

            ReflexRelationalTester tester = CreateHierarchy();

            container.AssignRelationalComponents(tester);

            Assert.That(assigner.CallCount, Is.EqualTo(1), "Assigner should be invoked once.");
            Assert.That(
                assigner.LastComponent,
                Is.SameAs(tester),
                "Assigner should target the supplied component."
            );
            Assert.IsTrue(tester.parentBody != null, "ParentComponent field should be assigned.");
            Assert.IsTrue(tester.childCollider != null, "ChildComponent field should be assigned.");
        }

        [Test]
        public void ContainerExtensionsFallbackWithoutAssigner()
        {
            Container container = new ContainerBuilder().Build();

            ReflexRelationalTester tester = CreateHierarchy();

            container.AssignRelationalComponents(tester);

            Assert.IsTrue(
                tester.parentBody != null,
                "Fallback path should assign parentBody using reflection."
            );
            Assert.IsTrue(
                tester.childCollider != null,
                "Fallback path should assign childCollider using reflection."
            );
        }

        [UnityTest]
        public System.Collections.IEnumerator AssignSceneHydratesComponents()
        {
            AttributeMetadataCache cache = CreateCacheFor(typeof(ReflexRelationalTester));
            RecordingAssigner assigner = new();
            Scene scene = CreateTempScene("ReflexAssignScene");
            ReflexRelationalTester tester = CreateHierarchy();
            GameObject root = tester.transform.root.gameObject;
            SceneManager.MoveGameObjectToScene(root, scene);
            yield return null;

            RelationalReflexSceneBootstrapper.AssignScene(
                scene,
                assigner,
                cache,
                RelationalSceneAssignmentOptions.Default
            );

            Assert.That(assigner.CallCount, Is.EqualTo(1), "AssignScene should use the assigner.");
            Assert.That(
                assigner.LastComponent,
                Is.SameAs(tester),
                "AssignScene should hydrate relational tester component."
            );
            Assert.IsTrue(tester.parentBody != null, "ParentBody should be assigned after scan.");
            Assert.IsTrue(
                tester.childCollider != null,
                "ChildCollider should be assigned after scan."
            );
        }

        [UnityTest]
        public System.Collections.IEnumerator InstallerBindsAssignerAndOptions()
        {
            AttributeMetadataCache cache = CreateCacheFor(typeof(ReflexRelationalTester));
            Lazy<AttributeMetadataCache> previousLazy = AttributeMetadataCache._lazyInstance;
            AttributeMetadataCache._lazyInstance = new Lazy<AttributeMetadataCache>(() => cache);

            Scene scene = CreateTempScene("ReflexInstallerScene");
            GameObject installerObject = Track(new GameObject("ReflexInstaller"));
            RelationalComponentsInstaller installer =
                installerObject.AddComponent<RelationalComponentsInstaller>();
            SceneManager.MoveGameObjectToScene(installerObject, scene);

            ReflexRelationalTester tester = CreateHierarchy();
            GameObject root = tester.transform.root.gameObject;
            SceneManager.MoveGameObjectToScene(root, scene);
            yield return null;

            ContainerBuilder builder = new();
            builder.SetName("ReflexTesterContainer");
            installer.InstallBindings(builder);
            try
            {
                Container container = builder.Build();
                yield return null;

                Assert.IsTrue(
                    container.HasBinding<IRelationalComponentAssigner>(),
                    "Installer should bind IRelationalComponentAssigner."
                );

                Assert.IsTrue(
                    tester.parentBody != null,
                    "Installer should hydrate parentBody via scene assignment."
                );
                Assert.IsTrue(
                    tester.childCollider != null,
                    "Installer should hydrate childCollider via scene assignment."
                );
            }
            finally
            {
                AttributeMetadataCache._lazyInstance = previousLazy;
            }
        }

        [UnityTest]
        public System.Collections.IEnumerator InstantiateComponentWithRelationsUsesAssigner()
        {
            ContainerBuilder builder = new();
            RecordingAssigner assigner = new();
            RegisterInstance(builder, assigner, typeof(IRelationalComponentAssigner));
            RegisterInstance(builder, CreateCacheFor(typeof(ReflexRelationalTester)));
            Container container = builder.Build();

            GameObject parent = Track(new GameObject("ReflexComponentParent"));
            Rigidbody parentBody = parent.AddComponent<Rigidbody>();
            parentBody.useGravity = false;

            ReflexRelationalTester prefab = CreateComponentPrefabTester();
            ReflexRelationalTester instance = container.InstantiateComponentWithRelations(
                prefab,
                parent.transform
            );
            Track(instance.gameObject);
            instance.gameObject.SetActive(true);

            yield return null;

            Assert.That(
                assigner.CallCount,
                Is.GreaterThanOrEqualTo(1),
                "InstantiateComponentWithRelations should invoke the assigner."
            );
            Assert.That(
                assigner.AssignedComponents,
                Does.Contain(instance),
                "Assigner should receive the instantiated component."
            );
            Assert.That(
                instance.parentBody,
                Is.SameAs(parentBody),
                "ParentComponent attribute should resolve the injected parent Rigidbody."
            );
            Assert.IsTrue(
                instance.childCollider != null,
                "ChildComponent attribute should resolve the child collider on instantiation."
            );
        }

        [UnityTest]
        public System.Collections.IEnumerator InstantiateComponentWithRelationsFallsBackWithoutAssigner()
        {
            Container container = new ContainerBuilder().Build();

            GameObject parent = Track(new GameObject("ReflexComponentParentFallback"));
            Rigidbody parentBody = parent.AddComponent<Rigidbody>();
            parentBody.useGravity = false;

            ReflexRelationalTester prefab = CreateComponentPrefabTester();
            ReflexRelationalTester instance = container.InstantiateComponentWithRelations(
                prefab,
                parent.transform
            );
            Track(instance.gameObject);
            instance.gameObject.SetActive(true);

            yield return null;

            Assert.That(
                instance.parentBody,
                Is.SameAs(parentBody),
                "Fallback path should assign the parent Rigidbody."
            );
            Assert.IsTrue(
                instance.childCollider != null,
                "Fallback path should assign the child collider."
            );
        }

        [UnityTest]
        public System.Collections.IEnumerator InstantiateGameObjectWithRelationsUsesAssigner()
        {
            ContainerBuilder builder = new();
            RecordingAssigner assigner = new();
            RegisterInstance(builder, assigner, typeof(IRelationalComponentAssigner));
            RegisterInstance(builder, CreateCacheFor(typeof(ReflexRelationalTester)));
            Container container = builder.Build();

            GameObject parent = Track(new GameObject("ReflexGameObjectParent"));
            Rigidbody parentBody = parent.AddComponent<Rigidbody>();
            parentBody.useGravity = false;

            GameObject prefabRoot = CreateGameObjectPrefab();
            GameObject instanceRoot = container.InstantiateGameObjectWithRelations(
                prefabRoot,
                parent.transform,
                includeInactiveChildren: true
            );
            Track(instanceRoot);
            instanceRoot.SetActive(true);

            yield return null;

            ReflexRelationalTester instanceTester =
                instanceRoot.GetComponentInChildren<ReflexRelationalTester>(true);
            Assert.NotNull(
                instanceTester,
                "Instantiated hierarchy should include the tester component."
            );

            Assert.That(
                assigner.CallCount,
                Is.GreaterThanOrEqualTo(1),
                "InstantiateGameObjectWithRelations should invoke the assigner for hierarchy hydration."
            );
            Assert.That(
                assigner.AssignedComponents,
                Does.Contain(instanceTester),
                "Assigner should hydrate the tester component inside the instantiated hierarchy."
            );
            Assert.That(
                instanceTester.parentBody,
                Is.SameAs(parentBody),
                "ParentComponent attribute should bind to the supplied parent hierarchy."
            );
            Assert.IsTrue(
                instanceTester.childCollider != null,
                "ChildComponent attribute should bind to the prefab child after instantiation."
            );
        }

        [UnityTest]
        public System.Collections.IEnumerator InstantiateGameObjectWithRelationsFallsBackWithoutAssigner()
        {
            Container container = new ContainerBuilder().Build();

            GameObject parent = Track(new GameObject("ReflexGameObjectParentFallback"));
            Rigidbody parentBody = parent.AddComponent<Rigidbody>();
            parentBody.useGravity = false;

            GameObject prefabRoot = CreateGameObjectPrefab();
            GameObject instanceRoot = container.InstantiateGameObjectWithRelations(
                prefabRoot,
                parent.transform,
                includeInactiveChildren: true
            );
            Track(instanceRoot);
            instanceRoot.SetActive(true);

            yield return null;

            ReflexRelationalTester instanceTester =
                instanceRoot.GetComponentInChildren<ReflexRelationalTester>(true);
            Assert.NotNull(
                instanceTester,
                "Instantiated hierarchy should include the tester component."
            );
            Assert.That(
                instanceTester.parentBody,
                Is.SameAs(parentBody),
                "Fallback path should assign the parent Rigidbody inside the instantiated hierarchy."
            );
            Assert.IsTrue(
                instanceTester.childCollider != null,
                "Fallback path should assign the child collider inside the instantiated hierarchy."
            );
        }

        private ReflexRelationalTester CreateHierarchy()
        {
            GameObject parent = Track(new GameObject("ReflexParent"));
            Rigidbody parentBody = parent.AddComponent<Rigidbody>();
            parentBody.useGravity = false;

            GameObject middle = Track(new GameObject("ReflexMiddle"));
            middle.transform.SetParent(parent.transform);
            ReflexRelationalTester tester = middle.AddComponent<ReflexRelationalTester>();

            GameObject child = Track(new GameObject("ReflexChild"));
            child.transform.SetParent(middle.transform);
            child.AddComponent<CapsuleCollider>();

            return tester;
        }

        private ReflexRelationalTester CreateComponentPrefabTester()
        {
            GameObject root = Track(new GameObject("ReflexComponentPrefab"));
            ReflexRelationalTester tester = root.AddComponent<ReflexRelationalTester>();

            GameObject child = new("ReflexComponentPrefabChild");
            child.AddComponent<CapsuleCollider>();
            child.transform.SetParent(root.transform, false);

            root.SetActive(false);
            return tester;
        }

        private GameObject CreateGameObjectPrefab()
        {
            GameObject root = Track(new GameObject("ReflexGameObjectPrefab"));
            root.AddComponent<ReflexRelationalTester>();

            GameObject child = new("ReflexGameObjectPrefabChild");
            child.AddComponent<CapsuleCollider>();
            child.transform.SetParent(root.transform, false);

            root.SetActive(false);
            return root;
        }

        private AttributeMetadataCache CreateCacheFor(Type componentType)
        {
            AttributeMetadataCache cache = Track(
                ScriptableObject.CreateInstance<AttributeMetadataCache>()
            );

            AttributeMetadataCache.RelationalFieldMetadata[] fields =
            {
                new(
                    nameof(ReflexRelationalTester.parentBody),
                    AttributeMetadataCache.RelationalAttributeKind.Parent,
                    AttributeMetadataCache.FieldKind.Single,
                    typeof(Rigidbody).AssemblyQualifiedName,
                    false
                ),
                new(
                    nameof(ReflexRelationalTester.childCollider),
                    AttributeMetadataCache.RelationalAttributeKind.Child,
                    AttributeMetadataCache.FieldKind.Single,
                    typeof(CapsuleCollider).AssemblyQualifiedName,
                    false
                ),
            };

            AttributeMetadataCache.RelationalTypeMetadata[] relationalTypes =
            {
                new(componentType.AssemblyQualifiedName, fields),
            };

            cache._relationalTypeMetadata = relationalTypes;
            cache.ForceRebuildForTests();
            return cache;
        }

        // Reflex's builder registration API changed at the 14.0.0 major bump
        // (AddSingleton -> RegisterValue), gated by REFLEX_14_0_OR_NEWER from the
        // integration asmdef's versionDefine so the fixture compiles against both a
        // 13.x vendored copy and CI's pinned 14.3.0. The no-contract overload binds
        // the instance to its own concrete type in BOTH APIs (RegisterValue(value)
        // and AddSingleton(value)); passing an empty contract array would instead
        // bind to nothing, so the contract-count branch is required, not cosmetic.
        private static void RegisterInstance(
            ContainerBuilder builder,
            object instance,
            params Type[] contracts
        )
        {
#if REFLEX_14_0_OR_NEWER
            if (contracts.Length == 0)
            {
                builder.RegisterValue(instance);
            }
            else
            {
                builder.RegisterValue(instance, contracts);
            }
#else
            if (contracts.Length == 0)
            {
                builder.AddSingleton(instance);
            }
            else
            {
                builder.AddSingleton(instance, contracts);
            }
#endif
        }

        private sealed class RecordingAssigner : IRelationalComponentAssigner
        {
            private readonly List<Component> _assignedComponents = new();

            public int CallCount { get; private set; }

            public Component LastComponent { get; private set; }

            public IReadOnlyList<Component> AssignedComponents => _assignedComponents;

            public bool HasRelationalAssignments(Type componentType)
            {
                return true;
            }

            public void Assign(Component component)
            {
                LastComponent = component;
                CallCount++;
                if (component != null)
                {
                    _assignedComponents.Add(component);
                }
                component?.AssignRelationalComponents();
            }

            public void Assign(IEnumerable<Component> components)
            {
                if (components == null)
                {
                    return;
                }

                foreach (Component component in components)
                {
                    Assign(component);
                }
            }

            public void AssignHierarchy(GameObject root, bool includeInactiveChildren = true)
            {
                if (root == null)
                {
                    return;
                }

                Component[] components = root.GetComponentsInChildren<Component>(
                    includeInactiveChildren
                );
                for (int i = 0; i < components.Length; i++)
                {
                    Assign(components[i]);
                }
            }
        }

        private sealed class ReflexRelationalTester : MonoBehaviour
        {
            [ParentComponent(OnlyAncestors = true)]
            public Rigidbody parentBody;

            [ChildComponent(OnlyDescendants = true)]
            public CapsuleCollider childCollider;
        }
    }
}
#endif
