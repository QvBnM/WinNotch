# WinNotch: auditul 2 (debug, testare, securitate)

Versiune auditată: 0.5.2. Versiune reparată: **0.5.3**. Data: 5 octombrie 2026.

## Cum s-a făcut

- **Analiză statică independentă,** pe cele 5 dimensiuni din prompt. Patru analize separate au citit tot codul și au urmărit scenarii reale, fără să fi văzut codul cât a fost scris.
- **Verificare înainte de reparare:** fiecare problemă raportată a fost confirmată în cod înainte să fie reparată.
- **Teste automate rulate efectiv:** 80 în total.
  - **63 pentru aplicație.** Rulează pe socket-uri reale: serverul extensiei, autentificarea, limitele. Mai acoperă adresele de rețea, calendarul, verdictul de viteză și calculatorul.
  - **17 pentru extensie,** rulate cu un Chrome simulat.
- **Compilare:** cu API-ul real WPF, **0 erori și 0 avertismente.**

Ce nu se poate face fără Windows: rularea ferestrei, a sunetului și a capturilor. Pentru acestea rămâne lista de verificări W1–W50 din `AUDIT.md`, plus cele noi de la final.

## Rezumat

| Dimensiune | Găsite | Reparate |
|---|---|---|
| 1. Funcțional și cazuri-limită | 9 | 9 |
| 2. UI/UX, scalare, temă | 12 | 12 |
| 3. Performanță | 7 | 7 |
| 4. Securitate și confidențialitate | 7 | 7 |
| 5. Calitate cod și teste | 3 | 3 |
| **Total** | **38** | **38** |

Fișierele modificate sunt cele care apar la fiecare problemă. Fiecare reparație e comentată direct în cod.

---

## 1. Funcțional și cazuri-limită

### F1. Selecția de zonă închisă cu Alt+F4 bloca uneltele și lăsa notch-ul invizibil
**Diagnostic.** `RegionPicker` răspundea doar la Esc, click dreapta, eliberarea mouse-ului sau pierderea focusului. Închisă cu Alt+F4, nu răspundea deloc. `RunHidden` aștepta la nesfârșit: `_toolBusy` rămânea `true`, iar opacitatea notch-ului rămânea 0.
**Impact.** Notch-ul dispărea, iar capturile, textul din ecran și eliberarea RAM nu mai mergeau până la repornire.
**Soluție.** `RegionPicker.cs`
```csharp
Closed += (o, e) => _done.TrySetResult(null);
LostMouseCapture += (o, e) => { if (_start != null && Mouse.LeftButton != MouseButtonState.Pressed) { _start = null; Finish(null); } };
// la eliberarea mouse-ului: _start = null ÎNAINTE de ReleaseMouseCapture(), ca să nu fie tratată ca anulare
```

### F2. Hover pe notch-ul ascuns în timpul unei capturi strica deschiderea la hover
**Diagnostic.** `PollTick` ignora `_toolBusy`. Notch-ul invizibil se deschidea sub selecția de zonă. `ShowInteractive` seta apoi `_liveInteractive = true` chiar dacă alerta nu fusese afișată, iar de atunci `PollTick` ieșea imediat la fiecare apel.
**Impact.** Preview-ul capturii nu apărea, iar notch-ul nu se mai deschidea la hover.
**Soluție.** `NotchWindow.xaml.cs`
```csharp
if (_toolHidden) { ClearDwell(); return; }           // în PollTick
if (_toolHidden) return;                              // în MonitorTick, înainte de MoveToMonitor
private bool ShowLive(...) { if (_mode == Mode.Expanded) return false; ... return true; }
if (!ShowLive(content, w, h, ms, true)) return;       // în ShowInteractive și la pauza pentru ochi
```

### F3. Videoclipurile pe mut care pornesc singure apăreau ca „se aude”
**Diagnostic.** Extensia considera „în redare” orice video nepus pe pauză, inclusiv previzualizările de pe YouTube și videoclipurile din feed-uri, care rulează pe mut.
**Impact.** Treci cu mouse-ul peste o miniatură și cardul mare, plus alerta de „piesă nouă”, sar la previzualizare.
**Soluție.** `extension/content.js`
```js
const audible = m => !m.paused && !m.ended && !m.muted && m.volume > 0;
// + evenimentul "volumechange", ca dezactivarea mutului să fie prinsă imediat
```

