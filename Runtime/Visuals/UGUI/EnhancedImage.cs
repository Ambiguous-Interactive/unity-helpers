// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

/*
    Original implementation provided by JWoe
 */

namespace WallstopStudios.UnityHelpers.Visuals.UGUI
{
    using UnityEngine;
    using UnityEngine.Serialization;
    using UnityEngine.UI;

    /// <summary>
    /// Extends Unity's <see cref="Image"/> with per-instance material instancing, HDR color support, and optional shape mask textures.
    /// </summary>
    /// <remarks>
    /// <para>Assign a material that exposes a `_Color` property (for tint) and, optionally, a `_ShapeMask` texture slot. EnhancedImage duplicates that material at runtime so per-instance HDR adjustments stay local to the image.</para>
    /// <para>Upsides:</para>
    /// <list type="bullet">
    /// <item>
    /// <description>Automatically instantiates materials so HDR tints and mask assignments do not leak to other UI elements.</description>
    /// </item>
    /// <item>
    /// <description>Supports shape masks without relying on additional UI mask components, enabling custom shader workflows.</description>
    /// </item>
    /// <item>
    /// <description>Falls back to the underlying <see cref="Graphic.color"/> whenever HDR values are not required.</description>
    /// </item>
    /// </list>
    /// <para>Downsides:</para>
    /// <list type="bullet">
    /// <item>
    /// <description>Creates and manages a material copy per instance, which adds allocation and cleanup overhead.</description>
    /// </item>
    /// <item>
    /// <description>Requires shaders that expose the `_ShapeMask` slot to benefit from mask-driven outlines or wipes.</description>
    /// </item>
    /// </list>
    /// <para>Reach for <see cref="EnhancedImage"/> when you need per-control HDR highlights, stylised wipes, or shader-driven reveal effects. Prefer the stock <see cref="Image"/> when shared materials and low overhead matter more than these features.</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// using UnityEngine;
    /// using WallstopStudios.UnityHelpers.Visuals.UGUI;
    ///
    /// public sealed class AbilityIconPresenter : MonoBehaviour
    /// {
    ///     [SerializeField] private EnhancedImage icon;
    ///     [SerializeField] private Material abilityMaterial;
    ///
    ///     void Awake()
    ///     {
    ///         icon.material = Instantiate(abilityMaterial);
    ///         icon.HdrColor = new Color(1.6f, 1.2f, 0.6f, 1f);
    ///     }
    ///
    ///     public void SetCharge(float normalizedCharge)
    ///     {
    ///         float intensity = Mathf.Lerp(1f, 2.5f, normalizedCharge);
    ///         icon.HdrColor = new Color(intensity, 1f, 0.4f, 1f);
    ///     }
    /// }
    /// </code>
    /// </example>
    /// <seealso cref="Image"/>
    public sealed class EnhancedImage : Image
    {
        private static readonly int ShapeMaskPropertyID = Shader.PropertyToID("_ShapeMask");
        private static readonly int ColorPropertyID = Shader.PropertyToID("_Color");
        private static readonly int MainTex = Shader.PropertyToID("_MainTex");

        /// <summary>
        /// Stores the dedicated material instance produced by <see cref="UpdateMaterialInstance"/>.
        /// This is a runtime-only object that doesn't survive domain reloads.
        /// </summary>
        private Material _cachedMaterialInstance;
        internal Material CachedMaterialInstanceForTests => _cachedMaterialInstance;

        /// <summary>
        /// Stores the original user-assigned material before we replace it with our instance.
        /// Serialized so we can recreate the material instance after domain reload.
        /// </summary>
        [SerializeField]
        [HideInInspector]
        private Material _baseMaterial;

        /// <summary>
        /// HDR-capable tint applied to the instantiated material. Values above 1 keep their intensity instead of being clamped.
        /// </summary>
        /// <remarks>
        /// Changing this value refreshes the cached material instance, using <see cref="Graphic.color"/> whenever the supplied color remains within the standard dynamic range.
        /// </remarks>
        public Color HdrColor
        {
            get => _hdrColor;
            set
            {
                if (_hdrColor == value)
                {
                    return;
                }

                _hdrColor = value;
                UpdateMaterialInstance();
            }
        }

