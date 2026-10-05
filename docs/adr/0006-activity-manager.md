# ADR 0006 — Activity Manager: alertele notch-ului printr-un singur loc

- **Stare:** Acceptată
- **Data:** 2026-10-05
- **Sarcina din roadmap:** P13

## Context
Până la 0.6.13, fiecare alertă a notch-ului (27 în total: volum, piesă, încărcător, baterie, temperatură, RAM, capturi, OCR,
pauza pentru ochi, actualizări, extensia veche, `context.show`) apela direct `NotchWindow.ShowLive`, care înlocuia imediat
ce era afișat. Nu exista prioritate, coadă sau grupare: o piesă nouă acoperea „Baterie descărcată”, iar zece alerte într-o
secundă clipeau una peste alta. Funcțiile din planul suplu (P44 Game Mode, P46 activități live din browser) au nevoie de
activități persistente și de reguli comune. Constrângeri: aspectul alertelor existente nu se schimbă; cu comutatorul oprit,
codul vechi rămâne neatins; fără polling în standby; logica testabilă fără WPF.

## Decizie
- **`Core/Activity/` (fără WPF):** `Activity` (id, cheie de înlocuire, prioritate Critical/High/Normal/Low, durată,
  persistentă, cu butoane, mărime, titlu scurt, UI-ul apelantului ca `Payload` opac) și `ActivityManager`
  (`Post`, `Update`, `Touch`, `Dismiss`, `DismissAll`, `NotchClosed`), care publică o stare imutabilă `ActivityView`
  (None, Single, Peek, Split, Group). Totul sub un singur lock; prezentatorul e chemat după eliberarea lui; ceas și mediu
  (notch deschis, ecran complet) injectate. Un singur cronometru one-shot cât se vede o alertă; în standby, niciunul.
- **Regulile cozii:**
  - aceeași cheie (afișată, la coadă sau persistentă) → actualizare pe loc, fără loc nou (volumul tras, pașii unui flux:
    OCR „Citesc…” → „Text copiat”, progresul RAM → rezultatul, descărcarea → „refuzată” / „Instalez…”);
  - prioritate egală sau mai mare → înlocuiește alerta afișată (ca înainte); mai mică → așteaptă la coadă și apare după,
    dacă nu a așteptat mai mult de 10 s; coada are cel mult 50 de locuri (pleacă întâi cele Low, apoi cele mai vechi);
  - Critical întrerupe orice; cu notch-ul deschis, Critical și persistentele așteaptă închiderea, restul sunt aruncate (ca
    înainte); peste ecran complet doar High și Critical (ca „important” înainte);
  - **„N noutăți”:** peste 3 alerte Normal/Low cu chei diferite în 5 s → un singur rezumat; alertele rafalei sunt aruncate,
    se păstrează doar numărul; rezumatul crește cât continuă rafala, nu e interactiv (click-ul trece prin el, ca la alertele
    obișnuite) și expiră după 4 s. Actualizările aceleiași chei, alertele High/Critical și cele cu butoane nu se grupează;
  - **peek:** o activitate Low nu ia pastila: o lărgește cu 48 px, la înălțimea pastilei mici, 2 s, cu titlul ei;
  - **persistente:** stau până la `Dismiss`, în spatele alertelor (o alertă trece peste ele și apoi revin); una → pastila ei;
    două → **pastila împărțită** (stânga: prioritatea mai mare, apoi ordinea sosirii); cel mult 8 ținute minte;
  - alertele cu butoane: acum sau deloc (niciodată la coadă sau în rezumat), ca apelantul să știe dacă să oprească
    click-through-ul, exact ca după `ShowLive`.
- **Rutare la apel, nu în `ShowLive`:** fiecare apel de alertă a devenit `Alert(LegacyAlerts.X, …)` /
  `ToolAlert(LegacyAlerts.X, …)` / `ShowInteractive(LegacyAlerts.X, …)`, cu aceleași argumente literale. `Alert`
  (`Features/Activity/NotchWindow.Activity.cs`) are ca prim rând `if (!_activityOn || _activity == null) return
  ShowLive(…)`: cu comutatorul oprit, calea veche e exact cea de dinainte. Cu el pornit, alerta e transformată pur
  (`ActivityRouting.FromAlert`: același UI, mărime, durată; „important” → High, altfel Normal; cheia și „are butoane” din
  tabel) și trimisă managerului; prezentatorul desenează tot cu `ShowLive` (aspect identic), apoi oprește cronometrul vechi:
  managerul decide când se termină.
