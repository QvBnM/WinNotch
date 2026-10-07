# WinNotch — documentație completă

Versiune: **0.6.13**. Ultima actualizare: 5 octombrie 2026.

WinNotch este un „Dynamic Island” pentru Windows 10 și 11: o pastilă neagră în partea de sus a ecranului.
- **Cât e închisă,** arată informații scurte.
- **La hover,** se deschide cu animație în panouri pentru muzică, sistem, dispozitive și unelte.
- **Când se întâmplă ceva** (volum, piesă nouă, baterie, temperatură, memorie plină), afișează alerte scurte.
- **Se personalizează** cu pagini proprii din widget-uri (ca pe iPhone) și cu teme întunecate, luminoase sau automate.

Documentul descrie tot ce e implementat în cod până la versiunea 0.6.13: paginile din widget-uri, editarea în notch, fereastra WinNotch (pagini, teme, setări) și temele sunt în secțiunea 16.

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
| `Core/Context/*` | Motorul de context, fără WPF: starea curentă, imutabilă (`ContextSnapshot.cs`), interfețele surselor (`ContextSources.cs`), tabelul proces → categorie (`AppCategories.cs`), regulile pentru ecran complet, întâlnire și ieșire audio (`ContextRules.cs`), când se recitește camera (`CameraRefresh.cs`), combinarea surselor cu debounce și evenimentul `Changed` (`ContextEngine.cs`) |
| `Core/Activity/*` | Managerul de activități, fără WPF: activitatea, prioritățile, starea afișată și interfețele (`Activity.cs`), regulile cozii, grupării, pastilei împărțite și peek-ului (`ActivityManager.cs`) |
| `Features/Activity/*` | Partea notch-ului: tabelul celor 27 de alerte existente și regulile lor de repetare (`LegacyAlerts.cs`), alertă → activitate (`ActivityRouting.cs`), acțiunea `activity.dismiss-all` (`ActivityActions.cs`), rutarea, prezentatorul și noile forme ale pastilei (`NotchWindow.Activity.cs`) |
| `Features/CommandBar/*` | Command Bar (P14): logica fără WPF — scurtătura și conflictul, când se deschide, căutarea cu parametri, Enter / confirmare, mărimi (`CommandBarModel.cs`); setările ca acțiuni `settings.*` (`SettingsActions.cs`, `AppSettingsHost.cs`); partea notch-ului (`NotchWindow.CommandBar.cs`) |
| `Features/ContextPages/*` | Pagina după context (P27): regula fără WPF — categoria din snapshot, maparea categorie → pagină, pagini ascunse / șterse, fereastra de 10 minute a alegerii manuale (`ContextPages.cs`); cheia `ContextPages` din setări (`ContextPagesSettings.cs`); rândurile din Setări (`SettingsWindow.ContextPages.cs`); legătura din notch (`NotchWindow.ContextPages.cs`) |
| `Features/QuickActions/*` | Quick Actions (P20): tabelul regulilor și evaluarea fără WPF — doar acțiuni sigure și disponibile din registru, prima regulă potrivită, sugestiile (Activity Manager, una la 10 minute, „Nu mai arăta”) (`QuickActions.cs`); cheia `QuickActionsHidden` din setări (`QuickActionsSettings.cs`); acțiunea `quick-actions.show-hidden` (`QuickActionsActions.cs`); rândul de butoane din notch și sugestiile (`NotchWindow.QuickActions.cs`) |
| `Features/SmartClipboard/*` | Smart Clipboard (P21): recunoașterea fără WPF — JSON, JWT, URL (curățarea de urmărire), e-mail, culoare, IP, cale, telefon, cu limita de 64 KB și ordinea de prioritate (`SmartClip.cs`); verificarea formatelor private ale managerelor de parole, folosită și de istoricul clipboard-ului (`ClipboardPrivacy.cs`); chip-urile, titlul peek-ului și acțiunile `clipboard.*` (`SmartClipboardActions.cs`); setarea `SmartClipboardPeek` (`SmartClipboardSettings.cs`); partea notch-ului (`NotchWindow.SmartClipboard.cs`) și rândul de chip-uri din widget (`SmartClipChips.cs`) |
| `Features/Shelf/*` | Raftul (P23): modelul (20 de referințe, fără dubluri, id-uri stabile), validarea căilor (rețea refuzată înainte de disc), numele noi și regula de hover cât tragi, fără WPF (`Shelf.cs`); scurtăturile `.lnk` / `.url` verificate din octeți (`ShelfShortcuts.cs`); discul, fișierele noi cu `FileMode.CreateNew` și zip-ul (`ShelfFiles.cs`); acțiunile `shelf.*` (`ShelfActions.cs`); cheia `Shelf` din setări (`ShelfSettings.cs`); conversia PNG ↔ JPG și imaginea pentru OCR, pe un fir STA (`ShelfImages.cs`); partea notch-ului: drop, overlay, tragerea în afară (`NotchWindow.Shelf.cs`) |
| `Features/AudioSwitch/*` | Căști/boxe (P30): lista ieșirilor (ordonată, implicita marcată, nume duplicate numerotate, id-uri scurte stabile), serviciul (pornit/oprit de comutator, debounce la notificările de dispozitive, oprirea automată la prima eroare) și acțiunile `audio.output-*`, fără WPF / NAudio / COM (`AudioSwitch.cs`); partea Windows, singura cu COM: lista prin NAudio și schimbarea prin `IPolicyConfig` nedocumentat, pe fir MTA (`PolicyConfigSwitcher.cs`); butonul de lângă volum, lista și mesajul de eroare (`NotchWindow.AudioSwitch.cs`) |
| `Features/Smoke/*` | Modul de test `--smoke`: argumentul, folderul separat și comenzile de test (`SmokeMode.cs`), partea notch-ului (`NotchWindow.Smoke.cs`: starea pentru UI Automation, comenzile) |
| `tests/WinNotch.Smoke/` | Testele de fum (FlaUI) pe exe-ul publicat, rulate în CI după build. `SmokeProgram.cs` are pornirea, ordinea verificărilor, utilitarele comune și verificările de bază; zonele stau în `SmokeAlerts.cs`, `SmokeCommandBar.cs`, `SmokeContext.cs` (P27, P20), `SmokeClipboard.cs` (P21), `SmokeShelf.cs` (P23) și `SmokeAudio.cs` (P30), aceeași clasă `partial` |
| `Features/Context/*` | Sursele Windows ale contextului (`WindowsSources.cs`: hook pentru aplicația din față, notificări audio, rețea, alimentare, monitoare, stick-uri, inactivitate), pornirea (`ContextStartup.cs`), acțiunea `context.show` (`ContextActions.cs`, `NotchWindow.Context.cs`) |
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

Tot ce poate face WinNotch e și o **acțiune** cu un id stabil, în formatul `zonă.verb` (de exemplu `audio.mute-mic`), într-un singur registru (`Core/Actions/ActionRegistry.cs`). Pe el se construiesc Command Bar (P14, mai jos), Quick Actions, Workflows, API-ul local și Undo Center. Butoanele și panourile merg ca înainte; din P14, Command Bar caută și pornește acțiunile (când e pornit).

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
| `device.open-<literă>` | Deschide stick-ul USB în Explorer (câte una pentru fiecare unitate detașabilă; din P20) | Sigură |
| `device.eject-<literă>` | Scoate stick-ul USB (câte una pentru fiecare unitate detașabilă) | Cu confirmare |
| `settings.bluetooth`, `settings.sound`, `settings.display`, `settings.wifi`, `settings.update`, `settings.battery-saver` | Setările Windows: Bluetooth, Sunet, Ecran, Wi-Fi, Windows Update, Economisire baterie (din P20; pagina Windows, unde o pornești tu: nu există un API documentat) | Sigură |
| `winnotch.open`, `winnotch.settings`, `winnotch.speed-test` | Deschide notch-ul, setările WinNotch, testul de viteză | Sigură |
| `settings.standby-items`, `settings.hover-delay`, `settings.position`, `settings.fullscreen`, `settings.mini-after`, `settings.size`, `settings.slim`, `settings.command-bar-key`, `settings.temperatures`, `settings.start-with-windows`, `settings.auto-update`, `settings.beta-channel`, `settings.lyrics`, `settings.eye-break`, `settings.ram-alert`, `settings.calendar`, `settings.browser-tabs`, `settings.workspaces`, `settings.accent`, `settings.weather`, `settings.context-pages`, `settings.clipboard-peek`, `settings.features` | Setările WinNotch, câte una pentru fiecare opțiune (din P14; `settings.context-pages` din P27, `settings.clipboard-peek` din P21): deschid fereastra WinNotch la Setări, derulată la opțiune, cu focusul pe ea. Listele (ce apare în standby, spațiile de lucru, culoarea accent, pagina după context, funcțiile noi) se deschid la secțiunea lor; pragul RAM e lângă „alerta de memorie RAM”, latitudinea și longitudinea lângă oraș („vremea”). Găsite după „Setări: …” sau „setări <opțiune>” („setări poziție”, „setări ochi”) | Sigură |
| `activity.dismiss-all` | Închide toate activitățile din notch (alerta afișată, cele la coadă, „N noutăți” și activitățile persistente). Doar cu „Manager de activități” pornit | Sigură |
| `clipboard.format-json`, `clipboard.minify-json` | Smart Clipboard: pune în clipboard JSON-ul copiat ultima dată, formatat (indentat cu 2 spații) sau compactat; disponibile doar dacă textul e JSON și s-ar schimba ceva (din P21). Doar cu „Smart Clipboard” pornit, ca toate `clipboard.*` | Sigură |
| `clipboard.clean-url`, `clipboard.open-url` | Smart Clipboard: link-ul copiat fără parametrii de urmărire (restul neatins) / deschis în browser (doar http și https, prin `Shell.Open`) | Sigură |
| `clipboard.decode-jwt` | Smart Clipboard: antetul și conținutul token-ului JWT copiat, decodate local, ca JSON formatat în clipboard; semnătura nu e verificată și nu e copiată; fără rețea | Sigură |
| `clipboard.copy-color-rgb`, `clipboard.copy-email`, `clipboard.copy-ip`, `clipboard.copy-phone` | Smart Clipboard: culoarea hex ca `rgb()` / `rgba()`, adresa de e-mail curățată (fără „mailto:”, domeniul cu litere mici), adresa IP normalizată, numărul de telefon doar cu cifre (`+` pentru internațional) | Sigură |
| `clipboard.open-folder` | Smart Clipboard: deschide în Explorer folderul căii copiate (o cale locală cu literă de unitate; niciodată o cale de rețea sau o unitate de rețea mapată; pentru un fișier, folderul lui, fără să-l pornească) | Sigură |
| `shelf.copy-path` | Raft: calea elementului (parametrul `element`: poziția 1–20 sau id-ul lui, niciodată calea) în clipboard (din P23). Doar cu „Raft” pornit, ca toate `shelf.*`; înainte de fiecare, calea e verificată din nou în fundal (lipsă → scoasă din raft) | Sigură |
| `shelf.open-folder` | Raft: deschide în Explorer folderul (pentru un fișier, folderul în care e; niciodată fișierul însuși), prin `Shell.Open` | Sigură |
| `shelf.zip` | Raft: arhivează elementul într-un zip nou, în același folder („x.zip”, „x (2).zip”…; niciodată peste un fișier existent), în fundal; cel mult 10.000 de fișiere și 4 GB; legăturile din folder nu sunt urmate | Sigură |
| `shelf.ocr` | Raft: textul dintr-o imagine (PNG, JPG, BMP, GIF, TIFF; ≤ 100 MB, ≤ 50 MP), cu recunoașterea Windows de la `Win+Alt+T`, în clipboard | Sigură |
| `shelf.convert-image` | Raft: o copie JPG a unui PNG (pe alb, calitate 92) sau o copie PNG a unui JPG, alături, cu nume nou | Sigură |
| `shelf.copy-files` | Raft: pune fișierele pe clipboard ca fișiere, ca un `Ctrl+C` din Explorer (parametrul `elemente`: id-urile sau pozițiile rândurilor bifate, separate prin virgulă; gol sau „tot” = tot raftul). Copierea o face Windows când dai `Ctrl+V` (progresul și întrebarea lui când un nume e luat); WinNotch nu scrie niciun fișier. Elementele care nu mai există sunt sărite (și scoase din raft), cele de pe o unitate deconectată rămân | Sigură |
| `shelf.remove`, `shelf.clear` | Raft: scoate un element / golește raftul (doar referințele; fișierele rămân pe disc) | Sigură |
| `audio.output-<id>` | Căști/boxe (din P30): face din dispozitiv ieșirea audio implicită a Windows (pentru toate rolurile: sistem, multimedia, apeluri). Una pentru fiecare ieșire activă, „Ieșire audio: <nume>” (cea de acum cu „(implicită)”), găsite și după „căști”, „boxe”, „headphones”, „speakers”; `<id>` = 10 cifre hex din SHA-256 al id-ului de endpoint (stabil, fără acolade sau puncte; „-2” la o coliziune). Doar cu „Căști/boxe” pornit; lista se schimbă singură când conectezi / deconectezi un dispozitiv | Sigură |
| `quick-actions.show-hidden` | Quick Actions: toate regulile ascunse cu „Nu mai arăta” pot sugera din nou (din P20). Doar cu „Quick Actions” pornit | Sigură |
| `context.show` | Arată contextul curent în notch (pentru depanare: categoria și procesul aplicației din față, ecran complet, întâlnire, media, microfon, ieșire audio, rețea, alimentare, monitoare, stick USB, inactivitate; niciodată titlul ferestrei). Doar cu „Motorul de context” pornit | Sigură |

