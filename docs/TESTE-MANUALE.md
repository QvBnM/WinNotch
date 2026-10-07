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

### P11 — Action Registry (fără schimbări vizibile)

| # | Verificare | Pași | Rezultat așteptat |
|---|---|---|---|
| P11.1 | Pornește normal, fără regresii | Instalează 0.6.10, pornește WinNotch, apoi parcurge lista scurtă de regresie R1–R15. Deschide `%AppData%\WinNotch\log.txt`. | Totul merge ca în 0.6.9 (butoanele din Unelte, Dispozitive, media, volum, capturi, spații de lucru). În log nu apare „Acțiuni: înregistrarea a eșuat” și nici rânduri „Acțiune …” (încă nu pornește nimeni acțiunile). |

### P12 — Context Engine (fără schimbări vizibile)

Motorul nu are interfață: verificările se fac în `%AppData%\WinNotch\log.txt` (rândurile „Context: …”) și în Task Manager.
`context.show` nu are încă un buton (va fi pornită din Command Bar, P14); logica ei e acoperită de testele automate CX30–CX32.

| # | Verificare | Pași | Rezultat așteptat |
|---|---|---|---|
| P12.1 | Pornește normal, fără regresii | Instalează 0.6.11, pornește WinNotch, parcurge lista scurtă de regresie R1–R15. | Totul merge ca în 0.6.10. În log: „Context: pornit (9 surse).”, fără „Context: pornirea a eșuat”. În Setări › Funcții noi apare „Motorul de context” (Beta, bifat). |
| P12.2 | Ecran complet și rețea în log | Pornește un video pe YouTube și apasă F; apoi ieși din ecran complet. Deconectează Wi-Fi-ul / cablul câteva secunde și conectează-l la loc. | În log apar rânduri „Context: aplicație chrome (Browser), ecran complet Video…”, apoi „…ecran complet None…”; la rețea „rețea Offline”, apoi „rețea WiFi” / „Ethernet”. **Niciun rând nu conține titlul videoclipului sau al ferestrei.** |
| P12.3 | Întâlnire | Intră într-o ședință Teams (sau Zoom, sau Google Meet în browser) cu microfonul pornit; apoi ieși din ea. | Log: „…întâlnire Teams…” (Zoom / Meet) cât ține ședința, apoi „întâlnire nu”. Cu Teams doar deschis, fără ședință, nu apare „întâlnire Teams”. |
| P12.4 | Ieșire audio, monitoare, stick USB | Conectează căști Bluetooth (sau schimbă ieșirea din Windows pe căști); conectează / deconectează un al doilea monitor; introdu și scoate un stick USB. | În câteva secunde, log: „ieșire Bluetooth” / „Headphones”, apoi „Speakers” la revenire; „monitoare 2” / „monitoare 1”; „stick USB” apare și dispare. |
| P12.5 | Alt+Tab rapid nu umple log-ul | Apasă Alt+Tab repede prin 10 ferestre, de câteva ori. | Log-ul nu crește cu un rând pe fereastră (aplicația din față nu se scrie în log); WinNotch rămâne fluid. |
| P12.6 | Comutatorul oprit | Setări › Funcții noi: debifează „Motorul de context” › Salvează; repetă P12.2. Apoi bifează-l la loc. | La oprire: „Context: oprit.”, iar P12.2 nu mai scrie nimic „Context: aplicație…”. La pornire: „Context: pornit (9 surse).” din nou, fără repornirea aplicației. |
| P12.7 | Mod sigur | Pornește `WinNotch.exe --safe-mode`. | În log nu apare „Context: pornit”; restul aplicației merge normal. |
| P12.8 | Consum în standby | Notch închis, 10 minute fără să atingi nimic; Task Manager › Detalii › WinNotch.exe. | CPU rămâne ca în 0.6.10 (aproape 0%); memoria nu crește în timp. După 2 minute fără input nu apar erori în log. |
| P12.9 | Teams fără ședință (R1) | Deschide Teams, intră pe „Calls” / „Apeluri”, apoi pe un chat; nu porni niciun apel. | În log nu apare „întâlnire Teams”. La un apel real („Call with…” / microfon pornit) apare. |
| P12.10 | Google Meet doar în cameră (R1) | Deschide meet.google.com (pagina de start), apoi caută „google meet” pe Google; apoi intră într-o cameră (`abc-defg-hij`). | Pe pagina de start și în căutare: nicio „întâlnire Meet”. În cameră: „întâlnire Meet”. |
| P12.11 | Video doar de la aplicația din față (R1) | Pornește muzica în Spotify, apoi pune o pagină fără video în Chrome pe F11. Apoi pornește un video YouTube în Chrome pe tot ecranul. | Primul caz: „ecran complet Other”. Al doilea: „ecran complet Video”. |
| P12.12 | Camera prin notificări (R1) | Notch închis. Pornește camera (aplicația Cameră sau o ședință), apoi oprește-o. Uită-te în log după „camera nu poate fi urmărită”. | Fără acel rând în log: schimbarea camerei apare în câteva secunde și cu notch-ul închis (de exemplu „întâlnire Teams” când doar camera e pornită de Teams). Cu rândul în log: camera e verificată doar cu notch-ul deschis, la 10 s. CPU în standby ca în P12.8. |

### P13 — Activity Manager (experimental, oprit implicit)

Cu comutatorul oprit, alertele trebuie să arate și să se comporte exact ca în 0.6.13 (aceeași cale de cod; testele AC* o fixează).
Verificările de mai jos sunt cu „Manager de activități” pornit (Setări › Funcții noi › bifează › Salvează), dacă nu scrie altfel.

