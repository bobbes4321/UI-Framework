using System;

namespace Neo.UI.Editor
{
    /// <summary>
    /// The blessed production reassignment of <see cref="UISpecGenerator.GeneratedRoot"/>: a scoped,
    /// always-restored redirect of the generated-asset root to one showcase's isolated folder.
    /// <para>
    /// <see cref="UISpecGenerator.GeneratedRoot"/> is process-wide ambient state. Outside of this struct
    /// production code must never touch it (the test fixtures use the identical redirect for their
    /// scratch root). Wrapping a generate/build in <c>using NeoWorkspace.Scoped(showcase)</c> captures
    /// the previous root, points it at the showcase's <c>Generated/</c> folder for the duration, and
    /// restores it in <see cref="Dispose"/> — even if the body throws. So an exception during
    /// generation can never leave the root pointed at a showcase folder and have the next, unscoped
    /// generate land its assets there.
    /// </para>
    /// <para>
    /// Guard: a scope can never target <see cref="UISpecGenerator.DefaultGeneratedRoot"/> (the committed
    /// demo root) — that's reserved for the headless default and must never be the destination of a
    /// scoped, deletable generate. Constructing such a scope throws <see cref="ArgumentException"/>, as
    /// does a null/empty root.
    /// </para>
    /// </summary>
    public readonly struct NeoWorkspace : IDisposable
    {
        private readonly string _previousRoot;

        /// <summary> The generated-asset root this scope points <see cref="UISpecGenerator.GeneratedRoot"/> at. </summary>
        public string Root { get; }

        /// <summary> The scene path associated with this scope (the showcase's scene, or null/explicit). </summary>
        public string ScenePath { get; }

        private NeoWorkspace(string root, string scenePath)
        {
            if (string.IsNullOrEmpty(root))
                throw new ArgumentException("NeoWorkspace root must be non-empty", nameof(root));
            if (string.Equals(root, UISpecGenerator.DefaultGeneratedRoot, StringComparison.Ordinal))
                throw new ArgumentException(
                    $"NeoWorkspace must never scope the committed demo root ('{UISpecGenerator.DefaultGeneratedRoot}') — " +
                    "give each showcase its own isolated root.", nameof(root));

            _previousRoot = UISpecGenerator.GeneratedRoot;
            Root = root;
            ScenePath = scenePath;
            UISpecGenerator.GeneratedRoot = root;
        }

        /// <summary> Scopes the root (and scene path) to a showcase's derived folders. </summary>
        public static NeoWorkspace Scoped(Showcase showcase)
        {
            if (showcase == null) throw new ArgumentException("Showcase must not be null", nameof(showcase));
            return new NeoWorkspace(showcase.GeneratedRoot, showcase.ScenePath);
        }

        /// <summary> Scopes the root to an explicit folder, optionally pairing it with a scene path. </summary>
        public static NeoWorkspace Scoped(string root, string scenePath = null) => new NeoWorkspace(root, scenePath);

        /// <summary> Restores <see cref="UISpecGenerator.GeneratedRoot"/> to its value before this scope. </summary>
        public void Dispose() => UISpecGenerator.GeneratedRoot = _previousRoot;
    }
}