        /// <summary>
        /// Optional shape mask texture assigned to the material's `_ShapeMask` slot to drive custom shader based masking.
        /// </summary>
        /// <remarks>
        /// Mimics UI mask behaviour without additional components. Provide a shader that samples `_ShapeMask` alpha and multiplies it with the sprite alpha.
        /// </remarks>
        [FormerlySerializedAs("shapeMask")]
        [SerializeField]
        internal Texture2D _shapeMask;

        /// <summary>
        /// Backing field for <see cref="HdrColor"/>, persisted for inspector integration and HDR authoring.
        /// </summary>
        // HDR Color field that will override the base Image color if values set > 1
        [FormerlySerializedAs("hdrColor")]
        [SerializeField]
        [ColorUsage(showAlpha: true, hdr: true)]
        internal Color _hdrColor = Color.white;

        /// <inheritdoc/>
        protected override void Start()
        {
            base.Start();
            UpdateMaterialInstance();
        }

        /// <inheritdoc/>
        protected override void OnDestroy()
        {
            // Ensure our instance is released before base classes tear down internals
            CleanupMaterialInstance();
            base.OnDestroy();
        }

#if UNITY_EDITOR
        /// <inheritdoc/>
        protected override void OnValidate()
        {
            base.OnValidate();
            UpdateMaterialInstance();
        }

        /// <summary>
        /// Forces a refresh of the material instance. Used by the Editor to ensure
        /// changes are immediately reflected when properties are modified.
        /// </summary>
        internal void ForceRefreshMaterialInstance()
        {
            UpdateMaterialInstance();
        }
#endif

