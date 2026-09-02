// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Validation.Continuous.Rules
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using UnityEngine;
    using Object = UnityEngine.Object;

    /// <summary>
    /// Reports every animation keyframe whose object reference no longer resolves.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The continuous half of <see cref="AnimationClipKeyframeValidator"/>. It is the one shipped
    /// rule that needs the loaded asset: a keyframe's guid can resolve perfectly while the object
    /// does not, because a sheet re-imported as <c>Single</c> keeps a <c>.meta</c> describing slices
    /// the importer no longer produces, so text cannot answer this question.
    /// </para>
    /// <para>
    /// <b>Severity is <see cref="ValidationSeverity.Warning"/>.</b> An empty object keyframe is a
    /// lost reference often enough to be worth reporting and an authored one often enough not to be
    /// an error: animating a renderer's sprite to nothing is how a frame is deliberately blanked.
    /// The clip loads and plays either way, which is the line between warning and error.
    /// </para>
    /// <para>
    /// A run hands a rule one main asset, so this sees <c>.anim</c> clips. A clip imported inside a
    /// model file is a sub-asset of a <c>GameObject</c> and is not claimed; the menu command reads
    /// those, through <c>LoadAllAssetsAtPath</c>.
    /// </para>
    /// </remarks>
    public sealed class AnimationKeyframeRule : IValidationRule
    {
        private readonly List<AnimationKeyframeFinding> _found =
            new List<AnimationKeyframeFinding>();

        private readonly Dictionary<string, int> _occurrences = new Dictionary<string, int>(
            StringComparer.Ordinal
        );

        /// <inheritdoc />
        public string RuleId => ValidationRuleIds.AnimationKeyframeEmpty;

        /// <inheritdoc />
        public string DisplayName => "Animation keyframes resolve";

        /// <inheritdoc />
        public bool AppliesTo(in ValidationTarget target)
        {
            return typeof(AnimationClip).IsAssignableFrom(target.MainAssetType);
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

            AnimationClip clip = asset as AnimationClip;
            if (clip == null)
            {
                findings.Add(
                    ValidationCoverage.Unreadable(
                        RuleId,
                        in target,
                        "the asset database named this path as a clip and handed back none"
                    )
                );
                return;
            }

            _found.Clear();
            _occurrences.Clear();
            AnimationClipKeyframeValidator.Inspect(target.AssetPath, clip, _found);

            for (int index = 0; index < _found.Count; ++index)
            {
                AnimationKeyframeFinding found = _found[index];
                string seconds = found.Time.ToString("0.###", CultureInfo.InvariantCulture);
                string curve = found.BindingPath + "/" + found.PropertyName + "@" + seconds;

                findings.Add(
                    new ValidationFinding(
                        RuleId,
                        ValidationSeverity.Warning,
                        clip,
                        target.AssetGuid,
                        target.AssetPath,
                        ValidationDiscriminators.Occurrence(_occurrences, curve),
                        curve
                            + " resolves to nothing, so the subject vanishes for that frame's "
                            + "duration and comes back."
                    )
                );
            }
        }
    }
#endif
}
