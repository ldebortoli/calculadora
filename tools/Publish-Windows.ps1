param(
    [switch]$Portable
)

$projectRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $projectRoot 'src\Cashflow.Windows\Cashflow.Windows.csproj'
$outputName = if ($Portable) { 'RutaCashflow-Windows-portable' } else { 'RutaCashflow-win-x64' }
$outputPath = Join-Path $projectRoot "artifacts\$outputName"
$selfContained = if ($Portable) { 'true' } else { 'false' }

Push-Location $projectRoot
try {
    & dotnet publish $projectPath `
        -c Release `
        -r win-x64 `
        --self-contained $selfContained `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        -o $outputPath

    if ($LASTEXITCODE -ne 0) {
        throw "La publicacion fallo con codigo $LASTEXITCODE."
    }

    Write-Host "Ejecutable creado en: $(Join-Path $outputPath 'RutaCashflow.exe')"
}
finally {
    Pop-Location
}