### F4. Două profiluri Chrome cu extensia se dădeau afară reciproc la ~4 s
**Diagnostic.** Serverul păstra o singură conexiune pentru fiecare nume de browser. Două profiluri înseamnă două service worker-e, ambele „chrome”, care se înlocuiau unul pe altul la nesfârșit.
**Impact.** Tab-urile apăreau de la un profil, apoi de la celălalt; lista pâlpâia.
**Soluție.** Extensia trimite un id de profil, păstrat în `chrome.storage`. Serverul înlocuiește doar conexiunea aceluiași profil, iar tab-urile se unesc după id.
```csharp
lock (_lock) stale = _conns.Where(c => c != conn && c.Browser == b && (c.Instance == inst || c.Instance.Length == 0)).ToList();
```

### F5. Aplicațiile cu mai multe fluxuri audio arătau mute/volum greșit
**Diagnostic.** La unirea fluxurilor aceleiași aplicații (Discord: voce și notificări), `Volume` și `Muted` rămâneau de la primul flux. Butonul de mute acționa doar pe unul, iar netezirea nivelului era făcută pe proces, nu pe flux.
**Impact.** O aplicație care cânta apărea „pe mut”; mute-ul oprea doar o parte din sunet.
**Soluție.** `Services/AudioSessions.cs`: lista tuturor fluxurilor, mut doar dacă toate sunt mute, iar volumul și mute-ul se aplică tuturor. Netezirea se face pe identificatorul fluxului, iar fluxurile ignorate sunt eliberate (Dispose).

### F6. Id-urile sesiunilor media se mutau când se închidea o sesiune
**Diagnostic.** Id-ul era poziția sesiunii printre cele ale aceleiași aplicații (`Chrome#0`, `#1`). Când se închidea #0, sesiunea #1 primea id-ul ei.
**Impact.** Sursa aleasă de tine devenea alt video, apărea o alertă falsă de „piesă nouă”, iar comenzile puteau ajunge la sesiunea greșită.
**Soluție.** `Services/MediaService.cs`: aceeași aplicație cu același titlu își păstrează id-ul; apoi, în ordine, id-urile rămase ale aplicației (piesa s-a schimbat); altfel un număr nou, nefolosit niciodată.

### F7. Sunetul din browser fără element media (apeluri Meet/Discord, jocuri) era ascuns
**Diagnostic.** Dacă browserul avea vreun tab cu media, chiar și pe pauză, sursa „Chrome” era sărită.
**Impact.** Un apel în Chrome nu apărea niciodată în listă cât era deschis un tab YouTube vechi.
**Soluție.** `Panes/HomePane.cs`
```csharp
if (list.Skip(before).Any(x => x.Visible) || !a.Active) continue;
```

### F8. Calendarul avea mai multe probleme confirmate
**Diagnostic și impact.**
- **Evenimentele de toată ziua** de azi dispăreau după ora 01:00.
- **COUNT** era ignorat, așa că o serie „de 5 ori” se repeta la nesfârșit.
- **EXDATE și ocurențele mutate** erau ignorate: ședințele anulate apăreau, iar cele mutate apăreau de două ori.
- **BYDAY** era ignorat: „luni, miercuri, vineri” apărea doar lunea.
- **Evenimentele lunare și anuale** nu se repetau deloc.
- **TZID** era ignorat: invitațiile din alt fus orar apăreau cu ore greșite.

**Soluție.** `Services/CalendarService.cs` a fost rescris:
- generează pe rând toate ocurențele, cu `COUNT` numărat de la prima;
- suportă DAILY, WEEKLY cu BYDAY, MONTHLY (aceeași zi sau „a 2-a marți” / „ultima vineri”) și YEARLY (cu 29 februarie);
- respectă `UNTIL`, `EXDATE` și ocurențele mutate (`RECURRENCE-ID` cu același `UID`);
- convertește fusul orar, acceptând atât nume IANA cât și Windows;
- evenimentele de toată ziua se caută de la începutul zilei.
- Acoperire: **10 teste noi (C1–C10)**, toate trec.

### F9. Extensia încerca să se reconecteze foarte des cât WinNotch era închis
**Diagnostic.** Evenimentele din tab-uri apelau `connect()` direct, ocolind pauza crescătoare.
**Impact.** Lista de erori a extensiei se umplea la câteva secunde.
**Soluție.** `extension/background.js`: `connect()` respectă `nextTry` pentru orice apelant; doar reîncercarea programată trece peste. Acoperire: test E14.

