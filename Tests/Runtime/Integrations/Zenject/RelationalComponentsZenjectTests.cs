// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if ZENJECT_PRESENT
namespace WallstopStudios.UnityHelpers.Tests.Integrations.Zenject.Runtime
{
    using System;
    using System.Collections.Generic;
    using global::Zenject;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.SceneManagement;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Integrations.Zenject;
    using WallstopStudios.UnityHelpers.Tags;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.TestUtils;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class RelationalComponentsZenjectTests : CommonTestBase
    {
        private DiContainer Container;

        [SetUp]
        public override void BaseSetUp()
        {
            base.BaseSetUp();
            ReflexTestSupport.EnsureReflexSettings();
            Container = new DiContainer();
        }

        [Test]
        public void ContainerExtensionsUseBoundAssigner()
        {
            RecordingAssigner assigner = new();
            Container.Bind<IRelationalComponentAssigner>().FromInstance(assigner);

            ZenjectRelationalTester tester = CreateHierarchy();

            Container.AssignRelationalComponents(tester);

            Assert.That(
                assigner.CallCount,
                Is.EqualTo(1),
                "Assigner should be called exactly once when bound"
            );
            Assert.That(
                assigner.LastComponent,
                Is.SameAs(tester),
                "Assigner should receive the same component instance"
            );
            Assert.IsTrue(
                tester.parentBody != null,
                "ParentComponent assignment should set parentBody"
            );
            Assert.IsTrue(
                tester.childCollider != null,
                "ChildComponent assignment should set childCollider"
            );
        }

        [Test]
        public void ContainerExtensionsFallBackWhenAssignerMissing()
        {
            ZenjectRelationalTester tester = CreateHierarchy();

            Container.AssignRelationalComponents(tester);

            Assert.IsTrue(
                tester.parentBody != null,
                "Fallback should assign parentBody without a bound assigner"
            );
            Assert.IsTrue(
                tester.childCollider != null,
                "Fallback should assign childCollider without a bound assigner"
            );
        }

        [UnityTest]
        public System.Collections.IEnumerator SceneInitializerAssignsActiveSceneComponents()
        {
            AttributeMetadataCache cache = CreateCacheFor(typeof(ZenjectRelationalTester));
            Container.BindInstance(cache);
            RecordingAssigner assigner = new();
            Container.Bind<IRelationalComponentAssigner>().FromInstance(assigner);
            Container.BindInstance(RelationalSceneAssignmentOptions.Default);
            Container.BindInterfacesTo<RelationalComponentSceneInitializer>().AsSingle();

            Scene scene = CreateTempScene("ZenjectTestScene_Active");
            ZenjectRelationalTester tester = CreateHierarchy();
            GameObject root = tester.transform.root.gameObject;
            SceneManager.MoveGameObjectToScene(root, scene);
            yield return null;

            IInitializable initializer = Container.Resolve<IInitializable>();
            initializer.Initialize();
            yield return null;

            Assert.That(
                assigner.CallCount,
                Is.EqualTo(1),
                "Initializer should invoke assigner exactly once for the tester component"
            );
            Assert.That(
                assigner.LastComponent,
                Is.SameAs(tester),
                "Initializer should target the created tester instance"
            );
        }

        [UnityTest]
        public System.Collections.IEnumerator SceneInitializerSkipsInactiveWhenOptionDisabled()
        {
            AttributeMetadataCache cache = CreateCacheFor(typeof(ZenjectRelationalTester));
            Container.BindInstance(cache);
            RecordingAssigner assigner = new();
            Container.Bind<IRelationalComponentAssigner>().FromInstance(assigner);
            Container.BindInstance(new RelationalSceneAssignmentOptions(false));
            Container.BindInterfacesTo<RelationalComponentSceneInitializer>().AsSingle();

            Scene scene = CreateTempScene("ZenjectTestScene_InactiveFalse");
            ZenjectRelationalTester tester = CreateHierarchy();
            tester.gameObject.SetActive(false);
            GameObject root = tester.transform.root.gameObject;
            SceneManager.MoveGameObjectToScene(root, scene);
            yield return null;

            IInitializable initializer = Container.Resolve<IInitializable>();
            initializer.Initialize();
            yield return null;

            Assert.That(
                assigner.CallCount,
                Is.EqualTo(0),
                "Initializer should skip inactive tester when IncludeInactive is false"
            );
        }

        [UnityTest]
        public System.Collections.IEnumerator SceneInitializerIncludesInactiveWhenOptionEnabled()
        {
            AttributeMetadataCache cache = CreateCacheFor(typeof(ZenjectRelationalTester));
            Container.BindInstance(cache);
            RecordingAssigner assigner = new();
            Container.Bind<IRelationalComponentAssigner>().FromInstance(assigner);
            Container.BindInstance(new RelationalSceneAssignmentOptions(true));
            Container.BindInterfacesTo<RelationalComponentSceneInitializer>().AsSingle();

            Scene scene = CreateTempScene("ZenjectTestScene_InactiveTrue");
            ZenjectRelationalTester tester = CreateHierarchy();
            tester.gameObject.SetActive(false);
            GameObject root = tester.transform.root.gameObject;
            SceneManager.MoveGameObjectToScene(root, scene);
            yield return null;

            IInitializable initializer = Container.Resolve<IInitializable>();
            initializer.Initialize();
            yield return null;

            Assert.That(
                assigner.CallCount,
                Is.EqualTo(1),
                "Initializer should include inactive tester when IncludeInactive is true"
            );
            Assert.That(
                assigner.LastComponent,
                Is.SameAs(tester),
                "Initializer should target the inactive tester component"
            );
        }

        [UnityTest]
        public System.Collections.IEnumerator SceneInitializerUsesMultiPassWhenConfigured()
        {
            AttributeMetadataCache cache = CreateCacheFor(typeof(ZenjectRelationalTester));
            Container.BindInstance(cache);
            RecordingAssigner assigner = new();
            Container.Bind<IRelationalComponentAssigner>().FromInstance(assigner);
            Container.BindInstance(
                new RelationalSceneAssignmentOptions(true, useSinglePassScan: false)
            );
            Container.BindInterfacesTo<RelationalComponentSceneInitializer>().AsSingle();

            Scene scene = CreateTempScene("ZenjectMultiPassScene");
            ZenjectRelationalTester tester = CreateHierarchy();
            SceneManager.MoveGameObjectToScene(tester.transform.root.gameObject, scene);
            yield return null;

            IInitializable initializer = Container.Resolve<IInitializable>();
            initializer.Initialize();
            yield return null;

            Assert.That(
                assigner.CallCount,
                Is.EqualTo(1),
                "Multi-pass configuration should assign each relational component once"
            );
            Assert.That(
                assigner.LastComponent,
                Is.SameAs(tester),
                "Multi-pass configuration should target the tester in the active scene"
            );
        }

        [UnityTest]
        public System.Collections.IEnumerator SceneLoadListenerAssignsAdditiveSceneSinglePass()
        {
            AttributeMetadataCache cache = CreateCacheFor(typeof(ZenjectRelationalTester));
            RelationalComponentAssigner assigner = new(cache);
            RelationalSceneAssignmentOptions options = new(
                includeInactive: true,
                useSinglePassScan: true
            );
            RelationalSceneLoadListener listener = new(assigner, cache, options);
            listener.Initialize();
            TrackDisposable(listener);

            Scene additive = CreateTempScene("Zenject_Additive_Runtime_Single", setActive: false);

            ZenjectRelationalTester tester = CreateHierarchy();
            GameObject root = tester.transform.root.gameObject;
            SceneManager.MoveGameObjectToScene(root, additive);

            yield return null;

            listener.OnSceneLoaded(additive, LoadSceneMode.Additive);
            yield return null;

            Assert.IsTrue(
                tester.parentBody != null,
                "Scene load listener should assign parentBody in single-pass mode"
            );
            Assert.IsTrue(
                tester.childCollider != null,
                "Scene load listener should assign childCollider in single-pass mode"
            );
        }

        [UnityTest]
        public System.Collections.IEnumerator SceneLoadListenerAssignsAdditiveSceneMultiPass()
        {
            AttributeMetadataCache cache = CreateCacheFor(typeof(ZenjectRelationalTester));
            RelationalComponentAssigner assigner = new(cache);
            RelationalSceneAssignmentOptions options = new(
                includeInactive: true,
                useSinglePassScan: false
            );
            RelationalSceneLoadListener listener = new(assigner, cache, options);
            listener.Initialize();
            TrackDisposable(listener);

            Scene additive = CreateTempScene("Zenject_Additive_Runtime_Multi", setActive: false);

            ZenjectRelationalTester tester = CreateHierarchy();
            GameObject root = tester.transform.root.gameObject;
            SceneManager.MoveGameObjectToScene(root, additive);

            yield return null;

            listener.OnSceneLoaded(additive, LoadSceneMode.Additive);
            yield return null;

            Assert.IsTrue(
                tester.parentBody != null,
                "Scene load listener should assign parentBody in multi-pass mode"
            );
            Assert.IsTrue(
                tester.childCollider != null,
                "Scene load listener should assign childCollider in multi-pass mode"
            );
        }

        [Test]
        public void ContainerAssignRelationalHierarchyAssignsFields()
        {
            ZenjectRelationalTester tester = CreateHierarchy();
            Container.AssignRelationalHierarchy(tester.gameObject, includeInactiveChildren: false);
            Assert.IsTrue(
                tester.parentBody != null,
                "AssignRelationalHierarchy should assign parentBody"
            );
            Assert.IsTrue(
                tester.childCollider != null,
                "AssignRelationalHierarchy should assign childCollider"
            );
        }

        [UnityTest]
        public System.Collections.IEnumerator SceneInitializerIgnoresNonActiveScenes()
        {
            AttributeMetadataCache cache = CreateCacheFor(typeof(ZenjectRelationalTester));
            RecordingAssigner assigner = new();
            Container.BindInstance(cache);
            Container.Bind<IRelationalComponentAssigner>().FromInstance(assigner);
            Container.BindInstance(new RelationalSceneAssignmentOptions(true));
            Container.BindInterfacesTo<RelationalComponentSceneInitializer>().AsSingle();

            Scene active = CreateTempScene("ZenjectActiveScene_Sep");
            ZenjectRelationalTester testerA = CreateHierarchy();
            SceneManager.MoveGameObjectToScene(testerA.transform.root.gameObject, active);

            Scene secondary = CreateTempScene("ZenjectSecondaryScene_Sep", setActive: false);
            ZenjectRelationalTester testerB = CreateHierarchy();
            SceneManager.MoveGameObjectToScene(testerB.transform.root.gameObject, secondary);
            yield return null;

            IInitializable initializer = Container.Resolve<IInitializable>();
            initializer.Initialize();
            yield return null;

            Assert.That(
                assigner.CallCount,
                Is.EqualTo(1),
                "Initializer should only process components from the active scene"
            );
            Assert.That(
                assigner.LastComponent,
                Is.SameAs(testerA),
                "Active scene tester should be assigned"
            );
        }

        [Test]
        public void ContainerAssignRelationalHierarchyRespectsIncludeInactiveChildren()
        {
            ZenjectRelationalTester rootTester = CreateHierarchy();

            // Create a second tester under an INACTIVE child to verify enumeration filter
            GameObject subTesterGO = Track(new GameObject("ZenjectSubTester"));
            subTesterGO.transform.SetParent(rootTester.transform);
            ZenjectRelationalTester subTester = subTesterGO.AddComponent<ZenjectRelationalTester>();
            // Provide a descendant collider for the sub tester
            GameObject subChild = Track(new GameObject("ZenjectSubChild"));
            subChild.AddComponent<CapsuleCollider>();
            subChild.transform.SetParent(subTesterGO.transform);
            // Make the tester itself inactive
            subTesterGO.SetActive(false);

            // includeInactiveChildren = false -> root tester is assigned (root is always included), inactive sub-tester skipped
            Container.AssignRelationalHierarchy(
                rootTester.gameObject,
                includeInactiveChildren: false
            );
            Assert.IsTrue(
                rootTester.parentBody != null,
                "Root tester should be assigned even when includeInactiveChildren is false"
            );
            Assert.IsTrue(
                rootTester.childCollider != null,
                "Root tester should be assigned even when includeInactiveChildren is false"
            );
            Assert.IsTrue(
                subTester.parentBody == null,
                "Inactive sub tester should be skipped when includeInactiveChildren is false"
            );
            Assert.IsTrue(
                subTester.childCollider == null,
                "Inactive sub tester should be skipped when includeInactiveChildren is false"
            );

            // includeInactiveChildren = true -> sub tester now gets assigned
            Container.AssignRelationalHierarchy(
                rootTester.gameObject,
                includeInactiveChildren: true
            );
            Assert.IsTrue(
                subTester.parentBody != null,
                "Inactive sub tester should be assigned when includeInactiveChildren is true"
            );
            Assert.IsTrue(
                subTester.childCollider != null,
                "Inactive sub tester should be assigned when includeInactiveChildren is true"
            );
        }

        [Test]
        public void ContainerAssignRelationalHierarchyUsesBoundAssigner()
        {
            RecordingAssigner assigner = new();
            Container.Bind<IRelationalComponentAssigner>().FromInstance(assigner);
            ZenjectRelationalTester tester = CreateHierarchy();
            Container.AssignRelationalHierarchy(tester.gameObject, includeInactiveChildren: true);
            Assert.That(
                assigner.HierarchyCallCount,
                Is.EqualTo(1),
                "AssignRelationalHierarchy should use bound assigner"
            );
        }

        [Test]
        public void InstantiateWithRelationsAssignsWhenAssignerBound()
        {
            RecordingAssigner assigner = new();
            Container.Bind<IRelationalComponentAssigner>().FromInstance(assigner);

            ZenjectRelationalTester prefab = CreatePrefabTesterHierarchy(rootHasRigidbody: false);

            GameObject overrideParent = Track(new GameObject("ZenjectOverrideParent_Bound"));
            overrideParent.AddComponent<Rigidbody>();

            ZenjectRelationalTester instance = Container.InstantiateComponentWithRelations(
                prefab,
                overrideParent.transform
            );
            Track(instance.gameObject);

            Assert.That(
                assigner.CallCount,
                Is.EqualTo(1),
                "Instantiate should invoke bound assigner exactly once"
            );
            Assert.That(
                assigner.LastComponent,
                Is.SameAs(instance),
                "Instantiate should target the created tester instance"
            );
            Assert.IsTrue(
                instance.parentBody != null,
                "ParentComponent should be assigned from override parent"
            );
            Assert.IsTrue(
                instance.childCollider != null,
                "ChildComponent should be assigned from prefab child collider"
            );
        }

        [Test]
        public void InstantiateWithRelationsAssignsWhenAssignerMissing()
        {
            ZenjectRelationalTester prefab = CreatePrefabTesterHierarchy(rootHasRigidbody: false);

            GameObject overrideParent = Track(new GameObject("ZenjectOverrideParent_NoAssigner"));
            overrideParent.AddComponent<Rigidbody>();

            ZenjectRelationalTester instance = Container.InstantiateComponentWithRelations(
                prefab,
                overrideParent.transform
            );
            Track(instance.gameObject);

            Assert.IsTrue(
                instance.parentBody != null,
                "ParentComponent should be assigned from override parent without a bound assigner"
            );
            Assert.IsTrue(
                instance.childCollider != null,
                "ChildComponent should be assigned without a bound assigner"
            );
        }

        [Test]
        public void InstantiateWithRelationsRespectsParentOverride()
        {
            ZenjectRelationalTester prefab = CreatePrefabTesterHierarchy(rootHasRigidbody: false);

            GameObject overrideParent = Track(new GameObject("ZenjectOverrideParent"));
            overrideParent.AddComponent<Rigidbody>();

            ZenjectRelationalTester instance = Container.InstantiateComponentWithRelations(
                prefab,
                overrideParent.transform
            );
            Track(instance.gameObject);

            Assert.IsTrue(
                instance.parentBody != null,
                "ParentComponent should be assigned from override parent"
            );
            Assert.IsTrue(
                instance.childCollider != null,
                "ChildComponent should be assigned from prefab child collider"
            );
        }

        [Test]
        public void InstantiateWithRelationsThrowsOnNullPrefab()
        {
            Assert.That(
                () => Container.InstantiateComponentWithRelations<ZenjectRelationalTester>(null),
                Throws.ArgumentNullException
            );
        }

        private ZenjectRelationalTester CreateHierarchy()
        {
            GameObject parent = Track(new GameObject("ZenjectParent"));
            parent.AddComponent<Rigidbody>();

            GameObject middle = Track(new GameObject("ZenjectMiddle"));
            ZenjectRelationalTester tester = middle.AddComponent<ZenjectRelationalTester>();
            middle.transform.SetParent(parent.transform);

            GameObject child = Track(new GameObject("ZenjectChild"));
            child.AddComponent<CapsuleCollider>();
            child.transform.SetParent(middle.transform);

            return tester;
        }

        private ZenjectRelationalTester CreatePrefabTesterHierarchy(bool rootHasRigidbody)
        {
            GameObject root = Track(new GameObject("ZenjectPrefabRoot"));
            if (rootHasRigidbody)
            {
                root.AddComponent<Rigidbody>();
            }

            GameObject middle = Track(new GameObject("ZenjectPrefabMiddle"));
            middle.transform.SetParent(root.transform);
            ZenjectRelationalTester tester = middle.AddComponent<ZenjectRelationalTester>();

            GameObject child = Track(new GameObject("ZenjectPrefabChild"));
            child.AddComponent<CapsuleCollider>();
            child.transform.SetParent(middle.transform);

            return tester;
        }

        private AttributeMetadataCache CreateCacheFor(Type componentType)
        {
            AttributeMetadataCache cache = Track(
                ScriptableObject.CreateInstance<AttributeMetadataCache>()
            );

            AttributeMetadataCache.RelationalFieldMetadata[] fields =
            {
                new(
                    nameof(ZenjectRelationalTester.parentBody),
                    AttributeMetadataCache.RelationalAttributeKind.Parent,
                    AttributeMetadataCache.FieldKind.Single,
                    typeof(Rigidbody).AssemblyQualifiedName,
                    isInterface: false
                ),
                new(
                    nameof(ZenjectRelationalTester.childCollider),
                    AttributeMetadataCache.RelationalAttributeKind.Child,
                    AttributeMetadataCache.FieldKind.Single,
                    typeof(CapsuleCollider).AssemblyQualifiedName,
                    isInterface: false
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

        private sealed class RecordingAssigner : IRelationalComponentAssigner
        {
            public int CallCount { get; private set; }

            public Component LastComponent { get; private set; }

            public int HierarchyCallCount { get; private set; }

            public bool HasRelationalAssignments(Type componentType)
            {
                return true;
            }

            public void Assign(Component component)
            {
                LastComponent = component;
                CallCount++;
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

                HierarchyCallCount++;
                Component[] components = root.GetComponentsInChildren<Component>(
                    includeInactiveChildren
                );
                foreach (Component component in components)
                {
                    Assign(component);
                }
            }
        }
    }
}
#endif