### Context (infrastructură, din 0.6.11)

Un singur loc știe **ce face utilizatorul acum** (`Core/Context/ContextEngine.cs`, ADR 0004) și anunță schimbările ca evenimente. Peste el vor veni Quick Actions, Game Mode, Dev Mode și paginile alese după context. **În 0.6.11 nu se schimbă nimic vizibil;** motorul are comutatorul „Motorul de context” (`context-engine`, Beta, pornit implicit fiindcă nu are interfață; oprit în `--safe-mode`).

- **Ce știe** (o stare imutabilă, `ContextSnapshot`): aplicația din față (procesul, titlul ferestrei și categoria: Programare, Browser, Întâlnire, Joc, Media, Birou, Creație sau Altele), ecranul complet (joc, video sau altceva), ce se redă (doar aplicația sau site-ul, nu titlul piesei), microfonul și camera în uz și de cine, ieșirea audio (căști, boxe, Bluetooth), întâlnirea activă (Teams, Zoom, Meet, Webex…), rețeaua (online, Wi-Fi / Ethernet / offline), alimentarea (baterie sau priză, procentul), numărul de monitoare, stick USB conectat și inactivitatea (fără tastatură și mouse de cel puțin 2 minute).
- **Întâlnire:** o aplicație de întâlniri folosește microfonul sau camera; sau e în față fereastra de ședință Teams / Zoom (după cuvinte întregi din titlu: „Meeting”, „Ședință”, „Call”, „Apel”…; secțiunea „Calls” / „Apeluri” din Teams nu e o întâlnire); sau un browser e pe Google Meet cu codul camerei în titlu („Meet - abc-defg-hij”) ori cu microfonul folosit de browser, sau pe Teams / Zoom pe web. Teams doar deschis nu înseamnă întâlnire.
- **Ecran complet:** fereastra din față acoperă tot monitorul (nu doar maximizată). Joc: Windows raportează un joc pe tot ecranul (Direct3D exclusiv) sau aplicația e un joc cunoscut; video: un player, sau un browser care redă el însuși ceva (muzica din Spotify cu o pagină pe F11 nu e „video”); altfel „altceva” (o prezentare, F11).
- **Cum află:** din evenimentele Windows (schimbarea ferestrei din față, a ieșirii audio, a rețelei, a alimentării, a monitoarelor, conectarea unui stick, schimbările din media, schimbarea cheii camerei din registru), fără verificări dese. Doar ce nu are eveniment e citit la 3 secunde: inactivitatea, microfonul (din aceeași sursă cu indicatorul Windows, cu cache de 2 s) și dacă fereastra din față a trecut pe tot ecranul. Dacă Windows nu poate anunța schimbările camerei, ea e verificată la 10 s doar cu notch-ul deschis; în standby e considerată „nefolosită”.
- **Pornire și oprire sigure:** schimbările comutatorului venite deodată din mai multe locuri sunt aplicate pe rând; ultima câștigă.
- **Fără rafale:** schimbările sunt adunate 300 ms (un Alt+Tab rapid prin zece ferestre e un singur eveniment) și publicate cel târziu la 1 s; fiecare eveniment spune exact ce câmpuri s-au schimbat.
- **Erori:** o sursă care dă erori își păstrează ultima valoare bună și e încercată din nou după 30 s, apoi tot mai rar; se numără o singură dată la rezumatul de sănătate, iar motorul merge mai departe. Trei surse diferite stricate opresc funcția (motivul apare în Setări).
- **Oprit din Setări:** toate sursele se opresc (inclusiv hook-ul ferestrei din față), starea se golește și nu mai pleacă niciun eveniment.
- **Log:** doar procesul și categoria (de exemplu „Context: aplicație zoom (Meeting), ecran complet Other, întâlnire Zoom…”), și doar la schimbările rare (ecran complet, întâlnire, ieșire audio, rețea, monitoare, stick); niciodată titlul ferestrei sau mesajul unei erori.

### Manager de activități (din 0.6.14, experimental)

Toate alertele notch-ului pot trece printr-un singur loc care decide ce se vede (`Core/Activity/ActivityManager.cs`, ADR 0006).
Comutatorul „Manager de activități” (`activity-manager`, **Experimental, oprit implicit**, oprit în `--safe-mode`). **Oprit,
alertele merg exact ca înainte** (aceeași cale de cod). Pornit, alertele arată la fel (același conținut, mărime și durată),
dar se schimbă cum se succed:

- **Prioritate:** alertele importante de azi (baterie, temperatură, unelte, actualizare, pauză pentru ochi, `context.show`)
  sunt High, celelalte Normal. Una nouă de prioritate egală sau mai mare o înlocuiește pe cea afișată (ca înainte); una mai
  puțin importantă așteaptă și apare după (de exemplu o piesă nouă nu mai acoperă „Baterie descărcată”), dar nu dacă a
  așteptat mai mult de 10 s. Coada are cel mult 50 de locuri. Critical (nicio alertă de azi) întrerupe orice.
- **Aceeași alertă se actualizează pe loc:** volumul tras, pașii unui flux (OCR „Citesc textul…” → „Text copiat”, RAM
  „Eliberez memoria…” → rezultat, descărcarea actualizării → „Instalez…” sau „refuzată”).
- **Notch deschis:** alertele nu apar și nu reapar după închidere (ca înainte); Critical (fără butoane) și activitățile
  persistente așteaptă închiderea. **Peste un joc sau video pe tot ecranul:** doar alertele High și Critical (ca înainte,
  „importante”); o activitate persistentă mai puțin importantă e păstrată și apare când ecranul complet se termină.
- **„N noutăți”:** peste 3 alerte diferite în 5 secunde (nu cele importante, nu cele cu butoane, nu actualizările aceleiași
  alerte) devin o singură pastilă „4 noutăți”, „5 noutăți”… care crește cât continuă rafala și dispare după 4 s; alertele
  din rafală nu mai apar și separat. Click-ul trece prin ea, ca prin alertele obișnuite. **Pentru funcțiile viitoare; azi
  apare doar în testele de fum:** alertele de acum care se pot grupa (volum, piesă, încărcător) sunt doar trei, sub prag.
- **Activități persistente** (pentru funcțiile viitoare, de exemplu activitățile live din browser): stau până sunt închise;
  o alertă trece peste ele și apoi revin. Două deodată împart pastila în două jumătăți (iconiță și titlu în fiecare).
- **Activități discrete (Low):** pastila mică se lărgește puțin, 2 secunde, cu un titlu scurt („peek”).
- **Alertele cu butoane** (memorie, captură, pauză pentru ochi, actualizare, extensie, serviciul de temperatură) apar imediat
  sau deloc, ca înainte; nu așteaptă la coadă.
- **Fără consum în standby:** un singur cronometru, doar cât se vede o alertă. În log ajung doar „Activity Manager:
  pornit/oprit”, niciodată titlurile alertelor (pot conține numele piesei).
- **Acțiunea `activity.dismiss-all`** închide tot ce arată managerul.

### Command Bar (din P14, experimental)

O bară de comenzi pentru tot ce poate face WinNotch (ADR 0007). Comutatorul „Command Bar” (`command-bar`, **Experimental,
oprit implicit**, oprit în `--safe-mode`; nu urmează 0.7.0 imediat, vezi `docs/PLAN.md`). **Oprit, nu se înregistrează nicio
scurtătură și nu se schimbă nimic.**

- **Scurtătura:** `Win+Alt+Space`, sau `Win+Alt+K` (Setări › Comportament › „Scurtătura Command Bar”). E înregistrată doar
  cât comutatorul e pornit și eliberată când e oprit. Dacă e folosită de altă aplicație, apare **o singură alertă** (prin
  calea obișnuită a alertelor, deci prin Managerul de activități când e pornit): „Win+Alt+Space e folosită de altă
  aplicație · Alege Win+Alt+K în Setări › Comportament › Scurtătura Command Bar”; sub opțiunea din Setări apare același
  mesaj. Nu se adaugă alte scurtături. Aceeași scurtătură închide bara.
- **Pastila devine un câmp de căutare** (560 px, rază 18, culorile temei) peste căutarea registrului de acțiuni: fără
  diacritice și majuscule, după titlu, cuvinte-cheie și categorie; listele dinamice (spații de lucru, stick-uri) sunt
  recitite la fiecare deschidere. Fără text: acțiunile folosite recent. Cel mult 7 rezultate, cu iconița, titlul,
  valoarea citită și categoria; „cere confirmare” la cele cu confirmare. **Acțiunile periculoase nu apar niciodată.**
- **Parametri în text:** cuvintele de la final sunt valorile acțiunii, verificate cu regulile ei („volum 30”, „volum 45%”);
  o valoare greșită („volum 150”) nu se leagă. Fără valoare, Enter spune ce lipsește („Scrie și „Volum” după comandă”).
