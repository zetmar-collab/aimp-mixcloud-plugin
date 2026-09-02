# Wtyczka Mixcloud dla AIMP — plan implementacji

> **Dla agentów wykonawczych:** WYMAGANY SUB-SKILL: użyj
> `superpowers:subagent-driven-development` (zalecane) albo
> `superpowers:executing-plans`, aby realizować ten plan zadanie po zadaniu.
> Kroki mają składnię checkboxów (`- [ ]`) do śledzenia postępu.

**Cel:** Wtyczka do AIMP 5.4 x64 otwierająca adresy Mixclouda jako playlisty,
wczytująca ulubione nagrania użytkownika i odtwarzająca miksy przez yt-dlp,
z dwujęzycznym interfejsem PL/EN.

**Architektura:** Dwa projekty. `Mixcloud.Core` zawiera całą logikę domenową
i nie ma żadnej referencji do AIMP — stąd bierze się testowalność.
`Mixcloud.Plugin` to cienki adapter rejestrujący menu, rozszerzenia
i stronę ustawień. Odtwarzanie działa przez podmianę adresu w
`IAimpExtensionPlayerHook.OnCheckURL`, metadane dociągane są leniwie przez
`IAimpExtensionFileInfoProvider`.

**Stack:** C# / .NET Framework 4.8.1, `AimpSDK-X64` 5.3.2394.5,
Newtonsoft.Json 13.0.3, xUnit, yt-dlp, PowerShell (wdrożenie).

## Global Constraints

Poniższe obowiązuje w **każdym** zadaniu i nie jest powtarzane w treści zadań.

- **TargetFramework: `net481`.** Nie `net48` — na maszynie jest wyłącznie
  targeting pack 4.8.1.
- **PlatformTarget: `x64`.** AIMP jest 64-bitowy; 32-bitowa wtyczka się nie
  załaduje.
- **Budujemy przez `dotnet build`**, nigdy przez MSBuild z Build Tools (brak
  resolvera `Microsoft.NET.Sdk`).
- **Zero napisów widocznych dla użytkownika wpisanych na sztywno w kodzie.**
  Każdy przechodzi przez `IStringProvider`. Dotyczy też komunikatów błędów.
- **Żadnej operacji sieciowej ani uruchomienia procesu na wątku UI.**
- **Każde wywołanie yt-dlp ma jawny timeout.** Wywołania listujące mają
  dodatkowo twardy limit `-I 1:<limit>`.
- **Selektor formatu: `http/hls-192/bestaudio`** — dokładnie ten ciąg.
- **Katalog danych wtyczki:** `Core.GetPath(AimpCorePathType.Profile)` +
  `\Mixcloud\`. Nigdy nie zgadujemy `%APPDATA%` ani nie piszemy do
  `Program Files`.
- **Wartości `AimpActionResult` sprawdzamy zawsze.** `ResultType != OK` to
  błąd do obsłużenia, nie do zignorowania.
- Commit po każdym zadaniu, wiadomości po polsku bez polskich znaków
  diakrytycznych (spójnie z istniejącą historią).

---

## Struktura plików

```
Mixcloud.sln
src/
  Mixcloud.Core/                        # zero zaleznosci od AIMP
    Mixcloud.Core.csproj
    Urls/MixcloudUrl.cs                 # walidacja i klasyfikacja adresow
    Urls/SlugTitle.cs                   # slug -> czytelny tytul
    Process/IProcessRunner.cs
    Process/ProcessRunner.cs
    Process/ProcessResult.cs
    YtDlp/YtDlpService.cs               # wywolania yt-dlp
    YtDlp/YtDlpInstaller.cs             # pobranie i auto-update binarki
    Catalog/MixcloudTrack.cs
    Catalog/MixcloudListing.cs
    Catalog/MixcloudCatalog.cs          # parsowanie JSON
    Media/IMediaSource.cs
    Media/StreamMediaSource.cs
    Media/DownloadMediaSource.cs        # tryb fallback
    Media/TempCache.cs
    Settings/MixcloudSettings.cs
    Localization/IStringProvider.cs
    Localization/StringKeys.cs
  Mixcloud.Plugin/                      # cienki adapter AIMP
    Mixcloud.Plugin.csproj              # AssemblyName = Mixcloud
    MixcloudPlugin.cs
    PluginContext.cs
    Localization/MuiStringProvider.cs
    Extensions/MixcloudPlayerHook.cs
    Extensions/MixcloudFileInfoProvider.cs
    Playlists/PlaylistBuilder.cs
    Ui/OpenUrlDialog.cs
    Ui/OptionsFrame.cs
    Langs/polish.lng
    Langs/english.lng
tests/
  Mixcloud.Core.Tests/
    Mixcloud.Core.Tests.csproj
    MixcloudUrlTests.cs
    SlugTitleTests.cs
    MixcloudCatalogTests.cs
    YtDlpServiceTests.cs
    YtDlpInstallerTests.cs
    MixcloudSettingsTests.cs
    TempCacheTests.cs
    LanguageFileTests.cs
    FakeProcessRunner.cs
  fixtures/                             # juz istnieja w repo
    cloudcast-single.json
    favorites-flat.jsonl
tools/
  deploy.ps1
```

---

### Task 1: Szkielet solucji, wtyczka ładująca się w AIMP

Zadanie kończy się dowodem, że wrapper działa z AIMP 5.4. Dopóki to nie
przejdzie, dalsze zadania nie mają sensu.

**Files:**
- Create: `Mixcloud.sln`
- Create: `src/Mixcloud.Core/Mixcloud.Core.csproj`
- Create: `src/Mixcloud.Plugin/Mixcloud.Plugin.csproj`
- Create: `src/Mixcloud.Plugin/MixcloudPlugin.cs`
- Create: `tests/Mixcloud.Core.Tests/Mixcloud.Core.Tests.csproj`
- Create: `tools/deploy.ps1`

**Interfaces:**
- Produces: klasa `Mixcloud.Plugin.MixcloudPlugin : AimpPlugin` z nadpisanymi
  `OnInitialize(IAimpPlayer player, int pluginId)` i `OnDispose()`.
  Właściwość bazowa `Player` (typ `IAimpPlayer`) jest punktem dostępu do
  wszystkich serwisów w kolejnych zadaniach.

- [ ] **Krok 1: Utwórz projekty i solucję**

```bash
cd "C:/Users/Marek/Claude/Aimp"
dotnet new classlib -n Mixcloud.Core -o src/Mixcloud.Core -f net481
dotnet new classlib -n Mixcloud.Plugin -o src/Mixcloud.Plugin -f net481
dotnet new xunit -n Mixcloud.Core.Tests -o tests/Mixcloud.Core.Tests -f net481
dotnet new sln -n Mixcloud
dotnet sln add src/Mixcloud.Core src/Mixcloud.Plugin tests/Mixcloud.Core.Tests
rm -f src/Mixcloud.Core/Class1.cs src/Mixcloud.Plugin/Class1.cs
```

- [ ] **Krok 2: Ustaw csproj projektu wtyczki**

Zawartość `src/Mixcloud.Plugin/Mixcloud.Plugin.csproj`. `AssemblyName` musi
brzmieć `Mixcloud`, bo skrypt wdrożeniowy szuka pliku o nazwie zgodnej
z nazwą wtyczki.

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net481</TargetFramework>
    <PlatformTarget>x64</PlatformTarget>
    <AssemblyName>Mixcloud</AssemblyName>
    <RootNamespace>Mixcloud.Plugin</RootNamespace>
    <LangVersion>latest</LangVersion>
    <UseWindowsForms>true</UseWindowsForms>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="AimpSDK-X64" Version="5.3.2394.5" />
    <ProjectReference Include="..\Mixcloud.Core\Mixcloud.Core.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Krok 3: Ustaw csproj rdzenia i testów**

`src/Mixcloud.Core/Mixcloud.Core.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net481</TargetFramework>
    <PlatformTarget>x64</PlatformTarget>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
  </ItemGroup>
</Project>
```

W `tests/Mixcloud.Core.Tests/Mixcloud.Core.Tests.csproj` dodaj do istniejącego
`<Project>` referencję i kopiowanie fixture'ów:

```xml
  <ItemGroup>
    <ProjectReference Include="..\..\src\Mixcloud.Core\Mixcloud.Core.csproj" />
  </ItemGroup>
  <ItemGroup>
    <None Include="..\fixtures\**\*" LinkBase="fixtures" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
```

- [ ] **Krok 4: Napisz klasę wtyczki**

`src/Mixcloud.Plugin/MixcloudPlugin.cs`. Na tym etapie wtyczka tylko dopisuje
pozycję menu — to jest cały dowód, którego szukamy. Napis jest tu tymczasowo
wpisany na sztywno, bo lokalizacja powstaje w zadaniu 10; zadanie 10 go usuwa.

```csharp
using AIMP.SDK;
using AIMP.SDK.MenuManager;
using AIMP.SDK.MenuManager.Objects;

namespace Mixcloud.Plugin
{
    [AimpPlugin("Mixcloud", "Marek Zettel", "1.0.0",
        AimpPluginType = AimpPluginType.Addons,
        Description = "Mixcloud integration for AIMP")]
    public sealed class MixcloudPlugin : AimpPlugin
    {
        private IAimpMenuItem _probeItem;

        public override void Initialize()
        {
            var created = Player.Core.CreateAimpObject<IAimpMenuItem>();
            if (created.ResultType != ActionResultType.OK) return;

            _probeItem = created.Result;
            _probeItem.Id = "Mixcloud.Probe";
            _probeItem.Name = "Mixcloud: dziala";
            _probeItem.Style = MenuItemStyle.Normal;
            Player.ServiceMenuManager.Add(ParentMenuType.PlayerMainOpen, _probeItem);
        }

        public override void Dispose()
        {
            if (_probeItem != null)
            {
                Player.ServiceMenuManager.Delete(_probeItem);
                _probeItem = null;
            }
        }
    }
}
```

Jeśli kompilator zgłosi, że `Initialize`/`Dispose` nie są `virtual`, użyj
`OnInitialize(IAimpPlayer player, int pluginId)` i `OnDispose()` — refleksja
potwierdziła obecność obu par. Sprawdź `AimpPlugin` w podpowiedziach IDE
i wybierz tę, która jest nadpisywalna.

- [ ] **Krok 5: Napisz skrypt wdrożeniowy**

`tools/deploy.ps1`. Układ katalogu narzuca wrapper: punkt wejścia to
**przemianowana kopia** `aimp_dotnet.dll`, nasz kod ląduje obok jako
`Mixcloud_plugin.dll`.

```powershell
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
```

- [ ] **Krok 6: Zbuduj**

Uruchom: `dotnet build Mixcloud.sln -c Debug`
Oczekiwane: `Kompilacja powiodła się`, zero błędów.

- [ ] **Krok 7: Wdróż i sprawdź w AIMP**

Zamknij AIMP, a następnie w PowerShellu **uruchomionym jako administrator**:

```bash
pwsh -File tools/deploy.ps1 -Configuration Debug
```

Uruchom AIMP i otwórz menu główne (przycisk otwierania plików).
Oczekiwane: widoczna pozycja **„Mixcloud: dziala"**.

Jeśli AIMP nie startuje albo pozycji nie ma, sprawdź
`C:\Program Files\AIMP\Plugins\Mixcloud\` — muszą tam być `Mixcloud.dll`,
`Mixcloud_plugin.dll` i `AIMP.SDK.dll`. **To jest moment decyzyjny całego
projektu**: jeśli wrapper 5.3 nie działa z AIMP 5.4, zatrzymaj się i zgłoś to,
zamiast obchodzić problem.

- [ ] **Krok 8: Commit**

```bash
git add -A && git commit -m "Szkielet solucji i wtyczka ladujaca sie w AIMP 5.4"
```

---

### Task 2: Spike — potwierdzenie OnCheckURL

Rozstrzyga, czy odtwarzanie idzie trybem strumieniowym, czy fallbackiem
z pobieraniem. Wynik jest wiążący dla zadań 11 i 13.

**Files:**
- Create: `src/Mixcloud.Plugin/Extensions/MixcloudPlayerHook.cs`
- Modify: `src/Mixcloud.Plugin/MixcloudPlugin.cs`

**Interfaces:**
- Consumes: `MixcloudPlugin.Player` z zadania 1.
- Produces: `MixcloudPlayerHook : IAimpExtensionPlayerHook` z metodą
  `bool OnCheckURL(ref string url)`. Rejestracja przez
  `Player.Core.RegisterExtension(hook)`.

- [ ] **Krok 1: Napisz hook z twardo zaszytym adresem**

Adres poniżej pochodzi z realnego rozpoznania i wygasa. Jeśli test da ciszę
zamiast dźwięku, odśwież go poleceniem:
`yt-dlp -g -f "http/hls-192/bestaudio" "https://www.mixcloud.com/NTSRadio/loraine-james-1st-september-2026/"`

`src/Mixcloud.Plugin/Extensions/MixcloudPlayerHook.cs`:

```csharp
using System;
using System.IO;
using AIMP.SDK.Player.Extensions;

namespace Mixcloud.Plugin.Extensions
{
    public sealed class MixcloudPlayerHook : IAimpExtensionPlayerHook
    {
        private readonly Func<string, string> _resolve;
        private readonly string _logPath;

        public MixcloudPlayerHook(Func<string, string> resolve, string logPath)
        {
            _resolve = resolve;
            _logPath = logPath;
        }

        public bool OnCheckURL(ref string url)
        {
            File.AppendAllText(_logPath, "OnCheckURL: " + url + Environment.NewLine);

            if (url == null || url.IndexOf("mixcloud.com", StringComparison.OrdinalIgnoreCase) < 0)
                return false;

            var resolved = _resolve(url);
            if (string.IsNullOrEmpty(resolved)) return false;

            File.AppendAllText(_logPath, "  -> " + resolved + Environment.NewLine);
            url = resolved;
            return true;
        }
    }
}
```

- [ ] **Krok 2: Zarejestruj hook w wtyczce**

W `MixcloudPlugin.Initialize()`, po dodaniu pozycji menu, dopisz. Stała
`SpikeDirectUrl` to adres odświeżony w kroku 1.

```csharp
            var logPath = Path.Combine(Path.GetTempPath(), "mixcloud-spike.log");
            _hook = new Extensions.MixcloudPlayerHook(_ => SpikeDirectUrl, logPath);
            Player.Core.RegisterExtension(_hook);
```

Pola i stała w klasie:

```csharp
        private Extensions.MixcloudPlayerHook _hook;
        private const string SpikeDirectUrl = "<wklej adres z kroku 1>";
```

W `Dispose()` dopisz: `if (_hook != null) Player.Core.UnregisterExtension(_hook);`

- [ ] **Krok 3: Zbuduj i wdróż**

```bash
dotnet build Mixcloud.sln -c Debug
```

Następnie `tools/deploy.ps1` jak w zadaniu 1 (AIMP zamknięty, PowerShell jako
administrator).

- [ ] **Krok 4: Przeprowadź test**

W AIMP użyj wbudowanego dodawania adresu i wklej stronę miksu:
`https://www.mixcloud.com/NTSRadio/loraine-james-1st-september-2026/`
Naciśnij odtwarzanie.

Sprawdź `%TEMP%\mixcloud-spike.log`.

Trzy możliwe wyniki, każdy prowadzi gdzie indziej:

| Obserwacja | Znaczenie | Konsekwencja |
|---|---|---|
| log zawiera `OnCheckURL` **i słychać dźwięk** | hook działa, AIMP honoruje podmieniony adres | tryb strumieniowy — zadanie 13 **pomijamy** |
| log zawiera `OnCheckURL`, ale cisza | hook wołany, adres nieakceptowany lub wygasł | odśwież adres i powtórz; jeśli nadal cisza — fallback |
| log pusty | hook nie jest wołany dla tego adresu | fallback z pobieraniem — zadanie 13 **jest obowiązkowe** |

- [ ] **Krok 5: Zapisz wynik w specyfikacji**

Dopisz do `docs/superpowers/specs/2026-09-02-wtyczka-mixcloud-aimp-design.md`
w sekcji „Przepływ: odtwarzanie" jedno zdanie stwierdzające rozstrzygnięcie,
z datą. Wynik spike'a przestaje być hipotezą i staje się faktem projektowym.

- [ ] **Krok 6: Commit**

```bash
git add -A && git commit -m "Spike: weryfikacja OnCheckURL i wybor trybu odtwarzania"
```

---

### Task 3: MixcloudUrl — walidacja i klasyfikacja adresów

**Files:**
- Create: `src/Mixcloud.Core/Urls/MixcloudUrl.cs`
- Test: `tests/Mixcloud.Core.Tests/MixcloudUrlTests.cs`

**Interfaces:**
- Produces:
  - `enum MixcloudUrlKind { Invalid, Cloudcast, Listing }`
  - `sealed class MixcloudUrl` z właściwościami
    `MixcloudUrlKind Kind`, `string Normalized`, `string UserSlug`,
    `string CloudcastSlug` (null dla list)
  - `static MixcloudUrl Parse(string raw)` — nigdy nie rzuca, dla śmieci
    zwraca obiekt z `Kind == Invalid`

- [ ] **Krok 1: Napisz testy**

`tests/Mixcloud.Core.Tests/MixcloudUrlTests.cs`:

```csharp
using Mixcloud.Core.Urls;
using Xunit;

public class MixcloudUrlTests
{
    [Theory]
    [InlineData("https://www.mixcloud.com/sub88/mental-place-26/")]
    [InlineData("https://mixcloud.com/sub88/mental-place-26")]
    [InlineData("  https://www.mixcloud.com/sub88/mental-place-26/  ")]
    public void RozpoznajePojedynczyMiks(string raw)
    {
        var u = MixcloudUrl.Parse(raw);
        Assert.Equal(MixcloudUrlKind.Cloudcast, u.Kind);
        Assert.Equal("sub88", u.UserSlug);
        Assert.Equal("mental-place-26", u.CloudcastSlug);
        Assert.Equal("https://www.mixcloud.com/sub88/mental-place-26/", u.Normalized);
    }

    [Theory]
    [InlineData("https://www.mixcloud.com/spartacus/favorites/")]
    [InlineData("https://www.mixcloud.com/spartacus/uploads/")]
    [InlineData("https://www.mixcloud.com/spartacus/listens/")]
    [InlineData("https://www.mixcloud.com/spartacus/stream/")]
    [InlineData("https://www.mixcloud.com/spartacus/")]
    public void RozpoznajeListy(string raw)
    {
        var u = MixcloudUrl.Parse(raw);
        Assert.Equal(MixcloudUrlKind.Listing, u.Kind);
        Assert.Equal("spartacus", u.UserSlug);
        Assert.Null(u.CloudcastSlug);
    }

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=abc")]
    [InlineData("https://mixcloud.com.evil.example/sub88/mix/")]
    [InlineData("nie-adres")]
    [InlineData("")]
    [InlineData(null)]
    public void OdrzucaObceIZepsuteAdresy(string raw)
    {
        Assert.Equal(MixcloudUrlKind.Invalid, MixcloudUrl.Parse(raw).Kind);
    }

    [Fact]
    public void BudujeAdresUlubionychZHandle()
    {
        var u = MixcloudUrl.ForFavorites("spartacus");
        Assert.Equal(MixcloudUrlKind.Listing, u.Kind);
        Assert.Equal("https://www.mixcloud.com/spartacus/favorites/", u.Normalized);
    }
}
```

- [ ] **Krok 2: Uruchom testy i potwierdź, że padają**

Run: `dotnet test tests/Mixcloud.Core.Tests -v:minimal`
Oczekiwane: FAIL — `MixcloudUrl` nie istnieje (błąd kompilacji CS0246).

- [ ] **Krok 3: Napisz implementację**

`src/Mixcloud.Core/Urls/MixcloudUrl.cs`:

```csharp
using System;
using System.Linq;

namespace Mixcloud.Core.Urls
{
    public enum MixcloudUrlKind { Invalid, Cloudcast, Listing }

    public sealed class MixcloudUrl
    {
        private static readonly string[] ListingSegments =
            { "uploads", "favorites", "listens", "stream", "playlists" };

        public MixcloudUrlKind Kind { get; private set; }
        public string Normalized { get; private set; }
        public string UserSlug { get; private set; }
        public string CloudcastSlug { get; private set; }

        private MixcloudUrl() { }

        private static readonly MixcloudUrl InvalidUrl =
            new MixcloudUrl { Kind = MixcloudUrlKind.Invalid };

        public static MixcloudUrl ForFavorites(string handle)
        {
            if (string.IsNullOrWhiteSpace(handle)) return InvalidUrl;
            return Parse("https://www.mixcloud.com/" + handle.Trim() + "/favorites/");
        }

        public static MixcloudUrl Parse(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return InvalidUrl;

            Uri uri;
            if (!Uri.TryCreate(raw.Trim(), UriKind.Absolute, out uri)) return InvalidUrl;
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                return InvalidUrl;

            // Dokladne dopasowanie hosta - "mixcloud.com.evil.example" musi odpasc.
            var host = uri.Host.ToLowerInvariant();
            if (host != "mixcloud.com" && host != "www.mixcloud.com") return InvalidUrl;

            var seg = uri.AbsolutePath
                .Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries)
                .ToArray();
            if (seg.Length == 0 || seg.Length > 2) return InvalidUrl;

            var user = seg[0];
            if (seg.Length == 1)
                return Listing(user, "https://www.mixcloud.com/" + user + "/");

            var second = seg[1];
            if (ListingSegments.Contains(second, StringComparer.OrdinalIgnoreCase))
                return Listing(user, "https://www.mixcloud.com/" + user + "/" + second + "/");

            return new MixcloudUrl
            {
                Kind = MixcloudUrlKind.Cloudcast,
                UserSlug = user,
                CloudcastSlug = second,
                Normalized = "https://www.mixcloud.com/" + user + "/" + second + "/"
            };
        }

        private static MixcloudUrl Listing(string user, string normalized)
        {
            return new MixcloudUrl
            {
                Kind = MixcloudUrlKind.Listing,
                UserSlug = user,
                CloudcastSlug = null,
                Normalized = normalized
            };
        }
    }
}
```

- [ ] **Krok 4: Uruchom testy i potwierdź, że przechodzą**

Run: `dotnet test tests/Mixcloud.Core.Tests -v:minimal`
Oczekiwane: PASS, 14 testów.

- [ ] **Krok 5: Commit**

```bash
git add -A && git commit -m "MixcloudUrl: walidacja i klasyfikacja adresow"
```

---

### Task 4: SlugTitle — czytelny tytuł ze slugu

Pozycje w trybie flat nie mają tytułów, więc do czasu leniwego uzupełnienia
metadanych pokazujemy tytuł wyprowadzony ze slugu adresu.

**Files:**
- Create: `src/Mixcloud.Core/Urls/SlugTitle.cs`
- Test: `tests/Mixcloud.Core.Tests/SlugTitleTests.cs`

**Interfaces:**
- Produces: `static class SlugTitle` z metodą `string FromSlug(string slug)`.

- [ ] **Krok 1: Napisz testy**

```csharp
using Mixcloud.Core.Urls;
using Xunit;

public class SlugTitleTests
{
    [Theory]
    [InlineData("mental-place-26", "Mental Place 26")]
    [InlineData("si-those-days-enr42", "Si Those Days Enr42")]
    [InlineData("loraine-james-1st-september-2026", "Loraine James 1st September 2026")]
    [InlineData("single", "Single")]
    public void ZamieniaSlugNaTytul(string slug, string oczekiwany)
    {
        Assert.Equal(oczekiwany, SlugTitle.FromSlug(slug));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void PustySlugDajePustyTytul(string slug)
    {
        Assert.Equal(string.Empty, SlugTitle.FromSlug(slug));
    }

    [Fact]
    public void ScalaWielokrotneMyslniki()
    {
        Assert.Equal("A B", SlugTitle.FromSlug("a---b"));
    }
}
```

- [ ] **Krok 2: Uruchom testy i potwierdź, że padają**

Run: `dotnet test tests/Mixcloud.Core.Tests --filter SlugTitleTests -v:minimal`
Oczekiwane: FAIL — CS0246, `SlugTitle` nie istnieje.

- [ ] **Krok 3: Napisz implementację**

```csharp
using System;
using System.Globalization;
using System.Linq;

namespace Mixcloud.Core.Urls
{
    public static class SlugTitle
    {
        public static string FromSlug(string slug)
        {
            if (string.IsNullOrWhiteSpace(slug)) return string.Empty;

            var words = slug
                .Split(new[] { '-', '_' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(Capitalize);

            return string.Join(" ", words);
        }

        private static string Capitalize(string word)
        {
            if (word.Length == 0) return word;
            return char.ToUpper(word[0], CultureInfo.InvariantCulture) + word.Substring(1);
        }
    }
}
```

- [ ] **Krok 4: Uruchom testy i potwierdź, że przechodzą**

Run: `dotnet test tests/Mixcloud.Core.Tests --filter SlugTitleTests -v:minimal`
Oczekiwane: PASS, 8 testów.

- [ ] **Krok 5: Commit**

```bash
git add -A && git commit -m "SlugTitle: czytelny tytul ze slugu adresu"
```

---

### Task 5: ProcessRunner — uruchamianie procesów z timeoutem

**Files:**
- Create: `src/Mixcloud.Core/Process/ProcessResult.cs`
- Create: `src/Mixcloud.Core/Process/IProcessRunner.cs`
- Create: `src/Mixcloud.Core/Process/ProcessRunner.cs`
- Create: `tests/Mixcloud.Core.Tests/FakeProcessRunner.cs`
- Test: `tests/Mixcloud.Core.Tests/ProcessRunnerTests.cs`

**Interfaces:**
- Produces:
  - `sealed class ProcessResult` z polami `int ExitCode`, `string StdOut`,
    `string StdErr`, `bool TimedOut`
  - `interface IProcessRunner` z metodą
    `ProcessResult Run(string exePath, string arguments, TimeSpan timeout, CancellationToken ct)`
  - `sealed class ProcessRunner : IProcessRunner` — implementacja realna
  - `sealed class FakeProcessRunner : IProcessRunner` (w projekcie testów)
    z właściwościami `string NextStdOut`, `string NextStdErr`,
    `int NextExitCode`, `bool NextTimedOut` oraz `string LastArguments`

- [ ] **Krok 1: Napisz kontrakty**

`ProcessResult.cs`:

```csharp
namespace Mixcloud.Core.Process
{
    public sealed class ProcessResult
    {
        public int ExitCode { get; set; }
        public string StdOut { get; set; } = string.Empty;
        public string StdErr { get; set; } = string.Empty;
        public bool TimedOut { get; set; }
    }
}
```

`IProcessRunner.cs`:

```csharp
using System;
using System.Threading;

namespace Mixcloud.Core.Process
{
    public interface IProcessRunner
    {
        ProcessResult Run(string exePath, string arguments, TimeSpan timeout, CancellationToken ct);
    }
}
```

- [ ] **Krok 2: Napisz implementację realną**

`ProcessRunner.cs`. Odczyt strumieni idzie asynchronicznie — synchroniczny
`ReadToEnd` na obu strumieniach zakleszcza się, gdy proces zapełni bufor.

```csharp
using System;
using System.Diagnostics;
using System.Text;
using System.Threading;

namespace Mixcloud.Core.Process
{
    public sealed class ProcessRunner : IProcessRunner
    {
        public ProcessResult Run(string exePath, string arguments, TimeSpan timeout, CancellationToken ct)
        {
            var psi = new ProcessStartInfo(exePath, arguments)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            var stdout = new StringBuilder();
            var stderr = new StringBuilder();

            using (var proc = new System.Diagnostics.Process { StartInfo = psi })
            using (var outDone = new ManualResetEventSlim(false))
            using (var errDone = new ManualResetEventSlim(false))
            {
                proc.OutputDataReceived += (s, e) =>
                {
                    if (e.Data == null) outDone.Set(); else stdout.AppendLine(e.Data);
                };
                proc.ErrorDataReceived += (s, e) =>
                {
                    if (e.Data == null) errDone.Set(); else stderr.AppendLine(e.Data);
                };

                proc.Start();
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();

                var deadline = DateTime.UtcNow + timeout;
                while (!proc.HasExited)
                {
                    if (ct.IsCancellationRequested || DateTime.UtcNow > deadline)
                    {
                        try { proc.Kill(); } catch { /* juz zakonczony */ }
                        return new ProcessResult
                        {
                            ExitCode = -1,
                            StdOut = stdout.ToString(),
                            StdErr = stderr.ToString(),
                            TimedOut = true
                        };
                    }
                    Thread.Sleep(50);
                }

                outDone.Wait(TimeSpan.FromSeconds(2));
                errDone.Wait(TimeSpan.FromSeconds(2));

                return new ProcessResult
                {
                    ExitCode = proc.ExitCode,
                    StdOut = stdout.ToString(),
                    StdErr = stderr.ToString(),
                    TimedOut = false
                };
            }
        }
    }
}
```

- [ ] **Krok 3: Napisz atrapę do testów**

`tests/Mixcloud.Core.Tests/FakeProcessRunner.cs`:

```csharp
using System;
using System.Threading;
using Mixcloud.Core.Process;

public sealed class FakeProcessRunner : IProcessRunner
{
    public string NextStdOut { get; set; } = string.Empty;
    public string NextStdErr { get; set; } = string.Empty;
    public int NextExitCode { get; set; }
    public bool NextTimedOut { get; set; }

    public string LastExePath { get; private set; }
    public string LastArguments { get; private set; }
    public TimeSpan LastTimeout { get; private set; }

    public ProcessResult Run(string exePath, string arguments, TimeSpan timeout, CancellationToken ct)
    {
        LastExePath = exePath;
        LastArguments = arguments;
        LastTimeout = timeout;
        return new ProcessResult
        {
            ExitCode = NextExitCode,
            StdOut = NextStdOut,
            StdErr = NextStdErr,
            TimedOut = NextTimedOut
        };
    }
}
```

- [ ] **Krok 4: Napisz testy na prawdziwych procesach**

`ProcessRunner` nie jest samym opakowaniem API — zawiera logikę timeoutu,
zabijania procesu i asynchronicznego odczytu obu strumieni. Zakleszczenie na
buforze strumienia jest później bardzo trudne do zdiagnozowania, więc pokrywamy
to testami. `cmd.exe` jest zawsze obecny w Windows, więc testy są
deterministyczne i nie wymagają sieci.

`tests/Mixcloud.Core.Tests/ProcessRunnerTests.cs`:

