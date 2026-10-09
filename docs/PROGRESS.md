# WinNotch — jurnalul rulărilor (pilot automat)

## Rularea 1 — Pasul 0 + P02 (5 oct 2026)

- **P02 Teste de fum:** făcut deja în `main` (9e39b8c, release 0.6.12 verificat: exe + .sig, semnătura ECDSA validă); `tests/WinNotch.Smoke` (FlaUI) pornește exe-ul publicat cu `--smoke` și face 9 verificări pe `windows-latest`.
- **Revizia R1** (`git diff 9e39b8c^1...9e39b8c`): aprobat cu reparații: 1 Major (release.yml semna fără teste de fum), 1 Mediu (erorile de comutator și oprirea automată nu picau testul de log), 6 Minore.
- **Reparat pe `p02-fix` (0.6.13):** testele de fum rulează în `release.yml` înainte de „Sign”; `SmokeMode.IsFatalLogLine` + testul SM6; `ElementNotAvailableException` prins; „Ieșire” verifică `startup.json`; avertisment când iconița nu e găsită; repornirea păstrează `--smoke`; ADR 0005 corectat.
- **Rămas așa (Minor, documentat):** polling la 500 ms doar în modul `--smoke` (asumat în ADR 0005); „pastila vizibilă” verifică doar mărimea; alerta de piesă apelată direct, fără detecția piesei; meniul iconiței cade încă pe mesajul de click dreapta la al doilea deschis în CI (acum avertisment vizibil).
- **Teste:** 255 C# + 23 extensie + 9 de fum, toate trec; CI run 20 pe `p02-fix` verde, cu testele de fum rulate efectiv pe Windows.

## Rularea 2 — P13 Activity Manager (5 oct 2026)

- **P13 Activity Manager:** `Core/Activity` (priorități, coadă de max. 50, Critical întrerupe, cheie comună = actualizare, „N noutăți”, ecran complet doar Critical/High, pastilă împărțită, peek pentru Low), `Features/Activity`, acțiunea `activity.dismiss-all`; comutatorul „activity-manager” Experimental, oprit; toate cele 27 de alerte trec prin manager cu el pornit, aspect neschimbat; release 0.6.14 verificat (exe + .sig, ECDSA validă).
- **Caracterizare întâi** (a9fe17a, separat, verificat pe codul vechi: 350 PASS): 132 de verificări (27 de alerte, 23 de limite de repetare, corpurile `ShowLive`/`EndLive` fixate), rulate pe ambele drumuri.
- **Revizia R1:** aprobat cu reparații: 1 Mediu (rafala „N noutăți” nenumărată din nou după rezumat → „6 noutăți” fals și peek dependent de timp), 7 Minore; toate reparate în abf8c04 (teste AM15, AM16).
- **Teste:** 416 C# (161 noi) + 23 extensie + fum 12 (oprit) / 13 (pornit); CI run 23 verde pe Windows, fumul rulat de două ori; release.yml run 13 verde (fum de două ori înainte de „Sign”).
- **Rămas așa:** „N noutăți” nu apare cu alertele de azi (doar 3 chei grupabile), e pentru P20+/P46; meniul iconiței cade încă pe mesajul de click dreapta în CI (avertisment, o singură reîncercare); nimic verificat vizual de un om pe Windows.

## Rularea 3 — P14 Command Bar + P27 Pagina după context (5 oct 2026)

### P14 Command Bar
- **Făcut:** `Features/CommandBar` (căutare peste `ActionRegistry.Search`, parametri în text „volum 30”, săgeți/Enter/Esc, Confirm = a doua apăsare pe Enter, Dangerous ascunse), Win+Alt+Space cu alternativa Win+Alt+K în Setări (alertă o dată la conflict), focusul pe calea lansatorului (`EnableTyping` → `StopTyping`/`LastForeground`), nimic peste ecran complet; 21 de acțiuni `settings.*`; comutatorul „command-bar” Experimental, oprit; ADR 0007.
- **Revizia R1:** aprobat cu reparații, 0 Critic/Major/Mediu, 7 Minore; reparate în e4c4990: panoul ascuns nu se mai reîmprospătează sub bară, raza urmează scara, textul tăiat cu „…”, textul din Setări într-un singur loc, nota din ADR; rămase (documentate): focus refuzat de Windows de două ori → bara fără tastatură până la scurtătură/mouse; click pe desktop trage tastatura înapoi (ca lansatorul).
- **CI:** run 25 roșu (meniul iconiței: `NoClickablePointException` după testul `settings.position`, nu Command Bar); reparat în testul de fum (cade pe mesajul de click dreapta, ca la iconița negăsită); run 26 verde.
- **Teste:** 455 C# (39 noi, CB1–CB39) + 23 extensie + fum 15 (oprit) / 16 (pornit); scurtătura reală Win+Alt+Space a mers pe ambele drumuri (fără comanda de rezervă); regula ecranului complet doar în teste unitare.
- **Abatere:** ramura `p14-command-bar` (nume ales de pilot); unită local în `main` cu `--no-ff`, fără versiune nouă.

### P27 Pagina după context
- **Făcut:** `Features/ContextPages` (regula pură cu ceas injectabil: categoria efectivă — întâlnire, apoi joc pe tot ecranul, apoi aplicația din față — → pagină; „—” = fără schimbare), citită doar la deschidere din `ContextEngine.Current.Snapshot` (fără surse noi, fără abonări, fără polling); Setări: 7 categorii (cu Creator) → pagină, implicit „—”; alegerea manuală (click pe tab, pagină nouă) respectată 10 minute; pagina ascunsă/ștearsă ignorată; comutatorul „context-pages” Experimental, oprit; acțiunea `settings.context-pages`; ADR 0008.
- **Revizia R1:** aprobat cu reparații, 0 Critic/Major/Mediu, 5 Minore; reparate în 4a2a488: comenzile de test verifică singure `SmokeMode.On`, starea de fum arată categoria efectivă, pagina nouă = alegere manuală, numărul de verificări din documentație; rămas: două pagini cu același nume nu se deosebesc în lista din Setări.
- **CI:** run 27 și run 28 verzi din prima; fumul P27 o singură dată (rularea cu activity-manager oprit; cealaltă scrie SKIP).
- **Teste:** 490 C# (35 noi, CP1–CP28) + 23 extensie + fum 16 / 16 (+1 SKIP); fereastra de 10 minute și paginile ascunse doar în teste unitare.
- **Abatere:** categoria „Creator” în plus față de PLAN (există în motor); alegerea manuală ținută în memorie (se pierde la repornire).


## Rularea 4 — P20 Quick Actions + P21 Smart Clipboard (6 oct 2026)