| # | Verificare | Pași | Rezultat așteptat |
|---|---|---|---|
| P13.1 | Oprit = neschimbat | Comutatorul oprit (implicit). Parcurge lista scurtă de regresie R1–R15 și declanșează volumul, o piesă nouă, o captură de zonă și „Text din ecran”. | Totul ca în 0.6.13; în log nu apare „Activity Manager: pornit.”. |
| P13.2 | Pornire fără repornire | Bifează „Manager de activități” › Salvează. Apoi debifează-l › Salvează. | Log: „Activity Manager: pornit.”, apoi „Activity Manager: oprit.”; o alertă afișată în acel moment dispare la oprire. |
| P13.3 | Alertele arată la fel | Pornit: schimbă volumul (trage de bară câteva secunde), pornește o piesă nouă, scoate încărcătorul (laptop), fă o captură de zonă, „Text din ecran”. | Aceleași alerte, aceeași mărime și durată ca cu el oprit; bara de volum se actualizează lin și dispare la ~1,6 s după ultima schimbare; după captură butoanele „Deschide” / „Folder” merg și închid alerta. |
| P13.4 | O alertă nu o acoperă pe una importantă | Laptop sub 20%, pe baterie: când apare „Baterie descărcată”, pornește repede o piesă nouă. | Bateria rămâne cele 5 s; piesa apare imediat după ea. Cu comutatorul oprit, piesa o acoperea imediat. |
| P13.5 | Fluxurile se înlocuiesc pe loc | „Text din ecran” pe o zonă cu text; „Eliberează RAM” din Unelte. | „Citesc textul…” devine direct „Text copiat · N rânduri”; „Eliberez memoria…” devine direct „Eliberat X GB…”, fără să clipească standby-ul între ele. |
| P13.6 | Notch deschis și ecran complet | Deschide notch-ul și schimbă volumul din tastatură; închide-l. Apoi un video YouTube pe tot ecranul (F) și schimbă volumul; scoate încărcătorul. | Cu notch-ul deschis nu apare nicio alertă și nici după închidere. Peste video: volumul nu apare (Normal), „Pe baterie” nu apare; o baterie descărcată (High) ar apărea. |
| P13.7 | Butoanele alertelor | Așteaptă (sau provoacă) alerta de memorie; apasă „Mai târziu”. Imediat după, schimbă volumul. | Alerta se închide la click; volumul apare imediat (nu după 15 s). Hover-ul pe notch funcționează după închiderea alertei. |
| P13.8 | Acțiunea `activity.dismiss-all` | Cu „Command Bar” pornit (P14): `Win+Alt+Space`, scrie „închide activitățile”, Enter. Altfel, prin testele de fum. | Tot ce arăta managerul dispare. Cu managerul oprit, acțiunea nu apare în Command Bar. |
| P13.9 | Consum în standby | Pornit, notch închis, 10 minute fără alerte; Task Manager › Detalii › WinNotch.exe. | CPU ca în 0.6.13 (aproape 0%); memoria nu crește. |
| P13.10 | Mod sigur | Comutatorul pornit, apoi `WinNotch.exe --safe-mode`. | Log fără „Activity Manager: pornit.”; alertele merg pe calea veche. |
| P13.11 | Formele noi (teste de fum) | GitHub › Actions › ultima rulare CI › pașii „Smoke tests (activity-manager off/on)”; artefactul „smoke-artifacts”. | Ambii pași verzi: „TEST DE FUM (activity-manager oprit): 16 PASS” și „(… pornit): 16 PASS” (15 / 16 până la P27; 12 / 13 până la P14); în `activity-manager-on\log.txt` rândurile „activitate de test … Grouped/Shown” și „activity.dismiss-all → făcut”, fără erori. |

### P14 — Command Bar (experimental, oprit implicit)

Cu comutatorul oprit nu se înregistrează nicio scurtătură: `Win+Alt+Space` face ce făcea înainte (nimic din WinNotch).
Verificările de mai jos sunt cu „Command Bar” pornit (Setări › Funcții noi › bifează › Salvează), dacă nu scrie altfel.
Fă-le o dată cu „Manager de activități” oprit și o dată pornit, unde scrie „ambele căi”.

| # | Verificare | Pași | Rezultat așteptat |
|---|---|---|---|
| P14.1 | Oprit = nimic | Comutatorul oprit (implicit). Apasă `Win+Alt+Space` într-un Notepad. | Nu se deschide nimic din WinNotch; în log nu apare „Command Bar: pornit.”. |
| P14.2 | Pornire și oprire fără repornire | Bifează „Command Bar” › Salvează; apoi debifează › Salvează. | Log: „Command Bar: pornit.” și „scurtătura Win+Alt+Space e activă.”; la oprire „Command Bar: oprit.” și „… a fost eliberată.”. |
| P14.3 | Se deschide cu tastatura | Scrie ceva în Notepad, apoi `Win+Alt+Space`. | Pastila devine un câmp de căutare (colțuri rotunjite, culorile temei) cu acțiunile folosite recent; poți scrie imediat, fără click. |
| P14.4 | Parametri în text | Scrie „volum 30”, Enter. Apoi „volum 150”; apoi „volum”, Enter. | Volumul devine 30%, bara se închide și Notepad are din nou tastatura. „volum 150” nu leagă valoarea; „volum” + Enter arată „Scrie și „Volum” după comandă…” fără să schimbe volumul. |
| P14.5 | Săgeți, Esc, focus | Scrie „setari”, mișcă selecția cu săgețile, apoi Esc. | Selecția se mută (circular); Esc închide bara și cursorul e înapoi în Notepad, exact unde era. |
| P14.6 | Click în altă parte | Deschide bara, apoi dă click în altă fereastră (de exemplu browserul). | Bara se închide; browserul rămâne în față, cu tastatura. |
| P14.7 | Confirmare dublă | Cu un stick USB conectat: scrie „scoate”, Enter; apoi o dată „scoate”, Enter, săgeată jos și sus, Enter. | Primul Enter arată „Apasă Enter din nou pentru a confirma” (stick-ul nu e scos); al doilea Enter îl scoate. După săgeți, Enter cere din nou confirmarea. |
| P14.8 | Setări la o opțiune | Scrie „setari pozitie”, Enter; apoi „setari ochi”, Enter; apoi „setari spatii”. | Fereastra WinNotch se deschide la Setări, derulată la „Poziție” (cu focusul pe listă), apoi la „Pauză pentru ochi”; „Spații de lucru” se deschide la secțiune. |
| P14.9 | Nimic peste ecran complet | Pornește un joc sau un video YouTube pe tot ecranul (F), apoi `Win+Alt+Space`. | Nu se deschide nimic, jocul / video-ul nu pierde tastatura; log: „nu se deschide peste o aplicație pe tot ecranul”. |
| P14.10 | Alertele așteaptă (ambele căi) | Bara deschisă: schimbă volumul din tastatura media; cu managerul pornit, provoacă și o alertă importantă (baterie). Închide bara. | Cât e deschisă, pastila rămâne bara (nicio alertă nu o înlocuiește). După închidere: alertele obișnuite nu mai apar (ca la notch-ul deschis); cu managerul pornit, Critical / persistentele apar după închidere. |
| P14.11 | Conflictul scurtăturii | Pornește o aplicație care ocupă `Win+Alt+Space` (de exemplu un alt launcher), apoi pornește comutatorul. Mergi în Setări › Comportament, alege `Win+Alt+K` › Salvează. | O singură alertă: „Win+Alt+Space e folosită de altă aplicație · Alege Win+Alt+K în Setări…”; același mesaj sub opțiune. După Salvează: `Win+Alt+K` deschide bara, mesajul dispare. Alerta nu reapare la fiecare salvare. |
| P14.12 | Acțiuni periculoase | (Azi nu există niciuna.) | — Fixat de testele automate (CB12). |
| P14.13 | Fără consum | Comutator pornit, bara închisă, 10 minute; Task Manager › WinNotch.exe. | CPU ca înainte (aproape 0%); memoria nu crește după 20 de deschideri / închideri. |
| P14.14 | Log fără text | După P14.3–P14.8, deschide `%AppData%\WinNotch\log.txt`. | Doar „Command Bar: deschis/închis” și „Acțiune <id> (CommandBar): reușită”; niciun text scris, nicio valoare („30”). |
| P14.15 | Mod sigur | Comutatorul pornit, apoi `WinNotch.exe --safe-mode`. | `Win+Alt+Space` nu deschide nimic; în log nu apare „Command Bar: pornit.”. |