```csharp
using System;
using System.Threading;
using Mixcloud.Core.Process;
using Xunit;

public class ProcessRunnerTests
{
    private static readonly string Cmd =
        Environment.ExpandEnvironmentVariables(@"%SystemRoot%\System32\cmd.exe");

    [Fact]
    public void PrzechwytujeStandardoweWyjscie()
    {
        var res = new ProcessRunner().Run(Cmd, "/c echo hello", TimeSpan.FromSeconds(30), CancellationToken.None);

        Assert.False(res.TimedOut);
        Assert.Equal(0, res.ExitCode);
        Assert.Contains("hello", res.StdOut);
    }

    [Fact]
    public void PrzechwytujeStandardowyBlad()
    {
        var res = new ProcessRunner().Run(Cmd, "/c echo problem 1>&2", TimeSpan.FromSeconds(30), CancellationToken.None);

        Assert.Contains("problem", res.StdErr);
    }

    [Fact]
    public void ZwracaNiezerowyKodWyjscia()
    {
        var res = new ProcessRunner().Run(Cmd, "/c exit 3", TimeSpan.FromSeconds(30), CancellationToken.None);

        Assert.False(res.TimedOut);
        Assert.Equal(3, res.ExitCode);
    }

    [Fact]
    public void ZabijaProcesPoPrzekroczeniuTimeoutu()
    {
        var start = DateTime.UtcNow;
        var res = new ProcessRunner().Run(Cmd, "/c ping -n 30 127.0.0.1 > nul",
            TimeSpan.FromSeconds(2), CancellationToken.None);

        Assert.True(res.TimedOut);
        // Musi wrocic po timeoucie, a nie po zakonczeniu 30-sekundowego procesu.
        Assert.True(DateTime.UtcNow - start < TimeSpan.FromSeconds(15));
    }

    [Fact]
    public void AnulowanieKonczyProcesPrzedTimeoutem()
    {
        using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1)))
        {
            var res = new ProcessRunner().Run(Cmd, "/c ping -n 30 127.0.0.1 > nul",
                TimeSpan.FromMinutes(5), cts.Token);

            Assert.True(res.TimedOut);
        }
    }

    [Fact]
    public void DuzeWyjscieNieZakleszczaOdczytu()
    {
        // Synchroniczny ReadToEnd na obu strumieniach zakleszcza sie, gdy proces
        // zapelni bufor. Ten test pilnuje, ze odczyt jest asynchroniczny.
        var res = new ProcessRunner().Run(Cmd,
            "/c for /L %i in (1,1,2000) do @echo wiersz-wypelniajacy-bufor-%i",
            TimeSpan.FromSeconds(60), CancellationToken.None);

        Assert.False(res.TimedOut);
        Assert.Equal(0, res.ExitCode);
        Assert.Contains("wiersz-wypelniajacy-bufor-2000", res.StdOut);
    }
}
```

- [ ] **Krok 5: Uruchom testy i potwierdź, że przechodzą**

Run: `dotnet test tests/Mixcloud.Core.Tests --filter ProcessRunnerTests -v:minimal`
Oczekiwane: PASS, 6 testów. Jeśli `DuzeWyjscieNieZakleszczaOdczytu` zawiesza
się do timeoutu, odczyt strumieni nie jest asynchroniczny — popraw
implementację, nie test.

- [ ] **Krok 6: Commit**

```bash
git add -A && git commit -m "ProcessRunner: uruchamianie procesow z timeoutem i anulowaniem"
```

---

### Task 6: YtDlpService — wywołania yt-dlp

**Files:**
- Create: `src/Mixcloud.Core/YtDlp/YtDlpService.cs`
- Test: `tests/Mixcloud.Core.Tests/YtDlpServiceTests.cs`

**Interfaces:**
- Consumes: `IProcessRunner`, `ProcessResult` (zad. 5); `MixcloudUrl` (zad. 3).
- Produces: `sealed class YtDlpService` z konstruktorem
  `YtDlpService(IProcessRunner runner, string exePath)` i metodami:
  - `IReadOnlyList<string> DumpListing(MixcloudUrl url, int limit, CancellationToken ct)`
  - `string DumpCloudcast(MixcloudUrl url, CancellationToken ct)`
  - `string ResolveDirectUrl(string pageUrl, CancellationToken ct)`
  - `string GetVersion(CancellationToken ct)`
  - stałe publiczne `FormatSelector`, `ListingTimeout`, `ResolveTimeout`
  - `sealed class YtDlpException : Exception` z właściwością `string StdErr`

- [ ] **Krok 1: Napisz testy**

```csharp
using System;
using System.Linq;
using System.Threading;
using Mixcloud.Core.Urls;
using Mixcloud.Core.YtDlp;
using Xunit;

public class YtDlpServiceTests
{
    private static YtDlpService Make(FakeProcessRunner r) => new YtDlpService(r, @"C:\yt\yt-dlp.exe");

    [Fact]
    public void ListaUzywaLeniwegoTrybuZLimitem()
    {
        var r = new FakeProcessRunner { NextStdOut = "{\"a\":1}\n{\"a\":2}\n" };
        var lines = Make(r).DumpListing(
            MixcloudUrl.Parse("https://www.mixcloud.com/spartacus/favorites/"), 50, CancellationToken.None);

        Assert.Equal(2, lines.Count);
        Assert.Contains("--flat-playlist", r.LastArguments);
        Assert.Contains("--dump-json", r.LastArguments);
        Assert.Contains("-I 1:50", r.LastArguments);
        // --dump-single-json zawiesza sie na duzych profilach - nie wolno go tu uzyc.
        Assert.DoesNotContain("--dump-single-json", r.LastArguments);
    }

    [Fact]
    public void PomijaPusteIniepoprawneLinie()
    {
        var r = new FakeProcessRunner { NextStdOut = "{\"a\":1}\n\n   \n{\"a\":2}\n" };
        var lines = Make(r).DumpListing(
            MixcloudUrl.Parse("https://www.mixcloud.com/spartacus/favorites/"), 10, CancellationToken.None);
        Assert.Equal(2, lines.Count);
    }

    [Fact]
    public void PojedynczyMiksUzywaDumpSingleJson()
    {
        var r = new FakeProcessRunner { NextStdOut = "{\"title\":\"x\"}" };
        var json = Make(r).DumpCloudcast(
            MixcloudUrl.Parse("https://www.mixcloud.com/sub88/mental-place-26/"), CancellationToken.None);

        Assert.Equal("{\"title\":\"x\"}", json.Trim());
        Assert.Contains("--dump-single-json", r.LastArguments);
    }

    [Fact]
    public void RozwiazywanieAdresuUzywaWlasciwegoSelektoraFormatu()
    {
        var r = new FakeProcessRunner { NextStdOut = "https://dl.mixcloud.stream/x.m4a?sig=abc\n" };
        var direct = Make(r).ResolveDirectUrl(
            "https://www.mixcloud.com/sub88/mental-place-26/", CancellationToken.None);

        Assert.Equal("https://dl.mixcloud.stream/x.m4a?sig=abc", direct);
        Assert.Contains("-f \"http/hls-192/bestaudio\"", r.LastArguments);
        Assert.Contains("-g", r.LastArguments);
    }

    [Fact]
    public void RozwiazywanieBierzePierwszyAdresGdyYtDlpZwrocaKilka()
    {
        var r = new FakeProcessRunner { NextStdOut = "https://a/1.m4a\nhttps://a/2.m4a\n" };
        Assert.Equal("https://a/1.m4a", Make(r).ResolveDirectUrl("https://www.mixcloud.com/a/b/", CancellationToken.None));
    }

    [Fact]
    public void TimeoutJestBledem()
    {
        var r = new FakeProcessRunner { NextTimedOut = true, NextExitCode = -1 };
        var ex = Assert.Throws<YtDlpException>(() => Make(r).DumpListing(
            MixcloudUrl.Parse("https://www.mixcloud.com/spartacus/favorites/"), 10, CancellationToken.None));
        Assert.Contains("timeout", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NiezerowyKodWyjsciaJestBledemZeStandardowymBledem()
    {
        var r = new FakeProcessRunner { NextExitCode = 1, NextStdErr = "ERROR: nie znaleziono" };
        var ex = Assert.Throws<YtDlpException>(() => Make(r).DumpCloudcast(
            MixcloudUrl.Parse("https://www.mixcloud.com/a/b/"), CancellationToken.None));
        Assert.Contains("nie znaleziono", ex.StdErr);
    }

    [Fact]
    public void NiepoprawnyAdresJestOdrzucanyPrzedUruchomieniemProcesu()
    {
        var r = new FakeProcessRunner();
        Assert.Throws<ArgumentException>(() => Make(r).DumpListing(
            MixcloudUrl.Parse("https://youtube.com/x"), 10, CancellationToken.None));
        Assert.Null(r.LastArguments);
    }

    [Fact]
    public void KazdeWywolanieMaJawnyTimeout()
    {
        var r = new FakeProcessRunner { NextStdOut = "{}" };
        Make(r).DumpListing(MixcloudUrl.Parse("https://www.mixcloud.com/a/favorites/"), 5, CancellationToken.None);
        Assert.True(r.LastTimeout > TimeSpan.Zero);
    }
}
```

- [ ] **Krok 2: Uruchom testy i potwierdź, że padają**

Run: `dotnet test tests/Mixcloud.Core.Tests --filter YtDlpServiceTests -v:minimal`
Oczekiwane: FAIL — CS0246, `YtDlpService` nie istnieje.

- [ ] **Krok 3: Napisz implementację**

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using Mixcloud.Core.Process;
using Mixcloud.Core.Urls;

namespace Mixcloud.Core.YtDlp
{
    public sealed class YtDlpException : Exception
    {
        public string StdErr { get; }
        public YtDlpException(string message, string stdErr) : base(message)
        {
            StdErr = stdErr ?? string.Empty;
        }
    }

    public sealed class YtDlpService
    {
        // Progresywny m4a jest przewijalny i gra natywnie przez bass_aac.
        public const string FormatSelector = "http/hls-192/bestaudio";

        public static readonly TimeSpan ListingTimeout = TimeSpan.FromSeconds(120);
        public static readonly TimeSpan ResolveTimeout = TimeSpan.FromSeconds(60);
        public static readonly TimeSpan VersionTimeout = TimeSpan.FromSeconds(20);

        private readonly IProcessRunner _runner;
        private readonly string _exePath;

        public YtDlpService(IProcessRunner runner, string exePath)
        {
            _runner = runner ?? throw new ArgumentNullException(nameof(runner));
            _exePath = exePath ?? throw new ArgumentNullException(nameof(exePath));
        }

        public IReadOnlyList<string> DumpListing(MixcloudUrl url, int limit, CancellationToken ct)
        {
            Require(url, MixcloudUrlKind.Listing);
            if (limit < 1) throw new ArgumentOutOfRangeException(nameof(limit));

            // Twardy limit jest obowiazkowy: bez niego yt-dlp stronicuje bez konca
            // na duzych profilach (zaobserwowane na /NTSRadio/uploads/).
            var args = string.Format(CultureInfo.InvariantCulture,
                "--flat-playlist --dump-json -I 1:{0} --no-warnings \"{1}\"",
                limit, url.Normalized);

            var res = Execute(args, ListingTimeout, ct);
            return res.StdOut
                .Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Where(l => l.StartsWith("{", StringComparison.Ordinal))
                .ToList();
        }

        public string DumpCloudcast(MixcloudUrl url, CancellationToken ct)
        {
            Require(url, MixcloudUrlKind.Cloudcast);
            var args = "--dump-single-json --no-warnings \"" + url.Normalized + "\"";
            return Execute(args, ListingTimeout, ct).StdOut;
        }

        public string ResolveDirectUrl(string pageUrl, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(pageUrl)) throw new ArgumentException("Pusty adres", nameof(pageUrl));

            var args = "-g -f \"" + FormatSelector + "\" --no-warnings \"" + pageUrl + "\"";
            var res = Execute(args, ResolveTimeout, ct);

            return res.StdOut
                .Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .FirstOrDefault(l => l.StartsWith("http", StringComparison.OrdinalIgnoreCase));
        }

        public string GetVersion(CancellationToken ct)
        {
            return Execute("--version", VersionTimeout, ct).StdOut.Trim();
        }

        private static void Require(MixcloudUrl url, MixcloudUrlKind expected)
        {
            if (url == null || url.Kind != expected)
                throw new ArgumentException("Adres nie jest typu " + expected, nameof(url));
        }

        private ProcessResult Execute(string args, TimeSpan timeout, CancellationToken ct)
        {
            var res = _runner.Run(_exePath, args, timeout, ct);

            if (res.TimedOut)
                throw new YtDlpException("yt-dlp: timeout po " + timeout, res.StdErr);
            if (res.ExitCode != 0)
                throw new YtDlpException("yt-dlp: kod wyjscia " + res.ExitCode, res.StdErr);

            return res;
        }
    }
}
```

- [ ] **Krok 4: Uruchom testy i potwierdź, że przechodzą**

Run: `dotnet test tests/Mixcloud.Core.Tests --filter YtDlpServiceTests -v:minimal`
Oczekiwane: PASS, 9 testów.

- [ ] **Krok 5: Commit**

```bash
git add -A && git commit -m "YtDlpService: wywolania listujace, pojedyncze i rozwiazywanie adresu"
```

---

### Task 7: MixcloudCatalog — parsowanie odpowiedzi yt-dlp

Testy działają na **prawdziwych** odpowiedziach Mixclouda zapisanych
w `tests/fixtures/`, nie na wymyślonym JSON-ie.

**Files:**
- Create: `src/Mixcloud.Core/Catalog/MixcloudTrack.cs`
- Create: `src/Mixcloud.Core/Catalog/MixcloudListing.cs`
- Create: `src/Mixcloud.Core/Catalog/MixcloudCatalog.cs`
- Test: `tests/Mixcloud.Core.Tests/MixcloudCatalogTests.cs`

**Interfaces:**
- Consumes: `MixcloudUrl`, `SlugTitle` (zad. 3, 4).
- Produces:
  - `sealed class MixcloudTrack` — właściwości `string PageUrl`,
    `string Title`, `string Artist`, `double DurationSeconds`,
    `string ThumbnailUrl`
  - `sealed class MixcloudListing` — `string Name`,
    `IReadOnlyList<MixcloudTrack> Tracks`
  - `static class MixcloudCatalog` z metodami
    `MixcloudListing ParseFlatListing(IEnumerable<string> jsonLines)` oraz
    `MixcloudTrack ParseCloudcast(string json)`

- [ ] **Krok 1: Napisz testy**

```csharp
using System;
using System.IO;
using System.Linq;
using Mixcloud.Core.Catalog;
using Xunit;

public class MixcloudCatalogTests
{
    private static string Fixture(string name) =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fixtures", name);

    [Fact]
    public void ParsujeListeUlubionychZPrawdziwejOdpowiedzi()
    {
        var lines = File.ReadAllLines(Fixture("favorites-flat.jsonl"));
        var listing = MixcloudCatalog.ParseFlatListing(lines);

        Assert.Equal("Spartacus (favorites)", listing.Name);
        Assert.Equal(2, listing.Tracks.Count);

        var first = listing.Tracks[0];
        Assert.Equal("https://www.mixcloud.com/sub88/mental-place-26/", first.PageUrl);
        Assert.Equal("Mental Place 26", first.Title);
        Assert.Equal("sub88", first.Artist);
        Assert.Equal(0d, first.DurationSeconds);
    }

    [Fact]
    public void WykonawcaPochodziZeSciezkiAdresuNieZPrefiksuId()
    {
        // Handle uzytkownika moze zawierac podkreslenie, wiec dzielenie id
        // po pierwszym '_' bylo by bledne. Zrodlem prawdy jest sciezka adresu.
        var line = "{\"_type\":\"url\",\"id\":\"a_b_c\"," +
                   "\"url\":\"https://www.mixcloud.com/a_b/c/\"," +
                   "\"playlist_title\":\"X\"}";
        var listing = MixcloudCatalog.ParseFlatListing(new[] { line });
        Assert.Equal("a_b", listing.Tracks[0].Artist);
        Assert.Equal("C", listing.Tracks[0].Title);
    }

