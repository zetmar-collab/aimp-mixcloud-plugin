Oficjalną dokumentację do tworzenia wtyczek dla **AIMP for PC** znajdziesz w pakiecie **AIMP SDK** na stronie producenta:  
**https://aimp.ru/?do=download&os=windows&cat=sdk**\[[aimp](https://aimp.ru/?do=download&os=windows&cat=sdk)\]

SDK zawiera dokumentację oraz nagłówki API potrzebne do budowania własnych pluginów. Wtyczki AIMP są zasadniczo bibliotekami DLL ładowanymi przez odtwarzacz, więc pakiet jest szczególnie przydatny przy pracy w C++ / Delphi i integracji z natywnym API.\[[aimp](https://aimp.ru/?do=download&os=windows&cat=sdk)\]

## Opcja dla .NET

Jeśli chcesz pisać wtyczki w C#, pomocny jest nieoficjalny wrapper **AIMP DotNet** wraz z dokumentacją API:

-   Repozytorium: https://github.com/martin211/aimp\_dotnet\[[github](https://github.com/martin211/aimp_dotnet)\]
-   Dokumentacja klas i interfejsów: https://martin211.github.io/aimp\_dotnet\_docs/api/AIMP.SDK.html\[[martin211.github](https://martin211.github.io/aimp_dotnet_docs/api/AIMP.SDK.html)\]
-   Pakiet NuGet: `AimpSDK` (dla 32-bit) lub `AimpSDK-x64` (dla 64-bit).\[[nuget](https://www.nuget.org/packages/AimpSDK)\]

Najlepszy punkt startu to pobranie oficjalnego SDK i sprawdzenie załączonych przykładów oraz plików nagłówkowych — będą zgodne z aktualną wersją AIMP.\[[aimp](https://aimp.ru/?do=download&os=windows&cat=sdk)\]