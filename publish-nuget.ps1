param(
    [Parameter(Mandatory = $true)]
    [string] $Version,

    [Parameter(Mandatory = $true)]
    [string] $ApiKey,

    [string] $Source = "https://api.nuget.org/v3/index.json",

    [string] $Configuration = "Release",

    [string] $OutputDirectory = "artifacts/nuget"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$outputPath = Join-Path $repoRoot $OutputDirectory

$packages = @(
    @{ Id = "HnswLite"; Project = "src/HnswIndex/HnswIndex.csproj" },
    @{ Id = "HnswLite.RamStorage"; Project = "src/HnswIndex.RamStorage/HnswIndex.RamStorage.csproj" },
    @{ Id = "HnswLite.SqliteStorage"; Project = "src/HnswIndex.SqliteStorage/HnswIndex.SqliteStorage.csproj" },
    @{ Id = "HnswLite.PostgresqlStorage"; Project = "src/HnswIndex.PostgresqlStorage/HnswIndex.PostgresqlStorage.csproj" },
    @{ Id = "HnswLite.Sdk"; Project = "sdk/csharp/HnswLite.Sdk/HnswLite.Sdk.csproj" }
)

New-Item -ItemType Directory -Force -Path $outputPath | Out-Null
Get-ChildItem -Path $outputPath -Filter "*.nupkg" -ErrorAction SilentlyContinue | Remove-Item -Force
Get-ChildItem -Path $outputPath -Filter "*.snupkg" -ErrorAction SilentlyContinue | Remove-Item -Force

foreach ($package in $packages) {
    $projectPath = Join-Path $repoRoot $package.Project
    Write-Host "Packing $($package.Id) $Version"
    & dotnet pack $projectPath `
        -c $Configuration `
        -p:Version=$Version `
        -p:PackageVersion=$Version `
        -p:ContinuousIntegrationBuild=true `
        -o $outputPath
}

foreach ($package in $packages) {
    $nupkg = Join-Path $outputPath "$($package.Id).$Version.nupkg"
    if (!(Test-Path $nupkg)) {
        throw "Expected package was not produced: $nupkg"
    }

    Write-Host "Pushing $($package.Id) $Version"
    & dotnet nuget push $nupkg `
        --api-key $ApiKey `
        --source $Source `
        --no-symbols `
        --skip-duplicate

    $snupkg = Join-Path $outputPath "$($package.Id).$Version.snupkg"
    if (Test-Path $snupkg) {
        Write-Host "Pushing symbols for $($package.Id) $Version"
        & dotnet nuget push $snupkg `
            --api-key $ApiKey `
            --source $Source `
            --skip-duplicate
    }
}

Write-Host "NuGet publish completed for version $Version."