    [Fact]
    public void ParsujePojedynczyMiksZPrawdziwejOdpowiedzi()
    {
        var track = MixcloudCatalog.ParseCloudcast(File.ReadAllText(Fixture("cloudcast-single.json")));

        Assert.Equal("Loraine James - 1st September 2026", track.Title);
        Assert.Equal("NTSRadio", track.Artist);
        Assert.Equal(3949d, track.DurationSeconds);
        Assert.Equal("https://www.mixcloud.com/NTSRadio/loraine-james-1st-september-2026/", track.PageUrl);
        Assert.StartsWith("https://thumbnailer.mixcloud.com/", track.ThumbnailUrl);
    }

    [Fact]
    public void PustaListaDajePustyWynikBezWyjatku()
    {
        var listing = MixcloudCatalog.ParseFlatListing(Enumerable.Empty<string>());
        Assert.Empty(listing.Tracks);
        Assert.Equal(string.Empty, listing.Name);
    }

    [Fact]
    public void PomijaUszkodzoneLinieZamiastPrzerywacCaleParsowanie()
    {
        var good = "{\"_type\":\"url\",\"url\":\"https://www.mixcloud.com/a/b/\",\"playlist_title\":\"X\"}";
        var listing = MixcloudCatalog.ParseFlatListing(new[] { "to nie json", good, "{ nadgryziony" });
        Assert.Single(listing.Tracks);
        Assert.Equal("X", listing.Name);
    }

    [Fact]
    public void PomijaPozycjeBezAdresu()
    {
        var listing = MixcloudCatalog.ParseFlatListing(new[] { "{\"_type\":\"url\",\"playlist_title\":\"X\"}" });
        Assert.Empty(listing.Tracks);
    }
}
```

- [ ] **Krok 2: Uruchom testy i potwierdź, że padają**

Run: `dotnet test tests/Mixcloud.Core.Tests --filter MixcloudCatalogTests -v:minimal`
Oczekiwane: FAIL — CS0246, `MixcloudCatalog` nie istnieje.

- [ ] **Krok 3: Napisz modele**

`MixcloudTrack.cs`:

```csharp
namespace Mixcloud.Core.Catalog
{
    public sealed class MixcloudTrack
    {
        public string PageUrl { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public double DurationSeconds { get; set; }
        public string ThumbnailUrl { get; set; } = string.Empty;
    }
}
```

`MixcloudListing.cs`:

```csharp
using System.Collections.Generic;

namespace Mixcloud.Core.Catalog
{
    public sealed class MixcloudListing
    {
        public string Name { get; set; } = string.Empty;
        public IReadOnlyList<MixcloudTrack> Tracks { get; set; } = new List<MixcloudTrack>();
    }
}
```

- [ ] **Krok 4: Napisz parser**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Mixcloud.Core.Urls;
using Newtonsoft.Json.Linq;

namespace Mixcloud.Core.Catalog
{
    public static class MixcloudCatalog
    {
        public static MixcloudListing ParseFlatListing(IEnumerable<string> jsonLines)
        {
            var tracks = new List<MixcloudTrack>();
            var name = string.Empty;

            foreach (var line in jsonLines ?? Enumerable.Empty<string>())
            {
                JObject o;
                // Uszkodzona linia nie moze przerwac calej listy.
                try { o = JObject.Parse(line); } catch (Exception) { continue; }

                if (name.Length == 0)
                    name = (string)o["playlist_title"] ?? string.Empty;

                var url = (string)o["url"] ?? (string)o["webpage_url"];
                if (string.IsNullOrWhiteSpace(url)) continue;

                var parsed = MixcloudUrl.Parse(url);
                if (parsed.Kind != MixcloudUrlKind.Cloudcast) continue;

                tracks.Add(new MixcloudTrack
                {
                    PageUrl = parsed.Normalized,
                    // Tryb flat nie zwraca tytulow - wyprowadzamy je ze slugu,
                    // a AIMP uzupelni prawdziwe leniwie przez FileInfoProvider.
                    Title = SlugTitle.FromSlug(parsed.CloudcastSlug),
                    Artist = parsed.UserSlug,
                    DurationSeconds = 0d,
                    ThumbnailUrl = string.Empty
                });
            }

            return new MixcloudListing { Name = name, Tracks = tracks };
        }

        public static MixcloudTrack ParseCloudcast(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new ArgumentException("Pusta odpowiedz", nameof(json));

            var o = JObject.Parse(json);
            var pageUrl = (string)o["webpage_url"] ?? string.Empty;
            var parsed = MixcloudUrl.Parse(pageUrl);

            return new MixcloudTrack
            {
                PageUrl = parsed.Kind == MixcloudUrlKind.Cloudcast ? parsed.Normalized : pageUrl,
                Title = (string)o["title"] ?? string.Empty,
                Artist = (string)o["uploader_id"] ?? (string)o["uploader"] ?? string.Empty,
                DurationSeconds = (double?)o["duration"] ?? 0d,
                ThumbnailUrl = (string)o["thumbnail"] ?? string.Empty
            };
        }
    }
}
```

- [ ] **Krok 5: Uruchom testy i potwierdź, że przechodzą**

Run: `dotnet test tests/Mixcloud.Core.Tests --filter MixcloudCatalogTests -v:minimal`
Oczekiwane: PASS, 6 testów.

- [ ] **Krok 6: Commit**

```bash
git add -A && git commit -m "MixcloudCatalog: parsowanie odpowiedzi yt-dlp na fixture'ach"
```

---

### Task 8: MixcloudSettings — ustawienia wtyczki

**Files:**
- Create: `src/Mixcloud.Core/Settings/MixcloudSettings.cs`
- Test: `tests/Mixcloud.Core.Tests/MixcloudSettingsTests.cs`

**Interfaces:**
- Produces: `sealed class MixcloudSettings` z właściwościami
  `string Handle`, `int ListingLimit`, `bool AutoUpdateYtDlp`,
  `long CacheLimitBytes`, `DateTime LastUpdateCheckUtc`,
  `string LastKnownYtDlpTag`; oraz metodami statycznymi
  `MixcloudSettings Load(string path)` i `void Save(string path)`.
  Stała `int DefaultListingLimit = 200`,
  `long DefaultCacheLimitBytes = 5L * 1024 * 1024 * 1024`.

- [ ] **Krok 1: Napisz testy**

```csharp
using System;
using System.IO;
using Mixcloud.Core.Settings;
using Xunit;

public class MixcloudSettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mcset-" + Guid.NewGuid().ToString("N"));
    private string Path_ => Path.Combine(_dir, "settings.json");

    public MixcloudSettingsTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void BrakPlikuDajeWartosciDomyslne()
    {
        var s = MixcloudSettings.Load(Path_);
        Assert.Equal(string.Empty, s.Handle);
        Assert.Equal(MixcloudSettings.DefaultListingLimit, s.ListingLimit);
        Assert.True(s.AutoUpdateYtDlp);
        Assert.Equal(MixcloudSettings.DefaultCacheLimitBytes, s.CacheLimitBytes);
    }

    [Fact]
    public void ZapisIOdczytZachowujaWartosci()
    {
        var s = MixcloudSettings.Load(Path_);
        s.Handle = "spartacus";
        s.ListingLimit = 42;
        s.AutoUpdateYtDlp = false;
        s.LastKnownYtDlpTag = "2026.06.09";
        s.Save(Path_);

        var back = MixcloudSettings.Load(Path_);
        Assert.Equal("spartacus", back.Handle);
        Assert.Equal(42, back.ListingLimit);
        Assert.False(back.AutoUpdateYtDlp);
        Assert.Equal("2026.06.09", back.LastKnownYtDlpTag);
    }

    [Fact]
    public void UszkodzonyPlikDajeWartosciDomyslneZamiastWyjatku()
    {
        File.WriteAllText(Path_, "{ to nie jest json");
        var s = MixcloudSettings.Load(Path_);
        Assert.Equal(MixcloudSettings.DefaultListingLimit, s.ListingLimit);
    }

    [Fact]
    public void ZapisTworzyBrakujacyKatalog()
    {
        var nested = Path.Combine(_dir, "a", "b", "settings.json");
        new MixcloudSettings { Handle = "x" }.Save(nested);
        Assert.True(File.Exists(nested));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void NiedodatniLimitJestKorygowanyDoDomyslnego(int zly)
    {
        var s = new MixcloudSettings { ListingLimit = zly };
        s.Save(Path_);
        Assert.Equal(MixcloudSettings.DefaultListingLimit, MixcloudSettings.Load(Path_).ListingLimit);
    }
}
```

- [ ] **Krok 2: Uruchom testy i potwierdź, że padają**

Run: `dotnet test tests/Mixcloud.Core.Tests --filter MixcloudSettingsTests -v:minimal`
Oczekiwane: FAIL — CS0246, `MixcloudSettings` nie istnieje.

- [ ] **Krok 3: Napisz implementację**

```csharp
using System;
using System.IO;
using Newtonsoft.Json;

namespace Mixcloud.Core.Settings
{
    public sealed class MixcloudSettings
    {
        public const int DefaultListingLimit = 200;
        public const long DefaultCacheLimitBytes = 5L * 1024 * 1024 * 1024;

        public string Handle { get; set; } = string.Empty;
        public int ListingLimit { get; set; } = DefaultListingLimit;
        public bool AutoUpdateYtDlp { get; set; } = true;
        public long CacheLimitBytes { get; set; } = DefaultCacheLimitBytes;
        public DateTime LastUpdateCheckUtc { get; set; } = DateTime.MinValue;
        public string LastKnownYtDlpTag { get; set; } = string.Empty;

        public static MixcloudSettings Load(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    var loaded = JsonConvert.DeserializeObject<MixcloudSettings>(File.ReadAllText(path));
                    if (loaded != null) return loaded.Normalized();
                }
            }
            catch (Exception)
            {
                // Uszkodzone ustawienia nie moga blokowac startu wtyczki.
            }
            return new MixcloudSettings();
        }

        public void Save(string path)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, JsonConvert.SerializeObject(Normalized(), Formatting.Indented));
        }

        private MixcloudSettings Normalized()
        {
            if (ListingLimit < 1) ListingLimit = DefaultListingLimit;
            if (CacheLimitBytes < 1) CacheLimitBytes = DefaultCacheLimitBytes;
            if (Handle == null) Handle = string.Empty;
            if (LastKnownYtDlpTag == null) LastKnownYtDlpTag = string.Empty;
            return this;
        }
    }
}
```

- [ ] **Krok 4: Uruchom testy i potwierdź, że przechodzą**

Run: `dotnet test tests/Mixcloud.Core.Tests --filter MixcloudSettingsTests -v:minimal`
Oczekiwane: PASS, 6 testów.

- [ ] **Krok 5: Commit**

```bash
git add -A && git commit -m "MixcloudSettings: trwale ustawienia z odporna deserializacja"
```

---

### Task 9: YtDlpInstaller — pobranie i aktualizacja binarki

**Files:**
- Create: `src/Mixcloud.Core/YtDlp/YtDlpInstaller.cs`
- Test: `tests/Mixcloud.Core.Tests/YtDlpInstallerTests.cs`

**Interfaces:**
- Consumes: `MixcloudSettings` (zad. 8).
- Produces:
  - `interface IHttpDownloader` z metodami
    `string GetString(string url, CancellationToken ct)` i
    `void DownloadFile(string url, string destPath, CancellationToken ct)`
  - `sealed class HttpDownloader : IHttpDownloader`
  - `sealed class YtDlpInstaller` z konstruktorem
    `YtDlpInstaller(IHttpDownloader http, string dataDir)` i metodami
    `string EnsureInstalled(CancellationToken ct)` (zwraca ścieżkę do exe),
    `void ApplyPendingUpdate()`,
    `bool CheckForUpdate(MixcloudSettings settings, CancellationToken ct)`

- [ ] **Krok 1: Napisz testy**

```csharp
using System;
using System.IO;
using System.Threading;
using Mixcloud.Core.Settings;
using Mixcloud.Core.YtDlp;
using Xunit;

public sealed class FakeDownloader : IHttpDownloader
{
    public string NextString { get; set; } = "{}";
    public string FileContent { get; set; } = "UDAWANY-EXE";
    public int DownloadCount { get; private set; }
    public string LastFileUrl { get; private set; }
    public Exception ThrowOnDownload { get; set; }

    public string GetString(string url, CancellationToken ct) => NextString;

    public void DownloadFile(string url, string destPath, CancellationToken ct)
    {
        if (ThrowOnDownload != null) throw ThrowOnDownload;
        LastFileUrl = url;
        DownloadCount++;
        File.WriteAllText(destPath, FileContent);
    }
}

public class YtDlpInstallerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mcinst-" + Guid.NewGuid().ToString("N"));
    public YtDlpInstallerTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private string Exe => Path.Combine(_dir, "yt-dlp.exe");

    [Fact]
    public void PierwszeUruchomieniePobieraBinarke()
    {
        var http = new FakeDownloader();
        var path = new YtDlpInstaller(http, _dir).EnsureInstalled(CancellationToken.None);

        Assert.Equal(Exe, path);
        Assert.True(File.Exists(Exe));
        Assert.Equal(1, http.DownloadCount);
    }

    [Fact]
    public void IstniejacaBinarkaNieJestPobieranaPonownie()
    {
        File.WriteAllText(Exe, "juz-jest");
        var http = new FakeDownloader();
        new YtDlpInstaller(http, _dir).EnsureInstalled(CancellationToken.None);
        Assert.Equal(0, http.DownloadCount);
    }

    [Fact]
    public void NowaWersjaLadujeObokJakoOczekujacaAktualizacja()
    {
        File.WriteAllText(Exe, "stara");
        var http = new FakeDownloader
        {
            NextString = "{\"tag_name\":\"2026.09.01\"}",
            FileContent = "nowa"
        };
        var settings = new MixcloudSettings { LastKnownYtDlpTag = "2026.06.09" };

        var updated = new YtDlpInstaller(http, _dir).CheckForUpdate(settings, CancellationToken.None);

        Assert.True(updated);
        // Dzialajaca binarka nie moze zostac podmieniona w locie.
        Assert.Equal("stara", File.ReadAllText(Exe));
        Assert.True(File.Exists(Exe + ".new"));
        Assert.Equal("2026.09.01", settings.LastKnownYtDlpTag);
    }

    [Fact]
    public void TaSamaWersjaNiePobieraNiczego()
    {
        File.WriteAllText(Exe, "stara");
        var http = new FakeDownloader { NextString = "{\"tag_name\":\"2026.06.09\"}" };
        var settings = new MixcloudSettings { LastKnownYtDlpTag = "2026.06.09" };

        Assert.False(new YtDlpInstaller(http, _dir).CheckForUpdate(settings, CancellationToken.None));
        Assert.Equal(0, http.DownloadCount);
    }

    [Fact]
    public void OczekujacaAktualizacjaJestStosowanaPrzyStarcie()
    {
        File.WriteAllText(Exe, "stara");
        File.WriteAllText(Exe + ".new", "nowa");

        new YtDlpInstaller(new FakeDownloader(), _dir).ApplyPendingUpdate();

        Assert.Equal("nowa", File.ReadAllText(Exe));
        Assert.False(File.Exists(Exe + ".new"));
    }

    [Fact]
    public void BladSieciPrzySprawdzaniuNiePrzerywaDzialania()
    {
        File.WriteAllText(Exe, "stara");
        var http = new FakeDownloader
        {
            NextString = "{\"tag_name\":\"2026.09.01\"}",
            ThrowOnDownload = new InvalidOperationException("brak sieci")
        };
        var settings = new MixcloudSettings { LastKnownYtDlpTag = "2026.06.09" };

        // Awaria aktualizacji nie moze psuc odtwarzania.
        Assert.False(new YtDlpInstaller(http, _dir).CheckForUpdate(settings, CancellationToken.None));
        Assert.Equal("stara", File.ReadAllText(Exe));
    }
}
```

- [ ] **Krok 2: Uruchom testy i potwierdź, że padają**

Run: `dotnet test tests/Mixcloud.Core.Tests --filter YtDlpInstallerTests -v:minimal`
Oczekiwane: FAIL — CS0246, `YtDlpInstaller` nie istnieje.

- [ ] **Krok 3: Napisz implementację**

```csharp
using System;
using System.IO;
using System.Net;
using System.Threading;
using Mixcloud.Core.Settings;
using Newtonsoft.Json.Linq;

namespace Mixcloud.Core.YtDlp
{
    public interface IHttpDownloader
    {
        string GetString(string url, CancellationToken ct);
        void DownloadFile(string url, string destPath, CancellationToken ct);
    }

    public sealed class HttpDownloader : IHttpDownloader
    {
        private const string UserAgent = "AIMP-Mixcloud-Plugin";

        static HttpDownloader()
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
        }

        private static WebClient Create()
        {
            var wc = new WebClient();
            wc.Headers.Add("User-Agent", UserAgent);
            return wc;
        }

        public string GetString(string url, CancellationToken ct)
        {
            using (var wc = Create()) return wc.DownloadString(url);
        }

        public void DownloadFile(string url, string destPath, CancellationToken ct)
        {
            using (var wc = Create()) wc.DownloadFile(url, destPath);
        }
    }

    public sealed class YtDlpInstaller
    {
        private const string LatestReleaseApi =
            "https://api.github.com/repos/yt-dlp/yt-dlp/releases/latest";
        private const string LatestExeUrl =
            "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";

        private readonly IHttpDownloader _http;
        private readonly string _dataDir;

        public YtDlpInstaller(IHttpDownloader http, string dataDir)
        {
            _http = http ?? throw new ArgumentNullException(nameof(http));
            _dataDir = dataDir ?? throw new ArgumentNullException(nameof(dataDir));
        }

        public string ExePath => Path.Combine(_dataDir, "yt-dlp.exe");
        private string PendingPath => ExePath + ".new";

        public string EnsureInstalled(CancellationToken ct)
        {
            Directory.CreateDirectory(_dataDir);
            if (!File.Exists(ExePath))
                _http.DownloadFile(LatestExeUrl, ExePath, ct);
            return ExePath;
        }

        public void ApplyPendingUpdate()
        {
            if (!File.Exists(PendingPath)) return;
            try
            {
                if (File.Exists(ExePath)) File.Delete(ExePath);
                File.Move(PendingPath, ExePath);
            }
            catch (IOException)
            {
                // Binarka wciaz zablokowana - sprobujemy przy nastepnym starcie.
            }
        }

        public bool CheckForUpdate(MixcloudSettings settings, CancellationToken ct)
        {
            try
            {
                var tag = (string)JObject.Parse(_http.GetString(LatestReleaseApi, ct))["tag_name"];
                settings.LastUpdateCheckUtc = DateTime.UtcNow;

                if (string.IsNullOrWhiteSpace(tag)) return false;
                if (string.Equals(tag, settings.LastKnownYtDlpTag, StringComparison.Ordinal)) return false;

                Directory.CreateDirectory(_dataDir);
                // Pobieramy obok. Podmiana nastapi dopiero przy nastepnym starcie.
                _http.DownloadFile(LatestExeUrl, PendingPath, ct);
                settings.LastKnownYtDlpTag = tag;
                return true;
            }
            catch (Exception)
            {
                // Brak sieci to cichy no-op: gramy dalej na dotychczasowej wersji.
                return false;
            }
        }
    }
}
```

- [ ] **Krok 4: Uruchom testy i potwierdź, że przechodzą**

Run: `dotnet test tests/Mixcloud.Core.Tests --filter YtDlpInstallerTests -v:minimal`
Oczekiwane: PASS, 6 testów.

- [ ] **Krok 5: Commit**

```bash
git add -A && git commit -m "YtDlpInstaller: pobranie binarki i atomowa aktualizacja przy starcie"
```

---

### Task 10: Lokalizacja — klucze, pliki .lng, test spójności

**Files:**
- Create: `src/Mixcloud.Core/Localization/IStringProvider.cs`
- Create: `src/Mixcloud.Core/Localization/StringKeys.cs`
- Create: `src/Mixcloud.Plugin/Localization/MuiStringProvider.cs`
- Create: `src/Mixcloud.Plugin/Langs/english.lng`
- Create: `src/Mixcloud.Plugin/Langs/polish.lng`
- Test: `tests/Mixcloud.Core.Tests/LanguageFileTests.cs`

**Interfaces:**
- Produces:
  - `interface IStringProvider` z metodą `string Get(string key)`
  - `static class StringKeys` — stałe z pełnymi kluczami MUI
  - `sealed class MuiStringProvider : IStringProvider` — konstruktor
    `MuiStringProvider(IAimpServiceMUI mui)`

- [ ] **Krok 1: Napisz kontrakt i klucze**

`IStringProvider.cs`:

```csharp
namespace Mixcloud.Core.Localization
{
    public interface IStringProvider
    {
        string Get(string key);
    }
}
```

`StringKeys.cs`. Format klucza MUI to `Sekcja\Klucz` — tak jak w plikach
`.lng` wtyczki `aimp_YouTube`, którą mamy pod ręką jako wzorzec.

```csharp
namespace Mixcloud.Core.Localization
{
    public static class StringKeys
    {
        public const string MenuOpenUrl       = @"Mixcloud.Menu\OpenUrl";
        public const string MenuLoadFavorites = @"Mixcloud.Menu\LoadFavorites";

        public const string DialogOpenUrlTitle  = @"Mixcloud.OpenUrl\Title";
        public const string DialogOpenUrlPrompt = @"Mixcloud.OpenUrl\Prompt";
        public const string DialogOk            = @"Mixcloud.OpenUrl\Ok";
        public const string DialogCancel        = @"Mixcloud.OpenUrl\Cancel";

        public const string MsgError            = @"Mixcloud.Messages\Error";
        public const string MsgInvalidUrl       = @"Mixcloud.Messages\InvalidUrl";
        public const string MsgNoHandle         = @"Mixcloud.Messages\NoHandle";
        public const string MsgEmptyResult      = @"Mixcloud.Messages\EmptyResult";
        public const string MsgYtDlpMissing     = @"Mixcloud.Messages\YtDlpMissing";
        public const string MsgYtDlpFailed      = @"Mixcloud.Messages\YtDlpFailed";
        public const string MsgLoading          = @"Mixcloud.Messages\Loading";
        public const string MsgNoDiskSpace      = @"Mixcloud.Messages\NoDiskSpace";

        public const string OptHandle           = @"Mixcloud.Options\Handle";
        public const string OptListingLimit     = @"Mixcloud.Options\ListingLimit";
        public const string OptAutoUpdate       = @"Mixcloud.Options\AutoUpdate";
        public const string OptCheckNow         = @"Mixcloud.Options\CheckNow";
        public const string OptYtDlpVersion     = @"Mixcloud.Options\YtDlpVersion";
        public const string OptCacheLimit       = @"Mixcloud.Options\CacheLimit";
    }
}
```

- [ ] **Krok 2: Napisz pliki językowe**

`src/Mixcloud.Plugin/Langs/english.lng` — zapisz w UTF-8:

```ini
[FILE]
Author=Marek Zettel
Name=English (EN)
VersionID=0
LangId=1033

[Mixcloud.Menu]
OpenUrl=Mixcloud URL...
LoadFavorites=Mixcloud: load my favorites

[Mixcloud.OpenUrl]
Title=Open Mixcloud URL
Prompt=Paste a Mixcloud address (a mix, a profile or /favorites/):
Ok=Open
Cancel=Cancel

[Mixcloud.Messages]
Error=Error
InvalidUrl=This is not a Mixcloud address.
NoHandle=Set your Mixcloud username in the plugin options first.
EmptyResult=Nothing was found at this address. The profile may be private or empty.
YtDlpMissing=yt-dlp is missing. Download it now?
YtDlpFailed=yt-dlp could not read this address.
Loading=Loading from Mixcloud...
NoDiskSpace=Not enough free disk space to download this mix.

[Mixcloud.Options]
Handle=Mixcloud username
ListingLimit=Maximum items per listing
AutoUpdate=Update yt-dlp automatically
CheckNow=Check for updates now
YtDlpVersion=yt-dlp version
CacheLimit=Temporary files limit (GB)
```

`src/Mixcloud.Plugin/Langs/polish.lng` — zapisz w UTF-8:

```ini
[FILE]
Author=Marek Zettel
Name=Polish (PL)
VersionID=0
LangId=1045

[Mixcloud.Menu]
OpenUrl=Adres Mixcloud...
LoadFavorites=Mixcloud: wczytaj moje ulubione

[Mixcloud.OpenUrl]
Title=Otwórz adres Mixcloud
Prompt=Wklej adres Mixclouda (miks, profil albo /favorites/):
Ok=Otwórz
Cancel=Anuluj

[Mixcloud.Messages]
Error=Błąd
InvalidUrl=To nie jest adres Mixclouda.
NoHandle=Najpierw podaj swoją nazwę użytkownika Mixcloud w ustawieniach wtyczki.
EmptyResult=Pod tym adresem nic nie znaleziono. Profil może być prywatny lub pusty.
YtDlpMissing=Brakuje yt-dlp. Pobrać teraz?
YtDlpFailed=yt-dlp nie zdołał odczytać tego adresu.
Loading=Wczytywanie z Mixclouda...
NoDiskSpace=Za mało wolnego miejsca na dysku, żeby pobrać ten miks.

[Mixcloud.Options]
Handle=Nazwa użytkownika Mixcloud
ListingLimit=Maksymalna liczba pozycji na liście
AutoUpdate=Aktualizuj yt-dlp automatycznie
CheckNow=Sprawdź aktualizacje teraz
YtDlpVersion=Wersja yt-dlp
CacheLimit=Limit plików tymczasowych (GB)
```

- [ ] **Krok 3: Napisz test spójności**

Bez tego testu tłumaczenia zgniją po cichu przy pierwszej większej zmianie.

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Mixcloud.Core.Localization;
using Xunit;

public class LanguageFileTests
{
    private static string LangDir()
    {
        var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Mixcloud.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir.FullName, "src", "Mixcloud.Plugin", "Langs");
    }

