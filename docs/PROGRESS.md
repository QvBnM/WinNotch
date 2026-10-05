# WinNotch — jurnalul rulărilor (pilot automat)

## Rularea 1 — Pasul 0 + P02 (5 oct 2026)

- **P02 Teste de fum:** făcut deja în `main` (9e39b8c, release 0.6.12 verificat: exe + .sig, semnătura ECDSA validă); `tests/WinNotch.Smoke` (FlaUI) pornește exe-ul publicat cu `--smoke` și face 9 verificări pe `windows-latest`.
- **Revizia R1** (`git diff 9e39b8c^1...9e39b8c`): aprobat cu reparații: 1 Major (release.yml semna fără teste de fum), 1 Mediu (erorile de comutator și oprirea automată nu picau testul de log), 6 Minore.
- **Reparat pe `p02-fix` (0.6.13):** testele de fum rulează în `release.yml` înainte de „Sign”; `SmokeMode.IsFatalLogLine` + testul SM6; `ElementNotAvailableException` prins; „Ieșire” verifică `startup.json`; avertisment când iconița nu e găsită; repornirea păstrează `--smoke`; ADR 0005 corectat.
- **Rămas așa (Minor, documentat):** polling la 500 ms doar în modul `--smoke` (asumat în ADR 0005); „pastila vizibilă” verifică doar mărimea; alerta de piesă apelată direct, fără detecția piesei; meniul iconiței cade încă pe mesajul de click dreapta la al doilea deschis în CI (acum avertisment vizibil).
- **Teste:** 255 C# + 23 extensie + 9 de fum, toate trec; CI run 20 pe `p02-fix` verde, cu testele de fum rulate efectiv pe Windows.
