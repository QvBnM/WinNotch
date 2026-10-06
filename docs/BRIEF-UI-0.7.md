# Brief UI 0.7 — notch ancorat, închidere uniformă, fereastra WinNotch

Document de lucru pentru agentul tehnic. Trei sarcini independente, în ordinea asta: **P50**, **P51**, **P52**.
Fiecare: ramură proprie, comutator propriu, teste, revizie R1, apoi merge în `main`. Regulile din `CLAUDE.md`
(flags, `Core/` + `Features/`, teste, fără polling sub 2 s, UI pe Dispatcher, nimic elevat) rămân obligatorii.

**Limbă:** tot textul văzut de utilizator în română cu diacritice; comentariile în cod în engleză.

---

## Direcția de design (se aplică la toate trei)

Ideea care leagă totul: **WinNotch nu e o fereastră care plutește peste Windows, e o prelungire a ramei monitorului.**
Notch-ul crește din marginea de sus; fereastra mare e „același obiect, desfăcut”.

**Culori:** exclusiv jetoanele temei din `Themes.cs` (`NotchBrush`, `ChipBrush`, `ChipHoverBrush`, `InkBrush`,
`MutedBrush`, `DimBrush`, `AccentBrush`, `TrackBrush`, `BorderBrush`, `HoverBrush`, `SegBrush`, `OkBrush`, `WarnBrush`,
`HotBrush`, `InfoBrush`, `OnAccentBrush`). Nicio culoare fixă în cod nou. Dacă lipsește un jeton, se adaugă în
`ThemeManager.Keys` **și** în toate cele 6 teme, nu se inventează local.

**Accente colorate pe categorii** (ca în mockup): nu culori noi, ci `AccentBrush` / `InfoBrush` / `OkBrush` /
`WarnBrush` folosite ca „familie” a cardului — iconiță colorată pe fundal din aceeași culoare la ~12% opacitate,
restul cardului rămâne neutru. Maximum o culoare per card.

**Geometrie:** raza 18 pentru carduri și panouri, 12 pentru controale mici, 999 (pastilă) pentru butoanele de acțiune.
Spațiere pe o scară de 4: 4 / 8 / 12 / 16 / 24 / 32.

**Tipografie:** rămâne Segoe UI Variable. Scară fixă: 20/600 titlu de pagină, 15/600 titlu de secțiune,
13.5/400 corp, 12/400 secundar, 11/600 majuscule cu 0.6 px tracking pentru etichete („TOATE UNELTELE”).
Majusculele doar pentru etichete de grup, niciodată pentru conținut.

**Mișcare:** 160 ms ease-out pentru apariții, 120 ms pentru dispariții, 460 ms doar pentru schimbarea formei
notch-ului (cea existentă). Nimic care pulsează sau se mișcă în repaus.

---

## P50 — Notch ancorat de ramă

**Comutator:** `notch-anchored`, `FeatureStage.Experimental`, `DefaultOn = false`.
Cod nou în `Features/NotchAnchored/`. În `NotchWindow.xaml.cs` doar legături de câteva rânduri.

### Ce se schimbă vizual

1. **Lipit de marginea de sus.** `Pill.Margin.Top = 0` în toate modurile (azi 8 în Idle/Live/Expanded,
   `NotchWindow.xaml.cs` → `ApplyMode`, variabila `top`). Fereastra e deja poziționată la `t.Bounds.Top`.
2. **Colțuri doar jos.** `CornerRadius(0, 0, R, R)`, `R = Clamp(S.CornerRadius, 12, 28)`. Sus rămâne drept.
3. **Racordări concave („urechi”)** în stânga și dreapta sus — asta face contopirea cu rama. Vezi geometria mai jos.
4. **Fundal opac.** În modul ancorat, `BgOpacity` se citește dar se limitează la minimum 0.92; transparența rupe iluzia.
5. **Umbră doar în jos:** `DropShadowEffect` cu `Direction = 270`, `ShadowDepth = 6`, `BlurRadius = 24`, `Opacity = 0.5`,
   `RenderingBias = Performance`. Fără umbră în sus (ar desena o linie peste ramă).