    private static Dictionary<string, string> ReadLng(string path)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var section = string.Empty;
        foreach (var raw in File.ReadAllLines(path, Encoding.UTF8))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(";", StringComparison.Ordinal)) continue;
            if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
            {
                section = line.Substring(1, line.Length - 2);
                continue;
            }
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            result[section + "\\" + line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
        }
        return result;
    }

    [Fact]
    public void ObaPlikiMajaIdentyczneZbioryKluczy()
    {
        var pl = ReadLng(Path.Combine(LangDir(), "polish.lng"));
        var en = ReadLng(Path.Combine(LangDir(), "english.lng"));

        var brakujeWPl = en.Keys.Except(pl.Keys).OrderBy(k => k).ToList();
        var brakujeWEn = pl.Keys.Except(en.Keys).OrderBy(k => k).ToList();

        Assert.True(brakujeWPl.Count == 0, "Brak w polish.lng: " + string.Join(", ", brakujeWPl));
        Assert.True(brakujeWEn.Count == 0, "Brak w english.lng: " + string.Join(", ", brakujeWEn));
    }

    [Fact]
    public void KazdaStalaZStringKeysMaOdpowiednikWObuPlikach()
    {
        var pl = ReadLng(Path.Combine(LangDir(), "polish.lng"));
        var en = ReadLng(Path.Combine(LangDir(), "english.lng"));

        var klucze = typeof(StringKeys)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue())
            .ToList();

        Assert.NotEmpty(klucze);
        foreach (var k in klucze)
        {
            Assert.True(en.ContainsKey(k), "english.lng nie ma klucza " + k);
            Assert.True(pl.ContainsKey(k), "polish.lng nie ma klucza " + k);
        }
    }

    [Fact]
    public void ZadenNapisNieJestPusty()
    {
        foreach (var plik in new[] { "polish.lng", "english.lng" })
            foreach (var para in ReadLng(Path.Combine(LangDir(), plik)))
                Assert.False(string.IsNullOrWhiteSpace(para.Value), plik + ": pusty napis dla " + para.Key);
    }
}
```

- [ ] **Krok 4: Uruchom testy i potwierdź, że przechodzą**

Run: `dotnet test tests/Mixcloud.Core.Tests --filter LanguageFileTests -v:minimal`
Oczekiwane: PASS, 3 testy. Jeśli któryś padnie, uzupełnij brakujący klucz
w pliku `.lng` — nie usuwaj stałej ze `StringKeys`.

- [ ] **Krok 5: Napisz dostawcę napisów**

`src/Mixcloud.Plugin/Localization/MuiStringProvider.cs`:

```csharp
using System;
using AIMP.SDK.MUIManager;
using Mixcloud.Core.Localization;

namespace Mixcloud.Plugin.Localization
{
    public sealed class MuiStringProvider : IStringProvider
    {
        private readonly IAimpServiceMUI _mui;

        public MuiStringProvider(IAimpServiceMUI mui)
        {
            _mui = mui ?? throw new ArgumentNullException(nameof(mui));
        }

        public string Get(string key)
        {
            try
            {
                var value = _mui.GetValue(key);
                // Brakujacy klucz zwraca swoja nazwe: widac to od razu przy pracy
                // i nie wywala wtyczki u uzytkownika.
                return string.IsNullOrEmpty(value) ? key : value;
            }
            catch (Exception)
            {
                return key;
            }
        }
    }
}
```

- [ ] **Krok 6: Usuń tymczasowy napis z zadania 1**

W `MixcloudPlugin.Initialize()` zamień `_probeItem.Name = "Mixcloud: dziala";`
na napis z `IStringProvider` (pełna integracja następuje w zadaniu 11).
Zbuduj: `dotnet build Mixcloud.sln -c Debug` — oczekiwane: sukces.

- [ ] **Krok 7: Commit**

```bash
git add -A && git commit -m "Lokalizacja PL/EN: klucze, pliki .lng i test spojnosci"
```

---

### Task 11: Budowa playlisty i komendy menu

Pierwsze zadanie dające użytkownikowi realną funkcję: wklejasz adres,
dostajesz playlistę.

**Files:**
- Create: `src/Mixcloud.Plugin/PluginContext.cs`
- Create: `src/Mixcloud.Plugin/Playlists/PlaylistBuilder.cs`
- Create: `src/Mixcloud.Plugin/Ui/OpenUrlDialog.cs`
- Modify: `src/Mixcloud.Plugin/MixcloudPlugin.cs`

**Interfaces:**
- Consumes: `MixcloudUrl`, `YtDlpService`, `MixcloudCatalog`,
  `MixcloudSettings`, `YtDlpInstaller`, `IStringProvider`, `StringKeys`.
- Produces:
  - `sealed class PluginContext` — właściwości `IAimpPlayer Player`,
    `IStringProvider Strings`, `MixcloudSettings Settings`,
    `YtDlpService YtDlp`, `string DataDir`; metoda `void SaveSettings()`
  - `sealed class PlaylistBuilder` — konstruktor `PlaylistBuilder(PluginContext ctx)`,
    metoda `void Build(MixcloudListing listing)`
  - `sealed class OpenUrlDialog` — statyczna metoda
    `string Show(IStringProvider strings)` zwracająca wpisany adres albo `null`

- [ ] **Krok 1: Napisz kontekst wtyczki**

```csharp
using System.IO;
using AIMP.SDK;
using AIMP.SDK.MessageDispatcher;
using Mixcloud.Core.Localization;
using Mixcloud.Core.Settings;
using Mixcloud.Core.YtDlp;