---

## 2. UI/UX, scalare și temă

### U1. Notch-ul putea apărea în captură sau se deschidea sub selecția de zonă
Aceeași cauză ca F2. Hover-ul e acum ignorat cât notch-ul e ascuns pentru o unealtă.

### U2. Data ultimului test de viteză era mereu tăiată („ULTIMUL · A…”)
**Diagnostic.** Coloana avea ~76 px pentru un text de ~112 px.
**Soluție.** `Panes/SpeedView.cs`: antetul are acum două rânduri („ULTIMUL” / „AZI 14:32”), iar primul rând al tabelului își ia înălțimea după conținut.

### U3. Setări: textul pauzei pentru ochi era tăiat, iar fereastra putea fi mai înaltă decât ecranul
**Soluție.** `SettingsWindow.xaml(.cs)`: textul se rupe pe rânduri (`TextBlock` cu `TextWrapping="Wrap"` în `CheckBox`), iar `Height = Math.Min(680, WorkArea.Height - 24)`.

### U4. Pe mai multe monitoare, indicațiile de la selecția de zonă erau în locul greșit
**Diagnostic.** Indicația era centrată pe toate monitoarele, adesea pe linia dintre ele. Eticheta cu dimensiunea ieșea din ecran la margini.
**Soluție.** `RegionPicker.cs`: indicația stă sus pe monitorul pe care e mouse-ul, iar eticheta e ținută în ecran pe ambele axe.

### U5. Alertele tăiau tocmai textul util
**Diagnostic.** Calea pentru instalarea OCR, linia capturii și rezumatul RAM ieșeau trunchiate.
**Soluție.** Lățimi potrivite pentru fiecare: OCR 560, captură 540, RAM 520. `ToolAlert` primește acum lățimea ca parametru.

### U6. Schimbarea culorii de accent nu actualiza tot
**Soluție.** Legenda graficului CPU și iconița de fixare din clipboard folosesc acum `SetResourceReference(..., "AccentBrush")`, deci se recolorează imediat.

### U7. Bara de scroll era aproape invizibilă pe carduri (contrast 1,6:1)
**Soluție.** `Theme.xaml`: thumb `#6A707A`, `#8A919C` la hover și `#A3AAB4` la tragere. Contrastul e acum peste 3:1 pe cardurile întunecate și în Setări.

### U8. Numele lungi de orașe erau tăiate brusc
**Soluție.** `Panes/HomePane.cs`: grilă pe 3 coloane, `CharacterEllipsis`, fără rupere pe rânduri.

### U9. Trecerea notch-ului pe alt monitor nu mai avea fade
**Diagnostic.** O animație anterioară de opacitate rămânea activă și bloca atribuirea `Opacity = 0`.
**Soluție.** `Pill.BeginAnimation(OpacityProperty, null)` înainte de a porni fade-ul.

### U10. Schimbarea scalei sau a DPI-ului în timpul unei alerte sau cu notch-ul deschis lăsa pastila la mărimea greșită
**Soluție.** `DpiChanged` folosește noul DPI (`_target.Scale = e.NewDpi.DpiScaleX`), iar `ApplyUiScale` reaplică forma dacă notch-ul nu e închis.

### U11. Contrastul scădea sub 4,5:1 în trei locuri
**Diagnostic.** Coperțile foarte deschise, plus sursele pe pauză (opacitate 0,6 aplicată și textului).
**Soluție.** Culoarea extrasă din copertă are luminozitatea plafonată la 0,55. Sursele pe pauză estompează doar iconița, iar textul rămâne lizibil.

### U12. Detalii mărunte
- Mostra de culoare „Alb” din Setări era invizibilă pe fundal alb; acum are contur.
- Subtitlul GPU („temperaturi oprite”) era tăiat; acum e „temp. oprite”.

---

## 3. Performanță și resurse

### P1. Senzorii de temperatură erau citiți la 2 s chiar dacă nimic nu-i afișa
**Soluție.** Citirea se face la 2 s doar dacă notch-ul e deschis sau standby-ul arată temperaturi; altfel la 15 s, suficient pentru alerta de 88 °C.

### P2. Un tab pe pauză trimitea actualizări la nesfârșit
**Diagnostic.** Fiecare actualizare declanșa o reconstruire în WinNotch și ținea treaz service worker-ul extensiei.
**Soluție.** `content.js`: pe pauză și fără schimbări nu trimite nimic. Acoperire: test E17.

