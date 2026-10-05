# WinNotch 0.5: audit de calitate și securitate

Data: 5 octombrie 2026. Versiune: 0.5.0.

## Cum s-a verificat

| Verificare | Rezultat |
|---|---|
| Compilare cu API-ul real WPF (.NET 8) și NAudio | 0 erori, 0 avertismente |
| Fișiere XAML bine formate (4) | toate valide |
| Revizie independentă a codului: bug-uri, fire de execuție, scurgeri, performanță | 11 probleme găsite, toate reparate |
| Revizie independentă de securitate | 9 probleme găsite, 7 reparate, 2 acceptate (motivele sunt la final) |
| Teste automate ale aplicației: serverul pentru extensie, filtre, calcule | 47 / 47 trec |
| Teste automate ale extensiei, rulate cu un Chrome simulat | 12 / 12 trec |
| Teste pe Windows | de făcut de tine: lista de mai jos, secțiunea 4 |

Ce nu se poate verifica aici: rularea efectivă pe Windows (fereastra, animațiile, audio, capturile, OCR). Pentru acestea e lista din secțiunea 4.

## 1. Probleme reparate: performanță și bug-uri

| # | Zonă | Problema | Efect | Rezolvare |
|---|---|---|---|---|
| Q1 | Fereastra notch | Desena la fiecare cadru (60+ pe secundă), chiar și închisă | Consum de procesor permanent | Desenarea pe cadru rulează doar cât notch-ul e deschis |
| Q2 | Animații | Animațiile infinite (egalizator, nivel surse, cerc de încărcare) continuau ascunse și se adunau | Procesorul creștea în timp, în câteva ore | Animațiile se opresc singure când nu se văd; straturile ascunse sunt scoase din desenare; alertele sunt eliberate după ce dispar |
| Q3 | Acasă | Sursele audio erau reconstruite la fiecare eveniment media, chiar și cu notch-ul închis | Animații noi la câteva secunde | Sursele se construiesc doar cât Acasă e deschis |
| Q4 | Media | Actualizările veneau din mai multe fire în același timp | Card cu date vechi; anunț „piesă nouă” dublat | Actualizările rulează una câte una |
| Q5 | Audio | Schimbarea ieșirii implicite (căști, Bluetooth) era ignorată | Volumul controla dispozitivul vechi; „nu se aude nimic” | Dispozitivul implicit e verificat la ~2–3 s și urmărit automat |
| Q6 | Surse audio | Scanarea pe aplicații rula de 2,5 ori pe secundă, permanent | Muncă inutilă toată ziua | Rulează doar cât notch-ul e deschis |
| Q7 | Dispozitive | Citirea monitoarelor și a stick-urilor USB pe firul interfeței | Interfața înghețată secunde întregi cu un stick adormit | Mutată în fundal |
| Q8 | Confidențialitate | Registrul (cine folosește microfonul) era citit de mai multe ori pe secundă | Muncă inutilă | Rezultat păstrat 2 s |
| Q9 | Internet | Un adaptor nou (VPN, Wi-Fi reconectat) adăuga tot traficul de la pornire | Vârfuri false de mii de Mb/s | Se calculează diferența pe fiecare adaptor |
| Q10 | Extensie | Un cadru (iframe) scos din pagină rămânea „în redare” | Tab afișat ca activ la nesfârșit | Cadrele tăcute peste 9 s sunt uitate; pagina anunță la închidere |
| Q11 | Mărunte | WinNotch apărea în „Consumă acum”; versurile eșuate erau ținute minte; administratorul era verificat la fiecare secundă; clipurile de mai mulți MB erau copiate la fiecare căutare; lista de pauze creștea la nesfârșit; închiderea senzorilor în timpul unei citiri; volumul trăgea zeci de animații pe secundă; istoricul de viteză cu intrări goale | Diverse | Toate reparate |

## 2. Probleme reparate: securitate

