// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tags
{
    using System;
    using System.Collections.Generic;
    using Core.Helper;
    using UnityEngine;
    using Utils;

    /// <summary>
    /// Prefab-like container for visual/audio behaviors that represent an effect's cosmetic feedback.
    /// Groups one or more <see cref="CosmeticEffectComponent"/>s and declares if instancing is required.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Role in the system: <see cref="AttributeEffect"/> references one or more CosmeticEffectData assets.
    /// When the effect is applied, <see cref="EffectHandler"/> will either:
    /// - Reuse the existing CosmeticEffectData on the target (RequiresInstancing = false), OR
    /// - Instantiate a copy and parent it to the target (RequiresInstancing = true).
    /// On removal, corresponding cosmetic components receive <see cref="CosmeticEffectComponent.OnRemoveEffect"/>.
    /// </para>
    /// <para>
    /// Problems solved:
    /// - Decouple gameplay logic from presentation.
    /// - Support shared cosmetic presenters (e.g., a single status icon) or per‑instance visuals (e.g., particle emitters).
    /// - Automatic lifecycle management (instantiation and cleanup) alongside effect application/removal.
    /// </para>
    /// <para>
    /// Authoring pattern:
    /// - Create a prefab with a CosmeticEffectData + one or more CosmeticEffectComponent scripts.
    /// - Mark a component's <see cref="CosmeticEffectComponent.RequiresInstance"/> true if a unique instance per effect is needed.
    /// - Reference the prefab in your <see cref="AttributeEffect.cosmeticEffects"/> list.
    /// </para>
    /// <para>
    /// Example:
    /// <code>
    /// // PoisonEffectData (Prefab)
    /// //  - CosmeticEffectData
    /// //  - PoisonParticles : CosmeticEffectComponent (RequiresInstance = true)
    /// //  - PoisonIcon : CosmeticEffectComponent (shared UI, RequiresInstance = false)
    ///
    /// // In AttributeEffect: cosmeticEffects = [ PoisonEffectData ]
    /// // EffectHandler will instance PoisonParticles per application and reuse PoisonIcon as needed.
    /// </code>
    /// </para>
    /// <para>
    /// Tips:
    /// - Prefer shared presenters when possible (fewer instantiations).
    /// - If a component animates its own teardown, set <see cref="CosmeticEffectComponent.CleansUpSelf"/> to true.
    /// - Keep CosmeticEffectData lightweight; heavy content belongs in the child components.
    /// </para>
    /// <para>
    /// <strong>Warning:</strong> All methods on this class reflect the current component state.
    /// If components are added or removed after an instance is placed in a hash-based collection
    /// (e.g., <see cref="Dictionary{TKey,TValue}"/> or <see cref="HashSet{T}"/>), the collection
    /// may behave unexpectedly because <see cref="GetHashCode"/> and <see cref="Equals(CosmeticEffectData)"/>
    /// will return different values than when the instance was added.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class CosmeticEffectData : MonoBehaviour, IEquatable<CosmeticEffectData>
    {
        /// <summary>
        /// Indicates whether this cosmetic effect requires a new instance for each application.
        /// Returns true if any child CosmeticEffectComponent requires instancing.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This property always reflects the current state of attached components.
        /// It uses Unity's non-allocating <c>GetComponents(List)</c> overload with pooled lists
        /// to achieve zero allocations while ensuring correctness when components are added or removed.
        /// </para>
        /// <para>
        /// Destroyed Unity objects are safely skipped during iteration.
        /// </para>
        /// </remarks>
        public bool RequiresInstancing
        {
            get
            {
                using PooledResource<List<CosmeticEffectComponent>> lease =
                    Buffers<CosmeticEffectComponent>.List.Get(
                        out List<CosmeticEffectComponent> cosmetics
                    );
                GetComponents(cosmetics);
                for (int i = 0; i < cosmetics.Count; i++)
                {
                    CosmeticEffectComponent cosmetic = cosmetics[i];
                    if (cosmetic == null)
                    {
                        continue;
                    }
                    if (cosmetic.RequiresInstance)
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        /// <summary>
        /// Populates the provided set with the types of all currently attached <see cref="CosmeticEffectComponent"/>s.
        /// </summary>
        /// <param name="types">The set to populate. Will be cleared before adding types.</param>
        /// <remarks>
        /// Uses pooled lists for zero-allocation queries. Destroyed Unity objects are safely skipped.
        /// </remarks>
        private void GetCurrentCosmeticTypes(HashSet<Type> types)
        {
            types.Clear();
            using PooledResource<List<CosmeticEffectComponent>> lease =
                Buffers<CosmeticEffectComponent>.List.Get(
                    out List<CosmeticEffectComponent> cosmetics
                );
            GetComponents(cosmetics);
            for (int i = 0; i < cosmetics.Count; i++)
            {
                CosmeticEffectComponent cosmetic = cosmetics[i];
                if (cosmetic == null)
                {
                    continue;
                }
                types.Add(cosmetic.GetType());
            }
        }

        /// <summary>
        /// Determines whether this instance is equal to another object.
        /// </summary>
        /// <param name="other">The object to compare with.</param>
        /// <returns><c>true</c> when <paramref name="other"/> is a <see cref="CosmeticEffectData"/> with matching components and name; otherwise, <c>false</c>.</returns>
        public override bool Equals(object other)
        {
            return other is CosmeticEffectData cosmeticEffectData && Equals(cosmeticEffectData);
        }

        /// <summary>
        /// Determines whether this instance is equal to another <see cref="CosmeticEffectData"/>.
        /// Equality compares the current set of contained <see cref="CosmeticEffectComponent"/> types and the GameObject name.
        /// </summary>
        /// <param name="other">The other cosmetic effect data to compare.</param>
        /// <returns><c>true</c> if both assets expose the same component types and share the same name; otherwise, <c>false</c>.</returns>
        /// <remarks>
        /// This method reflects the current component state at the time of the call.
        /// Uses pooled HashSets for zero-allocation comparison.
        /// </remarks>
        public bool Equals(CosmeticEffectData other)
        {
            if (ReferenceEquals(this, other))
            {
                return true;
            }

            if (other == null)
            {
                return false;
            }

            using PooledResource<HashSet<Type>> thisLease = Buffers<Type>.HashSet.Get(
                out HashSet<Type> thisTypes
            );
            using PooledResource<HashSet<Type>> otherLease = Buffers<Type>.HashSet.Get(
                out HashSet<Type> otherTypes
            );

            GetCurrentCosmeticTypes(thisTypes);
            other.GetCurrentCosmeticTypes(otherTypes);

            if (!thisTypes.SetEquals(otherTypes))
            {
                return false;
            }

            return Helpers.NameEquals(this, other);
        }

        /// <summary>
        /// Returns a hash code derived from exactly what <see cref="Equals(CosmeticEffectData)"/>
        /// compares: the name and the order-independent set of attached component types.
        /// </summary>
        /// <returns>A hash code suitable for use in hash-based collections.</returns>
        /// <remarks>
        /// <para>
        /// This method reflects the current component state at the time of the call.
        /// If components are added or removed, the hash code will change.
        /// </para>
        /// <para>
        /// Hashing the component <em>count</em> would disagree with equality, because
        /// <see cref="CosmeticEffectComponent"/> carries no <c>[DisallowMultipleComponent]</c>: two
        /// copies of one component and a single copy expose the same type set, so equality reports
        /// them equal while the counts differ.
        /// </para>
        /// </remarks>
        public override int GetHashCode()
        {
            using PooledResource<HashSet<Type>> lease = Buffers<Type>.HashSet.Get(
                out HashSet<Type> types
            );
            GetCurrentCosmeticTypes(types);

            /*
                XOR, so the contribution is a property of the set rather than of the iteration order
                GetComponents happens to return.
            */
            int typeHash = 0;
            foreach (Type type in types)
            {
                typeHash ^= type.GetHashCode();
            }

            return Objects.HashCode(Helpers.NameHashCode(this), typeHash);
        }
    }
}
