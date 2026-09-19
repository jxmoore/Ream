<#
.SYNOPSIS
    Builds a distributable Windows copy of Ream and zips it.

.DESCRIPTION
    Produces artifacts\Ream-win-x64\ (a single Ream.App.exe plus a few files) and
    artifacts\Ream-<version>-win-x64.zip. By default the build is self-contained, so the target
    machine needs nothing installed. Use -FrameworkDependent for a much smaller build that needs the
    .NET 8 Desktop Runtime. This is a portable build, not an installer: there is no Start-menu entry,
    file association, or auto-update. Nothing here launches the app.

.EXAMPLE
    .\build\publish.ps1
    .\build\publish.ps1 -FrameworkDependent
#>
param(
    [switch]$FrameworkDependent,
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src\Ream.App\Ream.App.csproj"
$artifacts = [System.IO.Path]::GetFullPath((Join-Path $root "artifacts"))

[xml]$xml = Get-Content -LiteralPath $project
$version = ($xml.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
if (-not $version) { throw "Couldn't read <Version> from $project" }

$flavor = if ($FrameworkDependent) { "framework-dependent" } else { "self-contained" }
$suffix = if ($FrameworkDependent) { "win-x64-framework-dependent" } else { "win-x64" }
$out = [System.IO.Path]::GetFullPath((Join-Path $artifacts "Ream-$suffix"))
$zip = [System.IO.Path]::GetFullPath((Join-Path $artifacts "Ream-$version-$suffix.zip"))

# Only ever clear something directly inside <repo>\artifacts.
foreach ($path in $out, $zip) {
    if (-not $path.StartsWith($artifacts + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to touch '$path': it is not inside '$artifacts'"
    }
}
New-Item -ItemType Directory -Force -Path $artifacts | Out-Null
if (Test-Path -LiteralPath $out) { Remove-Item -LiteralPath $out -Recurse -Force }
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }

Write-Host "Publishing Ream $version ($flavor) to $out"
$selfContained = if ($FrameworkDependent) { "false" } else { "true" }
dotnet publish $project `
    --configuration $Configuration `
    --runtime win-x64 `
    --self-contained $selfContained `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=none `
    --output $out
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

Compress-Archive -Path (Join-Path $out "*") -DestinationPath $zip
$size = [math]::Round((Get-Item -LiteralPath $zip).Length / 1MB, 1)
Write-Host "Done: $zip ($size MB)"
