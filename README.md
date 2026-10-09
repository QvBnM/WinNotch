# WinNotch

Un notch tip Dynamic Island pentru Windows 10 și 11, făcut după prototipul din conversație.

## Cum îl pornești

1. **Nu trebuie să instalezi nimic înainte.** Dacă pe calculator lipsește .NET SDK (unealta care construiește aplicația), `build.bat` se oferă să-l descarce de la Microsoft **o singură dată pentru contul tău**, în `%LOCALAPPDATA%\WinNotch\dotnet`, fără instalare în Windows și fără drepturi de administrator (~250 MB). Toate versiunile WinNotch pe care le dezarhivezi după aceea îl folosesc automat. Dacă preferi, îl instalezi tu de pe https://dotnet.microsoft.com/download/dotnet/8.0 (coloana „SDK”, „Windows x64”).

2. **Dezarhivează** folderul `WinNotch` oriunde (de exemplu în `Documente`).

3. **Dublu-click pe `build.bat`.** Prima dată durează 1–2 minute, pentru că descarcă pachetele. La final creează `publish\WinNotch.exe` și îl pornește.

   De acum înainte pornești direct `publish\WinNotch.exe`; nu mai ai nevoie de build sau de SDK pe alt calculator.

> Dacă Windows SmartScreen spune „Windows a protejat computerul”, apasă **Mai multe informații → Rulează oricum**. Apare pentru că aplicația nu e semnată digital.

Dacă build-ul afișează erori, copiază tot textul din fereastră și trimite-l în conversație.

## Noutăți în 0.6.23

- Fereastra WinNotch v2 (experimentală) regândită: o singură navigare, patru file — Workspace, Widgeturi, Teme, Sistem — și coloana din stânga aparține filei pe care ești.
- Workspace: paginile, pagina vie și inspectorul widget-ului, toate pe culorile temei tale.
- Jos, un câmp de comandă care pornește acțiuni (`Ctrl + K`).

## Noutăți în 0.6.22

- Fereastra WinNotch v2 (experimentală) e o singură fereastră: paginile, temele, setările și noutățile se deschid înăuntru.
- „Notch lipit de ramă” (experimental): racordările se leagă lin în colțurile de jos și forma are o singură nuanță, la toate temele.
- În Setări → „Funcții noi”, fiecare funcție spune când se vede, iar cele care merg deja sunt marcate „pornită implicit”.

## Noutăți în 0.6.21

- Fereastra WinNotch v2 (experimentală): filele de sus și categoriile din stânga își arată din nou numele întreg; categoriile se parcurg cu `Tab`.

## Noutăți în 0.6.20

- Peste un joc sau un film pe tot ecranul notch-ul dispare complet și nu mai reapare la mișcarea mouse-ului; doar alertele importante coboară scurt.
- Panourile (ieșire audio, raft, galerie, mărimi) se închid și la click în afara lor și la `Esc`.
- O alertă nu mai stă în calea unei acțiuni: tragerea de fișiere, `Win + Alt + N` și Command Bar-ul o dau la o parte imediat.
- Dacă aplicația se închide singură, motivul ajunge în log și notch-ul te anunță după a doua închidere neexplicată.
- Experimentale noi (Setări › Funcții noi, oprite implicit): „Notch lipit de ramă” și „Fereastra WinNotch v2”.

## Noutăți în 0.6.19

- Raft: bifezi rândurile și le copiezi pe toate deodată — „Copiază selecția”, apoi `Ctrl+V` unde ai nevoie (experimental, din Setări › Funcții noi).

## Noutăți în 0.6.18

- Reparat: notch-ul care se deschidea uneori gol și pastila care părea să arate doar ora; o plasă de siguranță îl reface singur dacă se mai întâmplă.

## Noutăți în 0.6.17

- „Raft” (fișiere trase peste notch, cu acțiuni rapide) și „Căști/boxe” (alegi ieșirea audio de lângă volum) în Setări › Funcții noi (experimentale, oprite implicit).

## Noutăți în 0.6.16

- „Quick Actions” (butoane după ce faci, sub pastilă) și „Smart Clipboard” (butoane pentru ce ai copiat, în widget-ul Clipboard) în Setări › Funcții noi (experimentale, oprite implicit).

## Noutăți în 0.6.15

- „Command Bar” (Win+Alt+Space) și „Pagina după context” în Setări › Funcții noi (experimentale, oprite implicit).

## Noutăți în 0.6.14

- „Manager de activități” în Setări › Funcții noi (experimental, oprit implicit): alertele din notch trec printr-un singur loc; arată la fel ca până acum.

## Noutăți în 0.6.13

- Testele automate de pornire rulează acum chiar pe fișierul care ți se trimite, înainte de semnare; nimic nu se schimbă la ce vezi.