        /// <summary>
        /// Ensures this component owns a dedicated material instance and reapplies mask and color data.
        /// </summary>
        private void UpdateMaterialInstance()
        {
            Material currentMaterial = material;

            /*
                Handle case where our cached instance was destroyed (e.g., domain reload)
                but _baseMaterial is still valid. Restore from base material.
                This can happen when:
                1. currentMaterial is null (instance was destroyed)
                2. currentMaterial is the default material (Unity reverted to default after instance was destroyed)
                In both cases, if we have a valid _baseMaterial, we should restore from it.
            */
            bool currentIsNullOrDefault =
                currentMaterial == null || ReferenceEquals(currentMaterial, defaultGraphicMaterial);
            bool hasValidBaseMaterial =
                _baseMaterial != null && !ReferenceEquals(_baseMaterial, defaultGraphicMaterial);
            bool cachedInstanceIsInvalid = _cachedMaterialInstance == null;

            if (currentIsNullOrDefault && hasValidBaseMaterial && cachedInstanceIsInvalid)
            {
                currentMaterial = _baseMaterial;
            }

            /*
                Treat the built-in default UI material the same as "no material assigned"
                so tests that explicitly set material = null do not cause an instance to be created.
                BUT only if we don't have a valid base material to restore from.
            */
            if (currentMaterial == null || ReferenceEquals(currentMaterial, defaultGraphicMaterial))
            {
                return;
            }

            /*
                Determine the base material - either a new assignment or our stored reference
                If the current material is our cached instance, use the stored base
                If it's something else, that's the new base material
            */
            Material baseMaterial;
            if (_cachedMaterialInstance != null && currentMaterial == _cachedMaterialInstance)
            {
                // User hasn't changed the material, use stored base
                baseMaterial = _baseMaterial;
            }
            else
            {
                // User assigned a new material (or first time setup)
                baseMaterial = currentMaterial;
                _baseMaterial = baseMaterial;

                // Cleanup old instance since we have a new base
                if (_cachedMaterialInstance != null)
                {
                    DestroyImmediate(_cachedMaterialInstance);
                    _cachedMaterialInstance = null;
                }
            }

            // Safety check - if base material is null or default, bail
            if (baseMaterial == null || ReferenceEquals(baseMaterial, defaultGraphicMaterial))
            {
                return;
            }

            // Create new instance only if we don't have one
            if (_cachedMaterialInstance == null)
            {
                _cachedMaterialInstance = new Material(baseMaterial);
                /*
                    Use HideFlags to prevent the instance from being saved to scene/prefab
                    but allow it to survive within the current editor session.
                */
                _cachedMaterialInstance.hideFlags = HideFlags.HideAndDontSave;
            }

            /*
                Copy the sprite's texture to the material's _MainTex.
                Unity's Image component provides mainTexture from its sprite.
            */
            Texture spriteTexture = mainTexture;
            if (spriteTexture != null && _cachedMaterialInstance.HasProperty(MainTex))
            {
                _cachedMaterialInstance.SetTexture(MainTex, spriteTexture);
            }

            if (_shapeMask != null)
            {
                /*
                    If the shader does not expose _ShapeMask, try to swap to a helper shader
                    that defines the property so tests and editor UX remain predictable.
                */
                if (!_cachedMaterialInstance.HasProperty(ShapeMaskPropertyID))
                {
                    Shader fallback = Shader.Find("Hidden/Wallstop/EnhancedImageSupport");
                    if (fallback != null)
                    {
                        // Preserve commonly used properties when swapping shaders
                        Texture mainTex = _cachedMaterialInstance.HasProperty(MainTex)
                            ? _cachedMaterialInstance.GetTexture(MainTex)
                            : null;
                        Color currentColor = _cachedMaterialInstance.HasProperty(ColorPropertyID)
                            ? _cachedMaterialInstance.GetColor(ColorPropertyID)
                            : Color.white;

                        _cachedMaterialInstance.shader = fallback;

                        if (mainTex != null)
                        {
                            _cachedMaterialInstance.SetTexture(MainTex, mainTex);
                        }
                        _cachedMaterialInstance.SetColor(ColorPropertyID, currentColor);
                    }
                }

                if (_cachedMaterialInstance.HasProperty(ShapeMaskPropertyID))
                {
                    _cachedMaterialInstance.SetTexture(ShapeMaskPropertyID, _shapeMask);
                }
            }

            /*
                Always use _hdrColor for the material's color. The "HDR" in the name means
                it supports values > 1, not that it should be ignored for standard range colors.
            */
            _cachedMaterialInstance.SetColor(ColorPropertyID, _hdrColor);

            /*
                Assign the material if it changed. When the material reference is already
                our cached instance, the base setter exits early without calling SetMaterialDirty.
                We need to handle this case explicitly below.
            */
            bool materialChanged = material != _cachedMaterialInstance;
            if (materialChanged)
            {
                material = _cachedMaterialInstance;
            }

            /*
                Notify the canvas system that both the material and geometry need updating.
                SetAllDirty() triggers layout, geometry, and material rebuilds. This is more
                aggressive than SetMaterialDirty() + SetVerticesDirty() but ensures the Canvas
                system fully re-reads material properties and rebuilds the mesh.
                This is necessary because modifying material properties (via SetColor) doesn't
                automatically notify the Canvas system - only assigning a different material
                reference would trigger that. Since we're modifying an existing instance,
                we must explicitly request the full rebuild.
            */
            SetAllDirty();
        }

        /// <summary>
        /// Releases the cached material instance created for this image.
        /// </summary>
        private void CleanupMaterialInstance()
        {
            if (_cachedMaterialInstance != null)
            {
                // Use immediate destruction so references become fake-null right away
                DestroyImmediate(_cachedMaterialInstance);
                _cachedMaterialInstance = null;
            }
            _baseMaterial = null;
        }

        // Test helpers to avoid reflection
        internal void InvokeStartForTests() => Start();

        internal void InvokeOnDestroyForTests() => OnDestroy();

        internal Material BaseMaterialForTests => _baseMaterial;
    }
}