6. **Animațiile cresc de sus în jos:** înălțimea rămâne animată ca acum, dar fără deplasare pe Y (azi `Margin` animată
   de la 8 la 0 face un mic salt).

### Geometria (exact)

Pastila nu mai e `Border`, ci un `Path` cu `StreamGeometry`, într-un `Grid` care o conține. Coordonate locale,
originea în colțul din stânga-sus al pastilei; `W` = lățimea pastilei, `H` = înălțimea, `R` = raza de jos,
`E` = raza urechilor (`E = Clamp(R * 0.75, 10, 22)`).

```
M (-E, 0)
A  rază E, centru (-E, E)   → (0, E)        // urechea stângă, concavă
L  (0, H-R)
A  rază R, centru (R, H-R)  → (R, H)        // colț jos-stânga, convex
L  (W-R, H)
A  rază R, centru (W-R, H-R)→ (W, H-R)      // colț jos-dreapta, convex
L  (W, E)
A  rază E, centru (W+E, E)  → (W+E, 0)      // urechea dreaptă, concavă
Z                                            // închidere pe marginea de sus (y = 0)
```

- Urechile ies în afara lățimii pastilei cu `E` de fiecare parte: fereastra (820 × scară) e mai lată decât pastila
  (max 720), deci există loc. Dacă `W + 2E` depășește fereastra, `E` se reduce proporțional.
- Geometria se reconstruiește în `SizeChanged` (nu per cadru), se face `Freeze()`, iar `Inner.Clip` folosește
  aceeași geometrie, ca fundalul colorat al copertei să fie tăiat corect.
- `PillScreenRect()` și `Inside(...)` din `NotchWindow.xaml.cs` trebuie să țină cont de urechi: zona de hover rămâne
  dreptunghiul pastilei (fără urechi), altfel notch-ul se deschide din colțuri.

### Lățimea în standby

Cerința utilizatorului: „să fie oarecum vizibil, dar nu încurce”.
- Standby normal: lățimea de acum (`IdleWidth()`), dar **minim 240** și **maxim 520**.
- Formă mică: lățimea de acum (minim 196), nemodificată.
- Peste o fereastră maximizată rămâne comportamentul actual (forma mică) — el rezolvă deja „să nu încurce”.

### Legături (maximum 3 locuri în cod vechi)

- `ApplyMode` — `top` devine 0 și raza/forma vin din `Features/NotchAnchored/`.
- `ApplyRadius` / `UpdateClip` — folosesc geometria nouă când comutatorul e pornit.
- `IdleWidth()` — limitele de mai sus.

Cu comutatorul oprit, totul arată exact ca azi (pastilă plutitoare, margine 8, colțuri complete).

### Teste (`tests/NotchAnchoredTests.cs`, fără WPF)

Clasa de reguli e pură (`AnchoredGeometry`), testele verifică:
1. Punctele geometriei pentru cazuri tipice (W=300/H=34, W=720/H=360) și că figura e închisă.
2. `E` se reduce când `W + 2E > lățimea ferestrei`.
3. Raza respectă `S.CornerRadius` și limitele 12–28.
4. Opacitatea fundalului nu coboară sub 0.92 în modul ancorat, dar respectă setarea în modul vechi.
5. `IdleWidth` limitat la 240–520; forma mică neatinsă.
6. Comutatorul oprit → aceleași valori ca azi (test de non-regresie).

### Verificări manuale (`docs/TESTE-MANUALE.md`)

- Notch-ul atinge marginea de sus pe fiecare monitor și la fiecare scalare (100 %, 125 %, 150 %).
- Urechile se văd la deschidere și la închidere, fără „scânteieri” în animație.
- Pe un fundal deschis (desktop alb) marginea de sus nu lasă o linie de 1 px.
- Hover-ul nu se declanșează din colțurile urechilor.

