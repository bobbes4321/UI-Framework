using System.Runtime.CompilerServices;

// The EditMode test assembly drives the flow-scoped scene-builder helpers
// (GeneratedSceneBuilder.SelectFlowGraph / CollectReferencedViewKeys) directly so the
// cross-spec contamination regression can be asserted without building a scene on disk.
[assembly: InternalsVisibleTo("Neo.UI.Tests.EditMode")]
