# WinNotch — documentație completă

Versiune: **0.6.10**. Ultima actualizare: 5 octombrie 2026.

WinNotch este un „Dynamic Island” pentru Windows 10 și 11: o pastilă neagră în partea de sus a ecranului.
- **Cât e închisă,** arată informații scurte.
- **La hover,** se deschide cu animație în panouri pentru muzică, sistem, dispozitive și unelte.
- **Când se întâmplă ceva** (volum, piesă nouă, baterie, temperatură, memorie plină), afișează alerte scurte.
- **Se personalizează** cu pagini proprii din widget-uri (ca pe iPhone) și cu teme întunecate, luminoase sau automate.

Documentul descrie tot ce e implementat în cod până la versiunea 0.6.10: paginile din widget-uri, editarea în notch, fereastra WinNotch (pagini, teme, setări) și temele sunt în secțiunea 16.

---

## Cuprins

1. Tehnologie și structură
2. Instalare, build și dezvoltare
3. Fereastra notch-ului: comportament general
4. Standby: pastila închisă
5. Alerte scurte (live activities)
6. Pagina Acasă
7. Sursele audio și extensia de browser
8. Pagina Sistem și testul de viteză
9. Pagina Dispozitive
10. Pagina Unelte
11. Setări
12. Iconița din zona de notificări
13. Date, fișiere și confidențialitate
14. Securitate
15. Calitate, teste și audit
16. Widget-uri, pagini și teme
17. Limitări cunoscute
18. Istoricul versiunilor

---

## 1. Tehnologie și structură

