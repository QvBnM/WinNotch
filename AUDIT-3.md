# WinNotch 0.6.4 — Raport de securitate ofensivă (audit 3)

Data: 5 octombrie 2026 · Versiune analizată: **0.6.4** · Tip: analiză de cod sursă cu mentalitate de atacator (threat modeling + attack surface), nu test dinamic pe Windows.

## Stare: toate cele 15 constatări sunt remediate în 0.6.5

| # | Remediere în 0.6.5 |
|---|---|
| 1, 2, 5 | **Arhitectură nouă:** WinNotch nu mai rulează ca administrator. Temperatura procesorului vine dintr-un serviciu SYSTEM (sarcină la boot, din `Program Files\WinNotch`, fără fereastră, fără intrări din profilul utilizatorului, răspunde doar pe un pipe read-only). Mediul SYSTEM nu e controlabil de utilizator; DLL-urile native se despachetează în `Program Files\WinNotch\runtime`. Copia se face dintr-un handle ținut deschis de la pornire (exe-ul nu poate fi redenumit/înlocuit). Vechea sarcină de logare elevată e ștearsă. `StartupHookSupport=false`. |
| 3 | Toate lansările trec prin `Shell.Open` (Explorer, drepturi normale); ca admin, butoanele de extensii copiază adresa în loc să pornească browserul; Explorer pornit cu cale completă. |
| 4 | `schtasks.exe` și `cmd.exe` din System32, cu cale completă. |
| 6 | Durata/poziția limitate la 0…7 zile (extensie + aplicație), un tab greșit nu mai blochează lista (try/catch per tab). Test B28. |
| 7 | Doar PNG/JPEG/WebP (după octeți), ≤ 300 KB, ≤ 4096 px; ca admin copertele din pagini nu se decodează deloc. Teste B29–B31. |
| 8 | Copertele le descarcă extensia în browser (proxy/VPN respectat) și trimite octeții; WinNotch nu mai face cereri spre adrese alese de pagini. |
| 9 | Autentificare reciprocă challenge–response HMAC-SHA-256; token-ul nu mai circulă; extensia nu trimite/execută nimic înainte ca serverul să se dovedească; `ExclusiveAddressUse`. Teste B23, B24, E13, E15, E16. |
| 10 | Conexiunile neautentificate sunt închise după 4 s; limită de 6 conexiuni. Test B25. |
| 11 | Ca admin nu se scrie nimic în folderul extensiei; altfel scrierea se face cu folderul blocat și verificat. |
| 12 | Ca admin, captura se verifică pe calea reală (`GetFinalPathNameByHandle`) să fie în profil. |
| 13 | Calendar: 4 MB, 5.000 de evenimente, buget de 500.000 de pași, redirecționări doar spre adrese publice (sau gazda scrisă de tine), o reîmprospătare odată. Test C11. |
| 14 | Căile UNC refuzate în Scurtături și fără cerere de iconiță. |
| 15 | Titluri ≤ 300 de caractere, intervale IP noi în `NetSafety`, limită de 1 MB și `using` la versuri/vreme. |

**Verificare independentă a remedierii:** o a doua revizie a codului nou a găsit 10 probleme (un client care ține pipe-ul blocat, reîncercarea la copierea exe-ului, calendarul în spatele unui proxy, căi de rețea scrise cu `\/`, mutex-ul serviciului, butonul din Setări apăsat de două ori, reîncercarea copertelor în extensie, un eveniment de calendar invalid care golea tot calendarul ș.a.) — toate reparate. Teste: 79 (aplicație) + 23 (extensie), toate trec.

**Risc rezidual:** instalarea serviciului rulează o singură dată elevat, din exe-ul din folderul tău, după confirmarea UAC (ca orice installer); exe-ul nu e semnat digital, deci UAC arată „editor necunoscut”. Golirea cache-ului Windows la „Eliberează RAM” nu mai are loc (necesita admin).

## Rezumat

Am analizat codul real (nu doar documentația) pe cele 5 direcții cerute, cu două revizii independente în paralel, apoi am verificat manual fiecare constatare în cod.

