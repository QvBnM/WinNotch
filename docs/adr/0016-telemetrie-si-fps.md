# ADR 0016 — Cum citim performanța, și cum vom citi FPS-ul

- **Stare:** Acceptată
- **Data:** 2026-10-10
- **Sarcina din roadmap:** P60 (fundația), P61 (modul de joc), P62 (FPS)

## Context

Autorul a cerut o secțiune serioasă de optimizare și monitorizare: memorie care chiar face ceva, un mod de joc adevărat
cu FPS, frametime și 1% low, un rezumat după joc, și nimic cosmetic. PC-ul țintă: Ryzen 7 5800X3D, RTX 4070, 32 GB RAM,
Windows 11, fără iGPU.

Constrângerile sunt dure și toate vin din reguli existente:

1. **Nimic nu rulează elevat** (`CLAUDE.md`, `DOCUMENTATIE.md` secțiunea 14). Ce cere drepturi mari trece prin serviciul
   SYSTEM existent (`Services/TempHelper.cs`), cu o singură confirmare.
2. **Nimic care riscă un ban:** fără injecție în joc, fără hooking, fără citit memoria altor procese.
3. **Fără polling sub 2 secunde în standby.**
4. **Fără snake oil:** o „optimizare” care nu se poate măsura nu se implementează.
5. **P51c:** NVML a închis aplicația fără urmă. `AccessViolationException` din
   `NvmlDeviceGetPowerUsage` ← `NvidiaGpu.Update()`, la o schimbare de monitoare, pe un handle rămas de la
   `Computer.Open()`. În .NET 8 e o excepție de stare coruptă: nici `catch (Exception)`, nici
   `AppDomain.UnhandledException` nu o văd. Greșeala aia nu se repetă.

## Decizie

### 1. Trei surse, în ordinea riscului

| Ce | De unde | Drepturi | Ce se întâmplă la o eroare |
|---|---|---|---|
| Procesor, memorie, commit | `GetSystemTimes`, `GlobalMemoryStatusEx` | niciunul | returnează 0, nimic nu moare |
| Încărcare GPU, memorie video, pe proces | contoarele Windows `GPU Engine` / `GPU Process Memory`, prin PDH | niciunul | `—` în interfață |
| Temperatură, putere, ceasuri, **FPS** | **serviciul SYSTEM**, proces separat | SYSTEM, confirmat o dată | WinNotch nu simte nimic |

Regula care iese din tabel, și care e singura care contează: **nicio bibliotecă de senzori și nicio bibliotecă de la
producătorul plăcii video nu se încarcă vreodată în procesul WinNotch.** Ce nu se poate citi cu un apel de sistem ieftin
se citește în alt proces sau nu se citește.

### 2. Contoarele de performanță Windows pentru GPU, nu NVML

`GPU Engine(*)\Utilization Percentage` și `GPU Process Memory(*)\Dedicated Usage` dau încărcarea pe proces și pe motor
(3D, VideoDecode, Copy, Compute) și memoria video pe proces, fără driver și fără drepturi. Se citesc cu PDH
(`PdhAddEnglishCounter`, deci aceleași nume pe un Windows în română). Numele instanțelor
(`pid_12345_luid_…_engtype_3D`) se desfac într-o clasă pură, `Core/Perf/GpuInstance.cs`, testată fără placă video.

Ce **nu** dau: temperatura, puterea, ceasurile, ventilatorul, motivul limitării. Alea rămân pentru serviciul SYSTEM
(P61/P62).

### 3. FPS-ul: ETW în serviciul SYSTEM, numere pe pipe-ul existent

Pe Windows, singura cale curată de a ști câte cadre a prezentat un joc, fără să atingi jocul, e să citești evenimentele
de prezentare din ETW (`Microsoft-Windows-DxgKrnl`, abordarea PresentMon). O sesiune ETW în timp real pe providerul ăla
cere privilegii (`SeSystemProfilePrivilege` sau grupul *Performance Log Users*).

**Decizia:** sesiunea ETW se deschide în **serviciul SYSTEM care există deja**, iar serviciul publică doar numere, pe
pipe-ul lui unidirecțional, exact ca temperaturile de azi:

- Pipe-ul rămâne `PipeDirection.Out`: serviciul **nu primește nimic**, nici de la WinNotch, nici de la altcineva.
  Granița de securitate de azi nu se mișcă cu un milimetru.
- Serviciul nu știe și nu trebuie să știe ce fereastră e în față (e în sesiunea 0, n-are desktop). Publică statisticile
  pentru PID-ul care a prezentat cele mai multe cadre în ultima secundă, plus PID-ul. Aplicația, în sesiunea
  utilizatorului, știe deja din `ContextEngine` care e procesul din față și le potrivește.