### P20 Quick Actions
- **Făcut:** `Features/QuickActions` (tabel de date cu 4 reguli: întâlnire + căști → mută/pornește microfonul, volum 40%; media → pauză, următor; stick USB → deschide; baterie sub 20% → economisire, setări ecran), evaluare pură peste ActionRegistry (indisponibile și Confirm/Dangerous excluse), rândul de 1–4 butoane sub pastilă la deschidere pe ambele drumuri, sugestie nesolicitată doar cu activity-manager pornit (peek Low, max. una la 10 minute, „Nu mai arăta” per regulă în setări); acțiuni noi `device.open-<literă>`, `settings.battery-saver`, `quick-actions.show-hidden`; comutatorul „quick-actions” Experimental, oprit; ADR 0009.
- **Revizia R1:** aprobat cu reparații, 0 Critic/Major, 2 Medii (rezultatul click-ului invizibil; citirea stick-urilor pe firul UI la fiecare deschidere), 5 Minore; reparate în 1d81729 (mesaj 4 s în rând, stick din context + reîmprospătare în fundal, limita doar pe peek afișat, eticheta clară, rândul revine după editare, „1–4” în documentație); rămas: testele QA27–QA36 caută șiruri în sursă (precedent).
- **CI:** run 29 și run 30 verzi din prima; fumul Quick Actions pe ambele drumuri (oprit: fără sugestii; pornit: o sugestie, a doua amânată, „Nu mai arăta”).
- **Teste:** 532 C# (42 noi, QA1–QA39) + 23 extensie + fum 17 / 17 (+1 SKIP).
- **Abateri:** stick-ul are un singur buton (Scoate cere confirmare, deci exclus); economisirea bateriei deschide pagina din Windows (fără API documentat); ramura de lucru e `claude/mod-pilot-automat-rularea-4-lneluy` (cea a sesiunii), unită local în `main` cu `--no-ff`.

### P21 Smart Clipboard
- **Făcut:** `Features/SmartClipboard` (recunoaștere pură fără regex, max. 64 KB: JSON → JWT → URL → e-mail → culoare → IP → cale → telefon), chip-uri pentru ultimul text copiat în widget-ul Clipboard, 10 acțiuni `clipboard.*` Safe (formatează/compactează JSON, decodează JWT local fără semnătură, curăță link-ul doar de parametri de urmărire cunoscuți, restul octet cu octet), peek la copiere cu setare separată, oprită implicit, doar prin ActivityManager; verificarea parolelor mutată neschimbată în `ClipboardPrivacy.IsPrivate` (pură, testată); în log doar contoare; comutatorul „smart-clipboard” Experimental, oprit; ADR 0010.
- **Revizia R1:** aprobat, 0 Critic/Major/Mediu, 6 Minore, toate reparate în 180d67b (liste JSON mici și „::” nu mai sunt recunoscute, link fără browser → mesaj fix, folderul deschis cu „\” la final, o singură memorie a recunoașterii, pagina de fum scoasă curat).
- **CI:** run 32 și run 33 verzi din prima; fumul Smart Clipboard o singură dată (rularea cu activity-manager oprit; cealaltă scrie SKIP): JSON copiat → chip „Formatează” → JSON indentat în clipboard, marcajul de conținut absent din log.
- **Teste:** 577 C# (45 noi, SC1–SC45) + 23 extensie + fum 18 / 17 (+2 SKIP).
- **Abateri:** chip-uri doar pentru ultimul text copiat; parametrii de urmărire comparați exact cu litere mici (`UTM_SOURCE` rămâne); „#123” nu e culoare; JWT-ul decodat se copiază, nu se afișează.

### Publicare
- 0.6.16 (P20 + P21), un singur push pe `main` (12923df); release.yml run 16 verde (fum de două ori înainte de „Sign”); release v0.6.16 cu WinNotch.exe (SHA-256 67d7e994…854c) + WinNotch.exe.sig, semnătura ECDSA verificată cu cheia publică din `Services/Updater.cs`.

## Rularea 5 — Curățenie + P23 Raft + P30 Căști/boxe (6 oct 2026)

### Curățenie
- Rândul de publicare 0.6.16 ajuns în `main`; `tests/WinNotch.Smoke/SmokeProgram.cs` împărțit pe zone (bază, alerte, Command Bar, context, clipboard; 63 de metode, aceleași), fără schimbări de comportament; CI run 36 verde, aceleași rânduri PASS (18 / 17 + 2 SKIP).

### P23 Raft drag & drop
- **Făcut:** spike în ADR 0011 — pastila închisă (WS_EX_TRANSPARENT) nu poate primi un drop (OLE o sare la WindowFromPoint) → **planul B**: fișierele se lasă pe notch-ul deschis, deschis și prin hover cât tragi (`ShelfDragHover`: tragerea adusă din afară nu mai e luată drept click). `Features/Shelf`: doar căi, max. 20 (al 21-lea refuzat), fără dubluri, păstrate în setări; dispărutele ies discret; rețea/unități mapate/symlink-uri/.lnk-.url spre rețea refuzate înainte de disc, fără iconița Shell; 7 acțiuni `shelf.*` (copiază calea, deschide folderul, zip, OCR, PNG↔JPG, scoate, golește) fără suprascriere (`FileMode.CreateNew`); tragerea în afară ca FileDrop; comutatorul „shelf” Experimental, oprit.
- **Revizia R1:** 1 Major (drop-urile netratate pe notch-ul deschis lăsau efectul implicit cu Move), 2 Medii (symlink local spre rețea acceptat; imagine blocată/OCR → eroare de funcție, raftul oprit după 3), 7 Minore; reparate în a391dd3 + 3ba33da: Major, Medii, zip gol = eșec, stick scos nu golește raftul, overlay doar la DragEnter cu fișiere, SM_SWAPBUTTON citit rar, fumul oprește comutatorul la eșec; rămase (ADR): mutarea de fereastră peste pastilă o deschide, codarea PNG pentru OCR pe firul UI, teste SH27–33 pe text.
- **CI:** run 37 și run 38 verzi din prima; fumul Raft o singură dată (AM oprit; cealaltă scrie SKIP): fișier local adăugat prin `smoke-shelf-add` → apare → „Copiază calea” → exact calea în clipboard → „Golește” → gol.
- **Teste:** 616 C# (39 noi, SH1–SH39) + 23 extensie + fum 19 / 17 (+3 SKIP).
- **Abateri:** planul B (fără drop pe pastila închisă); al 21-lea element refuzat (nu scos cel mai vechi); raftul e un overlay în notch-ul deschis; cheia veche `Shelf` din settings.json refolosită; refuz în plus pentru .scf/.library-ms/.searchconnector-ms.

### P30 Căști/boxe
- **Făcut:** `Features/AudioSwitch` (logică pură + `PolicyConfigSwitcher` izolat, singurul cod COM: IPolicyConfig pe fir MTA, rolurile eConsole/eMultimedia/eCommunications, obiectele eliberate); buton lângă volum (Acasă) → lista ieșirilor active, implicita bifată, „Nicio ieșire audio” fără dispozitive; acțiuni dinamice `audio.output-<10 hex>` (Safe, și în Command Bar) printr-un provider, reîmprospătat la notificările Windows (debounce 400 ms, fără polling); la prima eroare `FeatureFlags.Disable("audio-switch", motiv fix)` + mesaj în pastilă; fără rutare per aplicație; comutatorul „audio-switch” Experimental, oprit; ADR 0012.
- **Revizia R1:** aprobat, 0 Critic/Major/Mediu (vtable-ul IPolicyConfig verificat), 6 Minore, toate reparate în ec4af3e (dispozitiv scos în timpul schimbării nu mai oprește funcția, SetDefault mereu, oprirea COM în fundal, nume ilizibil → generic, microfoanele ignorate, debounce injectabil).
- **CI:** run 40 roșu (testul de fum: AutomationId pe un Border fără peer UIA, lista negăsită; run 41 la fel, fără reparație), reparat în 9a5ff9f (id pe titlu, cadru cu peer, linie de diagnostic fixă); run 42 verde — 2 încercări din 3.
- **Teste:** 641 C# (25 noi, AS1–AS25) + 23 extensie + fum 20 / 17 (+4 SKIP).
- **Abateri:** id-ul `audio.output-<id>` în loc de `audio.output.<id>` (registrul acceptă un singur punct); lista se deschide dintr-un buton lângă volum (alerta de volum e click-through), nu prin ActivityManager.

