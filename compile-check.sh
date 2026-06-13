#!/usr/bin/env bash
# Compile-check for the Neo UI Composer work (Plan 2).
#
# A Unity editor is open on a DIFFERENT checkout and cannot see this worktree, and this worktree
# has no Library/ScriptAssemblies. So instead of opening Unity, we recompile the affected assemblies
# from THIS worktree's source with Unity's bundled Roslyn (csc.dll), exactly as CLAUDE.md describes.
#
# It builds, in dependency order:
#   1. Neo.EditorUI       (Editor/EditorUI/*.cs)          — engine refs only (must stay kit-pure)
#   2. Neo.UI             (Runtime/**/*.cs)                — engine + package refs
#   3. Neo.UI.Editor      (Editor/**/*.cs minus EditorUI) — + Neo.UI + Neo.EditorUI  (our code lives here)
#   4. Neo.UI.Tests.EditMode (Tests/EditMode/**/*.cs)     — + nunit/test runner
#
# Prints "COMPILE-CHECK PASSED" on success, or the first errors and a non-zero exit on failure.
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
UNITY="/c/Program Files/Unity/Hub/Editor/6000.4.10f1/Editor/Data"
CSC="$UNITY/DotNetSdkRoslyn/csc.dll"
# the .NET facade set Unity's editor actually compiles against (Mono). Using mscorlib + the
# netstandard facade together matches what the builtin net40 nunit expects and what the Unity
# engine module DLLs were built against — avoids CS0012/CS0433 grief from mismatched corlibs.
MONO="$UNITY/MonoBleedingEdge/lib/mono/unityjit-win32"
# package assemblies aren't built in this worktree — borrow the sibling checkout's compiled ones
# (version-pinned packages, stable across branches)
PKG="/c/Users/maxim/RiderProjects/UI-Framework/Library/ScriptAssemblies"
NUNIT="$UNITY/Resources/PackageManager/BuiltInPackages/com.unity.ext.nunit/net40/unity-custom/nunit.framework.dll"
OUT="$ROOT/build/compile-check"
SRC="$ROOT/Assets/Neo UI Framework"

mkdir -p "$OUT"

# ---- reference sets ------------------------------------------------------------------------------
# Reference the individual engine/editor MODULES only — NOT the monolithic UnityEngine.dll /
# UnityEditor.dll facades, which type-forward into the same modules and cause CS0433 duplicates.
UNITY_REFS=("-r:$MONO/mscorlib.dll" "-r:$MONO/System.dll" "-r:$MONO/System.Core.dll" "-r:$MONO/Facades/netstandard.dll")
for dll in "$UNITY"/Managed/UnityEngine/*.dll; do UNITY_REFS+=("-r:$dll"); done

PKG_REFS=(
  "-r:$PKG/UnityEngine.UI.dll"
  "-r:$PKG/UnityEditor.UI.dll"
  "-r:$PKG/Unity.TextMeshPro.dll"
  "-r:$PKG/Unity.TextMeshPro.Editor.dll"
  "-r:$PKG/Unity.InputSystem.dll"
)

COMMON=(-nologo -target:library -langversion:9.0 -nostdlib+ -noconfig -nowarn:CS0169,CS0414,CS0649,CS0067 -define:UNITY_EDITOR -define:UNITY_2023_1_OR_NEWER -define:UNITY_6000_0_OR_NEWER)

fail=0

# Gather sources
mapfile -t EDITORUI_SRC < <(find "$SRC/Editor/EditorUI" -name "*.cs")
mapfile -t RUNTIME_SRC  < <(find "$SRC/Runtime" -name "*.cs")
mapfile -t EDITOR_SRC   < <(find "$SRC/Editor" -name "*.cs" -not -path "*/EditorUI/*")
mapfile -t TEST_SRC     < <(find "$SRC/Tests/EditMode" -name "*.cs")

# 1. Neo.EditorUI — engine refs ONLY (proves the kit stays dependency-free)
dotnet "$CSC" "${COMMON[@]}" "${UNITY_REFS[@]}" \
  -out:"$OUT/Neo.EditorUI.dll" "${EDITORUI_SRC[@]}" 2> "$OUT/Neo.EditorUI.log" 1>&2
if grep -q ": error" "$OUT/Neo.EditorUI.log"; then
  echo "FAILED: Neo.EditorUI (kit must stay engine-only)"; grep ": error" "$OUT/Neo.EditorUI.log" | head -40; fail=1
else echo "ok: Neo.EditorUI"; fi

# 2. Neo.UI runtime
dotnet "$CSC" "${COMMON[@]}" "${UNITY_REFS[@]}" "${PKG_REFS[@]}" \
  -out:"$OUT/Neo.UI.dll" "${RUNTIME_SRC[@]}" 2> "$OUT/Neo.UI.log" 1>&2
if grep -q ": error" "$OUT/Neo.UI.log"; then
  echo "FAILED: Neo.UI"; grep ": error" "$OUT/Neo.UI.log" | head -40; fail=1
else echo "ok: Neo.UI"; fi

# 3. Neo.UI.Editor — contains all the Composer code + the FlowGraphWindow edit
dotnet "$CSC" "${COMMON[@]}" "${UNITY_REFS[@]}" "${PKG_REFS[@]}" \
  -r:"$OUT/Neo.UI.dll" -r:"$OUT/Neo.EditorUI.dll" \
  -out:"$OUT/Neo.UI.Editor.dll" "${EDITOR_SRC[@]}" 2> "$OUT/Neo.UI.Editor.log" 1>&2
if grep -q ": error" "$OUT/Neo.UI.Editor.log"; then
  echo "FAILED: Neo.UI.Editor"; grep ": error" "$OUT/Neo.UI.Editor.log" | head -60; fail=1
else echo "ok: Neo.UI.Editor"; fi

# 4. EditMode tests (incl. the Composer tests)
dotnet "$CSC" "${COMMON[@]}" -define:UNITY_INCLUDE_TESTS "${UNITY_REFS[@]}" "${PKG_REFS[@]}" \
  -r:"$OUT/Neo.UI.dll" -r:"$OUT/Neo.EditorUI.dll" -r:"$OUT/Neo.UI.Editor.dll" \
  -r:"$NUNIT" -r:"$PKG/UnityEngine.TestRunner.dll" -r:"$PKG/UnityEditor.TestRunner.dll" \
  -out:"$OUT/Neo.UI.Tests.EditMode.dll" "${TEST_SRC[@]}" 2> "$OUT/Neo.UI.Tests.EditMode.log" 1>&2
if grep -q ": error" "$OUT/Neo.UI.Tests.EditMode.log"; then
  echo "FAILED: Neo.UI.Tests.EditMode"; grep ": error" "$OUT/Neo.UI.Tests.EditMode.log" | head -60; fail=1
else echo "ok: Neo.UI.Tests.EditMode"; fi

echo "──────────────────────────────────────────────────────────────"
if [ "$fail" -eq 0 ]; then
  echo "COMPILE-CHECK PASSED"
else
  echo "COMPILE-CHECK FAILED"
fi
exit $fail
