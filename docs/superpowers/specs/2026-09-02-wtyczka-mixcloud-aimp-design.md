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

Ryzyko podmiany adresu strumienia zostało **zamknięte**: wrapper wystawia
`IAimpExtensionPlayerHook` z metodą `bool OnCheckURL(ref string url)`, czyli
dokładnie punkt zaczepienia, którego potrzebujemy.

## Ustalenia z rozpoznania yt-dlp

Zweryfikowane 2026-09-02 na yt-dlp 2026.06.09, na żywym Mixcloudzie. Odpowiedzi
zapisano jako fixture'y w `tests/fixtures/`.

**Odtwarzanie jest łatwiejsze, niż zakładaliśmy.** Obok strumieni HLS Mixcloud
wystawia **progresywny plik m4a po HTTPS** (`format_id=http`). Selektor
`-f "http/hls-192/bestaudio"` rozwiązuje adres w **2 sekundy**, a serwer
odpowiada `206 Partial Content` z `audio/mp4` — strumień jest więc
**przewijalny**, a AIMP odtworzy go natywnie przez zainstalowany `bass_aac`,
bez udziału `bass_hls`. Adres zawiera parametr `?sig=`, czyli wygasa —
odświeżanie pozostaje potrzebne.

**`--dump-single-json` jest nie do użycia na listach.** Wymusza zmaterializowanie
całej playlisty; na profilu z tysiącami pozycji yt-dlp stronicuje bez końca
i `--playlist-end` tego nie zatrzymuje. Zaobserwowano zawieszenie na
`/NTSRadio/uploads/`. Obowiązuje wariant leniwy, linia po linii:
`--flat-playlist --dump-json -I 1:<limit>`.

**`/favorites/` — nasz główny przypadek użycia — działa i jest leniwe.** Zero
zapytań stronicujących, natychmiastowa odpowiedź, `-I` respektowane.

**Pozycje w trybie flat nie mają tytułów.** Zawierają wyłącznie `id`
(w formacie `<autor>_<slug>`), `url`, oraz — co cenne — `playlist_title`
(np. „Spartacus (favorites)") i `playlist_count`. Wynikają z tego dwie decyzje:

- **Nazwa playlisty bierze się z `playlist_title`**, prosto od yt-dlp. Nie
  budujemy jej sami z handle'a.
- **Tytuły pozycji uzupełnia AIMP leniwie**, przez
  `IAimpExtensionFileInfoProvider`, który dla adresu Mixclouda dociąga
  metadane. Do czasu uzupełnienia pokazujemy tytuł wyprowadzony ze slugu
  w `id`. Rozwiązywanie metadanych wszystkich pozycji z góry byłoby
  nieakceptowalnie wolne — 63 ulubione to 63 osobne wywołania yt-dlp.

**Każde wywołanie listujące musi mieć twardy limit i timeout.** Część typów
list potrafi się zapętlić; wtyczka nie może na tym zawisnąć.

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
| `MixcloudPlugin` | cykl życia wtyczki, menu, strona ustawień, budowa playlisty, rejestracja rozszerzeń `IAimpExtensionPlayerHook` i `IAimpExtensionFileInfoProvider` | tak |

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
3. `YtDlpService` woła yt-dlp **w wątku roboczym**, z paskiem postępu
   i możliwością anulowania:
   - lista → `--flat-playlist --dump-json -I 1:<limit>`, czytane linia po linii;
   - pojedynczy miks → `--dump-single-json`.
4. `MixcloudCatalog` parsuje wynik na listę `MixcloudTrack`.
5. Powstaje **nowa playlista**, nazwana wartością `playlist_title` z odpowiedzi
   yt-dlp (dla pojedynczego miksu — jego tytułem) — nigdy nie dopisujemy do
   playlisty, której użytkownik właśnie słucha.

Pojedynczy miks daje playlistę z jedną pozycją. Adres profilu albo
`/favorites/` rozwija się w pełną listę. Oba przypadki przechodzą tą samą
ścieżką kodu — nie ma dwóch wariantów do utrzymywania.

## Przepływ: odtwarzanie

Pozycje w playliście przechowują **adres strony Mixclouda**, nie adres
strumienia. Dopiero gdy AIMP przystępuje do odtwarzania, wtyczka przechwytuje
to przez `IAimpExtensionPlayerHook.OnCheckURL(ref string url)`, a `MediaSource`
woła `yt-dlp -g -f "http/hls-192/bestaudio"` i podstawia świeży adres
progresywnego m4a — przewijalny i obsługiwany natywnie przez `bass_aac`.

**ROZSTRZYGNIETE 2026-09-02: tryb strumieniowy dziala.** Spike wykonany na
zywym AIMP 5.4 wykazal, ze odtwarzacz wola `OnCheckURL` dla adresow Mixclouda
i honoruje podmieniony adres. Dowod: po odtworzeniu AIMP zapisal w playliste
metadane strumienia — 2 kanaly, 44100 Hz, 3 949 875 ms, kodek MP4 — ktore moze
znac wylacznie po faktycznym otwarciu i zdekodowaniu podstawionego adresu.
Czas trwania zgadza sie z tym, co raportuje yt-dlp.

W konsekwencji **zadanie 13 planu (fallback z pobieraniem do katalogu
tymczasowego) nie bedzie realizowane**. Sekcja ponizej pozostaje w dokumencie
jako zapis rozwazanego wariantu, nie jako zakres prac.

### Fallback: pobieranie do katalogu tymczasowego

Jeśli spike wykaże, że AIMP nie honoruje podmienionego adresu, wtyczka
przełącza się na pobieranie pliku i odtwarzanie lokalnie.

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

Wtyczka trzyma **własną kopię** `yt-dlp.exe` w podkatalogu `Mixcloud\` profilu
AIMP, którego ścieżkę daje `Core.GetPath(AimpCorePathType.Profile)` — nie
zgadujemy `%APPDATA%`. Katalog wtyczki w `Program Files` odpada: wymagałby
uprawnień administratora przy każdej aktualizacji.

- Pierwszy start: pobranie najnowszego wydania z GitHub Releases.
- Sprawdzanie aktualizacji: raz na dobę, porównanie tagu `releases/latest`
  z zapisanym lokalnie.
- Nowa wersja ląduje **obok** działającej binarki i zostaje podmieniona dopiero
  **przy następnym starcie AIMP**. Nigdy nie podmieniamy pliku, który właśnie
  jest uruchomiony.
- **Nieudane pobranie kasuje plik oczekujący.** Przerwany transfer zostawia
  obcięty plik; bez tego zostałby przy następnym starcie awansowany na miejsce
  działającej binarki i zepsułby odtwarzanie. Przed awansem sprawdzamy również
  rozmiar. Tag wersji zapisujemy dopiero po udanym pobraniu, żeby ponowna próba
  nie została pominięta.
- **Podmiana zachowuje kopię zapasową.** Stara binarka idzie najpierw na bok,
  nowa wchodzi na jej miejsce, a kopia znika dopiero po powodzeniu. Sekwencja
  „skasuj, potem przenieś" mogła zostawić użytkownika bez żadnej binarki.
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
