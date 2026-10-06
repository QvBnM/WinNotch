# ADR 0005 — Teste de fum pe exe-ul publicat (modul `--smoke`)

- **Stare:** Acceptată
- **Data:** 2026-10-05
- **Sarcina din roadmap:** P02

## Context
Testele automate de până acum (`tests/WinNotch.Tests.csproj`) rulează pe Linux, fără WPF: logica e acoperită, dar nimic nu
pornește aplicația reală. O versiune care cade la pornire, care nu mai arată notch-ul sau nu se mai închide curat ar trece
de CI și ar ajunge la utilizator prin actualizarea automată (protecția din ADR 0002 o prinde abia după, pe calculatorul lui).

## Decizie
- **Un proiect separat, `tests/WinNotch.Smoke`** (consolă .NET 8 pentru Windows, FlaUI.UIA3), rulat în `ci.yml` pe
  `windows-latest`, după `dotnet publish`, pe `publish\WinNotch.exe`. Eșecul face CI-ul roșu; captura de ecran și `log.txt`
  sunt păstrate ca artefacte. Codul: `SmokeProgram.cs` (pornirea, ordinea verificărilor, utilitarele comune, verificările de
  bază) și câte un fișier pe zonă, aceeași clasă `partial`: `SmokeAlerts.cs`, `SmokeCommandBar.cs`, `SmokeContext.cs`,
  `SmokeClipboard.cs` (fiecare adăugat în `WinNotch.Smoke.csproj`).
- **Argumentul `--smoke`** (`Features/Smoke/SmokeMode.cs`), citit primul la pornire:
  - folder de date separat, `%AppData%\WinNotch\smoke` (`AppSettings.Folder`), ca testul să nu atingă niciodată setările,
    log-ul sau `startup.json` adevărate și să pornească mereu „ca la prima rulare”;
  - fără căutarea actualizărilor și fără serviciul de temperatură (oprite la pornire; setările se salvează doar în folderul `smoke`);
  - „WinNotch rulează deja” devine codul de ieșire 3, fără fereastră de mesaj (celelalte ferestre de mesaj, pentru erori, rămân:
    în CI ar bloca testul, care pică la timpul maxim);
  - o repornire cerută de protecția la pornire (mod sigur, revenire) păstrează `--smoke`;
  - starea notch-ului publicată prin UI Automation: `AutomationId = WinNotchNotch` și `ItemStatus = "mode=Idle;pill=180x32"`
    (pastila e un `Border`, fără element propriu în UI Automation);
  - comenzi de test într-un fișier, `smoke-commands.txt`, citit la 500 ms **doar** în acest mod (excepție asumată de la regula „fără polling sub 2 s”: modul nu rulează niciodată la utilizator): `post-alert volume`,
    `post-alert track`, `toggle feature <id>` (id validat, cel mult 4 KB și 20 de linii; restul e ignorat). Alertele trec prin
    aceleași metode ca cele reale (`LiveVolume`, `ShowTrackAlert`), comutatorul prin `FeatureFlags.Set`.
- **Meniul iconiței** e deschis cu click dreapta pe iconița din bara de activități (inclusiv dintre iconițele ascunse); dacă
  bara nu poate fi citită pe mașina de CI, testul trimite ferestrei ascunse a iconiței exact mesajul pe care Windows îl
  trimite la click dreapta. Elementul din meniu e apăsat în ambele cazuri prin UI Automation.
- **Log-ul** pică testul (`SmokeMode.IsFatalLogLine`) pentru excepții neprinse, porniri eșuate, erori raportate de funcții sau
  prinse de comutatoare din abonați, opriri automate de funcții, comenzi de test eșuate și stive de apeluri; alte
  rânduri care numesc o excepție tratată (un serviciu care lipsește pe mașina de CI) sunt doar afișate.

## Alternative
- **WinAppDriver / Appium:** încă un serviciu de instalat pe runner, nemaiîntreținut; FlaUI e o bibliotecă NuGet.
- **Comenzi printr-un pipe sau un port local:** încă o suprafață de atac deschisă în aplicație; fișierul din folderul
  propriu, citit doar cu `--smoke`, nu deschide nimic nou.
- **Același folder de date ca aplicația:** testul ar strica setările dezvoltatorului și ar număra porniri în `startup.json`.

## Consecințe
- Textele din meniul iconiței („Pagini, teme și setări…”, „Ieșire”), modurile notch-ului și `Win+Alt+N` sunt folosite de
  test: o schimbare la ele schimbă și `tests/WinNotch.Smoke`.
- `release.yml` rulează aceleași teste de fum pe exe-ul care urmează să fie semnat (din 0.6.13, revizia R1): un push direct
  pe `main` nu poate publica o versiune care pică testele.
- „Ieșire” trebuie să lase `startup.json` cu `Running = false` (ieșire curată marcată); altfel testul pică.
- Cine are deja acces la contul utilizatorului poate porni `WinNotch.exe --smoke`, dar nu câștigă nimic: modul folosește alt
  folder și comenzile doar arată alerte sau schimbă comutatoare în acel folder.