---

## P51 — Închidere uniformă a panourilor

**Problema raportată:** „Ieșire audio” rămâne deschis la click în altă parte. La fel: raftul, quick actions,
galeria de widget-uri, nota paginii standard. Esc nu închide nimic (în afară de Command Bar și selecția de regiune).

**Comutator:** `overlay-dismiss`, `FeatureStage.Beta`, `DefaultOn = true` (e o reparație de comportament, nu o
funcție nouă; comutatorul există ca plasă). Cod nou în `Core/Ui/OverlayStack.cs` (pur, testabil) +
`Features/Overlays/NotchWindow.Overlays.cs` (legarea la WPF).

### Regula, pe scurt

Un panou deschis se închide prin **oricare** dintre: butonul lui „Închide”, click oriunde în afara lui (în fereastră
sau pe ecran), tasta Esc, deschiderea altui panou de același nivel, închiderea notch-ului, intrarea în modul editare.

### Arhitectura

`OverlayStack` (pur, în `Core/Ui/`):
- `Register(id, level, closeAction)` / `Close(id)` / `CloseAll(level)` / `Topmost`.
- `Level`: `Panel` (ieșire audio, raft, galerie, mărimi), `Hint` (quick actions, nota paginii standard),
  `Modal` (Command Bar). Deschiderea unui `Panel` închide celelalte `Panel`, nu și `Hint`-urile.
- `OnOutsideClick()` și `OnEscape()` returnează ce trebuie închis (doar cel de deasupra, nu tot teancul).
- Fără WPF, fără timere: decide doar *ce* se închide. Testele lucrează pe el.

`Features/Overlays/NotchWindow.Overlays.cs` leagă:
1. **Backdrop în fereastră.** Când se deschide primul `Panel`, se adaugă în `OverlayHost` un `Border` transparent
   (`Background = Brushes.Transparent`, ZIndex sub panou) care prinde click-urile din fereastră, în afara panoului.
   Se refolosește mecanismul existent din `Widgets/Gallery.cs:156` (`ShowPopupIn`), extins cu un callback `OnClosed`
   care curăță starea apelantului — azi `_sizes` și `_popup` din `NotchWindow.Pages.cs:211` rămân referințe moarte
   după un click în afară. **Reparația asta e obligatorie.**
2. **Click în afara ferestrei.** Fereastra notch-ului e `WS_EX_NOACTIVATE | WS_EX_TRANSPARENT`, deci click-ul din afară
   nu ajunge niciodată ca eveniment WPF. Se folosește mecanismul care există deja în `PollTick`
   (`NotchWindow.xaml.cs:340`): `Native.GetCursorPos` + `Inside(PillScreenRect(), p, 8)` +
   `Native.GetAsyncKeyState(0x01)` (tiparul de la linia 387). Dacă butonul stâng e apăsat și cursorul e în afara
   pastilei cât un `Panel` e deschis → `OnOutsideClick()`. Fără cronometru nou: se folosește polling-ul existent de 30 ms,
   doar cât un panou e deschis.
3. **Esc.** Nu se poate prinde fără focus. Se citește `Native.GetAsyncKeyState(0x1B)` în același `PollTick`,
   **numai cât `OverlayStack` are ceva deschis** (altfel nu se atinge tastatura deloc). Nu se folosește
   `RegisterHotKey` pe Esc (ar fura tasta global) și nu se ia focusul (ar scoate tastatura din aplicația userului).
4. **Închiderea trece prin rutinele existente**, nu prin `Children.Remove`: `RelayoutPanel()`, `PanelH()`,
   `PaneHost.Margin` (quick actions modifică marginea, `Features/QuickActions/NotchWindow.QuickActions.cs:191`).
