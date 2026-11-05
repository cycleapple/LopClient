param(
    [Parameter(Mandatory = $true)][string]$DalamudHome,
    [switch]$ManagedOnly,
    [string]$Dotnet = 'dotnet'
)
$ErrorActionPreference = 'Stop'
$refDir = (Resolve-Path -LiteralPath $DalamudHome).Path
$identity = [System.Reflection.AssemblyName]::GetAssemblyName((Join-Path $refDir 'Dalamud.dll'))
if ($identity.Version.Major -ne 14) { throw "Expected Dalamud API14; found $($identity.Version)." }
$env:DALAMUD_HOME = $refDir
$env:DOTNET_ROLL_FORWARD = 'Major'
$env:DOTNET_CLI_UI_LANGUAGE = 'en-US'
$dotnetExe = (Get-Command $Dotnet -ErrorAction Stop).Source
$env:PATH = (Split-Path -Parent $dotnetExe) + [IO.Path]::PathSeparator + $env:PATH
$env:DOTNET_ROOT = Split-Path -Parent $dotnetExe
$sdkVersion = & $dotnetExe --version
if ([version]$sdkVersion -lt [version]'10.0.101') { throw "Requires .NET SDK 10.0.101 or newer; found $sdkVersion." }
Push-Location (Join-Path $PSScriptRoot '..')
try {
    git submodule update --init --recursive
    if ($LASTEXITCODE -ne 0) { throw 'Submodule initialization failed.' }
    $plan = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'dependencies.json') -Raw | ConvertFrom-Json
    foreach ($dependency in $plan) {
        $patch = Join-Path $PSScriptRoot $dependency.patch
        $hash = (Get-FileHash -LiteralPath $patch -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($hash -ne $dependency.sha256) { throw "Patch hash mismatch: $patch" }
        git -C $dependency.path apply --check $patch 2>$null
        if ($LASTEXITCODE -eq 0) {
            git -C $dependency.path apply $patch
            if ($LASTEXITCODE -ne 0) { throw "Failed to apply $patch" }
        } else {
            git -C $dependency.path apply --reverse --check $patch 2>$null
            if ($LASTEXITCODE -ne 0) { throw "Dependency differs from its pinned API14 patch: $($dependency.path)" }
        }
    }
    $projects = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'projects.json') -Raw | ConvertFrom-Json
    foreach ($project in $projects) {
        $extra = @()
        if ($ManagedOnly -and $project.internalName -eq 'PocketRecorder') { $extra += '-p:NativeRecorderAutoBuild=false' }
        & $dotnetExe build $project.project -c Release "-p:DalamudLibPath=$refDir/" -p:EnableWindowsTargeting=true -p:RestoreLockedMode=false @extra
        if ($LASTEXITCODE -ne 0) { throw "API14 build failed: $($project.internalName)" }
    }
} finally { Pop-Location }