| # | Gravitate | Problema | Cine ar fi putut profita | Rezolvare |
|---|---|---|---|---|
| S1 | Mare | „Pornește cu Windows” ca administrator pornea exe-ul din Documente, pe care orice program îl poate înlocui | Un program rău-intenționat fără drepturi de admin ar fi primit drepturi de admin la logare | Pornirea automată rulează o copie din Program Files, unde doar administratorii pot scrie. După un build nou, copia se actualizează la prima pornire ca administrator; până atunci, o alertă îți spune că pornirea automată are versiunea veche |
| S2 | Medie | O pagină web putea da WinNotch-ului orice adresă de copertă, inclusiv din rețeaua ta locală | Orice site putea face WinNotch să trimită cereri către router sau alte dispozitive | Doar HTTPS și doar adrese publice, verificate la conectare; fără redirecționări; maxim 3 MB; 3 descărcări odată; adresele eșuate nu se reîncearcă |
| S3 | Medie | Orice extensie de browser se putea conecta la WinNotch | O extensie străină putea injecta tab-uri false sau primi comenzile | Extensia WinNotch are acum un ID fix și doar ea e acceptată |
| S4 | Medie | Istoricul de clipboard prelua și parolele copiate din managerele de parole | Parola ar fi apărut pe ecran și, dacă era fixată, în fișierul de setări | Textele marcate ca private (KeePass, Bitwarden, 1Password etc.) sunt ignorate, la fel ca în istoricul Windows |
| S5 | Medie | Pornit ca administrator, WinNotch pornea și aplicațiile din lansator sau din spațiile de lucru tot ca administrator | Un spațiu de lucru plantat în setări ar fi rulat cu drepturi de admin | Programele pornesc prin Explorer, cu drepturi normale. Fișierele redirecționate (junction/symlink) sunt refuzate cât rulează ca admin |
| S6 | Mică | Serverul local nu avea limite | Un proces local putea ocupa multă memorie | Maxim 4 conexiuni, mesaje de cel mult 256 KB, 60 de tab-uri, texte de cel mult 2048 caractere, deconectare după 90 s de tăcere, o singură conexiune per browser |
| S7 | Mică | Link-ul secret al calendarului, notița și clipurile fixate erau în text simplu | Alte conturi, backup-uri sau o copie a fișierului | Criptate pentru contul tău de Windows (DPAPI); setările vechi sunt convertite automat |
| S8 | Mică | Titlurile oricărui tab erau trimise la serviciul de versuri | Site-ul de versuri ar fi văzut ce urmărești | Pentru tab-uri, versurile se caută doar pe site-urile de muzică (YouTube Music, Spotify, SoundCloud, Deezer) |
| S9 | Mică | Extensia cerea și permisiunea „tabs”, care nu era necesară | — | Eliminată; extensia vede doar tab-urile care se aud sau au media |

Setările se salvează acum printr-un fișier temporar, așa că o închidere bruscă nu mai poate lăsa un `settings.json` stricat.

## 3. Teste automate (rulate, toate trec)

**Serverul pentru extensie (B1–B18)**

| ID | Test | Rezultat |
|---|---|---|
| B1 | Pagină web (origin https) refuzată | PASS |
| B2 | Altă extensie (alt ID) refuzată | PASS |
| B3 | Conexiune fără Origin (proces local) refuzată | PASS |
| B4 | Extensia WinNotch cu ID fix acceptată | PASS |
| B5 | Extensia în Edge (`extension://ID`) acceptată | PASS |
| B6 | Tab-urile trimise sunt primite | PASS |
| B7 | Maxim 60 de tab-uri dintr-un mesaj | PASS |
| B8 | Starea media din pagină citită (redare, titlu, durată) | PASS |
| B9 | Un tab pe pauză nu e „se aude” | PASS |
| B10 | Numele site-ului (YouTube) | PASS |
| B11 | Browserul conectat e raportat | PASS |
| B12 | Comanda „mute” ajunge la extensie | PASS |
| B13 | A doua conexiune pentru același browser o închide pe prima | PASS |
| B14 | Peste 4 conexiuni sunt refuzate | PASS |
| B15 | Mesajul de peste 256 KB închide conexiunea | PASS |
| B16 | JSON invalid ignorat, conexiunea continuă | PASS |
| B17 | Titlu lung tăiat la 2048 caractere | PASS |
| B18 | Nume de browser necunoscut ignorat | PASS |

**Nume de site-uri și titluri (S1–S6)**

| ID | Test | Rezultat |
|---|---|---|
| S1 | music.youtube.com → YouTube Music | PASS |
| S2 | youtube.com/shorts → YouTube Shorts | PASS |
| S3 | instagram.com/reels → Instagram Reels | PASS |
| S4 | Adresă invalidă → „Tab” | PASS |
| S5 | „(3) Titlu - YouTube” → „Titlu” | PASS |
| S6 | „Piesă - YouTube Music” → „Piesă” | PASS |

**Protecția la copertele din pagini (P1–P14)**

| ID | Test | Rezultat |
|---|---|---|
| P1 | 192.168.1.1 (router) blocat | PASS |
| P2 | 10.0.0.5 blocat | PASS |
| P3 | 172.16.0.1 și 172.31.255.255 blocate | PASS |
| P4 | 172.32.0.1 permis (e public) | PASS |
| P5 | 127.0.0.1 și ::1 blocate | PASS |
| P6 | 169.254.x (link-local) blocat | PASS |
| P7 | fd00::1 (IPv6 privat) blocat | PASS |
| P8 | ::ffff:192.168.0.1 (IPv4 ascuns în IPv6) blocat | PASS |
| P9 | 100.64.0.1 (rețeaua furnizorului) blocat | PASS |
| P10 | 8.8.8.8 și 142.250.0.1 permise | PASS |
| P11 | IPv6 public permis | PASS |
| P12 | Miniatura YouTube din link-ul „watch” | PASS |
| P13 | Miniatura din youtu.be și Shorts | PASS |
| P14 | Fără miniatură pentru alte site-uri | PASS |

