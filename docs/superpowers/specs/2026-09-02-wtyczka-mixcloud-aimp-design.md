# Wtyczka Mixcloud dla AIMP — specyfikacja projektowa

Data: 2026-09-02
Status: zatwierdzona, gotowa do planu implementacji

## Cel

Wtyczka do odtwarzacza AIMP dająca dostęp do Mixclouda: otwieranie dowolnego
adresu Mixclouda jako playlisty, wczytanie własnych ulubionych nagrań oraz
odtwarzanie miksów. Warstwa dostępu do serwisu opiera się na
yt-dlp, którym wtyczka zarządza samodzielnie. Interfejs jest dwujęzyczny:
polski i angielski.

## Środowisko docelowe

Środowisko zostało przygotowane i zweryfikowane 2026-09-02.

| Element | Stan |
|---|---|
| AIMP 5.4.0.2725, 64-bit, `C:\Program Files\AIMP` | zainstalowane |
| `bass_hls` (odtwarzanie strumieni HLS) | zainstalowane |
| VS Build Tools 2022 17.14, MSVC 14.44, C++/CLI (`msclr`) | zainstalowane |
| .NET Framework 4.8.1 Developer Pack (targeting pack) | **zainstalowane** |
| Źródło NuGet `nuget.org` | **dodane** (wcześniej nie było żadnego) |
| `AimpSDK-X64` 5.3.2394.5 | pobrany, build kontrolny przechodzi |
| .NET 8 SDK (do uruchamiania `dotnet build`) | dostępne |

Ustalenia z przygotowania środowiska, wiążące dla implementacji:

- **Projekt celuje w `net481`, nie `net48`.** Dostępny jest wyłącznie targeting
  pack 4.8.1; pakiet `AimpSDK-X64` deklaruje net48 „lub wyżej", więc referencja
  działa. Build kontrolny (`dotnet build` projektu net481 z tą referencją)
  przeszedł bez ostrzeżeń.
- **Buildujemy przez `dotnet` CLI, nie przez MSBuild z Build Tools.** MSBuild
  z instalacji Build Tools nie ma resolvera `Microsoft.NET.Sdk` i odrzuca
  projekty SDK-style.
- **Wrappera nie budujemy sami.** Pakiet NuGet zawiera gotowy, prekompilowany
  `aimp_dotnet.dll` (natywny proxy C++/CLI) oraz `AIMP.SDK.dll`. Toolchain
  C++/CLI nie jest więc na ścieżce krytycznej.
- .NET 8 służy wyłącznie jako narzędzie budowania. Sama wtyczka działa na
  runtime .NET Framework 4.8 wbudowanym w Windows 11.

## Struktura wdrożenia

