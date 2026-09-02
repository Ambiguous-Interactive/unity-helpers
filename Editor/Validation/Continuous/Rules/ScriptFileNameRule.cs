// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Validation.Continuous.Rules
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.IO;
    using UnityEditor;
    using Object = UnityEngine.Object;

    /// <summary>
    /// Reports every script asset that is not named after the authorable type it binds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The continuous half of <see cref="MonoScriptBindingValidator"/>'s second rule -- the cause
    /// rather than the symptom. Unity picks the class for a file by name and falls back silently
    /// when nothing matches, so a file declaring two types binds one of them by declaration order:
    /// add a type above and the binding moves, and every asset referencing the old one becomes a
    /// missing script.
    /// </para>
    /// <para>
    /// The first rule -- every concrete <c>MonoBehaviour</c> and <c>ScriptableObject</c> resolves to
    /// a <c>MonoScript</c> -- is a question about the type index rather than about any one asset, so
    /// it has no <see cref="ValidationTarget"/> to be asked against and stays a menu command.
    /// </para>
    /// <para>
    /// <b>Severity is <see cref="ValidationSeverity.Warning"/>.</b> Nothing is broken today: the
    /// binding Unity picked works, and the project runs. What is wrong is that it was decided by
    /// accident and one unrelated edit moves it.
    /// </para>
    /// </remarks>
    public sealed class ScriptFileNameRule : IValidationRule
    {
        /// <inheritdoc />
        public string RuleId => ValidationRuleIds.ScriptFileNameMismatch;

        /// <inheritdoc />
        public string DisplayName => "Script files are named after what they bind";

        /// <inheritdoc />
        public bool AppliesTo(in ValidationTarget target)
        {
            return typeof(MonoScript).IsAssignableFrom(target.MainAssetType);
        }

        /// <inheritdoc />
        public void Validate(
            in ValidationTarget target,
            Object asset,
            List<ValidationFinding> findings
        )
        {
            if (findings == null || !target.IsValid())
            {
                return;
            }

            MonoScript script = asset as MonoScript;
            if (script == null)
            {
                findings.Add(
                    ValidationCoverage.Unreadable(
                        RuleId,
                        in target,
                        "the asset database named this path as a script and handed back none"
                    )
                );
                return;
            }

            Type bound = script.GetClass();
            if (!MonoScriptBindingValidator.IsAuthorable(bound))
            {
                return;
            }

            string fileName = Path.GetFileNameWithoutExtension(target.AssetPath);
            string typeName = MonoScriptBindingValidator.SimpleNameOf(bound);
            if (string.Equals(fileName, typeName, StringComparison.Ordinal))
            {
                return;
            }

            findings.Add(
                new ValidationFinding(
                    RuleId,
                    ValidationSeverity.Warning,
                    script,
                    target.AssetGuid,
                    target.AssetPath,
                    null,
                    "this file binds "
                        + bound.FullName
                        + ", which it is not named after. The binding is decided by declaration "
                        + "order, so one type added above it moves the binding and every reference "
                        + "to it becomes a missing script."
                )
            );
        }
    }
#endif
}