- **Taste:** săgețile sus / jos (circular), Enter pornește acțiunea selectată (sau un click pe rând), Esc închide.
  **Confirmare:** la o acțiune cu confirmare (scoaterea unui stick), primul Enter arată „Apasă Enter din nou pentru a
  confirma”; orice modificare a textului sau mutare a selecției anulează confirmarea. Acțiunea pornește mereu prin
  registru (`ActionRegistry.Current.InvokeAsync`, ca „CommandBar”), cu toate verificările lui; dacă nu merge, o alertă scurtă
  spune de ce („Comanda nu a mers”).
- **Focus:** la deschidere bara primește tastatura; la Esc, Enter sau un click în altă parte (fereastra pierde focusul) o dă
  înapoi ferestrei care era în față — exact ca lansatorul din Unelte (`EnableTyping` / `StopTyping` → `LastForeground`). Dacă
  ai dat click în altă fereastră, aceea rămâne în față. Enter închide bara întâi, apoi pornește acțiunea (de exemplu
  „Fereastra pe jumătate” se aplică ferestrei tale). **Peste un joc sau video pe tot ecranul scurtătura nu deschide nimic**
  (fereastra din față acoperă monitorul sau e Direct3D exclusiv, citit ca în motorul de context; sau pastila e ascunsă pentru
  ecran complet); nici cât rulează o unealtă de ecran sau modul de editare.
- **Alertele cât e deschisă bara:** notch-ul e considerat deschis, deci pe ambele căi alertele se comportă ca la notch-ul
  deschis: calea veche nu le arată; cu Managerul de activități pornit, Critical și activitățile persistente așteaptă și apar
  după închidere, celelalte nu apar. Nicio alertă nu ia pastila cât scrii.
- **Fără consum:** nimic nu rulează cu bara închisă (niciun cronometru, nicio lucrare pe cadru); interfața ei e construită la
  deschidere și scoasă la închidere. În log: doar „Command Bar: pornit/oprit/deschis/închis” și starea scurtăturii;
  **niciodată textul scris sau valorile parametrilor** (registrul scrie doar id-ul și rezultatul).

### Pagina după context (din P27, experimental)

Notch-ul se deschide pe pagina potrivită pentru ce faci acum (ADR 0008). Comutatorul „Pagina după context”
(`context-pages`, **Experimental, oprit implicit**, oprit în `--safe-mode`). **Oprit (sau cu „Motorul de context” oprit),
notch-ul se deschide pe pagina de data trecută, ca înainte.**

- **Setări › „Pagina după context”:** câte un rând pentru fiecare categorie a motorului de context — Programare (Dev),
  Browser, Întâlnire (Meeting), Joc (Game), Media, Birou (Office), Creație (Creator) — cu o listă: „—” (nicio schimbare,
  implicit) sau o pagină (cele standard și ale tale; cele ascunse sunt marcate „(ascunsă)”). Se salvează în `settings.json`,
  cheia `ContextPages` (categorie → id-ul paginii: `home`, `system`, `devices`, `tools` sau id-ul paginii tale, nu poziția
  ei); o cheie lipsă (sau un fișier mai vechi) înseamnă „—”.
- **La deschidere** (hover, `Win+Alt+N`, click), o singură dată, notch-ul citește snapshot-ul motorului de context
  (`ContextEngine.Current.Snapshot`; nicio sursă proprie, nicio abonare, niciun cronometru): o întâlnire în curs înseamnă
  „Întâlnire” (și Meet în browser), un joc pe tot ecranul înseamnă „Joc”, altfel categoria aplicației din față. Dacă acea
  categorie are o pagină, notch-ul se deschide pe ea.
- **Alegerea ta are prioritate:** dacă schimbi singur pagina în notch (click pe un tab), contextul nu o mai schimbă timp de
  **10 minute** (la 10:00 fix alege din nou). Nu se păstrează după repornire.
- **Pagină ascunsă sau ștearsă:** e sărită în tăcere (notch-ul rămâne unde era), fără mesaje și fără rânduri în log.
- **Log:** doar „Pagina după context: <categorie>.” când pagina chiar se schimbă; niciodată aplicația sau titlul ferestrei.
  O eroare merge la comutator (`ReportError`; trei în 10 minute îl opresc).

### Quick Actions (din P20, experimental)

Câteva butoane sub conținutul notch-ului, după ce faci acum (ADR 0009). Comutatorul „Quick Actions” (`quick-actions`,
**Experimental, oprit implicit**, oprit în `--safe-mode`). **Oprit (sau cu „Motorul de context” oprit), nu apare nimic și
nu e nimic abonat.**

- **La deschidere** (hover, `Win+Alt+N`, click), o singură dată, notch-ul citește snapshot-ul motorului de context și
  alege din tabelul de reguli (`QuickActionRules.Table`, în `Features/QuickActions/QuickActions.cs`) **prima** regulă care se
  potrivește; sub pagină apare un rând de 1–4 butoane mici, rotunjite, în culorile temei (stick: doar „Deschide”, fiindcă
  „Scoate” cere confirmare). Rândul e același pe ambele căi ale alertelor („Manager de activități” oprit sau pornit), dispare
  la închiderea notch-ului și în modul de editare și revine la ieșirea din editare.

  | Regula (id) | Când | Butoane (acțiuni) | Sugestie nesolicitată |
  |---|---|---|---|
  | `meeting-headphones` | Întâlnire în curs și căști (și Bluetooth) | „Mută / pornește microfonul” (`audio.mute-mic`), „Volum 40%” (`audio.volume-set`, 40) | Da |
  | `battery-low` | Pe baterie, sub 20% | „Economisire” (`settings.battery-saver`), „Luminozitate” (`settings.display`) | Nu (alerta de baterie există deja) |
  | `media-playing` | Se redă ceva | „Pauză” (`media.play-pause`), „Următoarea” (`media.next`) | Nu (alerta de piesă există deja) |
  | `usb-drive` | Stick USB conectat | „Deschide <stick>” (`device.open-<literă>`); „Scoate” (`device.eject-<literă>`) cere confirmare, deci nu apare | Da |

- **Doar acțiuni sigure și disponibile acum:** butoanele vin din registrul de acțiuni, după id. O acțiune care lipsește,
  nu e disponibilă (nu se redă nimic, stick-ul a fost scos), ține de o funcție oprită, cere confirmare sau e periculoasă
  **nu apare**. O regulă rămasă fără niciun buton cedează locul următoarei. Un click pornește acțiunea doar prin registru
  (`ActionRegistry.InvokeAsync`, ca „QuickAction”, cu toate verificările lui); notch-ul rămâne deschis, iar la capătul rândului
  apare 4 s rezultatul, scurt (de exemplu „Microfon oprit (în toate aplicațiile)” sau, la eșec, „Unitatea nu mai e conectată.”).
  Butonul stick-ului se bazează pe motorul de context (stick conectat), nu citește unitățile la deschidere; lista lor e citită
  din nou în fundal când motorul vede un stick conectat sau scos.
- **Sugestii nesolicitate** (doar cu „Manager de activități” pornit): când o regulă marcată „Da” începe să se potrivească
  (de exemplu intri într-un apel cu căștile puse), pastila se lărgește 2 s cu „<regula> · acțiuni rapide la hover” (o
  activitate Low, prin managerul de activități). **Cel mult una la 10 minute**, pentru toate regulile; una care nu s-a afișat
  imediat (notch deschis, ecran complet, pusă la coadă sau strânsă în „N noutăți”) nu pornește cele 10 minute. În rândul de butoane, pentru regulile cu sugestii, apare
  „Nu mai arăta”: regula nu mai e sugerată (butoanele ei rămân la deschidere). Se salvează în `settings.json`, cheia
  `QuickActionsHidden` (id-urile regulilor); acțiunea `quick-actions.show-hidden` le readuce. Cu managerul oprit: nicio sugestie.
- **Fără polling:** rândul citește snapshot-ul la deschidere; sugestiile ascultă `ContextEngine.Changed` (doar întâlnirea,
  ieșirea audio și stick-ul), cu dezabonare la oprirea comutatorului.
- **Log:** doar „Quick Actions: pornit/oprit.”, „sugestie <id regulă>.”, „sugestie amânată (<id regulă>…)” și „sugestiile
  pentru „<id regulă>” nu mai apar.”; registrul scrie id-ul acțiunii și rezultatul. Niciodată aplicația, titlul ferestrei sau
  valorile parametrilor. O eroare merge la comutator (`ReportError`; trei în 10 minute îl opresc).
- **Adaugi o regulă:** un rând nou în `QuickActionRules.Table` (id nou, condiția pe snapshot, câmpurile citite, 2–4 id-uri
  de acțiuni, dacă poate fi sugerată); testele QA2–QA4 verifică tabelul și că acțiunile există.

### Smart Clipboard (din P21, experimental)

Widget-ul Clipboard recunoaște ce ai copiat ultima dată și îți dă câteva butoane mici (chip-uri) sub titlu (ADR 0010).
Comutatorul „Smart Clipboard” (`smart-clipboard`, **Experimental, oprit implicit**, oprit în `--safe-mode`). **Oprit,
widget-ul arată exact ca înainte** (rândul de chip-uri are înălțimea 0), nimic nu e recunoscut și acțiunile `clipboard.*`
nu sunt disponibile.