**Concluzia principală:** în modul normal (fără administrator) aplicația e solidă: WebSocket-ul nu poate fi atacat din pagini web (Origin exact + token de 256 biți), descărcările nu pot fi folosite pentru SSRF spre rețeaua locală, calculatorul nu permite injecții. **Riscul real e concentrat în modul „pornire cu Windows ca administrator”**: un program malițios care rulează deja cu drepturi normale pe contul tău poate transforma acel mod într-o escaladare la administrator, persistentă la fiecare logare. Pe acela îl recomand reproiectat.

| # | Problemă | Severitate | Doar ca admin? |
|---|---|---|---|
| 1 | Copia „protejată” din Program Files e luată dintr-un folder pe care îl poate modifica orice program al tău | **Ridicată** | da |
| 2 | Sarcina programată elevată moștenește variabilele de mediu ale utilizatorului (cod injectat în .NET la pornire) | **Ridicată** | da |
| 3 | Câteva lansări din procesul elevat ocolesc `Shell.Open` și folosesc asocieri de fișiere controlate de utilizator | **Ridicată** | da |
| 4 | `schtasks.exe` pornit fără cale absolută (căutat întâi în folderul aplicației) | Medie | da |
| 5 | DLL-urile native extrase din exe-ul single-file într-un folder temporar modificabil | Medie (de confirmat pe Windows) | da |
| 6 | O pagină web poate „îngheța” cardul media (overflow la durată) | Medie | nu |
| 7 | Imagini controlate de pagini web decodate în procesul elevat | Medie | da |
| 8 | Descărcarea copertelor ocolește proxy-ul / VPN-ul browserului (scurgere de IP, beacon) | Scăzută–Medie | nu |
| 9 | Port squatting pe 127.0.0.1:47811: extensia trimite token-ul oricui ascultă | Scăzută | nu |
| 10 | Un proces local poate ocupa toate conexiunile WebSocket (DoS) | Scăzută | nu |
| 11 | Scrierea fișierelor extensiei: verificare de junction fără blocarea folderului (TOCTOU) | Scăzută | da |
| 12 | Capturile de ecran se salvează fără verificare de junction | Scăzută | da |
| 13 | Calendarul iCal: consum CPU nelimitat și redirecționări spre rețeaua locală | Scăzută | nu |
| 14 | Căi UNC în „Scurtături” → conexiune SMB automată (scurgere NTLM) | Scăzută | nu |
| 15 | Observații informative (titluri false, intervale IP, limite de răspuns, DPAPI, log) | Info | — |

**Model de amenințare folosit**
- **A1 — pagină web malițioasă** vizitată în browser (fără nicio altă poziție). Cel mai probabil atacator.
- **A2 — program malițios cu drepturi normale** pe contul tău (de ex. un installer dubios). Nu poate deveni administrator singur; ne interesează dacă WinNotch îl ajută.
- **A3 — alt cont Windows** pe același PC sau un proces dintr-un sandbox.
- **A4 — server extern** pe care îl folosești (calendarul iCal, site-uri de coperte).
- În afara modelului: un atacator care e deja administrator (poate face oricum orice).

---

## Constatări detaliate

### 1. Copia „protejată” din Program Files provine dintr-o sursă controlabilă de atacator