Narzucona przez wrapper (`CopyPlugin.ps1` z pakietu NuGet). Katalog
`AIMP\Plugins\<Nazwa>\` musi zawierać:

| Plik | Pochodzenie |
|---|---|
| `<Nazwa>.dll` | kopia `aimp_dotnet.dll` z pakietu, **przemianowana** — to punkt wejścia, który ładuje AIMP |
| `<Nazwa>_plugin.dll` | nasz zarządzany assembly |
| `AIMP.SDK.dll` | z pakietu |
| pozostałe `*.dll` | zależności naszego projektu |
| `Langs\` | nasze pliki `polish.lng` i `english.lng` |

Skrypt z pakietu zakłada piaskownicę AIMP obok solucji, nie prawdziwą
instalację. Piszemy własny skrypt wdrożeniowy, celujący w
`C:\Program Files\AIMP\Plugins\`. Kopiowanie tam wymaga podniesionych
uprawnień — skrypt musi to wykryć i powiedzieć wprost, zamiast cicho zawieść.

## Ryzyko wiodące

`AimpSDK-X64` stoi na wersji 5.3 z lutego 2024 i nie był aktualizowany pod
AIMP 5.4. Zgodność jest prawdopodobna, ale niepotwierdzona. Plan implementacji
musi zaczynać się od spike'a: minimalna wtyczka, która ładuje się w AIMP 5.4
x64 i dopisuje pozycję do menu. Dopóki to nie przejdzie, reszta specyfikacji
jest bezprzedmiotowa.

## Architektura

Sześć modułów. Cztery pierwsze nie mają żadnej zależności od AIMP, dzięki czemu
dają się testować jednostkowo bez uruchamiania odtwarzacza. `Localization`
dotyka wyłącznie usługi MUI i chowa ją za interfejsem, który w testach da się
podmienić. `MixcloudPlugin` jest celowo cienkim adapterem, bo to jedyna
warstwa weryfikowana wyłącznie ręcznie.

| Moduł | Odpowiedzialność | Zależny od AIMP |
|---|---|---|
| `YtDlpService` | prywatna kopia yt-dlp.exe: pobranie, wersjonowanie, auto-update, wywołania procesu | nie |
| `MixcloudCatalog` | adres Mixclouda → lista pozycji `MixcloudTrack` | nie |
| `MediaSource` | dostarczenie AIMP grywalnego adresu: rozwiązanie strumienia i odświeżenie wygasłego, albo — w trybie fallback — pobranie pliku do katalogu tymczasowego i zarządzanie nim | nie |
| `MixcloudSettings` | handle użytkownika, ścieżki, flagi, serializacja | nie |
| `Localization` | odczyt napisów z plików `.lng` przez usługę MUI AIMP | tylko usługa MUI |
| `MixcloudPlugin` | cykl życia wtyczki, menu, strona ustawień, budowa playlisty | tak |

`MixcloudTrack` to rekord: adres strony Mixclouda, tytuł, wykonawca, czas
trwania, adres okładki.

### Granice modułów

`MixcloudCatalog` przyjmuje tekst JSON i zwraca listę pozycji — nie uruchamia
procesów i nie sięga do sieci. `YtDlpService` uruchamia procesy i zwraca surowy
tekst — nie wie, czym jest Mixcloud. Ten rozdział jest tym, co czyni testy
jednostkowe możliwymi; nie wolno go zacierać dla wygody.

## Przepływ: otwarcie adresu

1. Użytkownik wybiera z menu AIMP pozycję otwarcia adresu Mixclouda.
2. Dialog przyjmuje adres. Walidacja odrzuca wszystko spoza domeny
   `mixcloud.com` z czytelnym komunikatem.
3. `YtDlpService` woła yt-dlp z `--flat-playlist --dump-single-json`
   **w wątku roboczym**, z paskiem postępu i możliwością anulowania.
4. `MixcloudCatalog` parsuje wynik na listę `MixcloudTrack`.
5. Powstaje **nowa playlista**, nazwana tytułem miksu albo handle'em
   wykonawcy — nigdy nie dopisujemy do playlisty, której użytkownik właśnie
   słucha.

Pojedynczy miks daje playlistę z jedną pozycją. Adres profilu albo
`/favorites/` rozwija się w pełną listę. Oba przypadki przechodzą tą samą
ścieżką kodu — nie ma dwóch wariantów do utrzymywania.

## Przepływ: odtwarzanie

Pozycje w playliście przechowują **adres strony Mixclouda**, nie adres
strumienia. Dopiero gdy AIMP przystępuje do odtwarzania, `MediaSource` woła
`yt-dlp -g` i podstawia świeży adres HLS, który obsługuje zainstalowany
`bass_hls`.

Podejście to zakłada, że wrapper aimp_dotnet wystawia hook pozwalający podmienić
adres w momencie otwierania pozycji. **Założenie jest niezweryfikowane** i
spike musi je rozstrzygnąć.

### Fallback: pobieranie do katalogu tymczasowego

Jeśli strumieniowanie okaże się niewykonalne — brak hooka podmiany adresu albo
`bass_hls` nie radzi sobie ze strumieniem Mixclouda — wtyczka przełącza się na
pobieranie pliku i odtwarzanie lokalnie.

- Katalog: `%TEMP%\AIMP-Mixcloud\`, plik nazwany skrótem adresu strony, żeby
  ponowne odtworzenie tego samego miksu trafiło w gotowy plik zamiast pobierać
  go drugi raz.
- Pobieranie startuje w momencie żądania odtworzenia, w wątku roboczym,
  z widocznym postępem i możliwością anulowania. Dwugodzinny miks to setki
  megabajtów — użytkownik musi widzieć, że coś się dzieje, i móc się wycofać.
- Odtwarzanie rusza po zakończeniu pobierania. Nie próbujemy grać z pliku
  w trakcie zapisu — to źródło trudnych do zdiagnozowania błędów.
- **Sprzątanie**: katalog jest czyszczony przy starcie wtyczki, z zachowaniem
  plików nowszych niż 24 godziny, oraz ograniczony limitem rozmiaru
  (domyślnie 5 GB, konfigurowalny) — przy przekroczeniu kasowane są najdawniej
  używane pliki. Katalog tymczasowy nie może rosnąć w nieskończoność.
- Brak miejsca na dysku → jasny komunikat przed rozpoczęciem pobierania,
  nie w połowie.

**Decyzja o trybie zapada raz, na podstawie wyniku spike'a**, i zostaje
zapisana w specyfikacji jako fakt. Nie budujemy przełącznika ani dwóch
równolegle utrzymywanych ścieżek odtwarzania — to podwoiłoby powierzchnię
testów bez korzyści dla użytkownika. Jeśli później dojdzie potrzeba
pobierania offline mimo działającego strumieniowania, wróci to jako osobna,
świadoma decyzja.

## Przepływ: ulubione

Osobna komenda menu buduje adres `https://www.mixcloud.com/<handle>/favorites/`
z handle'a zapisanego w ustawieniach i przepuszcza go tym samym pipeline'em.
Wynik trafia do playlisty nazwanej „Mixcloud — ulubione (handle)".

