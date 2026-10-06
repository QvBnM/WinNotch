# WinNotch — roadmap

Fiecare sarcină are un ID (P00, P10, P11…) folosit în commit-uri, pull request-uri și ADR-uri.
Stări: **De făcut** · **În lucru** · **Gata**. Regulile de lucru sunt în `CLAUDE.md` („Reguli pentru dezvoltarea 0.7+”); planul aprobat e în `docs/PLAN.md`,
iar jurnalul rulărilor în `docs/PROGRESS.md`.

## Pregătire

| ID | Sarcină | Stare |
|---|---|---|
| P00 | Reguli pentru agenți, roadmap, ADR, teste manuale, șabloane GitHub, CI | Gata |
| P01 | Revenire automată la versiunea anterioară și pornire în mod sigur (`--safe-mode`), compararea versiunilor, canal beta, măsurători de bază | Gata |
| P02 | Teste de fum în CI pe exe-ul publicat (FlaUI, `--smoke`): pornire, notch, alerte, comutator, scurtătură, meniul iconiței, ieșire curată, log; rulate și în `release.yml` înainte de semnare (revizia R1, 0.6.13) | Gata |

## 0.7 — Fundația

| ID | Sarcină | Stare |
|---|---|---|
| P10 | Feature flags: comutatoare pentru funcțiile noi, mod sigur, secțiunea din Setări, rezumat de sănătate în log | Gata |
| P11 | Action Registry: registru central de acțiuni (nume, iconiță, execuție) folosit de notch, scurtături și Command Bar | Gata |
| P12 | Context Engine: ce faci acum (aplicația din față, media, întâlniri, rețea), publicat ca evenimente | Gata |
| P13 | Activity Manager: activitățile live din notch (prioritate, coadă, durată), peste alertele existente; întâi testele de caracterizare ale celor 27 de alerte; „N noutăți”, pastilă împărțită, peek, `activity.dismiss-all`; teste de fum cu comutatorul oprit și pornit. În 0.6.14, comutatorul `activity-manager` Experimental, oprit implicit (ADR 0006) | Gata |
| P14 | Command Bar: `Win+Alt+Space` (sau `Win+Alt+K`, conflictul semnalat o dată) transformă pastila în câmp de căutare peste Action Registry; parametri în text („volum 30”), săgeți / Enter / Esc, confirmare dublă, fără acțiuni periculoase; focusul înapoi la fereastra anterioară, nimic peste ecran complet; alertele așteaptă ca la notch-ul deschis; fiecare setare e o acțiune `settings.<nume>`; teste de fum pe ambele căi. Comutatorul `command-bar` Experimental, oprit implicit (ADR 0007) | Gata |

## După 0.7 — planul suplu (`docs/PLAN.md`, aprobat 5 oct 2026)

Lista veche 0.8 → 1.0 (planificări pe versiuni: P20, P30, P40, P50, P60) e înlocuită de sarcinile de mai jos; ID-urile P20 și P30
au acum sensul din plan. Fiecare sarcină se face într-o rulare de pilot automat: 1. Pasul 0 + P02 · 2. P13 · 3. P14 + P27 ·
4. P20 + P21 · 5. P23 + P30 · 6. P44 + P46. Versiuni 0.6.x, o publicare pe rulare; 0.7.0 (pornirea implicită a
„activity-manager” și „command-bar”) e un pas separat, după ce autorul le folosește câteva zile (PLAN, „Modificare de plan”).

| ID | Sarcină | Comutator | Stare |
|---|---|---|---|
| P27 | Pagina după context: în Setări, categoria de context (Dev, Browser, Meeting, Game, Media, Office, Creator) → pagină sau „—”; la deschidere notch-ul citește doar snapshot-ul motorului de context; alegerea manuală respectată 10 minute; paginile ascunse / șterse sărite; test de fum o dată (cu activity-manager oprit), cu contextul pus în motor (ADR 0008) | `context-pages` (Experimental, oprit) | Gata |
| P20 | Quick Actions: 2–4 acțiuni sigure sub conținutul notch-ului, la deschidere, după snapshot-ul motorului de context (întâlnire + căști → microfon / volum 40%; media → pauză / următoarea; stick → deschide; baterie sub 20% → economisire); reguli într-un tabel de date; doar acțiuni Safe și disponibile din registru; cu Activity Manager pornit, cel mult o sugestie (peek Low) la 10 minute, cu „Nu mai arăta” per regulă; test de fum pe ambele căi (ADR 0009) | `quick-actions` (Experimental, oprit) | Gata |
| P21 | Smart Clipboard: URL, JSON, culoare hex, email, IP, cale, JWT, telefon → chip-uri în widget-ul Clipboard | `smart-clipboard` | De făcut |
| P23 | Raft drag & drop: spike click-through; referințe (max. 20); copiază calea, folder, zip, OCR, PNG/JPG | `shelf` | De făcut |
| P30 | Căști/boxe: lista ieșirilor audio și acțiuni `audio.output.<dispozitiv>` (IPolicyConfig) | `audio-switch` | De făcut |
| P44 | Game Mode: pastila ascunsă în joc, doar alerte Critical/High, rezumat la ieșire (fără FPS) | `game-mode` | De făcut |
| P46 | Activități live din browser: Glovo, Bolt Food, Tazz, Flashscore, LiveScore, ca activitate persistentă | `browser-live` | De făcut |

După P46: oprire. Autorul folosește aplicația 2 săptămâni, apoi alegem din „Idei pentru mai târziu”.

## Idei pentru mai târziu (nu se fac acum)

- Semnare SignPath, ghid la prima pornire, traducere în engleză (pentru când aplicația merge la alții).
- Notification Inbox (cere package identity); Workflows; Undo Center; desktopuri virtuale; Dev Mode; OBS / Recording HUD.
- API local / CLI; rutare audio per aplicație; control monitoare DDC/CI; Network Intelligence; FPS în Game Mode.
- Din lista veche (scoasă): planificările pe versiuni 0.8 „Context și acțiune”, 0.9 „Control Windows”, 0.10 „Platformă și pro”,
  0.11 „Automatizare” și 1.0 „Lansare” (fostele P20, P30, P40, P50, P60).