### P3. Standby-ul reaplica toată forma (~8 animații) la fiecare schimbare de lățime
**Diagnostic.** Valori precum CPU % schimbau lățimea pastilei aproape în fiecare secundă.
**Soluție.** Se animă doar lățimea, și doar pentru schimbări de peste 6 px. Valorile au și o lățime minimă fixă.

### P4. Obiecte de sistem neeliberate
- **WMI:** colecțiile și obiectele (USB, monitoare) sunt acum eliberate cu `using`.
- **Audio:** fluxurile audio ignorate (sunete de sistem, expirate, WinNotch însuși) sunt eliberate.

### P5. Orele de vreme erau reconstruite în fiecare secundă
**Soluție.** Se reconstruiesc doar când prognoza se schimbă.

### P6. Pensule necongelate și căutarea clasei ferestrei la 30 ms
**Soluție.**
- `Ui.Rgb` întoarce pensule înghețate (`Freeze()`).
- Clasa ferestrei active se caută doar când fereastra activă se schimbă.

### P7. Verificat și corect
- Desenarea pe fiecare cadru e oprită pe toate drumurile spre standby.
- Salvările de setări rulează toate pe firul interfeței, deci nu se suprapun.
- Limitele serverului (256 KB, citire pe bucăți de 16 KB) se aplică și unui cadru de 1 GB.

---

## 4. Securitate și confidențialitate

### S1. Datele criptate care nu puteau fi deschise erau șterse definitiv
**Diagnostic.** Pe alt cont sau alt PC, după restaurarea profilului sau o parolă resetată de administrator, `Unprotect` întorcea „”. Următoarea salvare suprascria nota, link-ul calendarului și clipurile fixate.
**Impact.** Pierdere de date fără niciun avertisment.
**Soluție.** `AppSettings.cs`:
- `TryUnprotect` raportează eșecul;
- valorile care nu pot fi deschise rămân criptate în fișier (`LockedIcs` / `LockedNote`);
- se face o copie `settings.locked.json`, iar evenimentul apare în jurnal;
- dacă DPAPI lipsește, tot apare în jurnal.

### S2. Protecția descărcării copertelor era ocolită cu un proxy de sistem
**Diagnostic.** Cu proxy, verificarea adresei se făcea pe proxy, nu pe serverul copertei.
**Soluție.** `UseProxy = false`. În plus, sunt blocate și formele IPv6 care pot ascunde o adresă IPv4 privată: 6to4 `2002::/16`, Teredo `2001::/32` și NAT64 `64:ff9b::/96`. Acoperire: teste P15–P16.

### S3. Reparația pentru pornirea automată ca administrator putea fi întoarsă împotriva utilizatorului
**Diagnostic.** La orice pornire ca administrator, exe-ul din Documente era copiat automat în Program Files. O alertă îți cerea chiar să-l pornești elevat ori de câte ori exe-ul se schimba, inclusiv dacă fusese înlocuit de alt program.
**Soluție.**
- Copierea automată și alerta au fost eliminate.
- Copia din Program Files se actualizează **doar la click**, din Setări → „Actualizează copia de pornire”, cu WinNotch pornit ca administrator.
- Setările arată când copia e mai veche.

### S4. Între verificarea „nu e scurtătură” și scriere exista o fereastră de atac (rulând ca admin)
**Diagnostic.** Un program fără drepturi putea înlocui folderul cu un junction exact între verificare și scriere. Fișierul `.tmp` nu era verificat deloc.
**Soluție.**
- Cât rulează ca administrator, folderul e ținut deschis fără drept de ștergere sau redenumire, deci nu poate fi înlocuit (`HoldFolder`).
- E verificat și fișierul `.tmp`.
- Jurnalul se golește pe loc, în loc să fie șters.

### S5. Filtrul de parole lăsa textul să treacă la o eroare
**Diagnostic.** Dacă clipboard-ul era ocupat chiar după ce un manager de parole scrisese în el, parola putea fi înregistrată.
**Soluție.** La orice eroare, textul **nu** se înregistrează. Valoarea marcajului e citită acum atât ca `MemoryStream` cât și ca `byte[]`.