- **Ce recunoaște,** în această ordine (primul tip care se potrivește câștigă; un text de peste 64 KB nu e analizat deloc;
  în afară de JSON, doar un singur rând):

  | Tip | Ce e | Chip-uri (acțiuni) |
  |---|---|---|
  | JSON | Un obiect `{…}` sau o listă `[…]`, tot textul (nimic după), cel mult 64 de niveluri; o listă doar cu un obiect / o listă înăuntru sau de cel puțin 8 caractere („[1]” e o notă de subsol); un număr sau un șir singur nu e JSON aici | „Formatează” (`clipboard.format-json`), „Compactează” (`clipboard.minify-json`), fiecare doar dacă ar schimba ceva |
  | JWT | Trei părți base64url; antetul un obiect JSON cu „alg”, conținutul un obiect JSON | „Decodează” (`clipboard.decode-jwt`) |
  | Link | `http://` sau `https://` cu o gazdă reală (cu punct, `localhost` sau IP), fără spații | „Curăță link-ul” (`clipboard.clean-url`, doar cu parametri de urmărire), „Deschide” (`clipboard.open-url`) |
  | E-mail | nume@domeniu.tld (și cu „mailto:”) | „Copiază adresa” (`clipboard.copy-email`) |
  | Culoare | `#RRGGBB`, `#RRGGBBAA` sau `#RGB` cu cel puțin o literă („#123” pare un număr de issue, nu o culoare) | Mostra culorii și „Copiază rgb()” (`clipboard.copy-color-rgb`) |
  | IP | IPv4 scris strict (patru numere 0–255, fără zerouri în față) sau IPv6 (cu cel puțin o cifră: „::” singur nu) | „Copiază IP-ul” (`clipboard.copy-ip`) |
  | Cale | O cale locală cu literă de unitate (`C:\…`, și între ghilimele); căile de rețea nu sunt căi aici | „Deschide folderul” (`clipboard.open-folder`) |
  | Telefon | `+` și 8–15 cifre, `00` și 10–17 cifre sau național, cu 0 la început și 9–15 cifre; separatoare: spații, `-`, `/`, o pereche de paranteze | „Copiază numărul” (`clipboard.copy-phone`) |

  Nu sunt recunoscute (testat): date („2026-10-06”), sume („1.234,56”), numere scurte („12345”), „{nu e json}”, „[1,2,”,
  „{} extra”, „#hashtag”, „#12345”, adrese și link-uri greșite, căi relative sau de rețea, JWT-uri cu base64 invalid sau
  fără JSON. Recunoașterea e scrisă de mână (fără expresii regulate, deci nimic nu poate „exploda” la un text ciudat).
- **Curățarea link-urilor** scoate doar parametrii de urmărire cunoscuți, scriși exact așa (cu litere mici): orice `utm_…`
  și `fbclid`, `gclid`, `dclid`, `gbraid`, `wbraid`, `msclkid`, `mc_cid`, `mc_eid`, `yclid`, `igshid`, `igsh`, `_hsenc`,
  `_hsmi`, `mkt_tok`, `ref_src`, `twclid`, `ttclid`, `li_fat_id`. Restul link-ului rămâne octet cu octet (ceilalți
  parametri și ordinea lor, codificarea `%..`, fragmentul de după `#`); fără niciun parametru rămas dispare și „?”.
  `UTM_SOURCE` sau `Fbclid` rămân: ar putea fi ai site-ului.
- **Un click pe chip** pornește acțiunea doar prin registru (`ActionRegistry.InvokeAsync`, cu verificările lui) și pune
  rezultatul în clipboard (sau deschide link-ul / folderul); rezultatul apare 3 s la capătul rândului. Textul pus de
  WinNotch e marcat ca al lui: nu intră din nou în istoric, nu e analizat ca o copiere nouă și nu dă peek (fără bucle), dar
  chip-urile îl urmează (după „Formatează” apare „Compactează”). Acțiunile se găsesc și în Command Bar.
- **Chip-urile sunt pentru textul copiat ultima dată** (cel care a intrat în istoric), cât timp e pornit comutatorul.
  O imagine copiată sau un text privat golește rândul (altfel chip-urile ar lucra pe ce era înainte).
- **Parolele rămân ignorate,** ca înainte: un text marcat de managerul de parole („ExcludeClipboardContentFromMonitorProcessing”,
  „Clipboard Viewer Ignore”, „CanIncludeInClipboardHistory” / „CanUploadToCloudClipboard” = 0, orice eroare la citire) nu
  intră în istoric și **nu e analizat**. Verificarea e aceeași pentru istoric și pentru Smart Clipboard (`ClipboardPrivacy`).
- **Peek la copiere** (Setări › Smart Clipboard, **implicit oprit**, doar cu „Manager de activități” pornit): la un JSON de
  formatat, un link cu urmărire sau un JWT, pastila se lărgește 2 s cu un titlu fix („JSON copiat · Formatează”, „Link cu
  urmărire copiat · Curăță”, „Token JWT copiat · Decodează”), o activitate Low prin managerul de activități. Fără el, nimic.
  Se salvează în `settings.json`, cheia `SmartClipboardPeek`.
- **Fără polling:** doar notificarea de clipboard pe care notch-ul o ascultă deja; chip-urile se redesenează cu widget-ul
  (cam o dată pe secundă cât e vizibilă pagina) și numai când s-a schimbat textul.
- **Log:** **nimic din conținut** — nici textul, nici tipul lângă text. Doar „Smart Clipboard: pornit.” și „Smart
  Clipboard: oprit (N recunoașteri).”; registrul scrie id-ul acțiunii și rezultatul („Acțiune clipboard.format-json (UI):
  reușită”). Acțiunile nu au parametri, deci conținutul nu trece prin registru. O eroare merge la comutator (`ReportError`).
- **Unde apar:** în widget-ul Clipboard (pe o pagină de-a ta). Lista de clipboard din pagina standard Unelte rămâne ca
  înainte.

### Raft (din P23, experimental)

Un loc în notch pentru fișierele pe care le folosești acum: le tragi peste notch și le ai la îndemână (ADR 0011).
Comutatorul „Raft” (`shelf`, **Experimental, oprit implicit**, oprit în `--safe-mode`). **Oprit, nimic nu se schimbă:**
notch-ul nu primește fișiere, nu are butonul „Raft”, iar regula de hover e exact cea de dinainte; căile salvate rămân în
`settings.json` pentru când îl pornești din nou.

- **Cum pui un fișier pe raft:** pastila închisă lasă click-urile să treacă spre fereastra de dedesubt, deci nu poate primi
  un fișier (spike-ul din ADR 0011). Tragi fișierul (sau mai multe, ori un folder) din Explorer **peste pastilă și aștepți
  ca la hover**: notch-ul se deschide singur, cu raftul arătat, și dai drumul oriunde pe el. Cu notch-ul deja deschis
  (hover, `Win+Alt+N`), tragi direct peste el; raftul apare singur cât tragi fișiere (nu și la un text sau o fereastră
  trasă; ce nu poate primi notch-ul e refuzat, niciodată „mutat”). Butonul „Raft N” din antet îl deschide și
  închide. Cursorul arată „link”: raftul ține **doar calea** (o referință), **nu o copie**; fișierul nu e mutat niciodată.
- **Ce primește:** căi locale cu literă de unitate (discuri, stick-uri, CD), fișiere și foldere care există. **Refuzate:**
  căile de rețea în orice formă (`\\server\share`, `//server`, `\\?\UNC\…`, `\\?\C:\…`, `\\.\…`, `file://server`), **unitățile
  mapate de rețea** (refuzate înainte să fie atinsă calea), scurtăturile `.lnk` / `.url` spre rețea sau care nu se pot
  verifica local, fișierele `.scf`, `.library-ms`, `.searchconnector-ms`, căile ciudate (`..`, nume de dispozitiv ca CON
  sau NUL, fluxuri `a.txt:x`). Mesajul de sus spune câte au intrat și de ce n-au intrat celelalte (fără nume de fișiere).
- **Cel mult 20 de elemente,** în ordinea în care au venit, fără dubluri (aceeași cale scrisă altfel nu intră a doua oară).
  **Al 21-lea e refuzat** cu „raftul e plin (maximum 20)”: nimic de pe raft nu dispare fără să ceri. Rămân până golești
  raftul, și după repornire (cheia `Shelf` din `settings.json`, salvată ca restul setărilor, printr-un fișier temporar).
  Un element al cărui fișier nu mai există dispare discret la deschiderea notch-ului (verificat în fundal, fără timer;
  un stick adormit nu blochează notch-ul) sau la prima acțiune pe el. Pe un stick scos, elementele rămân (dispar doar
  dacă unitatea e prezentă și fișierul lipsește). Legăturile simbolice și junction-urile nu intră în raft.
- **Pe fiecare element** (butoane mici, rotunjite): „Copiază calea” (`shelf.copy-path`), „Deschide folderul”
  (`shelf.open-folder`: al fișierului, fără să-l pornească; un folder se deschide pe el însuși), „Arhivează” (`shelf.zip`: un
  zip nou alături, „x.zip”, „x (2).zip”…, niciodată peste un fișier existent; în fundal), „Text din imagine” (`shelf.ocr`,
  doar pe imagini; textul în clipboard), „Fă o copie JPG / PNG” (`shelf.convert-image`, doar pe PNG și JPG; fișier nou
  alături) și „Scoate din raft” (`shelf.remove`). Sus, „Copiază selecția” (`shelf.copy-files`) și „Golește” (`shelf.clear`). Fișierele făcute de raft (zip-ul, copia)
  intră și ele pe raft dacă e loc. Fiecare buton trece prin registru (`ActionRegistry.InvokeAsync`); rezultatul apare câteva
  secunde sub titlu. Acțiunile se găsesc și în Command Bar („raft copiază calea 2”).
- **Bifa de pe fiecare rând și copierea în bloc:** la începutul fiecărui rând e o bifă. Butonul din antet spune pe ce
  lucrează — „Copiază selecția (N)” când ai bifat ceva, „Copiază tot (N)” când nu —, iar un click pune fișierele pe
  clipboard **ca fișiere**, exact ca un `Ctrl+C` din Explorer: te duci în folderul în care le vrei și dai `Ctrl+V`. Copierea
  e a Windows-ului (bara lui de progres, întrebarea lui „Înlocuiește / Sari peste / Păstrează ambele” când un nume e deja
  luat, anularea lui); WinNotch nu copiază, nu mută și nu scrie niciun fișier, iar lista rămâne pe clipboard și după ce
  închizi WinNotch. Merge la fel într-un e-mail, în Word sau într-un chat. Bifele stau în memorie cât ține sesiunea (nu
  ajung în `settings.json`) și se uită singure când un element iese din raft.
- **Tragi un element din raft** într-o altă aplicație (Explorer, un e-mail, un chat) și îl primește ca fișier (copie sau
  link, niciodată mutare), doar dacă e local și nu a fost găsit lipsă. Dacă tragi un rând bifat, **toate rândurile bifate
  merg împreună** (un rând nebifat pleacă singur).
- **Iconițele** sunt din fontul aplicației, după tipul din nume (folder, imagine, arhivă, document, fișier): raftul nu
  cere niciodată iconița Windows (Shell) a unui fișier și nu rezolvă scurtăturile.
- **Log:** doar contoare — „Raft: pornit (N elemente).”, „Raft: oprit.”, „Raft: adăugate N, refuzate M (din rețea K),
  dubluri D, peste limită L.”, „Raft: N elemente care nu mai există, scoase.”; registrul scrie id-ul acțiunii și rezultatul.
  Nicio cale, niciun nume de fișier. O eroare merge la comutator (`ReportError`).

### Plasa de siguranță a notch-ului (din 0.6.18, B1)

Reparația B1 (ADR 0013): pe 0.6.17 notch-ul se deschidea uneori **gol** (panoul mare, cu fundalul temei, fără tab-uri și
fără pagină), iar pastila arăta uneori **doar ora**. Comutatorul „Plasa de siguranță a notch-ului” (`notch-guard`,
**Stabil, pornit implicit**, rămâne pornit și în `--safe-mode`). **Oprit:** nicio verificare și nicio reparație (reparațiile
de cauză de mai jos rămân).

- **Cauzele găsite și reparate:**
  1. **Un strat colapsat de o animație veche** (`FadeLayer`): o ascundere înlocuită de o afișare cu întârziere (120–140 ms)
     își termina totuși animația și colapsa stratul care tocmai se arăta. Rezultatul: standby-ul, forma mică sau o alertă
     (chiar una mare, de exemplu Memorie sau Captură) rămâneau goale, doar cu fundalul. Acum fiecare animație a unui strat
     ia un jeton și doar cea mai nouă îl poate colapsa (la fel pentru panoul notch-ului).
  2. **Iconița vremii invizibilă pe tema luminoasă:** galbenul și liliachiul deschis, fixe, nu se vedeau pe pastila deschisă
     la culoare, iar fără date de vreme rămânea doar „—”: pastila părea să arate doar ora. Pe temele luminoase iconița ia
     culorile temei (soare: „Atenție”, lună / nori: „Informație”, fără date: „Text secundar”); temele întunecate rămân la fel.
- **Verificarea:** la **300 ms după fiecare deschidere** (dacă panoul doar nu e încă opac, încă 300 ms: fade-ul lui durează 340 ms) (hover, `Win+Alt+N`, raft, pagina după context, editare) se citește
  ce e cu adevărat pe ecran: panoul (vizibil și opac), tab-urile, pagina curentă (pusă în panou și vizibilă), pastila. La
  **450 ms după fiecare schimbare a pastilei** (standby, forma mică, alertă, Command Bar; animațiile s-au terminat):
  stratul modului e vizibil, forma mică are data (nu goală, nu tăiată), alerta are conținut, niciun strat din alt mod deasupra.
  Nimic nu rulează periodic: două cronometre de o singură dată, pornite de deschidere și de schimbarea pastilei.
- **Reparația:** notch-ul deschis gol → **pasul 1:** panoul și pastila arătate din nou, pagina curentă pusă înapoi, tab-urile
  refăcute; verificat din nou după 300 ms → **pasul 2:** toate paginile refăcute (ca la schimbarea temei) și **Acasă**
  deschisă; tot gol → scris în log, se încearcă din nou la următoarea deschidere. Command Bar care nu se vede → închis.
  Pastila goală → stratul potrivit arătat pe loc, celelalte ascunse, standby-ul refăcut (cel mult de 3 ori pe minut; panoul, de cel mult 4 ori).
- **Log:** câte un rând **„B1 recover: …”** cu problemele (`PanelHidden`, `PanelTransparent`, `NoTabs`, `NoPage`,
  `PageHidden`, `PillTransparent`, `CommandBarHidden`, `IdleEmpty`, `MiniEmpty`, `MiniWithoutDate`, `LiveEmpty`,
  `WrongLayer`), modul, straturile (opacitatea panoului, numărul de tab-uri; la pastilă standby / mică / alertă), pagina
  (`home`, `system`, `devices`, `tools`, `sources` sau „proprie”, niciodată numele paginii tale), comutatoarele pornite,
  activitatea curentă (felul și prioritatea, fără titlu), overlay-urile (galerie, raft, ieșire-audio, quick-actions,
  command-bar, editare…) și pasul făcut; apoi un rând cu rezultatul („conținutul se vede” sau „tot gol”).
- **Formă mică — regulile:** pastila se strânge în forma mică (196 × 22 px, **„ora · data”**) doar **peste o fereastră
  maximizată** (cu setarea pornită; nu peste un joc pe tot ecranul) sau **după inactivitatea aleasă** în Setări (implicit
  10 s; „niciodată” = doar peste ferestre maximizate). Inactivitate = nicio atingere a notch-ului: hover, o alertă sau
  închiderea notch-ului readuc **standby-ul complet**. Data nu e niciodată goală; ora singură e tratată ca problemă.

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
- **O alertă nu stă în cale (P51b, comutatorul „Alertele nu stau în cale”).** O alertă e informație, nu o stare: dacă începi
  să tragi fișiere peste notch, apeși `Win + Alt + N`, deschizi Command Bar-ul, alegi ceva din meniul iconiței sau deschizi un
  panou, alerta se dă la o parte pe loc și acțiunea se execută. Hover-ul deschide notch-ul peste o alertă de informare (volum,
  piesă nouă), dar **nu** peste una la care trebuie să apeși ceva (actualizare, memorie plină, pauză pentru ochi, confirmări) —
  acolo butonul trebuie să rămână apăsabil. Mișcarea obișnuită a mouse-ului, tastatul în altă aplicație și o altă alertă de
  prioritate mai mică nu întrerup nimic. O alertă dată la o parte nu revine în următoarele 30 de secunde (altfel ar întrerupe
  chiar acțiunea pentru care s-a retras); dacă avea o acțiune nefăcută (actualizarea), se oferă din nou mai târziu, prin
  mecanismul ei obișnuit, iar pauza pentru ochi se consideră sărită. În log: „Alertă întreruptă: eye-break (tragere de fișiere).”
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
  - Ora, Data, Vremea (pe temele luminoase iconița vremii ia culorile temei, din 0.6.18);
  - CPU, RAM;
  - Temperatura CPU, GPU și SSD (colorate după cât de cald e);
  - Baterie (cu iconiță de umplere), Volum, Internet.
- **Forma mică.** După câteva secunde fără activitate pe notch (implicit 10; se poate 5 s, 30 s, 1 min sau niciodată; un hover, o alertă sau închiderea notch-ului readuc standby-ul complet), sau peste o fereastră maximizată (opțional), se strânge într-o pastilă de 196×22 px (regulile, din 0.6.18: secțiunea „Plasa de siguranță a notch-ului”):
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

Cu „Manager de activități” pornit (Setări › Funcții noi, experimental), aceleași alerte trec printr-o coadă cu priorități:
o alertă mai puțin importantă nu o mai acoperă pe una importantă, multe alerte deodată devin „N noutăți” (pentru funcțiile
viitoare; azi doar în testele de fum, fiindcă alertele de acum care se pot grupa sunt doar trei; vezi secțiunea 1,
„Manager de activități”).

---

## 6. Pagina Acasă

Înălțime 310 px (la mărire 100%).

**Cardul de muzică**
- Piesa care se aude: titlu, artist, aplicație, copertă.
- **Fundal colorat după copertă:** se ia culoarea dominantă a copertei.
- **Versuri sincronizate** de la LRCLIB (gratuit): rândul curent și următorul, după poziția din piesă. Se pornesc și se opresc cu butonul „Versuri”. Pentru tab-uri din browser, versurile se caută doar pe site-urile de muzică.
- **Vizualizator real:** 44 de bare care se mișcă după sunetul care iese din boxe (FFT pe captura audio). Rulează doar cât Acasă e deschisă.
- Progres cu timpul curent și durata. Butoanele anterior, redare/pauză, următor. În browser fac ce fac tastele media ale tastaturii (handler-ele „nexttrack / previoustrack” ale paginii): video-ul sau piesa următoare pe YouTube, YouTube Music, Spotify, SoundCloud; dacă pagina nu are așa ceva, apasă butonul vizibil al player-ului, iar ca ultimă variantă sare 10 s. „Anterior” repornește piesa dacă a trecut de 3 s; apăsat din nou, merge la cea dinainte.
- **Volumul general,** cu buton de mute. Cu „Căști/boxe” pornit (P30, experimental), lângă el un buton cu căști deschide lista ieșirilor audio (vezi „Căști/boxe” în secțiunea 1).
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
| Comportament | Întârzierea la hover (0–1000 ms) · poziția (stânga/centru/dreapta) · peste jocuri/fullscreen · micșorare automată (niciodată/5 s/10 s/30 s/1 min) · **mărimea notch-ului deschis** (automat, 100–160%) · mic peste ferestre maximizate · **scurtătura Command Bar** (Win+Alt+Space sau Win+Alt+K; dacă e ocupată de altă aplicație, mesajul apare sub ea) · citirea temperaturilor · **„Activează temperatura procesorului”** (instalează serviciul de temperatură, o confirmare UAC; tot de aici se actualizează după un build nou) · pornire cu Windows · căutarea versiunilor noi, **canalul beta** și „Caută acum” |
| Acasă și sănătate | Versuri · pauză pentru ochi · **alertă de memorie** (pornit/oprit, prag 75/80/85/90%) · link-ul iCal pentru calendar (cu instrucțiuni pentru Google și Outlook) |
| Tab-uri din browser | Fiecare tab separat (pornit/oprit) · starea extensiei · butoane: deschide extensiile în Chrome sau Edge, arată folderul extensiei · pașii de instalare |
| Spații de lucru | Redenumire și ștergere |
| Culoare accent | Culoarea temei (implicit), Chihlimbar, Verde, Albastru, Roz, Mov, Alb |
| Vremea | Orașul, latitudinea și longitudinea |
| Pagina după context | Pentru fiecare categorie (Programare, Browser, Întâlnire, Joc, Media, Birou, Creație) pagina pe care se deschide notch-ul, sau „—” (nicio schimbare, implicit); merge cu „Pagina după context” pornită în „Funcții noi” (din P27) |
| Smart Clipboard | Peek la copiere (implicit oprit): un mesaj scurt în pastilă când copiezi un JSON, un link cu urmărire sau un JWT; merge cu „Smart Clipboard” și „Manager de activități” pornite (din P21) |
| Funcții noi (experimental) | Un comutator pentru fiecare funcție nouă din catalog, cu numele, o descriere de un rând și eticheta de stadiu (**Experimental**, **Beta**, **Stabil**); se aplică la „Salvează”, fără repornire. Dacă o funcție a fost oprită automat, apare și motivul |

**Fiecare opțiune e și o acțiune (din P14):** `settings.<nume>` (lista în secțiunea „Acțiuni”) deschide fereastra WinNotch la Setări, derulată la opțiune și cu focusul pe ea; din Command Bar le găsești cu „setări <opțiune>”.

**Funcții noi și comutatoare (feature flags, din 0.7)**
- Fiecare funcție nouă e declarată într-un singur loc, `Core/Flags/FeatureCatalog.cs` (ID, nume, descriere, stadiu, valoare implicită) și e oprită implicit până la versiunea în care e anunțată. Prima intrare e „Funcție de test” (`demo-flag`, Experimental, oprită), care nu face nimic vizibil. A doua e „Motorul de context” (`context-engine`, Beta, pornită implicit, fiindcă nu are interfață proprie). A treia e „Manager de activități” (`activity-manager`, Experimental, oprită; din 0.6.14). A patra e „Command Bar” (`command-bar`, Experimental, oprită; din P14). A cincea e „Pagina după context” (`context-pages`, Experimental, oprită; din P27). A șasea e „Quick Actions” (`quick-actions`, Experimental, oprită; din P20). A șaptea e „Smart Clipboard” (`smart-clipboard`, Experimental, oprită; din P21). A opta e „Raft” (`shelf`, Experimental, oprită; din P23). A noua e „Căști/boxe” (`audio-switch`, Experimental, oprită; din P30).
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

### Căști/boxe (din P30, experimental)

Alegi ieșirea audio implicită (căștile, boxele, un dispozitiv Bluetooth sau HDMI) din notch sau din Command Bar (ADR 0012).
Comutatorul „Căști/boxe” (`audio-switch`, **Experimental, oprit implicit**, oprit în `--safe-mode`). **Oprit, nimic nu se
schimbă:** niciun buton nou, nicio listă, nicio acțiune `audio.output-*`, nicio abonare la dispozitive.

- **Unde:** pe pagina Acasă, lângă volum (după numărul volumului), un buton mic cu căști. Click pe el → o listă mică,
  rotunjită, peste pagină, cu **ieșirile active** (doar cele conectate și pornite, ca în Windows), ordonate după nume; cea
  de acum are o bifă. Un nume repetat (două „Speakers”) apare „Speakers (2)”. Fără niciun dispozitiv: „Nicio ieșire audio”.
  Click pe o ieșire → acțiunea `audio.output-<id>` prin registru; rezultatul apare câteva secunde sub titlu, bifa se mută.
  Lista se închide cu „Închide”, din nou cu butonul, când schimbi pagina, intri în modul de editare sau închizi notch-ul.
  Volumul, alerta de volum și paginile urmează singure noua ieșire (ca la conectarea căștilor).
- **Command Bar:** „căști”, „boxe”, „ieșire audio” sau numele dispozitivului găsesc „Ieșire audio: <nume>”.
- **Ce se schimbă:** ieșirea implicită a Windows pentru **toate cele trei roluri** (sistem, multimedia, apeluri), ca
  „Setează ca implicit” plus „dispozitiv de comunicare implicit” din panoul Sunet: trecând pe căști, și apelurile (Teams,
  Zoom cu „implicit”) trec pe căști. **Fără rutare per aplicație** (o aplicație anume pe alt dispozitiv nu e oferită).
- **Interfață nedocumentată:** Windows nu are un API public pentru schimbarea ieșirii implicite; WinNotch folosește
  `IPolicyConfig` (cel folosit de panoul Sunet al Windows), izolat într-o singură clasă. **La prima eroare** (o versiune
  de Windows care nu mai are interfața sau o refuză), funcția **se oprește singură** (`FeatureFlags.Disable`, cu motivul
  fix „Windows a refuzat schimbarea ieșirii audio implicite (interfață nedocumentată).”, vizibil în Setări › Funcții noi), iar
  pastila spune „Ieșirea audio nu a putut fi schimbată — „Căști/boxe” s-a oprit; o poți porni din nou din Setări › Funcții
  noi.” (prin Activity Manager când e pornit; cu notch-ul deschis, întâi în listă, apoi în pastilă după închidere). Pornit
  din nou de tine, are o nouă șansă. Un dispozitiv scos între listă și click nu oprește funcția („Dispozitivul nu mai e conectat.”).
- **Fără polling:** lista se recitește la notificările Windows (dispozitiv adăugat, scos, pornit / oprit, altă ieșire
  implicită), după 400 ms de liniște (o rafală → o singură recitire; microfoanele care vin sau pleacă nu contează), și la deschiderea listei; toate apelurile COM sunt pe
  fire din fundal (MTA), niciodată pe firul interfeței.
- **Log:** doar contoare și texte fixe — „Ieșire audio: pornit.”, „Ieșire audio: oprit.”, „Ieșire audio: N ieșiri
  active.”, „Ieșire audio: lista deschisă (N).”, la o eroare tipul ei; registrul scrie id-ul acțiunii și rezultatul.
  **Niciun nume de dispozitiv** (pot fi personale, de exemplu „Căștile lui …”).

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
- **Clipboard:** conținutul marcat ca privat de managerele de parole e ignorat: nu intră în istoric și Smart Clipboard nu îl analizează (o singură verificare, `ClipboardPrivacy`, iar orice eroare la citire înseamnă „privat”). Smart Clipboard nu scrie nimic din conținut în log, decodează JWT-urile doar local (fără rețea, fără semnătură), deschide doar link-uri http/https și foldere locale (niciodată căi sau unități de rețea, niciodată fișierul însuși), prin `Shell.Open`, și nu analizează texte de peste 64 KB.
- **Raftul (P23):** ține doar căi locale; orice cale de rețea (și o unitate mapată de rețea) e refuzată înainte de orice acces la disc, cu aceeași regulă ca la scurtături și Smart Clipboard; nu cere iconița Shell a niciunui fișier și nu rezolvă scurtăturile (un `.lnk` / `.url` e citit ca octeți și acceptat doar dacă tot ce e în el e local; `.scf`, `.library-ms`, `.searchconnector-ms` niciodată; nici legăturile simbolice / junction-urile din cale, citite fără să fie urmate). Nu suprascrie nimic: zip-ul și imaginile convertite sunt fișiere noi (`FileMode.CreateNew`, „x (2).zip”…), iar unul început și nereușit e șters; nu mută, nu copiază și nu șterge fișierele tale (golirea scoate doar referințele; „Copiază selecția” pune pe clipboard doar căile, cu „Preferred DropEffect” = copiere, niciodată mutare — copierea o face Windows la `Ctrl+V`); un drop întoarce „link” sau „copy”, niciodată „move”, iar orice tragere pe care notch-ul nu o primește e refuzată explicit (nu rămâne efectul implicit al sursei). Imaginile au limite verificate din antet înainte de decodare (100 MB, 50 MP), zip-ul 10.000 de fișiere și 4 GB, fără urmarea legăturilor. Nimic din căi sau nume în log; drop-ul nu activează fereastra (focusul rămâne în aplicația ta).
- **Căști/boxe (P30):** singura interfață Windows nedocumentată din aplicație (`IPolicyConfig`) e izolată în `Features/AudioSwitch/PolicyConfigSwitcher.cs` (`[ComImport]`, obiectul COM creat, folosit și eliberat pe același fir MTA, `FinalReleaseComObject`); orice eroare a ei oprește funcția automat la prima apariție, cu un motiv fix (niciodată mesajul excepției). Nu cere drepturi de administrator, nu pornește procese, nu atinge sesiunile per aplicație; în log nu ajung numele dispozitivelor.
- **Calculatorul din lansator:** acceptă doar cifre și operatori, deci nu se poate injecta nimic.

---

## 15. Calitate, teste și audit

Detaliile sunt în `AUDIT.md`. Pe scurt:
- **Compilare** cu API-ul real WPF: 0 erori, 0 avertismente.
- **Două revizii independente,** una pentru bug-uri și performanță, una pentru securitate: 20 de probleme găsite, 18 reparate, 2 acceptate cu motivare.
- **Teste de fum pe exe-ul publicat (din 0.6.12),** la fiecare pull request și push pe alte ramuri decât `main` (`.github/workflows/ci.yml`, pe `windows-latest`, după build) și, din 0.6.13, înainte de semnarea fiecărei versiuni noi (`release.yml`, pe exe-ul care se publică): `tests/WinNotch.Smoke` (FlaUI, UI Automation) pornește `publish\WinNotch.exe --smoke` și verifică, ca un utilizator: fereastra notch-ului există și e vizibilă; procesul trăiește 30 s; alerta de volum și cea de piesă schimbă mărimea pastilei, care apoi revine; un comutator de funcție („context-engine”) se oprește și pornește; `Win+Alt+N` deschide și închide notch-ul; fereastra WinNotch se deschide din meniul iconiței; „Ieșire” închide curat (cod 0, iar `startup.json` marchează ieșirea curată); în `log.txt` nu e nicio excepție neprinsă, eroare de funcție (inclusiv prinsă de comutatoare), oprire automată de funcție, comandă de test eșuată sau stivă de apeluri. La eșec, CI-ul e roșu, iar captura de ecran și `log.txt` rămân ca artefacte („smoke-artifacts”). Din 0.6.14 (P13) tot setul rulează **de două ori**, cu „activity-manager” oprit (`--activity-manager=off`, calea veche a alertelor) și pornit (`--activity-manager=on`: comutatorul e pornit înainte de verificări, iar în plus se verifică pastila împărțită cu două activități persistente și închiderea lor prin `activity.dismiss-all`, „5 noutăți” la 5 alerte rapide și peek-ul unei activități Low); cu el oprit, aceleași comenzi de test trebuie refuzate fără erori în log. Din P14, în ambele rulări, **Command Bar**: cu „command-bar” oprit, `Win+Alt+Space` nu deschide nimic (iar comanda de test `open-command-bar` e refuzată); pornit, testul își deschide o fereastră proprie și o aduce în față, apoi scurtătura reală `Win+Alt+Space` deschide bara (dacă scurtătura e ocupată pe mașina de CI sau nu ajunge în 5 s, bara e deschisă cu `open-command-bar`, care trece prin aceeași cod ca scurtătura, iar rezultatul scrie un avertisment care spune asta), bara are tastatura, „volum” găsește „Setează volumul”, o alertă de volum (și, cu managerul pornit, o activitate persistentă) nu ia pastila cât e deschisă, Esc o închide și tastatura revine în fereastra testului (verificat cu `GetForegroundWindow`), activitatea persistentă apare după închidere, iar Enter pe „setări poziție” (`settings.position`, sigură) deschide fereastra WinNotch; la final comutatorul e oprit și scurtătura eliberată. Regula „nimic peste ecran complet” e verificată de testele unitare, nu de cele de fum. Din P27, **o singură dată** (doar în rularea cu „activity-manager” oprit; cealaltă scrie `SKIP`), **pagina după context**: comanda de test `fake-context dev` pune categoria „Dev” în snapshot-ul motorului de context (prin debounce-ul lui obișnuit; starea notch-ului o arată ca `;ctx=Dev`), `set-context-page dev devices` salvează maparea ca Setările; cu „context-pages” oprit, `Win+Alt+N` deschide notch-ul pe pagina de dinainte, pornit, pe „devices” (`;page=devices`, plus „Pagina după context: Dev.” în log); la final comutatorul e oprit, maparea și contextul fals scoase. Fereastra de 10 minute și paginile ascunse sunt verificate de testele unitare. Din P20, în ambele rulări, **Quick Actions**: comanda de test `fake-meeting headphones` pune în motorul de context o întâlnire („Test”) cu căști (prin debounce-ul lui; motorul o scrie în log); cu „quick-actions” oprit, `Win+Alt+N` deschide notch-ul fără butoane și fără sugestii; pornit, rândul are „Mută / pornește microfonul” și „Volum 40%” (găsite prin UI Automation după `qa-<id acțiune>`, 2–4 butoane), iar un click pe microfon trece prin registru („Acțiune audio.mute-mic (QuickAction): reușită”, contorul `;qa=` din starea notch-ului, rezultatul în rând, `qa-message`) — de două ori, ca microfonul să rămână cum era; cu „activity-manager” pornit apare exact o sugestie (`;qs=1`), a doua în mai puțin de 10 minute e amânată, iar „Nu mai arăta” se salvează și dispare din rând; cu el oprit, nicio sugestie și niciun „Nu mai arăta”. La final comutatorul e oprit și întâlnirea falsă scoasă. Celelalte reguli și limita de la 10:00 sunt verificate de testele unitare. Din P21, **o singură dată** (doar în rularea cu „activity-manager” oprit; cealaltă scrie `SKIP`), **Smart Clipboard**: comanda de test `smoke-clipboard-page on` adaugă (doar în setările din folderul `smoke`) o pagină cu widget-ul Clipboard și o arată, fiindcă paginile standard nu au acest widget; testul copiază el însuși în clipboard un JSON compact (cu diacritice și numărul „2.50”); cu „smart-clipboard” oprit, `Win+Alt+N` deschide pagina fără niciun chip; pornit (și JSON-ul copiat din nou), apare chip-ul „Formatează” (`sc-clipboard.format-json`, UI Automation), un click trece prin registru („Acțiune clipboard.format-json (UI): reușită”, rezultatul lângă chip-uri, `sc-message`), iar clipboard-ul conține apoi același JSON, indentat cu 2 spații (verificat prin comparare, cu diacriticele și „2.50” neschimbate); chip-urile îl urmează („Compactează”, fără „Formatează”), clipboard-ul nu se mai schimbă singur (fără buclă), iar marcajul din JSON nu apare în `log.txt`. La final comutatorul e oprit și pagina scoasă. Celelalte tipuri, peek-ul și formatele private ale managerelor de parole sunt verificate de testele unitare. Din P23, **o singură dată** (doar în rularea cu „activity-manager” oprit; cealaltă scrie `SKIP`), **raftul**: testul își creează un fișier local într-un folder temporar (cu un marcaj în nume), pornește „shelf”, iar comanda de test `smoke-shelf-add <cale>` (doar cu `--smoke`; UI Automation nu poate face o tragere OLE) îl trece prin aceleași verificări ca un drop („Raft: adăugate 1, refuzate 0…” în log); `Win+Alt+N` deschide notch-ul, butonul „Raft” (`shelf-toggle`) arată raftul, elementul apare (`shelf-item-<id>`, cu numele fișierului), „Copiază calea” (`shelf-copy-<id>`) trece prin registru („Acțiune shelf.copy-path (UI): reușită”, rezultatul în `shelf-message`) și clipboard-ul conține exact calea; „Golește” (`shelf-clear`, „Acțiune shelf.clear (UI): reușită”) lasă raftul gol, iar fișierul rămâne pe disc. La final comutatorul e oprit, fișierul și folderul de test șterse, iar marcajul și folderul nu apar în `log.txt`. Căile de rețea, scurtăturile, al 21-lea element, zip-ul, OCR-ul și conversia sunt verificate de testele unitare. Din P30, **o singură dată** (doar în rularea cu „activity-manager” oprit; cealaltă scrie `SKIP`), **Căști/boxe**: pornește „audio-switch”, deschide notch-ul, comanda de test `smoke-audio-outputs` (doar cu `--smoke`) arată pagina Acasă și deschide lista ca butonul (o linie fixă în log spune ce a făcut: deschisă, pagina, butonul, numărul de ieșiri; lista e găsită după titlul ei, `audio-outputs-title`); pe mașina de CI, fără audio, lista trebuie să spună „Nicio ieșire audio” (`audio-outputs-empty`; cu dispozitive, rândurile `audio-output-<id>`, cel mult unul marcat implicit); apoi butonul real de lângă volum (`audio-outputs-toggle`) închide și redeschide lista; procesul trăiește, comutatorul nu s-a oprit singur. Nu alege nicio ieșire (ar schimba ieșirea mașinii); la final comutatorul e oprit. Schimbarea, oprirea automată, id-urile și debounce-ul sunt verificate de testele unitare. Rânduri PASS: cu „activity-manager” oprit, 20; cu el pornit, 17 plus patru rânduri SKIP (pagina după context, Smart Clipboard, raftul și Căști/boxe) (19 / 17 înainte de P30, 18 / 17 înainte de P23, 17 / 17 înainte de P21, 16 înainte de P20, 15 / 16 înainte de P27); fiecare rulare își păstrează `log.txt` în `smoke-artifacts\activity-manager-off` / `-on`. Meniul iconiței e încercat de cel mult două ori (o singură reîncercare, cu motivul scris în rezultat). Local: `dotnet run --project tests/WinNotch.Smoke/WinNotch.Smoke.csproj -- publish\WinNotch.exe smoke-artifacts --activity-manager=on` (cu WinNotch închis; fără argument, rularea cu comutatorul oprit).
- **Modul de test `--smoke`** (doar pentru testele de fum): folder de date separat (`%AppData%\WinNotch\smoke`, deci setările, log-ul și `startup.json` adevărate nu sunt atinse), fără căutarea actualizărilor, fără serviciul de temperatură (setările se salvează doar în folderul `smoke`) și fără fereastra „rulează deja” (dacă WinNotch rulează deja, se închide cu codul 3); o repornire în mod sigur păstrează `--smoke`. Doar în acest mod citește comenzi de test din `smoke-commands.txt` din acel folder (cel mult 4 KB, 20 de linii): `post-alert volume`, `post-alert track`, `toggle feature <id>` și, din 0.6.14, `post-activity persistent <1–3>`, `post-activity burst <1–10>`, `post-activity low`, `dismiss-activities` (acestea doar cu „activity-manager” pornit; altfel sunt refuzate); restul liniilor sunt ignorate. Starea notch-ului (modul și mărimea pastilei, plus `;split=1`, `;group=N`, `;peek=1` când managerul de activități le arată) e publicată pentru UI Automation.
- **600 de teste automate, toate trec,** incluse în proiect și rulabile cu `tests\run-tests.bat`:
  - 672 pentru aplicație (din care 27 pentru **B1, notch-ul gol** (0.6.18): cursa animațiilor care colapsa un strat arătat (simulată, cu regula veche care eșuează), culorile vremii pe tema luminoasă, ce înseamnă „gol” pentru panou, Command Bar, standby, forma mică (data goală sau tăiată) și alertă, pașii plasei de siguranță (pagina, apoi Acasă, apoi renunță), limitele de reparații pe minut, „doar transparent” verificat încă o dată, rândul „B1 recover” fără titluri sau nume, regulile formei mici (inactivitatea, fereastra maximizată, revenirea la activitate, data în fiecare zi a anului), comutatorul, starea de fum și, fixate în sursă, legăturile și cronometrele one-shot (testul de fum B1 a fost scos la cererea autorului); 25 pentru **Căști/boxe** (P30, inclusiv 3 din revizia R1 și 1 după CI: lista vizibilă în UI Automation): zero, unul și mai multe dispozitive, implicita marcată, nume duplicate, id-uri stabile și valide pentru registru, acțiunile prin registru cu un `IAudioEndpointSwitcher` fals (reușit, deja implicită, dispozitiv scos între listă și click sau chiar în timpul schimbării), oprirea din UI fără așteptarea COM-ului, evenimentele microfoanelor ignorate, oprirea automată o singură dată la prima eroare cu motivul fix și mesajul, comutatorul oprit fără acțiuni, refresh-ul și `Changed`, debounce-ul notificărilor, log-ul fără nume și, fixate în sursă, `IPolicyConfig` izolat (CLSID, IID, cele trei roluri, eliberarea COM, firul MTA), legăturile din notch și testul de fum; 43 pentru **Raft** (P23, inclusiv 5 din revizia R1: legăturile simbolice, stick-ul scos, imaginile ilizibile, zip-ul fără fișiere citite, tragerile netratate, și 4 pentru bife și copierea în bloc): comutatorul, fiecare formă de cale de rețea refuzată fără acces la disc (`\\server`, `//server`, `\/`, `\\?\UNC\`, `\\?\C:\`, `\\.\`, IP, WebDAV, `file://server`, în ghilimele, cu spații), căile locale valide și cele ciudate (relative, `..`, nume de dispozitiv, fluxuri, metacaractere), unitatea mapată de rețea, scurtăturile `.lnk` scrise octet cu octet (țintă locală / pe stick, LinkInfo de rețea, volum „remote”, fără LinkInfo, iconiță / dosar de lucru / țintă `%VAR%` de rețea, fișier stricat) și `.url` (URL și iconiță de rețea), modelul (20, al 21-lea refuzat, dubluri, id-uri, setările vechi), numele noi și fișierele noi cu `CreateNew` pe disc (cele vechi neatinse, cel început șters la eroare și la anulare), zip-ul real (fișier, folder cu subfoldere, legături sărite, limite, anulare), formatul imaginilor și punerea pe alb, cele 8 acțiuni prin registru cu o gazdă falsă (comutator oprit, mod sigur, API local, parametri refuzați, raft gol, element dispărut scos), bifele (pune / scoate, nimic bifat = tot raftul, elementele plecate uitate), „elemente” (id-uri, poziții, „tot”, chei inexistente) și copierea în bloc (ordinea raftului, clipboard ocupat, element șters de pe disc sărit și scos, unitate deconectată), log-ul fără căi, regula de hover cât tragi, comanda de fum și, fixate în sursă, legăturile din notch, protocolul comutatorului, drop fără „move”, fără polling, fără iconița Shell, fără suprascriere, fără culori scrise în cod; 45 pentru **Smart Clipboard** (P21, inclusiv 1 din revizia R1): comutatorul, fiecare tip recunoscut și fiecare tip fals (numere care nu sunt telefoane, date, sume, IP-uri, „{nu e json}”, „[1,2,”, „{} extra”, „#hashtag”, „#12345”, e-mailuri și link-uri greșite, căi nevalide și de rețea, JWT-uri cu base64 invalid sau fără JSON), ordinea de prioritate, limita de 64 KB și intrări patologice (sub 0,5 s), curățarea link-urilor octet cu octet (fără urmărire identic, amestecat, doar urmărire fără „?”, fragmentul, codificarea, majusculele), JSON formatat / compact echivalent, JWT decodat fără semnătură, chip-urile și titlurile peek-ului, cele 10 acțiuni prin registru cu o gazdă falsă (comutator oprit, mod sigur, API local, parametri refuzați, clipboard ocupat), log-ul registrului fără conținut, formatele private ale managerelor de parole, setarea, comenzile testului de fum și, fixate în sursă, legăturile din notch și din widget, fără buclă, fără polling, fără culori scrise în cod; 42 pentru **Quick Actions** (P20, inclusiv 3 din revizia R1): comutatorul, tabelul regulilor (id-uri, 2–4 acțiuni care există în registru), fiecare regulă pe contextul ei și pe niciun altul, limitele condițiilor, prioritatea, acțiunile lipsă / indisponibile / cu confirmare / periculoase / fără drept de QuickAction / cu parametri greșiți scoase, Empty și comutatorul oprit, sugestiile doar cu Activity Manager, doar la o regulă nouă, una la 10 minute (9:59 / 10:00, ceas injectat), „Nu mai arăta” și setările, pornirea prin registru ca QuickAction, acțiunile noi, injecția și comenzile testului de fum și, fixate în sursă, legăturile din notch, abonările cu dezabonare, fără polling și log fără context; 35 pentru **Pagina după context** (P27): comutatorul, categoriile, fiecare categorie → pagina ei, „—”, paginile ascunse sau șterse, alegerea manuală la 9:59 și la 10:00, ceasul injectat, comutatorul oprit și snapshot-ul gol, motorul de context real ca singură sursă, injecția testului de fum prin debounce-ul motorului, setările vechi / salvare / citire, comenzile de fum și, fixate în sursă, legăturile din notch și din Setări, fără surse proprii și log fără titluri; 39 pentru **Command Bar** (P14): comutatorul, scurtătura și o singură alertă de conflict, regula pentru ecran complet, căutarea cu parametri („volum 30”), fără acțiuni periculoase, Enter și confirmarea dublă anulată de editare sau de săgeți, mărimile, acțiunile `settings.*` (câte una pentru fiecare opțiune din `SettingsWindow.xaml`, fără id-uri dublate), comanda de test și, fixate în sursă, legăturile din notch, calea focusului, pornirea doar prin registru și log-ul fără text; 132 de **caracterizare a celor 27 de alerte existente** — sursa fixată: mărime, durată, „important”, corpurile `ShowLive` / `EndLive` și fiecare limită de repetare; apoi, pentru fiecare alertă și limită, același rezultat vizibil pe calea veche și prin Activity Manager —, 3 pentru rutarea lor, 23 pentru Activity Manager (inclusiv 2 din revizia R1): înlocuire și coadă, Critical, notch deschis și ecran complet, „N noutăți”, persistente și pastila împărțită, peek, limite, 1000 de alerte în 10 s sub o secundă, 8 fire în paralel cu cronometre reale, comutatorul, acțiunea `activity.dismiss-all`, alertă → activitate; 39 pentru motorul de context și 9 pentru modul `--smoke`: comenzile și rândurile de log care pică testul de fum; dintre celelalte, 48 pentru acțiuni: id-uri, căutare fără diacritice și ordinea rezultatelor, cine are voie să pornească o acțiune, confirmarea obligatorie, cereri deja anulate, disponibilitate, comutatoare, parametri, erori, timp maxim și anulare, firul interfeței, liste dinamice, jurnal fără valori, toate acțiunile incluse; 54 pentru actualizări: ordinea versiunilor, inclusiv cu sufixe de test, canalul beta, versiunile refuzate, lista de la GitHub, pornirea monitorizată pe toate ramurile — mod sigur, revenire doar după modul sigur automat și recent, blocarea buclelor între versiuni, sănătos după 10 minute chiar și cu somn sau ceas schimbat, oprirea Windows anulată, `startup.json` lipsă, corupt sau ciudat, ordinea pașilor (mutex, notă, schimbarea fișierelor) și eșecul la jumătatea schimbării; și 29 pentru comutatoarele funcțiilor noi: setări vechi, salvare și recitire, `Changed` o singură dată, abonați care dau erori, oprire automată care nu e anulată de „Salvează”, mod sigur, rezumatul de sănătate): autentificarea reciprocă a extensiei, refuzul vechiului token în clar, închiderea conexiunilor neautentificate, copertele (doar PNG/JPEG/WebP, ≤ 300 KB), limitarea duratelor, calendarul ostil (20.000 de evenimente procesate sub 3 s), plus: serverul extensiei și autentificarea, nume de site-uri, protecția adreselor, verdictul de viteză, calculatorul, calendarul, **grila de widget-uri** (locuri libere, limite, mutare cu rearanjare, pagină plină, 2000 de mutări aleatoare fără suprapuneri);
  - 23 pentru extensie, rulate cu un Chrome simulat, inclusiv butoanele următoarea/anterioara și refuzul unui server fals.
- **Revizii independente pentru 0.6** (pagini, editor, drag & drop, teme): 13 probleme găsite, toate reparate.
- **Al doilea audit** (`AUDIT-2.md`): 38 de probleme găsite pe 5 dimensiuni, toate reparate.
- **62 de verificări pe Windows,** de făcut manual, fiecare cu rezultatul așteptat.

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

**Unde se salvează:** în `settings.json`: `Pages`, `HiddenPages`, `ContextPages` (pagina după context, din P27), `QuickActionsHidden` (Quick Actions, regulile cu „Nu mai arăta”, din P20), `SmartClipboardPeek` (Smart Clipboard, peek la copiere, din P21), `Shelf` (raftul: căile, din P23), `ThemeMode`, `ThemeDark`, `ThemeLight`, `ThemeOverrides`, `CustomThemes`, `CornerRadius`, `BgOpacity`.

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
- **Manager de activități (experimental):** o activitate persistentă deja afișată rămâne vizibilă dacă pornește apoi un joc pe tot ecranul (ca o alertă importantă; Game Mode o va ascunde); una Normal venită sau ascunsă în timpul ecranului complet reapare când acesta se termină. Alertele din „N noutăți” nu pot fi deschise una câte una.
- **Command Bar (experimental):** alertele obișnuite care vin cât e deschisă bara nu mai apar după (ca la notch-ul deschis); doar Critical și activitățile persistente așteaptă, și doar cu Managerul de activități pornit. Un click pe desktop sau pe bara de activități închide bara și încearcă să dea tastatura înapoi ferestrei de dinainte (Windows poate refuza; nu se întâmplă nimic rău). Dacă Windows refuză să-i dea tastatura la deschidere, bara o cere ca fereastra WinNotch (`ForceForeground`). Fullscreen-ul e verificat pentru fereastra din față în clipa apăsării: un joc pe alt monitor decât cel cu fereastra activă nu blochează deschiderea.
- **Quick Actions (experimental):** rândul se alege doar la deschiderea notch-ului (nu se schimbă cât e deschis); se arată doar prima regulă potrivită; „Scoate stick-ul” nu e Quick Action (cere confirmare); „Economisire” deschide pagina Windows, nu pornește singur economisirea; ora ultimei sugestii nu se păstrează după repornire; fără „Motorul de context” (sau în `--safe-mode`) nu face nimic.
- **Căști/boxe (experimental):** folosește o interfață Windows nedocumentată, care se poate schimba la o actualizare de Windows (funcția se oprește atunci singură); schimbă ieșirea pentru toate aplicațiile (nu una anume); butonul e doar pe pagina Acasă (și în Command Bar), nu pe pastila închisă sau pe alerta de volum, care lasă click-urile să treacă.
- **Raft (experimental):** pastila închisă nu primește fișiere (lasă click-urile să treacă): tragi peste ea și aștepți ca la hover, ori deschizi notch-ul înainte (dacă dai drumul înainte să se deschidă, fișierul ajunge la fereastra de dedesubt, ca fără raft); deasupra unei ferestre maximizate se deschide doar la marginea de sus a ecranului, ca la hover. O tragere de fereastră sau o selecție de text adusă peste pastilă o deschide și ea (se închide singură când pleci). Din aplicații pornite ca administrator Windows nu lasă tragerea spre WinNotch. Un `.lnk` fără informații locale (de exemplu spre „Acest PC” sau spre o aplicație din Store) e refuzat. OCR-ul și conversia citesc doar primul cadru și nu aplică rotirea EXIF a fotografiilor; o imagine e micșorată la 3000 px pentru OCR.
- **Smart Clipboard (experimental):** chip-urile sunt doar pentru ultimul text copiat (nu pentru cele mai vechi din istoric) și doar în widget-ul Clipboard, nu și în lista paginii Unelte; după o repornire nu e niciun text până la prima copiere; un text de peste 64 KB nu e analizat; „Deschide folderul” nu selectează fișierul în Explorer, doar deschide folderul lui.
- **Pagina după context (experimental):** decide doar la deschiderea notch-ului (nu schimbă pagina cât e deschis); alegerea manuală de 10 minute nu se păstrează după repornire; fără „Motorul de context” (sau în `--safe-mode`) nu face nimic. Paginile ascunse nu sunt alese, chiar dacă sunt în mapare.
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
| 0.6.14 | Manager de activități (P13, experimental, oprit implicit, comutatorul „activity-manager”): alertele notch-ului trec prin `Core/Activity` (priorități, coadă, Critical întrerupe, aceeași cheie actualizează, „N noutăți”, ecran complet doar Critical/High, pastilă împărțită pentru 2 activități persistente, peek pentru Low); acțiunea `activity.dismiss-all`; teste de caracterizare pentru toate alertele; testele de fum rulează cu comutatorul oprit și pornit; aspectul alertelor neschimbat |
| 0.6.15 | Command Bar (P14, experimental, oprit implicit, comutatorul „command-bar”): Win+Alt+Space (sau Win+Alt+K) transformă pastila în căutare peste acțiuni, cu parametri în text, confirmare la a doua apăsare pe Enter, fără acțiuni Dangerous, focusul înapoi la fereastra anterioară, nimic peste ecran complet; 21 de acțiuni `settings.*`. Pagina după context (P27, experimentală, oprită implicit, comutatorul „context-pages”): categoria din motorul de context → pagina la deschidere, alegerea manuală respectată 10 minute. Teste de fum pentru ambele |
| 0.6.16 | Quick Actions (P20, experimental, oprit implicit, comutatorul „quick-actions”): 1–4 butoane sub pastilă la deschidere, după context, din tabelul de reguli peste ActionRegistry (fără acțiuni Confirm/Dangerous), sugestie nesolicitată doar cu Activity Manager (peek Low, cel mult una la 10 minute, „Nu mai arăta” per regulă). Smart Clipboard (P21, experimental, oprit implicit, comutatorul „smart-clipboard”): JSON, JWT, link, e-mail, culoare, IP, cale, telefon → chip-uri în widget-ul Clipboard și 10 acțiuni `clipboard.*`; peek la copiere opțional, oprit implicit; parolele ignorate, nimic din conținut în log. Teste de fum pentru ambele |
| 0.6.17 | Raft (P23, experimental, oprit implicit, comutatorul „shelf”): planul B — fișierele se lasă pe notch-ul deschis (deschis și prin hover cât tragi), max. 20 de căi păstrate în setări, rețeaua/unitățile mapate/symlink-urile/scurtăturile spre rețea refuzate, 7 acțiuni `shelf.*` (calea, folderul, zip, OCR, PNG↔JPG, scoate, golește) fără suprascriere, tragerea în afară ca fișier. Căști/boxe (P30, experimental, oprit implicit, comutatorul „audio-switch”): lista ieșirilor active lângă volum, acțiuni `audio.output-<id>`, IPolicyConfig izolat, oprire automată la prima eroare, fără rutare per aplicație. Curățenie: testele de fum împărțite pe zone. Teste de fum pentru ambele |
| 0.6.18 | Reparația B1: notch-ul deschis gol și pastila doar cu ora — o animație veche de ascundere nu mai colapsează un strat arătat între timp (jetoane per strat), iconița vremii ia culorile temei pe temele luminoase, forma mică are mereu „ora · data”; plasa de siguranță „notch-guard” (Stabil, pornită): verificare la 300 ms după deschidere și la 450 ms după schimbarea pastilei, pagina curentă refăcută, apoi Acasă, rândul „B1 recover” în log (fără date personale) |
| 0.6.19 | Raft (P23, experimental): bifă pe fiecare rând și „Copiază selecția” / „Copiază tot” (`shelf.copy-files`) — fișierele ajung pe clipboard ca fișiere (ca `Ctrl+C` din Explorer), iar copierea o face Windows la `Ctrl+V`, cu dialogul lui de conflicte; rândurile bifate se trag împreună din raft. WinNotch tot nu scrie, nu mută și nu șterge niciun fișier al tău |
| 0.6.13 | Testele de fum rulează și înainte de semnarea fiecărei versiuni (`release.yml`); prind și erorile de comutator prinse în log, oprirea automată a unei funcții și o ieșire nemarcată curată în `startup.json` (revizia R1 a P02); nimic schimbat vizibil |
| 0.6.12 | Teste de fum automate pe exe-ul publicat (FlaUI, în CI, la fiecare pull request): pornire, notch vizibil, alerte, comutator de funcție, `Win+Alt+N`, fereastra WinNotch din meniul iconiței, ieșire curată, log fără excepții; modul de test `--smoke`; nimic schimbat vizibil |
| 0.6.11 | Pregătire internă pentru funcțiile care se adaptează la ce faci: motorul de context (aplicația din față și categoria ei, ecran complet, media, microfon și cameră, ieșire audio, întâlniri, rețea, alimentare, monitoare, stick USB, inactivitate), cu evenimente, debounce și comutatorul „Motorul de context”; acțiunea `context.show`; nimic schimbat vizibil |
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
