[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'

$projects = @{
    Core = 'src/Raffinert.Expressions/Raffinert.Expressions.csproj'
    Adapter = 'src/Raffinert.Expressions.EntityFrameworkCore/Raffinert.Expressions.EntityFrameworkCore.csproj'
}
$versions = @{}
foreach ($name in $projects.Keys) {
    $projectPath = Join-Path $RepositoryRoot $projects[$name]
    [xml]$project = Get-Content -LiteralPath $projectPath -Raw
    $version = $project.SelectSingleNode('/Project/PropertyGroup/Version').InnerText
    if ([string]::IsNullOrWhiteSpace($version)) {
        throw "Missing package Version declaration in $($projects[$name])."
    }
    $versions[$name] = $version.Trim()
}

$guideRelativePath = 'docs/efcore-integration.md'
$guide = Get-Content -LiteralPath (Join-Path $RepositoryRoot $guideRelativePath)
$declarations = [ordered]@{
    'adapter guide header' = 'User guide for `Raffinert.Expressions.EntityFrameworkCore` **{0}**.' -f $versions.Adapter
    'adapter installation command' = 'dotnet add package Raffinert.Expressions.EntityFrameworkCore --version {0}' -f $versions.Adapter
    'Core dependency row' = '| Core dependency | `Raffinert.Expressions` {0} |' -f $versions.Core
}

foreach ($declaration in $declarations.GetEnumerator()) {
    if ($guide -cnotcontains $declaration.Value) {
        throw "$guideRelativePath has a missing or mismatched $($declaration.Key). Expected: $($declaration.Value)"
    }
}

Write-Output 'Documentation versions match the Core and EF adapter project versions.'
