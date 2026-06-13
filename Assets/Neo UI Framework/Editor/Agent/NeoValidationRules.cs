using System;
using System.Collections.Generic;
using UnityEngine;

namespace Neo.UI.Editor
{
    /// <summary>
    /// Which severity lane a validation rule runs in. The lanes mirror the existing split in
    /// <see cref="AgentValidation"/>: <see cref="Hard"/> rules are contracts surfaced by
    /// <see cref="AgentValidation.ValidateAll"/> (they can fail a build); <see cref="Design"/> and
    /// <see cref="Interactivity"/> rules are soft warnings surfaced as <c>designWarnings</c> /
    /// dead-interaction notes and never run inside <see cref="AgentValidation.ValidateAll"/>.
    /// </summary>
    public enum ValidationBucket { Hard, Design, Interactivity }

    /// <summary>
    /// Everything a registered rule needs to do its job. <see cref="AgentValidation"/> hands the
    /// same context it already built (settings + theme) and the rule reports findings through
    /// <see cref="Add"/> — there is no second list to merge, so a custom rule's findings land in the
    /// same place the built-ins write to (issues for Hard, warnings for Design/Interactivity).
    /// </summary>
    public sealed class ValidationContext
    {
        /// <summary> The package settings under test. Never null (the caller guards). </summary>
        public NeoUISettings Settings { get; }

        /// <summary> Convenience accessor for the active theme (may be null). </summary>
        public Theme Theme => Settings != null ? Settings.theme : null;

        /// <summary> The generated view prefabs the built-ins also walk (UIView roots only). </summary>
        public IReadOnlyList<GameObject> GeneratedViews { get; }

        private readonly List<string> _output;

        public ValidationContext(NeoUISettings settings, IReadOnlyList<GameObject> generatedViews, List<string> output)
        {
            Settings = settings;
            GeneratedViews = generatedViews ?? Array.Empty<GameObject>();
            _output = output;
        }

        /// <summary> Report a finding. It lands in the same list the built-in checks write to. </summary>
        public void Add(string message)
        {
            if (!string.IsNullOrEmpty(message)) _output.Add(message);
        }
    }

    /// <summary>
    /// A project-supplied validation rule. Implement this and register it from
    /// <c>[InitializeOnLoad]</c> to add a check the package can't ship out of the box. The
    /// <see cref="Bucket"/> decides where it runs — a <see cref="ValidationBucket.Design"/> rule
    /// only fires in the soft design lint, a <see cref="ValidationBucket.Hard"/> rule fires in the
    /// hard contracts. Built-in checks are NOT expressed as rules (they stay inline in
    /// <see cref="AgentValidation"/> so a missing registration can never weaken a contract).
    /// </summary>
    public interface INeoValidationRule
    {
        ValidationBucket Bucket { get; }
        void Validate(ValidationContext ctx);
    }

    /// <summary>
    /// Pattern-R registry of project validation rules (see
    /// <c>extensibility-seams-master-plan.md</c>). The package ships ZERO built-in rules through
    /// here — the built-ins stay inline in <see cref="AgentValidation"/>. This is purely the seam a
    /// consuming project extends through. Editor-only single domain, so a plain static list is fine.
    /// </summary>
    public static class NeoValidationRules
    {
        private static readonly List<INeoValidationRule> _rules = new();

        /// <summary> Every registered rule, in registration order. </summary>
        public static IReadOnlyList<INeoValidationRule> All => _rules;

        /// <summary> Register a rule. Idempotent per-instance (the same instance is not added twice). </summary>
        public static void Register(INeoValidationRule rule)
        {
            if (rule == null)
            {
                Debug.LogWarning("[Neo.UI] NeoValidationRules.Register called with a null rule — ignored.");
                return;
            }
            if (!_rules.Contains(rule)) _rules.Add(rule);
        }

        /// <summary> Remove a previously registered rule (mostly for tests / live re-registration). </summary>
        public static bool Unregister(INeoValidationRule rule) => _rules.Remove(rule);

        /// <summary> Run every registered rule in <paramref name="bucket"/> against <paramref name="ctx"/>. </summary>
        internal static void Run(ValidationBucket bucket, ValidationContext ctx)
        {
            // Index loop so a rule registering another rule mid-run doesn't throw.
            for (int i = 0; i < _rules.Count; i++)
            {
                INeoValidationRule rule = _rules[i];
                if (rule.Bucket != bucket) continue;
                try
                {
                    rule.Validate(ctx);
                }
                catch (Exception e)
                {
                    // No silent failures: a thrown rule is surfaced, not swallowed.
                    Debug.LogWarning($"[Neo.UI] Validation rule {rule.GetType().Name} threw: {e.Message}");
                }
            }
        }
    }

    /// <summary>
    /// The lighter of the two interactivity seams from the plan: a project registers a predicate
    /// that claims a GameObject is "wired" (does something when clicked). The dead-interaction lint
    /// OR-s these into its built-in checks, so a project's custom wiring component (e.g. "opens a
    /// URL") stops being falsely flagged dead — without the project implementing a full rule.
    /// Registered from <c>[InitializeOnLoad]</c>. See <c>CLAUDE.md</c> → Dead-interaction lint.
    /// </summary>
    public static class NeoInteractivityProviders
    {
        private static readonly List<Func<GameObject, bool>> _providers = new();

        public static IReadOnlyList<Func<GameObject, bool>> All => _providers;

        /// <summary> Register a "is this object wired?" predicate. </summary>
        public static void Register(Func<GameObject, bool> provider)
        {
            if (provider == null)
            {
                Debug.LogWarning("[Neo.UI] NeoInteractivityProviders.Register called with a null provider — ignored.");
                return;
            }
            if (!_providers.Contains(provider)) _providers.Add(provider);
        }

        /// <summary> Remove a previously registered provider (mostly for tests). </summary>
        public static bool Unregister(Func<GameObject, bool> provider) => _providers.Remove(provider);

        /// <summary> True if ANY registered provider claims <paramref name="go"/> is wired. </summary>
        internal static bool ClaimsWired(GameObject go)
        {
            if (go == null) return false;
            for (int i = 0; i < _providers.Count; i++)
            {
                try
                {
                    if (_providers[i](go)) return true;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Neo.UI] Interactivity provider threw: {e.Message}");
                }
            }
            return false;
        }
    }
}
