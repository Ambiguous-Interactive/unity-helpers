// MIT License - Copyright (c) 2024 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Helper
{
    using System.Collections.Generic;
    using UnityEngine;
    using Utils;

    public static partial class Helpers
    {
        public static List<T> IterateOverAllParentComponentsRecursively<T>(
            this Component component,
            List<T> buffer,
            bool includeSelf = false
        )
        {
            buffer.Clear();
            if (component == null)
            {
                return buffer;
            }

            using PooledResource<List<T>> bufferResource = Buffers<T>.List.Get(
                out List<T> internalBuffer
            );
            if (includeSelf)
            {
                component.GetComponents(internalBuffer);
                buffer.AddRange(internalBuffer);
            }

            Transform parent = component.transform.parent;
            while (parent != null)
            {
                parent.GetComponents(internalBuffer);
                buffer.AddRange(internalBuffer);
                parent = parent.parent;
            }

            return buffer;
        }

        public static IEnumerable<T> IterateOverAllParentComponentsRecursively<T>(
            this Component component,
            bool includeSelf = false
        )
        {
            if (component == null)
            {
                yield break;
            }

            using PooledResource<List<T>> bufferResource = Buffers<T>.List.Get(out List<T> buffer);
            using PooledResource<List<Transform>> transformResource = Buffers<Transform>.List.Get(
                out List<Transform> transforms
            );
            foreach (Transform parent in component.IterateOverAllParents(transforms, includeSelf))
            {
                parent.GetComponents(buffer);
                foreach (T c in buffer)
                {
                    yield return c;
                }
            }
        }

        public static List<T> IterateOverAllChildComponentsRecursively<T>(
            this Component component,
            List<T> buffer,
            bool includeSelf = false
        )
        {
            buffer.Clear();
            if (component == null)
            {
                return buffer;
            }

            using PooledResource<List<T>> internalBufferResource = Buffers<T>.List.Get(
                out List<T> internalBuffer
            );
            if (includeSelf)
            {
                component.GetComponents(internalBuffer);
                buffer.AddRange(internalBuffer);
            }

            Transform transform = component.transform;
            for (int i = 0; i < transform.childCount; ++i)
            {
                Transform child = transform.GetChild(i);
                child.GetComponentsInChildren(true, internalBuffer);
                buffer.AddRange(internalBuffer);
            }

            return buffer;
        }

        public static IEnumerable<T> IterateOverAllChildComponentsRecursively<T>(
            this Component component,
            bool includeSelf = false
        )
        {
            if (component == null)
            {
                yield break;
            }

            using PooledResource<List<T>> bufferResource = Buffers<T>.List.Get(out List<T> buffer);
            if (includeSelf)
            {
                component.GetComponents(buffer);
                foreach (T c in buffer)
                {
                    yield return c;
                }
            }

            Transform transform = component.transform;
            for (int i = 0; i < transform.childCount; ++i)
            {
                Transform child = transform.GetChild(i);
                child.GetComponentsInChildren(true, buffer);
                foreach (T c in buffer)
                {
                    yield return c;
                }
            }
        }

        public static IEnumerable<Transform> IterateOverAllChildren(
            this Component component,
            bool includeSelf = false
        )
        {
            if (component == null)
            {
                yield break;
            }

            Transform transform = component.transform;
            if (includeSelf)
            {
                yield return transform;
            }

            for (int i = 0; i < transform.childCount; ++i)
            {
                yield return transform.GetChild(i);
            }
        }

        public static List<Transform> IterateOverAllChildren(
            this Component component,
            List<Transform> buffer,
            bool includeSelf = false
        )
        {
            buffer.Clear();
            if (component == null)
            {
                return buffer;
            }

            Transform transform = component.transform;
            if (includeSelf)
            {
                buffer.Add(transform);
            }

            for (int i = 0; i < transform.childCount; ++i)
            {
                buffer.Add(transform.GetChild(i));
            }

            return buffer;
        }

        public static IEnumerable<Transform> IterateOverAllParents(
            this Component component,
            bool includeSelf = false
        )
        {
            if (component == null)
            {
                yield break;
            }

            Transform transform = component.transform;
            if (includeSelf)
            {
                yield return transform;
            }

            Transform parent = transform.parent;
            while (parent != null)
            {
                yield return parent;
                parent = parent.parent;
            }
        }

        public static List<Transform> IterateOverAllParents(
            this Component component,
            List<Transform> buffer,
            bool includeSelf = false
        )
        {
            buffer.Clear();
            if (component == null)
            {
                return buffer;
            }

            Transform transform = component.transform;
            if (includeSelf)
            {
                buffer.Add(transform);
            }

            Transform parent = transform.parent;
            while (parent != null)
            {
                buffer.Add(parent);
                parent = parent.parent;
            }

            return buffer;
        }

        public static IEnumerable<Transform> IterateOverAllChildrenRecursively(
            this Component component,
            bool includeSelf = false
        )
        {
            if (component == null)
            {
                yield break;
            }

            Transform transform = component.transform;
            if (includeSelf)
            {
                yield return transform;
            }

            using PooledResource<List<Transform>> transformResource = Buffers<Transform>.List.Get(
                out List<Transform> transforms
            );
            for (int i = 0; i < transform.childCount; ++i)
            {
                foreach (
                    Transform child in transform
                        .GetChild(i)
                        .IterateOverAllChildrenRecursively(transforms, includeSelf: true)
                )
                {
                    yield return child;
                }
            }
        }

        public static List<Transform> IterateOverAllChildrenRecursively(
            this Component component,
            List<Transform> buffer,
            bool includeSelf = false
        )
        {
            buffer.Clear();
            if (component == null)
            {
                return buffer;
            }

            Transform transform = component.transform;
            if (includeSelf)
            {
                buffer.Add(transform);
            }

            return transform.InternalIterateOverAllChildrenRecursively(buffer);
        }

        public static IEnumerable<Transform> IterateOverAllChildrenRecursivelyBreadthFirst(
            this Component component,
            bool includeSelf = false
        )
        {
            if (component == null)
            {
                yield break;
            }

            Transform transform = component.transform;
            if (includeSelf)
            {
                yield return transform;
            }

            using PooledResource<Queue<Transform>> queueResource = Buffers<Transform>.Queue.Get(
                out Queue<Transform> iteration
            );
            iteration.Enqueue(transform);
            while (iteration.TryDequeue(out Transform current))
            {
                for (int i = 0; i < current.childCount; ++i)
                {
                    Transform childTransform = current.GetChild(i);
                    iteration.Enqueue(childTransform);
                    yield return childTransform;
                }
            }
        }

        public static List<Transform> IterateOverAllChildrenRecursivelyBreadthFirst(
            this Component component,
            List<Transform> buffer,
            bool includeSelf = false
        )
        {
            return component.IterateOverAllChildrenRecursivelyBreadthFirst(
                buffer,
                includeSelf,
                maxDepth: 0
            );
        }

        public static List<Transform> IterateOverAllChildrenRecursivelyBreadthFirst(
            this Component component,
            List<Transform> buffer,
            bool includeSelf,
            int maxDepth
        )
        {
            buffer.Clear();
            if (component == null)
            {
                return buffer;
            }

            Transform transform = component.transform;
            if (includeSelf)
            {
                buffer.Add(transform);
            }

            if (maxDepth == 0)
            {
                maxDepth = int.MaxValue;
            }

            using PooledResource<Queue<(Transform, int)>> iterationResource = Buffers<(
                Transform,
                int
            )>.Queue.Get();
            Queue<(Transform transform, int depth)> iteration = iterationResource.resource;
            iteration.Enqueue((transform, 0));

            while (iteration.TryDequeue(out (Transform current, int depth) item))
            {
                for (int i = 0; i < item.current.childCount; ++i)
                {
                    Transform childTransform = item.current.GetChild(i);
                    int childDepth = item.depth + 1;

                    if (childDepth <= maxDepth)
                    {
                        buffer.Add(childTransform);
                        iteration.Enqueue((childTransform, childDepth));
                    }
                }
            }
            return buffer;
        }

        private static List<Transform> InternalIterateOverAllChildrenRecursively(
            this Transform transform,
            List<Transform> buffer
        )
        {
            for (int i = 0; i < transform.childCount; ++i)
            {
                Transform child = transform.GetChild(i);
                buffer.Add(child);
                child.InternalIterateOverAllChildrenRecursively(buffer);
            }

            return buffer;
        }
    }
}