- Nimic nu atinge procesul jocului: ETW e telemetrie pasivă a sistemului operativ, aceeași pe care o citesc PresentMon
  și RTSS în modul fără overlay. Niciun anti-cheat nu are ce vedea.
- Ce iese corect: **FPS, frametime, 1% / 0.1% low, numărul de stutter-uri, modul de prezentare.** Ce **nu** promitem:
  latența reală până la afișare, care cere încă două providere și multă acrobație.
- Consumatorul ETW se scrie peste `TraceEvent` (NuGet, Microsoft), doar pe calea serviciului.
- Serviciul e o copie a exe-ului din Program Files, deci la fiecare actualizare a aplicației trebuie reinstalat (un UAC).
  Fluxul există deja („serviciu învechit”). Cu un serviciu vechi, FPS-ul lipsește curat: funcția spune „fără FPS”, nu
  arată zerouri. Sarcina SYSTEM primește și repornire la eșec, care azi îi lipsește.

### 4. Matematica, separat de tot

`Core/Perf/` nu știe nimic despre Windows și nimic despre WPF: cadențele (`PerfRules`), eșantionul (`PerfSample`),
fereastra fixă de istoric (`SampleRing`), percentilele de frametime (`FrameStats`), trendul de memorie
(`MemoryTrend`), adunarea pe proces și vina (`ProcessRollup`), numele contoarelor (`GpuInstance`). Fiecare număr pe care
îl va vedea autorul e decis acolo și are test.

Definiția lui „1% low” e scrisă o dată și e cea folosită de CapFrameX: media FPS peste cele mai lente 1% din cadre. Sub
un prag de cadre (5 cadre în interval, deci 500 pentru 1% și 5000 pentru 0.1%) nu se raportează nimic: `—`, nu un număr
inventat. Percentila simplă de frametime există separat, ca să nu fie confundate.

### 5. Cadențele, și regula de standby verificabilă

`PerfRules.Pick(enabled, visible, gameRunning)`: oprit → **niciun cronometru** (nu unul lent); ceva se uită la numere →
2 s; joc pornit → 1 s. Trecerea prin toate procesele rămâne la 4 s în ambele cazuri. `AllowedInStandby` e testată și
spune explicit că un ritm sub 2 s fără joc e un bug, nu o setare.

Un singur loc măsoară **pentru secțiunea de performanță**: `PerfMonitor`. Tot ce arată un număr acolo îi cere, cu
`AddViewer` / `RemoveViewer`, și nu există un al doilea cronometru al secțiunii.

**Ce nu e unificat, și se spune pe față** (găsit la revizia R1): pastila notch-ului are de dinainte propriul
`SystemStats`, citit din `SecondTick` o dată pe secundă, permanent, pentru CPU, RAM și rețea. Cu fila Performanță
deschisă, procesorul și memoria se citesc deci din două locuri. N-am unificat-o în P60 pentru că ar fi însemnat să umblu
prin `NotchWindow.xaml.cs` mai mult decât „câteva rânduri de legătură”, pe un drum acoperit de teste de caracterizare.
Unificarea (pastila devine un consumator al lui `PerfMonitor`, iar `SystemStats` dispare) e o sarcină separată, de făcut
după ce secțiunea se așază — altfel aș repara un lucru care funcționează ca să respect o propoziție dintr-un ADR.

### 6. Memoria: ce facem și ce nu

Nu construim nimic pe `EmptyWorkingSet`. Golirea working set-ului mută paginile în fișierul de paginare și ele se întorc
la prima atingere; pe 32 GB câștigul e zero și costul e micro-stutter plus scrieri pe SSD. Butonul vechi ⚡ din widget-ul
Memorie rămâne neatins (non-regresie), dar secțiunea nu se sprijină pe el și nu-l laudă.

Ce facem, cu numere: atribuirea pe proces (adunată după nume), **commit** în loc de „RAM liber”, și detectarea unei
scurgeri în timp. Pragurile trendului sunt sfioase intenționat (peste 50 MB/h, minim 20 de minute, minim 150 MB adunați,
R² ≥ 0,80): o aplicație folosită normal crește repede dar în zig-zag și nu trece; un serviciu care curge crește drept și
încet și trece. Golirea listei de standby și managerul de aplicații de pornire vin în P63, fiecare cu înainte/după.

## Alternative

- **NVML sau LibreHardwareMonitor în procesul WinNotch, cu try/catch.** Respinsă: P51c a dovedit că `try/catch` nu ajută
  la o excepție de stare coruptă. Linia care a crăpat **era deja** într-un `catch (Exception)`.
