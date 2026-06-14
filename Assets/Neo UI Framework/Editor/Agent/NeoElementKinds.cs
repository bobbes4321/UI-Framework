using System.Collections.Generic;
using Neo.UI.Editor.Composer;
using UnityEngine;

namespace Neo.UI.Editor
{
    /// <summary>
    /// The context a custom <see cref="INeoElementKind"/> needs to materialize itself, mirroring the
    /// locals the built-in <c>UISpecGenerator.BuildElementTree</c> switch cases use. A provider builds
    /// (and parents) its root GameObject under <see cref="parent"/> from <see cref="element"/>, registers
    /// any addressable ids through <see cref="RegisterId"/>/<see cref="RegisterStreamId"/>, and reports
    /// problems through <see cref="report"/> (never fail silently). The shared geometry/anchor/child pass
    /// in the generator still runs on whatever GameObject the provider returns, so a provider only owns
    /// its widget's internals — not its placement.
    /// </summary>
    public sealed class ElementBuildContext
    {
        /// <summary> The RectTransform the new element should be created under. </summary>
        public RectTransform parent;
        /// <summary> The spec node being built. </summary>
        public ElementSpec element;
        /// <summary> Sibling index within the parent (for default naming / placement). </summary>
        public int index;
        /// <summary> True when the parent is a layout group (sizes ride a LayoutElement, anchors are skipped). </summary>
        public bool inLayout;
        /// <summary> The active settings asset (id databases live here). </summary>
        public NeoUISettings settings;
        /// <summary> The run report — add issues here; an unmatched lookup must warn, never no-op. </summary>
        public GenerateReport report;

        /// <summary> Category/Name parsed from <see cref="element"/>.id (null when the element has no id). </summary>
        public string category;
        public string name;

        // The generator's id-registration helpers, surfaced so a provider registers exactly like a
        // built-in does (button/toggle/etc.). Set by the generator when it constructs the context.
        internal System.Action<IdDatabase, string, string> registerId;
        internal System.Action<GameObject, string, ViewCommandOnClick.Command> addViewCommand;

        /// <summary> Registers an addressable id into one of the settings databases (e.g. buttonIds). </summary>
        public void RegisterId(IdDatabase database, string idCategory, string idName)
            => registerId?.Invoke(database, idCategory, idName);

        /// <summary> Registers a signal stream id (shortcut for the streamIds database). </summary>
        public void RegisterStreamId(string streamCategory, string streamName)
            => registerId?.Invoke(settings != null ? settings.streamIds : null, streamCategory, streamName);

        /// <summary> Wires a Show/Hide view command onto the given GameObject (button onClick semantics). </summary>
        public void AddViewCommand(GameObject go, string viewId, ViewCommandOnClick.Command command)
            => addViewCommand?.Invoke(go, viewId, command);
    }

    /// <summary>
    /// A project-defined element kind: how to build it, export it, edit it, color it, and what domain
    /// signal it publishes. Register one from <c>[InitializeOnLoad]</c> via <see cref="NeoElementKinds.Register"/>
    /// and the Composer picker/inspector, the generator, the exporter and the binding manifest all pick it
    /// up — with no package file edited. This is the keystone extensibility seam (Pattern R); see
    /// <c>extensibility-seam-element-kinds-plan.md</c>.
    /// </summary>
    public interface INeoElementKind
    {
        /// <summary> The spec key / kind string (e.g. "carousel"). Must be unique and not collide with a built-in. </summary>
        string Kind { get; }

        /// <summary> Builds the widget's root GameObject under <c>ctx.parent</c>. The shared
        /// geometry/anchor/child pass runs on the returned object afterward. </summary>
        GameObject Build(ElementBuildContext ctx);

        /// <summary> Reads a GameObject back into spec form; returns false if this object isn't ours.
        /// Must match a marker component specific to this kind so it never hijacks a built-in. </summary>
        bool TryExport(GameObject go, out ElementSpec spec);

        /// <summary> The inspector fields the Composer should expose for this kind. </summary>
        IEnumerable<SpecField> Fields { get; }

        /// <summary> The Composer tree/inspector accent color for this kind. </summary>
        Color Accent { get; }

        /// <summary> The domain-signal value type this kind publishes via <c>element.signal</c>/
        /// <c>element.onClickSignal</c>: none | bool | float | int | string. Drives the binding manifest. </summary>
        string SignalPayload { get; }
    }

    /// <summary>
    /// Optional seam: a custom element kind that nests child elements (a layout container). When a
    /// registered kind implements this and returns true, the Composer treats it like a built-in container
    /// — the canvas accepts drops into it and palette click-to-add nests into it instead of appending to
    /// the view root. Built-in containers are listed in <c>ComposerCanvas.IsContainerKind</c>; a project
    /// kind carries the same fact here. Mirrors the <see cref="ITriggerKindIdDatabase"/> pattern.
    /// </summary>
    public interface IElementKindContainer
    {
        /// <summary> True when this kind's elements hold <see cref="ElementSpec.children"/>. </summary>
        bool AcceptsChildren { get; }
    }

    /// <summary>
    /// Optional seam: a custom element kind that is addressed by a Category/Name id and wants its Composer
    /// id picker to autocomplete against a specific id database. Built-in kinds map this in
    /// <c>IdDatabaseOptions.ForElementKind</c>; a project kind carries it here. Mirrors
    /// <see cref="ITriggerKindIdDatabase"/> (the same seam, for triggers).
    /// </summary>
    public interface IElementKindIdDatabase
    {
        /// <summary> The id-asset <see cref="System.Type"/> (e.g. <c>typeof(ButtonId)</c>) whose database
        /// the id picker should offer, or null for none. </summary>
        System.Type PreferredIdType { get; }
    }

    /// <summary>
    /// Pattern-R registry of project-defined element kinds. Built-ins are NOT registered here in Phase 1
    /// (they keep their proven generator switch / exporter chain), so <see cref="All"/> is empty until a
    /// project registers — zero risk to the round-trip. Mirrors the shape of the master plan's reference
    /// registry: <see cref="All"/> / <see cref="TryGet"/> / <see cref="Register"/> (replace-by-Kind, else append).
    /// </summary>
    public static class NeoElementKinds
    {
        private static readonly List<INeoElementKind> _kinds = new List<INeoElementKind>();

        /// <summary> Every registered project kind (built-ins live in the generator switch, not here). </summary>
        public static IReadOnlyList<INeoElementKind> All => _kinds;

        /// <summary> Finds a registered kind by its <see cref="INeoElementKind.Kind"/> string. </summary>
        public static bool TryGet(string kind, out INeoElementKind result)
        {
            if (!string.IsNullOrEmpty(kind))
                foreach (INeoElementKind k in _kinds)
                    if (k != null && k.Kind == kind) { result = k; return true; }
            result = null;
            return false;
        }

        /// <summary> Registers a kind, replacing any existing one with the same <see cref="INeoElementKind.Kind"/>
        /// (so a project can override), else appending. </summary>
        public static void Register(INeoElementKind kind)
        {
            if (kind == null || string.IsNullOrEmpty(kind.Kind))
            {
                Debug.LogWarning("NeoElementKinds.Register ignored a kind with a null/empty Kind id.");
                return;
            }
            for (int i = 0; i < _kinds.Count; i++)
                if (_kinds[i].Kind == kind.Kind) { _kinds[i] = kind; return; }
            _kinds.Add(kind);
        }

        /// <summary> Test/seam hook: clears the registry (built-ins are unaffected — they aren't here). </summary>
        public static void ClearForTests() => _kinds.Clear();
    }
}
