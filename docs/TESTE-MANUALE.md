# WinNotch — teste manuale

Verificări de făcut pe Windows înainte de fiecare versiune (pe lângă testele automate din `tests/`).
Notează pentru fiecare: ✓ / ✗ și, la ✗, ce ai văzut (și ultimele rânduri din `%AppData%\WinNotch\log.txt`).

## Lista scurtă de regresie

| # | Verificare | Pași | Rezultat așteptat |
|---|---|---|---|
| R1 | Hover deschide notch-ul | Ține mouse-ul peste pastilă, fără click. | După întârzierea din Setări (implicit 0,4 s) notch-ul se deschide cu animație; când ieși cu mouse-ul se închide. Focusul rămâne în aplicația ta. |
| R2 | `Win+Alt+N` | Apasă `Win+Alt+N` de două ori, cu altă aplicație în față. | Prima dată notch-ul se deschide, a doua oară se închide. Aplicația din față nu pierde tastatura. |
| R3 | Alerta de volum | Schimbă volumul din tastatură sau din bara Windows. | Apare bara de volum cu numărul; se actualizează lin cât tragi; dispare după câteva secunde. Nu apare fără o schimbare reală. |
| R4 | Piesă nouă din Spotify și din YouTube | a) Pornește o piesă în Spotify, cu altă fereastră în față. b) Pornește un clip în YouTube (Chrome sau Edge, cu extensia), cu altă fereastră în față. | a) Alertă cu coperta, titlul și artistul. b) Alertă cu miniatura și titlul curat (fără „- YouTube”), fără repetare la pauză/reluare. Nu apare dacă aplicația sau browserul care cântă e fereastra din față; aceeași piesă nu e anunțată din nou în 15 minute. |
| R5 | Controale media în browser | Din notch, pe un tab YouTube: pauză, play, următoarea, anterioara, volumul tab-ului. | Fiecare comandă acționează pe tab-ul corect, imediat; starea din notch se actualizează. |
| R6 | Extensia conectată | Setări → „Tab-uri din browser”. | „✓ Conectată: Chrome” (sau browserul tău). După ↻ pe extensie în `chrome://extensions` se reconectează în câteva secunde. |
| R7 | Temperatura CPU | Cu serviciul de temperatură activat, deschide Sistem (sau pune „Temp. CPU” în standby). | Apare o valoare realistă (30–90 °C), actualizată la câteva secunde; WinNotch rulează fără drepturi de administrator (Task Manager → coloana „Elevated”: Nu). |
| R8 | Captură zonă | `Win+Alt+S`, trage un dreptunghi. | Ecranul îngheață și se întunecă, apare dimensiunea în pixeli; după eliberare: alertă cu miniatura, fișier în *Imagini › Screenshots* și imagine în clipboard. Esc anulează. |
| R9 | OCR | `Win+Alt+T`, trage peste un text dintr-o imagine. | „Citesc textul…”, apoi „Text copiat · N rânduri”; textul e în clipboard, cu diacritice dacă limba română e instalată. |
| R10 | Editarea unei pagini | Fereastra WinNotch → o pagină proprie → mută un widget, redimensionează altul, adaugă unul din galerie. | Celelalte widget-uri se rearanjează live, fără suprapuneri; schimbările apar și în notch și rămân după închiderea ferestrei. |
| R11 | Schimbarea temei | Fereastra WinNotch → Teme → alege altă temă, apoi modul luminos/automat. | Notch-ul și fereastra își schimbă culorile pe loc, fără repornire; textul rămâne lizibil. |
| R12 | Se ascunde peste un joc fullscreen | Pornește un joc sau un video pe tot ecranul (inclusiv borderless). Schimbă volumul. | Pastila se ascunde în sus; cu „Se ascunde, apare doar pentru alerte” doar alertele importante mai apar. Pe alt monitor liber notch-ul rămâne vizibil. |
| R13 | Se dă la o parte peste o fereastră maximizată | Maximizează browserul; du mouse-ul peste pastilă și dă click pe „+” (tab nou). | Pastila e mică și dispare cât mouse-ul e peste ea; click-ul ajunge la browser. Notch-ul se deschide doar dacă împingi mouse-ul de tot sus. |
| R14 | Actualizarea automată | Cu o versiune mai veche instalată: Setări → „Caută acum” sau meniul iconiței › „Caută actualizări”. | Alerta „WinNotch X e gata” cu lista schimbărilor; „Actualizează” descarcă, verifică semnătura, repornește și arată „Actualizat la…”. „Mai târziu” amână. |
| R15 | Setările rămân după repornire | Schimbă poziția, standby-ul, accentul și notița; ieși din meniul iconiței și repornește WinNotch. | Toate valorile sunt la fel; notița și link-ul de calendar se văd (criptate în `settings.json`, nu în clar). |