namespace Mixcloud.Plugin
{
    public sealed class PluginContext
    {
        public IAimpPlayer Player { get; }
        public IStringProvider Strings { get; }
        public MixcloudSettings Settings { get; }
        public YtDlpService YtDlp { get; }
        public string DataDir { get; }

        public PluginContext(IAimpPlayer player, IStringProvider strings,
            MixcloudSettings settings, YtDlpService ytDlp, string dataDir)
        {
            Player = player;
            Strings = strings;
            Settings = settings;
            YtDlp = ytDlp;
            DataDir = dataDir;
        }

        public string SettingsPath => Path.Combine(DataDir, "settings.json");

        public void SaveSettings() => Settings.Save(SettingsPath);

        public static string ResolveDataDir(IAimpPlayer player)
        {
            // Profil AIMP, nie zgadywany %APPDATA% i nie Program Files.
            var profile = player.Core.GetPath(AimpCorePathType.Profile);
            return Path.Combine(profile, "Mixcloud");
        }
    }
}
```

- [ ] **Krok 2: Napisz budowniczego playlist**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using AIMP.SDK;
using AIMP.SDK.Playlist.Objects;
using Mixcloud.Core.Catalog;

namespace Mixcloud.Plugin.Playlists
{
    public sealed class PlaylistBuilder
    {
        private readonly PluginContext _ctx;

        public PlaylistBuilder(PluginContext ctx)
        {
            _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
        }

        public void Build(MixcloudListing listing)
        {
            if (listing == null || listing.Tracks.Count == 0) return;

            var name = string.IsNullOrWhiteSpace(listing.Name) ? "Mixcloud" : listing.Name;

            // Zawsze nowa playlista - nigdy nie dopisujemy do tej,
            // ktorej uzytkownik wlasnie slucha.
            var created = _ctx.Player.ServicePlaylistManager.CreatePlaylist(name, true);
            if (created.ResultType != ActionResultType.OK) return;

            var playlist = created.Result;
            playlist.BeginUpdate();
            try
            {
                IList<string> urls = listing.Tracks.Select(t => t.PageUrl).ToList();
                // NoCheckFormat: adresy Mixclouda nie sa plikami, ktore AIMP
                // umie rozpoznac po rozszerzeniu.
                playlist.AddList(urls, PlaylistFlags.NoCheckFormat, PlaylistFilePosition.EndPosition);
            }
            finally
            {
                playlist.EndUpdate();
            }

            _ctx.Player.ServicePlaylistManager.SetActivePlaylist(playlist);
        }
    }
}
```

- [ ] **Krok 3: Napisz dialog adresu**

```csharp
using System;
using System.Drawing;
using System.Windows.Forms;
using Mixcloud.Core.Localization;

namespace Mixcloud.Plugin.Ui
{
    public static class OpenUrlDialog
    {
        public static string Show(IStringProvider s)
        {
            using (var form = new Form())
            using (var prompt = new Label())
            using (var input = new TextBox())
            using (var ok = new Button())
            using (var cancel = new Button())
            {
                form.Text = s.Get(StringKeys.DialogOpenUrlTitle);
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.StartPosition = FormStartPosition.CenterScreen;
                form.MinimizeBox = false;
                form.MaximizeBox = false;
                form.ClientSize = new Size(520, 120);

                prompt.Text = s.Get(StringKeys.DialogOpenUrlPrompt);
                prompt.SetBounds(12, 12, 496, 20);

                input.SetBounds(12, 38, 496, 24);

                ok.Text = s.Get(StringKeys.DialogOk);
                ok.DialogResult = DialogResult.OK;
                ok.SetBounds(332, 78, 84, 28);

                cancel.Text = s.Get(StringKeys.DialogCancel);
                cancel.DialogResult = DialogResult.Cancel;
                cancel.SetBounds(424, 78, 84, 28);

                form.Controls.AddRange(new Control[] { prompt, input, ok, cancel });
                form.AcceptButton = ok;
                form.CancelButton = cancel;

                return form.ShowDialog() == DialogResult.OK && input.Text.Trim().Length > 0
                    ? input.Text.Trim()
                    : null;
            }
        }
    }
}
```

Dodaj na górze pliku `using Mixcloud.Core.Localization;` — `StringKeys` jest
w tej przestrzeni nazw.

- [ ] **Krok 4: Podłącz komendy w wtyczce**

Zastąp treść `MixcloudPlugin.cs` poniższą. Wywołania sieciowe idą na wątek
roboczy; do AIMP wracamy przez `ServiceSynchronizer`, bo playlisty wolno
tworzyć wyłącznie z wątku głównego.

```csharp
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using AIMP.SDK;
using AIMP.SDK.MenuManager;
using AIMP.SDK.MenuManager.Objects;
using Mixcloud.Core.Catalog;
using Mixcloud.Core.Localization;
using Mixcloud.Core.Process;
using Mixcloud.Core.Settings;
using Mixcloud.Core.Urls;
using Mixcloud.Core.YtDlp;
using Mixcloud.Plugin.Playlists;
using Mixcloud.Plugin.Ui;

namespace Mixcloud.Plugin
{
    [AimpPlugin("Mixcloud", "Marek Zettel", "1.0.0",
        AimpPluginType = AimpPluginType.Addons,
        Description = "Mixcloud integration for AIMP")]
    public sealed class MixcloudPlugin : AimpPlugin
    {
        private PluginContext _ctx;
        private YtDlpInstaller _installer;
        private IAimpMenuItem _openUrlItem;
        private IAimpMenuItem _favoritesItem;

        public override void Initialize()
        {
            var strings = new Localization.MuiStringProvider(Player.ServiceMui);
            var dataDir = PluginContext.ResolveDataDir(Player);
            Directory.CreateDirectory(dataDir);

            var settings = MixcloudSettings.Load(Path.Combine(dataDir, "settings.json"));
            _installer = new YtDlpInstaller(new HttpDownloader(), dataDir);
            _installer.ApplyPendingUpdate();

            var ytDlp = new YtDlpService(new ProcessRunner(), _installer.ExePath);
            _ctx = new PluginContext(Player, strings, settings, ytDlp, dataDir);

            _openUrlItem = AddMenuItem("Mixcloud.OpenUrl", StringKeys.MenuOpenUrl, OnOpenUrl);
            _favoritesItem = AddMenuItem("Mixcloud.Favorites", StringKeys.MenuLoadFavorites, OnLoadFavorites);

            StartBackgroundSetup();
        }

        public override void Dispose()
        {
            if (_openUrlItem != null) { Player.ServiceMenuManager.Delete(_openUrlItem); _openUrlItem = null; }
            if (_favoritesItem != null) { Player.ServiceMenuManager.Delete(_favoritesItem); _favoritesItem = null; }
        }

        private IAimpMenuItem AddMenuItem(string id, string labelKey, Action onClick)
        {
            var created = Player.Core.CreateAimpObject<IAimpMenuItem>();
            if (created.ResultType != ActionResultType.OK) return null;

            var item = created.Result;
            item.Id = id;
            item.Name = _ctx.Strings.Get(labelKey);
            item.Style = MenuItemStyle.Normal;

            // Klikniecie obsluguje IAimpAction: dziedziczy po IAimpActionEvent,
            // wiec ma zdarzenie OnExecute. Wlasciwosc IAimpMenuItem.Custom jest
            // typu string i sluzy do czego innego - nie wolno jej tu uzyc.
            var action = Player.ServiceActionManager.CreateAction();
            action.Id = id + ".Action";
            action.Name = item.Name;
            action.GroupName = "Mixcloud";
            action.Enabled = true;
            action.OnExecute += (s, e) => onClick();
            Player.ServiceActionManager.Register(action);
            item.Action = action;

            Player.ServiceMenuManager.Add(ParentMenuType.PlayerMainOpen, item);
            return item;
        }

        private void OnOpenUrl()
        {
            var raw = OpenUrlDialog.Show(_ctx.Strings);
            if (raw == null) return;

            var url = MixcloudUrl.Parse(raw);
            if (url.Kind == MixcloudUrlKind.Invalid)
            {
                ShowError(StringKeys.MsgInvalidUrl);
                return;
            }
            LoadAsync(url);
        }

        private void OnLoadFavorites()
        {
            if (string.IsNullOrWhiteSpace(_ctx.Settings.Handle))
            {
                ShowError(StringKeys.MsgNoHandle);
                return;
            }
            LoadAsync(MixcloudUrl.ForFavorites(_ctx.Settings.Handle));
        }

        private void LoadAsync(MixcloudUrl url)
        {
            Task.Run(() =>
            {
                try
                {
                    var listing = url.Kind == MixcloudUrlKind.Listing
                        ? MixcloudCatalog.ParseFlatListing(
                            _ctx.YtDlp.DumpListing(url, _ctx.Settings.ListingLimit, CancellationToken.None))
                        : SingleTrackListing(url);

                    if (listing.Tracks.Count == 0)
                    {
                        OnMainThread(() => ShowError(StringKeys.MsgEmptyResult));
                        return;
                    }
                    OnMainThread(() => new PlaylistBuilder(_ctx).Build(listing));
                }
                catch (YtDlpException)
                {
                    OnMainThread(() => ShowError(StringKeys.MsgYtDlpFailed));
                }
            });
        }

        private MixcloudListing SingleTrackListing(MixcloudUrl url)
        {
            var track = MixcloudCatalog.ParseCloudcast(
                _ctx.YtDlp.DumpCloudcast(url, CancellationToken.None));
            return new MixcloudListing
            {
                Name = track.Title,
                Tracks = new[] { track }
            };
        }

        private void StartBackgroundSetup()
        {
            Task.Run(() =>
            {
                try
                {
                    _installer.EnsureInstalled(CancellationToken.None);
                    if (_ctx.Settings.AutoUpdateYtDlp &&
                        DateTime.UtcNow - _ctx.Settings.LastUpdateCheckUtc > TimeSpan.FromHours(24))
                    {
                        _installer.CheckForUpdate(_ctx.Settings, CancellationToken.None);
                        _ctx.SaveSettings();
                    }
                }
                catch (Exception)
                {
                    // Awaria przygotowania yt-dlp zglosi sie dopiero przy probie uzycia.
                }
            });
        }

        private void OnMainThread(Action action)
        {
            // ExecuteInMainThread przyjmuje IAimpTask, nie Action - stad opakowanie.
            Player.ServiceSynchronizer.ExecuteInMainThread(new DelegateTask(action), true);
        }

        private sealed class DelegateTask : AIMP.SDK.Threading.IAimpTask
        {
            private readonly Action _action;
            public DelegateTask(Action action) { _action = action; }
            public void Execute(AIMP.SDK.Threading.IAimpTaskOwner owner) { _action(); }
        }

        private void ShowError(string messageKey)
        {
            MessageBox.Show(_ctx.Strings.Get(messageKey),
                _ctx.Strings.Get(StringKeys.MsgError),
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
```

Powyzsze sygnatury sa zweryfikowane refleksja na AIMP.SDK.dll 5.3.2394.5:
`IAimpAction` dziedziczy po `IAimpActionEvent` (stad `OnExecute`),
`IAimpMenuItem.Custom` jest typu `string` i sluzy do czego innego, a
`IAimpServiceSynchronizer.ExecuteInMainThread` przyjmuje `(IAimpTask, bool)`.
- [ ] **Krok 5: Zbuduj, wdróż i sprawdź**

```bash
dotnet build Mixcloud.sln -c Debug
```

Zamknij AIMP, uruchom `tools/deploy.ps1` jako administrator, otwórz AIMP.