**Verdictul testului de viteză (V1–V6)**

| ID | Test | Rezultat |
|---|---|---|
| V1 | Totul bine → „nu e nevoie de restart” | PASS |
| V2 | Router lent pe cablu → recomandă restart | PASS |
| V3 | Pierderi la router → recomandă restart | PASS |
| V4 | Router ok, internet cu pierderi → furnizorul | PASS |
| V5 | Viteză la jumătate față de cel mai bun test → avertisment | PASS |
| V6 | Test eșuat → explicație | PASS |

**Calculator din lansator (L1–L3)**

| ID | Test | Rezultat |
|---|---|---|
| L1 | `=250*1,19` → 297,5 | PASS |
| L2 | Text cu litere nu e evaluat (fără injecție) | PASS |
| L3 | Împărțirea la zero nu blochează aplicația | PASS |

**Extensia, cu un Chrome simulat (E1–E12)**

| ID | Test | Rezultat |
|---|---|---|
| E1 | Se conectează la 127.0.0.1:47811 și se prezintă | PASS |
| E2 | Trimite doar tab-urile care se aud, nu toate | PASS |
| E3 | Un tab fără sunet (ex. banca) nu e trimis deloc | PASS |
| E4 | Play raportat de pagină ajunge imediat | PASS |
| E5 | Pauza raportată de pagină ajunge imediat | PASS |
| E6 | Un cadru tăcut peste 9 s nu mai e „în redare” | PASS |
| E7 | Comanda mute ajunge la tab | PASS |
| E8 | Pauza rulează în pagina tab-ului | PASS |
| E9 | Volumul e limitat la 0–100% | PASS |
| E10 | După un mesaj invalid, comenzile merg mai departe | PASS |
| E11 | O comandă necunoscută (ex. „eval”) nu rulează nimic | PASS |
| E12 | Se reconectează singură după ~4 s, nu în buclă | PASS |

## 4. Teste pe Windows (de bifat de tine)

Fiecare rând are rezultatul așteptat. Dacă unul nu se potrivește, trimite-mi numărul lui și o captură.

**Instalare și pornire**
- [ ] W1. `build.bat` cu WinNotch pornit: îl închide singur, construiește și pornește versiunea nouă.
- [ ] W2. `build.bat` cu WinNotch pornit ca administrator: apare confirmarea Windows, după „Da” continuă.
- [ ] W3. A doua pornire a exe-ului nu deschide un al doilea notch.
- [ ] W4. Iconița de lângă ceas are meniu: Setări, Repornește ca administrator, Ieșire.

**Notch și standby**
- [ ] W5. Hover 0,4 s deschide notch-ul; ieșirea cu mouse-ul îl închide.
- [ ] W6. După 10 s fără activitate se face pastila mică (oră · dată).
- [ ] W7. Peste o fereastră maximizată, pe ambele monitoare, rămâne mic.
- [ ] W8. Joc sau video pe tot ecranul: notch-ul trece pe celălalt monitor sau se ascunde.
- [ ] W9. Pe 2K: notch-ul deschis e mărit automat; din Setări → Mărime se schimbă între 100% și 160%.
- [ ] W10. Textul se citește bine, fără margini încețoșate deranjante.
- [ ] W11. Scroll-ul din Clipboard și din lista de surse e o bară subțire închisă, fără săgeți gri.
- [ ] W12. Notch-ul închis nu ocupă procesorul: Task Manager arată ~0% pentru WinNotch.

**Acasă și audio**
- [ ] W13. Spotify pornit: cardul arată piesa, coperta, versurile și progresul.
- [ ] W14. Două tab-uri YouTube cu sunet: apar separat, cu titlul și miniatura fiecăruia.
- [ ] W15. Pauză din pagină: dispare din surse în sub o secundă.
- [ ] W16. Pauză din notch: rămâne în listă ca „pe pauză”, iar Play îl repornește.
- [ ] W17. Sursele nu își schimbă ordinea cât se aud.
- [ ] W18. Cardul mare nu sare între tab-uri cât cel afișat încă se aude.
- [ ] W19. Butonul mute pe un tab oprește doar acel tab.
- [ ] W20. Volum pe tab: se schimbă doar acel tab.
- [ ] W21. Anterior/următor merg pe YouTube Music.
- [ ] W22. Pui căștile sau Bluetooth-ul: volumul din notch controlează noul dispozitiv în câteva secunde.
- [ ] W23. WinNotch nu apare ca sursă audio.

