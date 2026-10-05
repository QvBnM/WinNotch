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