5. **Compatibilitate cu NotchGuard:** panourile rămân cu numele lor cunoscute în
   `NotchGuardOverlays()` (`Features/NotchGuard/NotchWindow.NotchGuard.cs:292`), ca plasa să nu raporteze „alte-N”.

### Unde se leagă (din inventarul codului)

| Panou | Fișier:linie | Ce lipsește azi |
|---|---|---|
| Ieșire audio | `Features/AudioSwitch/NotchWindow.AudioSwitch.cs:196` | click în afară, Esc |
| Raft | `Features/Shelf/NotchWindow.Shelf.cs:403` | click în afară, Esc |
| Quick actions | `Features/QuickActions/NotchWindow.QuickActions.cs:190` | click în afară (nivel `Hint`) |
| Galeria de widget-uri | `NotchWindow.Pages.cs:238` | nu are backdrop deloc |
| Popup de mărimi | `NotchWindow.Pages.cs:274` | are click în afară, dar lasă `_sizes`/`_popup` moarte |
| Nota paginii standard | `NotchWindow.Pages.cs:189` | nivel `Hint`, se închide la Esc |
| `CloseOverlays()` | `NotchWindow.Pages.cs:208` | devine „închide tot ce e deschis”, prin `OverlayStack` |
| Popup-uri în fereastră | `EditorWindow.cs:459`, `Widgets/Gallery.cs:164` | Esc (acolo fereastra are focus: `PreviewKeyDown`) |

În `EditorWindow` se folosește `PreviewKeyDown` pe fereastră pentru Esc — acolo e focus real, nu e nevoie de polling.

### Teste (`tests/OverlayStackTests.cs`)

1. Un `Panel` deschis peste altul îl închide pe primul; un `Hint` nu închide `Panel`-ul.
2. Click în afară închide doar panoul de deasupra.
3. Esc închide de sus în jos, câte unul.
4. Închiderea notch-ului / intrarea în editare închid tot.
5. `Close(id)` al unui panou deja închis nu face nimic și nu aruncă.
6. După închiderea prin click în afară, apelantul primește `OnClosed` (testul care pică azi pentru `_sizes`).
7. Polling-ul de tastatură se cere doar cât teancul nu e gol (test pe regulă, nu pe timer).

### Verificări manuale

- Ieșire audio deschisă → click pe pagina din notch → se închide. La fel raftul, galeria, mărimile.
- Esc închide panoul de deasupra, apoi al doilea, apoi nimic (notch-ul rămâne deschis).
- Esc apăsat fără niciun panou deschis **nu** face nimic (nici în alte aplicații).
- Quick actions nu dispar când deschizi ieșirea audio, dar dispar la click pe pagină.

---

## P51b — O alertă nu stă în calea unei acțiuni

**Problema raportată:** era afișată alerta „Pauză pentru ochi”; utilizatorul a început un drag cu fișiere pentru Raft,
iar notch-ul a rămas pe alertă — nu s-a transformat în țintă de drop. Regula lipsește în general: modul `Live` (alertă)
blochează interacțiunea, oricare ar fi ea.

**Regula:** o alertă e informație, nu o stare. Orice intenție clară a utilizatorului are prioritate și alerta
se dă la o parte imediat (fără animația de 140 ms de ieșire, direct `EndLive()`), apoi se execută acțiunea.

**Intenții care întrerup o alertă:**
1. **Drag cu fișiere deasupra pastilei** → `EndLive()` și se deschide notch-ul ca țintă pentru Raft
   (`Features/Shelf/NotchWindow.Shelf.cs:134` `ShelfDragHover` e azi apelat doar pe ramura Idle din `PollTick`;
   trebuie să fie verificat și când `_mode == Mode.Live`).
2. **Hover intenționat** peste pastilă (dwell-ul obișnuit) → se deschide notch-ul, alerta nu mai „ține” pastila.
   Excepție: alertele interactive la care utilizatorul trebuie să apese ceva (actualizare, memorie plină, pauză pentru
   ochi, confirmări) — acolo hover-ul nu le închide, dar un click în afara lor da.