- **PresentMon ca executabil separat, livrat lângă aplicație.** Respinsă: aplicația e un singur exe self-contained, iar
  un binar străin livrat de noi e o suprafață de încredere nouă, cu actualizările lui.
- **Adăugarea utilizatorului în grupul *Performance Log Users*** ca să putem deschide sesiunea ETW neelevat. Respinsă: e
  o escaladare permanentă a contului autorului, făcută de o aplicație care tocmai se laudă că nu cere drepturi.
- **Overlay injectat sau hooking în joc** pentru FPS. Respinsă de autor și de reguli: risc de ban.
- **`PerformanceCounter` din .NET** în loc de PDH direct. Respinsă: cere un pachet NuGet în plus, iar enumerarea
  instanțelor pentru „GPU Engine” (sute de instanțe) costă sute de milisecunde la fiecare trecere.
- **Un singur fir care citește tot la 1 s.** Respinsă: trecerea prin ~300 de procese costă zeci de milisecunde, iar
  într-un joc exact asta nu avem de dat. De aici cele două ritmuri.
- **Buton „eliberează RAM” cu un număr mare.** Respinsă: e exact lucrul pe care autorul l-a interzis. Numărul se întoarce
  în 30 de secunde, deci nu e o măsurătoare, e o reclamă.

## Adăugat la P61 — ce nu intră în modul de joc

Cererea spunea „să tacă, să amâne alertele”. Nu am scris nimic pentru asta, pentru că există: `fullscreen-hide` (P53)
decide deja ce trece peste un joc pe tot ecranul (`HiddenAlert.Show` / `Defer` / `Drop`), ține ce e amânat și îl arată
o dată după ieșire. O a doua poartă ar fi un sistem paralel — exact ce interzice `CLAUDE.md` („Nu duplica sisteme”) —
cu propriile lui reguli de prioritate care ar intra în conflict cu primele la prima alertă importantă.

Ce **face** modul de joc automat se limitează la lucruri care sunt ale noastre: ritmul de măsurare, prioritatea
propriului proces și amânarea celor două aduceri din rețea. Nimic nu atinge sistemul, deci nu există stare de curățat
după o închidere bruscă și nu e nevoie de jurnal de revenire. Jurnalul apare la P64, când se schimbă planul de
alimentare — adică primul lucru care supraviețuiește procesului nostru.

Moștenirea de la P53 e condiționată, și am scris-o așa și în documentație după revizia R1: P53 tace doar cu setarea
„Peste jocuri / fullscreen” pe „ascuns”. Decizia de a nu construi o a doua poartă rămâne, dar afirmația „modul de joc
moștenește tăcerea” nu e necondiționată.

**Cadrele nu se țin ca listă.** Prima versiune a lui `GameSession` avea un `List<double>` de până la un milion de
timpi de cadru „pentru P62”: 8 MB reținuți, plus o mutare de 8 MB la fiecare adăugare odată atins plafonul, în chiar
partea de aplicație care urmărește memoria — și cu un comentariu care pretindea că nimic nu crește cu numărul de
măsurători. Codul a fost scos (nu avea nici apelant, nici test). P62 aduce cadrele cu un **histogram** de timpi de
cadru, nu cu valorile brute: percentilele ies din el cu precizia unui interval, iar costul e de câțiva kilobytes,
oricât ar dura seara.

## Consecințe

- Mai ușor: orice funcție nouă de performanță are deja eșantioane, istoric, atribuire și percentile, toate testate fără
  Windows. O funcție nouă adaugă o regulă pură și o bucată de interfață, nu un sistem.
- Mai greu: FPS-ul depinde de serviciul SYSTEM și de reinstalarea lui după fiecare actualizare. Trebuie să degradeze
  curat, nu să se plângă.
- **De respectat de acum:** (1) nicio bibliotecă de senzori în procesul WinNotch, niciodată; (2) nimic din secțiunea de
  performanță nu măsoară pe cont propriu — tot prin `PerfMonitor` (pastila are încă `SystemStats`, vezi §5); (3)
  serviciul SYSTEM nu primește intrări, doar publică numere; (4) orice funcție de „optimizare” vine cu înainte/după
  măsurat, altfel nu se face; (5) nume de procese, căi și titluri nu ajung în `log.txt`.
- **Cine ține handle-urile native:** samplerul le deține, iar monitorul nu i le ia niciodată din mână în timpul unei
  treceri (vezi cele două lacăte din `PerfMonitor`). Regula asta a ieșit din revizia R1, care a găsit exact cursa prin
  care `PdhCloseQuery` putea fi apelat peste un `PdhCollectQueryData` în curs — adică P51c, din nou, cu altă
  bibliotecă. Orice funcție viitoare care ține un handle nativ într-un pas periodic respectă același tipar.
