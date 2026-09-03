# Mixcloud dla AIMP

Wtyczka do odtwarzacza [AIMP](https://www.aimp.ru/) (wersja 5.4, x64), która
pozwala otwierać adresy Mixcloud bezpośrednio w playerze: pojedyncze audycje
(cloudcasty), profile użytkowników oraz listę ulubionych. Wtyczka korzysta
z `yt-dlp` do rozwiązywania adresów Mixcloud na bezpośrednie strumienie audio
i buduje playlisty AIMP z wyników.

## Wymagania

- **AIMP 5.4 x64** — zainstalowany na maszynie, na której ma działać wtyczka.
- **.NET Framework 4.8.1 Developer Pack** — projekt celuje w `net481`, ponieważ
  to jedyny pakiet targetujący dostępny na maszynie deweloperskiej; instalacja
  zestawu Developer Pack (nie samego Runtime) jest wymagana do budowania.
- **.NET SDK** (dowolna nowsza wersja obsługująca `dotnet build`/`dotnet test`)
  — do kompilacji i uruchamiania testów.

Budowanie **musi** odbywać się poleceniem `dotnet build`, a nie MSBuildem
z Visual Studio Build Tools — ten drugi nie ma zarejestrowanego resolvera
`Microsoft.NET.Sdk` i kompilacja się nie powiedzie.

## Budowanie

```bash
dotnet build Mixcloud.sln -c Release
```

Wynik trafia do `src/Mixcloud.Plugin/bin/Release/net481/`.

## Wdrożenie

Wdrożenie wykonuje skrypt `tools/deploy.ps1`, który kopiuje zbudowane pliki
do katalogu wtyczek AIMP:

```powershell
tools/deploy.ps1 -Configuration Release
```

Wymagania przy uruchamianiu skryptu:

- **PowerShell musi być uruchomiony jako administrator** — katalog docelowy
  leży w `C:\Program Files\AIMP\Plugins`, a zapis tam wymaga podniesionych
  uprawnień.
- **AIMP musi być zamknięty** — proces trzyma swoje DLL-e zablokowane,
  dopóki działa, więc kopiowanie nad uruchomionym odtwarzaczem się nie uda.

> **Ważne:** po wdrożeniu **uruchom AIMP ponownie**, nawet jeśli był otwarty
> przed wdrożeniem i tylko go zamknąłeś na czas kopiowania. Działająca
> instancja AIMP nie wykrywa nowo zainstalowanej wtyczki przy starcie — tylko
> pełny restart programu wczytuje nowe pliki. Ten jeden fakt kosztował
> realny czas debugowania przy pracy nad tym projektem.

## Testy

Testy jednostkowe działają na nagranych fixture'ach (`tests/fixtures/`) i nie
wymagają sieci:

```bash
dotnet test Mixcloud.sln -v:minimal
```

Dodatkowo istnieje opcjonalny test integracyjny (`LiveMixcloudTests`), który
łączy się z prawdziwym Mixcloud i realnym `yt-dlp`. Jest domyślnie pominięty
(SKIPPED) — służy jako wczesne ostrzeżenie, gdyby Mixcloud zmienił format
odpowiedzi i fixture'y przestały odzwierciedlać rzeczywistość. Aby go
uruchomić, ustaw zmienną środowiskową `MIXCLOUD_LIVE=1` (opcjonalnie też
`YTDLP_PATH`, jeśli `yt-dlp.exe` nie jest na `PATH`):

```bash
MIXCLOUD_LIVE=1 YTDLP_PATH="C:/sciezka/do/yt-dlp.exe" dotnet test tests/Mixcloud.Core.Tests --filter LiveMixcloudTests -v:minimal
```

Jeśli testy sieciowe zawiodą, a testy jednostkowe nadal przechodzą, to znak,
że coś zmieniło się po stronie Mixclouda — trzeba odświeżyć fixture'y
w `tests/fixtures/`.

## Dodawanie nowego języka

1. Skopiuj `src/Mixcloud.Plugin/Langs/english.lng` pod nową nazwą
   (np. `german.lng`).
2. Ustaw `LangId` na właściwy identyfikator języka Windows (np. `1031` dla
   niemieckiego — `polish.lng` używa `1045`, `english.lng` używa `1033`).
3. Przetłumacz wszystkie wartości, zachowując nazwy kluczy bez zmian.

Test `LanguageFileTests` pilnuje spójności: sprawdza, że `polish.lng`
i `english.lng` mają identyczny zestaw kluczy, że każda stała w
`Mixcloud.Core.Localization.StringKeys` ma odpowiednik w obu plikach oraz że
żadna wartość nie jest pusta. Niekompletne tłumaczenie nowego pliku językowego
nie zablokuje builda samo z siebie, ale każda zmiana kluczy w `english.lng`
lub `polish.lng` bez odpowiednika w drugim pliku wywali ten test — więc trzymaj
oba pliki bazowe w komplecie.

## Architektura

- **`Mixcloud.Core`** — logika domenowa (parsowanie i walidacja adresów
  Mixcloud, katalog/listing, ustawienia, uruchamianie procesów, instalacja
  i wywoływanie `yt-dlp`, lokalizacja). Nie ma żadnej zależności od AIMP SDK,
  dzięki czemu cała logika jest w pełni testowalna jednostkowo poza
  odtwarzaczem — to właśnie tutaj żyją testy w `tests/Mixcloud.Core.Tests`.
- **`Mixcloud.Plugin`** — cienka warstwa adaptera do AIMP: rejestracja
  wtyczki, menu, okna opcji, budowanie playlist i podpięcie źródła strumienia
  w oparciu wyłącznie o API z `Mixcloud.Core`. Ta warstwa nie ma testów
  automatycznych — jest weryfikowana ręcznie w działającym AIMP, ponieważ
  zależy bezpośrednio od SDK odtwarzacza.
