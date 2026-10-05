# WinNotch — notes for AI coding assistants

WinNotch is a "Dynamic Island" for Windows 10/11: C# .NET 8 WPF, single self-contained exe, plus a browser extension
(`extension/`). The owner is Romanian: **talk to him in Romanian**; all user-facing text in the app is Romanian with
correct diacritics (ă â î ș ț). Code comments are in English.

## Where things are
- `DOCUMENTATIE.md` — full description of every feature (keep it up to date when behaviour changes).
- `AUDIT.md`, `AUDIT-2.md`, `AUDIT-3.md` — security/quality audits. Don't undo their fixes (see "Security rules").
- `NotchWindow.*.cs` — the notch (modes, alerts, pages, edit mode, updates); `EditorWindow.cs` — the WinNotch window
  (pages, themes, settings, news); `Widgets/` — widget grid 6×4, catalog, gallery; `Panes/` — standard pages;
  `Services/` — media, audio, browser bridge, temperatures (SYSTEM helper), updater, calendar, etc.
- `tests/` — C# tests (`dotnet run --project tests/WinNotch.Tests.csproj`) and extension tests (`node tests/extension/*.test.js`).

## Releasing a new version (automatic updates)
1. Bump `<Version>` in `WinNotch.csproj` (e.g. 0.6.6 → 0.6.7).
2. Rewrite `RELEASE_NOTES.md` for that version only, with these headings and one `- ` line per change, in Romanian,
   written for the user (what he sees, not code details):
   `## Nou`, `## Îmbunătățit`, `## Modificat`, `## Reparat` (omit empty ones).
3. Add a row to the version history table at the end of `DOCUMENTATIE.md` (and "Noutăți" in `README.md`).
4. Run the tests, commit, push to `main`.
GitHub Actions (`.github/workflows/release.yml`) then runs all tests, builds `WinNotch.exe`, signs it with the
`WINNOTCH_RELEASE_KEY` secret and creates the release `v<version>`. The installed app offers it in the notch with the notes.
Pushes that don't change the version only run the tests. Never put the private key in the repo.

## Security rules (don't regress)
- The app never needs to run as administrator; CPU temperature comes from the SYSTEM helper (`Services/TempHelper.cs`).
- Any process launch goes through `Services/Shell.Open`; system tools by full path (System32).
- Browser bridge: exact extension Origin + mutual HMAC handshake; never send the token itself; limits on sizes.
- Covers/images from web pages: only bytes sent by the extension, PNG/JPEG/WebP, size/pixel limits.
- Updates: only files whose ECDSA signature matches the public key in `Services/Updater.cs`, newer versions only.
- No secrets, clipboard text, URLs or titles in `log.txt`; sensitive settings via DPAPI (`Secret`).

## Style
- Keep UI smooth and rounded (radius 16–18, theme brushes via `DynamicResource` / `SetResourceReference`, no hard-coded colours in the notch).
- The notch window is a transparent layered window: keep it small; avoid per-frame work when closed.

## Reguli pentru dezvoltarea 0.7+
Versiunile 0.7 → 1.0 sunt construite pe pași (ID-uri P00, P10, P11… în `docs/ROADMAP.md`). Arhitectura e în
`docs/adr/0001-arhitectura-0.7.md`.
- **Cod nou doar în foldere noi:** `Core/` (infrastructura: Context, Activity, Actions, Flags) și `Features/<NumeFuncție>/`.
  Fișierele mari existente (`NotchWindow.xaml.cs`, `EditorWindow.cs`) se ating minim: doar puncte de legătură de câteva rânduri.
- **Orice funcție nouă are comutator (feature flag),** oprit implicit până la versiunea în care e anunțată.
- **Nu duplica sisteme:** înainte să creezi un serviciu, o alertă, o setare sau un registru, caută dacă există deja
  (`Services/`, `AppSettings.cs`, alertele din `NotchWindow.xaml.cs`, `Widgets/Catalog.cs`).
- **Teste:** fiecare schimbare vine cu teste automate în `tests/` care trec cu `tests\run-tests.bat`; fiecare bug reparat
  vine cu un test care eșua înainte de reparație.
- **Nimic nu rulează elevat;** regulile din secțiunea 14 a `DOCUMENTATIE.md` (și „Security rules” de mai sus) rămân obligatorii.
- **Fără polling sub 2 secunde în standby;** preferă evenimentele Windows (WinEvent hooks, notificări WinRT, WMI events).
- **UI doar pe Dispatcher;** fiecare abonare la un eveniment are dezabonarea ei (la oprirea funcției sau la închiderea ferestrei).
- **La finalul fiecărei sarcini:** actualizează `DOCUMENTATIE.md`, `docs/ROADMAP.md` (starea sarcinii),
  `docs/TESTE-MANUALE.md` (verificările noi) și, dacă e o decizie de arhitectură, scrie un ADR (`docs/adr/`, după `0000-template.md`).
- **Definition of Done:** build fără avertismente noi, toate testele trec (C# și extensie), documentația e actualizată,
  iar lista de verificări manuale noi e scrisă în `docs/TESTE-MANUALE.md`.
- CI (`.github/workflows/ci.yml`) rulează build-ul și testele la fiecare pull request și push pe alte ramuri decât `main`.