### Publicare
- 0.6.17 (P23 + P30), un singur push pe `main`.


## Reparația B1 — notch-ul gol și pastila doar cu ora (6 oct 2026)

- **Cauze găsite:** (1) `FadeLayer`: o animație de ascundere înlocuită își ridica totuși `Completed` și colapsa un strat arătat între timp (afișarea așteaptă 120–140 ms la opacitatea găsită, adesea 0) → standby, formă mică sau alertă goale, doar cu fundalul; (2) iconița vremii cu culori fixe deschise, invizibilă pe tema luminoasă, iar fără date doar „—” → pastila părea să arate doar ora.
- **Reparat:** jetoane per strat (`FadeTokens`, și pentru panou), culorile temei pentru vreme pe temele luminoase, regulile formei mici pure (`PillRules`, data niciodată goală); plasa de siguranță `Features/NotchGuard` (comutatorul „notch-guard”, Stabil, pornit): verificare la 300 ms după deschidere (pagina curentă refăcută, apoi Acasă) și la 450 ms după schimbarea pastilei, cu încă o privire pentru o animație încă în curs; un rând „B1 recover” în log, fără date personale; ADR 0013.
- **Nereprodus:** un drum din cod care să lase panoul fără tab-uri sau pagină cu notch-ul deschis (în afara Command Bar și a unei excepții la jumătatea lui `Expand`); acoperit de plasă și de log.
- **Revizia R1:** 1 Critic (pin-ul QA27), 1 Mediu (verificarea de 300 ms înainte de sfârșitul fade-ului), 5 Minore; reparate în 81140ea, plus fals pozitivul văzut în CI (6205f45, `MaybeFading`); rămas: `WeatherBrush` dă o pensulă fixă (se recitește la reîmprospătare).
- **CI:** run 45 roșu (QA27), run 46 roșu (doar fumul B1: fals pozitiv al plasei, reparat), run 47–48 verzi, toate testele de fum existente pe ambele drumuri. 3 încercări.
- **Teste:** 668 C# (27 noi, NG1–NG27) + 23 extensie + fum 20 / 17 (+4 SKIP). Fumul B1 (golire intenționată + 20 de cicluri) a fost scris și apoi **scos la cererea autorului** (testează el pe Windows); comenzile `smoke-empty-panel` / `smoke-empty-pill` rămân.
- **Abateri:** comutatorul „notch-guard” e Stabil și pornit (reparație anunțată în 0.6.18); fără abonare la `FeatureFlags.Changed` (citit la fiecare verificare, ca P27).
- **Publicare:** 0.6.18, merge --no-ff 96c9d7b + caee8a5 pe `main`; release v0.6.18 cu WinNotch.exe (SHA-256 9781e0a2…d59e) + WinNotch.exe.sig, semnătura ECDSA verificată cu cheia publică din `Services/Updater.cs` („Verified OK”).


## P23 — copierea în bloc din raft (cerere a autorului, 6 oct 2026)

- **Cerut:** „pun mai multe fișiere în raft și după le copiez pe toate în locația de care am nevoie”, cu conflictele de nume
  rezolvate „ca la copy-paste”.
- **Făcut:** bifă pe fiecare rând al raftului (`ShelfSelection`, pură, doar în memorie) și butonul „Copiază selecția (N)” /
  „Copiază tot (N)” în antet; acțiunea `shelf.copy-files` (Safe, parametrul opțional `elemente`: id-uri sau poziții,
  gol = tot raftul) pune căile pe clipboard ca `FileDrop` + „Preferred DropEffect” = copiere, prin `IShelfHost.SetFiles`;
  copierea însăși e a Windows-ului, la `Ctrl+V` (progresul și întrebarea lui la nume luate), deci WinNotch tot nu scrie,
  nu mută și nu șterge niciun fișier al utilizatorului. Căile sunt verificate din nou în fundal înainte de copiere (șterse
  → scoase din raft și sărite; unitate deconectată → rămân, mesaj). Rândurile bifate se trag împreună din raft.
