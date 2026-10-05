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
| R14 | Actualizarea automată | Cu o versiune mai veche instalată: Setări → „Caută acum”. | Alerta „WinNotch X e gata” cu lista schimbărilor; „Actualizează” descarcă, verifică semnătura, repornește și arată „Actualizat la…”. „Mai târziu” amână. |
| R15 | Setările rămân după repornire | Schimbă poziția, standby-ul, accentul și notița; ieși din meniul iconiței și repornește WinNotch. | Toate valorile sunt la fel; notița și link-ul de calendar se văd (criptate în `settings.json`, nu în clar). |

## Verificări per funcție

Fiecare sarcină din `docs/ROADMAP.md` adaugă aici o secțiune `### PNN — nume` cu verificările ei (pași și rezultat așteptat).
