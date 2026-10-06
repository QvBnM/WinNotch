# ADR 0013 — B1: notch-ul gol, pastila doar cu ora și plasa de siguranță

- **Stare:** Acceptată
- **Data:** 2026-10-06
- **Sarcina din roadmap:** B1 (reparație, 0.6.18)

## Context
Pe 0.6.17 (Windows 11, tema luminoasă) autorul a văzut: (1) notch-ul deschis complet gol — panoul mare, cu fundalul temei,
fără tab-uri și fără pagină; (2) pastila doar cu ora — fără dată (cum are forma mică) și fără elementele din standby
(Muzică, Ora, Vremea). Nu avem un log al momentului și nici Windows aici; reproducerea se face doar în testele de fum din CI.
Constrângeri: corpurile `ShowLive` / `EndLive` / `Collapse` și legăturile din `NotchWindow.xaml.cs` sunt fixate de teste;
fără polling sub 2 s; nimic personal în log; aspectul neschimbat.

## Analiza drumurilor
Am urmărit fiecare drum care deschide sau schimbă conținutul: hover (`PollTick` → `Expand`), `Win+Alt+N`
(`ToggleByHotkey`), acțiunile care deschid notch-ul, Command Bar (`Mode.Expanded` fără panou, `LayoutCommandBar`), raftul
(hover cât tragi, overlay), pagina după context (`ShowPane` înainte de `Mode.Expanded`), Activity Manager (o alertă care se
termină cu notch-ul deschis: prezentatorul nu desenează în `Expanded`), revenirea din forma mică (`Touch`), tema
(`RebuildUi`), monitorul și scalarea (`MoveToMonitor`, `ApplyUiScale`, `DpiChanged`). Concluzii:
- **Cauza 1 (găsită, reparată): `FadeLayer`.** Ascunderea unui strat (`IdleLayer`, `MiniLayer`, `LiveLayer`) e o animație
  spre 0 care, la final (`Completed`), colapsează stratul dacă e transparent. WPF ridică `Completed` și pentru o animație
  înlocuită de alta. Afișarea are o întârziere (120–140 ms) în care stratul păstrează opacitatea găsită. `ApplyMode`
  ascunde la fiecare apel toate straturile nefolosite, deci și unul deja ascuns (0 → 0); dacă în următoarele 60–200 ms
  același strat e arătat (o alertă după o schimbare a standby-ului, închiderea notch-ului după un `ApplyMode` cu panoul,
  standby-ul după Command Bar închis repede), ascunderea veche se termină cât afișarea încă așteaptă, la opacitatea 0, și
  colapsează stratul. Rezultat: o pastilă, o formă mică sau o **alertă (și una mare: Memorie, Captură) goale**, doar cu
  fundalul. Panoul notch-ului avea deja o gardă (`_mode != Expanded`); a primit și el jetonul.
- **Cauza 2 (găsită, reparată): culorile vremii.** Iconița vremii din standby, Acasă și widget folosea galben / liliachiu
  deschis fixe, invizibile pe tema luminoasă; fără date de vreme rămânea doar „—”. Standby-ul implicit (Muzică, Ora,
  Vremea) fără muzică arăta deci **doar ora**.
- **Nereprodus în cod:** un drum care să lase panoul notch-ului fără tab-uri sau fără pagină cu `_mode == Expanded`, în
  afara Command Bar (care îl ascunde intenționat) și a unei excepții la jumătatea lui `Expand` / `ShowPane` (prinsă de
  `DispatcherUnhandledException`, scrisă ca „Eroare neprevăzută”). Pentru asta e plasa de siguranță, cu un log care spune ce
  era pe ecran.
- **Regulile formei mici** erau corecte (fereastră maximizată sau inactivitate pe notch; hover / alertă / închidere
  readuc standby-ul complet); au fost mutate într-o funcție pură, testată, iar data e garantată nevidă.

## Decizie
- **`Features/NotchGuard/NotchGuard.cs` (fără WPF, testat):** `FadeTokens` (fiecare animație a unui strat ia un jeton; doar
  cea mai nouă poate colapsa), `PillRules` (forma mică, fereastra maximizată, data, culoarea vremii pe tema luminoasă),
  `NotchView` + `NotchContentRules` (ce înseamnă „gol”: panou ascuns / transparent, fără tab-uri, fără pagină sau pagina
  ascunsă, pastila transparentă, Command Bar invizibil, standby / formă mică / alertă fără strat, formă mică fără dată sau
  cu data tăiată, strat din alt mod deasupra), `NotchRecovery` (pașii), `RecoveryBudget`, `NotchGuardLog`.
