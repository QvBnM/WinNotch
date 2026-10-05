# ADR 0007 — Command Bar: scurtătura, focusul și alertele cât e deschisă

- **Stare:** Acceptată
- **Data:** 2026-10-05
- **Sarcina din roadmap:** P14

## Context
Registrul de acțiuni (ADR 0003) are deja tot ce poate face WinNotch, cu căutare și verificări, dar nimic nu-l folosește.
P14 cere o bară de comenzi: o scurtătură transformă pastila în câmp de căutare, Enter pornește acțiunea, Esc închide, iar
tastatura revine la fereastra de dinainte. Constrângeri: notch-ul e o fereastră transparentă, `WS_EX_NOACTIVATE`, care nu
ia niciodată focusul de la sine; peste un joc pe tot ecranul nu are voie să fure tastatura; Activity Manager (ADR 0006) e
opțional, deci bara trebuie să meargă pe ambele căi ale alertelor, iar corpurile `ShowLive` / `EndLive` / `Collapse` sunt
fixate de testele de caracterizare; logica trebuie testată fără WPF; nicio scurtătură nouă în afară de cea a barei.

## Decizie
- **Logica fără WPF** în `Features/CommandBar/CommandBarModel.cs`, testată în `tests/CommandBarTests.cs`:
  `CommandBarHotkey` (înregistrare doar cu comutatorul pornit, schimbarea tastei, o singură alertă de conflict pe tastă și
  rulare; apelurile Win32 injectate), `CommandBarRules` (când deschide scurtătura), `CommandBarSearch` (text → rânduri peste
  `ActionRegistry.Search`, cuvintele de la final ca parametri verificați cu `ActionParameter.TryConvert`, fără acțiuni
  periculoase), `CommandBarSession` (selecția, Enter, confirmarea dublă), `CommandBarLayout` (mărimi).
- **Scurtătura:** `Win+Alt+Space` (implicit) sau `Win+Alt+K`, aleasă în Setări (`AppSettings.CommandBarKey`). Înregistrată cu
  id-ul 4 doar cât comutatorul „command-bar” e pornit, eliberată la oprire și la ieșire; abonare la `FeatureFlags.Changed`
  (UI prin Dispatcher, dezabonare la închidere). Dacă `RegisterHotKey` eșuează: **o singură alertă** prin calea obișnuită
  (`Alert(…)`, deci prin Activity Manager când e pornit), care o propune pe cealaltă și numește opțiunea din Setări; același
  mesaj apare sub opțiune. Nu trecem singuri pe `Win+Alt+K`: alegerea rămâne a utilizatorului. O alertă care nu poate fi
  arătată acum (notch deschis, ecran complet) e încercată din nou când pastila revine în standby.
- **Bara e „notch-ul deschis” fără panou:** la deschidere `_mode = Mode.Expanded`, ca în `Expand`, dar cu stratul barei în
  locul panoului (un rând în `ApplyMode`: `if (CommandBarApplyMode()) return;`). Consecința căutată: alertele se comportă
  **exact ca la notch-ul deschis, pe ambele căi** — calea veche (`ShowLive`) le refuză; Activity Manager le vede ca „notch
  deschis”: Critical și persistentele așteaptă și apar la închidere, restul nu apar. Nicio alertă nu ia pastila cât scrii.
  Închiderea trece mereu prin `Collapse` (Esc, Enter, click în altă parte, scurtătura, `Win+Alt+N`, o unealtă de ecran,
  comutatorul oprit), care dă tastatura înapoi și cheamă `ActivityNotchClosed`; hook-ul din `ApplyMode` scoate interfața
  barei. Interfața e construită la deschidere și scoasă din arbore la închidere; nimic nu rulează cât e închisă.
- **Focusul, ca la lansator:** `EnableTyping(_cmdBox)` la deschidere (scoate `WS_EX_NOACTIVATE`, `Activate`, focus), iar
  `Collapse` → `StopTyping` → `SetForegroundWindow(LastForeground)` la închidere. Înainte de deschidere, `LastForeground` e
  recitit cu aceeași regulă ca în `PollTick` (nu notch-ul, nu desktopul / bara de activități). La pierderea focusului
  (`Deactivated`) fereastra pe care ai dat click devine `LastForeground`, deci rămâne în față. Dacă Windows refuză
  `Activate` (scurtătura dă de obicei dreptul de a veni în față), bara folosește `Native.ForceForeground`, ca fereastra
  WinNotch. Enter închide bara **înainte** de a porni acțiunea, ca acțiunile pe fereastra activă să o găsească pe a ta.