3. **Scurtătura `Win+Alt+N`**, Command Bar (`Win+Space`) și orice acțiune din tray → închid alerta și execută.
4. **Deschiderea unui panou** (ieșire audio, raft) → alerta dispare.

**Nu întrerup:** mișcarea obișnuită a mouse-ului pe ecran, tastatul în altă aplicație, o altă alertă de prioritate mai
mică (aceea se așază la coadă, cum face deja `ActivityManager`).

**Ce rămâne din alerta întreruptă:** nimic pe ecran, dar dacă avea o acțiune nefăcută (ex. „Actualizează”), ea se oferă
din nou mai târziu, prin mecanismul existent de amânare. Pauza pentru ochi se consideră sărită, ca la butonul „Sari”.

**Unde se leagă:** `PollTick` (`NotchWindow.xaml.cs:340`) — ramura `_mode == Mode.Live` nu testează azi nici dwell-ul,
nici drag-ul; `ShowLive` / `EndLive` (`NotchWindow.xaml.cs:874`, `:891`); `Features/Activity/` pentru prioritate.

**Teste:** reguli pure (`Core/Ui/InterruptRules.cs`): pentru (tip alertă, intenție) → întrerupe / nu întrerupe;
alertele interactive nu se închid la hover, dar se închid la drag și la scurtătură; o alertă întreruptă nu se
re-afișează imediat (anti-buclă).

**Verificare manuală:** pornește pauza pentru ochi, trage un fișier peste notch → notch-ul se deschide cu Raftul gata
de drop. Repetă cu alerta de volum și cu cea de actualizare.

## P52 — Fereastra WinNotch, redesign

**Comutator:** `window-v2`, `Experimental`, `DefaultOn = false`, până e gata. Cod nou în `Features/WindowV2/`.
`EditorWindow.cs` rămâne ca variantă veche până la anunț; nu se rescrie în loc.

**Decizie de scop:** se construiește **doar cu funcțiile care există azi**. Nu apar carduri „în curând” și nu se
desenează funcții viitoare (Focus Mode, File Drop, Window Wizard, Color Picker, Automatizări, Dev Tools din mockup).
Layout-ul trebuie să suporte adăugarea lor mai târziu, fără rearanjare.

### Elementul de semnătură

**Antetul ferestrei e notch-ul, desfăcut.** Bara de sus a ferestrei folosește aceeași siluetă ca P50: lipită de
marginea ferestrei, colțuri doar jos, aceleași urechi concave, aceleași file (Acasă · Sistem · Dispozitive · Unelte).
Cine deschide fereastra recunoaște instant obiectul din marginea ecranului. Asta e singurul loc unde se cheltuie
îndrăzneala; restul ferestrei rămâne liniștit.

### Structura (3 coloane + antet)

```
┌──────────────────────────────────────────────────────────────────────────┐
│  ⌂ Acasă   ⚙ Sistem   ▭ Dispozitive   ⊞ Unelte        ☀  ⤢  ⇩   22:57   │ ← antet-notch
├────────────┬──────────────────────────────────────────┬──────────────────┤
│ TOATE      │  🔍 Ce vrei să faci?            Ctrl + K │  Clipboard       │
│ UNELTELE   │                                          │  3 elemente      │
│            │  ACȚIUNI RAPIDE                          │  ┌────────────┐  │
│ ▸ Acțiuni  │  ┌────────┬────────┬────────┬────────┐   │  │ …text…     │  │
│ ▸ Clipboard│  │ card   │ card   │ card   │ card   │   │  └────────────┘  │
│ ▸ Captură  │  └────────┴────────┴────────┴────────┘   │                  │
│ ▸ Ferestre │                                          │  Captură recentă │
│ ▸ Sistem   │  UNELTE                                  │  ┌────────────┐  │
│ ▸ Teme     │  ┌──────┬──────┬──────┬──────┐           │  │ miniatură  │  │
│ ▸ Pagini   │  │ …    │ …    │ …    │ …    │           │  └────────────┘  │
│ ▸ Setări   │  └──────┴──────┴──────┴──────┘           │  Confidențialit. │
│            │                                          │  Microfon  oprit │
│ + Pagină   │                                          │  Cameră    oprită│
├────────────┴──────────────────────────────────────────┴──────────────────┤
│  💡 Scrie ce vrei să faci, ex. „curăță clipboard-ul”      Win+Space       │
└──────────────────────────────────────────────────────────────────────────┘
```