### P27 — Pagina după context (experimental, oprită implicit)

Cu comutatorul oprit, notch-ul se deschide pe pagina de data trecută, ca înainte. Verificările de mai jos sunt cu „Pagina
după context” pornită (Setări › Funcții noi › bifează › Salvează) și „Motorul de context” pornit (implicit), dacă nu scrie altfel.
Pregătire: Setări › „Pagina după context”: Programare → Sistem, Browser → Dispozitive, Întâlnire → Unelte › Salvează.

| # | Verificare | Pași | Rezultat așteptat |
|---|---|---|---|
| P27.1 | Oprit = nimic | Comutatorul oprit (implicit), maparea de mai sus. Lucrează în VS Code, deschide notch-ul. | Se deschide pe pagina de data trecută; în log nu apare „Pagina după context”. |
| P27.2 | Setările implicite | Pe un `settings.json` fără `ContextPages` (sau după „Renunță”), deschide Setări › „Pagina după context”. | Toate cele 7 rânduri (Programare, Browser, Întâlnire, Joc, Media, Birou, Creație) arată „—”. |
| P27.3 | Categoria aleasă | Comutatorul pornit. Click în VS Code (sau Visual Studio), apoi hover pe notch; închide; click în Chrome, `Win+Alt+N`. | Prima dată notch-ul se deschide pe Sistem, a doua oară pe Dispozitive; în log „Pagina după context: Dev.” și „… Browser.” (fără numele aplicației sau titlu). |
| P27.4 | „—” | Click în Word (Birou are „—”), deschide notch-ul. | Rămâne pe pagina de data trecută. |
| P27.5 | Alegerea manuală, 10 minute | În Chrome, deschide notch-ul (Dispozitive), dă click pe tab-ul Acasă, închide. Redeschide de câteva ori în 9 minute (tot din Chrome); apoi după 10 minute. | În primele 10 minute se deschide pe Acasă; după 10 minute, din nou pe Dispozitive. |
| P27.6 | Pagină ascunsă sau ștearsă | Ascunde pagina Sistem (ochiul din editare); deschide notch-ul din VS Code. Apoi mapează Programare pe o pagină a ta și șterge-o. | Notch-ul rămâne pe pagina de data trecută, fără mesaje și fără rânduri noi în log. La a doua, în Setări rândul Programare arată „—”. |
| P27.7 | Întâlnire | Într-un apel Teams / Zoom sau Meet în browser (microfonul pornit), deschide notch-ul. | Se deschide pe Unelte (Întâlnire are prioritate față de Browser). |
| P27.8 | Motorul de context oprit | Debifează „Motorul de context” › Salvează; deschide notch-ul din VS Code. Apoi `WinNotch.exe --safe-mode`. | Nicio schimbare de pagină în ambele cazuri. |
| P27.9 | Fără consum | Comutator pornit, notch închis, 10 minute; Task Manager › WinNotch.exe. | CPU ca înainte (aproape 0%): funcția nu rulează nimic cât notch-ul e închis. |
| P27.10 | Acțiunea de setări | Cu Command Bar pornit: `Win+Alt+Space`, „setari pagina context”, Enter. | Fereastra WinNotch se deschide la Setări, la secțiunea „Pagina după context”. |
| P27.11 | Testul de fum | GitHub › Actions › ultima rulare CI › pașii „Smoke tests”. | Rularea cu activity-manager oprit are „PASS  Pagina după context…” (16 PASS; 17 din P20); cea cu el pornit scrie „SKIP  Pagina după context…” (tot 16 PASS; 17 din P20). |

### P20 — Quick Actions (experimental, oprit implicit)

Cu comutatorul oprit, notch-ul arată ca înainte (niciun rând de butoane, nicio sugestie). Verificările de mai jos sunt cu
„Quick Actions” pornit (Setări › Funcții noi › bifează › Salvează) și „Motorul de context” pornit (implicit), dacă nu scrie altfel.

