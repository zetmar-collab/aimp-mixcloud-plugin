[CmdletBinding()]
param(
    [string] $Configuration = "Debug",
    [string] $AimpPath = "C:\Program Files\AIMP"
)
$ErrorActionPreference = "Stop"
$name = "Mixcloud"
$root = Split-Path $PSScriptRoot -Parent
$build = Join-Path $root "src\Mixcloud.Plugin\bin\$Configuration\net481"
$dest  = Join-Path $AimpPath "Plugins\$name"

if (-not (Test-Path $build)) { throw "Brak katalogu build: $build. Uruchom najpierw dotnet build." }

# Kopiowanie do Program Files wymaga uprawnien administratora.
$isAdmin = ([Security.Principal.WindowsPrincipal] `
    [Security.Principal.WindowsIdentity]::GetCurrent()
    ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin -and $dest -like "$env:ProgramFiles*") {
    throw "Wdrozenie do '$dest' wymaga uruchomienia PowerShell jako administrator."
}

# AIMP trzyma DLL-e zablokowane, dopoki dziala.
if (Get-Process -Name AIMP -ErrorAction SilentlyContinue) {
    throw "AIMP jest uruchomiony. Zamknij go przed wdrozeniem."
}

New-Item -ItemType Directory -Force -Path $dest, (Join-Path $dest "Langs") | Out-Null
Copy-Item (Join-Path $build "aimp_dotnet.dll") (Join-Path $dest "$name.dll") -Force
Copy-Item (Join-Path $build "$name.dll")       (Join-Path $dest "${name}_plugin.dll") -Force
Copy-Item (Join-Path $build "AIMP.SDK.dll")    $dest -Force
Get-ChildItem "$build\*.dll" -Exclude "aimp_dotnet.dll","AIMP.SDK.dll","$name.dll" |
    ForEach-Object { Copy-Item $_.FullName $dest -Force }
$langs = Join-Path $root "src\Mixcloud.Plugin\Langs"
if (Test-Path $langs) { Copy-Item "$langs\*" (Join-Path $dest "Langs") -Force }

Write-Output "Wdrozono do: $dest"
Get-ChildItem $dest -Recurse -File | ForEach-Object { "  " + $_.FullName.Substring($dest.Length + 1) }