## Noutăți în 0.6.12

- Fiecare versiune e încercată automat înainte de publicare (pornire, notch, alerte, meniul iconiței, închidere); nimic nu se schimbă la ce vezi.

## Noutăți în 0.6.11

- Pregătire internă pentru funcțiile care se adaptează la ce faci: nimic nu se schimbă la ce vezi („Motorul de context”, în Setări › Funcții noi).

## Noutăți în 0.6.10

- Pregătire internă pentru bara de comenzi (Command Bar): nimic nu se schimbă la ce vezi.

## Noutăți în 0.6.9

- **Protecție la o versiune stricată:** dacă o versiune nouă se tot închide, WinNotch încearcă întâi modul sigur, apoi revine singur la versiunea anterioară și nu ți-o mai propune pe cea stricată.
- **Canal beta** (Setări › Comportament): primești și versiunile de test, înaintea celor finale.

## Noutăți în 0.6.8

- **„Caută actualizări”** în meniul iconiței de lângă ceas: verifici tu când vrei, chiar dacă ai oprit căutarea automată.

## Noutăți în 0.6.7

- **Funcții noi (experimental):** o secțiune nouă în Setări de unde vei porni și opri funcțiile noi, fără repornire. Cele care dau erori repetate se opresc singure.
- **Mod sigur:** `WinNotch.exe --safe-mode` pornește fără funcțiile experimentale.

## Noutăți în 0.6.6

- **Actualizări automate:** WinNotch verifică singur dacă există o versiune nouă și te întreabă în notch („Actualizează” / „Mai târziu”). Versiunile sunt construite și semnate de GitHub; WinNotch instalează doar fișiere cu semnătura lui. Fără zip-uri și fără build.bat.

## Noutăți în 0.6.5

- **Securitate (auditul 3):** WinNotch nu mai rulează ca administrator. Pentru temperatura procesorului: meniul iconiței › „Activează temperatura procesorului” (o singură confirmare). Dacă aveai pornirea „ca administrator” de dinainte, Setări îți propune să o înlocuiești.
- **După build, apasă ↻ pe extensia WinNotch în chrome://extensions:** extensia 1.6 folosește autentificarea nouă (cea veche nu mai e acceptată) și descarcă ea copertele.

## Noutăți în 0.6.4

- Fereastra WinNotch se deschide centrată, mai lată și complet pe ecran.
- Cât muți sau redimensionezi un widget, vezi live cum se rearanjează celelalte.
- Galeria are „Toate” și arată fiecare widget așa cum va arăta.

## Noutăți în 0.6.3

- Widget-ul Memorie are un buton ⚡ care optimizează memoria direct de acolo.

## Noutăți în 0.6.2

- Editarea arată ca în Control Center pe iPhone: ⊖ pe colțul din stânga-sus, arc de redimensionare pe colțul din dreapta-jos.
- Mărimile unui widget din galerie se deschid într-un pop-up, galeria nu se mai schimbă.

## Noutăți în 0.6.1

- În editare, butoanele de pe widget-uri stau pe colțuri și nu mai acoperă conținutul; barele de la Memorie și Baterie sunt centrate.

## Noutăți în 0.6

- **Widget-uri și pagini proprii, ca pe iPhone.** Creionul din dreapta-sus al notch-ului intră în editare: tragi widget-urile ca să le muți, tragi unele noi din galerie (intră la mărimea implicită), click pe un widget ca să-i alegi mărimea din previzualizări. Paginile standard rămân la fel. În editare, fiecare tab are un ochi: un click și pagina se ascunde sau apare.
- **O singură fereastră** (rotița din notch sau iconița din tray → „Pagini, teme și setări…”): paginile (le tragi ca să le reordonezi), previzualizare live, opțiunile fiecărui widget, widget-uri personalizate (scurtături, senzor, text, link, ceas din alt oraș, comandă), teme și toate setările.
- **Teme:** întunecat, luminos sau automat după Windows; 6 teme, culori proprii, colțuri, transparență, teme salvate.
- Peste o fereastră maximizată, notch-ul dispare când treci cu mouse-ul pe sub el (ca să ajungi la „+ tab nou”); se deschide împingând mouse-ul de tot sus.
- Butoanele „următoarea” și „anterioara” fac acum exact ce fac tastele media: trec la video-ul sau piesa următoare (YouTube, YouTube Music, Spotify, SoundCloud…), nu mai sar doar 10 secunde. **După build, apasă ↻ pe extensia WinNotch în chrome://extensions.**
- **Alertă de memorie:** când RAM-ul trece de 80% (reglabil), notch-ul îți arată cine consumă cel mai mult și îți propune „Optimizează”.
- Alerta „piesă nouă” nu se mai repetă cât te uiți la un video pe YouTube și nu mai apare deloc când aplicația sau browserul care cântă e chiar fereastra în care ești.