| # | Verificare | Pași | Rezultat așteptat |
|---|---|---|---|
| P20.1 | Oprit = nimic | Comutatorul oprit (implicit). Într-un apel Teams / Zoom cu căștile puse, deschide notch-ul. | Notch-ul arată ca înainte, fără rând de butoane; în log nu apare „Quick Actions”. |
| P20.2 | Întâlnire cu căști | Comutatorul pornit. Într-un apel (microfonul pornit), cu căști (cu fir sau Bluetooth), hover pe notch. | Sub pagină, centrat, un rând cu „Mută / pornește microfonul” și „Volum 40%”, butoane mici rotunjite în culorile temei; pagina nu e acoperită. |
| P20.3 | Butoanele merg | În P20.2: click pe microfon, apoi pe „Volum 40%”. Click din nou pe microfon. | Microfonul se oprește în toate aplicațiile (și pornește la al doilea click); volumul devine 40%. La capătul rândului apare 4 s rezultatul („Microfon oprit (în toate aplicațiile)”, „Volum 40%”, „Microfon pornit”). Notch-ul rămâne deschis. În log „Acțiune audio.mute-mic (QuickAction): reușită”, fără valori. |
| P20.4 | Boxe = altă regulă | Același apel, cu boxele ca ieșire. | Nu apare rândul întâlnirii (eventual cel pentru muzică / stick, dacă e cazul). |
| P20.5 | Muzică | Pornește o piesă în Spotify (sau YouTube cu extensia), deschide notch-ul. | „Pauză” și „Următoarea”; merg pe sursa care cântă. Fără sursă media nu apare nimic. |
| P20.6 | Stick USB | Conectează un stick, deschide notch-ul. | Un buton „Deschide <nume> (E:)” care deschide stick-ul în Explorer. „Scoate” nu apare (cere confirmare: rămâne în pagina Dispozitive și în Command Bar). |
| P20.7 | Baterie sub 20% | Pe laptop, pe baterie, sub 20%: deschide notch-ul. | „Economisire” deschide pagina Windows de economisire a bateriei; „Luminozitate” deschide Setări › Ecran. Nicio sugestie nesolicitată (alerta de baterie rămâne). |
| P20.8 | Prioritatea | În apel cu căști și cu muzică pornită. | Apare doar rândul întâlnirii. |
| P20.9 | Modul de editare | Cu rândul vizibil, intră în editare (creionul), apoi „Gata”. | În editare rândul dispare și nota de jos a paginii standard apare normal; după „Gata” rândul revine. |
| P20.15 | Stick scos între timp | Cu un stick conectat, deschide notch-ul; scoate stick-ul fără „Scoate” și dă imediat click pe „Deschide …”. Apoi conectează un stick adormit / lent și deschide notch-ul. | Rezultatul „Unitatea nu mai e conectată.” apare în rând (portocaliu). Deschiderea notch-ului nu se blochează așteptând stick-ul. |
| P20.10 | Sugestie, doar cu Activity Manager | Pornește și „Manager de activități”. Intră într-un apel cu căștile puse (notch-ul închis). Ieși din apel și intră din nou în 2–3 minute. | La prima intrare, pastila se lărgește 2 s cu „Întâlnire cu căști · acțiuni rapide la hover”; la a doua, nimic (în log „sugestie amânată”). Cu „Manager de activități” oprit: nicio sugestie. |
| P20.11 | „Nu mai arăta” | Cu Activity Manager pornit, deschide notch-ul în apel cu căști; click pe „Nu mai arăta”. După 10 minute, intră iar într-un apel. | Butonul dispare (celelalte rămân); nu mai vine sugestia pentru întâlnire, dar butoanele apar la deschidere. Command Bar › „sugestii” › „Quick Actions: arată din nou sugestiile ascunse” le readuce. |
| P20.12 | Motorul de context oprit | Debifează „Motorul de context” › Salvează; în apel cu căști, deschide notch-ul. Apoi `WinNotch.exe --safe-mode`. | Niciun rând de butoane în ambele cazuri. |
| P20.13 | Fără consum | Comutator pornit, notch închis, 10 minute; Task Manager › WinNotch.exe. | CPU ca înainte (aproape 0%): nimic nu rulează periodic. |
| P20.14 | Testul de fum | GitHub › Actions › ultima rulare CI › pașii „Smoke tests”. | Ambele rulări au „PASS  Quick Actions: …” (17 PASS); cea cu activity-manager pornit spune și „o singură sugestie (peek) la 10 minute; „Nu mai arăta””. |

### P21 — Smart Clipboard (experimental, oprit implicit)

Cu comutatorul oprit, widget-ul Clipboard arată ca înainte (niciun chip). Verificările de mai jos sunt cu „Smart Clipboard”
pornit (Setări › Funcții noi › bifează › Salvează) și cu widget-ul Clipboard pe o pagină de-a ta (Editează › Adaugă widget ›
Clipboard), dacă nu scrie altfel.

| # | Verificare | Pași | Rezultat așteptat |
|---|---|---|---|
| P21.1 | Oprit = ca înainte | Comutatorul oprit (implicit). Copiază un JSON, un link și o culoare; deschide pagina cu widget-ul. | Widget-ul arată exact ca înainte: titlul, lista, fără rând de chip-uri și fără spațiu în plus. În log nu apare „Smart Clipboard”. |
| P21.2 | JSON | Comutatorul pornit. Copiază `{"nume":"Ion","oraș":"Brașov","note":[10,9.50]}` dintr-un editor; deschide pagina. Click pe „Formatează”, lipește în Notepad. | Sub titlu: „JSON”, „Formatează”, chip-uri mici rotunjite în culorile temei. În Notepad JSON-ul e indentat cu 2 spații, cu „oraș”, „Brașov” și „9.50” neschimbate. Lângă chip-uri apare 3 s „JSON formatat, în clipboard”; chip-ul devine „Compactează”. |
| P21.3 | Fără buclă | După P21.2, lasă notch-ul deschis 10 s; uită-te în istoricul widget-ului. | Clipboard-ul nu se mai schimbă singur; JSON-ul formatat nu apare ca intrare nouă în istoric; niciun peek. |
| P21.4 | Link cu urmărire | Copiază `https://exemplu.ro/pagina?utm_source=nl&id=7&fbclid=abc#sus`. Click pe „Curăță link-ul”, lipește. Apoi „Deschide”. | Rămâne `https://exemplu.ro/pagina?id=7#sus` (mesajul spune „2 parametri de urmărire scoși”). „Deschide” deschide link-ul în browserul implicit. Un link fără urmărire are doar „Deschide”. |
| P21.5 | JWT | Copiază un token JWT (de exemplu de pe jwt.io). Click pe „Decodează”, lipește. | Un JSON cu „header” și „payload” (de exemplu „alg”, „sub”, „name”); semnătura nu apare. Fără nicio conexiune la rețea (poți verifica deconectat). |
| P21.6 | Celelalte tipuri | Copiază pe rând: `#1A2B3C`, `Ion@Exemplu.RO`, `192.168.1.10`, `0721 123 456`, `C:\Windows\notepad.exe`. Click pe chip, lipește. | Culoarea are o mostră albastru-închis și dă `rgb(26, 43, 60)`; adresa `Ion@exemplu.ro`; IP-ul la fel; numărul `0721123456`; „Deschide folderul” deschide `C:\Windows` în Explorer (Notepad nu pornește). |
| P21.7 | Nu e recunoscut | Copiază pe rând: `2026-10-06`, `1.234,56`, `12345`, `{nu e json}`, `#hashtag`, `#123`, un text oarecare. | Niciun chip. |
| P21.8 | Parole ignorate | Din KeePass / Bitwarden / 1Password copiază o parolă (sau un text cu formatul „ExcludeClipboardContentFromMonitorProcessing”). | Parola nu apare în istoric; rândul de chip-uri dispare (nimic analizat). |
| P21.9 | Căi de rețea | Copiază `\\server\share\x` și apoi o cale de pe o unitate de rețea mapată (de exemplu `Z:\Proiecte`). Click pe „Deschide folderul” unde apare. | Calea `\\server…` nu are chip; pentru unitatea mapată: „Calea e pe o unitate de rețea sau lipsă; nu o deschid.”; Windows nu se conectează la server. |
| P21.10 | Peek la copiere | Setări › Smart Clipboard › bifează „Un mesaj scurt în pastilă…” › Salvează. Cu „Manager de activități” oprit, copiază un JSON compact; apoi pornește-l și copiază din nou. | Fără manager: nimic în pastilă. Cu el: pastila se lărgește 2 s cu „JSON copiat · Formatează”. Pentru un e-mail, un IP sau un JSON deja formatat: niciun peek. |
| P21.11 | Nimic în log | După P21.2–P21.10, deschide `%AppData%\WinNotch\log.txt`. | Doar „Smart Clipboard: pornit.” / „oprit (N recunoașteri).” și „Acțiune clipboard.… (UI): reușită”; niciun text copiat, niciun link, nicio adresă. |
| P21.12 | Command Bar | Cu „Command Bar” pornit și un JSON copiat: `Win+Alt+Space`, „formateaza json”, Enter. | JSON-ul formatat ajunge în clipboard. Fără JSON copiat, acțiunea nu apare în rezultate. |
| P21.13 | Fără consum | Comutator pornit, notch închis, 10 minute; Task Manager › WinNotch.exe. | CPU ca înainte (aproape 0%): nimic nu rulează periodic. |
| P21.14 | Testul de fum | GitHub › Actions › ultima rulare CI › pașii „Smoke tests”. | Rularea cu activity-manager oprit are „PASS  Smart Clipboard: …” (18 PASS); cea cu el pornit scrie „SKIP  Smart Clipboard…” (17 PASS). |

