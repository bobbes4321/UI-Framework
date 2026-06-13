# Compile-check helper for the Neo UI package while the editor holds the lock.
# Usage: powershell -File neo-compilecheck.ps1 -Target runtime|editor|tests|all
# Compiles Runtime -> Temp dll, Editor against it, then EditMode+PlayMode tests against both.
param([string]$Target = "all")

$ErrorActionPreference = "Stop"
$UnityDir = "C:\Program Files\Unity\Hub\Editor\6000.4.10f1"
$Proj     = "C:\_git\UI Package"
$Pkg      = "$Proj\Assets\Neo UI Framework"
$Data     = "$UnityDir\Editor\Data"
$csc      = "$Data\DotNetSdkRoslyn\csc.dll"
$SA       = "$Proj\Library\ScriptAssemblies"
$Tmp      = "$Proj\TestResults"
if (-not (Test-Path $Tmp)) { New-Item -ItemType Directory $Tmp | Out-Null }

$netstd   = "$Data\NetStandard\ref\2.1.0\netstandard.dll"
$engineGlob = Get-ChildItem "$Data\Managed\UnityEngine\*.dll" | Where-Object { $_.Name -ne "UnityEditor.dll" -and $_.Name -ne "UnityEngine.dll" } | ForEach-Object { "-r:`"$($_.FullName)`"" }
$shims    = Get-ChildItem "$Data\NetStandard\compat\2.1.0\shims\netfx\*.dll" | ForEach-Object { "-r:`"$($_.FullName)`"" }

function Compile($outDll, $defines, $refs, $sources) {
    $a = @("-nologo", "-target:library", "-langversion:9.0", "-nowarn:CS1701,CS1702", "-out:`"$outDll`"")
    $a += $defines | ForEach-Object { "-define:$_" }
    $a += $engineGlob
    $a += $refs
    $a += $sources | ForEach-Object { "`"$($_.FullName)`"" }
    $tmpRsp = "$Tmp\neo_csc.rsp"
    $a -join "`n" | Out-File -Encoding utf8 $tmpRsp
    & dotnet "$csc" "@$tmpRsp" 2>&1 | Where-Object { "$_" -match "error" } | ForEach-Object { Write-Host "$_" }
    return $LASTEXITCODE
}

$runtimeOut = "$Tmp\NeoUI_Runtime.dll"
$editorOut  = "$Tmp\NeoUI_Editor.dll"
$editoruiOut= "$Tmp\NeoUI_EditorUI.dll"

if ($Target -eq "runtime" -or $Target -eq "all") {
    Write-Host "=== Runtime ===" -ForegroundColor Cyan
    $src = Get-ChildItem "$Pkg\Runtime" -Recurse -Filter *.cs
    $refs = @("-r:`"$netstd`"", "-r:`"$SA\UnityEngine.UI.dll`"", "-r:`"$SA\Unity.TextMeshPro.dll`"", "-r:`"$SA\Unity.InputSystem.dll`"")
    if ((Compile $runtimeOut @("UNITY_EDITOR","UNITY_6000_0_OR_NEWER") $refs $src) -ne 0) { Write-Host "RUNTIME FAILED" -ForegroundColor Red; exit 1 }
    Write-Host "RUNTIME OK" -ForegroundColor Green
}

if ($Target -eq "editor" -or $Target -eq "all") {
    Write-Host "=== EditorUI kit ===" -ForegroundColor Cyan
    $euiSrc = Get-ChildItem "$Pkg\Editor\EditorUI" -Recurse -Filter *.cs
    if ((Compile $editoruiOut @("UNITY_EDITOR") (@("-r:`"$netstd`"") + $shims) $euiSrc) -ne 0) { Write-Host "EDITORUI FAILED" -ForegroundColor Red; exit 1 }
    Write-Host "EDITORUI OK" -ForegroundColor Green

    Write-Host "=== Editor ===" -ForegroundColor Cyan
    $edSrc = Get-ChildItem "$Pkg\Editor" -Recurse -Filter *.cs | Where-Object { $_.FullName -notmatch "\\EditorUI\\" }
    $edRefs = @("-r:`"$runtimeOut`"", "-r:`"$editoruiOut`"", "-r:`"$netstd`"",
        "-r:`"$SA\UnityEngine.UI.dll`"", "-r:`"$SA\UnityEditor.UI.dll`"",
        "-r:`"$SA\Unity.TextMeshPro.dll`"", "-r:`"$SA\Unity.InputSystem.dll`"") + $shims
    if ((Compile $editorOut @("UNITY_EDITOR") $edRefs $edSrc) -ne 0) { Write-Host "EDITOR FAILED" -ForegroundColor Red; exit 1 }
    Write-Host "EDITOR OK" -ForegroundColor Green
}

if ($Target -eq "tests" -or $Target -eq "all") {
    $nunit = Get-ChildItem "$Proj\Library\PackageCache" -Recurse -Filter nunit.framework.dll | Where-Object { $_.FullName -match "net40\\unity-custom" } | Select-Object -First 1
    $tRefs = @("-r:`"$runtimeOut`"", "-r:`"$editorOut`"", "-r:`"$editoruiOut`"", "-r:`"$netstd`"",
        "-r:`"$SA\UnityEngine.UI.dll`"", "-r:`"$SA\Unity.TextMeshPro.dll`"", "-r:`"$SA\Unity.InputSystem.dll`"",
        "-r:`"$SA\UnityEngine.TestRunner.dll`"", "-r:`"$SA\UnityEditor.TestRunner.dll`"",
        "-r:`"$($nunit.FullName)`"") + $shims

    Write-Host "=== EditMode tests ===" -ForegroundColor Cyan
    $tSrc = Get-ChildItem "$Pkg\Tests\EditMode" -Recurse -Filter *.cs
    if ((Compile "$Tmp\NeoUI_TestsEdit.dll" @("UNITY_EDITOR","UNITY_INCLUDE_TESTS") $tRefs $tSrc) -ne 0) { Write-Host "EDIT TESTS FAILED" -ForegroundColor Red; exit 1 }
    Write-Host "EDIT TESTS OK" -ForegroundColor Green

    Write-Host "=== PlayMode tests ===" -ForegroundColor Cyan
    $pSrc = Get-ChildItem "$Pkg\Tests\PlayMode" -Recurse -Filter *.cs
    if ((Compile "$Tmp\NeoUI_TestsPlay.dll" @("UNITY_EDITOR","UNITY_INCLUDE_TESTS") $tRefs $pSrc) -ne 0) { Write-Host "PLAY TESTS FAILED" -ForegroundColor Red; exit 1 }
    Write-Host "PLAY TESTS OK" -ForegroundColor Green
}
Write-Host "DONE" -ForegroundColor Green