## Noutăți în 0.5.3

- Al doilea audit: 38 de probleme reparate (detalii în `AUDIT-2.md`). Testele automate sunt în `tests/` (`tests\run-tests.bat`).
- După build, apasă ↻ pe extensia WinNotch în chrome://extensions (fără Remove): acum se autentifică cu un cod secret scris de WinNotch în folderul ei.

## Noutăți în 0.5

- Extensia de browser are acum un ID fix și doar ea se poate conecta. **Dacă ai instalat-o înainte:** în chrome://extensions apasă Remove pe ea, apoi Load unpacked din nou (Setări → Tab-uri din browser îți arată folderul).
- (Până la 0.6.4) Pornirea cu Windows ca administrator folosea o copie din Program Files; din 0.6.5 e înlocuită de serviciul de temperatură.
- Lista completă de verificări și de teste e în `AUDIT.md`.

## Ce face

**Standby.** Pastila de sus arată ce alegi în Setări (muzică, oră, dată, vreme, CPU, RAM, temperaturi, baterie, volum, internet). După câteva secunde fără activitate, sau peste o fereastră maximizată, se strânge într-o pastilă mică cu ora și data. O linie subțire dedesubt arată progresul piesei, iar un semn portocaliu „mic” apare cât microfonul e folosit.

**Se deschide la hover** (după 0,4 s) sau cu `Win + Alt + N`, cu patru ecrane. Înălțimea notch-ului se potrivește fiecăruia.

- **Acasă**
  - Piesa curentă, cu coperta, culoarea luată din copertă, **versuri sincronizate** (de la LRCLIB, gratuit) și un **vizualizator** care se mișcă după sunetul real.
  - Butoane de redare și volum.
  - **Doar ce se aude acum** (Spotify, Discord, YouTube, Reels…), ordonate după cât de tare se aud. Pe fiecare o oprești din sunet separat; „+N” deschide lista completă, cu pauză și volum separat pentru fiecare.
  - Cu **extensia de browser**, fiecare tab apare separat (vezi mai jos).
  - Ora, următorul eveniment din calendar și vremea pe următoarele ore.
- **Sistem**
  - Graficul procesorului (încărcare și temperatură) pe ultimul minut.
  - **Internetul:** cât folosești acum față de viteza maximă. „Test viteză” deschide un ecran cu viteza în timp real, comparația cu testul anterior, ping-ul spre router și spre internet, istoricul ultimelor teste și un verdict: dacă trebuie restartat routerul sau problema e la furnizor.
  - Memorie, placă video, baterie și aplicațiile care consumă cel mai mult acum.
- **Dispozitive**
  - **Cine folosește microfonul sau camera** chiar acum și de cât timp, cu buton de oprire a microfonului.
  - Ieșirea și intrarea audio.
  - Tot ce e conectat: Bluetooth, USB, monitoare (rezoluție, Hz) și stick-uri, cu buton „Scoate”.
- **Unelte**
  - **Lansator:** scrii și deschizi aplicații, setări Windows sau foldere. Merge și cu calcule, dacă începi cu `=`.
  - **Captură ecran** (monitorul curent) și **captură zonă** (`Win + Alt + S`): se salvează în Imagini › Screenshots și se copiază, gata de lipit cu Ctrl+V.
  - **Text din ecran** (`Win + Alt + T`): tragi peste orice text (o imagine, un video, un PDF scanat) și e copiat ca text. Folosește recunoașterea de text din Windows, fără internet; pentru diacritice instalează limba română cu „Recunoaștere optică”.
  - **Eliberează RAM:** golește memoria ținută degeaba de aplicații; cache-ul Windows se golește doar dacă WinNotch rulează ca administrator (nu mai e nevoie). Windows reîncarcă ce e nevoie, deci ajută mai ales înainte de un joc sau o aplicație grea.
  - La toate patru, notch-ul dispare înainte, ca să nu apară în captură.
  - **Spații de lucru:** salvezi aplicațiile deschise și pozițiile lor, iar un click le redeschide la fel.
  - **Fereastra activă:** ține-o deasupra celorlalte, mut-o pe celălalt monitor, jumătate de ecran sau mică în colț.
  - **Clipboard** cu ultimele 20 de texte copiate, căutare și elemente fixate.
  - **Notiță.**

**Alerte scurte:** volum, piesă nouă, încărcător, baterie descărcată, temperatură ridicată și **pauză pentru ochi** (la fiecare 20 de minute la PC, 20 de secunde de privit în depărtare; se poate opri din Setări).

**Nu te încurcă:**
- Click-urile trec prin el.
- Devine transparent când stai cu mouse-ul peste el.
- Urmează mouse-ul pe monitoare și se mută de pe monitorul ocupat de un joc sau video în fullscreen.