- **Sidebar 240 px:** categorii, fundal `ChipBrush`, selecția cu bară de 3 px în `AccentBrush` în stânga și fundal
  `ChipHoverBrush`. Jos, paginile tale (ce există azi) cu „+ Pagină goală”.
- **Centru:** bara de căutare (`Ctrl+K`, deschide Command Bar-ul existent, **nu** un sistem nou), apoi grupuri de
  carduri. Card = iconiță 40 px pe fundal colorat 12 %, titlu 13.5/600, descriere 12/400 `MutedBrush`,
  acțiunea principală ca pastilă în josul cardului. Grilă fluidă: 4 coloane peste 1280 px, 3 sub, 2 sub 900.
- **Coloana dreaptă 300 px:** doar ce există: Clipboard (ultimele elemente), ultima captură, confidențialitate
  (microfon / cameră, din `PrivacyService`), surse audio. Se ascunde sub 1100 px lățime.
- **Bara de jos:** o singură sugestie contextuală + scurtătura.

### Reguli de implementare

- Fereastra ia tema aplicației (`ThemeManager`), nu paleta deschisă fixă de azi (`EditorWindow.cs:22-26`).
  Pe tema luminoasă trebuie să arate la fel de bine: se testează amândouă.
- Conținutul paginilor existente (pagini, teme, setări, noutăți) se mută în structura nouă **fără a schimba logica**:
  `BuildSettings()`, `BuildThemes()`, `BuildNews()` se refolosesc, doar ambalajul e nou.
- Fără capturi de ecran din mockup în cod: iconițele vin din fontul existent (`Ui.Icon`).
- Minim 900 × 600; tot ce e mai jos se așază pe o coloană.
- Tastatură: `Tab` parcurge sidebar → căutare → carduri; focusul se vede (contur 2 px `AccentBrush`).

### Teste

- Reguli pure în `Features/WindowV2/LayoutRules.cs`: câte coloane la o lățime dată, când se ascunde coloana dreaptă,
  ce categorie e selectată pentru o pagină dată. Teste în `tests/WindowV2Tests.cs`.
- Test de non-regresie: cu comutatorul oprit se deschide fereastra veche.

### Verificări manuale

- Antetul ferestrei și notch-ul din marginea ecranului arată ca același obiect (aceeași rază, aceleași urechi).
- Toate setările de dinainte se găsesc și funcționează.
- Tema luminoasă și cea întunecată: niciun text sub contrast 4.5:1.
- Fereastra la 900 px lățime: nimic tăiat, nicio bară de derulare orizontală.

---

## Ordinea și livrarea

1. **P51** + **P51b** primele (reparații, se simt imediat, risc mic).
2. **P50** al doilea (vizual, comutator, ușor de comparat).
3. **P52** ultimul (cel mai mare).

Fiecare pas: ramură, teste verzi (C# + extensie), R1, `docs/PROGRESS.md`, `docs/ROADMAP.md`, `DOCUMENTATIE.md`,
`docs/TESTE-MANUALE.md`, ADR dacă e decizie de arhitectură (sigur pentru P50 și P51).
Versiune nouă + `RELEASE_NOTES.md` doar când pasul e gata de testat de autor.
