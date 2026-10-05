# ADR 0008 — Pagina după context: decizia la deschidere, alegerea manuală și testul de fum

- **Stare:** Acceptată
- **Data:** 2026-10-05
- **Sarcina din roadmap:** P27

## Context
P27 cere ca notch-ul să se deschidă pe pagina potrivită pentru ce faci acum (Setări: categorie de context → pagină), dar
să respecte 10 minute o pagină aleasă de mână. Constrângeri: contextul vine doar din motorul de context (ADR 0004), fără
surse noi și fără polling; o pagină ascunsă sau ștearsă nu trebuie să dea erori; `NotchWindow.xaml.cs` și
`NotchWindow.Pages.cs` se ating doar cu legături de un rând; logica se testează fără WPF; testul de fum trebuie să verifice
calea reală, deși pe mașina de CI nu putem porni VS Code sau Teams.

## Decizie
- **Decizia la deschidere, nu la schimbare.** `Expand` cheamă `ContextPagesOnOpen()` (un rând, înainte de
  `_mode = Mode.Expanded`, ca pagina să fie schimbată înainte să se arate). Acolo se citesc, o singură dată, comutatorul
  (`FeatureFlags.Current.IsEnabled("context-pages")`) și `ContextEngine.Current?.Snapshot` (null → `Empty`). Nu ne abonăm
  la `ContextEngine.Changed`: cu notch-ul închis pagina nu se vede, deci nu are de ce să se schimbe, iar cu el deschis o
  schimbare de pagină sub mouse ar fi deranjantă. Comutatorul nu are nimic de pornit sau oprit, deci nici abonare la
  `FeatureFlags.Changed`.
- **Logica pură** în `Features/ContextPages/ContextPages.cs`: `ContextPageRules.Resolve(categorie, mapare, pagini vizibile,
  ultima alegere manuală, acum)` → id-ul paginii sau null („nicio schimbare”); `EffectiveCategory(snapshot)`: întâlnirea în
  curs are prioritate (și Meet în browser), apoi un joc pe tot ecranul (`Fullscreen == Game`, pentru jocurile pe care tabelul
  nu le știe), apoi categoria aplicației din față; `ContextPageChooser` ține ora ultimei alegeri manuale, cu ceasul injectat.
- **Alegerea manuală** = click pe un tab al notch-ului (legătura de un rând din `RebuildTabs`, doar când pagina chiar se
  schimbă; schimbările făcute de cod nu contează). Fereastra e de 10 minute, cu limita deschisă: 9:59 o respectă, 10:00 nu.
  Ora e în memorie (după repornire, contextul alege din nou).
- **Maparea** în `AppSettings.ContextPages` (fișier parțial nou, `ContextPagesSettings.cs`): numele categoriei (`Dev`…) →
  id-ul paginii (`home`, `system`, `devices`, `tools` sau id-ul paginii tale), nu indicele. Cheie lipsă = „—”. La citire,
  cheile necunoscute, `Other`, valorile goale sau prea lungi sunt scoase. Dicționarul nu e schimbat pe loc, ci înlocuit
  (o salvare de pe alt fir poate citi vechiul dicționar). Paginile ascunse sau șterse nu sunt în lista paginilor vizibile:
  sunt sărite în tăcere, fără log.
- **Testul de fum injectează contextul în motor**, nu într-o variabilă separată: `ContextEngine.ForceCategoryForSmoke`
  (internal) face ca aplicația din față să fie citită ca o categorie dată și trece prin flush-ul obișnuit (debounce,
  `Changed`, `Snapshot`). E apelată doar din `NotchWindow.Smoke.cs`, la comanda `fake-context <categorie|none>`, care există
  doar cu `--smoke` (testul CP27 fixează asta). `set-context-page <categorie> <pagină|none>` salvează maparea ca Setările.
  Starea pentru UI Automation primește la final `;page=<id>` și `;ctx=<categorie din snapshot>`. Se verifică: oprit →
  pagina veche; pornit → `Win+Alt+N` (calea reală `ToggleByHotkey` → `Expand` → legătura) deschide pe pagina mapată.
  **Rulează o singură dată**, în rularea cu „activity-manager” oprit (funcția nu atinge alertele); cealaltă scrie `SKIP`.
  `ci.yml` și `release.yml` nu se schimbă.

## Alternative
- **Abonare la `ContextEngine.Changed` și schimbarea paginii cu notch-ul deschis:** pagina s-ar muta sub cursor la un Alt+Tab;
  în plus, un abonat permanent fără folos cât notch-ul e închis.
- **Mapare pe indici de pagină:** s-ar strica la reordonarea, ascunderea sau ștergerea paginilor.
- **Fereastra manuală resetată la fiecare schimbare de context:** mai complicat de explicat; specificația cere simplu „10 minute”.
- **Un context fals citit direct de notch în modul `--smoke`:** testul n-ar mai trece prin motorul de context, adică exact
  prin partea pe care vrem să o verificăm.
- **O sursă falsă de aplicație din față (un proces numit `code.exe` pornit de test):** depinde de focus și de hook-ul Windows
  pe mașina de CI; injecția în motor e deterministă.

## Consecințe
- Funcțiile viitoare care vor să știe „ce face utilizatorul” pentru o decizie punctuală pot citi `Snapshot` la cerere, ca aici,
  fără abonare.
- `ForceCategoryForSmoke` e doar pentru testele de fum: orice alt apelant face testul CP27 să pice.
- Textul „Pagina după context: <categorie>.” din log, comenzile `fake-context` / `set-context-page` și câmpurile `;page=` /
  `;ctx=` sunt folosite de testul de fum.
- Opțiunea nouă din Setări are acțiunea `settings.context-pages` (secțiune, ca listele).