Świadomie **nie** ma tu logowania: żadnych haseł, żadnych ciasteczek, żadnego
przechowywania poświadczeń. Ulubione nagrania są na profilu publiczne, więc
sama nazwa użytkownika wystarcza. Ceną jest brak dostępu do treści prywatnych
i Mixcloud Select.

## Zarządzanie yt-dlp

Wtyczka trzyma **własną kopię** `yt-dlp.exe` w katalogu profilu użytkownika
(`%APPDATA%\AIMP\Plugins\Mixcloud\`), niezależną od tego, co jest w systemowym
PATH. Katalog wtyczki w `Program Files` odpada — wymagałby uprawnień
administratora przy każdej aktualizacji.

- Pierwszy start: pobranie najnowszego wydania z GitHub Releases.
- Sprawdzanie aktualizacji: raz na dobę, porównanie tagu `releases/latest`
  z zapisanym lokalnie.
- Nowa wersja ląduje **obok** działającej binarki i zostaje podmieniona
  atomowo dopiero **przy następnym starcie AIMP**. Nigdy nie podmieniamy pliku,
  który właśnie jest uruchomiony.
- Brak sieci albo błąd pobierania to cichy no-op — odtwarzanie działa dalej na
  dotychczasowej wersji. Awaria aktualizacji nie może psuć odtwarzania.

## Dwujęzyczność

Wtyczka korzysta z **natywnego mechanizmu lokalizacji AIMP**, nie z własnego.

- Katalog `Langs\` obok DLL-a wtyczki, dokładnie jak w `aimp_YouTube`.
- Dwa pliki: `polish.lng` i `english.lng`, format INI w UTF-8, z nagłówkiem
  `[FILE]` zawierającym `Author`, `Name`, `VersionID` i `LangId`
  (1045 dla polskiego, 1033 dla angielskiego).
- Napisy pogrupowane w sekcje w przestrzeni `Mixcloud.*` — osobno komunikaty,
  osobno menu, osobno dialogi, osobno ustawienia.
- Odczyt przez usługę MUI AIMP. Język idzie za ustawieniem odtwarzacza —
  wtyczka nie ma własnego przełącznika języka.
- Gdy wybrany język odtwarzacza nie ma odpowiednika w `Langs\` wtyczki,
  używany jest `english.lng`.

Zasady twarde:

1. **Zero napisów wpisanych na sztywno w kodzie.** Każdy tekst widoczny dla
   użytkownika przechodzi przez helper lokalizacji.
2. Brakujący klucz zwraca samą nazwę klucza. Widać to natychmiast podczas
   pracy i nie wywala wtyczki u użytkownika.
3. Test jednostkowy porównuje zbiory kluczy obu plików `.lng` i nie przechodzi,
   gdy się rozjadą. To jedyny sposób, żeby tłumaczenia nie zgniły po cichu.

## Obsługa błędów

| Sytuacja | Reakcja |
|---|---|
| brak lub uszkodzony yt-dlp | komunikat z akcją pobrania |
| adres spoza Mixclouda | odrzucenie w dialogu, przed jakąkolwiek pracą |
| pusty wynik albo profil prywatny | jasna informacja, nie milcząco pusta playlista |
| wygasły adres strumienia | jeden automatyczny retry z ponownym rozwiązaniem |
| brak miejsca na dysku (tryb fallback) | komunikat przed startem pobierania, nie w połowie |
| błąd sieci przy aktualizacji | cichy no-op |

Każda operacja sieciowa i każde uruchomienie procesu odbywa się poza wątkiem
interfejsu. AIMP nie może się zawiesić przez wtyczkę — to wymóg twardy,
nie preferencja.

## Testy

- **Jednostkowe** dla `YtDlpService`, `MixcloudCatalog`, `MediaSource`,
  `MixcloudSettings` i spójności plików `.lng`, na **nagranych odpowiedziach
  JSON z yt-dlp**. Szybkie, deterministyczne, bez sieci.
- **Integracyjny**, jeden, opt-in, wymagający sieci, na prawdziwym publicznym
  adresie Mixclouda. Pełni rolę wczesnego ostrzeżenia, gdy Mixcloud zmieni
  format i nagrane odpowiedzi przestaną odpowiadać rzeczywistości.
- **Ręcznie**: adapter `MixcloudPlugin` w uruchomionym AIMP.

## Zakres etapu 1

W zakresie: otwieranie adresu, ulubione nagrania, tworzenie playlist,
odtwarzanie (strumieniowe albo z pobraniem do katalogu tymczasowego — rozstrzyga
spike), samodzielna aktualizacja yt-dlp, strona ustawień
(handle, wersja yt-dlp z ręcznym sprawdzeniem, przełącznik auto-update,
limit pozycji przy dużych profilach), pełna dwujęzyczność.

Świadomie poza zakresem: dokowany panel z drzewem źródeł, wyszukiwarka,
lista obserwowanych wykonawców (`/following/` — yt-dlp tego nie obsługuje,
wymagałoby własnych zapytań do GraphQL Mixclouda), pobieranie offline,
import ciasteczek, okładki jako artwork w AIMP.
