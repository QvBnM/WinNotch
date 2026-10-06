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