**Extensia**
- [ ] W24. După actualizare: Remove pe extensia veche, apoi Load unpacked din folderul arătat în Setări.
- [ ] W25. Setări → „Tab-uri din browser” arată „✓ Conectată: Chrome”.
- [ ] W26. Cu WinNotch închis, lista de erori a extensiei nu se umple (o încercare pe minut, cel mult).

**Sistem**
- [ ] W27. Test viteză: ecranul rămâne deschis, arată viteza în timp real, apoi „Acum” față de „Ultimul”, ping router și verdict.
- [ ] W28. Pornești și oprești VPN-ul sau Wi-Fi-ul: nu apar vârfuri de mii de Mb/s.
- [ ] W29. Ca administrator, cu PawnIO instalat: apare temperatura procesorului.
- [ ] W30. „Consumă acum” nu include WinNotch.

**Dispozitive**
- [ ] W31. Microfonul pornit (ex. Discord): apare cine îl folosește și de cât timp.
- [ ] W32. Un stick USB adormit nu blochează notch-ul.
- [ ] W33. „Scoate” pe un stick îl scoate în siguranță.

**Unelte**
- [ ] W34. Captură ecran: notch-ul dispare, apoi apare preview-ul cu bliț; fișierul e în Imagini › Screenshots; Ctrl+V lipește imaginea.
- [ ] W35. Captură zonă (`Win+Alt+S`): ecranul se întunecă, apare dimensiunea în pixeli, Esc anulează.
- [ ] W36. Captură zonă pe al doilea monitor (cu altă scalare): zona salvată e exact cea aleasă.
- [ ] W37. Text din ecran (`Win+Alt+T`): apare „Citesc textul…”, apoi „Text copiat” cu începutul textului; diacriticele sunt corecte cu limba română instalată.
- [ ] W38. Eliberează RAM: bara de progres avansează, apoi „X% → Y% folosit”.
- [ ] W39. Lansator: `calc` deschide Calculatorul; `=2+2` arată 4.
- [ ] W40. Ca administrator: o aplicație deschisă din lansator nu rulează ca administrator (Task Manager → Detalii → coloana „Elevated”: „No”).
- [ ] W41. Spații de lucru: salvezi, închizi aplicațiile, un click le redeschide în aceleași poziții.
- [ ] W42. Fereastra activă: Deasupra / Monitor 2 / Jumătate / Mini.

**Clipboard, notiță, setări**
- [ ] W43. Copiezi text: apare în istoric; maxim 20, iar cele fixate rămân.
- [ ] W44. Copiezi o parolă din Bitwarden, KeePass sau 1Password: NU apare în istoric.
- [ ] W45. Notița se păstrează după `build.bat` (s-a salvat singură).
- [ ] W46. În `%AppData%\WinNotch\settings.json`, calendarul, notița și clipurile fixate apar ca `dpapi:…`, nu în clar.
- [ ] W47. „Pornește cu Windows” bifat ca administrator: există `C:\Program Files\WinNotch\WinNotch.exe`, iar după repornirea PC-ului notch-ul pornește singur, cu temperaturi.
- [ ] W48. Debifat fără drepturi de admin: apare mesajul care explică de ce trebuie admin.
- [ ] W48b. După un build nou, pornit normal: apare alerta „Pornirea cu Windows are versiunea veche”; după „Repornește ca administrator” nu mai apare.

**Alerte și pauze**
- [ ] W49. Schimbi volumul din tastatură: alerta de volum se actualizează lin, fără să tremure.
- [ ] W50. Pauza pentru ochi apare la 20 de minute; „Sari” o închide.

## 5. Riscuri acceptate și limitări

- **Legătura cu extensia prin rețeaua locală (127.0.0.1).** Varianta mai strictă e „native messaging”, unde Chrome pornește el aplicația și ambele părți se verifică reciproc. Ea cere o cheie în registru, iar WinNotch ar trebui pornit de browser. Riscul rămas: dacă WinNotch e închis, un alt program de pe calculator ar putea ocupa portul și ar primi titlurile tab-urilor care se aud. N-ar putea rula cod în browser: extensia execută doar comenzile ei fixe (pauză, volum etc.). Pentru un calculator personal, riscul e mic.
- **Rularea ca administrator, pentru temperatura procesorului.** Am redus riscul cât s-a putut: aplicațiile deschise din WinNotch pornesc fără drepturi de admin, iar fișierele redirecționate sunt refuzate. Soluția completă ar fi o mică aplicație separată, doar pentru senzori, care să ruleze ca admin.
- **Recunoașterea textului** depinde de limbile instalate în Windows; pentru diacritice trebuie limba română cu „Recunoaștere optică”.
- **Notificările Windows** (WhatsApp, Outlook) cer împachetarea aplicației ca MSIX și nu sunt incluse.
