# Publica Arcano para Windows x64 como carpeta autocontenida (no requiere .NET
# instalado), la comprime en un .zip y, si está Inno Setup 6, arma el instalador.
#
#   pwsh scripts/publicar.ps1    → publicar\Arcano-<versión>-win-x64.zip
#                                  publicar\Arcano-<versión>-instalador.exe
#
# Requisitos en el equipo destino: Windows 10/11 con WebView2 Runtime (viene con
# Edge) y Visual C++ 2015-2022 x64 (lo usa el OCR). Para abrir Word: Microsoft
# Word o LibreOffice.
$ErrorActionPreference = 'Stop'
$raiz = Split-Path -Parent $PSScriptRoot
$salida = Join-Path $raiz 'publicar'
$carpeta = Join-Path $salida 'Arcano'

if (Test-Path $carpeta) { Remove-Item -Recurse -Force $carpeta }

dotnet publish (Join-Path $raiz 'src/Arcano.Escritorio') `
    -c Release -r win-x64 --self-contained true `
    -p:PublishReadyToRun=true -p:DebugType=none `
    -o $carpeta
if ($LASTEXITCODE -ne 0) { throw 'Falló la publicación.' }

foreach ($necesario in 'Arcano.exe', 'pdfium.dll', 'x64\tesseract50.dll', 'tessdata\spa.traineddata', 'wwwroot\index.html') {
    if (-not (Test-Path (Join-Path $carpeta $necesario))) { throw "Falta $necesario en la publicación." }
}

[xml]$props = Get-Content (Join-Path $raiz 'Directory.Build.props')
$version = $props.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$zip = Join-Path $salida "Arcano-$version-win-x64.zip"
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path $carpeta -DestinationPath $zip
Write-Host "Listo: $zip ($([math]::Round((Get-Item $zip).Length / 1MB, 1)) MB)"

# Instalador (Inno Setup 6: winget install JRSoftware.InnoSetup)
$iscc = @(
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) {
    Write-Warning 'No se encontró Inno Setup 6: se omite el instalador.'
    return
}
& $iscc /Q "/DVersion=$version" "/DOrigen=$carpeta" "/O$salida" (Join-Path $raiz 'instalador\Arcano.iss')
if ($LASTEXITCODE -ne 0) { throw 'Falló la compilación del instalador.' }
$instalador = Join-Path $salida "Arcano-$version-instalador.exe"
Write-Host "Listo: $instalador ($([math]::Round((Get-Item $instalador).Length / 1MB, 1)) MB)"