### P23 — Raft (experimental, oprit implicit)

Cu comutatorul oprit nimic nu se schimbă. Verificările de mai jos sunt cu „Raft” pornit (Setări › Funcții noi › bifează ›
Salvează), dacă nu scrie altfel. Pentru fișiere de test folosește un folder de pe discul tău (de exemplu `C:\Temp\raft`).

| # | Verificare | Pași | Rezultat așteptat |
|---|---|---|---|
| P23.1 | Oprit = ca înainte | Comutatorul oprit (implicit). Trage un fișier din Explorer peste pastilă și ține-l acolo 2 s; apoi deschide notch-ul cu `Win+Alt+N` și trage fișierul peste el. Fă și R1 și R13. | Pastila nu se deschide cât tragi (ca înainte); peste notch-ul deschis cursorul arată „interzis”; în antet nu e niciun buton „Raft”. Hover-ul și click-ul prin pastilă merg ca înainte. În log nu apare „Raft:”. |
| P23.2 | Tragi peste pastila închisă | Comutatorul pornit. Cu altă aplicație în față, trage un fișier din Explorer peste pastilă și ține-l nemișcat cât întârzierea de hover. Dă drumul pe notch. | Notch-ul se deschide singur, cu raftul arătat; cursorul arată „link”; după drop, fișierul apare în listă (iconiță după tip, nume, folderul dedesubt) și sus „Un element adăugat.”. Fișierul rămâne unde era (nu e mutat, nu e copiat). Aplicația din față nu pierde tastatura. |
| P23.3 | Click prin pastilă | Comutatorul pornit. Dă click pe pastila închisă (pe ce e sub ea), apoi apasă butonul pe pastilă și trage puțin. | Click-ul ajunge la fereastra de dedesubt și notch-ul nu se deschide, ca înainte (doar o tragere începută **în afara** pastilei o poate deschide). |
| P23.4 | Pe notch-ul deschis | Deschide notch-ul (hover sau `Win+Alt+N`) pe o pagină de-a ta cu widget-uri și pe Acasă; trage 3 fișiere și un folder din Explorer peste pagină și peste notița din Unelte. | Raftul apare singur cât tragi; după drop are cele 4 elemente; textul nu ajunge în notiță; pagina nu se rearanjează. În modul de editare (creionul) un fișier tras nu intră în raft. |
| P23.5 | Limita de 20 | Trage 25 de fișiere deodată (sau în mai multe drop-uri). Apoi trage din nou unul care e deja în raft. | Raftul are 20; mesajul spune „raftul e plin (maximum 20): 5 n-au mai încăput”; primele 20 rămân în ordinea venirii, niciunul nu e scos. Dublura: „unul era deja în raft”. |
| P23.6 | Căi de rețea refuzate | Din Explorer, trage un fișier dintr-un folder de rețea (`\\server\share`) și unul de pe o unitate mapată (`Z:`); pune pe desktop o scurtătură `.lnk` spre `\\server\share\x.txt` și una `.url` cu `IconFile=\\server\i.ico`, apoi trage-le. Urmărește traficul (de exemplu `netstat` sau Wireshark pe portul 445). | Niciunul nu intră („… sunt din rețea (refuzate)”, „… nu sunt fișiere locale valide”); WinNotch nu se conectează la server (nicio conexiune SMB nouă din WinNotch.exe). O scurtătură `.lnk` spre un fișier local intră. |
| P23.7 | Copiază calea | Pe un element, click pe „Copiază calea”; lipește în Notepad. | Exact calea fișierului (cu diacriticele și spațiile); sus „Calea, în clipboard”. Calea nu apare ca intrare nouă în istoricul widget-ului Clipboard. |
| P23.8 | Deschide folderul | „Deschide folderul” pe un fișier, apoi pe un folder. | Explorer deschide folderul în care e fișierul (fișierul nu pornește), respectiv folderul însuși. |
| P23.9 | Zip | „Arhivează” pe un fișier `raport.pdf` (cu un `raport.zip` deja în folder) și pe un folder cu subfoldere. | Apar `raport (2).zip` și `Folder.zip` lângă ele; `raport.zip` vechi e neatins; zip-urile se deschid în Explorer cu tot conținutul; ele intră și pe raft. Pe un folder mare (câțiva GB) notch-ul nu îngheață. |
| P23.10 | OCR | „Text din imagine” pe un PNG / JPG cu text (o captură). | „Citesc textul…”, apoi „Text copiat · N rânduri”; textul e în clipboard. Butonul nu apare pe un PDF sau un folder. |
| P23.11 | PNG ↔ JPG | „Fă o copie JPG” pe un PNG cu transparență; „Fă o copie PNG” pe un JPG. Repetă o dată. | Apar `x.jpg` (transparentul alb, nu negru) și `y.png` lângă originale; a doua oară `x (2).jpg`, `y (2).png`; originalele neatinse. Un fișier `.png` care de fapt nu e PNG: „Fișierul nu e un PNG valid.” |
| P23.12 | Trage din raft spre Explorer | Trage un element din raft într-un alt folder din Explorer, apoi într-un e-mail / chat. | Explorer face o copie (sau o scurtătură), originalul rămâne; aplicația primește fișierul. Elementul rămâne în raft. |
| P23.13 | Trage din raft înapoi pe notch | Trage un element din raft și dă-i drumul tot pe notch. | Nu apare de două ori; nu se întâmplă nimic. |
| P23.14 | Dispare discret | Pune un fișier pe raft, închide notch-ul, șterge fișierul din Explorer, deschide notch-ul și raftul. Apoi scoate un stick cu un fișier pe raft. | Elementul nu mai e în listă (fără mesaj de eroare). Cu stick-ul scos, notch-ul se deschide la fel de repede. |
| P23.15 | Golește, scoate, repornire | Pune 3 elemente; ieși din meniul iconiței și repornește WinNotch; deschide raftul. Apoi „Scoate” pe unul și „Golește”. | După repornire sunt tot 3, în aceeași ordine. „Scoate” și „Golește” scot doar referințele: fișierele rămân pe disc. |
| P23.16 | Nimic în log | După P23.2–P23.15, deschide `%AppData%\WinNotch\log.txt`. | Doar „Raft: pornit (N elemente).”, „Raft: adăugate N, refuzate M (din rețea K)…”, „Raft: N elemente care nu mai există, scoase.” și „Acțiune shelf.… (UI): reușită”; nicio cale, niciun nume de fișier. |
| P23.17 | Command Bar | Cu „Command Bar” pornit și 2 elemente în raft: `Win+Alt+Space`, „raft copiaza calea 2”, Enter. | Calea elementului 2 ajunge în clipboard. Cu raftul gol acțiunile nu apar. |
| P23.18 | Fără consum | Comutator pornit, notch închis, 10 minute; Task Manager › WinNotch.exe. | CPU ca înainte (aproape 0%): nimic nu rulează periodic. |
| P23.20 | Bifele și „Copiază tot” | Pune 4 fișiere pe raft (din Explorer, selectate toate odată și trase o dată). Deschide raftul fără să bifezi nimic: butonul din antet. Click pe el, apoi `Ctrl+V` într-un folder gol. | Butonul scrie „Copiază tot (4)”; după click, sub titlu „4 fișiere pe clipboard · dă Ctrl+V unde le vrei”. `Ctrl+V` copiază exact cele 4 fișiere, cu bara de progres a Windows-ului; originalele rămân unde erau. |
| P23.21 | „Copiază selecția” | Bifează 2 din cele 4 (click pe bifa de la începutul rândului), click pe buton, `Ctrl+V` în alt folder. Debifează una și uită-te la buton. | Bifele se colorează (accent); butonul scrie „Copiază selecția (2)” și copiază doar cele 2, în ordinea din raft. După debifare scrie „Copiază selecția (1)”, iar fără nicio bifă „Copiază tot (4)”. |
| P23.22 | Conflicte de nume = ca la copy-paste | Copiază selecția într-un folder în care există deja un fișier cu același nume. Alege pe rând „Înlocuiește”, „Sari peste”, „Păstrează ambele”. | Întrebarea e cea a Windows-ului (aceeași ca la un `Ctrl+C` / `Ctrl+V` din Explorer) și face exact ce alegi; WinNotch nu arată niciun dialog propriu și nu scrie nimic de la el. |
| P23.23 | Elemente care nu mai sunt | Șterge din Explorer unul dintre fișierele bifate, apoi click pe „Copiază selecția”. Apoi bifează un fișier de pe un stick, scoate stick-ul și copiază din nou. | Primul: se copiază restul, mesajul se termină cu „(unul sărit)”, iar rândul dispare din raft. Al doilea: fișierul de pe stick nu e copiat și **rămâne** în raft; dacă e singurul ales, mesajul spune „Unitatea elementelor nu e conectată acum; rămân în raft.” |
| P23.24 | Tragerea mai multor rânduri | Bifează 3 rânduri și trage unul dintre ele în Explorer. Apoi trage un rând nebifat. | Primul: toate cele 3 ajung în folder (copie, originalele rămân). Al doilea: pleacă doar rândul tras. |
| P23.25 | Clipboard-ul rezistă | Click pe „Copiază tot”, ieși din WinNotch (meniul iconiței › Ieșire), apoi `Ctrl+V` în Explorer. După `Ctrl+V`, uită-te în widget-ul Clipboard (dacă îl ai pe o pagină) și în `log.txt`. | Fișierele se copiază și cu WinNotch închis. În istoricul widget-ului Clipboard nu apare nimic nou (nu e text), iar în log doar „Acțiune shelf.copy-files (UI): reușită”, fără căi și fără nume. |
| P23.19 | Testul de fum | GitHub › Actions › ultima rulare CI › pașii „Smoke tests”. | Rularea cu activity-manager oprit are „PASS  Raft: …” (20 PASS din P30); cea cu el pornit scrie „SKIP  Raft…” (17 PASS). |

