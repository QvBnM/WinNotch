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