Test: menu → „Adres Mixcloud..." → wklej
`https://www.mixcloud.com/spartacus/favorites/` → Otwórz.
Oczekiwane: powstaje playlista **„Spartacus (favorites)"** z pozycjami,
których tytuły są wyprowadzone ze slugów (np. „Mental Place 26").

- [ ] **Krok 6: Commit**

```bash
git add -A && git commit -m "Komendy menu, dialog adresu i budowa playlisty"
```

---

### Task 12: Odtwarzanie — podmiana adresu w OnCheckURL

Wykonaj **tylko jeśli** spike z zadania 2 potwierdził, że AIMP honoruje
podmieniony adres. W przeciwnym razie pomiń i przejdź do zadania 13.

**Files:**
- Create: `src/Mixcloud.Core/Media/IMediaSource.cs`
- Create: `src/Mixcloud.Core/Media/StreamMediaSource.cs`
- Modify: `src/Mixcloud.Plugin/Extensions/MixcloudPlayerHook.cs`
- Modify: `src/Mixcloud.Plugin/MixcloudPlugin.cs`
- Test: `tests/Mixcloud.Core.Tests/StreamMediaSourceTests.cs`

**Interfaces:**
- Consumes: `YtDlpService` (zad. 6).
- Produces:
  - `interface IMediaSource` z metodą
    `string GetPlayableUrl(string pageUrl, CancellationToken ct)`
  - `sealed class StreamMediaSource : IMediaSource` — konstruktor
    `StreamMediaSource(YtDlpService ytDlp, TimeSpan cacheLifetime)`

- [ ] **Krok 1: Napisz testy**

```csharp
using System;
using System.Threading;
using Mixcloud.Core.Media;
using Mixcloud.Core.YtDlp;
using Xunit;

public class StreamMediaSourceTests
{
    private static StreamMediaSource Make(FakeProcessRunner r, TimeSpan life) =>
        new StreamMediaSource(new YtDlpService(r, @"C:\yt\yt-dlp.exe"), life);

    [Fact]
    public void RozwiazujeAdresStrony()
    {
        var r = new FakeProcessRunner { NextStdOut = "https://dl.mixcloud.stream/a.m4a?sig=1\n" };
        var url = Make(r, TimeSpan.FromMinutes(10))
            .GetPlayableUrl("https://www.mixcloud.com/a/b/", CancellationToken.None);
        Assert.Equal("https://dl.mixcloud.stream/a.m4a?sig=1", url);
    }

    [Fact]
    public void SwiezyWynikJestBranyZPamieciPodrecznej()
    {
        var r = new FakeProcessRunner { NextStdOut = "https://dl.mixcloud.stream/a.m4a?sig=1\n" };
        var src = Make(r, TimeSpan.FromMinutes(10));
        src.GetPlayableUrl("https://www.mixcloud.com/a/b/", CancellationToken.None);

        r.NextStdOut = "https://dl.mixcloud.stream/INNY.m4a?sig=2\n";
        var drugi = src.GetPlayableUrl("https://www.mixcloud.com/a/b/", CancellationToken.None);

        Assert.EndsWith("a.m4a?sig=1", drugi);
    }

    [Fact]
    public void WygasnietyWynikJestRozwiazywanyPonownie()
    {
        var r = new FakeProcessRunner { NextStdOut = "https://dl.mixcloud.stream/a.m4a?sig=1\n" };
        var src = Make(r, TimeSpan.Zero);
        src.GetPlayableUrl("https://www.mixcloud.com/a/b/", CancellationToken.None);

        r.NextStdOut = "https://dl.mixcloud.stream/b.m4a?sig=2\n";
        Assert.EndsWith("b.m4a?sig=2", src.GetPlayableUrl("https://www.mixcloud.com/a/b/", CancellationToken.None));
    }

    [Fact]
    public void BladRozwiazywaniaDajeNullZamiastWyjatku()
    {
        var r = new FakeProcessRunner { NextExitCode = 1, NextStdErr = "ERROR" };
        Assert.Null(Make(r, TimeSpan.FromMinutes(10))
            .GetPlayableUrl("https://www.mixcloud.com/a/b/", CancellationToken.None));
    }
}
```

- [ ] **Krok 2: Uruchom testy i potwierdź, że padają**

Run: `dotnet test tests/Mixcloud.Core.Tests --filter StreamMediaSourceTests -v:minimal`
Oczekiwane: FAIL — CS0246, `StreamMediaSource` nie istnieje.

- [ ] **Krok 3: Napisz implementację**

```csharp
using System.Threading;

namespace Mixcloud.Core.Media
{
    public interface IMediaSource
    {
        string GetPlayableUrl(string pageUrl, CancellationToken ct);
    }
}
```

```csharp
using System;
using System.Collections.Concurrent;
using System.Threading;
using Mixcloud.Core.YtDlp;

namespace Mixcloud.Core.Media
{
    public sealed class StreamMediaSource : IMediaSource
    {
        private sealed class Entry
        {
            public string Url;
            public DateTime ResolvedUtc;
        }

        private readonly YtDlpService _ytDlp;
        private readonly TimeSpan _lifetime;
        private readonly ConcurrentDictionary<string, Entry> _cache =
            new ConcurrentDictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

        public StreamMediaSource(YtDlpService ytDlp, TimeSpan cacheLifetime)
        {
            _ytDlp = ytDlp ?? throw new ArgumentNullException(nameof(ytDlp));
            _lifetime = cacheLifetime;
        }

        public string GetPlayableUrl(string pageUrl, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(pageUrl)) return null;

            Entry cached;
            if (_cache.TryGetValue(pageUrl, out cached) &&
                DateTime.UtcNow - cached.ResolvedUtc < _lifetime)
            {
                return cached.Url;
            }

            try
            {
                // Adres zawiera parametr ?sig= i wygasa, dlatego trzymamy go
                // tylko przez _lifetime, a potem rozwiazujemy od nowa.
                var resolved = _ytDlp.ResolveDirectUrl(pageUrl, ct);
                if (string.IsNullOrEmpty(resolved)) return null;

                _cache[pageUrl] = new Entry { Url = resolved, ResolvedUtc = DateTime.UtcNow };
                return resolved;
            }
            catch (YtDlpException)
            {
                return null;
            }
        }
    }
}
```

- [ ] **Krok 4: Uruchom testy i potwierdź, że przechodzą**

Run: `dotnet test tests/Mixcloud.Core.Tests --filter StreamMediaSourceTests -v:minimal`
Oczekiwane: PASS, 4 testy.

- [ ] **Krok 5: Podłącz hook do prawdziwego źródła**

Zastąp treść `MixcloudPlayerHook.cs` — usuwa to spike'owy log i twardo
zaszyty adres:

```csharp
using System;
using System.Threading;
using AIMP.SDK.Player.Extensions;
using Mixcloud.Core.Media;
using Mixcloud.Core.Urls;

namespace Mixcloud.Plugin.Extensions
{
    public sealed class MixcloudPlayerHook : IAimpExtensionPlayerHook
    {
        private readonly IMediaSource _source;

        public MixcloudPlayerHook(IMediaSource source)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
        }

        public bool OnCheckURL(ref string url)
        {
            if (MixcloudUrl.Parse(url).Kind != MixcloudUrlKind.Cloudcast) return false;

            var playable = _source.GetPlayableUrl(url, CancellationToken.None);
            if (string.IsNullOrEmpty(playable)) return false;

            url = playable;
            return true;
        }
    }
}
```

W `MixcloudPlugin.Initialize()`, po utworzeniu `_ctx`:

```csharp
            _mediaSource = new StreamMediaSource(ytDlp, TimeSpan.FromMinutes(30));
            _hook = new Extensions.MixcloudPlayerHook(_mediaSource);
            Player.Core.RegisterExtension(_hook);
```

Pola klasy: `private IMediaSource _mediaSource;` oraz
`private Extensions.MixcloudPlayerHook _hook;`
W `Dispose()`: `if (_hook != null) { Player.Core.UnregisterExtension(_hook); _hook = null; }`

- [ ] **Krok 6: Zbuduj, wdróż i sprawdź odtwarzanie**

```bash
dotnet build Mixcloud.sln -c Debug
```

Wdróż, uruchom AIMP, wczytaj `https://www.mixcloud.com/spartacus/favorites/`
i odtwórz pierwszą pozycję.
Oczekiwane: dźwięk startuje w kilka sekund, suwak postępu działa i pozwala
przewijać (serwer zwraca `206 Partial Content`).

- [ ] **Krok 7: Commit**

```bash
git add -A && git commit -m "Odtwarzanie strumieniowe przez podmiane adresu w OnCheckURL"
```

---

### Task 13: Fallback — pobieranie do katalogu tymczasowego

Wykonaj **tylko jeśli** spike z zadania 2 wykazał, że AIMP nie honoruje
podmienionego adresu.

**Files:**
- Create: `src/Mixcloud.Core/Media/TempCache.cs`
- Create: `src/Mixcloud.Core/Media/DownloadMediaSource.cs`
- Test: `tests/Mixcloud.Core.Tests/TempCacheTests.cs`

**Interfaces:**
- Consumes: `IMediaSource` (zad. 12, krok 3 — utwórz plik `IMediaSource.cs`
  także w tym wariancie), `YtDlpService`, `IProcessRunner`.
- Produces:
  - `sealed class TempCache` — konstruktor `TempCache(string dir, long limitBytes)`,
    metody `string PathFor(string pageUrl)`, `void Prune(TimeSpan keepNewerThan)`,
    `long CurrentSizeBytes()`
  - `sealed class DownloadMediaSource : IMediaSource` — konstruktor
    `DownloadMediaSource(IProcessRunner runner, string exePath, TempCache cache)`

- [ ] **Krok 1: Napisz testy pamięci podręcznej**

```csharp
using System;
using System.IO;
using System.Linq;
using Mixcloud.Core.Media;
using Xunit;

public class TempCacheTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mccache-" + Guid.NewGuid().ToString("N"));
    public TempCacheTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void TenSamAdresDajeTeSamaSciezke()
    {
        var c = new TempCache(_dir, 1024 * 1024);
        Assert.Equal(c.PathFor("https://www.mixcloud.com/a/b/"), c.PathFor("https://www.mixcloud.com/a/b/"));
    }

    [Fact]
    public void RozneAdresyDajaRozneSciezki()
    {
        var c = new TempCache(_dir, 1024 * 1024);
        Assert.NotEqual(c.PathFor("https://www.mixcloud.com/a/b/"), c.PathFor("https://www.mixcloud.com/a/c/"));
    }

    [Fact]
    public void SciezkaNieZawieraZnakowNiedozwolonychWNazwiePliku()
    {
        var name = Path.GetFileName(new TempCache(_dir, 1024).PathFor("https://www.mixcloud.com/a/b/"));
        Assert.Empty(name.Intersect(Path.GetInvalidFileNameChars()));
    }

    [Fact]
    public void PruneUsuwaStarePlikiAZostawiaSwieze()
    {
        var stary = Path.Combine(_dir, "stary.m4a");
        var swiezy = Path.Combine(_dir, "swiezy.m4a");
        File.WriteAllText(stary, "x");
        File.WriteAllText(swiezy, "y");
        File.SetLastAccessTimeUtc(stary, DateTime.UtcNow.AddDays(-3));

        new TempCache(_dir, 1024 * 1024).Prune(TimeSpan.FromHours(24));

        Assert.False(File.Exists(stary));
        Assert.True(File.Exists(swiezy));
    }

    [Fact]
    public void PoPrzekroczeniuLimituKasowaneSaNajdawniejUzywane()
    {
        for (var i = 0; i < 3; i++)
        {
            var p = Path.Combine(_dir, "f" + i + ".m4a");
            File.WriteAllBytes(p, new byte[100]);
            File.SetLastAccessTimeUtc(p, DateTime.UtcNow.AddMinutes(-10 + i));
        }

        new TempCache(_dir, 150).Prune(TimeSpan.FromHours(24));

        Assert.True(new TempCache(_dir, 150).CurrentSizeBytes() <= 150);
        Assert.False(File.Exists(Path.Combine(_dir, "f0.m4a")));
    }
}
```

- [ ] **Krok 2: Uruchom testy i potwierdź, że padają**

Run: `dotnet test tests/Mixcloud.Core.Tests --filter TempCacheTests -v:minimal`
Oczekiwane: FAIL — CS0246, `TempCache` nie istnieje.

- [ ] **Krok 3: Napisz pamięć podręczną**

```csharp
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Mixcloud.Core.Media
{
    public sealed class TempCache
    {
        private readonly string _dir;
        private readonly long _limitBytes;

        public TempCache(string dir, long limitBytes)
        {
            _dir = dir ?? throw new ArgumentNullException(nameof(dir));
            _limitBytes = limitBytes;
            Directory.CreateDirectory(_dir);
        }

        public string PathFor(string pageUrl)
        {
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(pageUrl ?? string.Empty));
                var name = BitConverter.ToString(hash).Replace("-", string.Empty).Substring(0, 32);
                return Path.Combine(_dir, name + ".m4a");
            }
        }

        public long CurrentSizeBytes()
        {
            return new DirectoryInfo(_dir).GetFiles().Sum(f => f.Length);
        }

        public void Prune(TimeSpan keepNewerThan)
        {
            var info = new DirectoryInfo(_dir);
            if (!info.Exists) return;

            var cutoff = DateTime.UtcNow - keepNewerThan;
            var files = info.GetFiles().OrderBy(f => f.LastAccessTimeUtc).ToList();

            foreach (var f in files.Where(f => f.LastAccessTimeUtc < cutoff).ToList())
            {
                TryDelete(f);
                files.Remove(f);
            }

            // Katalog tymczasowy nie moze rosnac w nieskonczonosc.
            var total = files.Sum(f => f.Length);
            foreach (var f in files)
            {
                if (total <= _limitBytes) break;
                total -= f.Length;
                TryDelete(f);
            }
        }

        private static void TryDelete(FileInfo f)
        {
            try { f.Delete(); } catch (IOException) { /* w uzyciu - zostaw */ }
        }
    }
}
```

- [ ] **Krok 4: Uruchom testy i potwierdź, że przechodzą**

Run: `dotnet test tests/Mixcloud.Core.Tests --filter TempCacheTests -v:minimal`
Oczekiwane: PASS, 5 testów.

- [ ] **Krok 5: Napisz źródło pobierające**

```csharp
using System;
using System.IO;
using System.Threading;
using Mixcloud.Core.Process;
using Mixcloud.Core.YtDlp;

namespace Mixcloud.Core.Media
{
    public sealed class DownloadMediaSource : IMediaSource
    {
        private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(30);

        private readonly IProcessRunner _runner;
        private readonly string _exePath;
        private readonly TempCache _cache;

        public DownloadMediaSource(IProcessRunner runner, string exePath, TempCache cache)
        {
            _runner = runner ?? throw new ArgumentNullException(nameof(runner));
            _exePath = exePath ?? throw new ArgumentNullException(nameof(exePath));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        }

        public string GetPlayableUrl(string pageUrl, CancellationToken ct)
        {
            var target = _cache.PathFor(pageUrl);

            // Gotowy plik oznacza, ze tego miksu nie pobieramy drugi raz.
            if (File.Exists(target))
            {
                File.SetLastAccessTimeUtc(target, DateTime.UtcNow);
                return target;
            }

            var partial = target + ".part";
            var args = "-f \"" + YtDlpService.FormatSelector + "\" --no-warnings -o \"" +
                       partial + "\" \"" + pageUrl + "\"";

            var res = _runner.Run(_exePath, args, DownloadTimeout, ct);
            if (res.TimedOut || res.ExitCode != 0 || !File.Exists(partial))
            {
                TryDelete(partial);
                return null;
            }

            // Nie gramy z pliku w trakcie zapisu - dopiero po zamknieciu.
            try { File.Move(partial, target); }
            catch (IOException) { TryDelete(partial); return null; }

            return target;
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { }
        }
    }
}
```

- [ ] **Krok 6: Podłącz w wtyczce**

W `MixcloudPlugin.Initialize()` zamiast `StreamMediaSource`:

```csharp
            var cache = new TempCache(
                Path.Combine(Path.GetTempPath(), "AIMP-Mixcloud"),
                settings.CacheLimitBytes);
            cache.Prune(TimeSpan.FromHours(24));
            _mediaSource = new DownloadMediaSource(new ProcessRunner(), _installer.ExePath, cache);
            _hook = new Extensions.MixcloudPlayerHook(_mediaSource);
            Player.Core.RegisterExtension(_hook);
```

- [ ] **Krok 7: Zbuduj, wdróż i sprawdź**

```bash
dotnet build Mixcloud.sln -c Debug
```

Wdróż i odtwórz pozycję. Oczekiwane: po chwili pobierania miks gra,
a w `%TEMP%\AIMP-Mixcloud\` pojawia się plik `.m4a`.

- [ ] **Krok 8: Commit**

```bash
git add -A && git commit -m "Fallback: pobieranie do katalogu tymczasowego z limitem i sprzataniem"
```

---

### Task 14: Metadane pozycji przez FileInfoProvider

Bez tego zadania playlista pokazuje tytuły wyprowadzone ze slugów i zerowy
czas trwania.

**Files:**
- Create: `src/Mixcloud.Plugin/Extensions/MixcloudFileInfoProvider.cs`
- Modify: `src/Mixcloud.Plugin/MixcloudPlugin.cs`

**Interfaces:**
- Consumes: `YtDlpService`, `MixcloudCatalog`, `MixcloudUrl`, `MixcloudTrack`.
- Produces: `sealed class MixcloudFileInfoProvider : IAimpExtensionFileInfoProvider`
  z metodami `AimpActionResult GetFileInfo(string fileUri, out IAimpFileInfo info)`
  oraz `AimpActionResult GetFileInfo(IAimpStream stream, out IAimpFileInfo info)`.

- [ ] **Krok 1: Napisz rozszerzenie**

```csharp
using System;
using System.Collections.Concurrent;
using System.Threading;
using AIMP.SDK;
using AIMP.SDK.FileManager.Extensions;
using AIMP.SDK.FileManager.Objects;
using AIMP.SDK.Objects;
using Mixcloud.Core.Catalog;
using Mixcloud.Core.Urls;
using Mixcloud.Core.YtDlp;

namespace Mixcloud.Plugin.Extensions
{
    public sealed class MixcloudFileInfoProvider : IAimpExtensionFileInfoProvider
    {
        private readonly IAimpCore _core;
        private readonly YtDlpService _ytDlp;
        private readonly ConcurrentDictionary<string, MixcloudTrack> _cache =
            new ConcurrentDictionary<string, MixcloudTrack>(StringComparer.OrdinalIgnoreCase);

        public MixcloudFileInfoProvider(IAimpCore core, YtDlpService ytDlp)
        {
            _core = core ?? throw new ArgumentNullException(nameof(core));
            _ytDlp = ytDlp ?? throw new ArgumentNullException(nameof(ytDlp));
        }

        public AimpActionResult GetFileInfo(string fileUri, out IAimpFileInfo info)
        {
            info = null;

            var url = MixcloudUrl.Parse(fileUri);
            if (url.Kind != MixcloudUrlKind.Cloudcast)
                return new AimpActionResult(ActionResultType.NoInterface);

            MixcloudTrack track;
            if (!_cache.TryGetValue(url.Normalized, out track))
            {
                try
                {
                    track = MixcloudCatalog.ParseCloudcast(
                        _ytDlp.DumpCloudcast(url, CancellationToken.None));
                    _cache[url.Normalized] = track;
                }
                catch (Exception)
                {
                    return new AimpActionResult(ActionResultType.Fail);
                }
            }

            var created = _core.CreateAimpObject<IAimpFileInfo>();
            if (created.ResultType != ActionResultType.OK)
                return new AimpActionResult(created.ResultType);

            info = created.Result;
            info.FileName = url.Normalized;
            info.Title = track.Title;
            info.Artist = track.Artist;
            info.Album = "Mixcloud";
            info.Duration = track.DurationSeconds;
            return new AimpActionResult(ActionResultType.OK);
        }

        public AimpActionResult GetFileInfo(IAimpStream stream, out IAimpFileInfo info)
        {
            // Strumienie obsluguje AIMP samodzielnie; nas interesuja tylko adresy.
            info = null;
            return new AimpActionResult(ActionResultType.NotImplemented);
        }
    }
}
```

- [ ] **Krok 2: Zarejestruj rozszerzenie**

W `MixcloudPlugin.Initialize()`, obok rejestracji hooka:

```csharp
            _fileInfo = new Extensions.MixcloudFileInfoProvider(Player.Core, ytDlp);
            Player.Core.RegisterExtension(_fileInfo);
```

Pole: `private Extensions.MixcloudFileInfoProvider _fileInfo;`
W `Dispose()`: `if (_fileInfo != null) { Player.Core.UnregisterExtension(_fileInfo); _fileInfo = null; }`

- [ ] **Krok 3: Zbuduj, wdróż i sprawdź**

```bash
dotnet build Mixcloud.sln -c Debug
```

Wdróż, wczytaj ulubione.
Oczekiwane: po chwili pozycje pokazują prawdziwe tytuły z Mixclouda
(np. „Loraine James - 1st September 2026") i rzeczywisty czas trwania
zamiast `0:00`.

- [ ] **Krok 4: Commit**

```bash
git add -A && git commit -m "FileInfoProvider: leniwe uzupelnianie metadanych pozycji"
```

---

### Task 15: Strona ustawień

**Files:**
- Create: `src/Mixcloud.Plugin/Ui/OptionsFrame.cs`
- Modify: `src/Mixcloud.Plugin/MixcloudPlugin.cs`

**Interfaces:**
- Consumes: `PluginContext`, `IStringProvider`, `StringKeys`,
  `MixcloudSettings`, `YtDlpInstaller`, `YtDlpService`.
- Produces: `sealed class OptionsFrame : IAimpOptionsDialogFrame` z metodami
  `string GetName()`, `IntPtr CreateFrame(IntPtr parentHandle)`,
  `void DestroyFrame()`, `void Notification(OptionsDialogFrameNotificationType type)`.

- [ ] **Krok 1: Napisz stronę ustawień**

AIMP daje uchwyt okna rodzica, więc osadzamy panel WinForms jako okno
potomne.

```csharp
using System;
using System.Drawing;
using System.Windows.Forms;
using AIMP.SDK.Options;
using Mixcloud.Core.Localization;
using Mixcloud.Core.Settings;
using Mixcloud.Core.YtDlp;

namespace Mixcloud.Plugin.Ui
{
    public sealed class OptionsFrame : IAimpOptionsDialogFrame
    {
        private readonly PluginContext _ctx;
        private readonly YtDlpInstaller _installer;

        private Panel _panel;
        private TextBox _handle;
        private NumericUpDown _limit;
        private NumericUpDown _cacheGb;
        private CheckBox _autoUpdate;
        private Label _version;

        public OptionsFrame(PluginContext ctx, YtDlpInstaller installer)
        {
            _ctx = ctx;
            _installer = installer;
        }

        public string GetName() => "Mixcloud";

        public IntPtr CreateFrame(IntPtr parentHandle)
        {
            var s = _ctx.Strings;
            _panel = new Panel { Location = new Point(0, 0), Size = new Size(560, 260) };

            _panel.Controls.Add(Label(s.Get(StringKeys.OptHandle), 12, 15));
            _handle = new TextBox { Text = _ctx.Settings.Handle };
            _handle.SetBounds(240, 12, 300, 24);
            _panel.Controls.Add(_handle);

            _panel.Controls.Add(Label(s.Get(StringKeys.OptListingLimit), 12, 51));
            _limit = new NumericUpDown { Minimum = 1, Maximum = 10000, Value = _ctx.Settings.ListingLimit };
            _limit.SetBounds(240, 48, 100, 24);
            _panel.Controls.Add(_limit);

            _panel.Controls.Add(Label(s.Get(StringKeys.OptCacheLimit), 12, 87));
            _cacheGb = new NumericUpDown
            {
                Minimum = 1,
                Maximum = 200,
                Value = Math.Max(1, _ctx.Settings.CacheLimitBytes / (1024L * 1024 * 1024))
            };
            _cacheGb.SetBounds(240, 84, 100, 24);
            _panel.Controls.Add(_cacheGb);

            _autoUpdate = new CheckBox
            {
                Text = s.Get(StringKeys.OptAutoUpdate),
                Checked = _ctx.Settings.AutoUpdateYtDlp,
                AutoSize = true
            };
            _autoUpdate.Location = new Point(12, 122);
            _panel.Controls.Add(_autoUpdate);

            _panel.Controls.Add(Label(s.Get(StringKeys.OptYtDlpVersion), 12, 159));
            _version = Label("...", 240, 159);
            _panel.Controls.Add(_version);

            var check = new Button { Text = s.Get(StringKeys.OptCheckNow) };
            check.SetBounds(12, 190, 220, 28);
            check.Click += (o, e) => CheckNow();
            _panel.Controls.Add(check);

            RefreshVersion();

            // Osadzenie panelu w oknie ustawien AIMP.
            SetParent(_panel.Handle, parentHandle);
            return _panel.Handle;
        }

        public void DestroyFrame()
        {
            if (_panel == null) return;
            _panel.Dispose();
            _panel = null;
        }

        public void Notification(OptionsDialogFrameNotificationType type)
        {
            if (_panel == null) return;

            if (type == OptionsDialogFrameNotificationType.Save)
            {
                _ctx.Settings.Handle = _handle.Text.Trim();
                _ctx.Settings.ListingLimit = (int)_limit.Value;
                _ctx.Settings.CacheLimitBytes = (long)_cacheGb.Value * 1024 * 1024 * 1024;
                _ctx.Settings.AutoUpdateYtDlp = _autoUpdate.Checked;
                _ctx.SaveSettings();
            }
            else if (type == OptionsDialogFrameNotificationType.Load)
            {
                _handle.Text = _ctx.Settings.Handle;
                _limit.Value = _ctx.Settings.ListingLimit;
                _autoUpdate.Checked = _ctx.Settings.AutoUpdateYtDlp;
                RefreshVersion();
            }
        }

        private void CheckNow()
        {
            System.Threading.Tasks.Task.Run(() =>
            {
                _installer.CheckForUpdate(_ctx.Settings, System.Threading.CancellationToken.None);
                _ctx.SaveSettings();
            });
        }

        private void RefreshVersion()
        {
            System.Threading.Tasks.Task.Run(() =>
            {
                string v;
                try { v = _ctx.YtDlp.GetVersion(System.Threading.CancellationToken.None); }
                catch (Exception) { v = _ctx.Strings.Get(StringKeys.MsgYtDlpMissing); }

                var label = _version;
                if (label != null && label.IsHandleCreated)
                    label.BeginInvoke(new Action(() => label.Text = v));
            });
        }

        private static Label Label(string text, int x, int y)
        {
            var l = new System.Windows.Forms.Label { Text = text, AutoSize = true };
            l.Location = new Point(x, y);
            return l;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetParent(IntPtr child, IntPtr newParent);
    }
}
```

`OptionsDialogFrameNotificationType` ma dokladnie wartosci
`Load`, `Localization`, `Save`, `CanSave` — zweryfikowane refleksja.

W `MixcloudPlugin.Initialize()`:

```csharp
            _options = new Ui.OptionsFrame(_ctx, _installer);
            Player.Core.RegisterExtension(_options);
```

Pole: `private Ui.OptionsFrame _options;`
W `Dispose()`: `if (_options != null) { Player.Core.UnregisterExtension(_options); _options = null; }`

- [ ] **Krok 3: Zbuduj, wdróż i sprawdź**

```bash
dotnet build Mixcloud.sln -c Debug
```

Wdróż, otwórz Ustawienia AIMP.
Oczekiwane: strona „Mixcloud" z polem nazwy użytkownika, limitem pozycji,
limitem cache, przełącznikiem auto-update i wersją yt-dlp. Wpisz swój handle,
zatwierdź, a następnie użyj „Mixcloud: wczytaj moje ulubione" — musi powstać
playlista z Twoimi ulubionymi.

- [ ] **Krok 4: Sprawdź dwujęzyczność**

Przełącz język AIMP na angielski i z powrotem na polski (Ustawienia → Język).
Oczekiwane: pozycje menu, dialog adresu i strona ustawień zmieniają język.
Jeśli gdziekolwiek widzisz surowy klucz w rodzaju `Mixcloud.Menu\OpenUrl`,
brakuje wpisu w pliku `.lng` — dopisz go do **obu** plików.

- [ ] **Krok 5: Commit**

```bash
git add -A && git commit -m "Strona ustawien wtyczki z obsluga jezykow"
```

---

### Task 16: Test integracyjny i domknięcie

**Files:**
- Create: `tests/Mixcloud.Core.Tests/LiveMixcloudTests.cs`
- Create: `README.md`

**Interfaces:**
- Consumes: wszystko z zadań 3–9.

- [ ] **Krok 1: Napisz test integracyjny**

Test jest opt-in — wymaga sieci i zmiennej środowiskowej, więc nie psuje
zwykłego przebiegu testów. Pełni rolę wczesnego ostrzeżenia, gdy Mixcloud
zmieni format i nagrane fixture'y przestaną odpowiadać rzeczywistości.

```csharp
using System;
using System.Linq;
using System.Threading;
using Mixcloud.Core.Catalog;
using Mixcloud.Core.Process;
using Mixcloud.Core.Urls;
using Mixcloud.Core.YtDlp;
using Xunit;

public class LiveMixcloudTests
{
    // Uruchom: MIXCLOUD_LIVE=1 dotnet test --filter LiveMixcloudTests
    private const string Gate = "MIXCLOUD_LIVE";

    private static YtDlpService Service()
    {
        var exe = Environment.GetEnvironmentVariable("YTDLP_PATH") ?? "yt-dlp.exe";
        return new YtDlpService(new ProcessRunner(), exe);
    }

    [SkippableFact]
    public void UlubioneZwracajaPozycjeIMajaNazweListy()
    {
        Skip.If(Environment.GetEnvironmentVariable(Gate) != "1", "Test sieciowy wylaczony.");

        var url = MixcloudUrl.Parse("https://www.mixcloud.com/spartacus/favorites/");
        var listing = MixcloudCatalog.ParseFlatListing(
            Service().DumpListing(url, 5, CancellationToken.None));

        Assert.NotEmpty(listing.Tracks);
        Assert.False(string.IsNullOrWhiteSpace(listing.Name));
        Assert.All(listing.Tracks, t => Assert.StartsWith("https://www.mixcloud.com/", t.PageUrl));
    }

    [SkippableFact]
    public void RozwiazanyAdresJestBezposrednimStrumieniem()
    {
        Skip.If(Environment.GetEnvironmentVariable(Gate) != "1", "Test sieciowy wylaczony.");

        var url = MixcloudUrl.Parse("https://www.mixcloud.com/spartacus/favorites/");
        var first = MixcloudCatalog.ParseFlatListing(
            Service().DumpListing(url, 1, CancellationToken.None)).Tracks.First();

        var direct = Service().ResolveDirectUrl(first.PageUrl, CancellationToken.None);

        Assert.NotNull(direct);
        Assert.StartsWith("http", direct);
        Assert.DoesNotContain("mixcloud.com/", direct);
    }
}
```

Dodaj pakiet dający `[SkippableFact]`:

```bash
dotnet add tests/Mixcloud.Core.Tests package Xunit.SkippableFact --version 1.4.13
```

- [ ] **Krok 2: Uruchom pełny zestaw testów**

Run: `dotnet test Mixcloud.sln -v:minimal`
Oczekiwane: wszystkie testy PASS, dwa testy sieciowe pominięte (skipped).

- [ ] **Krok 3: Uruchom testy sieciowe raz, ręcznie**

```bash
MIXCLOUD_LIVE=1 YTDLP_PATH="C:/Users/Marek/AppData/Local/Microsoft/WinGet/Links/yt-dlp.exe" dotnet test tests/Mixcloud.Core.Tests --filter LiveMixcloudTests -v:minimal
```

Oczekiwane: PASS. Jeśli padną, a testy jednostkowe przechodzą, oznacza to
zmianę po stronie Mixclouda — odśwież fixture'y w `tests/fixtures/`.

- [ ] **Krok 4: Napisz README**

`README.md` musi zawierać: wymagania (AIMP 5.4 x64, .NET Framework 4.8.1
Developer Pack, .NET SDK do budowania), polecenie budowania
(`dotnet build Mixcloud.sln -c Release`), polecenie wdrożenia
(`tools/deploy.ps1` jako administrator przy zamkniętym AIMP), sposób
uruchomienia testów, oraz opis dodawania nowego języka (skopiuj
`english.lng`, ustaw `LangId`, przetłumacz wartości — test spójności
pilnuje kompletu kluczy).

- [ ] **Krok 5: Commit**

```bash
git add -A && git commit -m "Test integracyjny na zywym Mixcloudzie i README"
```

---

## Pokrycie specyfikacji

| Wymaganie ze specyfikacji | Zadanie |
|---|---|
| Wtyczka ładuje się w AIMP 5.4 x64 | 1 |
| Struktura wdrożenia i wykrycie braku uprawnień | 1 |
| Rozstrzygnięcie trybu odtwarzania | 2 |
| Walidacja adresów, odrzucanie obcych domen | 3 |
| Tytuł ze slugu (flat nie ma tytułów) | 4 |
| Procesy z timeoutem, zabijanie, poza wątkiem UI | 5, 11 |
| Leniwe listowanie z twardym limitem | 6 |
| Selektor `http/hls-192/bestaudio` | 6 |
| Parsowanie na prawdziwych fixture'ach | 7 |
| Ustawienia: handle, limit, cache, auto-update | 8, 15 |
| Własna kopia yt-dlp, atomowa aktualizacja | 9 |
| Dwujęzyczność PL/EN + test spójności kluczy | 10 |
| Zero napisów na sztywno w kodzie | 10 |
| Nowa playlista nazwana `playlist_title` | 11 |
| Ulubione z handle'a | 11, 15 |
| Odtwarzanie strumieniowe | 12 |
| Fallback z katalogiem tymczasowym, limit i sprzątanie | 13 |
| Metadane leniwie | 14 |
| Obsługa błędów z komunikatami | 10, 11 |
| Test integracyjny opt-in | 16 |

Zadania 12 i 13 wykluczają się wzajemnie — o tym, które wykonać, decyduje
wynik zadania 2.