| | |
|---|---|
| Limbaj și platformă | C#, .NET 8, WPF (`net8.0-windows10.0.19041.0`, rulează pe Windows 10 1809+) |
| Livrare | un singur `WinNotch.exe` self-contained (nu cere .NET instalat ca să ruleze) |
| Biblioteci | NAudio.Wasapi 2.2.1 (sunet), LibreHardwareMonitorLib 0.9.6 (temperaturi), System.Management 10.0.2 (WMI) |
| API-uri Windows | Media Controls (WinRT), Core Audio (WASAPI), OCR (Windows.Media.Ocr), Bluetooth (WinRT), registru ConsentStore, Win32 (ferestre, monitoare, memorie, DPAPI) |
| DPI | Per-Monitor V2 (clar pe orice scalare, inclusiv monitoare amestecate) |
| Mărime cod | ~13.600 de linii (C#, XAML, JavaScript) |

**Structura proiectului**

| Fișier / folder | Rol |
|---|---|
| `App.xaml(.cs)` | Pornire, o singură instanță, jurnal, modurile `--temps` / `--install-temps` (serviciul de temperatură) |
| `Services/Updater.cs`, `NotchWindow.Updates.cs` | Actualizările automate: verificarea pe GitHub, descărcarea, verificarea semnăturii, înlocuirea exe-ului, alertele din notch |
| `.github/workflows/release.yml`, `RELEASE_NOTES.md` | Construirea, testarea, semnarea și publicarea fiecărei versiuni pe GitHub |
| `Services/TempHelper.cs` | Serviciul de temperatură: sarcina SYSTEM din Program Files, pipe-ul local (doar citire), instalarea cu o confirmare UAC |
| `NotchWindow.xaml(.cs)` | Fereastra notch-ului: forme, animații, standby, alerte, unelte de ecran, alerta de memorie |
| `NotchWindow.Pages.cs` | Tab-urile, paginile proprii, modul editare din notch (galerie, mărimi, ochi pe tab-uri) |
| `EditorWindow.cs` | Fereastra WinNotch: pagini, previzualizare editabilă, inspector, galerie, teme, setări |
| `Themes.cs`, `Theme.xaml`, `Ui.cs` | Teme (6 + ale tale), culori aplicate live, stiluri (butoane, slidere, scroll), helpere pentru interfață |
| `Widgets/*` | Modelul paginilor și grila 6×4 (`Model.cs`), catalogul de 27 de widget-uri (`Catalog.cs`), galeria (`Gallery.cs`) și widget-urile (media, timp, sistem, dispozitive, unelte, personalizate) |
| `Panes/WidgetPage.cs` | O pagină din widget-uri: afișare, mutare, redimensionare cu rearanjare live, tragere din galerie |
| `Panes/HomePane.cs` | Acasă |
| `Panes/SourcesPane.cs` | Lista completă de surse audio |
| `Panes/SystemPane.cs`, `Panes/SpeedView.cs` | Sistem și ecranul testului de viteză |
| `Panes/DevicesPane.cs` | Dispozitive |
| `Panes/ToolsPane.cs` | Unelte |
| `RegionPicker.cs` | Selecția unei zone de pe ecran (capturi, text) |
| `SettingsWindow.xaml(.cs)` | Setările (afișate ca pagină în fereastra WinNotch) |
| `TrayIcon.cs` | Iconița de lângă ceas |
| `AppSettings.cs` | Setări, salvare criptată, pornire cu Windows |
| `Core/Update/*` | Logica actualizărilor, fără WPF: compararea versiunilor (`AppVersion.cs`), alegerea versiunii de oferit și canalul beta (`ReleaseFeed.cs`), pornirea monitorizată și decizia mod sigur / revenire (`StartupGuard.cs`), `startup.json` / `rollback.json` (`StartupStore.cs`), schimbarea fișierelor la revenire (`Rollback.cs`), ordinea pașilor la pornire (`StartupCoordinator.cs`) |
| `Core/Actions/*` | Registrul de acțiuni, fără WPF: descrierea unei acțiuni, parametrii și siguranța (`ActionDescriptor.cs`), anularea (`IUndoableAction.cs`), firul de interfață (`IUiDispatcher.cs`), înregistrarea, căutarea și pornirea cu toate verificările (`ActionRegistry.cs`) |
| `Features/Actions/*` | Acțiunile incluse: lista (`BuiltInActions.cs`, cu spațiile de lucru și stick-urile USB ca liste dinamice), legătura cu aplicația (`IBuiltInHost.cs`, `AppActionHost.cs`) |
| `Core/Flags/*` | Comutatoarele funcțiilor noi: catalogul (`FeatureCatalog.cs`), starea, modul sigur și oprirea automată (`FeatureFlags.cs`), cheia `Features` din setări (`FeatureSettings.cs`) |
| `Core/Diagnostics/HealthLog.cs`, `PerfProbe.cs` | Rezumatul de sănătate din log, la fiecare 6 ore; măsurarea opțională a deschiderii (`perf.flag`) |
| `tools/measure-perf.ps1`, `docs/perf/` | Măsurătorile de bază (memorie, CPU la repaus, 10 minute) și rezultatele lor, pe versiuni |
| `Services/*` | Media, audio, temperaturi, rețea, vreme, versuri, calendar, dispozitive, confidențialitate, ferestre, lansator, capturi/OCR/RAM, extensie |
| `extension/` | Extensia de browser (Chrome, Edge, Brave, Opera, Vivaldi) |
| `build.bat`, `run.bat`, `tools/get-sdk.ps1` | Build, pornire, descărcarea SDK-ului |
| `tests/` | Teste automate (C# și extensia) |
| `README.md`, `AUDIT.md`, `AUDIT-2.md`, `AUDIT-3.md`, `DOCUMENTATIE.md` | Ghid scurt, cele trei audituri, acest document |
| `CLAUDE.md` | Ghid pentru asistenții AI care lucrează la cod (structură, publicare, reguli de securitate, reguli pentru dezvoltarea 0.7+) |
| `docs/ROADMAP.md`, `docs/adr/`, `docs/TESTE-MANUALE.md` | Planul versiunilor 0.7 → 1.0, deciziile de arhitectură (ADR), verificările manuale (lista scurtă de regresie și verificările per funcție) |
| `.github/workflows/ci.yml`, `.github/ISSUE_TEMPLATE/bug.yml`, `.github/pull_request_template.md` | Build și teste la fiecare pull request și push pe alte ramuri decât `main`; formularul de bug; șablonul de pull request |

### Acțiuni (infrastructură, din 0.6.10)

Tot ce poate face WinNotch e și o **acțiune** cu un id stabil, în formatul `zonă.verb` (de exemplu `audio.mute-mic`), într-un singur registru (`Core/Actions/ActionRegistry.cs`). Pe el se vor construi Command Bar (P14), Quick Actions, Workflows, API-ul local și Undo Center. **În 0.6.10 nu se schimbă nimic vizibil:** butoanele și panourile merg ca înainte, iar acțiunile nu sunt încă pornite de nimic.

- **Căutare** fără diacritice și fără majuscule („muta” găsește „Mută / pornește microfonul”), după titlu, cuvinte-cheie în română și engleză și categorie. Ordinea: potrivire exactă, început de cuvânt, în interiorul cuvântului, litere în ordine; la egalitate, cele folosite recent (ultimele 50, doar în memorie, nesalvate).
- **Verificări înainte de pornire:** acțiunea există; cine o pornește are voie (interfața, Command Bar, Quick Actions, Workflows; API-ul local doar pentru acțiunile sigure și doar dacă acțiunea o permite explicit); e disponibilă acum (de exemplu „Scoate stick-ul” doar dacă e conectat); comutatorul funcției e pornit; parametrii sunt valizi (procent 0–100, valori dintr-o listă, text cu lungime maximă). Fiecare acțiune are 10 secunde (cât timp nu cere altceva), iar ce atinge ferestrele rulează pe firul interfeței.
- **Niciodată o eroare spre apelant:** o excepție devine un rezultat „nu a reușit” și se numără la funcția ei (comutatorul o poate opri automat).
- **Siguranță:** fiecare acțiune e Sigură, Cu confirmare (scoaterea unui stick USB) sau Periculoasă (niciuna acum). Una cu confirmare sau periculoasă pornește doar dacă cel care o cere confirmă că te-a întrebat; altfel e refuzată. Implicit, Workflows nu le pot porni (n-au pe cine întreba). O cerere deja anulată nu pornește nimic, iar o acțiune care a așteptat prea mult după interfață nu mai pornește deloc.
- **Liste dinamice** (spații de lucru, stick-uri USB): citite la prima folosire și păstrate până când cine le folosește (Command Bar) cere reîmprospătarea; o eroare trecătoare la citire nu rămâne ținută minte.
- **Log:** doar id-ul, cine a pornit-o și rezultatul; niciodată valorile parametrilor.
- **Anulare:** volumul, sunetul și microfonul pot fi readuse la starea de dinainte (pregătire pentru Undo Center).

| Id | Acțiune | Siguranță |
|---|---|---|
| `audio.volume-set` | Setează volumul (0–100%), cu anulare | Sigură |
| `audio.mute` | Oprește / pornește sunetul, cu anulare | Sigură |
| `audio.mute-mic` | Mută / pornește microfonul (în toate aplicațiile), cu anulare | Sigură |
| `media.play-pause`, `media.next`, `media.previous` | Redă / pauză, piesa următoare, anterioară (doar când se redă ceva) | Sigură |
| `tools.screenshot`, `tools.screenshot-area`, `tools.ocr`, `tools.free-ram` | Captură ecran, captură zonă, text din ecran, eliberează RAM | Sigură |
| `window.topmost`, `window.next-monitor`, `window.half`, `window.mini` | Fereastra activă: deasupra, pe celălalt monitor (doar cu 2 monitoare), jumătate stânga / dreapta (alternativ, ca butonul), mică în colț | Sigură |
| `workspace.open-<nume>` | Deschide spațiul de lucru (câte una pentru fiecare spațiu salvat) | Sigură |
| `device.eject-<literă>` | Scoate stick-ul USB (câte una pentru fiecare unitate detașabilă) | Cu confirmare |
| `settings.bluetooth`, `settings.sound`, `settings.display`, `settings.wifi`, `settings.update` | Setările Windows: Bluetooth, Sunet, Ecran, Wi-Fi, Windows Update | Sigură |
| `winnotch.open`, `winnotch.settings`, `winnotch.speed-test` | Deschide notch-ul, setările WinNotch, testul de viteză | Sigură |

---

## 2. Instalare, build și dezvoltare

**Instalare obișnuită:** descarci `WinNotch.exe` din https://github.com/QvBnM/WinNotch/releases și îl pornești (de oriunde). De acolo încolo se actualizează singur (vezi „Actualizări automate”).

**Codul:** e public pe https://github.com/QvBnM/WinNotch. Se poate lucra la el din VS Code (Git: Clone + .NET 8 SDK + Node.js); asistenții AI (Claude Code etc.) citesc `CLAUDE.md`, care descrie structura, regulile de securitate și pașii de publicare. O versiune nouă = `<Version>` crescut în `WinNotch.csproj` + `RELEASE_NOTES.md` rescris + push pe `main`; restul îl face GitHub.

**Build local (opțional):**

1. Dezarhivezi sau clonezi folderul oriunde.
2. Dublu-click pe **`build.bat`**.
   - **Verifică SDK-ul.** Îl caută întâi instalat în Windows, apoi în copia comună a contului tău (`%LOCALAPPDATA%\WinNotch\dotnet`). Dacă nu-l găsește, te întreabă o singură dată dacă să-l descarce. Cu „D”, ia ultima versiune .NET 8 SDK de pe serverul oficial Microsoft, o verifică cu SHA-512 și o pune în copia comună, fără instalare în Windows și fără drepturi de admin; toate versiunile WinNotch o folosesc de acum înainte. Pe ecran vezi pași numerotați și o bară cu MB, viteză și timp rămas. Dacă prima metodă eșuează, încearcă scriptul oficial Microsoft. Cu „N”, deschide pagina de descărcare.
   - **Închide WinNotch-ul care rulează,** ca să poată înlocui exe-ul (nu și serviciul de temperatură, care rulează separat ca SYSTEM). Dacă rula ca administrator, cere confirmarea Windows.
   - **Construiește** `publish\WinNotch.exe` și îl pornește. Scrie „Gata!” doar dacă exe-ul chiar a fost creat; altfel afișează eroarea cu textul de trimis.
3. **`run.bat`** pornește aplicația deja construită sau face build-ul dacă lipsește.

Prima dată, build-ul descarcă pachetele NuGet (1–2 minute). SmartScreen poate avertiza, pentru că exe-ul nu e semnat digital: „Mai multe informații → Rulează oricum”.

---

### Actualizări automate

- **De unde:** codul e pe GitHub (`QvBnM/WinNotch`). Când pe `main` ajunge o versiune nouă (`<Version>` din `WinNotch.csproj`), GitHub Actions rulează toate testele, construiește `WinNotch.exe` și îl **semnează** cu cheia de lansare WinNotch (ECDSA P-256; partea privată stă doar în secretele criptate ale GitHub), apoi publică exe-ul și semnătura în Releases.
- **Verificare:** automat (dacă „Caută singur versiuni noi” e bifat) la 60 de secunde după pornire și apoi la 6 ore, sau oricând vrei tu, din meniul iconiței › **„Caută actualizări”** (merge și cu verificarea automată oprită; răspunsul apare în notch: oferta versiunii noi sau „Ai ultima versiune”). WinNotch întreabă GitHub dacă există o versiune mai nouă. Nu trimite nimic despre tine (doar o cerere publică, cu versiunea în User-Agent).
- **În notch:** „WinNotch 0.6.7 e gata” cu **lista schimbărilor**, fiecare marcată **Nou** (verde), **Îmbunătățit** (albastru), **Modificat** sau **Reparat** (portocaliu), și butoanele „Actualizează” / „Mai târziu” (amână 24 de ore). Nu apare peste jocuri sau video pe tot ecranul.
- **Notele** vin din `RELEASE_NOTES.md` (un titlu `## Nou` / `## Îmbunătățit` / `## Modificat` / `## Reparat` și câte un rând `- ` pe schimbare); sunt și textul release-ului pe GitHub și sunt incluse în exe.
- **Instalare:** descarcă exe-ul (cu progres în notch), verifică semnătura pentru exact acea versiune (o versiune mai veche sau un fișier modificat e refuzat și șters), redenumește exe-ul curent în `WinNotch.old.exe`, pune versiunea nouă în locul lui și repornește. La pornire, versiunea nouă șterge descărcarea; **`WinNotch.old.exe` rămâne până când versiunea nouă e declarată sănătoasă** (vezi mai jos), ca să existe la ce reveni.
- **După actualizare:** „Actualizat la WinNotch 0.6.7 — Ce e nou” cu aceeași listă (buton „Am înțeles”); tot timpul o găsești și în fereastra WinNotch › **Noutăți**; dacă serviciul de temperatură e mai vechi, îți propune să-l actualizezi (o confirmare). Dacă browserul încă rulează extensia de dinainte, notch-ul îți spune să apeși ↻ în chrome://extensions (Chrome nu permite reîncărcarea automată a extensiilor instalate din folder).
- **Setări:** „Caută singur versiuni noi” (pornit implicit), **„Canal beta (versiuni de test)”** (oprit implicit) și „Caută acum”. O versiune găsită manual e oferită în notch și când verificarea automată e oprită.
- **Compararea versiunilor** se face pe numere, componentă cu componentă (0.6.10 e mai nouă decât 0.6.9, 1.0.0 decât 0.11.0). O versiune cu sufix de test e mai veche decât aceeași fără sufix: 0.7.0-rc.1 < 0.7.0-rc.2 < 0.7.0. Se propune doar o versiune strict mai nouă decât cea instalată.
- **Canal beta:** oprit, WinNotch întreabă GitHub doar de ultima versiune finală (ca până acum). Pornit, ia în calcul și versiunile de test (pre-release) din ultimele 20 publicate. O versiune e publicată ca pre-release automat dacă `<Version>` conține „-” (de exemplu `0.7.0-rc.1`); semnarea și verificarea sunt aceleași.
- **Protecție la o versiune stricată (pornire monitorizată):**
  - la fiecare pornire, înaintea oricărui serviciu, WinNotch notează pornirea în `%AppData%\WinNotch\startup.json`; „Ieșire” din meniu, oprirea Windows și repornirea pentru actualizare o marchează ca închidere curată, care nu se numără (dacă oprirea Windows e anulată de alt program, protecția se reia după 2 minute);
  - o pornire care eșuează la jumătate (eroare înainte să apară notch-ul și iconița) închide procesul și se numără ca închidere bruscă, în loc să lase un WinNotch fără fereastră;
  - **3 porniri fără închidere curată în 5 minute** → WinNotch repornește o singură dată în **modul sigur** (`--safe-mode`: funcțiile Experimental și Beta oprite);
  - dacă se închide brusc **și în acest mod sigur pornit automat, în primele 5 minute** → **revine singur la versiunea anterioară** (un mod sigur care a mers mai mult, sau unul pornit de mână cu `--safe-mode`, urmat de o închidere bruscă, se numără doar ca o închidere obișnuită): exe-ul curent devine `WinNotch.rejected.exe`, `WinNotch.old.exe` redevine `WinNotch.exe`, se scrie `rollback.json` (versiunea refuzată și motivul) și pornește versiunea restaurată;
  - se revine doar la un `WinNotch.old.exe` care e un WinNotch **mai vechi** decât cel curent (după versiunea din fișier); dacă lipsește sau nu e valid, nu se schimbă nimic: rămâne în modul sigur și scrie motivul în log. `rollback.json` se scrie înaintea schimbării fișierelor; dacă schimbarea eșuează, se anulează și totul rămâne cum era;
  - cel mult **o revenire la 30 de minute**, ca două versiuni stricate să nu se înlocuiască una pe alta la nesfârșit (altfel rămâne în modul sigur);
  - după **10 minute de rulare fără erori neprinse** (nu în modul sigur; numărate minut cu minut, deci somnul laptopului sau schimbarea ceasului le iau de la capăt), versiunea e declarată sănătoasă: contorul se golește și abia acum se șterg `WinNotch.old.exe` (și un eventual `WinNotch.rejected.exe`);
  - versiunea restaurată (dacă are acest cod, adică 0.6.9 sau mai nouă) citește `rollback.json` și afișează o dată în notch „Am revenit la 0.6.9: 0.7.0 se închidea”; versiunea refuzată nu mai e propusă, doar una mai nouă decât ea.
- **build.bat** rămâne pentru cine vrea să construiască singur din cod.

---

## 3. Fereastra notch-ului: comportament general

- **Formă și animații.** O singură fereastră transparentă, mereu deasupra. Pastila își schimbă lățimea, înălțimea și rotunjirea cu animații elastice. Panourile apar cu fade.
- **Nu fură focusul.** Fereastra nu se activează și nu ia tastatura aplicației tale. Doar când scrii într-un câmp (lansator, căutare, notiță) primește temporar focusul, iar la închidere îl dă înapoi ferestrei de dinainte.
- **Click-uri prin el.** Închis, click-urile trec prin notch spre ce e dedesubt.
- **Deschidere.**
  - Hover peste pastilă, după un timp reglabil (implicit 0,4 s).
  - **`Win + Alt + N`** sau meniul iconiței.
  - Pe durata hover-ului, pastila devine semi-transparentă, ca să vezi ce e sub ea.
- **Închidere.** Când ieși cu mouse-ul, după o scurtă întârziere.
- **Monitoare.**
  - Urmează monitorul pe care e mouse-ul.
  - Dacă acel monitor e „ocupat” (joc sau video pe tot ecranul, inclusiv borderless), trece pe un monitor liber sau se ascunde. Setarea „Peste jocuri / fullscreen” alege între „se ascunde, apare doar pentru alerte” și „rămâne mereu vizibil”.
- **Ferestre maximizate.** Peste o fereastră maximizată se face mic (opțional) și **se dă la o parte**: cât timp mouse-ul e peste el, dispare complet și click-ul ajunge la fereastra de dedesubt (de exemplu butonul „+” tab nou din browser). Se deschide doar dacă împingi mouse-ul de tot sus, în marginea ecranului.
- **Click prin notch.** Dacă dai click cât mouse-ul stă pe pastilă, click-ul era pentru fereastra de sub ea: notch-ul nu se mai deschide până nu ieși cu mouse-ul de pe el.
- **Poziție.** Stânga, centru sau dreapta, pe lățimea monitorului.
- **Mărire pe ecrane mari.** Notch-ul deschis și alertele se măresc automat: 120% pe 1440p, 135% pe 4K, 110% pe ecrane intermediare, când Windows e la scalare 100%. Manual, se alege din Setări între 100% și 160%. Pastila mică nu se mărește.
- **Text.** Randat clar, cu contrast îmbunătățit și minimum 11 px.
- **Performanță.** Fereastra transparentă e cât de mică se poate (Windows o redesenează toată la fiecare cadru de animație); crește doar cât e deschisă galeria în editare și revine după închidere. Închis, nu face aproape nimic: nu desenează la fiecare cadru, nu scanează sunetul pe aplicații, iar animațiile ascunse sunt oprite.

---

## 4. Standby: pastila închisă

- **Conținut ales de tine,** maxim 5 elemente, în ordinea dorită. Prima jumătate stă în stânga „camerei”, restul în dreapta. Elemente disponibile:
  - Muzică (coperta mică și egalizator animat);
  - Ora, Data, Vremea;
  - CPU, RAM;
  - Temperatura CPU, GPU și SSD (colorate după cât de cald e);
  - Baterie (cu iconiță de umplere), Volum, Internet.
- **Forma mică.** După câteva secunde fără activitate (implicit 10; se poate 5 s, 30 s, 1 min sau niciodată), sau peste o fereastră maximizată, se strânge într-o pastilă de 196×22 px:
  - afișează **ora · data**;
  - o linie subțire de jos arată progresul piesei;
  - un semn portocaliu **„mic”** apare cât microfonul e folosit.
- **Ecran ocupat.** Pe un monitor ocupat, pastila se ascunde în sus, cu animație.

---

## 5. Alerte scurte (live activities)

Notch-ul se transformă pentru câteva secunde și apoi revine.

| Alertă | Ce arată |
|---|---|
| Volum | Bară și număr; se actualizează lin cât tragi de volum |
| Piesă nouă | Coperta, titlul, artistul, egalizator. Nu apare dacă aplicația sau browserul care cântă e fereastra în care lucrezi; aceeași piesă e anunțată cel mult o dată în 15 minute |
| Încărcător | „Se încarcă” sau „Pe baterie”, cu procent |
| Baterie descărcată | La 20% și la 10% |
| Temperatură ridicată | CPU sau GPU peste 88 °C (cel mult o dată la 5 minute) |
| Memorie plină | RAM peste prag (implicit 80%, reglabil 75–90% în Setări) timp de 20 s: procentul, GB folosiți, primele 4 aplicații după memorie (cu bară și GB) și butoanele „Optimizează” (pornește eliberarea RAM, cu progres) și „Mai târziu”. Cel mult o dată la 15 minute; nu apare peste jocuri/video pe tot ecranul sau cât notch-ul e deschis |
| Pauză pentru ochi | La fiecare 20 de minute de activitate: 20 de secunde de privit în depărtare, cu numărătoare și buton „Sari” |
| Captură ecran / zonă | Miniatura capturii cu efect de bliț, rezoluția, butoanele „Deschide” și „Folder” |
| Text din ecran | Miniatura zonei și „Citesc textul…”, apoi „Text copiat · N rânduri” cu începutul textului |
| Eliberează RAM | Progres în timp real, apoi „Eliberat X GB · 62% → 48% folosit” |
| Versiune nouă | „WinNotch 0.6.7 e gata” cu lista schimbărilor (Nou / Îmbunătățit / Modificat / Reparat) și „Actualizează” / „Mai târziu”; apoi progresul descărcării |
| După actualizare | „Actualizat la WinNotch 0.6.7 — Ce e nou” cu aceeași listă; dacă e cazul, propunerea de a actualiza serviciul de temperatură |
| Extensie veche | „Extensia din browser e veche: apasă ↻ în chrome://extensions”, cu „Copiază adresa” |

Pe un monitor ocupat, doar alertele importante (baterie, temperatură, pauză, unelte) mai apar.

---

## 6. Pagina Acasă

Înălțime 310 px (la mărire 100%).

**Cardul de muzică**
- Piesa care se aude: titlu, artist, aplicație, copertă.
- **Fundal colorat după copertă:** se ia culoarea dominantă a copertei.
- **Versuri sincronizate** de la LRCLIB (gratuit): rândul curent și următorul, după poziția din piesă. Se pornesc și se opresc cu butonul „Versuri”. Pentru tab-uri din browser, versurile se caută doar pe site-urile de muzică.
- **Vizualizator real:** 44 de bare care se mișcă după sunetul care iese din boxe (FFT pe captura audio). Rulează doar cât Acasă e deschisă.
- Progres cu timpul curent și durata. Butoanele anterior, redare/pauză, următor. În browser fac ce fac tastele media ale tastaturii (handler-ele „nexttrack / previoustrack” ale paginii): video-ul sau piesa următoare pe YouTube, YouTube Music, Spotify, SoundCloud; dacă pagina nu are așa ceva, apasă butonul vizibil al player-ului, iar ca ultimă variantă sare 10 s. „Anterior” repornește piesa dacă a trecut de 3 s; apăsat din nou, merge la cea dinainte.
- **Volumul general,** cu buton de mute.
- **Alegerea piesei din card:**
  - cardul rămâne pe ce afișează cât timp acel lucru încă se aude;
  - trece la altceva doar când se oprește, când pornește altceva și nimic altceva nu se aude, sau când alegi tu o sursă.

**Rândul de surse:** primele 3 lucruri care se aud acum (vezi secțiunea 7), plus „+N” pentru lista completă.

**Coloana din dreapta**
- Ceasul mare și data.
- **Următorul eveniment din calendar** (din link-ul iCal din Setări; înțelege evenimente care se repetă zilnic sau săptămânal). Fără link, arată „Leagă calendarul”.
- **Vremea** de la Open-Meteo (fără cont): temperatura acum, minima și maxima, iconiță de zi/noapte și următoarele 4 ore. Se actualizează la 20 de minute.

---

## 7. Sursele audio și extensia de browser

**Ce apare în listă**
- **Doar ce produce sunet acum.** O sursă dispare abia după ~5 secunde de liniște, ca să nu clipească în pauzele scurte. Ce pui pe pauză sau pe mut din notch mai rămâne puțin, ca să-l poți reporni.
- **Ordine fixă:** fiecare sursă stă pe locul în care a apărut, iar cele noi se adaugă la final. Nimic nu sare.
- Pe fiecare sursă: iconiță colorată, nume, titlu, **4 bare verzi animate** cât se aude (gri cât tace), buton de mute.
- WinNotch însuși nu apare în listă.

**Lista completă („+N”)**
- Pentru fiecare sursă: pauză/redare, „Deschide tab-ul”, **volum separat** și mute.
- Click pe o sursă o pune în cardul mare.
- Dacă un browser cântă dar extensia nu e instalată, apare link-ul „Vezi fiecare tab din browser separat”.

**Surse urmărite**
- **Aplicațiile Windows** (Spotify, Discord, VLC, jocuri…), din mixerul de volum: nivel, volum și mute per aplicație.
- **Sesiunile media Windows,** pentru titlu, artist, copertă, poziție și controale. Fiecare sesiune e ținută separat, iar toate sunt recitite la orice schimbare. O verificare suplimentară la 3 secunde prinde aplicațiile care nu anunță pauza.
- **Schimbarea ieșirii audio** (căști, Bluetooth) e urmărită automat.

**Extensia de browser (Chrome, Edge, Brave, Opera, Vivaldi)**
- **De ce e nevoie de ea:** Windows vede tot browserul ca o singură aplicație. Cu extensia, **fiecare tab** apare separat, cu numele site-ului (YouTube, YouTube Music, YouTube Shorts, Instagram Reels, TikTok, Twitch, Netflix, Spotify Web, SoundCloud…) și titlul curat (fără „(3)” și „- YouTube”).
- **Play și pauză instant:** un script din pagină anunță imediat când un video sau audio pornește ori se oprește. Indicatorul de sunet al Chrome are 2–3 secunde întârziere.
- **Datele fiecărui tab,** luate din pagină: titlu, artist, copertă și poziție. Pe YouTube, coperta implicită e miniatura video-ului. **Copertele le descarcă extensia** (în browser, prin proxy-ul/VPN-ul lui) și le trimite ca imagine; WinNotch nu se conectează la site-uri.
- **Comenzi din notch:** pauză și redare, volum pe tab (0–100%), mute pe tab, anterior/următor, salt în piesă, „du-mă la tab”. Anterior/următor folosesc comenzile pe care pagina le pregătește pentru tastele media ale tastaturii (video-ul sau piesa următoare pe YouTube, YouTube Music, Spotify, SoundCloud…); altfel butonul vizibil al player-ului, iar ca ultimă variantă un salt de 10 s. „Anterior” repornește piesa dacă a trecut de 3 s; apăsat din nou, trece la cea dinainte.
- **Doar tab-uri cu sunet:** extensia trimite doar tab-urile care se aud sau au media pornită; celelalte tab-uri nu sunt trimise deloc.
- **Instalare:**
  - în Setări → „Tab-uri din browser”, apeși „Deschide extensiile”; calea folderului se copiază automat;
  - pornești „Developer mode”, apoi „Load unpacked”.
  - Starea conexiunii se vede în Setări: „✓ Conectată: Chrome”.
- **Fișierele extensiei** sunt incluse în exe și scrise la pornire în `%AppData%\WinNotch\extension`.
- **Comunicare:** WebSocket local pe `127.0.0.1:47811`. Extensia are ID fix și doar ea e acceptată. WinNotch și extensia își dovedesc reciproc că știu codul secret din `token.json` (HMAC pe două numere aleatoare), fără ca acesta să fie trimis; până atunci extensia nu trimite și nu execută nimic. Cât WinNotch e închis, se reconectează din ce în ce mai rar, până la o dată pe minut. Versiunea curentă a extensiei: 1.6 (după o actualizare care o schimbă: ↻ în chrome://extensions).

---

## 8. Pagina Sistem și testul de viteză

Înălțime 320 px.

- **Procesor:** încărcarea în % și temperatura, plus un grafic al ultimului minut, cu încărcarea (linie plină) și temperatura (linie punctată). Dacă temperatura lipsește, explică de ce: „activează” (serviciul de temperatură nu e instalat) sau „PawnIO” (lipsește driverul).
- **Internet:**
  - tipul conexiunii (Wi-Fi sau Ethernet) și ping-ul;
  - descărcare și încărcare **acum**, cu bare față de **viteza maximă măsurată**;
  - un rând cu ultimul test (dată, viteze, ping router), pe care dai click ca să deschizi detaliile.
  - Măsurarea traficului nu mai arată vârfuri false când pornește un VPN sau se reconectează Wi-Fi-ul.
- **Memorie** (% și GB folosiți din total) și **placa video** (temperatură și încărcare).
- **Baterie:** bară, procent, „se încarcă” sau timpul rămas. Pe un PC fără baterie, rândul e estompat.
- **„Consumă acum”:** primele 4 aplicații după CPU și RAM (procesele aceleiași aplicații sunt adunate; WinNotch e exclus).

**Ecranul „Test de viteză”**
- **În timpul testului** (~20 s), vezi viteza în timp real și o bară de progres. Testul are trei pași:
  1. **ping** spre router și spre internet (1.1.1.1), câte 12 încercări, cu medie, variație (jitter) și pierderi;
  2. **descărcare** 8 s, pe 6 conexiuni paralele (serverele de test Cloudflare);
  3. **încărcare** 6 s, pe 6 conexiuni paralele.
- **Rezultat:**
  - un tabel „Acum” față de „Ultimul” test: descărcare, încărcare, ping internet, ping router, pierderi; valorile mult mai slabe apar portocaliu;
  - **istoricul ultimelor 10 teste,** ca grafic de bare cu detalii la hover;
  - **un verdict în cuvinte simple:**
    - „Routerul răspunde greu, restartează-l” (pe Wi-Fi: „sau apropie-te / folosește cablu”);
    - „Routerul e în regulă, problema pare la furnizor”;
    - „Viteza e la jumătate față de cel mai bun test”;
    - „Totul arată bine”.
- **După test:** ecranul rămâne deschis până apeși „Înapoi”. Testul continuă chiar dacă închizi notch-ul. Se păstrează ultimele 20 de teste.

---

## 9. Pagina Dispozitive

Înălțime 330 px.

- **Microfon și cameră:**
  - **cine le folosește acum** și **de cât timp**, citit din același loc ca indicatorul de confidențialitate Windows;
  - dacă nu sunt folosite, cine le-a folosit ultima dată;
  - buton de **oprire a microfonului** pentru toate aplicațiile.
- **Audio:** ieșirea și intrarea implicite.
- **Dispozitive conectate,** într-o listă care se derulează, reîmprospătată la 6 s:
  - Bluetooth (căști, mouse, tastatură, telefon, controler) și USB (tastaturi, mouse-uri, camere, imprimante, telefoane, audio);
  - monitoare, cu nume real, rezoluție și Hz;
  - stick-uri și unități USB, cu spațiu liber și buton **„Scoate”** (scoatere în siguranță).
  - Citirile lente (monitoare, stick-uri) rulează în fundal, ca să nu blocheze interfața.

---

## 10. Pagina Unelte

Înălțime 360 px.

- **Lansator:**
  - scrii și deschizi aplicații (din meniul Start), setări Windows (Bluetooth, Sunet, Ecran, Wi-Fi, Update etc.) și foldere;
  - navighezi cu săgețile, deschizi cu Enter, renunți cu Esc;
  - **calcule** dacă începi cu `=`, de exemplu `=250*1,19` → 297,5; un click copiază rezultatul.
- **Unelte rapide:** notch-ul dispare înainte, ca să nu apară în captură.
  - **Captură ecran:** monitorul pe care e mouse-ul, salvată în *Imagini › Screenshots* (`WinNotch AAAA-LL-ZZ HH-mm-ss.png`) și copiată în clipboard.
  - **Captură zonă** (`Win + Alt + S`): ecranul îngheață și se întunecă, tragi un dreptunghi și vezi dimensiunea în pixeli. Esc sau click dreapta anulează. Merge pe mai multe monitoare, inclusiv cu scalări diferite.
  - **Text din ecran / OCR** (`Win + Alt + T`):
    - tragi peste orice text (imagine, video, PDF scanat, joc) și e copiat ca text;
    - folosește recunoașterea Windows, fără internet, în română dacă limba e instalată;
    - zonele mici sunt mărite pentru o citire mai bună.
  - **Eliberează RAM:**
    - butonul arată procentul de memorie folosit acum;
    - la apăsare golește memoria ținută degeaba de aplicații (cache-ul Windows doar dacă WinNotch rulează ca administrator, ceea ce nu mai e necesar);
    - progresul și rezultatul apar în notch.
- **Spații de lucru:**
  - salvezi aplicațiile deschise și poziția lor pe fiecare monitor, cu nume automate (Lucru, Gaming, Seară, Studiu, Proiect);
  - un click redeschide aplicațiile care lipsesc și le pune la loc;
  - meniul de click dreapta permite actualizarea sau ștergerea, iar din Setări le redenumești.
- **Fereastra activă:** „Deasupra” (o ține peste toate), „Monitor 2” (o mută pe celălalt monitor), „Jumătate” (stânga sau dreapta), „Mini” (mică, în colțul din dreapta jos).
- **Clipboard:**
  - ultimele **20** de texte copiate, cu căutare;
  - **fixarea** păstrează un text și după repornire;
  - un click copiază din nou textul.
  - **Parolele** copiate din managere de parole (KeePass, Bitwarden, 1Password etc.) nu sunt înregistrate.
- **Notiță:** un câmp de text liber, salvat automat la 1,5 s după ce te oprești din scris.

---

## 11. Setări

Setările sunt pagina „Setări” din fereastra WinNotch (o singură fereastră pentru pagini, teme și setări). Se deschid din rotița din notch, din meniul iconiței sau cu dublu-click pe iconiță. „Salvează” aplică pe loc și afișează „✓ Salvat”; „Renunță” revine la valorile salvate.

| Secțiune | Opțiuni |
|---|---|
| Ce apare în standby | Bifezi până la 5 elemente și le ordonezi cu ▲▼ |
| Comportament | Întârzierea la hover (0–1000 ms) · poziția (stânga/centru/dreapta) · peste jocuri/fullscreen · micșorare automată (niciodată/5 s/10 s/30 s/1 min) · **mărimea notch-ului deschis** (automat, 100–160%) · mic peste ferestre maximizate · citirea temperaturilor · **„Activează temperatura procesorului”** (instalează serviciul de temperatură, o confirmare UAC; tot de aici se actualizează după un build nou) · pornire cu Windows · căutarea versiunilor noi, **canalul beta** și „Caută acum” |
| Acasă și sănătate | Versuri · pauză pentru ochi · **alertă de memorie** (pornit/oprit, prag 75/80/85/90%) · link-ul iCal pentru calendar (cu instrucțiuni pentru Google și Outlook) |
| Tab-uri din browser | Fiecare tab separat (pornit/oprit) · starea extensiei · butoane: deschide extensiile în Chrome sau Edge, arată folderul extensiei · pașii de instalare |
| Spații de lucru | Redenumire și ștergere |
| Culoare accent | Culoarea temei (implicit), Chihlimbar, Verde, Albastru, Roz, Mov, Alb |
| Vremea | Orașul, latitudinea și longitudinea |
| Funcții noi (experimental) | Un comutator pentru fiecare funcție nouă din catalog, cu numele, o descriere de un rând și eticheta de stadiu (**Experimental**, **Beta**, **Stabil**); se aplică la „Salvează”, fără repornire. Dacă o funcție a fost oprită automat, apare și motivul |

**Funcții noi și comutatoare (feature flags, din 0.7)**
- Fiecare funcție nouă e declarată într-un singur loc, `Core/Flags/FeatureCatalog.cs` (ID, nume, descriere, stadiu, valoare implicită) și e oprită implicit până la versiunea în care e anunțată. Prima intrare e „Funcție de test” (`demo-flag`, Experimental, oprită), care nu face nimic vizibil.
- Starea se salvează în `settings.json`, în cheia `Features` (ID → pornit/oprit). Se păstrează doar ce diferă de valoarea implicită; un fișier mai vechi, fără `Features`, înseamnă „toate la valoarea implicită”. Cheile funcțiilor necunoscute (de exemplu dintr-o versiune mai nouă) rămân neatinse.
- Funcțiile pornesc și se opresc pe loc: ascultă evenimentul `Changed` al `FeatureFlags`, care se declanșează o singură dată la fiecare schimbare reală.
- **Oprire automată:** o funcție care prinde 3 erori în 10 minute se oprește singură; motivul (cel mult 120 de caractere) apare în log și în Setări. „Salvează” aplică doar comutatoarele pe care le-ai schimbat, deci nu repornește din greșeală o funcție oprită automat cât pagina era deschisă. O pornești din nou bifând-o și apăsând „Salvează”.
- **Mod sigur:** `WinNotch.exe --safe-mode` pornește cu toate funcțiile Experimental și Beta oprite, fără să schimbe ce e salvat (Setări arată în continuare alegerile tale, cu o notă despre modul sigur). La următoarea pornire normală revin cum erau.
- **Rezumat de sănătate:** la fiecare 6 ore, un rând în `log.txt`: memoria WinNotch (MB), procesorul folosit în medie de WinNotch în acest interval și numărul de erori prinse pe fiecare funcție. Fără date personale: în log ajunge doar tipul erorii, nu mesajul ei.

**Pornirea cu Windows:** o intrare în „Run” din registru, cu drepturi normale. WinNotch nu mai pornește niciodată ca administrator (până la 0.6.4 exista o sarcină de logare elevată; instalarea serviciului de temperatură o șterge).

**Temperatura procesorului (serviciul de temperatură)**
- Senzorii procesorului se citesc doar cu drepturi de administrator. În loc ca toată aplicația să ruleze elevat, o copie a WinNotch rulează ca **sarcină a contului SYSTEM, din `C:\Program Files\WinNotch\`**, cu `--temps`: fără fereastră, nu citește nimic din profilul tău, nu primește comenzi; doar răspunde pe un pipe local (`WinNotch.Temps.v1`, utilizatorii au numai drept de citire) cu temperaturile.
- Notch-ul (cu drepturi normale) întreabă pipe-ul la 2 secunde și acceptă răspunsul doar din sesiunea 0 (unde rulează SYSTEM), deci un program care ar crea un pipe fals e ignorat.
- Se instalează din meniul iconiței sau din Setări („Activează temperatura procesorului”), cu o singură confirmare UAC; copia în Program Files se face din fișierul pe care WinNotch îl ține blocat de la pornire (nu poate fi înlocuit între timp). DLL-urile native se despachetează într-un folder din Program Files, nu în Temp. Pornește la boot, merge și pe baterie, se repornește singur dacă se oprește.
- Dezinstalare: `WinNotch.exe --uninstall-temps` (cere confirmare).

---

## 12. Iconița din zona de notificări

Meniul iconiței are:
- Deschide notch-ul (`Win+Alt+N`);
- Pagini, teme și setări… (fereastra WinNotch);
- Caută actualizări (verifică acum dacă există o versiune nouă; răspunsul apare în notch);
- Activează temperatura procesorului…, afișat cât serviciul de temperatură nu e instalat;
- Deschide folderul cu setări și log;
- Ieșire.

Dublu-click pe iconiță deschide fereastra WinNotch pe pagina Setări.

---

## 13. Date, fișiere și confidențialitate

| Unde | Ce |
|---|---|
| `%AppData%\WinNotch\settings.json` | Toate setările, inclusiv paginile tale de widget-uri (`Pages`), paginile ascunse și temele. **Calendarul (link secret), notița și clipurile fixate sunt criptate** pentru contul tău de Windows (DPAPI). Salvarea se face printr-un fișier temporar, ca o închidere bruscă să nu strice fișierul |
| `%AppData%\WinNotch\log.txt` | Erori și evenimente tehnice (fără texte din clipboard sau link-uri secrete), plus la 6 ore rezumatul de sănătate (RAM, CPU mediu, erori pe funcții); se golește la 512 KB |
| `%AppData%\WinNotch\startup.json` | Pornirile versiunii curente care nu s-au încheiat curat, dacă rularea e în modul sigur, dacă versiunea e sănătoasă și versiunile refuzate (protecția la o versiune stricată); salvat tot printr-un fișier temporar |
| `%AppData%\WinNotch\rollback.json` | Doar după o revenire automată: versiunea refuzată și motivul; citit și șters de versiunea restaurată |
| `%AppData%\WinNotch\crash-test.flag`, `perf.flag` | Doar pentru teste, create de tine: primul provoacă o închidere bruscă la 5 s după pornire (test manual al protecției), al doilea scrie în log timpul de la începutul hover-ului până la primul cadru al deschiderii (include întârzierea la hover din Setări) |
| `%AppData%\WinNotch\extension\` | Fișierele extensiei de browser |
| `Imagini\Screenshots\` | Capturile |
| `C:\Program Files\WinNotch\` | Serviciul de temperatură (doar dacă l-ai activat): exe-ul și folderul `runtime` |

**Servicii externe folosite** (toate prin HTTPS, fără cont):
- Open-Meteo, pentru vreme (trimite coordonatele orașului);
- LRCLIB, pentru versuri (trimite titlul și artistul; pentru tab-uri doar de pe site-urile de muzică);
- Cloudflare, doar pentru testul de viteză;
- link-ul tău iCal, pentru calendar;
- copertele tab-urilor le descarcă **browserul** (prin proxy-ul/VPN-ul lui), nu WinNotch; vezi secțiunea 14.

Istoricul de clipboard nefixat stă doar în memorie și dispare la închidere.

---

## 14. Securitate

Trei audituri (`AUDIT.md`, `AUDIT-2.md`, `AUDIT-3.md` — ultimul, de securitate ofensivă, pe 0.6.4, cu toate cele 15 constatări remediate în 0.6.5).

- **Fără proces elevat:** WinNotch rulează mereu cu drepturi normale. Singura parte cu drepturi mari e serviciul de temperatură (SYSTEM, din Program Files, fără fereastră, fără intrări din profilul tău, doar răspunde cu temperaturi). Astfel nimic din `%AppData%`, din variabilele tale de mediu sau din asocierile de fișiere nu poate influența un proces cu drepturi de administrator.
- **Exe-ul propriu e blocat** cât rulează (nu poate fi redenumit sau înlocuit); instalarea serviciului copiază exact acel fișier. Build-ul dezactivează „startup hooks” (.NET), iar programele de sistem (`schtasks`, `cmd`, `explorer`) se pornesc cu cale completă.
- **Extensia:**
  - doar extensia WinNotch se poate conecta (ID fix în Origin); paginile web nu se pot conecta (nici prin DNS rebinding);
  - **autentificare reciprocă fără ca token-ul să circule:** WinNotch trimite o provocare, extensia răspunde cu HMAC-SHA-256(token, nonce-uri), WinNotch își dovedește la rândul lui identitatea; până atunci extensia nu trimite nimic și nu execută nimic. Un program care ascultă pe port cât WinNotch e închis nu primește nici token-ul, nici tab-urile;
  - o conexiune care nu se autentifică în 4 secunde e închisă; maximum 6 conexiuni, mesaje ≤ 512 KB, 60 de tab-uri, titluri ≤ 300 de caractere, durate/poziții limitate la 7 zile;
  - extensia execută doar comenzile ei fixe (pauză, volum etc.), niciodată cod primit.
- **Copertele:** le descarcă extensia în browser (prin proxy-ul/VPN-ul lui) și trimite octeții; WinNotch nu se mai conectează la adrese alese de pagini. Se acceptă doar PNG/JPEG/WebP (după primii octeți), maximum 300 KB și 4096 px, iar ca administrator nu se decodează deloc.
- **Calendarul:** maximum 4 MB, 5.000 de evenimente și un buget fix de calcul pentru repetări; redirecționările nu pot duce spre adrese din rețeaua locală (doar adresa scrisă de tine poate fi una locală); o singură reîmprospătare odată.
- **Rularea ca administrator** (dacă totuși pornești manual așa): toate lansările trec prin Explorer cu drepturi normale; fișierele extensiei nu se mai scriu; capturile se salvează doar în profilul tău (verificat pe calea reală).
- **Scurtături:** doar http/https/fișiere/ms-settings; căile de rețea (`\\server\share`) sunt refuzate și nu li se cere nici iconița (altfel Windows s-ar autentifica automat la acel server).
- **Fișiere:** salvări atomice, fără urmarea junction-urilor din folderul WinNotch; log-ul nu conține link-uri, texte din clipboard sau titluri.
- **Revenirea automată** e singura excepție de la „doar versiuni mai noi”: pornește înapoi doar `WinNotch.old.exe` de lângă exe, numai dacă e un WinNotch mai vechi, cu cale completă. `startup.json` și `rollback.json` din `%AppData%\WinNotch` sunt de încredere doar cât contul tău: un program care rulează deja cu contul tău le poate modifica (de exemplu ca să blocheze o versiune), dar nu poate obține mai multe drepturi; versiunile refuzate sunt scrise în log la fiecare pornire.
- **Clipboard:** conținutul marcat ca privat de managerele de parole e ignorat.
- **Calculatorul din lansator:** acceptă doar cifre și operatori, deci nu se poate injecta nimic.

---

## 15. Calitate, teste și audit

Detaliile sunt în `AUDIT.md`. Pe scurt:
- **Compilare** cu API-ul real WPF: 0 erori, 0 avertismente.
- **Două revizii independente,** una pentru bug-uri și performanță, una pentru securitate: 20 de probleme găsite, 18 reparate, 2 acceptate cu motivare.
- **233 de teste automate, toate trec,** incluse în proiect și rulabile cu `tests\run-tests.bat`:
  - 210 pentru aplicație (din care 48 pentru acțiuni: id-uri, căutare fără diacritice și ordinea rezultatelor, cine are voie să pornească o acțiune, confirmarea obligatorie, cereri deja anulate, disponibilitate, comutatoare, parametri, erori, timp maxim și anulare, firul interfeței, liste dinamice, jurnal fără valori, toate acțiunile incluse; 54 pentru actualizări: ordinea versiunilor, inclusiv cu sufixe de test, canalul beta, versiunile refuzate, lista de la GitHub, pornirea monitorizată pe toate ramurile — mod sigur, revenire doar după modul sigur automat și recent, blocarea buclelor între versiuni, sănătos după 10 minute chiar și cu somn sau ceas schimbat, oprirea Windows anulată, `startup.json` lipsă, corupt sau ciudat, ordinea pașilor (mutex, notă, schimbarea fișierelor) și eșecul la jumătatea schimbării; și 29 pentru comutatoarele funcțiilor noi: setări vechi, salvare și recitire, `Changed` o singură dată, abonați care dau erori, oprire automată care nu e anulată de „Salvează”, mod sigur, rezumatul de sănătate): autentificarea reciprocă a extensiei, refuzul vechiului token în clar, închiderea conexiunilor neautentificate, copertele (doar PNG/JPEG/WebP, ≤ 300 KB), limitarea duratelor, calendarul ostil (20.000 de evenimente procesate sub 3 s), plus: serverul extensiei și autentificarea, nume de site-uri, protecția adreselor, verdictul de viteză, calculatorul, calendarul, **grila de widget-uri** (locuri libere, limite, mutare cu rearanjare, pagină plină, 2000 de mutări aleatoare fără suprapuneri);
  - 23 pentru extensie, rulate cu un Chrome simulat, inclusiv butoanele următoarea/anterioara și refuzul unui server fals.
- **Revizii independente pentru 0.6** (pagini, editor, drag & drop, teme): 13 probleme găsite, toate reparate.
- **Al doilea audit** (`AUDIT-2.md`): 38 de probleme găsite pe 5 dimensiuni, toate reparate.
- **51 de verificări pe Windows,** de făcut manual, fiecare cu rezultatul așteptat.

---

## 16. Widget-uri, pagini și teme

**Grila.** O pagină are 6 coloane × până la 4 rânduri. Fiecare widget are 2–3 mărimi permise (ex. Muzică 2×1 / 4×2 / 6×2) și își schimbă conținutul după mărime. Notch-ul își ia înălțimea după rândurile folosite. Colțurile cardurilor sunt rotunjite (18 px), cu decupare rotundă, ca fundalurile colorate să nu iasă în colțuri drepte.

**Pagini standard** (Acasă, Sistem, Dispozitive, Unelte):
- rămân mereu la fel;
- au buton de **vizibilitate** (se ascund din tab-uri) și de **duplicare**: copia e o pagină din widget-uri, cu același conținut, pe care o modifici cum vrei.

**Paginile mele:** oricâte; goale, copie după altă pagină sau după una standard; se redenumesc, primesc iconiță, se reordonează, se ascund și se șterg (ștergerea cere a doua apăsare). Cu mai mult de 5 tab-uri, bara arată doar iconițe (numele apare la hover).

**Editare direct în notch** (creionul din dreapta-sus):
- se vede toată grila (6 × 4); trage un widget ca să-l muți: **cât tragi, celelalte se mută live** acolo unde ar ajunge; dacă nu încape, fantoma se face roșie și la eliberare nu se schimbă nimic;
- **click pe un widget** (ca pe iPhone): apar toate mărimile lui ca previzualizări; click pe una și widget-ul se face așa. Tot acolo: „Setări…” și „Scoate”;
- ⊖ (colțul stânga-sus) îl scoate; **arcul din colțul dreapta-jos** se trage ca să-i schimbi mărimea (se oprește la mărimile permise; celelalte se rearanjează live), ca în Control Center pe iPhone. Arcul apare doar când widget-ul are loc de altă mărime;
- „+ Widget” deschide galeria: „Toate” (implicit) sau pe categorii, fiecare widget arătat ca previzualizare live, la mărimea lui implicită. **Trage un widget din galerie pe pagină** și intră la mărimea lui implicită, unde îl lași; **click pe el** în galerie și se deschide un pop-up peste fereastră cu toate mărimile, pe care le poți apăsa sau trage (galeria rămâne cum era);
- „Pagini și setări” deschide fereastra WinNotch; „Gata” închide editarea;
- **în bara de tab-uri apar toate paginile, și cele ascunse (estompate), fiecare cu un ochi: un click o arată sau o ascunde**, fără alte ferestre; la „Gata” cele ascunse ies din bară;
- pe o pagină standard apare doar o notă mică jos („nu se modifică; fă-ți o copie”), pagina rămâne normală;
- în editare notch-ul rămâne deschis până apeși „Gata” (sau `Win+Alt+N`).

**Fereastra WinNotch** (rotița din notch, „Pagini și setări”, iconița din tray): un singur loc pentru tot.
- stânga: paginile standard (ochi, duplicare), paginile tale (le tragi ca să le schimbi ordinea; ochi), „Teme și culori”, „Setări”;
- mijloc: numele și iconița paginii, pagina live pe fundalul notch-ului (editabilă ca în notch, se micșorează pe ecrane mici) și galeria dedesubt, din care tragi widget-uri pe pagină;
- dreapta: widget-ul selectat: mărimile ca previzualizări, opțiunile și „Scoate widget-ul”;
- „Setări”: toate setările de dinainte, cu „Salvează” / „Renunță”;
- se deschide mereu în față, chiar dacă lucrai în altă aplicație, centrată pe monitorul cu mouse-ul, lată (94% din ecran) și complet vizibilă; schimbările din notch apar și în fereastră.

**Widget-uri** (27): Muzică, Surse audio, Volum, Ceas, Calendar, Vremea, Procesor, Memorie, Placă video, Baterie, Consumă acum, Internet, Microfon și cameră, Ieșire și intrare audio, Dispozitive conectate, Lansator, Unelte rapide, Clipboard, Notiță, Spații de lucru, Fereastra activă, plus cele personalizate:
- **Scurtături:** aplicații, foldere, fișiere și link-uri, cu iconițele lor reale (se adaugă cu butoane în editor; doar http/https, fișiere și ms-settings);
- **Senzor:** CPU/GPU/SSD, încărcare, RAM, internet, ca număr, bară sau grafic, cu prag de avertizare și culoare;
- **Text**, **Link web**, **Ceas din alt oraș** (fus orar și etichetă), **Comandă** (rulează un program sau script; ca administrator îl pornește cu drepturi normale, fără argumente).

**Teme** (fereastra WinNotch → „Teme și culori”, se aplică pe loc):
- mod Întunecat, Luminos sau Automat (urmează Windows, verificat la câteva secunde);
- temele Noapte, Grafit, Nord, Contrast mare (întunecate), Luminos, Hârtie (luminoase), fiecare cu previzualizare;
- 16 culori editabile per temă (fundal, carduri, text, accent, bare, stări etc.), cu revenire la culoarea temei;
- rotunjirea colțurilor (12–40 px) și opacitatea fundalului (60–100%);
- „Salvează ca temă nouă”; temele proprii se pot șterge;
- accentul din Setări are prioritate până alegi un accent în editor (sau „Culoarea temei” în Setări).

**Unde se salvează:** în `settings.json`: `Pages`, `HiddenPages`, `ThemeMode`, `ThemeDark`, `ThemeLight`, `ThemeOverrides`, `CustomThemes`, `CornerRadius`, `BgOpacity`.

---

## 17. Limitări cunoscute

- **Notificările Windows** (WhatsApp, Outlook etc.) nu sunt preluate: Windows le dă doar aplicațiilor împachetate ca MSIX.
- **Temperatura procesorului** cere driverul gratuit PawnIO și activarea o singură dată a serviciului de temperatură (confirmare UAC).
- **Recunoașterea textului** depinde de limbile instalate în Windows (pentru diacritice: română cu „Recunoaștere optică”).
- **Pe tab, volumul** se aplică elementelor audio/video din pagină; site-urile care folosesc doar WebAudio (unele jocuri) nu pot fi date mai încet separat, doar oprite pe mut.
- **Legătura cu extensia** folosește un port local. Dacă WinNotch e închis, un alt program local ar putea ocupa portul și ar vedea titlurile tab-urilor care se aud, dar fără să poată controla browserul altfel decât prin comenzile fixe.
- **Exe-ul nu e semnat digital,** așa că SmartScreen poate afișa un avertisment.
- **Revenirea la 0.6.8 sau mai veche:** acele versiuni nu au codul de revenire, deci după o revenire la ele nu apare mesajul „Am revenit la…” și nu țin minte versiunea refuzată: o pot propune din nou. Odată ajunsă pe 0.6.9 sau mai nouă, protecția funcționează complet.
- **Revenirea are nevoie de `WinNotch.old.exe`:** după ce o versiune a fost declarată sănătoasă (10 minute fără erori), fișierul vechi e șters; o problemă apărută mai târziu duce doar la modul sigur, nu la revenire.
- **O închidere forțată** (din Task Manager, o pană de curent) se numără ca închidere bruscă; o singură dată nu are niciun efect.
- **Detectarea se bazează pe reporniri apropiate:** după o închidere bruscă WinNotch nu repornește singur; protecția reacționează când îl pornești din nou (3 porniri în 5 minute). Un WinNotch blocat, dar încă deschis, nu e detectat.

---

## 18. Istoricul versiunilor

| Versiune | Ce a adus |
|---|---|
| 0.1 | Prima variantă: pastilă, hover, muzică cu volum, standby personalizabil, temperaturi, monitoare și fullscreen, mod mic peste ferestre maximizate, tray, setări |
| 0.2 | Reproiectare completă după schițele aprobate: Acasă (versuri, vizualizator, surse audio, calendar, vreme), Sistem (grafic CPU, internet, test viteză), Dispozitive (microfon/cameră, conectate), Unelte (lansator, spații de lucru, fereastra activă, clipboard 20, notiță), pastila mică, pauza pentru ochi |
| 0.2.1 | Conflictul de pachete NU1605 rezolvat |
| 0.3 | Extensia de browser (fiecare tab separat), testul de viteză cu diagnostic de router și istoric, doar sursele care se aud, text mai clar |
| 0.3.1 | Sesiunile media nu se mai amestecă, sursele stau pe loc, mărire automată pe 2K/4K |
| 0.3.2 | Play/pauză instant și copertă corectă din pagină, cardul nu mai sare între surse |
| 0.3.3 | WinNotch nu mai apare ca sursă audio |
| 0.4 | Captură ecran și zonă, text din ecran (OCR), eliberare RAM, scurtăturile `Win+Alt+S` și `Win+Alt+T` |
| 0.4.1 | Preview la capturi și text, progres la RAM, build-ul închide singur versiunea veche, notița se salvează automat |
| 0.5 | Scroll nou, optimizări de performanță, reparații de securitate, audit cu 59 de teste automate |
| 0.5.1 | `build.bat` verifică SDK-ul, îl poate descărca singur și raportează corect eșecurile |
| 0.5.2 | Descărcarea SDK-ului arată pașii, progresul și timpul rămas |
| 0.5.4 | SDK-ul descărcat de `build.bat` e păstrat o singură dată pentru contul tău și folosit de toate versiunile (nu mai întreabă la fiecare arhivă nouă) |
| 0.6.0 | Widget-uri pe grilă 6×4, pagini proprii (goale sau copiate), paginile standard cu ascundere și duplicare, editare în notch (drag, resize, galerie, manager de pagini), fereastra Editor cu inspector, widget-uri personalizate, teme (întunecat/luminos/automat, 6 teme, culori proprii, colțuri, transparență, teme salvate); notch-ul se dă la o parte peste ferestre maximizate; alerta de piesă nouă nu se mai repetă la YouTube și nu apare când sursa e fereastra din față; alerta de volum doar la schimbări reale; next/previous în browser prin handler-ele media ale paginii (extensia 1.5); widget-uri ca pe iPhone (tragi din galerie la mărimea implicită, click pentru toate mărimile, click pe widget pentru redimensionare); o singură fereastră pentru pagini, teme și setări; ochi pe tab-uri în editare; colțuri rotunjite peste tot și widget-ul Muzică aliniat ca pagina Acasă; fereastra notch-ului din nou mică (animații fluide), crește doar cât e deschisă galeria; alertă de memorie cu cine consumă și „Optimizează” |
| 0.6.10 | Pregătire internă pentru Command Bar: registrul de acțiuni (căutare, verificări, anulare), cu toate capabilitățile existente ca acțiuni; nimic schimbat vizibil |
| 0.6.9 | Protecție la o versiune stricată: după 3 închideri bruște în 5 minute repornește în modul sigur, apoi revine singur la versiunea anterioară (cel mult o dată la 30 de minute); versiunea anterioară e păstrată până când cea nouă rulează 10 minute fără erori; versiunile refuzate nu mai sunt propuse; canal beta pentru versiunile de test; compararea corectă a versiunilor (inclusiv 0.6.10 și sufixe -rc); măsurători de performanță (`tools/measure-perf.ps1`) |
| 0.6.8 | „Caută actualizări” în meniul iconiței; o versiune găsită manual e oferită în notch și cu verificarea automată oprită |
| 0.6.7 | Secțiunea „Funcții noi (experimental)” în Setări (comutatoare pentru funcțiile noi, oprire automată după erori repetate), modul sigur `--safe-mode`, rezumatul de sănătate în log la 6 ore |
| 0.6.6 | Actualizări automate din GitHub Releases: construite și testate de GitHub Actions, semnate cu cheia WinNotch și verificate înainte de instalare; ofertă în notch (Actualizează / Mai târziu), progres, repornire; alerte pentru serviciul de temperatură și extensia de reîncărcat |
| 0.6.5 | Remedierea auditului de securitate 3: WinNotch nu mai rulează ca administrator (temperatura procesorului vine dintr-un serviciu SYSTEM read-only), exe blocat cât rulează, startup hooks dezactivate, lansări doar prin Explorer, autentificare reciprocă extensie–aplicație fără token pe rețea, coperte descărcate de browser, limite pentru pagini, calendar și conexiuni, căi de rețea refuzate (extensia 1.6) |
| 0.6.4 | Fereastra WinNotch se deschide centrată pe monitorul cu mouse-ul, lată (94%) și complet vizibilă. În editare se vede toată grila 6×4; cât tragi sau redimensionezi, celelalte widget-uri se mută live unde ar ajunge (fantoma devine roșie unde nu încape). Arcul de redimensionare apare doar dacă widget-ul are loc de altă mărime. Galeria are „Toate” și arată fiecare widget ca previzualizare live |
| 0.6.3 | Widget-ul Memorie are buton ⚡ „Optimizează” (eliberează RAM-ul ținut degeaba, cu progres în notch) |
| 0.6.2 | Editare ca în Control Center pe iPhone: ⊖ pe colțul stânga-sus, arc de redimensionare pe colțul dreapta-jos (fără rotiță; click pe widget = mărimi și setări). Mărimile unui widget din galerie se deschid într-un pop-up peste fereastră, galeria rămâne neschimbată |
| 0.6.1 | În editare, butoanele ⊖ / rotiță stau pe colțuri, pe jumătate în afara widget-ului (ca pe iPhone), iar mărimea apare doar la cel selectat, pe marginea de jos: conținutul widget-urilor nu mai e acoperit. Barele de la Memorie și Baterie sunt centrate, iar textul din dreapta se scurtează în loc să fie tăiat |
| 0.5.3 | Auditul 2: 38 de probleme reparate (calendar complet, surse audio cu mai multe fluxuri, profiluri Chrome, previzualizări pe mut, cod secret pentru extensie, date criptate care nu se mai pierd, pornire ca admin doar la cerere, verificare SHA-512 pentru SDK), 80 de teste automate incluse în proiect (`tests/`) |