- **Legături în fișierele mari, câte un rând:** la finalul `EndLive` (`ActivityLiveEnded()`: un buton sau un `catch` a
  închis alerta → managerul trece la următoarea), la finalul `Collapse` (`ActivityNotchClosed()`) și în ramura „pe loc” din
  `LiveVolume` (`ActivityTouch`, înaintea cronometrului vechi). Toate sunt fără efect cu comutatorul oprit.
- **Fixarea căii vechi:** întâi, într-un commit separat, testele de caracterizare (`tests/AlertCharacterizationTests.cs`,
  tabelul `Features/Activity/LegacyAlerts.cs`): fiecare apel trimite mărimea, durata și „important” din tabel; corpurile
  `ShowLive`, `EndLive` (fără rândul de legătură) și `EndLiveInteractive` sunt neschimbate; fiecare limită de repetare
  (RAM 15 min + 20 s, temperatură 5 min, piesă 15 min / 10 s, baterie 20/10, încărcător, pauză ochi, volum 600 ms, oferta
  24 h, extensie, după actualizare / revenire) e încă în cod. Aceleași scenarii rulează pe un model al căii vechi și pe
  manager și cer același rezultat vizibil.
- **Comutatorul „activity-manager”** (Experimental, oprit implicit, oprit în `--safe-mode`); oprit din Setări → tot ce
  arăta managerul dispare, alertele următoare merg pe calea veche. Erorile → `FeatureFlags.ReportError` și alerta apare
  totuși, pe calea veche. Acțiunea `activity.dismiss-all` (legată de comutator).
- **Teste de fum:** toate rulează de două ori (`--activity-manager=off` / `on`); cu el pornit, în plus: pastila împărțită,
  „5 noutăți”, peek (starea UI Automation primește `;split=1`, `;group=N`, `;peek=1`); cu el oprit, comenzile de test
  ale managerului sunt refuzate fără erori în log.

## Ce a rămas pe calea veche și de ce
- **Toate cele 27 de alerte trec prin manager când comutatorul e pornit;** nimic nu a rămas exclusiv pe calea veche.
- Rămân în codul vechi, pe ambele căi: **limitele de repetare** (ele decid *dacă* se cere o alertă; managerul decide doar
  *cum* se arată), actualizarea pe loc a barei de volum (doar cronometrul ei trece la manager), butoanele alertelor și
  numărătoarea pauzei pentru ochi.
- Alertele cu butoane nu sunt puse la coadă: apelanții lor își schimbă starea (click-through, „oferit”, pauza următoare)
  imediat după răspuns, ca înainte.

## Alternative
- **Un rând de rutare în `ShowLive`:** schimbă mai puțin la apeluri, dar `ShowLive` nu știe ce alertă e (cheia, butoanele)
  și corpul lui n-ar mai fi „neatins”.
- **Coadă strictă (doar Critical întrerupe):** o bară de volum ar aștepta 45 s în spatele ofertei de actualizare; am păstrat
  înlocuirea de dinainte la prioritate egală sau mai mare.
- **Managerul desenează singur, fără `ShowLive`:** ar dubla codul de animație și ar risca diferențe de aspect.

## Consecințe
- Orice alertă nouă (de la P14 încolo) se cere prin `ActivityManager` sau prin `Alert(id, …)` cu un rând în `LegacyAlerts`
  (testul AC2 pică pentru un apel necunoscut).
- Cu comutatorul pornit: o alertă mai puțin importantă nu o mai acoperă pe una importantă (o piesă nouă așteaptă după
  „Baterie descărcată”); multe alerte deodată devin „N noutăți”.
- **Limitări cunoscute:** o activitate persistentă deja afișată rămâne vizibilă dacă pornește apoi un joc pe tot ecranul
  (ca o alertă importantă de azi; Game Mode, P44, o va ascunde); una Normal ascunsă de ecranul complet reapare abia la
  următoarea schimbare din manager. Mediul (notch deschis, ecran complet) e citit fără lock de pe firul cronometrului;
  prezentatorul verifică din nou pe firul interfeței.