- **Teste:** 672 C# (4 noi, SH35–SH38; SH17, SH29, SH33 actualizate) + 23 extensie; `dotnet run --project
  tests/WinNotch.Tests.csproj` verde pe Linux. Partea WPF (`NotchWindow.Shelf.cs`) nu se compilează pe Linux: rămâne de
  verificat la primul build pe Windows (`build.bat`) și de făcut verificările manuale noi P23.20–P23.23.
- **Abateri:** fără selector de folder în WinNotch (ales de autor: clipboard + `Ctrl+V`), deci fără motor de copiere
  propriu și fără acțiune `Confirm`.
- **Publicare:** 0.6.19 (versiunea, `RELEASE_NOTES.md`, istoricul din `DOCUMENTATIE.md` și „Noutăți” din `README.md`);
  release-ul îl face GitHub Actions la push-ul pe `main`.

## P51c — De ce s-a închis aplicația singură (brief UI 0.7, 6 oct 2026)

- **Cerut:** întâi investigația („nu scrie cod până nu avem o ipoteză sprijinită de dovezi”), apoi jurnalul de închidere.
- **Investigația.** Autorul a trimis `log.txt`. Rândurile cu funcțiile pornite (`Activity Manager: pornit`, `Raft: pornit`,
  `Context: pornit (9 surse)`) apar o dată per proces, deci au delimitat exact două porniri pe 6 octombrie: `19:01:16` și
  `22:33:56`, cu ultimul rând al primei la `21:36:38`. **Niciun `Eroare fatală`, niciun `Eroare neprevăzută`** — ceea ce a
  eliminat cele patru ipoteze de cod din prima trecere (Visualizer, AudioService, firul de temperatură, timerul alertelor):
  `AppDomain.UnhandledException` e abonat și `App.Log` scrie sincron, deci o excepție .NET obișnuită ar fi lăsat urmă.
  Au rămas trei posibilități cu aceeași semnătură (zero rânduri): omorât din afară (jucase CS2 și un repack), crash în cod
  nativ, sau o moarte fără handler. Ipoteza principală a fost cerută explicit cu Event Viewer, pe fereastra `21:30–22:40`.
- **Cauza, confirmată:** `System.AccessViolationException` în `LibreHardwareMonitor.Interop.NvidiaML.NvmlDeviceGetPowerUsage`
  ← `NvidiaGpu.Update()` ← `WinNotch.Services.TempService.<RefreshAsync>b__26_0()`, pe un fir din thread pool, cod
  `0xc0000005`. Se potrivește la secundă: la `21:36:28` un monitor a dispărut și la `21:36:33` a revenit (log-ul arată și
  ieșirile audio re-enumerându-se, 6 → 7 → 8: audio prin DisplayPort). Handle-ul de GPU ținut de la `Computer.Open()`
  murise cu reinițializarea driverului.
- **Două constatări care au schimbat reparația:** (1) linia care a crăpat era **deja** într-un `try`/`catch (Exception)` —
  o excepție de stare coruptă nu e prinsă în .NET 8; (2) **nici `AppDomain.UnhandledException` nu a rulat**, de unde
  log-ul gol. Deci „nicio închidere tăcută” nu se poate rezolva scriind în momentul morții; se rezolvă la pornirea
  următoare, din `StartupGuard`, exact cum cere brief-ul.
- **Făcut:** `Core/Diagnostics/ShutdownJournal.cs` (pur: motive fixe, rândurile din log, regula alertei) și
  `Core/Diagnostics/SensorGuard.cs` (pur: `Read` / `Wait` / `Reopen` / `Blocked`, cu liniștea de 3 s și limita de 2
  închideri bruște). Fiecare ieșire scrie `Închidere: <motiv>`; motivul se salvează în `startup.json` **doar** pe cele două
  căi care nu marchează ieșire curată (eroare fatală, pornire eșuată). `StartupGuard.NameLastClosure` numește închiderea
  anterioară și o numără, și o face și când între timp s-a actualizat versiunea. `Features/Diagnostics` adaugă alerta
  „WinNotch s-a închis singur” cu „Deschide log-ul” (comutatorul `shutdown-report`, Beta, pornit; un cârlig de un rând în
  `UpdateTick`). `TempService` nu mai citește nimic din clipa unei schimbări de monitoare, redeschide biblioteca după 3 s
  de liniște, abandonează o trecere în curs dacă schimbarea vine la mijloc, și se oprește definitiv după 2 închideri
  bruște (pagina Sistem: „temp: oprite”, cu explicație). `TaskScheduler.UnobservedTaskException` abonat (doar tipurile
  excepțiilor, nu mesajele). `try/catch` în `Visualizer.OnData` (plus 0 canale), `AudioService.OnNotify`,
  `ActivityManager.Expire` și firul din `InstallTempHelper`.
- **Reparație de securitate, găsită pe drum:** rândul `Monitoare:` scria **titlul ferestrei** (adrese Telegram, nume de
  torrente, titluri de postări), încălcând secțiunea 14 și regula din ADR 0004. Acum scrie clasa, procesul și geometria,
  fără marcajul monitorului țintă — deci schimbarea unui tab sau mutarea mouse-ului nu mai scriu nimic. Era și cauza
  pierderii dovezilor: peste 300 din ~340 de rânduri erau `Monitoare:`, iar log-ul se golește la 512 KB.
- **Revizia R1:** 1 Critic, 3 Majore, 5 Medii, 6 Minore. **Criticul era o regresie introdusă de mine:** pe calea „alt
  WinNotch a luat mutex-ul”, `Ending` apela `MarkReason` → `Save`, scriind peste `startup.json`-ul instanței care rulează
  și dezactivând în liniște protecția la versiuni stricate. Reparat prin restrângerea lui `MarkReason` la cele două căi
  care au nevoie de el (ceea ce a rezolvat și cele două Majore de cursă din coordonator și pe cea din `RestartAsAdmin`).
  Celelalte reparate: mesajele de excepție scoase din canalul nou de log (Major, securitate); raportul pierdut la
  schimbarea versiunii; fereastra în care handle-ul mort era încă folosit (liniștea de 3 s + abandonarea trecerii +
  `CompareExchange`, ca o schimbare sosită în timpul redeschiderii să nu fie înghițită); `AC2` nu scana fișierul nou;
  condiția din Setări nu se declanșa niciodată când citirea era blocată; alerta pierdută dacă `Alert` o refuza; pin-ul
  SD17 fragil; numele procesului memorat ca să nu deschidem procesul la fiecare secundă. **Acceptate cu motivare:**
  `AppDomain.UnhandledException` scrie în continuare `ToString()`-ul complet (cerut explicit de brief, o dată per moarte);
  `App.xaml.cs` a crescut cu ~45 de rânduri (e fișierul de legătură firesc; `NotchWindow.xaml.cs` e atins 7 rânduri).
- **Teste:** 35 noi (SD1–SD32, cu SD6b/6c, SD20b/20c, SD25b/25c), `AC1` și `AC6` actualizate pentru alerta nouă.
  **Nerulate de mine:** containerul nu are .NET SDK, iar politica de rețea a refuzat `builds.dotnet.microsoft.com`; CI-ul
  le rulează la push pe ramură, autorul cu `tests\run-tests.bat`.
- **CI:** run 52 roșu (3 teste, toate ale mele: `SD19` cu șirul așteptat rămas vechi după reparația Criticului, și `SC1` /
  `SC2`, care comparau secvența exactă de pași a `FakeHost` — rândul nou de jurnal apare acum în ea; se compară pașii fără
  „log”, iar rândul e pinat de `SD20` / `SD20b`). Run 53 încercarea 1: 713/713 C# + 23 extensie + build fără avertismente
  verzi, dar un test de fum roșu (Quick Actions, rezultatul clickului pe microfon în 3 s) — rerulat o dată, verde; rulările
  47–51 trecuseră cu același test și nimic din diff nu atinge `audio.mute-mic`, deci instabilitate de runner (fără
  dispozitiv audio). Run 53 încercarea 2: **tot verde**, ambele drumuri de fum. 2 push-uri.
- **Nereprodus:** crash-ul însuși nu poate fi reprodus fără placa video a autorului; testele acoperă regulile pure și
  pinează locurile din codul WPF.
- **Rămas pentru P51d:** fereastra dintre o schimbare pe care Windows nu a anunțat-o încă și citirea următoare. Doar
  mutarea citirii în alt proces o închide; scris în `docs/ROADMAP.md`, cu mențiunea explicită în DOCUMENTATIE.md.
- **Publicare:** niciuna. Versiunea nu a fost crescută și nu s-a făcut merge în `main`: autorul testează pasul pe Windows
  și confirmă, conform cerinței lui.


## P53 — Pe tot ecranul, notch-ul dispare (cerere a autorului, 7 oct 2026)

- **Cerut:** video pe tot ecranul pe YouTube — notch-ul rămânea „cocoțat” acolo, micșorat dar vizibil; dorit: invizibil
  complet, cu revenire scurtă doar pentru ceva important.
- **Cauza (confirmată din log-ul autorului):** `Busy = covers && !Native.IsZoomed(h)`. Chrome maximizat normal apare
  `[-2568,-8 2576x1408]` (nu acoperă banda barei de activități → `maximizat`, corect), dar în fullscreen apare
  `[-2560,0 2560x1440]`, exact dreptunghiul monitorului, **și păstrează `WS_MAXIMIZE`**, deci `IsZoomed` rămâne `true`
  și fereastra era clasificată `maximizat`, nu `ocupat`. Jocurile reale (Elden Ring, CS2) apăreau corect `ocupat`.
  Testul FS1 folosește exact aceste dreptunghiuri și pică pe codul vechi.
- **Făcut:** `Features/Fullscreen/FullscreenRules.cs` (pur, fără WPF și fără interop): `Classify` decide după geometrie
  și stil (acoperă tot monitorul **și** e fără ramă — fără `WS_CAPTION` / `WS_THICKFRAME` — sau acoperă și banda barei de
  activități), `Ignored` (desktop, shell, fereastra proprie, fără titlu, 0 px), `ForAlert` (trece / se amână / se sare),
  `DurationWhileHidden` (2,5 s, dar alertele cu butoane își păstrează durata), `Opens` (hover și tragere nu, scurtătura /
  Command Bar / tray da) și `DeferredAlerts` (cel mult una de fiecare fel, cea mai recentă, arătată o dată, la 2 s după
  ieșirea din fullscreen). `Services/MonitorService.cs` doar apelează regulile; legăturile în fișierele vechi sunt trei
  rânduri (`PollTick`, `MonitorTick`, `ToggleByHotkey`) plus unul în `Features/Activity/NotchWindow.Activity.cs` (`Alert`).
  Fără cronometru nou: numărătoarea de 2 s merge pe `_mon` (500 ms), care exista.
- **Securitate:** rândul „Monitoare:” din log nu mai conține titlul ferestrei (era personal), doar clasa și dreptunghiul.
- **Comutator:** `fullscreen-hide` (Beta, pornit implicit, oprit în `--safe-mode`); oprit = regula veche, bit cu bit.
- **Teste:** 27 noi (FS1–FS27) în `tests/FullscreenTests.cs`; FS8 codifică regula veche (`strict: false`) și arată
  greșeala, FS1 regula nouă pe exact aceleași dreptunghiuri (regula nouă nu exista pe `main`, deci FS1 nu se putea
  compila acolo). Pinurile FS25–FS27 țin legăturile și absența titlului din log; pinul AR-P13-2 a fost actualizat pentru
  noul prim rând din `Alert`. `dotnet` nu există în container (politica de rețea blochează `builds.dotnet.microsoft.com`):
  rularea testelor s-a făcut în CI, la push pe ramură.
- **Revizia R1:** 2 Majore, 6 Medii, 8 Minore. Reparate: alertele cu butoane nu se mai amână prin reluarea lui `Alert`
  (butoanele s-ar fi legat prea târziu) — se sar, iar porțile lor le oferă din nou (`!_hidden` adăugat și la „ce e nou”,
  ca la ofertă și la extensia veche); `Maximized` nu se mai pierde pentru o fereastră ocupată (altfel, cu „rămâne mereu
  vizibil”, pastila rămânea mare peste film); pașii unui flux (OCR, memorie, descărcare) nu se mai taie la 2,5 s;
  comutatorul oprit golește coada; `Ignored` primește valorile reale și `IsCloaked` se cheamă după filtrele ieftine;
  implicitul când nu există `FeatureFlags` e același în ambele locuri; `Passes(prioritate)` și `PeekFadeMs`, nefolosite,
  scoase; documentația și verificările manuale corectate.
- **Abateri:** Command Bar peste fullscreen rămâne refuzat (regula P14 existentă, `CommandBarRules.OnShortcut`), deși
  brieful îl trece printre intenții explicite — schimbarea ține de P14 și de testele lui; `FullscreenRules.Opens` îl
  acceptă deja, ca regulă. Alertele importante cu butoane (captură, memorie) nu sunt „fără butoane care cer click”, cum
  cere brieful: sunt rezultatul unei unelte cerute de utilizator și butoanele îi sunt utile. „Rămâne cât timp cursorul e
  pe el” (brief, pct. 3) nu e implementat: cât e ascuns, hover-ul nu mai ține și nu mai deschide nimic. Fără abonare la
  `FeatureFlags.Changed` (comutatorul e citit la fiecare alertă și la fiecare tur de monitoare, ca la P27 și notch-guard).
  Fără test de fum nou: starea „ocupat” cere o fereastră reală pe tot ecranul, pe care testul de fum nu o poate crea fără
  să deschidă o aplicație străină; verificările P53.1–P53.14 rămân manuale.
- **Limite cunoscute (scrise în comentariul din `Classify`):** pe un monitor fără bară de activități (sau cu bară cu
  ascundere automată) `zona de lucru == monitorul`, deci rămâne doar criteriul „fără ramă”: o fereastră fără ramă doar
  maximizată e citită ca „ocupat”, iar o aplicație care intră în fullscreen păstrându-și rama nu e. Windows nu dă un
  semnal mai bun; cazul autorului (bară vizibilă) e acoperit de ambele criterii.
- **Stare:** ramura `p53-fullscreen-hide`, fără merge în `main` și fără versiune nouă până la testarea pe Windows
  (verificările P53.1–P53.14 din `docs/TESTE-MANUALE.md`).


## P51 — Închidere uniformă a panourilor (cerere a autorului, 7 oct 2026)

- **Cerut:** „Ieșire audio” rămânea deschis la click în altă parte; la fel raftul, quick actions, galeria, nota paginii
  standard. `Esc` nu închidea nimic (în afară de Command Bar și selecția de regiune).
- **Făcut:** `Core/Ui/OverlayStack.cs` (pur, fără WPF, fără timere): `Register` / `Close` / `CloseAll(nivel)` /
  `Topmost` / `OnOutsideClick(insideId)` / `OnEscape` / `NeedsKeyboard`, cu nivelurile `Panel`, `Hint`, `Modal`
  (un panou nou închide celelalte panouri, nu indiciile; un modal rămâne deasupra) și motivul închiderii
  (`OverlayClose`), care ajunge în log ca rând scurt: „Panou închis: raft (Esc).”
  `Features/Overlays/NotchWindow.Overlays.cs` leagă: un singur `PreviewMouseDown` pe fereastră (pus doar cât comutatorul
  e pornit, scos la `Cleanup`) spune în ce panou a căzut click-ul; click-ul din afara ferestrei (fereastra e
  `NOACTIVATE | TRANSPARENT`, deci nu primește evenimente WPF) și `Esc` se citesc din `PollTick`-ul existent de 30 ms,
  **numai cât teancul nu e gol** — fără cronometru nou, fără `RegisterHotKey` pe `Esc`, fără focus luat de la aplicația
  utilizatorului. Închiderea trece mereu prin rutina existentă a panoului (`ShelfHidePanel`, `AudioSwitchHidePanel`,
  `RemoveQuickActionsRow`, `CloseOverlays`), deci marginile și înălțimea panoului se refac ca înainte.
- **Reparația cerută explicit în brief:** `Gallery.ShowPopupIn` primește `onClosed` și se închide o singură dată; la
  click pe fundal apelantul își curăță starea, deci `_sizes` și `_popup` din `NotchWindow.Pages.cs` nu mai rămân
  referințe moarte (OS12, OS13 — testele care pică pe codul vechi).
- **Id-urile panourilor** sunt exact numele folosite de plasa de siguranță (`NotchGuardOverlays`): „galerie”, „mărimi”,
  „notă”, „raft”, „ieșire-audio”, „quick-actions” — nimic nu e raportat ca „alte-N”.
- **Fereastra WinNotch:** `Esc` prin `PreviewKeyDown` (acolo e focus real), nu prin citirea tastelor în fundal.
- **Comutator:** `overlay-dismiss` (Beta, pornit implicit, oprit în `--safe-mode`), cu abonare la
  `FeatureFlags.Changed` și dezabonare la `Cleanup`; oprit = panourile se închid doar cu butonul lor, ca înainte, și
  nu se citește nicio tastă și niciun click.
- **Teste:** 21 noi (OS1–OS17, cu OS1a/OS1b și OS2b–OS2d) în `tests/OverlayStackTests.cs`, inclusiv reintrarea (rutina
  de închidere a apelantului apelează `Close` din nou) și pinurile pe legături; OS12 și OS13 pică pe codul vechi.
  `dotnet` lipsește în container: rularea e în CI, la push pe ramură.
- **Decizie de arhitectură:** `docs/adr/0014-inchiderea-panourilor.md` (de ce `GetAsyncKeyState` în `PollTick`-ul
  existent și nu `RegisterHotKey` pe `Esc`, nu un hook de tastatură, nu luarea focusului).
- **Revizia R1:** 2 Critice (fișierul de teste lipsea din commit — o comandă scurtcircuitată; raportul din PROGRESS
  care se sprijinea pe el), 1 Major pin spart (SH27: legătura din `Collapse` mutată la final, după `ApplyMode`, ca
  ordinea veche să rămână și ca panourile să nu refacă aspectul în timpul animației), 2 Majore reparate (oprirea
  comutatorului nu închidea nimic și lăsa înregistrări moarte; prima apăsare după golirea teancului era înghițită),
  5 Medii (click într-un panou de dedesubt închidea panoul de deasupra; reacția la orice buton de mouse; `Esc` dublu
  cu Command Bar-ul deschis; `Esc`-ul din fereastra WinNotch ocolea comutatorul; „mărimi” se înregistra cu fundalul,
  nu cu cardul) și Minorele ieftine (`CloseOverlaysCore`, ordinea în `OverlayRegister`, cursorul primit din `PollTick`,
  `Esc` doar cât e deschis un panou — nu un indiciu).
- **Stare:** ramura `p51-overlay-dismiss`, pornită din `main` (fără P53), fără merge și fără versiune nouă până la
  testarea pe Windows (verificările P51.1–P51.15 din `docs/TESTE-MANUALE.md`).


## P51b — O alertă nu stă în calea unei acțiuni (cerere a autorului, 7 oct 2026)

- **Cerut:** era afișată „Pauză pentru ochi”; autorul a început o tragere de fișiere pentru Raft și notch-ul a rămas pe
  alertă, fără să devină țintă de drop.
- **Cauza:** în `PollTick`, ramura alertei iese pe `if (_liveInteractive) return;` — o alertă cu butoane blochează orice
  interacțiune, inclusiv o tragere adusă din afară, iar `ShelfDragHover` era verificat doar mai jos, pe calea standby.
- **Făcut:** `Core/Ui/InterruptRules.cs` (pur): `Interrupts(intenție, areButoane)` — tragerea de fișiere, scurtătura,
  Command Bar-ul, meniul iconiței și deschiderea unui panou întrerup orice alertă; hover-ul doar alertele fără butoane;
  mișcarea mouse-ului, tastatul și o altă alertă niciodată. `InterruptMemory` (pur) ține alerta întreruptă 30 de secunde
  (anti-buclă: altfel ar reapărea la următorul tick și ar întrerupe chiar acțiunea). Legătura e în
  `Features/AlertInterrupt/NotchWindow.AlertInterrupt.cs`: tragerea e recunoscută cu **detectorul raftului**
  (`ShelfDragHover`, P23 — niciun sistem nou), hrănit la fiecare tur al `PollTick`-ului existent, iar alerta se încheie
  prin `EndLive()` (rutina existentă, fără animație de ieșire). Trei legături de un rând: `PollTick`, `ToggleByHotkey`,
  `OnCommandBarShortcut`, plus poarta anti-buclă din `Alert`; `EndLive` rămâne neatins (pinul AC4 îl ține literal), iar
  „alerta s-a terminat” se vede din `PollTick` (`_mode != Mode.Live`).
- **Comutator:** `alert-interrupt` (Beta, pornit implicit, oprit în `--safe-mode`); oprit = o alertă ține pastila până la
  capătul duratei ei, ca înainte.
- **Teste:** 22 noi (IR1–IR20, cu IR10b/IR10c) în `tests/InterruptTests.cs`, inclusiv cazul raportat (tragere peste o alertă cu butoane),
  anti-bucla și pinurile pe legături; `dotnet` lipsește în container, rularea e în CI.
- **Revizia R1:** 2 Critice (pinul AC4 — `EndLive` rămâne neatins, „alerta s-a terminat” se citește din `PollTick`; și
  cazul raportat **care nu era reparat**: după `EndLive`, detectorul raftului nu avea istoricul tragerii, deci
  `_clickedThrough` ținea notch-ul închis până la sfârșitul tragerii), 3 Majore (Activity Manager redesena alerta
  întreruptă și umplea log-ul; răgazul de 30 s înghițea volumul, piesa nouă și oferta one-shot a serviciului de
  temperatură; trei intenții din brief nu erau legate nicăieri), 5 Medii (`_aiCurrentId` pus înainte să se știe dacă
  alerta se vede; Command Bar-ul întrerupea și când nu se deschidea; `_shPrimaryButton` necitit cu raftul oprit;
  comutatorul citit la fiecare tick fără copie, fără abonare, fără `try/catch`; ADR-ul lipsă) și Minorele ieftine
  (același prag `Inside`, ceas dat înapoi, rândul dublat din Command Bar).
  Reparate toate: un singur detector de tragere (`AlertInterruptDragging` folosit și ca `shelfDrag` și ca excepție la
  „dă-te la o parte peste o fereastră maximizată”), `ActivityDismissShown()` înainte de `EndLive`, răgaz de 2 s pe cheia
  fluxului + 1,5 s între întreruperi, `OpenPanel` legat la ieșirea audio și la raft, Command Bar-ul doar pe
  `ShortcutDecision.Open`, `StartAlertInterrupt` / `StopAlertInterrupt` (copie, abonare, dezabonare, butonul principal
  al mouse-ului citit și cu raftul oprit), `try/catch` → `ReportError`, ADR `docs/adr/0015-alerta-nu-sta-in-cale.md`.
- **Abateri:** meniul iconiței întrerupe prin `ToggleByHotkey` (aceeași cale ca scurtătura), deci nu are intenție
  separată; pauza pentru ochi întreruptă își oprește numărătoarea când `EndLive` golește stratul (400 ms), nu pe loc.
- **Stare:** ramura `p51b-alert-interrupt`, pornită din `main`, fără merge și fără versiune nouă până la testarea pe
  Windows (verificările P51b.1–P51b.10).


## P50 — Notch ancorat de ramă (brief UI 0.7, 7 oct 2026)

- **Cerut:** „WinNotch nu e o fereastră care plutește peste Windows, e o prelungire a ramei monitorului”.
- **Făcut:** `Features/NotchAnchored/AnchoredGeometry.cs` (pur, fără WPF): conturul (ureche concavă → latura → colț de jos
  convex → baza → colț de jos convex → latura → ureche concavă, închis pe marginea de sus), raza (`Clamp(setare, 12, 28)`),
  urechea (`Clamp(R*0,75, 10, 22)`, redusă când pastila plus urechile n-ar încăpea în fereastra de 820), opacitatea minimă
  0,92, lățimea în standby 240–520 și constantele umbrei. `Features/NotchAnchored/NotchWindow.Anchored.cs` desenează:
  pastila primește `CornerRadius(0, 0, R, R)`, `Inner.Clip` e un `StreamGeometry` înghețat cu aceeași formă, iar
  racordările sunt un `Path` în grila ferestrei, în spatele pastilei, care împarte cu ea deplasarea (`PillShift`) și
  opacitatea, nu primește mouse-ul și se reconstruiește la schimbarea mărimii (nu per cadru).
- **Legături în fișierele vechi (patru rânduri):** `ApplyMode` (marginea 0, raza, lățimea în standby), `ApplyRadius` și
  `UpdateClip` (forma nouă), plus pornirea/oprirea; `Themes.cs` citește opacitatea prin regula pură.
- **Comutator:** `notch-anchored` (Experimental, **oprit implicit**, până la versiunea care îl anunță); oprit = pastila
  plutitoare de azi, bit cu bit.
- **Teste:** 15 noi (NA1–NA15) în `tests/NotchAnchoredTests.cs`: punctele conturului pentru 300×34 și 720×360, figura
  închisă, urechea redusă, limitele razei, opacitatea, lățimea, umbra, comutatorul, pinurile pe legături și faptul că
  racordările nu intră în zona de hover. `dotnet` lipsește în container: rularea e în CI.
- **Abateri:** pastila rămâne un `Border` (cu colțuri doar jos) plus un `Path` pentru racordări, în loc să devină un
  singur `Path` ca în brief: aceeași siluetă, dar fără să rescriem `NotchWindow.xaml` și fără să atingem straturile
  dinăuntru (regula „fișierele mari se ating minim”). Geometria pură e scrisă oricum punct cu punct și testată, ca să poată
  fi folosită la P52 (antetul ferestrei).
- **Revizia R1:** 1 Major (geometria pură era cod mort — fereastra își scria singură formele, deci testele NA1–NA5
  validau altceva decât ce se desena) și 5 Medii/Minore. Reparate: un singur traducător (`Build`) care transformă
  conturul pur în `StreamGeometry`, folosit și pentru tăierea conținutului (`PillOnly`) și pentru racordări; forma se
  reconstruiește doar când (w, h, rază, ureche) s-au schimbat, nu la fiecare cadru al animației; raza citită e cea
  **animată** (`Radius`), nu setarea, deci forma urmează animația; forma mică și alertele își păstrează raza lor;
  la pornirea/oprirea comutatorului se reconstruiesc pensulele (`ThemeManager.Apply`), altfel opacitatea minimă nu se
  aplica până la următoarea salvare de setări; scalarea urechii se face o singură dată.
- **Rămas minor:** racordările n-au umbră proprie (ar dubla umbra de sub pastilă, fiindcă forma desenată o conține);
  la pornirea comutatorului în timpul rulării marginea pastilei se animă 300 ms, deci racordările „plutesc” atât.
- **Stare:** ramura `p50-notch-anchored`, pornită din `main`, fără versiune nouă până la testarea pe Windows
  (verificările P50.1–P50.11).


## P52 — Fereastra WinNotch v2 (brief UI 0.7, 7 oct 2026)

- **Cerut:** fereastra nouă, cu antetul ca notch desfăcut, trei coloane, **doar cu funcțiile care există azi**.
- **Făcut:** `Features/WindowV2/LayoutRules.cs` (pur): coloanele de carduri după lățime (4 / 3 / 2 / 1), ascunderea
  coloanei din dreapta sub 1100 px, o coloană sub 900 px, lista categoriilor (doar grupuri care există), traducerea
  intrărilor vechi („themes”, „settings”, „news”, un id de pagină) în categorii, categoria unei acțiuni din registru,
  razele și spațierile din brief, sugestia din bara de jos. `Features/WindowV2/WindowV2.cs` desenează fereastra:
  antetul folosește **geometria P50** (`AnchoredGeometry`, nu una nouă), cardurile se construiesc din
  `ActionRegistry.Current.All` (deci nu există card fără acțiune reală) și pornesc doar prin `InvokeAsync`, cu
  confirmare pentru ce nu e `Safe`; coloana din dreapta arată clipboard-ul fixat și `PrivacyService`.
- **Ramura:** pornită din `p50-notch-anchored`, fiindcă antetul refolosește geometria de acolo (altfel ar fi fost
  duplicată). La merge, P50 intră primul.
- **Legături în cod vechi:** un rând în `App.OpenEditor` (`if (OpenWindowV2(pageId)) return;`) plus metoda nouă de
  deschidere din `App.xaml.cs`; `EditorWindow.cs` nu e atins deloc.
- **Comutator:** `window-v2` (Experimental, oprit implicit); oprit sau la orice eroare → se deschide fereastra clasică.
- **Teste:** 15 noi (WV1–WV15) în `tests/WindowV2Tests.cs`: coloanele, ascunderile, categoriile, non-regresia, absența
  culorilor scrise în cod, antetul care refolosește geometria P50, acțiunile doar prin registru, „nimic în curând”.
- **Gata parțial, spus pe față:** conținutul paginilor, temelor, setărilor și noutăților **nu** e mutat încă în v2 — acele
  categorii deschid fereastra clasică la secțiunea lor (`OpenClassicEditor`), ca să nu dublăm logica înainte de a muta-o.
  De asemenea, din coloana din dreapta lipsesc ultima captură și lista de ieșiri audio (nu există o stare citibilă pentru
  ele în afara notch-ului), iar bara de căutare pornește acțiunea potrivită direct, fără să deschidă Command Bar-ul.
  Toate trei rămân de făcut înainte de anunțarea comutatorului.
- **Revizia R1:** 1 Critic (fereastra nu se deschidea deloc: cardul de căutare era mutat într-un `Grid` nou la fiecare
  reconstruire, iar un element WPF are un singur părinte → excepție, prinsă de `OpenWindowV2`, deci se vedea doar ca
  „v2 nu pornește”), 3 Majore (cardurile se reconstruiau la fiecare cadru de redimensionare; fereastra nu respecta
  protocolul comutatorului și nu raporta erorile; tastatura și focusul cerute de brief lipseau) și 7 Medii/Minore.
  Reparate toate: shell-ul (antet, căutare, bară) se construiește o dată și doar cardurile se refac, numai când se
  schimbă numărul de coloane; abonare/dezabonare la `FeatureFlags.Changed` (fereastra se închide dacă comutatorul se
  oprește) și `try/catch` → `ReportError` în fiecare intrare; rândurile din bara laterală sunt butoane (Tab, Enter,
  Space, nume pentru accesibilitate), `Esc` închide fereastra; fundalul ferestrei e un jeton opac (`SegBrush`), ca
  antetul desenat cu `NotchBrush` să se vadă; lățimile se citesc o singură dată, din lățimea ferestrei; rezultatul unei
  acțiuni (inclusiv „lipsește un parametru”) apare în bara de jos; ceasul nu mai bate cu fereastra minimizată și
  reîmprospătează coloana din dreapta; filele din antet sunt cele din brief (Acasă · Sistem · Dispozitive · Unelte),
  cu o categorie „Sistem” proprie; la deschiderea v2 fereastra clasică se ascunde (și invers), ca să nu se salveze una
  peste alta.
- **Rămas de făcut înainte de anunț (pe lângă conținutul setărilor):** sub 900 px bara laterală se strânge la zero în loc
  să devină un rând de jetoane; scurtătura din bara de jos e scrisă fix, nu citită din setări; dialogul de confirmare e
  `MessageBox`, fără tema aplicației.
- **Stare:** ramura `p52-window-v2`, fără versiune nouă până la testarea pe Windows (verificările P52.1–P52.10).


## Reparație P52 — fereastra WinNotch, textul tăiat (raportat de autor pe 0.6.20, 7 oct 2026)

- **Raportat:** captură de ecran a ferestrei WinNotch v2: filele scriau „Ac”, „Si”, „Di”, „Ur”, iar fiecare rând din bara
  laterală era doar o iconiță și „…”; stările din coloana dreaptă („oprit”) stăteau lipite de marginea ferestrei.
- **Cauza:** filele din antet și rândurile din bara laterală foloseau `Ui.S("IconButton")`, iar stilul acela fixează
  `Width=30` / `Height=30` (plus `Focusable=False`). Orice buton cu text era tăiat la 30 px și centrat în coloana lui —
  de aici și iconițele din bara laterală aliniate la mijloc, și tastatura care nu putea parcurge rândurile, deși
  brief-ul o cere explicit.
- **Reparat:** stil nou `NavButton` în `Theme.xaml` (fără mărime fixă, focusabil, contur de 2 px în `AccentBrush` la
  focus de tastatură, fundal `HoverBrush` la hover); `IconButton` rămâne neatins. Coloana din dreapta are acum margini
  pe toate părțile, iar cei 300 px ai ei includ marginile (lățimea stă pe gazdă, nu pe conținut), ca `CenterWidth()` să
  rămână potrivit cu ce e pe ecran.
- **Greșeală pe parcurs:** prima variantă a numit stilul `RowButton`, nume deja folosit în `Theme.xaml` pentru rândurile
  din clipboard și raft. Două resurse cu aceeași cheie fac WPF să arunce la încărcarea dicționarului, deci aplicația
  murea înainte de prima fereastră: cele 822 de teste unitare treceau toate, iar CI-ul a căzut abia la testul de fum.
  De aici testul **WV23**, care refuză orice cheie repetată în `Theme.xaml`.
- **Teste:** WV20–WV23 (pin-uri pe sursă, pică înainte de reparație).
- **CI:** run 76 roșu (cheia dublată, aplicația nu pornea), run 77 verde: 822 C# + 16 + 3 extensie, build, ambele
  drumuri de fum.
- **Publicare:** 0.6.21, la cererea autorului.



## Reparație P50 — geometria și culorile notch-ului ancorat (raportat de autor pe 0.6.21, 9 oct 2026)

- **Raportat:** cu „Notch lipit de ramă” pornit, racordările concave din stânga-sus și dreapta-sus nu se continuau lin
  în colțurile de jos (se vedea o îmbinare), iar pe tema luminoasă forma notch-ului avea altă nuanță decât ce e desenat
  peste ea. Autorul bănuia o pensulă fixă, un jeton greșit sau opacitatea minimă de 0,92 aplicată peste un fundal deja
  opac.
- **Verificat punct cu punct, față de brief → P50 → „Geometria (exact)”:** conturul pur era corect — start `(-E,0)`,
  racordare concavă spre `(0,E)` în sens orar, latura verticală, colț convex spre `(R,H)` în sens antiorar, baza, colț
  convex spre `(W,H-R)`, latura, racordare concavă spre `(W+E,0)`, închis pe `y = 0`. Tangenta e verticală în ambele
  capete ale fiecărei racordări, deci racordarea **este** G1-continuă; `E` se reducea corect când `W + 2E` depășea
  fereastra; `Inner.Clip` folosea deja exact aceeași geometrie (`PillOnly`). Niciuna dintre cauzele bănuite de autor nu
  era pensula: `NotchBrush` e același jeton în ambele locuri.
- **Cauza adevărată:** se desenau **două** suprafețe cu aceeași pensulă — pastila (`Border`, colțuri doar jos) și
  silueta (`Path`, pastilă + racordări, dedesubt). `NotchBrush` are alfa din opacitatea fundalului (minim 0,92 cât e
  ancorat), deci corpul primea două straturi (0,92 peste 0,92 ≈ 0,994) iar racordările unul singur — exact „nuanțe
  diferite” —, iar cele două contururi antialiasate se suprapuneau în colțurile de jos: îmbinarea.
  Abaterea notată la livrarea P50 („pastila rămâne un `Border` plus un `Path`, în loc să devină un singur `Path`”) era
  deci chiar cauza.
- **Reparat:**
  - cât e ancorat, pastila nu-și mai desenează fundalul (`Brushes.Transparent`): silueta e singura suprafață pictată,
    adică exact ce cere brief-ul; umbra se mută pe siluetă, altfel ar cădea din textul pastilei. La oprirea
    comutatorului fundalul se leagă din nou la jetonul temei.
  - silueta urmează pastila prin legături (marginea animată, vizibilitatea, opacitatea, deplasarea), nu prin valori
    copiate, și nu mai e lipită la grila de pixeli (rotunjirea lățimii îi mișca centrul cu o jumătate de pixel față de
    pastilă);
  - fără racordări (fereastră prea îngustă) se desenează conturul pastilei, nu „nimic” — altfel notch-ul ar fi devenit
    invizibil, fiindcă pastila nu mai pictează;
  - racordarea e limitată și de înălțime: pe forma mică (22 px) o racordare de 21 px trecea sub începutul colțului de
    jos, deci latura mergea înapoi și conturul se îndoia peste el însuși;
  - un singur traducător geometrie→WPF (`Features/NotchAnchored/AnchoredShape.cs`), folosit și de antetul ferestrei v2,
    care avea o copie scrisă de mână a arcelor;
  - `ThemeManager.Apply` ținea minte semnătura temei **fără** comutator, deci pornirea sau oprirea lui nu reconstruia
    pensulele: opacitatea minimă de 0,92 aștepta până la următoarea salvare de setări.
- **Teste:** NA18–NA21 (pică înainte de reparație: racordarea limitată de înălțime, o singură suprafață pictată,
  legăturile siluetei, semnătura pensulelor); NA11, NA14, NA16 și WV13 urmăresc structura nouă.
- **Revizia R1 (pe `git diff main...p50-geometry-fix`):** fără Critic sau Major. Verificat: nicio culoare scrisă în cod,
  nicio pensulă fixă, nicio dezabonare lipsă (legăturile mor cu `Path`-ul, scos din arbore la oprirea comutatorului),
  nicio redesenare pe cadru (forma se reconstruiește doar când s-au schimbat lățimea, înălțimea, raza sau racordarea),
  niciun cronometru nou, nimic sensibil în log. Minor rămas: la pornirea comutatorului în timpul rulării marginea
  pastilei se animă 300 ms, iar silueta o urmează prin legătură, deci coboară împreună cu ea — comportamentul vechi era
  ca racordările „plutească” singure în acel interval.
- **Atenție la merge:** ramura atinge și `Features/WindowV2/WindowV2.cs` (antetul folosește traducătorul comun) și
  `tests/WindowV2Tests.cs` (WV13), deci se va ciocni cu `p52-settings-fusion`; se intră una după alta.
- **Stare:** ramura `p50-geometry-fix`, pornită din `main`, fără versiune nouă până la testarea pe Windows
  (verificările P50.12–P50.18, plus P50.1–P50.11 de dinainte).
