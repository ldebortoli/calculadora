param(
    [double]$MinimumLineCoverage = 100,
    [double]$MinimumBranchCoverage = 100
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$coverageDirectory = Join-Path $root "coverage"
$settingsPath = Join-Path $PSScriptRoot "coverage.runsettings.xml"
$coreReport = Join-Path $coverageDirectory "core.cobertura.xml"
$windowsReport = Join-Path $coverageDirectory "windows.cobertura.xml"
$combinedReport = Join-Path $coverageDirectory "coverage.cobertura.xml"
$coreAssembly = Join-Path $root "tests/Cashflow.Core.Tests/bin/Release/net8.0/Cashflow.Core.Tests.dll"
$windowsAssembly = Join-Path $root "tests/Cashflow.Windows.Tests/bin/Release/net8.0-windows/Cashflow.Windows.Tests.dll"

New-Item -ItemType Directory -Path $coverageDirectory -Force | Out-Null
foreach ($report in @($coreReport, $windowsReport, $combinedReport)) {
    Remove-Item -LiteralPath $report -Force -ErrorAction SilentlyContinue
}

Push-Location $root
try {
    & dotnet tool restore
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    & dotnet build CashflowCalculator.sln -c Release
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    [xml]$project = Get-Content -LiteralPath (Join-Path $root 'src/Cashflow.Windows/Cashflow.Windows.csproj') -Raw
    $releaseVersion = [string]$project.Project.PropertyGroup.Version
    $releaseFileVersion = [string]$project.Project.PropertyGroup.FileVersion
    if ($releaseVersion -notmatch '^\d+\.\d+\.\d+$' -or $releaseFileVersion -ne "$releaseVersion.0") {
        throw "Version y FileVersion deben coincidir: $releaseVersion / $releaseFileVersion."
    }
    $windowsProductAssembly = Join-Path $root 'src/Cashflow.Windows/bin/Release/net8.0-windows/RutaCashflow.dll'
    $builtFileVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($windowsProductAssembly).FileVersion
    $builtAssemblyVersion = [Reflection.AssemblyName]::GetAssemblyName($windowsProductAssembly).Version.ToString()
    if ($builtFileVersion -ne $releaseFileVersion -or $builtAssemblyVersion -ne $releaseFileVersion) {
        throw "Version compilada inconsistente: archivo $builtFileVersion, ensamblado $builtAssemblyVersion, proyecto $releaseFileVersion."
    }

    & dotnet dotnet-coverage collect --settings $settingsPath --output $coreReport --output-format cobertura --nologo dotnet $coreAssembly
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    & dotnet dotnet-coverage collect --settings $settingsPath --output $windowsReport --output-format cobertura --nologo dotnet $windowsAssembly
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    & dotnet dotnet-coverage merge $coreReport $windowsReport --output $combinedReport --output-format cobertura --nologo
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    [xml]$coverage = Get-Content -LiteralPath $combinedReport -Raw
    $culture = [System.Globalization.CultureInfo]::InvariantCulture
    $lineCoverage = [double]::Parse($coverage.coverage.'line-rate', $culture) * 100
    $branchCoverage = [double]::Parse($coverage.coverage.'branch-rate', $culture) * 100

    Write-Output ("Cobertura de líneas: {0:N2}% ({1}/{2})" -f $lineCoverage, $coverage.coverage.'lines-covered', $coverage.coverage.'lines-valid')
    Write-Output ("Cobertura de ramas: {0:N2}% ({1}/{2})" -f $branchCoverage, $coverage.coverage.'branches-covered', $coverage.coverage.'branches-valid')
    Write-Output "Informe: $combinedReport"

    if ($lineCoverage + 0.0001 -lt $MinimumLineCoverage) {
        throw "La cobertura de líneas debe ser al menos $MinimumLineCoverage%."
    }
    if ($branchCoverage + 0.0001 -lt $MinimumBranchCoverage) {
        throw "La cobertura de ramas debe ser al menos $MinimumBranchCoverage%."
    }
}
finally {
    Pop-Location
}