- **Vector:** înlocuirea exe-ului sursă înainte de copierea elevată (binary planting / TOCTOU).
- **Severitate:** Ridicată.
- **Cod:** `AppSettings.cs` — `RefreshProtectedCopy()` (rândurile ~198–212) și setter-ul `StartWithWindows` (~257–271): `File.Copy(Environment.ProcessPath, ProtectedExe, true)`.
- **Cum se exploatează (A2):** WinNotch rulează de obicei din `publish\` din folderul tău, unde orice program al tău poate scrie. Windows permite **redenumirea** unui exe care rulează. Programul malițios redenumește `WinNotch.exe`-ul real și pune în locul lui un exe propriu cu același nume. Când tu (sau o alertă care te îndeamnă) apeși „Actualizează copia de pornire” ori bifezi „Pornire cu Windows” cât rulezi ca admin, procesul elevat copiază fișierul de pe disc — adică pe cel al atacatorului — în `C:\Program Files\WinNotch\`. `ProtectedCopyOutdated` compară doar mărimea și data, deci atacatorul poate face să apară butonul de actualizare oricând.
- **Impact:** cod al atacatorului care rulează **ca administrator la fiecare logare**, fără prompt UAC (persistență + escaladare completă).
- **Remediere:**
  1. Nu mai copia din folderul utilizatorului. Varianta corectă: un mic installer separat, pornit elevat, care instalează exe-ul în Program Files.
  2. Minimal: deschide sursa cu `FileShare.Read` (blochează scrierea și ștergerea), calculează SHA-256 pe **același handle** și compară cu un hash așteptat (de ex. hash-ul propriului proces, citit din `Process.MainModule` deja încărcat în memorie, nu de pe disc), apoi copiază din acel handle.
  3. Și mai bine: semnează exe-ul (Authenticode) și verifică semnătura înainte de copiere.

### 2. Sarcina elevată de la logare moștenește mediul utilizatorului

- **Vector:** injecție de cod prin variabile de mediu ale runtime-ului .NET într-un proces elevat.
- **Severitate:** Ridicată.
- **Cod:** `AppSettings.cs` ~272: `schtasks /Create … /SC ONLOGON /RL HIGHEST`.
- **Cum se exploatează (A2):** sarcina rulează sub contul tău, deci primește variabilele de mediu din `HKCU\Environment`, pe care orice program cu drepturi normale le poate scrie. Runtime-ul .NET citește la pornire variabile care îi spun să încarce cod suplimentar (startup hooks, profilere CLR, director de extragere pentru single-file). La următoarea logare, procesul **elevat** încarcă acel cod. Copia „protejată” din Program Files nu ajută: binarul e intact, dar runtime-ul e deturnat.
- **Impact:** cod arbitrar ca administrator la fiecare logare, fără UAC. Același efect ca la #1, fără nicio interacțiune din partea ta.
- **Remediere:**
  1. În `WinNotch.csproj`: `<StartupHookSupport>false</StartupHookSupport>` (dezactivează startup hooks la nivel de build).
  2. Recomandarea de fond: **renunță la procesul UI elevat**. WinNotch rulează normal; singura parte care are nevoie de admin (citirea temperaturilor prin LibreHardwareMonitor/PawnIO) se mută într-un serviciu Windows minimal (LocalSystem, binar în Program Files) care doar **trimite** valori (named pipe cu ACL, read-only). Astfel nu mai există niciun proces elevat care să citească intrări controlate de utilizator.

### 3. Lansări din procesul elevat care ocolesc `Shell.Open`

- **Vector:** deturnarea asocierilor de fișiere / App Paths per-utilizator (HKCU) → execuție elevată.
- **Severitate:** Ridicată (când WinNotch rulează ca admin, ceea ce e configurația pentru temperaturi).
- **Cod:**
  - `NotchWindow.xaml.cs:931` — „Deschide” pe captura de ecran: `Process.Start(path) { UseShellExecute = true }`;
  - `Services/ScreenTools.cs:103` — `explorer.exe /select,…` pornit după nume, cu ShellExecute;
  - `SettingsWindow.xaml.cs:161` — `chrome.exe` / `msedge.exe` / `explorer.exe` după nume;
  - `TrayIcon.cs:24` — deschiderea folderului de setări.
- **Cum se exploatează (A2):** ShellExecute rezolvă asocierile și App Paths din `HKCU\Software\Classes` și `HKCU\…\App Paths`, scriabile fără admin, și le aplică și proceselor elevate. Atacatorul setează, de exemplu, handler-ul pentru `.png` sau App Path-ul pentru `chrome.exe` spre programul său. Când apeși „Deschide” pe o captură sau butonul de extensii din Setări, programul lui pornește **elevat**. Bonus: un browser pornit așa rulează și el ca admin.
- **Impact:** escaladare la administrator la prima acțiune obișnuită a utilizatorului.
- **Remediere:** toate lansările trec prin `Shell.Open` (care, ca admin, deleagă la Explorer-ul neelevat), iar binarele de sistem se pornesc cu cale absolută (`%windir%\explorer.exe`). O regulă de cod: niciun `Process.Start` cu `UseShellExecute=true` în afara `Shell.cs`.

### 4. `schtasks.exe` pornit fără cale absolută

- **Vector:** DLL/EXE search-order hijacking.
- **Severitate:** Medie.
- **Cod:** `AppSettings.cs:286` — `new ProcessStartInfo("schtasks.exe", args) { UseShellExecute = false }`.
- **Cum se exploatează (A2):** cu `UseShellExecute=false`, `CreateProcess` caută un nume simplu întâi în **folderul aplicației**. Dacă WinNotch a fost pornit ca admin din `publish\` (folder al utilizatorului), un `schtasks.exe` plantat acolo rulează elevat. Getter-ul `StartWithWindows` apelează `Schtasks("/Query …")` de fiecare dată când se deschid Setările.
- **Impact:** execuție elevată fără nicio acțiune specială (doar deschiderea setărilor).
- **Remediere:** `Path.Combine(Environment.SystemDirectory, "schtasks.exe")`. Mai bine: API-ul Task Scheduler (COM) în loc de proces extern.

### 5. DLL-uri native extrase într-un folder modificabil

- **Vector:** DLL planting în directorul de extragere single-file.
- **Severitate:** Medie (mecanismul e documentat de .NET; comportamentul exact trebuie confirmat pe Windows).
- **Cod:** `build.bat` — `PublishSingleFile=true` + `IncludeNativeLibrariesForSelfExtract=true`.
- **Cum se exploatează (A2):** la pornire, exe-ul single-file extrage DLL-urile native WPF într-un director per-utilizator (implicit sub `%TEMP%`, sau unde indică variabila de mediu de extragere — vezi #2). Dacă fișierele există deja, sunt refolosite. Un atacator care pre-plantează un DLL acolo obține cod în procesul elevat, inclusiv în copia din Program Files.
- **Impact:** execuție elevată persistentă.
- **Remediere:** pentru copia din Program Files, publică fără auto-extragere (DLL-urile native lângă exe, în folderul protejat) sau fixează directorul de extragere într-o locație protejată. Se rezolvă natural cu arhitectura de la #2 (UI-ul nu mai rulează elevat).

### 6. O pagină web poate îngheța cardul media

- **Vector:** valoare numerică nelimitată din pagină → `OverflowException` repetat (DoS).
- **Severitate:** Medie.
- **Cod:** `extension/content.js` trimite `duration` din pagină; `Services/BrowserBridge.cs:257–258` (`Num()` filtrează doar NaN/∞); `Services/NowPlaying.cs:146–147` — `TimeSpan.FromSeconds(t.Duration)` aruncă peste ~9,2·10¹¹ s.
- **Cum se exploatează (A1):** o pagină pornește un `<video>` mut (permis fără click) și îi declară o durată uriașă (MediaSource sau un fișier cu header modificat). Funcționează și dintr-un iframe ascuns (de ex. reclamă). Fiecare reconstruire a listei de surse aruncă excepție.
- **Impact:** cardul de muzică și lista de surse (inclusiv Spotify etc.) rămân înghețate cât e deschis tab-ul; log-ul se umple. Fără execuție de cod.
- **Remediere:** limitează `duration`/`position` la 0…7 zile în `Num()`/`FromTab` și în `content.js`; `try/catch` per tab în `Build()`, ca un tab stricat să nu blocheze lista.

### 7. Imagini controlate de pagini, decodate în procesul elevat

- **Vector:** suprafață de atac a decodoarelor WIC într-un proces cu drepturi de admin.
- **Severitate:** Medie (cere și o vulnerabilitate într-un decodor de imagini; calea pagină → decodor elevat e confirmată în cod).
- **Cod:** `content.js` (artwork din `navigator.mediaSession`) → `NowPlaying.cs:151,180–181` → `MediaService.ImageFromStream` (`BitmapImage`, orice codec WIC instalat, inclusiv codecuri terțe pentru RAW/HEIF).
- **Cum se exploatează (A1):** pagina setează coperta la un URL HTTPS cu un fișier construit special (TIFF/ICO/HEIF/RAW). WinNotch îl descarcă și îl decodează. Un bug de memorie în decodor devine execuție de cod — ca administrator, dacă WinNotch rulează elevat. Separat, un PNG de 3 MB care declară 60000×60000 pixeli costă CPU/memorie.
- **Impact:** potențial RCE elevat de la o simplă pagină web; DoS de resurse.
- **Remediere:** acceptă doar PNG/JPEG/WebP după primii octeți; citește dimensiunile cu `BitmapDecoder` și refuză peste ~4096 px; când rulează elevat, nu descărca deloc coperte din pagini (sau decodează-le într-un proces neelevat).

### 8. Descărcarea copertelor ocolește proxy-ul / VPN-ul browserului

- **Vector:** scurgere de IP / tracking (web beacon).
- **Severitate:** Scăzută–Medie (confidențialitate).
- **Cod:** `NowPlaying.cs:31` (`UseProxy = false`), `NowPlaying.cs:173` (orice host HTTPS public).
- **Cum se exploatează (A1):** orice pagină care setează un URL de copertă primește o cerere **directă** de la PC-ul tău, cu IP-ul real, chiar dacă browserul folosește proxy sau o extensie VPN. Cu URL-uri unice per vizită, pagina știe și că ai WinNotch instalat.
- **Impact:** dezanonimizare pentru cine navighează prin proxy/VPN de browser; fingerprinting.
- **Remediere:** extensia descarcă imaginea în contextul de rețea al browserului și o trimite ca octeți (limitată) prin bridge; sau listă de host-uri permise pentru coperte (i.ytimg.com, lh3.googleusercontent.com, i.scdn.co…) și ignorarea celorlalte.

### 9. Port squatting pe 127.0.0.1:47811

- **Vector:** impersonarea serverului WinNotch față de extensie (lipsă de autentificare mutuală).
- **Severitate:** Scăzută.
- **Cod:** `extension/background.js:58–59` trimite `hello` cu token-ul imediat la `onopen`, apoi titlurile și URL-urile complete ale tab-urilor; comenzile primite sunt executate de la orice server. `BrowserBridge.cs:81–85` (fără `ExclusiveAddressUse`, fără reîncercare), token permanent.
- **Cum se exploatează (A2/A3):** cât WinNotch nu rulează, un proces local (chiar al **altui cont Windows**, loopback-ul e comun) ascultă pe 47811. Extensia se conectează și îi dă token-ul și URL-urile tab-urilor cu sunet (query string-urile pot conține date). Procesul poate apoi da comenzi tab-urilor (mute/pauză/focus) și, cu token-ul furat, se poate conecta la WinNotch-ul real ca să injecteze titluri/coperte false.
- **Impact:** scurgere de URL-uri, control limitat al tab-urilor, conținut fals în notch. Fără execuție de cod.
- **Remediere:** challenge–response: serverul trimite un nonce; extensia răspunde HMAC(token, nonce‖"client"); serverul se dovedește cu HMAC(token, nonce‖"server"); extensia nu trimite nimic și nu execută nimic înainte. `ExclusiveAddressUse = true`, reîncercare periodică a `Start()`, rotirea token-ului la fiecare pornire.

### 10. DoS al bridge-ului prin ocuparea conexiunilor

- **Vector:** epuizarea sloturilor de conexiune (slowloris).
- **Severitate:** Scăzută.
- **Cod:** `BrowserBridge.cs:138` (limita globală de 4 include conexiunile neautentificate), `:156` (Origin-ul se falsifică ușor de un client non-browser), `:174–187,233` (nu există termen limită pentru `hello`; un fragment de 1 octet la 80 s ține conexiunea vie).
- **Cum se exploatează (A2/A3):** un proces local deschide 4 conexiuni cu Origin fals și le ține deschise.
- **Impact:** extensia reală e refuzată; tab-urile nu mai apar în notch.
- **Remediere:** `hello` valid în maximum ~3 s de la upgrade; durată de viață absolută pentru conexiunile neautentificate; la limită, eliberează cea mai veche conexiune neautentificată.

### 11. Scrierea fișierelor extensiei: TOCTOU cu junction

- **Vector:** redirecționarea scrierilor unui proces elevat prin junction/symlink.
- **Severitate:** Scăzută (nume și conținut fixe).
- **Cod:** `BrowserBridge.cs:373–396` (`WriteExtension`): `token.json` e scris înainte de verificarea folderului `extension`; verificarea `SafeToWrite` se face fără `HoldFolder()`, deci folderul poate fi înlocuit între verificare și scriere.
- **Cum se exploatează (A2):** înlocuiește `%AppData%\WinNotch\extension` cu un junction spre un folder de sistem; procesul elevat scrie acolo `token.json`, `manifest.json`, `background.js`…
- **Impact:** fișiere cu conținut fix create/suprascrise în foldere protejate (potențial DoS al altor aplicații; nu execuție directă).
- **Remediere:** `HoldFolder()` pe toată durata, deschiderea folderului cu `FILE_FLAG_OPEN_REPARSE_POINT` și refuz dacă e reparse point; sau nu rescrie extensia când rulezi elevat.

### 12. Capturile de ecran se salvează fără verificare de junction

- **Vector:** același tip ca #11.
- **Severitate:** Scăzută.
- **Cod:** `Services/ScreenTools.cs:88–92` (excepția a fost lăsată intenționat pentru OneDrive).
- **Impact:** când WinNotch e elevat, un junction în `Imagini\Screenshots` duce PNG-ul (nume fix, conținut = ecranul) în orice folder.
- **Remediere:** când rulezi elevat, verifică calea finală cu `GetFinalPathNameByHandle` după deschidere (OneDrive rămâne permis dacă ținta e tot în profilul tău) sau salvează prin partea neelevată.

### 13. Calendarul iCal: consum de CPU și redirecționări

- **Vector:** fișier ICS ostil (A4: server de calendar compromis sau link greșit).
- **Severitate:** Scăzută.
- **Cod:** `Services/CalendarService.cs` — acceptă până la 20 MB; `Candidates` parcurge până la 20.000 de apariții per eveniment de la DTSTART; clientul HTTP urmează redirecționări (inclusiv spre adrese HTTP din LAN).
- **Impact:** un calendar cu sute de mii de evenimente zilnice din 1900 ține un nucleu ocupat minute întregi, iar reîmprospătările la 15 minute se pot suprapune; GET „orb” spre rețeaua locală (răspunsul apare doar ca titluri de evenimente, local).
- **Remediere:** maximum ~5.000 de evenimente și un buget total de iterații; saltul aritmetic al recurenței până în fereastra de interes; `MaxResponseContentBufferSize` ~2 MB; `AllowAutoRedirect = false` sau aceeași verificare de IP public ca la coperte; fără reîmprospătare paralelă.

### 14. Căi UNC în widget-ul „Scurtături”

- **Vector:** autentificare SMB automată (scurgere de hash NTLM).
- **Severitate:** Scăzută (cere modificarea `settings.json`, adică A2, sau ca tu să lipești un astfel de link).
- **Cod:** `Widgets/CustomWidgets.cs:75` (UNC trece ca `file:`), `Widgets/Widget.cs:93` (`SHGetFileInfo` pentru iconiță la desenare).
- **Impact:** la afișarea widget-ului, Windows se conectează la `\\server\share` și trimite hash-ul NTLM al contului, fără click.
- **Remediere:** refuză `Uri.IsUnc`; nu cere iconițe pentru căi de rețea.

### 15. Observații informative

- **Titluri false în notch:** titlul/artistul vin din orice frame (inclusiv iframe-uri de reclame), până la 2048 de caractere — se poate afișa text înșelător. WPF nu interpretează markup, deci nu e injecție. Recomandare: metadate doar din frame-ul principal, limită ~200 de caractere.
- **`NetSafety.IsPrivate`:** lipsesc câteva intervale rare (IPv4-compatible `::a.b.c.d`, 198.18.0.0/15, 192.0.0.0/24, prefixe NAT64 locale). Apărare în profunzime.
- **Versuri și vreme:** fără limită de mărime a răspunsului și `HttpResponseMessage` neeliberat (host-uri fixe, TLS). Recomandare: `MaxResponseContentBufferSize` și `using`.
- **DPAPI:** protejează datele față de **alte conturi și de copierea discului**, nu față de programele care rulează pe contul tău (pot decripta la fel ca WinNotch) — limita normală a DPAPI la nivel de utilizator. Dacă DPAPI nu e disponibil, valoarea se salvează necriptată (`AppSettings.cs:318`, cu mesaj în log). Restul datelor (pagini, căi din spații de lucru, link-uri) sunt în clar, sensibilitate scăzută.
- **Log:** nu conține link-uri, texte din clipboard sau titluri; conține excepții complete (stack trace) și numele ferestrelor/proceselor care decid modul „ocupat” al monitoarelor. Acceptabil; se poate scurta la mesaj fără stack trace în build-ul final.
- **Permisiuni extensie:** `<all_urls>` + `scripting` + content scripts în toate frame-urile — largi, dar necesare; comenzile execută doar funcții fixe, rezultatele sunt ignorate.

---

## Ce am verificat și e în regulă

- **CSWSH și DNS rebinding:** serverul cere Origin-ul exact al extensiei (ID fix). O pagină web nu-și poate schimba Origin-ul, deci nu se poate conecta. Ascultă doar pe 127.0.0.1, iar extensia folosește adresa literală.
- **Token:** 256 de biți aleatori, comparat în timp constant; token greșit → conexiune închisă. Nu e expus paginilor (nu e în `web_accessible_resources`).
- **Parsarea WebSocket:** cadre mascate obligatoriu, mesaje ≤ 256 KB, ≤ 60 de tab-uri, texte ≤ 2048 caractere, antete ≤ 16 KB cu timeout de 5 s; excepțiile sunt prinse per mesaj.
- **Extensia:** fără `onMessageExternal`/`externally_connectable`; `__winnotchMS` e definit primul, ne-rescriibil; handler-ele paginii rulează doar în originea paginii.
- **Coperte (SSRF):** doar HTTPS, IP-ul verificat **la conectare** pe adresa efectivă (fără TOCTOU/rebinding), fără redirecționări, fără decompresie automată, maximum 3 MB, timeout 10 s, maximum 3 descărcări simultane; blochează IPv4-mapped, 6to4, Teredo, ULA, link-local, CGNAT.
- **Calculatorul din lansator:** `DataTable.Compute` primește doar cifre și operatori (regex strict) — fără injecție.
- **Lansări din lansator, spații de lucru, widget-ul Comandă:** ca admin trec prin Explorer-ul neelevat (`Shell.Open`), fără argumente; Scurtăturile acceptă doar http/https/file/ms-settings.
- **Datele din tab-uri** nu ajung niciodată la `Process.Start`/`Shell.Open`; interogarea de versuri e codificată cu `EscapeDataString`.
- **Salvări:** atomice (fișier temporar + mutare), cu verificare de reparse point și folderul blocat; log-ul se golește pe loc, nu se șterge.
- **Clipboard:** conținutul marcat de managerele de parole e ignorat.
- **Memorie:** `EmptyWorkingSet` cu drepturi minime; privilegiul de golire a cache-ului e activat doar pentru acea operație.

## Plan de remediere recomandat

1. **Imediat (închide escaladările):** #3 și #4 (toate lansările prin `Shell.Open`, căi absolute), `StartupHookSupport=false` (#2), verificarea de integritate a copiei (#1).
2. **Arhitectural (elimină clasa de probleme 1, 2, 5, 7, 11, 12):** UI-ul WinNotch nu mai rulează niciodată elevat; temperaturile vin dintr-un serviciu Windows minimal, read-only.
3. **Robustețe:** #6 (limitarea duratei), #10 (termen pentru `hello`), #13 (limite pentru calendar), #8 (coperte prin browser sau listă de host-uri).
4. **Defensiv:** #9 (autentificare mutuală), #14, observațiile de la #15.

## Limitări ale acestui audit

- Analiză statică a codului sursă; nu a fost rulat pe Windows și nu s-au făcut teste de exploatare.
- Neanalizate în detaliu: `DeviceService`, interiorul `RegionPicker`, `tools/get-sdk.ps1`, driverul PawnIO și biblioteca LibreHardwareMonitor (cod terț).
- Severitățile țin cont de condițiile necesare: problemele „Ridicate” cer un program malițios deja rulat pe contul tău **și** modul admin activat; fără modul admin, cea mai serioasă problemă e #6 (DoS).
