// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Validation.Continuous
{
#if UNITY_EDITOR
    using System;
    using Object = UnityEngine.Object;

    /// <summary>
    /// One thing a <see cref="IValidationRule"/> found wrong with one asset.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="Id"/> is the finding's identity across runs, and it is composed rather than
    /// generated so a later slice can remember a suppression against it. It cannot depend on the
    /// asset's path -- a moved asset is the same finding -- nor on the message, which a package
    /// upgrade may reword. It is the rule, the asset's GUID, and whatever the rule uses to tell its
    /// own findings on one asset apart.
    /// </para>
    /// <para>
    /// <see cref="TryGetTarget"/> is how the object is read. The reference is captured while the
    /// run held the asset loaded, and Unity may have destroyed it since -- a domain reload, an
    /// unload, a reimport -- so the aliveness check belongs here rather than at every call site.
    /// </para>
    /// </remarks>
    public readonly struct ValidationFinding : IEquatable<ValidationFinding>
    {
        private readonly Object _target;
        private readonly string _id;

        /// <summary>
        /// Initializes a new instance of the <see cref="ValidationFinding"/> struct.
        /// </summary>
        /// <param name="ruleId">The reporting rule's stable identifier.</param>
        /// <param name="severity">How much the finding should interrupt a reader.</param>
        /// <param name="target">The asset or sub-object at fault; may be <c>null</c>.</param>
        /// <param name="assetGuid">The GUID of the asset the finding belongs to.</param>
        /// <param name="assetPath">The asset's project-relative path when it was found.</param>
        /// <param name="discriminator">
        /// What tells this finding apart from the same rule's other findings on the same asset --
        /// a field name, a member path, an index. May be <c>null</c> when a rule reports at most
        /// one finding per asset.
        /// </param>
        /// <param name="message">The human-readable description.</param>
        public ValidationFinding(
            string ruleId,
            ValidationSeverity severity,
            Object target,
            string assetGuid,
            string assetPath,
            string discriminator,
            string message
        )
        {
            RuleId = ruleId;
            Severity = severity;
            _target = target;
            AssetGuid = assetGuid;
            AssetPath = assetPath;
            Discriminator = discriminator;
            Message = message;
            _id = ruleId + "|" + assetGuid + "|" + discriminator;
        }

        /// <summary>The reporting rule's stable identifier.</summary>
        public string RuleId { get; }

        /// <summary>How much the finding should interrupt a reader.</summary>
        public ValidationSeverity Severity { get; }

        /// <summary>The GUID of the asset the finding belongs to.</summary>
        public string AssetGuid { get; }

        /// <summary>The asset's project-relative path as of the run that found it.</summary>
        public string AssetPath { get; }

        /// <summary>What tells this finding apart from the rule's others on the same asset.</summary>
        public string Discriminator { get; }

        /// <summary>The human-readable description.</summary>
        public string Message { get; }

        /// <summary>
        /// The finding's identity across runs: rule, asset GUID, and discriminator.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Path and message are deliberately excluded, so moving an asset or rewording a rule does
        /// not present an old finding as a new one.
        /// </para>
        /// <para>
        /// Built once in the constructor rather than on every read. It is read four times per
        /// rendered list row, once per <see cref="GetHashCode"/>, once per suppression test and
        /// once per report line, and each of those was a fresh string. The fallback keeps
        /// <c>default(ValidationFinding).Id</c> answering exactly what concatenating three nulls
        /// always answered.
        /// </para>
        /// </remarks>
        public string Id => _id ?? RuleId + "|" + AssetGuid + "|" + Discriminator;

        /// <summary>
        /// Resolves the object at fault, when Unity still has it.
        /// </summary>
        /// <param name="target">The live object, or <c>null</c>.</param>
        /// <returns><c>true</c> when <paramref name="target"/> is a live Unity object.</returns>
        public bool TryGetTarget(out Object target)
        {
            /*
                A destroyed Unity object is a live managed reference with a dead native pointer, so
                handing it back on the false path gives a caller who ignores the bool a
                MissingReferenceException. Answering null means the out parameter matches the return.
            */
            if (_target != null)
            {
                target = _target;
                return true;
            }

            target = null;
            return false;
        }

        /// <summary>Reports whether two findings say the same thing about the same object.</summary>
        /// <param name="other">The finding to compare against.</param>
        /// <returns><c>true</c> when every field matches and both name the same object.</returns>
        /// <remarks>
        /// Written out rather than left to the runtime, which would compare a struct holding a
        /// <see cref="Object"/> field reflectively. The target is compared by reference:
        /// <see cref="Object"/>'s own <c>==</c> is a liveness check, so two findings about one
        /// destroyed asset would otherwise read as findings about nothing.
        /// </remarks>
        public bool Equals(ValidationFinding other)
        {
            return Severity == other.Severity
                && ReferenceEquals(_target, other._target)
                && string.Equals(RuleId, other.RuleId, StringComparison.Ordinal)
                && string.Equals(AssetGuid, other.AssetGuid, StringComparison.Ordinal)
                && string.Equals(AssetPath, other.AssetPath, StringComparison.Ordinal)
                && string.Equals(Discriminator, other.Discriminator, StringComparison.Ordinal)
                && string.Equals(Message, other.Message, StringComparison.Ordinal);
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return obj is ValidationFinding other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return StringComparer.Ordinal.GetHashCode(Id) * 397 ^ (int)Severity;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            string where = string.IsNullOrEmpty(AssetPath) ? AssetGuid : AssetPath;
            return string.IsNullOrEmpty(Discriminator)
                ? $"[{Severity}] {RuleId}: {where} -- {Message}"
                : $"[{Severity}] {RuleId}: {where} ({Discriminator}) -- {Message}";
        }
    }
#endif
}
