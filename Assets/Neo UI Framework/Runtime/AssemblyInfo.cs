using System.Runtime.CompilerServices;

// Grants the EditMode/PlayMode test assemblies access to `internal` members on Runtime-asmdef
// Pattern R registries (e.g. NeoAnimatorRoles.ResetForTests — Wave 4 Task 4.4) so a suite can reset
// a static registry between tests without exposing that reset to consuming projects. Mirrors
// Editor/AssemblyInfo.cs's grant for the editor asmdef.
[assembly: InternalsVisibleTo("Neo.UI.Tests.EditMode")]
[assembly: InternalsVisibleTo("Neo.UI.Tests.PlayMode")]
