[CmdletBinding()]
param(
    [string] $Configuration = "Release",
    [string] $Version = "1.0.1"
)
$ErrorActionPreference = "Stop"
$name = "Mixcloud"
$root = Split-Path $PSScriptRoot -Parent
$build = Join-Path $root "src\Mixcloud.Plugin\bin\$Configuration\net481"
$out = Join-Path $root "release-out"

if (-not (Test-Path $build)) { throw "Brak katalogu build: $build. Uruchom najpierw dotnet build -c $Configuration." }

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
$flat = Join-Path $out "flat\$name"
$nested = Join-Path $out "nested\$name"
New-Item -ItemType Directory -Force -Path "$flat\Langs", "$nested\x64", "$nested\Langs" | Out-Null

# Ten sam wzorzec co tools/deploy.ps1: entry point przemianowany, reszta *.dll
# przez wzorzec, zeby zaden plik (np. Mixcloud.Core.dll) nie zostal pominiety
# przy recznym wypisywaniu nazw - to dokladnie ten blad, ktory zepsul v1.0.0.
$exclude = @("aimp_dotnet.dll", "AIMP.SDK.dll", "$name.dll")
Copy-Item (Join-Path $build "aimp_dotnet.dll") (Join-Path $flat "$name.dll") -Force
Copy-Item (Join-Path $build "$name.dll") (Join-Path $flat "${name}_plugin.dll") -Force
Copy-Item (Join-Path $build "AIMP.SDK.dll") $flat -Force
Get-ChildItem "$build\*.dll" -Exclude $exclude | ForEach-Object { Copy-Item $_.FullName $flat -Force }
Copy-Item (Join-Path $root "src\Mixcloud.Plugin\Langs\*") (Join-Path $flat "Langs") -Force

# Wariant zagniezdzony w x64\ - wymagany przez wbudowany instalator AIMP
# (Ustawienia -> Wtyczki -> Instaluj), ktory odrzuca pakiet bez tego
# podkatalogu komunikatem "package has no 64-bit binaries".
Copy-Item "$flat\*.dll" (Join-Path $nested "x64") -Force
Copy-Item "$flat\Langs\*" (Join-Path $nested "Langs") -Force

$zip = Join-Path $out "AIMP-Mixcloud-v$Version.zip"
$installNotes = @"
Wtyczka Mixcloud dla AIMP - instalacja
================================================================

Wymagania: AIMP 5.4, wersja 64-bit.

SPOSOB A - wbudowany instalator AIMP (najlatwiejszy)
------------------------------------------------------
1. Pobierz plik "aimp_mixcloud.aimppack" (osobno dolaczony do release'a).
2. W AIMP: Ustawienia -> Wtyczki -> przycisk "Instaluj" (lewy dolny rog)
   -> wskaz pobrany plik .aimppack.
3. Uruchom AIMP ponownie.

SPOSOB B - recznie z archiwum ZIP
------------------------------------------------------
1. Odblokuj pobrany plik ZIP, zanim go rozpakujesz - kliknij prawym
   przyciskiem -> Wlasciwosci -> jesli widac "Zabezpieczenia: ten plik
   pochodzi z innego komputera..." zaznacz "Odblokuj" -> OK.
2. Zamknij AIMP, jesli jest uruchomiony.
3. Skopiuj caly katalog "Mixcloud" z archiwum do:
   C:\Program Files\AIMP\Plugins\
   (wymaga uprawnien administratora)
4. Uruchom AIMP ponownie.

Po instalacji (obiema metodami)
------------------------------------------------------
Ustawienia AIMP -> Wtyczki -> Mixcloud: wpisz nazwe uzytkownika Mixcloud
(albo wklej caly adres profilu - wtyczka sama wyciagnie z niego nazwe),
nastepnie "Wczytaj ulubione teraz".

Wtyczka jest wylacznie 64-bitowa - nie ma wariantu 32-bitowego.

Pelna dokumentacja, kod zrodlowy i instrukcje budowania:
https://github.com/zetmar-collab/aimp-mixcloud-plugin
"@
Set-Content -Path (Join-Path $flat "..\INSTALACJA.txt") -Value $installNotes -Encoding UTF8
Compress-Archive -Path $flat, (Join-Path $out "flat\INSTALACJA.txt") -DestinationPath $zip -Force

$pack = Join-Path $out "aimp_mixcloud.aimppack"
if (Test-Path $pack) { Remove-Item $pack -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($nested, $pack, [IO.Compression.CompressionLevel]::Optimal, $true)

Write-Output "Zbudowano:"
Write-Output "  $zip"
Write-Output "  $pack"