### P30 — Căști/boxe (experimental, oprit implicit)

Cu comutatorul oprit nimic nu se schimbă. Verificările de mai jos sunt cu „Căști/boxe” pornit (Setări › Funcții noi ›
bifează › Salvează), dacă nu scrie altfel. Ai nevoie de cel puțin două ieșiri (boxele / difuzoarele laptopului și niște
căști cu fir, USB sau Bluetooth). Atenție: fiecare click schimbă ieșirea implicită a Windows.

| # | Verificare | Pași | Rezultat așteptat |
|---|---|---|---|
| P30.1 | Oprit = ca înainte | Comutatorul oprit (implicit). Deschide notch-ul pe Acasă; `Win+Alt+Space` (cu Command Bar pornit) și scrie „căști”. | Lângă volum nu e niciun buton nou; Command Bar nu găsește „Ieșire audio: …”. În log nu apare „Ieșire audio:”. |
| P30.2 | Lista | Pornește comutatorul. Deschide notch-ul pe Acasă, click pe butonul cu căști de lângă numărul volumului. | O listă mică, rotunjită, cu culorile temei, peste pagină: ieșirile active (ca în Setări Windows › Sistem › Sunet › Ieșire), ordonate după nume, cea de acum cu bifă. Cu două dispozitive cu același nume: „Speakers” și „Speakers (2)”. |
| P30.3 | Căști ↔ boxe | În listă, click pe căști; pornește o melodie; apoi click pe boxe. | Sunetul trece pe căști, bifa se mută, sub titlu „Ieșire audio: …”; volumul din notch și alerta de volum arată volumul căștilor. Apoi totul trece înapoi pe boxe. În panoul Sunet al Windows (`mmsys.cpl`) dispozitivul ales e și „Implicit”, și „Dispozitiv de comunicare implicit”. |
| P30.4 | Deja implicită | Click pe ieșirea care are deja bifa. | „„…” e deja ieșirea audio.”; nimic nu se schimbă. |
| P30.5 | Bluetooth conectat / deconectat | Cu lista deschisă, conectează căștile Bluetooth; apoi deconectează-le (sau oprește-le). | În cel mult o secundă apar în listă (și „Ieșire audio: <căștile>” în Command Bar); la deconectare dispar; dacă erau implicite, Windows alege altă ieșire și bifa o urmează. Fără lag, fără erori în log. |
| P30.6 | Dispozitiv scos între listă și click | Deschide lista, scoate căștile USB, apoi click repede pe ele (înainte să dispară). | „Dispozitivul nu mai e conectat.”; funcția rămâne pornită. |
| P30.7 | Command Bar | Cu „Command Bar” pornit: `Win+Alt+Space`, „casti” (fără diacritice), Enter pe „Ieșire audio: <căștile>”; apoi „speakers”. | Ieșirea se schimbă ca din listă; cea de acum are „(implicită)” în titlu. |
| P30.8 | Eroare simulată | Simulează o eroare a interfeței nedocumentate: într-un build de test, fă ca `PolicyConfigSwitcher.SetDefault` să arunce (de exemplu `throw new System.Runtime.InteropServices.COMException()` la început), apoi click pe o ieșire cu notch-ul deschis; închide notch-ul. Repetă cu „Manager de activități” pornit. | În listă: „Ieșirea audio nu a putut fi schimbată. „Căști/boxe” s-a oprit…”, lista golită, butonul dispare; după închiderea notch-ului, aceeași alertă în pastilă (o singură dată; cu Activity Manager, ca activitate). În Setări › Funcții noi comutatorul e oprit, cu motivul „Windows a refuzat schimbarea ieșirii audio implicite (interfață nedocumentată).”. În log o singură „a fost oprită automat”, tipul excepției, fără mesajul ei. Pornit din nou, încearcă iar. |
| P30.9 | Pagină, editare, temă | Cu lista deschisă: schimbă pagina; deschide-o din nou și intră în modul de editare; schimbă tema din fereastra WinNotch. | Lista se închide la schimbarea paginii și la editare; după schimbarea temei butonul e tot lângă volum, cu culorile noi. |
| P30.10 | Nimic în log, fără consum | După P30.2–P30.7 deschide `%AppData%\WinNotch\log.txt`; apoi lasă notch-ul închis 10 minute și urmărește Task Manager. | Doar „Ieșire audio: pornit.”, „Ieșire audio: N ieșiri active.”, „Ieșire audio: lista deschisă (N).”, „Acțiune audio.output-… (UI): reușită”; niciun nume de dispozitiv. CPU aproape 0%: nimic nu rulează periodic. |
| P30.11 | Testul de fum | GitHub › Actions › ultima rulare CI › pașii „Smoke tests”. | Rularea cu activity-manager oprit are „PASS  Căști/boxe: …” (20 PASS); cea cu el pornit scrie „SKIP  Căști/boxe…” (17 PASS). |

