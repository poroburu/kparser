param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$CliArgs
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot

function Find-MSBuild {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vswhere) {
        $path = & $vswhere -latest -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
        if ($path) { return $path }
    }
    foreach ($candidate in @(
            "${env:ProgramFiles}\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe",
            "${env:ProgramFiles}\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe",
            "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\MSBuild.exe",
            "${env:ProgramFiles(x86)}\MSBuild\14.0\Bin\MSBuild.exe"
        )) {
        if (Test-Path $candidate) { return $candidate }
    }
    throw "MSBuild not found. Install Visual Studio with the .NET desktop workload."
}

$msbuild = Find-MSBuild
$csproj = Join-Path $repoRoot "kparser.Cli\kparser.Cli.csproj"
& $msbuild $csproj /p:Configuration=Debug /p:Platform=x86 /v:minimal /nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$exeCandidates = @(
    (Join-Path $repoRoot "kparser.Cli\bin\x86\Debug\kparser.cli.exe"),
    (Join-Path $repoRoot "kparser.Cli\bin\Debug\kparser.cli.exe")
)
$exe = $exeCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $exe) {
    throw "kparser.cli.exe not found after build."
}

if (-not $CliArgs -or $CliArgs.Count -eq 0) {
    & $exe
    exit $LASTEXITCODE
}

& $exe @CliArgs
exit $LASTEXITCODE
