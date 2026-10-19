# Rebuilds Mandarine into publish\ and restarts it from there.
# Windows integration (Start menu, right-click, startup) points at publish\Mandarine.exe,
# so every run of this script makes the current code the one in use.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$publish = Join-Path $root 'publish'
$exe = Join-Path $publish 'Mandarine.exe'

Get-Process Mandarine -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

dotnet publish (Join-Path $root 'src\Mandarine.App') -c Release -r win-x64 --self-contained `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=none -o $publish
if ($LASTEXITCODE -ne 0) { throw "La compilation a échoué." }
Remove-Item (Join-Path $publish '*.pdb') -ErrorAction SilentlyContinue

# The first run registers the right-click menu and startup entry for this exe; later runs keep them.
$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'Mandarine.lnk'
$shell = New-Object -ComObject WScript.Shell
$link = $shell.CreateShortcut($shortcut)
$link.TargetPath = $exe
$link.IconLocation = "$exe,0"
$link.Description = 'Convertisseur de fichiers'
$link.Save()

Start-Process $exe
Write-Host "Mandarine relancé depuis $exe"