### B1 — Notch-ul gol și pastila doar cu ora (0.6.18)

Plasa de siguranță („Plasa de siguranță a notch-ului”, Setări › Funcții noi) e pornită implicit. Pe tema luminoasă și pe cea
întunecată. După fiecare verificare, caută în `%AppData%\WinNotch\log.txt` rânduri „B1 recover” (nu ar trebui să apară).

| # | Verificare | Pași | Rezultat așteptat |
|---|---|---|---|
| B1.1 | Deschideri repetate | Cu toate funcțiile noi pornite (Manager de activități, Command Bar, Pagina după context, Quick Actions, Smart Clipboard, Raft, Căști/boxe): deschide notch-ul de 20 de ori, amestecat — hover, `Win+Alt+N`, peste o alertă de volum, după `Win+Alt+Space` + Esc, trăgând un fișier peste pastilă, pe fiecare pagină. | De fiecare dată: tab-urile, pagina și ora din antet se văd. Niciun „B1 recover” în log. |
| B1.2 | Pastila după închidere | Închide notch-ul repede (mouse-ul afară imediat după deschidere, sau `Win+Alt+N` de două ori rapid), de 10 ori; apoi schimbă volumul imediat după închidere. | Standby-ul (Muzică, Ora, Vremea) apare de fiecare dată; alerta de volum are bara și numărul (nu e goală). |
| B1.3 | Forma mică | Lasă mouse-ul departe de notch 10 s (Setări: forma mică după 10 s); apoi maximizează o fereastră pe același monitor. | După 10 s: pastila mică, sus, cu „ora · data” (ex. „14:05 · mar 6 oct”), niciodată doar ora. Peste fereastra maximizată: la fel. Hover peste ea (sau o alertă) → standby-ul complet (fără fereastra maximizată). |
| B1.4 | Vremea pe tema luminoasă | Tema luminoasă; standby cu Vremea; apoi deconectează internetul și repornește WinNotch. | Iconița vremii se vede (portocaliu-închis ziua cu soare, albastru noaptea / cu nori; gri fără date, lângă „—”). Pe tema întunecată culorile sunt ca înainte. |
| B1.5 | Plasa în acțiune | (Pentru dezvoltare) pornește `WinNotch.exe --smoke`, deschide notch-ul și scrie `smoke-empty-panel` în `%AppData%\WinNotch\smoke\smoke-commands.txt`; apoi, cu notch-ul închis, `smoke-empty-pill`. | În cel mult o secundă panoul (apoi pastila) apare din nou; în `%AppData%\WinNotch\smoke\log.txt` câte un rând „B1 recover”, urmat de „conținutul se vede”. |
| B1.6 | Ce scrii dacă se mai întâmplă | Notch gol sau pastila doar cu ora. | Copiază rândurile „B1 recover” și „Eroare neprevăzută” din jurul momentului (`log.txt`): ele spun ce strat lipsea, pagina, comutatoarele, activitatea și overlay-urile, fără titluri sau nume. |

