# ADR 0015 — O alertă nu stă în calea unei acțiuni

- **Stare:** Acceptată
- **Data:** 2026-10-07
- **Sarcina din roadmap:** P51b

## Context

Era afișată alerta „Pauză pentru ochi”; utilizatorul a început o tragere de fișiere pentru Raft, iar notch-ul a rămas pe
alertă — nu s-a transformat în țintă de drop. Cauza: în `PollTick`, ramura alertei iese pe `if (_liveInteractive) return;`,
deci o alertă cu butoane blochează orice interacțiune; iar detectorul de tragere al raftului (`ShelfDragHover`) era hrănit
doar mai jos, pe calea de standby, așa că după ce alerta se încheia tragerea nu mai era recunoscută ca „adusă din afară”
și regula „un click cât mouse-ul e pe pastilă era pentru fereastra de dedesubt” ținea notch-ul închis până la sfârșitul
tragerii.

Constrângeri: fără cronometre noi și fără polling nou (regulile 0.7+); alerta se încheie prin rutina existentă
(`EndLive`), nu prin umblat la straturi; Activity Manager-ul (P13) redesenează o activitate persistentă la finalul lui
`EndLive`, deci o întrerupere nu are voie să intre în buclă cu el; în log nu intră nimic personal.

## Decizie

- Regulile sunt pure, în `Core/Ui/InterruptRules.cs`: `Interrupts(intenție, areButoane)`. Tragerea de fișiere,
  `Win+Alt+N` (și „Deschide notch-ul” din tray, aceeași cale), Command Bar-ul și deschiderea unui panou întrerup orice
  alertă. Hover-ul întrerupe doar alertele fără butoane: la cele la care utilizatorul trebuie să apese ceva (actualizare,
  memorie plină, pauză pentru ochi, confirmări) butonul trebuie să rămână apăsabil. Mișcarea mouse-ului, tastatul în altă
  aplicație și o altă alertă nu întrerup nimic.
- **Un singur detector de tragere:** `ShelfDragHover` (P23), hrănit la fiecare tur al `PollTick`-ului existent din
  `Features/AlertInterrupt`, iar rezultatul lui (`AlertInterruptDragging`) e folosit și pe calea raftului
  (`shelfDrag = ShelfDragHover(ins) || AlertInterruptDragging`) și ca excepție pentru „dă-te la o parte peste o fereastră
  maximizată”. Altfel alerta dispărea, dar notch-ul nu se mai deschidea în timpul acelei trageri.
- **Răgaz scurt, nu lung:** `InterruptMemory` ține alerta întreruptă **2 secunde** (cheia fluxului, nu id-ul), cât să nu
  revină peste gestul pentru care s-a retras; plus `Quiet` = 1,5 s între două întreruperi, ca o activitate persistentă
  redesenată de manager să nu facă buclă. Un răgaz lung ar fi înghițit alerte cerute de utilizator (volumul, piesa nouă).
- **Activity Manager:** la întrerupere se apelează `ActivityDismissShown()` (manager-ul uită activitatea) înainte de
  `EndLive()`, altfel hook-ul lui de la finalul `EndLive` ar desena-o din nou în același tur.
- Comutator `alert-interrupt` (Beta, pornit implicit, oprit în `--safe-mode`), cu copie pe firul UI, abonare la
  `FeatureFlags.Changed` și dezabonare la `Cleanup`; erorile merg în `ReportError`.
- În log un singur rând, cu id-ul alertei și motivul din cuvinte fixe: „Alertă întreruptă: eye-break (tragere de fișiere).”

## Alternative

- **Un detector de tragere propriu pentru P51b** — respinsă: două surse de adevăr pentru aceeași stare (regula „nu
  duplica sisteme”), și exact așa a apărut bug-ul rămas după prima variantă.
- **Să nu se atingă ramura `_liveInteractive`** (doar să se încheie alerta din altă parte) — respinsă: acolo se decide
  dacă interacțiunea ajunge mai departe; fără schimbarea aceea tragerea nu trece.
- **Răgaz de 30 de secunde** (prima variantă) — respinsă la revizie: oprea volumul, piesa nouă și oferta de actualizare
  a serviciului de temperatură (one-shot), nu doar bucla.
- **Întreruperea prin ascunderea straturilor** (`Fade`, `LiveLayer`) — respinsă: ar ocoli `EndLive` și starea
  (`_liveInteractive`, click-through, Activity Manager) ar rămâne în urmă.

## Consecințe

- O intenție nouă a utilizatorului se adaugă într-un singur loc (enum + regulă) și se leagă cu un rând.
- Cine schimbă `PollTick` trebuie să țină minte că tragerea trece pe lângă trei reguli mai vechi: alerta cu butoane,
  „dă-te la o parte peste fereastra maximizată” și click-through-ul.
- Alertele unui flux (OCR, memorie, actualizare) împart răgazul, fiindcă memoria lucrează pe cheia fluxului.
