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
| P13.11 | Formele noi (teste de fum) | GitHub › Actions › ultima rulare CI › pașii „Smoke tests (activity-manager off/on)”; artefactul „smoke-artifacts”. | Ambii pași verzi: „TEST DE FUM (activity-manager oprit): 15 PASS” și „(… pornit): 16 PASS” (12 / 13 până la P14); în `activity-manager-on\log.txt` rândurile „activitate de test … Grouped/Shown” și „activity.dismiss-all → făcut”, fără erori. |

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

### P02 — Teste de fum (CI)

Testele rulează singure în CI (pasul „Smoke tests”); verificările de mai jos sunt pentru rularea locală și pentru siguranța modului `--smoke`.

| # | Verificare | Pași | Rezultat așteptat |
|---|---|---|---|
| P02.1 | Rulare locală | Închide WinNotch. `dotnet publish WinNotch.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish`, apoi `dotnet run --project tests/WinNotch.Smoke/WinNotch.Smoke.csproj -- publish\WinNotch.exe`. Nu atinge mouse-ul și tastatura ~1 minut. | 15 rânduri PASS și „TEST DE FUM (activity-manager oprit): 15 PASS, 0 FAIL”; WinNotch se închide singur. Cu `-- publish\WinNotch.exe smoke-artifacts --activity-manager=on`: 16 PASS (12 / 13 până la P14). În timpul rulării apare o fereastră mică a testului; nu da click în ea. |
| P02.2 | Setările tale nu sunt atinse | După P02.1, compară `%AppData%\WinNotch\settings.json` și `log.txt` cu cele dinainte. | Neschimbate; testul a scris doar în `%AppData%\WinNotch\smoke\`. |
| P02.3 | Fără `--smoke`, comenzile nu există | Pornește WinNotch normal; creează `%AppData%\WinNotch\smoke-commands.txt` cu `post-alert volume`. | Nu apare nicio alertă; fișierul rămâne neatins. |
| P02.4 | Eșecul lasă urme | (Pentru dezvoltare) rulează P02.1 cu WinNotch deja pornit. | Testul eșuează („WinNotch s-a închis (cod 3)”); în `smoke-artifacts\` sunt `ecran.png` și `log.txt`. |
| P02.5 | Release-ul rulează testele de fum (R1) | Pe GitHub › Actions, ultima rulare „Release” care a publicat o versiune. | Pașii „Smoke tests (activity-manager off)” și „(… on)” sunt verzi și sunt înaintea pasului „Sign”; 15, respectiv 16 PASS (din P14; 12 / 13 în 0.6.14). |
