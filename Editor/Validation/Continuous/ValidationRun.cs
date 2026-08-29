// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Validation.Continuous
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using UnityEditor;
    using Object = UnityEngine.Object;

    /// <summary>
    /// A validation pass over a fixed set of assets, advanced in bounded slices.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The run does not schedule itself. A caller drives <see cref="Step"/> with the time it is
    /// willing to give up -- from <c>EditorApplication.update</c>, from a progress bar, or from a
    /// test loop -- which is what makes the engine testable without an editor tick and what keeps a
    /// project-wide scan from stalling the editor the way an unbounded one did.
    /// </para>
    /// <para>
    /// Work is ordered asset-major: for each asset, every rule that claims it runs before the next
    /// asset is considered. An asset is therefore loaded at most once per run, and only when some
    /// rule asked for it.
    /// </para>
    /// <para>
    /// Nothing here throws. A malformed argument produces an empty, already-complete run, and a rule
    /// that throws is recorded in <see cref="Failures"/> while the run continues.
    /// </para>
    /// </remarks>
    public sealed class ValidationRun
    {
        private static readonly IValidationRule[] NoRules = Array.Empty<IValidationRule>();
        private static readonly ValidationTarget[] NoTargets = Array.Empty<ValidationTarget>();

        private readonly IValidationRule[] _rules;
        private readonly ValidationTarget[] _targets;
        private readonly Func<ValidationTarget, Object> _loader;
        private readonly List<ValidationFinding> _findings = new List<ValidationFinding>();
        private readonly List<ValidationRuleFailure> _failures = new List<ValidationRuleFailure>();
        private readonly List<ValidationFinding> _scratch = new List<ValidationFinding>();

        private int _nextTarget;
        private bool _cancelled;

        /// <summary>
        /// Initializes a run that loads assets through Unity's asset database.
        /// </summary>
        /// <param name="rules">The rules to apply. <c>null</c> entries are ignored.</param>
        /// <param name="targets">The assets to consider. Invalid entries are ignored.</param>
        public ValidationRun(
            IReadOnlyList<IValidationRule> rules,
            IReadOnlyList<ValidationTarget> targets
        )
            : this(rules, targets, LoadMainAsset) { }

        /// <summary>
        /// Initializes a run that loads assets through <paramref name="loader"/>.
        /// </summary>
        /// <param name="rules">The rules to apply. <c>null</c> entries are ignored.</param>
        /// <param name="targets">The assets to consider. Invalid entries are ignored.</param>
        /// <param name="loader">
        /// How to load an asset a rule claimed. A test supplies its own so the engine can be driven
        /// without an asset database; <c>null</c> falls back to the asset database.
        /// </param>
        public ValidationRun(
            IReadOnlyList<IValidationRule> rules,
            IReadOnlyList<ValidationTarget> targets,
            Func<ValidationTarget, Object> loader
        )
        {
            _rules = Compact(rules);
            _targets = Compact(targets);
            _loader = loader ?? LoadMainAsset;
        }

        /// <summary>How many assets this run will consider.</summary>
        public int TotalCount => _targets.Length;

        /// <summary>How many assets it has considered so far.</summary>
        public int ProcessedCount => _nextTarget;

        /// <summary>
        /// Whether every asset has been considered, or the run was cancelled.
        /// </summary>
        public bool IsComplete => _cancelled || _targets.Length <= _nextTarget;

        /// <summary>Whether <see cref="Cancel"/> ended the run before it finished.</summary>
        public bool IsCancelled => _cancelled;

        /// <summary>Everything the rules have found so far, in the order it was reported.</summary>
        public IReadOnlyList<ValidationFinding> Findings => _findings;

        /// <summary>Every rule that threw, and the asset it threw on.</summary>
        public IReadOnlyList<ValidationRuleFailure> Failures => _failures;

        /// <summary>
        /// Advances the run for up to <paramref name="budgetMilliseconds"/> of wall time.
        /// </summary>
        /// <param name="budgetMilliseconds">
        /// How long the caller is willing to block. One asset is always processed, however small
        /// this is, so a caller cannot accidentally configure a run that never finishes.
        /// </param>
        /// <returns><c>true</c> when the run is complete.</returns>
        public bool Step(double budgetMilliseconds)
        {
            if (IsComplete)
            {
                return true;
            }

            // Timestamps rather than a Stopwatch instance: a driver calls this on every editor
            // tick, and the run should not allocate to find out what time it is.
            long started = Stopwatch.GetTimestamp();
            double budgetTicks = budgetMilliseconds * Stopwatch.Frequency / 1000.0;
            do
            {
                ProcessOneTarget(_targets[_nextTarget]);
                _nextTarget++;
            } while (!IsComplete && Stopwatch.GetTimestamp() - started < budgetTicks);

            return IsComplete;
        }

        /// <summary>
        /// Ends the run where it stands. Findings already collected are kept.
        /// </summary>
        public void Cancel()
        {
            _cancelled = true;
        }

        private void ProcessOneTarget(ValidationTarget target)
        {
            bool loaded = false;
            Object asset = null;

            foreach (IValidationRule rule in _rules)
            {
                if (!Claims(rule, target))
                {
                    continue;
                }

                if (!loaded)
                {
                    loaded = true;
                    asset = Load(target);
                }

                Apply(rule, target, asset);
            }
        }

        private bool Claims(IValidationRule rule, ValidationTarget target)
        {
            try
            {
                return rule.AppliesTo(in target);
            }
            catch (Exception thrown)
            {
                _failures.Add(new ValidationRuleFailure(RuleIdOf(rule), target.AssetPath, thrown));
                return false;
            }
        }

        private Object Load(ValidationTarget target)
        {
            try
            {
                return _loader(target);
            }
            catch (Exception thrown)
            {
                _failures.Add(new ValidationRuleFailure(null, target.AssetPath, thrown));
                return null;
            }
        }

        private void Apply(IValidationRule rule, ValidationTarget target, Object asset)
        {
            _scratch.Clear();
            try
            {
                rule.Validate(in target, asset, _scratch);
            }
            catch (Exception thrown)
            {
                _failures.Add(new ValidationRuleFailure(RuleIdOf(rule), target.AssetPath, thrown));
                // Whatever the rule appended before it threw is an incomplete answer for this
                // asset, and reporting half of one reads as a complete one. The failure is the
                // result.
                return;
            }

            _findings.AddRange(_scratch);
        }

        private static string RuleIdOf(IValidationRule rule)
        {
            try
            {
                return rule.RuleId;
            }
            catch (Exception)
            {
                return rule.GetType().FullName;
            }
        }

        private static Object LoadMainAsset(ValidationTarget target)
        {
            return AssetDatabase.LoadMainAssetAtPath(target.AssetPath);
        }

        private static IValidationRule[] Compact(IReadOnlyList<IValidationRule> rules)
        {
            if (rules == null)
            {
                return NoRules;
            }

            List<IValidationRule> kept = new List<IValidationRule>(rules.Count);
            for (int index = 0; index < rules.Count; index++)
            {
                IValidationRule rule = rules[index];
                if (rule != null)
                {
                    kept.Add(rule);
                }
            }

            return kept.Count == 0 ? NoRules : kept.ToArray();
        }

        private static ValidationTarget[] Compact(IReadOnlyList<ValidationTarget> targets)
        {
            if (targets == null)
            {
                return NoTargets;
            }

            List<ValidationTarget> kept = new List<ValidationTarget>(targets.Count);
            for (int index = 0; index < targets.Count; index++)
            {
                ValidationTarget target = targets[index];
                if (target.IsValid())
                {
                    kept.Add(target);
                }
            }

            return kept.Count == 0 ? NoTargets : kept.ToArray();
        }
    }
#endif
}
