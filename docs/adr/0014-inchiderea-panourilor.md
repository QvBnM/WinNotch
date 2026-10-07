# ADR 0014 — Închiderea panourilor din notch

- **Stare:** Acceptată
- **Data:** 2026-10-07
- **Sarcina din roadmap:** P51

## Context

Panourile deschise în notch (ieșire audio, raft, galeria de widget-uri, mărimile unui widget) se închideau doar prin
butonul lor „Închide”. Un click în altă parte le lăsa deschise, iar `Esc` nu închidea nimic (în afară de Command Bar și
de selecția de regiune, care au propriile mecanisme). Fiecare panou își ținea singur starea, iar pop-up-ul de mărimi
închis de fundalul lui întunecat nu anunța pe nimeni: `_sizes` și `_popup` din `NotchWindow.Pages.cs` rămâneau
referințe moarte.

Constrângeri:

- Fereastra notch-ului e `WS_EX_NOACTIVATE | WS_EX_TRANSPARENT | WS_EX_LAYERED`: **nu primește focus** și un click dat
  în afara ei nu ajunge niciodată ca eveniment WPF.
- Fără polling sub 2 s în standby și fără cronometre noi (regulile 0.7+).
- Nimic nu are voie să fure `Esc` din aplicația în care lucrează utilizatorul.
- Plasa de siguranță (`NotchGuardOverlays`, B1) cunoaște panourile pe nume; un sistem paralel ar raporta „alte-N”.

## Decizie

- Deciziile stau într-o clasă pură, `Core/Ui/OverlayStack.cs`: `Register` / `Close` / `CloseAll` / `Topmost` /
  `OnOutsideClick(insideId)` / `OnEscape` / `NeedsKeyboard` / `NeedsEscape`. Fără WPF, fără timere, fără hit-test
  (apelantul spune în ce panou a căzut click-ul). Trei niveluri: `Panel` (un panou nou închide celelalte panouri),
  `Hint` (quick actions, nota paginii standard — nu sunt închise de un panou și nu iau `Esc`), `Modal` (Command Bar,
  mereu deasupra). Închiderea primește un motiv (`OverlayClose`), care ajunge în log ca rând scurt.
- Legătura cu fereastra e în `Features/Overlays/NotchWindow.Overlays.cs`: un singur `PreviewMouseDown` pe fereastră
  (adăugat doar cât comutatorul e pornit, scos la `Cleanup`) spune în ce panou a căzut click-ul, iar click-ul din afara
  ferestrei și `Esc` se citesc cu `GetAsyncKeyState` **din `PollTick`-ul existent de 30 ms**, numai cât teancul nu e
  gol (`Esc` doar cât e deschis un panou, nu un indiciu) și numai cât Command Bar-ul nu e deschis (acolo e focus real
  și bara își închide singură fereastra la `Esc`).
- Închiderea trece mereu prin rutina existentă a panoului (`AudioSwitchHidePanel`, `ShelfHidePanel`,
  `RemoveQuickActionsRow`, `CloseOverlaysCore`), nu prin `Children.Remove`, ca marginile și înălțimea panoului să se
  refacă exact ca înainte.
- `Gallery.ShowPopupIn` primește `onClosed` și se închide o singură dată: apelantul își curăță starea indiferent cine
  a închis pop-up-ul (butonul, fundalul, `Esc`).
- Id-urile panourilor sunt exact numele folosite de plasa de siguranță: „galerie”, „mărimi”, „notă”, „raft”,
  „ieșire-audio”, „quick-actions”.
- Comutator: `overlay-dismiss` (Beta, pornit implicit, oprit în `--safe-mode`). Oprit: nimic nu se înregistrează, nu se
  citește nicio tastă și niciun click, panourile se închid doar cu butonul lor.

## Alternative

- **`RegisterHotKey` pe `Esc`** — respinsă: ar fura tasta la nivel global, în toate aplicațiile, inclusiv când WinNotch
  nu are nimic deschis.
- **`SetWindowsHookEx` (hook de tastatură)** — respinsă: un hook global pe tastatură e exact tipul de componentă care
  arată ca un keylogger, cere grijă la fiecare apăsare și ar încălca spiritul regulilor de securitate (secțiunea 14 din
  `DOCUMENTATIE.md`).
- **Luarea focusului de către fereastra notch-ului** (ca să prindă `Esc` ca eveniment WPF) — respinsă: ar scoate
  tastatura din aplicația utilizatorului de fiecare dată când se deschide un panou.
- **Un cronometru nou doar pentru teanc** — respinsă: `PollTick` rulează deja la 30 ms cât notch-ul e deschis, iar cât
  e închis teancul e gol și prima linie a verificării iese imediat.
- **Câte un „backdrop” transparent pentru fiecare panou** (ca la pop-up-urile din galerie) — respinsă ca mecanism
  general: ar acoperi pagina, ar strica tragerile de fișiere peste notch (P23) și nu rezolvă click-ul din afara
  ferestrei.

## Consecințe

- Orice panou nou se închide „ca toate celelalte” dacă se înregistrează la deschidere și se scoate la închidere;
  altfel nu participă deloc (comportamentul vechi).
- Un panou nou trebuie să-și dea ca element de hit-test **cardul**, nu un fundal care acoperă tot stratul de overlay:
  altfel orice click din notch pare „în interiorul lui”.
- Rutina de închidere a apelantului poate apela `Close` din nou fără buclă (teancul marchează ce e în curs de
  închidere), dar nu trebuie să se bazeze pe ordinea apelurilor.
- `Esc` rămâne al aplicației utilizatorului cât timp nu e deschis niciun panou al notch-ului.
