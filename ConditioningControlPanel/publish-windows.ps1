# Publish the Avalonia Windows desktop app as a self-contained folder.
param(
    [ValidateSet("win-x64", "win-arm64")]
    [string]$Runtime = "win-x64",

    [string]$Configuration = "Release",

    [switch]$SingleFile
)

$ErrorActionPreference = "Stop"
$project = "$PSScriptRoot\CCP.Avalonia.Desktop.Windows\CCP.Avalonia.Desktop.Windows.csproj"
$output = "$PSScriptRoot\publish\$Runtime"

$args = @(
    "publish", $project,
    "-c", $Configuration,
    "-r", $Runtime,
    "--self-contained", "true",
    "-o", $output
)

if ($SingleFile) {
    # Single-file with WindowsAppSDK requires EnableMsixTooling.
    $args += "-p:EnableMsixTooling=true"
    $args += "-p:PublishSingleFile=true"
}

& dotnet @args

if ($LASTEXITCODE -ne 0) {
    throw "Publish failed."
}

Write-Host "Published to $output" -ForegroundColor Green
