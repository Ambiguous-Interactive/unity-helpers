// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Utils
{
#if UNITY_EDITOR
    using System;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Utils;

    /// <summary>Collapses an isolated Undo group when its scope ends.</summary>
    /// <remarks>Use on the editor thread. Copies share one disposal claim; a default scope does nothing. Reused leases allocate no managed memory.</remarks>
    public readonly struct UndoGroupScope : IDisposable, IEquatable<UndoGroupScope>
    {
        /// <summary>Reports whether this scope still owns its group cleanup.</summary>
        public bool IsHeld => _lease.IsHeld;

        private readonly int _group;
        private readonly DisposalLease _lease;

        private UndoGroupScope(int group, DisposalLease lease)
        {
            _group = group;
            _lease = lease;
        }

        /// <summary>Begins a named group, or returns an inactive scope when creation fails.</summary>
        public static UndoGroupScope Begin(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return default;
            }
            try
            {
                Undo.IncrementCurrentGroup();
                int group = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName(name);
                DisposalLease lease = DisposalLeases.Acquire();
                return new UndoGroupScope(group, lease);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                return default;
            }
        }

        /// <summary>Collapses this group once and isolates following operations.</summary>
        public void Dispose()
        {
            if (!_lease.TryClaim())
            {
                return;
            }
            try
            {
                Undo.CollapseUndoOperations(_group);
                Undo.IncrementCurrentGroup();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        /// <summary>Compares group ownership.</summary>
        public bool Equals(UndoGroupScope other)
        {
            return _group == other._group && _lease.Equals(other._lease);
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return obj is UndoGroupScope other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return Objects.HashCode(_group, _lease);
        }
    }
#endif
}