### P02 — Teste de fum (CI)

Testele rulează singure în CI (pasul „Smoke tests”); verificările de mai jos sunt pentru rularea locală și pentru siguranța modului `--smoke`.

| # | Verificare | Pași | Rezultat așteptat |
|---|---|---|---|
| P02.1 | Rulare locală | Închide WinNotch. `dotnet publish WinNotch.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish`, apoi `dotnet run --project tests/WinNotch.Smoke/WinNotch.Smoke.csproj -- publish\WinNotch.exe`. Nu atinge mouse-ul și tastatura ~1 minut. | 20 de rânduri PASS și „TEST DE FUM (activity-manager oprit): 20 PASS, 0 FAIL” (19 până la P30, 18 până la P23, 17 până la P21, 16 până la P20, 15 până la P27); WinNotch se închide singur. Cu `-- publish\WinNotch.exe smoke-artifacts --activity-manager=on`: 17 PASS și patru rânduri SKIP (pagina după context, Smart Clipboard, raftul, Căști/boxe) (12 / 13 până la P14). În timpul rulării apare o fereastră mică a testului; nu da click în ea. |
| P02.2 | Setările tale nu sunt atinse | După P02.1, compară `%AppData%\WinNotch\settings.json` și `log.txt` cu cele dinainte. | Neschimbate; testul a scris doar în `%AppData%\WinNotch\smoke\`. |
| P02.3 | Fără `--smoke`, comenzile nu există | Pornește WinNotch normal; creează `%AppData%\WinNotch\smoke-commands.txt` cu `post-alert volume`. | Nu apare nicio alertă; fișierul rămâne neatins. |
| P02.4 | Eșecul lasă urme | (Pentru dezvoltare) rulează P02.1 cu WinNotch deja pornit. | Testul eșuează („WinNotch s-a închis (cod 3)”); în `smoke-artifacts\` sunt `ecran.png` și `log.txt`. |
| P02.5 | Release-ul rulează testele de fum (R1) | Pe GitHub › Actions, ultima rulare „Release” care a publicat o versiune. | Pașii „Smoke tests (activity-manager off)” și „(… on)” sunt verzi și sunt înaintea pasului „Sign”; 17 PASS în ambele (din P20; 16 din P27; 15 / 16 din P14; 12 / 13 în 0.6.14). |

### P51 — Închiderea panourilor

Comutatorul „Închiderea panourilor” (`overlay-dismiss`) e pornit implicit. Pentru raft, quick actions și ieșirea audio
pornește întâi comutatoarele lor din Setări → funcții noi.

| # | Verificare | Pași | Rezultat așteptat |
|---|---|---|---|
| P51.1 | Ieșire audio, click pe pagină | Deschide notch-ul, Acasă → butonul de lângă volum. Dă click pe pagina din notch, în afara listei. | Lista se închide. În `log.txt`: „Panou închis: ieșire-audio (click în afară).” |
| P51.2 | Click pe ecran, în afara notch-ului | Deschide din nou lista și dă click pe desktop. | Lista se închide (notch-ul se închide și el, ca până acum, când ieși cu mouse-ul). |
| P51.3 | Esc | Deschide lista de ieșiri audio și apasă `Esc`. | Lista se închide, notch-ul rămâne deschis. |
| P51.4 | Esc, de sus în jos | Cu Quick Actions pornit: deschide notch-ul (apar butoanele), apoi lista de ieșiri audio. Apasă `Esc` de două ori. | Prima apăsare închide lista, a doua butoanele; notch-ul rămâne deschis. |
| P51.5 | Un panou închide celălalt | Deschide raftul, apoi lista de ieșiri audio. | Raftul se închide singur când se deschide lista („alt panou” în log). |
| P51.6 | Indiciile nu se închid la un panou | Cu Quick Actions pornit, deschide lista de ieșiri audio. | Butoanele de sub conținut rămân; dispar la un click pe pagină sau la `Esc`. |
| P51.7 | Galeria și mărimile | Editare → „Adaugă widget”: click în afara galeriei. Apoi atinge un widget (mărimile) și dă click pe fundalul întunecat. | Ambele se închid; panoul notch-ului revine la înălțimea lui (fără spațiu gol rămas). |
| P51.8 | Mărimile, a doua oară | După P51.7, atinge din nou același widget. | Pop-up-ul se deschide normal (înainte rămâneau referințe moarte și a doua deschidere se purta ciudat). |
| P51.9 | Esc nu fură tasta | Închide toate panourile. Deschide Notepad, scrie ceva și apasă `Esc` de câteva ori. | `Esc` ajunge în Notepad, ca întotdeauna; WinNotch nu reacționează. |
| P51.10 | Editarea închide tot | Deschide lista de ieșiri audio, apoi intră în modul editare (butonul „Editează”). | Lista se închide la intrarea în editare. |
| P51.11 | Nota paginii standard | Editare pe o pagină standard (apare nota de jos), apasă `Esc`. | Nota dispare, editarea rămâne. |
| P51.12 | Fereastra WinNotch | Deschide fereastra WinNotch → o pagină → „Adaugă”, click pe un widget (apare pop-up-ul cu mărimi) și apasă `Esc`. | Pop-up-ul se închide; fereastra rămâne deschisă. |
| P51.14 | Esc cu notch-ul deschis prin scurtătură | Deschide notch-ul cu `Win + Alt + N` (rămâne deschis), cu Quick Actions pornit. Fără niciun panou deschis, apasă `Esc` într-o altă aplicație. | Butoanele Quick Actions rămân: `Esc` nu e citit cât e deschis doar un „indiciu”. Deschide apoi lista de ieșiri audio și apasă `Esc`: se închide doar lista. |
| P51.15 | Command Bar | Cu Command Bar pornit: deschide lista de ieșiri audio, apoi Command Bar-ul (`Win + Alt + Space`). Apasă `Esc` o dată. | Se închide doar Command Bar-ul; lista rămâne. A doua apăsare închide lista. |
| P51.13 | Comutatorul oprit | Setări → funcții noi → oprește „Închiderea panourilor”. Repetă P51.1 și P51.3. | Comportamentul de dinainte: lista rămâne deschisă la click pe pagină și la `Esc`; se închide doar cu butonul ei. |