## Verificări per funcție

Fiecare sarcină din `docs/ROADMAP.md` adaugă aici o secțiune `### PNN — nume` cu verificările ei (pași și rezultat așteptat).

### P10 — Comutatoare pentru funcțiile noi (feature flags)

| # | Verificare | Pași | Rezultat așteptat |
|---|---|---|---|
| P10.1 | Secțiunea din Setări | Fereastra WinNotch → Setări → derulează până jos. | Secțiunea „Funcții noi (experimental)” are „Funcție de test” cu descrierea și eticheta portocalie „Experimental”, nebifată. |
| P10.2 | Pornire și oprire fără repornire | Bifează „Funcție de test” → „Salvează”. Apoi debifeaz-o → „Salvează”. | „✓ Salvat” de fiecare dată; nicio repornire, nicio eroare în log; restul setărilor neschimbate. |
| P10.3 | Se păstrează după repornire | Bifează „Funcție de test” → „Salvează”, ieși din WinNotch și pornește-l din nou. | E tot bifată. În `settings.json` apare `"Features": { "demo-flag": true }`; notița și calendarul rămân criptate (`dpapi:`). |
| P10.4 | Setări vechi | Pornește cu un `settings.json` de la 0.6.6 (fără `Features`). | Pornește normal, toate setările vechi sunt la locul lor, „Funcție de test” e nebifată. |
| P10.5 | Mod sigur | Cu „Funcție de test” bifată, ieși și pornește `WinNotch.exe --safe-mode`. | În log: „Pornit în mod sigur…”. În Setări apare nota portocalie despre modul sigur, iar bifa rămâne (alegerea e păstrată). La o pornire normală funcția e din nou pornită. |
| P10.6 | Renunță | Schimbă bifa, apoi „Renunță”. | Bifa revine la valoarea salvată; nimic nu se schimbă. |
| P10.7 | Rezumat de sănătate | Lasă WinNotch pornit peste 6 ore, apoi deschide `%AppData%\WinNotch\log.txt`. | Un rând „Sănătate (6 h): RAM … MB · CPU mediu …% · erori pe funcții: fără erori”, fără titluri, căi sau adrese. |
| P10.8 | Setări după salvare | Bifează „Funcție de test”, schimbă și orașul, apasă „Salvează”, apoi debifeaz-o și „Salvează” din nou fără să ieși din pagină. | De fiecare dată bifa arată starea reală, iar orașul rămâne salvat. |

### Caută actualizări (0.6.8)

| # | Verificare | Pași | Rezultat așteptat |
|---|---|---|---|
| U1 | Din meniul iconiței, la zi | Click dreapta pe iconiță › „Caută actualizări”, cu ultima versiune instalată. | În notch: „Ai ultima versiune (0.6.8).” |
| U2 | Din meniul iconiței, versiune nouă | La fel, cu o versiune mai veche instalată. | Oferta „WinNotch X e gata” apare imediat, chiar dacă ai apăsat „Mai târziu” înainte. |
| U3 | Cu verificarea automată oprită | Debifează „Caută singur versiuni noi”, „Salvează”, apoi „Caută actualizări” (sau „Caută acum” în Setări) cu o versiune mai veche. | Oferta apare în notch; fără apăsarea ta, WinNotch nu mai caută singur. |