**Mărime:** pe monitoarele 2K/4K, notch-ul deschis și alertele se măresc automat (Setări → „Mărimea notch-ului deschis”, de la 100% la 160%). Pastila mică din standby rămâne la fel.

Setările sunt în iconița de lângă ceas sau în rotița din colțul notch-ului deschis.

## Tab-uri din browser

Windows vede tot Chrome-ul ca o singură aplicație, așa că trei tab-uri cu sunet ar apărea ca un singur „Chrome”. Extensia WinNotch rezolvă asta. Cu ea, fiecare tab care redă sunet apare separat, cu numele site-ului (YouTube, YouTube Music, Instagram Reels…) și titlul, și are butoane de pauză, volum, oprire sunet și „du-mă la tab”.

Instalare, o singură dată în fiecare browser (Chrome, Edge, Brave, Opera, Vivaldi):
1. Setări WinNotch → „Tab-uri din browser” → **Deschide extensiile**. Calea folderului se copiază automat.
2. Pornește **Modul dezvoltator** (Developer mode), dreapta sus.
3. **Încarcă extensia neîmpachetată** (Load unpacked) → Ctrl+V în bara de adrese a ferestrei → Selectează folderul.

**După o actualizare WinNotch:** în chrome://extensions apasă iconița de reîncărcare (↻) de pe extensia WinNotch, apoi dă refresh tab-urilor deja deschise.

Extensia vorbește doar cu WinNotch de pe același calculator (127.0.0.1). Nu trimite nimic pe internet.

## Calendar

În Setări → Calendar lipești link-ul secret iCal:
- **Google Calendar:** Setări → calendarul tău → „Adresa secretă în format iCal”.
- **Outlook:** Setări → Calendare partajate → Publică un calendar → link ICS.

## Temperaturi

Temperaturile se citesc cu LibreHardwareMonitor. GPU-ul și SSD-ul merg de obicei direct. Pentru **temperatura procesorului** sunt necesare două lucruri:

1. **Driverul PawnIO** (gratuit), de pe https://pawnio.eu. Îl instalezi o singură dată.
2. **Serviciul de temperatură**, activat o singură dată: meniul iconiței › „Activează temperatura procesorului” (sau Setări). Windows cere o confirmare. Un mic proces fără fereastră (contul SYSTEM, din Program Files) citește doar temperaturile și i le dă notch-ului; WinNotch rămâne cu drepturi normale. După un build nou, Setări îți arată „Actualizează serviciul de temperatură”.

Poți opri complet citirea temperaturilor din Setări.

## Ce nu e încă în versiunea asta

- **Notificările Windows** (WhatsApp, Outlook etc.): Windows le dă doar aplicațiilor instalate ca pachet (MSIX). Urmează într-o versiune împachetată.

## Unde sunt datele

`%AppData%\WinNotch\` conține `settings.json` (setări, notița, raftul) și `log.txt` (erori). Din meniul iconiței poți deschide direct folderul.

Ca să-l oprești de tot: iconița din zona de notificări → **Ieșire**. Dacă ai bifat „Pornește odată cu Windows”, debifează din Setări înainte să ștergi aplicația.

## Structura proiectului

| Fișier | Ce face |
|---|---|
| `NotchWindow.xaml(.cs)` | Fereastra notch-ului: forme, animații, standby, alerte, tab-uri |
| `Services/MonitorService.cs` | Detectează fullscreen și ferestre maximizate pe fiecare monitor |
| `Services/MediaService.cs` | Ce rulează acum + control media (API-ul Windows) |
| `Services/AudioService.cs` | Volumul sistemului (NAudio) |
| `Services/TempService.cs` | Temperaturi (LibreHardwareMonitor) |
| `Services/SystemStats.cs` | CPU, RAM, internet, baterie |
| `Services/WeatherService.cs` | Vremea (Open-Meteo, fără cont) |
| `Panes/*.cs` | Ecranele Acasă, Surse audio, Sistem, Dispozitive, Unelte |
| `Services/AudioSessions.cs`, `Visualizer.cs`, `LyricsService.cs` | Sunetul pe aplicații, vizualizatorul, versurile |
| `Services/NetService.cs`, `ProcessMonitor.cs` | Test de viteză, aplicațiile care consumă |
| `Services/PrivacyService.cs`, `DeviceService.cs` | Microfon/cameră în uz, dispozitive conectate |
| `Services/WindowTools.cs`, `Launcher.cs`, `CalendarService.cs` | Spații de lucru, lansator, calendar |
| `SettingsWindow.xaml(.cs)` | Fereastra de setări |
| `TrayIcon.cs` | Iconița de lângă ceas |
