# Publica Testado para Windows x64 como carpeta autocontenida (no requiere .NET
# instalado) y la comprime en un .zip listo para copiar a los equipos.
#
#   pwsh scripts/publicar.ps1            → publicar\Testado-<versión>-win-x64.zip
#
# Requisitos en el equipo destino: Windows 10/11 con WebView2 Runtime (viene con
# Edge) y Visual C++ 2015-2022 x64 (lo usa el OCR). Para abrir Word: Microsoft
# Word o LibreOffice.
$ErrorActionPreference = 'Stop'
$raiz = Split-Path -Parent $PSScriptRoot
$salida = Join-Path $raiz 'publicar'
$carpeta = Join-Path $salida 'Testado'

if (Test-Path $carpeta) { Remove-Item -Recurse -Force $carpeta }

dotnet publish (Join-Path $raiz 'src/Testado.Escritorio') `
    -c Release -r win-x64 --self-contained true `
    -p:PublishReadyToRun=true -p:DebugType=none `
    -o $carpeta
if ($LASTEXITCODE -ne 0) { throw 'Falló la publicación.' }

foreach ($necesario in 'Testado.exe', 'pdfium.dll', 'x64\tesseract50.dll', 'tessdata\spa.traineddata', 'wwwroot\index.html') {
    if (-not (Test-Path (Join-Path $carpeta $necesario))) { throw "Falta $necesario en la publicación." }
}

[xml]$props = Get-Content (Join-Path $raiz 'Directory.Build.props')
$version = $props.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$zip = Join-Path $salida "Testado-$version-win-x64.zip"
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path $carpeta -DestinationPath $zip
Write-Host "Listo: $zip ($([math]::Round((Get-Item $zip).Length / 1MB, 1)) MB)"