### P01 — Revenire automată, mod sigur, canal beta

Fișierele de test se creează în `%AppData%\WinNotch\` (Win+R → `%AppData%\WinNotch`). Pentru P01.2–P01.4 ai nevoie de o instalare actualizată din notch (să existe `WinNotch.old.exe` lângă `WinNotch.exe`).

| # | Verificare | Pași | Rezultat așteptat |
|---|---|---|---|
| P01.1 | Canal beta | Setări › Comportament: „Canal beta (versiuni de test)” oprit → „Caută acum”. Apoi pornit → „Caută acum” (cu un pre-release mai nou publicat pe GitHub). | Oprit: doar versiunile finale (pre-release-ul nu apare). Pornit: pre-release-ul e oferit în notch, cu notele lui. |
| P01.2 | Crash-test → mod sigur → revenire | Creează fișierul gol `crash-test.flag`. Pornește WinNotch de 3 ori (se închide singur după 5 s de fiecare dată), apoi a 4-a oară. | A 4-a pornire repornește singură în modul sigur (în log: „repornesc în modul sigur”, apoi „Pornit în mod sigur”). Modul sigur se închide și el după 5 s → **șterge acum `crash-test.flag`**, apoi pornește WinNotch a 5-a oară, în cel mult 5 minute: WinNotch revine la versiunea anterioară și o pornește singur: lângă exe apare `WinNotch.rejected.exe`, `WinNotch.old.exe` dispare, iar `WinNotch.exe` e versiunea veche. |
| P01.3 | Mesajul după revenire | După P01.2 (versiunea restaurată e 0.6.9 sau mai nouă), lasă WinNotch să pornească. | O singură dată, în notch: „Am revenit la <versiune>: <versiunea refuzată> se închidea”. La pornirea următoare nu mai apare. Cu revenire la 0.6.8 sau mai veche, mesajul nu apare (limitare documentată). |
| P01.4 | `WinNotch.old.exe` păstrat 10 minute | Actualizează din notch la o versiune nouă; uită-te în folderul exe-ului imediat, apoi după 11 minute de rulare normală. | Imediat: `WinNotch.old.exe` e acolo. După 10 minute fără erori: șters (și `WinNotch.rejected.exe`, dacă era). |
| P01.5 | Versiunea refuzată nu mai e oferită | După P01.2–P01.3: „Caută acum” / „Caută actualizări”. | Versiunea refuzată nu e oferită („Ai ultima versiune”). Când apare pe GitHub una mai nouă decât ea, aceea e oferită. |
| P01.6 | „Ieșire” nu se numără | Pornește și închide WinNotch din meniul iconiței („Ieșire”) de 4 ori la rând, în mai puțin de 5 minute; la fel cu repornirea Windows. | Pornește normal de fiecare dată: niciun mod sigur, nicio revenire. |
| P01.7 | Măsurători de bază | Ieși din WinNotch, apoi `powershell -ExecutionPolicy Bypass -File tools\measure-perf.ps1`. Opțional: creează `perf.flag` și deschide notch-ul cu hover. | După ~11 minute apare `docs\perf\baseline-<versiune>.md` cu media și maximul pentru Working Set, Private Bytes și CPU%. Cu `perf.flag`: în log „Perf: hover → primul cadru al deschiderii: N ms” (include întârzierea la hover din Setări). |
| P01.8 | Pornire eșuată la jumătate | (Doar pentru dezvoltare) un build cu o excepție aruncată intenționat în constructorul notch-ului. | Procesul se închide (nu rămâne în Task Manager fără notch); în log „Pornirea a eșuat”; a doua pornire nu spune „WinNotch rulează deja”. |