### S6. Scriptul de descărcare a SDK-ului
**Diagnostic.** Verificarea „să nu scrie în afara folderului” avea o problemă de prefix (`.dotnet-x` trecea). Nu exista verificare de integritate.
**Soluție.** `tools/get-sdk.ps1`:
- prefixul are acum `\` la final;
- SHA-512-ul arhivei e comparat cu cel publicat de Microsoft în `releases.json`, iar o nepotrivire oprește instalarea.

`build.bat`: scriptul Microsoft de rezervă rulează doar dacă are semnătură digitală validă.

### S7. Orice program local putea copia antetul Origin și se putea da drept extensie
**Diagnostic.** ID-ul fix al extensiei oprea paginile web și alte extensii, nu și programele locale.
**Soluție.** WinNotch generează un **cod secret** de 32 de octeți în folderul extensiei (`token.json`), iar extensia îl trimite la conectare. Fără el:
- conexiunea e închisă;
- mesajele dinaintea autentificării sunt ignorate;
- comparația e în timp constant.

Acoperire: teste B20–B22 și E13.

---

## 5. Calitatea codului și teste

### Q1. Testele nu erau în proiect
**Soluție.**
- Folderul `tests/` conține un proiect de consolă fără pachete (`WinNotch.Tests.csproj`), testele extensiei (`tests/extension/*.test.js`) și `tests/run-tests.bat`.
- Folosește SDK-ul descărcat de `build.bat` dacă există.
- Build-ul aplicației exclude folderul de teste.

### Q2. Funcțiile de siguranță nu puteau fi testate fără WPF
**Soluție.** Verificarea adreselor și miniatura YouTube au fost mutate în `Services/NetSafety.cs`, fără dependențe de interfață, și sunt testate direct.

### Q3. Acoperirea testelor
| Zonă | Teste | Rezultat |
|---|---|---|
| Server extensie: origine, autentificare, limite, profiluri, JSON invalid | B1–B22 | 22/22 |
| Nume de site-uri și titluri | S1–S6 | 6/6 |
| Adrese private/publice, IPv6, miniaturi | P1–P16 | 16/16 |
| Verdictul testului de viteză | V1–V6 | 6/6 |
| Calendar (toată ziua, COUNT, BYDAY, EXDATE, lunar, anual, mutate, fus orar, UNTIL) | C1–C10 | 10/10 |
| Calculator (rezultat, fără injecție, împărțire la zero) | L1–L3 | 3/3 |
| Extensie: background (conexiune, filtrare, comenzi, reconectare, token, pauză) | E1–E14 | 14/14 |
| Extensie: pagină (mut ≠ se aude, sunet pornit, pauză fără trafic) | E15–E17 | 3/3 |
| **Total** | **80** | **80/80** |

---

## Verificări noi pe Windows

Se adaugă la W1–W50 din `AUDIT.md`:
- [ ] W51. Win+Alt+S, apoi Alt+F4 în selecție: notch-ul reapare, iar uneltele merg în continuare.
- [ ] W52. Win+Alt+S cu mouse-ul peste notch: notch-ul nu apare în captură și nu se deschide.
- [ ] W53. Pe pagina principală YouTube, treci peste miniaturi: cardul nu se schimbă.
- [ ] W54. Două profiluri Chrome cu extensia: tab-urile din ambele apar stabil.
- [ ] W55. Un apel Discord sau Meet în Chrome, cu un tab YouTube pe pauză: apelul apare ca sursă.
- [ ] W56. Un eveniment de toată ziua de azi apare și după ora 01:00. O serie „luni, miercuri, vineri” apare în toate cele trei zile.
- [ ] W57. Copiezi `settings.json` pe alt cont: nota nu se pierde, iar `settings.locked.json` apare.
- [ ] W58. Setări → după un build nou, apare „Actualizează copia de pornire” și merge doar ca administrator.
- [ ] W59. După build: în `chrome://extensions` apeși ↻ pe WinNotch (fără Remove), iar extensia se reconectează cu codul secret din folder: în Setări apare „✓ Conectată”.

## O problemă rămasă de investigat

**Modul mic pe monitorul principal,** raportat mai demult, nu are o cauză vizibilă în cod. Jurnalul notează fereastra care decide starea fiecărui monitor. Un rând de forma `Monitoare: M1* … <- <clasă> "<titlu>"` din `%AppData%\WinNotch\log.txt`, copiat cât e deschisă o fereastră maximizată pe monitorul principal, arată exact ce fereastră încurcă detectarea.