- **Ecran complet:** scurtătura nu deschide nimic dacă fereastra din față acoperă monitorul sau e Direct3D exclusiv (citită
  cu `ForegroundSource.Read()` al motorului de context, fără hook), dacă motorul de context spune ecran complet sau dacă
  pastila e ascunsă pentru ecran complet; nici cât rulează o unealtă de ecran sau modul de editare. Regula e pură și testată
  unitar (CB7, CB8), nu în testele de fum (pe mașina de CI nu pornim un joc).
- **Siguranță:** acțiunile periculoase nu apar; una cu confirmare cere al doilea Enter („Apasă Enter din nou pentru a
  confirma”); editarea textului sau mutarea selecției anulează confirmarea. Pornirea e mereu
  `ActionRegistry.Current.InvokeAsync(id, args, ActionInvoker.CommandBar, …, confirmed)`. În log: doar stări fixe
  („Command Bar: deschis/închis”, scurtătura); registrul scrie id-ul și rezultatul, niciodată textul sau valorile.
- **Setările ca acțiuni:** `Features/CommandBar/SettingsActions.cs`, câte o acțiune `settings.<nume>` pentru fiecare opțiune
  din `SettingsWindow.xaml` (sigure, pe firul interfeței), care deschid fereastra WinNotch la Setări, derulată la controlul
  cu acel `x:Name` și cu focusul pe el (`SettingsWindow.Reveal`). Listele (standby, spații, accent, funcții noi) se deschid
  la secțiune. Id-urile setărilor Windows (`settings.bluetooth`, `.sound`, `.display`, `.wifi`, `.update`) nu sunt refolosite.
- **Teste de fum** (`tests/WinNotch.Smoke`, ambele rulări): cu comutatorul oprit `Win+Alt+Space` nu deschide nimic; pornit,
  scurtătura reală deschide bara peste o fereastră a testului; dacă scurtătura e ocupată pe mașina de CI sau nu ajunge în
  5 s, comanda de test `open-command-bar` trece prin același cod (`OnCommandBarShortcut`), iar rezultatul spune asta cu un
  avertisment. Se verifică: tastatura în bară, un rezultat pentru „volum”, o alertă care nu ia pastila, Esc + focusul înapoi
  (`GetForegroundWindow`), Enter pe `settings.position` care deschide fereastra WinNotch.

## Alternative
- **Un mod nou al notch-ului (`Mode.Command`):** mai curat ca nume, dar `ShowLive` (fixat de testele de caracterizare) blochează
  doar `Mode.Expanded`; ar fi trebuit schimbat corpul lui sau al lui `Alert`, iar Activity Manager ar fi avut nevoie de o stare
  nouă. Refolosirea lui `Expanded` dă exact regula „ca la notch-ul deschis”, fără să atingă calea alertelor.
- **O fereastră separată pentru bară:** focusul ar fi fost mai simplu, dar pastila ar fi rămas dedesubt, alertele ar fi
  continuat să o schimbe, iar mecanismul de focus ar fi fost al doilea, diferit de al lansatorului.
- **Activitățile puse la coadă cât e deschisă bara:** ar fi cerut o regulă nouă în Activity Manager și ar fi schimbat calea
  veche; „ca la notch-ul deschis” e deja regula cunoscută și testată.
- **Trecerea automată pe `Win+Alt+K` la conflict:** ar fi schimbat o setare fără să întrebe; o singură alertă care propune e
  mai sigur.

## Consecințe
- Cât e deschisă bara, `IsOpen` e adevărat: acțiunile care deschid notch-ul o găsesc închisă, pentru că Enter o închide întâi.
- Alertele obișnuite care vin cât e deschisă bara se pierd (ca la notch-ul deschis); doar Critical / persistentele așteaptă,
  și doar cu Activity Manager pornit. Game Mode (P44) și Quick Actions (P20) pot refolosi aceeași regulă.
- `NotchWindow.xaml.cs` are patru legături de câte un rând (WndProc, `ApplyMode`, `ApplySettings`, `Cleanup`), fixate de CB32.
- Orice opțiune nouă în Setări trebuie să primească o acțiune `settings.<nume>` (testul CB29 pică altfel).
- Scurtătura, textele „Command Bar: …” din log și id-urile de UI Automation (`WinNotchCommandBox`, `WinNotchCommandResult:<id>`)
  sunt folosite de testele de fum.

## Note după revizia R1
- Bara citește fereastra din față la cerere (`ForegroundSource.Read()`, o singură dată la apăsarea scurtăturii), doar pentru regula „nimic peste ecran complet”: trebuie să meargă și cu „context-engine” oprit. Nu pornește hook-ul sursei, iar titlul nu ajunge nicăieri. Excepție acceptată de la „Cum folosești contextul”, pct. 1.
- Dacă Windows refuză și `Activate`, și `ForceForeground`, bara rămâne deschisă fără tastatură; se închide cu scurtătura sau trecând cu mouse-ul peste ea și plecând. Nu o închidem singuri (testul de fum dă focusul cu un click după deschidere).
