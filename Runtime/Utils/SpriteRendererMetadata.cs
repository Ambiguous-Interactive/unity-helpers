// MIT License - Copyright (c) 2024 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Utils
{
    using System.Collections.Generic;
    using Core.Attributes;
    using UnityEngine;

    /// <summary>
    ///     Keeps stack-like track of Colors and Materials of SpriteRenderers
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SpriteRendererMetadata : MonoBehaviour
    {
        private bool Enabled => enabled && gameObject.activeInHierarchy;

        private readonly List<(Component component, Color color)> _colorStack = new();
        private readonly List<(Component component, Material material)> _materialStack = new();

        private readonly List<(Component component, Color color)> _colorStackCache = new();
        private readonly List<(Component component, Material material)> _materialStackCache = new();

        public Color OriginalColor => _colorStack[0].color;

        public Color CurrentColor => _colorStack[^1].color;

        public Material OriginalMaterial => _materialStack[0].material;

        public Material CurrentMaterial => _materialStack[^1].material;

        public IEnumerable<Material> Materials
        {
            get
            {
                for (int i = _materialStack.Count - 1; 0 <= i; --i)
                {
                    yield return _materialStack[i].material;
                }
            }
        }

        public IEnumerable<Color> Colors
        {
            get
            {
                for (int i = _colorStack.Count - 1; 0 <= i; --i)
                {
                    yield return _colorStack[i].color;
                }
            }
        }

        [SiblingComponent]
        [SerializeField]
        private SpriteRenderer _spriteRenderer;

        private bool _enabled;

        public void PushColor(Component component, Color color, bool force = false)
        {
            if (component == this)
            {
                return;
            }

            if (!force && !Enabled)
            {
                return;
            }

            InternalPushColor(component, color);
        }

        private void InternalPushColor(Component component, Color color)
        {
            RemoveColor(component);
            _colorStack.Add((component, color));
            _spriteRenderer.color = CurrentColor;
        }

        public void PushBackColor(Component component, Color color, bool force = false)
        {
            if (component == this)
            {
                return;
            }

            if (!force && !Enabled)
            {
                return;
            }

            RemoveColor(component);
            _colorStack.Insert(1, (component, color));
            _spriteRenderer.color = CurrentColor;
        }

        public void PopColor(Component component)
        {
            RemoveColor(component);
            _spriteRenderer.color = CurrentColor;
        }

        public bool TryGetColor(Component component, out Color color)
        {
            foreach ((Component component, Color color) entry in _colorStack)
            {
                if (entry.component == component)
                {
                    color = entry.color;
                    return true;
                }
            }

            color = default;
            return false;
        }

        /// <summary>
        ///     Inserts a material as "first in the queue".
        /// </summary>
        /// <param name="component">Component that owns the material.</param>
        /// <param name="material">Material to use.</param>
        /// <param name="force">If true, overrides the enabled check.</param>
        /// <returns>The instanced material, if possible.</returns>
        public Material PushMaterial(Component component, Material material, bool force = false)
        {
            if (component == this)
            {
                return null;
            }

            if (!force && !Enabled)
            {
                return null;
            }

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                return null;
            }
#endif
            return InternalPushMaterial(component, material);
        }

        private Material InternalPushMaterial(Component component, Material material)
        {
            RemoveMaterial(component);
            _spriteRenderer.material = material;
            Material instanced = _spriteRenderer.material;
            _materialStack.Add((component, instanced));
            return instanced;
        }

        /// <summary>
        ///     Inserts a material as "last in the queue".
        /// </summary>
        /// <param name="component">Component that owns the material.</param>
        /// <param name="material">Material to use.</param>
        /// <param name="force">If true, overrides the enabled check.</param>
        /// <returns>The instanced material, if possible.</returns>
        public Material PushBackMaterial(Component component, Material material, bool force = false)
        {
            if (component == this)
            {
                return null;
            }

            if (!force && !Enabled)
            {
                return null;
            }

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                return null;
            }
#endif

            RemoveMaterial(component);
            Material instanced = material;
            if (_materialStack.Count <= 1)
            {
                _spriteRenderer.material = material;
                instanced = _spriteRenderer.material;
            }

            _materialStack.Insert(1, (component, instanced));
            return instanced;
        }

        public void PopMaterial(Component component)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                return;
            }
