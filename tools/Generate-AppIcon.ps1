$projectRoot = Split-Path -Parent $PSScriptRoot
$source = Join-Path $projectRoot 'assets\app-icon.svg'
$assetFolder = Join-Path $projectRoot 'src\Cashflow.Windows\Assets'
$magick = Get-Command magick -ErrorAction Stop

& $magick.Source -background none $source -resize 512x512 (Join-Path $assetFolder 'AppIcon.png')
if ($LASTEXITCODE -ne 0) { throw 'No se pudo generar AppIcon.png.' }

& $magick.Source -background none $source -define icon:auto-resize=256,128,64,48,32,16 (Join-Path $assetFolder 'AppIcon.ico')
if ($LASTEXITCODE -ne 0) { throw 'No se pudo generar AppIcon.ico.' }

Write-Host "Iconos actualizados desde $source"