- **`Features/NotchGuard/NotchWindow.NotchGuard.cs`:** după fiecare deschidere (legătura de la finalul `Expand`), un
  cronometru one-shot de **300 ms** citește ce e pe ecran; gol → **pasul 1** (panoul și pastila opace, pagina curentă pusă
  înapoi cu `ShowPane`, tab-urile refăcute), verificat după 300 ms → **pasul 2** (`RebuildUi`, apoi Acasă) → altfel scris
  în log și lăsat până la următoarea deschidere. După fiecare schimbare a pastilei (legătura de la finalul `ApplyMode` și
  din aranjarea Command Bar) un one-shot de **450 ms** (animațiile s-au terminat): stratul potrivit arătat pe loc, celelalte
  ascunse, cel mult de 3 ori pe minut. Fiecare reparație scrie un rând „B1 recover” (probleme, mod, straturi, pagina ca id
  standard sau „proprie”, comutatoarele pornite, activitatea ca fel/prioritate, overlay-urile ca nume fixe, pasul) și un rând
  cu rezultatul. Fără polling: nimic nu rulează cât notch-ul nu se schimbă.
- **Comutatorul „notch-guard”:** e o reparație, anunțată în 0.6.18, deci **Stabil, pornit implicit** (și în `--safe-mode`);
  oprit, verificările nu rulează. Reparațiile de cauză (jetoanele, culorile) nu depind de el. Nu e o capabilitate a
  utilizatorului, deci nu are acțiune în registru.
- **Teste de fum:** starea notch-ului primește `;b1=` (problemele de acum) și `;b1r=` (reparațiile); comenzile de test
  `smoke-empty-panel` / `smoke-empty-pill` golesc intenționat panoul / pastila, iar plasa trebuie să le refacă; apoi 20 de
  cicluri pe ambele drumuri ale activity-manager, cu toate comutatoarele noi pornite, fără nicio reparație.

## Alternative
- **Fără întârziere la afișarea straturilor:** ar fi schimbat animațiile aprobate; jetonul repară cauza fără să schimbe aspectul.
- **`BeginAnimation(null)` / `FillBehavior.Stop` pentru animațiile vechi:** WPF tot ar fi ridicat `Completed` pentru ceasul vechi.
- **O verificare periodică a conținutului:** polling în standby (interzis); verificarea legată de schimbări e suficientă.
- **Plasa oprită implicit, ca funcțiile noi:** n-ar fi ajutat exact când apare problema; e o reparație, nu o funcție.

## Consecințe
- Orice strat nou al pastilei se ascunde / arată prin `FadeLayer` (sau prin `NotchGuardShow` / `NotchGuardHide`), ca jetoanele
  să rămână corecte; o animație pe un strat fără jeton poate fi colapsată de una veche.
- Dacă problema apare din nou, `log.txt` spune ce era pe ecran: rândurile „B1 recover” și, înainte de ele, eventualele
  „Eroare neprevăzută”.
- `;b1=` / `;b1r=` și comenzile `smoke-empty-*` sunt folosite de testul de fum.

## Note după revizia R1
- **Pin-ul QA27** (starea de fum) actualizat pentru câmpurile `;b1=` / `;b1r=` adăugate după contoarele Quick Actions.
- **Verificarea de la 300 ms** cădea înainte de sfârșitul fade-ului panoului (120 + 220 ms): pe o mașină lentă „doar
  transparent” ar fi declanșat o reparație falsă. Acum, la prima verificare, problemele care sunt doar `PanelTransparent` /
  `PillTransparent` (stratul vizibil) primesc încă 300 ms înainte de orice reparație (`NotchContentRules.OnlyFading`, NG27);
  restul (panou colapsat, fără tab-uri, fără pagină) se repară la 300 ms, ca în cerință. Reparațiile panoului au și ele un
  buget: cel mult 4 pe minut.
- **O reparație nu mai pornește o altă verificare** (`NotchGuardLaidOut` ignoră apelurile din timpul reparației); rezultatul
  ei e citit o singură dată.
- **Comutatorul fără abonare la `FeatureFlags.Changed`** (abatere asumată, ca la „Pagina după context”, ADR 0008): plasa nu
  are nimic de pornit sau oprit; fiecare legătură și fiecare verificare citesc `IsEnabled` în acel moment, deci oprirea are
  efect imediat (un cronometru deja pornit nu mai face nimic).
- **Testul de fum** numără rândurile „B1 recover” de dinaintea golirii intenționate (o reparație anterioară nu mai poate
  satisface așteptarea).
- Rămas așa (Minor): `WeatherBrush()` dă o pensulă fixă (nu o referință de resursă); standby-ul, Acasă și widget-ul o recitesc
  la reîmprospătare și la schimbarea temei.
- **Testul de fum B1 a fost scos la cererea autorului** (testează el pe Windows). Rămân comenzile `smoke-empty-panel` /
  `smoke-empty-pill` (doar cu `--smoke`) și câmpurile `;b1=` / `;b1r=`, pentru o verificare manuală.