#endif

            RemoveMaterial(component);
            _spriteRenderer.material = CurrentMaterial;
            Material instanced = _spriteRenderer.material;
            Component currentComponent = _materialStack[^1].component;
            _materialStack[^1] = (currentComponent, instanced);
        }

        public bool TryGetMaterial(Component component, out Material material)
        {
            foreach ((Component component, Material material) entry in _materialStack)
            {
                if (entry.component == component)
                {
                    material = entry.material;
                    return true;
                }
            }

            material = default;
            return false;
        }

        private void Awake()
        {
            if (_spriteRenderer == null)
            {
                this.AssignSiblingComponents();
            }

            InternalPushColor(this, _spriteRenderer.color);
            _colorStackCache.AddRange(_colorStack);
            _ = InternalPushMaterial(this, _spriteRenderer.material);
            _materialStackCache.AddRange(_materialStack);
        }

        private void OnEnable()
        {
            // Ignore the OnEnable call from when the object is first initialized
            if (!_enabled)
            {
                _enabled = true;
                return;
            }

            _colorStack.Clear();
            if (0 < _colorStackCache.Count)
            {
                _colorStack.Add(_colorStackCache[0]);
            }

            using PooledResource<List<(Component component, Color color)>> colorBufferResource =
                Buffers<(Component component, Color color)>.List.Get(
                    out List<(Component component, Color color)> colorBuffer
                );
            colorBuffer.AddRange(_colorStackCache);
            for (int i = 1; i < colorBuffer.Count; ++i)
            {
                (Component component, Color color) entry = colorBuffer[i];
                PushColor(entry.component, entry.color, force: true);
            }

            _materialStack.Clear();
            if (0 < _materialStackCache.Count)
            {
                _materialStack.Add(_materialStackCache[0]);
            }

            using PooledResource<
                List<(Component component, Material material)>
            > materialBufferResource = Buffers<(Component component, Material material)>.List.Get(
                out List<(Component component, Material material)> materialBuffer
            );
            materialBuffer.AddRange(_materialStackCache);
            for (int i = 1; i < materialBuffer.Count; ++i)
            {
                (Component component, Material material) entry = materialBuffer[i];
                PushMaterial(entry.component, entry.material, force: true);
            }
        }

        private void OnDisable()
        {
            using PooledResource<List<(Component component, Color color)>> colorBufferResource =
                Buffers<(Component component, Color color)>.List.Get(
                    out List<(Component component, Color color)> colorBuffer
                );
            colorBuffer.AddRange(_colorStack);
            for (int i = colorBuffer.Count - 1; 1 <= i; --i)
            {
                PopColor(colorBuffer[i].component);
            }

            _colorStackCache.Clear();
            _colorStackCache.AddRange(colorBuffer);

            using PooledResource<
                List<(Component component, Material material)>
            > materialBufferResource = Buffers<(Component component, Material material)>.List.Get(
                out List<(Component component, Material material)> materialBuffer
            );
            materialBuffer.AddRange(_materialStack);

            for (int i = materialBuffer.Count - 1; 1 <= i; --i)
            {
                PopMaterial(materialBuffer[i].component);
            }

            _materialStackCache.Clear();
            _materialStackCache.AddRange(materialBuffer);
        }

        private void RemoveColor(Component component)
        {
            if (component == this)
            {
                return;
            }

            for (int i = _colorStack.Count - 1; 0 <= i; --i)
            {
                (Component component, Color color) stackEntry = _colorStack[i];
                if (stackEntry.component == component || stackEntry.component == null)
                {
                    _colorStack.RemoveAt(i);
                }
            }

            for (int i = _colorStackCache.Count - 1; 0 <= i; --i)
            {
                (Component component, Color color) stackEntry = _colorStackCache[i];
                if (stackEntry.component == component || stackEntry.component == null)
                {
                    _colorStackCache.RemoveAt(i);
                }
            }
        }

        private void RemoveMaterial(Component component)
        {
            if (component == this)
            {
                return;
            }

            for (int i = _materialStack.Count - 1; 0 <= i; --i)
            {
                (Component component, Material material) stackEntry = _materialStack[i];
                if (stackEntry.component == component || stackEntry.component == null)
                {
                    _materialStack.RemoveAt(i);
                }
            }

            for (int i = _materialStackCache.Count - 1; 0 <= i; --i)
            {
                (Component component, Material material) stackEntry = _materialStackCache[i];
                if (stackEntry.component == component || stackEntry.component == null)
                {
                    _materialStackCache.RemoveAt(i);
                }
            }
        }
    }
}
