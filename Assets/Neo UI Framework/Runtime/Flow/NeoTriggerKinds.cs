using System;
using System.Collections.Generic;
using UnityEngine;

namespace Neo.UI
{
    /// <summary>
    /// A consuming-project flow trigger kind: a novel way for a flow edge to advance (a gesture, an
    /// input action, proximity, a network event…) that the package's built-in
    /// <see cref="FlowTrigger.TriggerType"/> enum does not cover. Register one from a
    /// <c>[RuntimeInitializeOnLoadMethod]</c> and an <c>on: { yourKey: … }</c> edge in a spec will
    /// parse, serialize, connect and fire it — no package file edited.
    /// </summary>
    /// <remarks>
    /// Lives in <b>Runtime</b> (not Editor) because <see cref="Connect"/>/<see cref="Matches"/> run at
    /// play time. The editor spec tooling references it from the allowed Editor → Runtime direction.
    /// </remarks>
    public interface INeoTriggerKind
    {
        /// <summary> Stable id stored on <see cref="FlowTrigger.customKind"/> (e.g. "gesture"). </summary>
        string Id { get; }

        /// <summary> The single key the trigger uses in a spec's <c>on</c> object (e.g. "gesture"). </summary>
        string JsonKey { get; }

        /// <summary>
        /// Subscribes the listener to whatever stream this kind fires on. Implementations typically
        /// pick a <see cref="SignalStream"/> via <see cref="Signals.Stream(string,string)"/> and call
        /// <see cref="FlowTriggerListener.BindStream"/> so the listener delivers signals to
        /// <see cref="Matches"/>. <paramref name="fire"/> is the same callback the listener invokes on
        /// a match — provided for kinds that fire from a non-signal source (input action, coroutine).
        /// </summary>
        void Connect(FlowTriggerListener listener, Action fire);

        /// <summary> True when <paramref name="signal"/> satisfies this trigger for the listener. </summary>
        bool Matches(FlowTriggerListener listener, Signal signal);
    }

    /// <summary>
    /// Optional seam: a custom trigger kind that also tells the inspector which id database its
    /// category/name dropdown should offer (button ids, view ids, a project's own database…).
    /// Built-in triggers map this in <c>IdDatabaseOptions.ForTrigger</c>; a custom kind carries it.
    /// </summary>
    public interface ITriggerKindIdDatabase
    {
        /// <summary> The id-asset <see cref="System.Type"/> (e.g. <c>typeof(ButtonId)</c>) whose
        /// database the trigger's drawer should offer, or null for none. </summary>
        Type PreferredIdType { get; }
    }

    /// <summary>
    /// Pattern-R registry of project-supplied flow trigger kinds (runtime-side). The 8 built-in
    /// triggers keep their proven enum/switch path; only a project's novel kind flows through here,
    /// addressed by <see cref="FlowTrigger.TriggerType.Custom"/> + <see cref="FlowTrigger.customKind"/>.
    ///
    /// Seam-first (Phase 1): the registry starts empty and is seeded lazily — there are no built-in
    /// custom kinds to register. A project registers from <c>[RuntimeInitializeOnLoadMethod]</c> so the
    /// registration survives a domain reload.
    /// </summary>
    public static class NeoTriggerKinds
    {
        private static List<INeoTriggerKind> s_kinds;

        private static List<INeoTriggerKind> Kinds
        {
            get
            {
                // Lazy-seed so first access (editor or play) always has a live list, even right after
                // a domain reload cleared the statics before any RuntimeInitialize hook ran.
                if (s_kinds == null) s_kinds = new List<INeoTriggerKind>();
                return s_kinds;
            }
        }

        /// <summary> All registered custom trigger kinds (empty by default — built-ins are not here). </summary>
        public static IReadOnlyList<INeoTriggerKind> All => Kinds;

        /// <summary>
        /// Registers a custom trigger kind, replacing any existing one with the same <see cref="INeoTriggerKind.Id"/>
        /// (otherwise appended). Call from <c>[RuntimeInitializeOnLoadMethod]</c>.
        /// </summary>
        public static void Register(INeoTriggerKind kind)
        {
            if (kind == null)
            {
                Debug.LogWarning("NeoTriggerKinds.Register: ignoring null trigger kind.");
                return;
            }
            if (string.IsNullOrEmpty(kind.Id) || string.IsNullOrEmpty(kind.JsonKey))
            {
                Debug.LogWarning($"NeoTriggerKinds.Register: trigger kind '{kind.GetType().Name}' must have a non-empty Id and JsonKey; ignored.");
                return;
            }
            List<INeoTriggerKind> kinds = Kinds;
            for (int i = 0; i < kinds.Count; i++)
            {
                if (kinds[i].Id == kind.Id)
                {
                    kinds[i] = kind;
                    return;
                }
            }
            kinds.Add(kind);
        }

        /// <summary> Looks up a registered kind by its <see cref="INeoTriggerKind.Id"/>. </summary>
        public static bool TryGet(string id, out INeoTriggerKind kind)
        {
            kind = null;
            if (string.IsNullOrEmpty(id)) return false;
            List<INeoTriggerKind> kinds = Kinds;
            for (int i = 0; i < kinds.Count; i++)
            {
                if (kinds[i].Id == id)
                {
                    kind = kinds[i];
                    return true;
                }
            }
            return false;
        }

        /// <summary> Looks up a registered kind by the <c>on</c>-object key it parses (<see cref="INeoTriggerKind.JsonKey"/>). </summary>
        public static bool TryGetByKey(string jsonKey, out INeoTriggerKind kind)
        {
            kind = null;
            if (string.IsNullOrEmpty(jsonKey)) return false;
            List<INeoTriggerKind> kinds = Kinds;
            for (int i = 0; i < kinds.Count; i++)
            {
                if (kinds[i].JsonKey == jsonKey)
                {
                    kind = kinds[i];
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Removes the registered kind with the given <see cref="INeoTriggerKind.Id"/>, if any. The
        /// inverse of <see cref="Register"/> — handy for tests and for a project that conditionally
        /// enables a trigger kind.
        /// </summary>
        public static void Unregister(string id)
        {
            if (string.IsNullOrEmpty(id) || s_kinds == null) return;
            for (int i = s_kinds.Count - 1; i >= 0; i--)
                if (s_kinds[i].Id == id) s_kinds.RemoveAt(i);
        }
    }
}
