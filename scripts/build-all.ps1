param(
    [int[]]$Years = @(2019, 2020, 2021, 2022, 2023, 2024, 2025, 2026)
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$diagnosticsRoot = Join-Path $projectRoot 'artifacts/build'
New-Item -ItemType Directory -Path $diagnosticsRoot -Force | Out-Null
$manifest = @()
Push-Location $projectRoot
try {
    $commit = (& git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Cannot identify the source commit.' }
    foreach ($year in $Years) {
        if ($year -lt 2019 -or $year -gt 2026) { throw "Unsupported Revit year: $year" }
        $config = "R$year"
        $logPath = Join-Path $diagnosticsRoot "build-$config.log"
        # Keep native project OutputPath. Do not create a combined bin or copy assemblies elsewhere.
        & dotnet build CreateWallElevation.sln -c $config --no-incremental --nologo -v:minimal -p:DebugSymbols=false -p:DebugType=None 2>&1 |
            Tee-Object -FilePath $logPath
        if ($LASTEXITCODE -ne 0) { throw "Build failed: $config. See $logPath" }
        foreach ($variant in @('CreateWallElevation', 'CreateWallElevationSpectrum')) {
            $relativeAssembly = "$variant/bin/$config/CreateWallElevation.dll"
            $assembly = Join-Path $projectRoot $relativeAssembly
            if (!(Test-Path -LiteralPath $assembly)) { throw "Missing output: $assembly" }
            $manifest += [pscustomobject]@{
                Revit = $year
                Variant = $variant
                Commit = $commit
                File = $relativeAssembly
                SHA256 = (Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash
                BuiltUtc = (Get-Date).ToUniversalTime().ToString('o')
            }
        }
    }
    $manifest | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $diagnosticsRoot 'build-manifest.json') -Encoding UTF8
    Write-Output "Builds completed in CreateWallElevation/bin and CreateWallElevationSpectrum/bin."
}
finally { Pop-Location }
