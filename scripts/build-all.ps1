param(
    [int[]]$Years = @(2019, 2020, 2021, 2022, 2023, 2024, 2025, 2026)
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$outputRoot = Join-Path $projectRoot 'bin'
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
$manifest = @()
Push-Location $projectRoot
try {
    $commit = (& git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Cannot identify the source commit.' }
    foreach ($year in $Years) {
        if ($year -lt 2019 -or $year -gt 2026) { throw "Unsupported Revit year: $year" }
        $config = "R$year"
        $logPath = Join-Path $outputRoot "build-$config.log"
        & dotnet build CreateWallElevation.sln -c $config --no-incremental --nologo -v:minimal 2>&1 |
            Tee-Object -FilePath $logPath
        if ($LASTEXITCODE -ne 0) { throw "Build failed: $config. See $logPath" }
        foreach ($variant in @('CreateWallElevation', 'CreateWallElevationSpectrum')) {
            $source = Join-Path $projectRoot "$variant\bin\$config"
            $destination = Join-Path $outputRoot "$config\$variant"
            New-Item -ItemType Directory -Path $destination -Force | Out-Null
            # Copy only this plugin's output. RevitAPI DLLs must never be deployed with the plugin.
            foreach ($name in @('CreateWallElevation.dll', 'CreateWallElevation.pdb', 'CreateWallElevation.dll.config', 'CreateWallElevation.deps.json')) {
                $file = Join-Path $source $name
                if (Test-Path -LiteralPath $file) { Copy-Item -LiteralPath $file -Destination $destination -Force }
            }
            $dataSource = Join-Path $projectRoot 'data'
            if (Test-Path -LiteralPath $dataSource) {
                $dataDestination = Join-Path $destination 'data'
                New-Item -ItemType Directory -Path $dataDestination -Force | Out-Null
                Get-ChildItem -LiteralPath $dataSource -File | Copy-Item -Destination $dataDestination -Force
            }
            $assembly = Join-Path $destination 'CreateWallElevation.dll'
            if (!(Test-Path -LiteralPath $assembly)) { throw "Missing output: $assembly" }
            $manifest += [pscustomobject]@{
                Revit = $year
                Variant = $variant
                Commit = $commit
                File = "$config/$variant/CreateWallElevation.dll"
                SHA256 = (Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash
                BuiltUtc = (Get-Date).ToUniversalTime().ToString('o')
            }
        }
    }
    $manifest | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $outputRoot 'build-manifest.json') -Encoding UTF8
    Write-Output "All requested builds completed: $outputRoot"
}
finally { Pop-Location }
