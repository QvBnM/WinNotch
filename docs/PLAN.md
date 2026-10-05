# WinNotch — plan suplu (aprobat 5 oct 2026)

Aplicația e în primul rând pentru autor; poate mai târziu pentru alții. Țintă: puține funcții, folosite zilnic, stabile. Autorul nu testează manual: verificarea pe Windows o fac testele de fum din CI, iar autorul doar folosește aplicația. Lucrul se face în 6 rulări de pilot automat, cu audit după fiecare.

## Reguli
- Orice funcție nouă: comutator în FeatureCatalog, logică fără WPF cu teste, capabilități expuse ca acțiuni (ActionRegistry), context din ContextEngine, tot ce apare în pastilă doar prin ActivityManager (după P13).
- Fără polling sub 2 s în standby. Fără titluri de fereastră sau conținut de clipboard în log.
- Nimic elevat. Regulile de securitate din DOCUMENTATIE.md, secțiunea 14, rămân obligatorii.
- Fiecare sarcină extinde testele de fum din tests/WinNotch.Smoke.
- Versiuni: 0.6.x, o singură publicare pe rulare (nu pe sarcină). 0.7.0 (pornirea implicită a „activity-manager” și „command-bar”) e un pas separat, după ce autorul le folosește câteva zile.

## Modificare de plan (rularea 3, 5 oct 2026)
După P14 NU se publică 0.7.0 și NU se pornește nimic implicit. „command-bar” și „context-pages” sunt Experimental, oprite implicit. 0.7.0 (pornirea implicită a „activity-manager” și „command-bar”) devine un pas separat, după ce autorul le folosește câteva zile. Publicarea se face o dată pe rulare, nu pe sarcină (rularea 3 → 0.6.15 pentru P14 + P27).

## Rulări
1. Pasul 0 + P02 · 2. P13 · 3. P14 + P27 · 4. P20 + P21 · 5. P23 + P30 · 6. P44 + P46

## Sarcini
1. P02 Teste de fum Windows — proiect tests/WinNotch.Smoke (FlaUI.UIA3), rulat în ci.yml pe windows-latest după build. Argumentul --smoke: fără actualizări, fără serviciu de temperatură. Comenzi de test prin smoke-commands.txt (doar cu --smoke): post-alert, toggle feature. Verifică: procesul trăiește 30 s, pastila e vizibilă, Win+Alt+N deschide, fereastra WinNotch se deschide, „Ieșire” dă cod 0, nicio excepție în log. Eșec = CI roșu, cu log.txt și captură ca artefacte.
2. P13 Activity Manager — întâi teste de caracterizare pentru fiecare alertă actuală (durată, prioritate, blocare în fullscreen și cu notch-ul deschis, limitele de repetare: 15/5/15 min). Core/Activity: Activity (prioritate Critical/High/Normal/Low, durată, persistentă, coalesceKey), ActivityManager (Post/Update/Dismiss). Reguli: Critical întrerupe; aceeași cheie actualizează; peste 3 în 5 s → „N noutăți”; în fullscreen doar Critical/High; 2 persistente → split pill; peeking pentru Low (2 s, lărgire mică). Alertele vechi mutate pe rând, aspect neschimbat. Comutator „activity-manager”: oprit = codul vechi neatins. Acțiunea activity.dismiss-all. Test: 1000 de alerte în 10 s, fără blocare.
3. P14 Command Bar — scurtătura Win+Alt+Space (configurabilă; conflictul detectat și semnalat). Pastila devine câmp de căutare peste ActionRegistry.Search; Refresh() la deschidere; parametri în text („volum 30”); săgeți, Enter, Esc; focusul revine la fereastra anterioară. Fiecare setare din Setări devine o acțiune („settings.<nume>”) care deschide Setări la acea opțiune. Comutator „command-bar” (Experimental, oprit implicit). Nu urmează 0.7.0 imediat (vezi „Modificare de plan”).
4. P27 Pagina după context — în Setări: categorie de context (Dev, Browser, Meeting, Game, Media, Office) → pagină. La deschidere, notch-ul alege pagina după contextul curent; o alegere manuală e respectată 10 minute. Comutator „context-pages” (Experimental, oprit implicit).
5. P20 Quick Actions — 2–4 acțiuni sub pastilă, la hover, după context: Meeting + căști → mută mic / volum 40%; Media → pauză / următor; USB conectat → deschide / scoate; baterie sub 20% → economisire. Reguli într-un tabel ușor de extins. Nesolicitat: cel mult o sugestie la 10 minute, cu „Nu mai arăta”. Comutator „quick-actions”.
6. P21 Smart Clipboard — recunoaște URL (curățare de parametri de tracking), JSON (formatare/minify), culoare hex (mostră), email, IP, cale de fișier, JWT (decodare locală), număr de telefon. Acțiunile apar ca chip-uri în widget-ul Clipboard și, opțional, ca peek la copiere (implicit oprit). Parolele rămân ignorate, ca azi. Comutator „smart-clipboard”.
7. P23 Raft drag & drop — întâi un spike: poate fereastra click-through să primească un drop? Dacă nu: planul B, raftul primește fișiere doar când notch-ul e deschis. Raftul ține referințe (nu copii), maximum 20, până la golire. Acțiuni: copiază calea, deschide folderul, arhivează zip, OCR pe imagini, convertește PNG/JPG. Fără căi de rețea. Comutator „shelf”.
8. P30 Căști/boxe — lista ieșirilor audio la click pe volum și acțiuni audio.output.<dispozitiv> pentru setarea ieșirii implicite (IPolicyConfig, nedocumentat: try/catch, la prima eroare FeatureFlags.Disable cu mesaj). Fără rutare per aplicație. Comutator „audio-switch” (Experimental).
9. P44 Game Mode — după Context (Game + fullscreen): pastila ascunsă, doar alerte Critical/High, notificările adunate pentru după. La ieșirea din joc: rezumat (durată, temperatura maximă CPU/GPU din datele existente, RAM mediu). Fără FPS. Comutator „game-mode”.
10. P46 Activități live din browser — extensia existentă citește tab-urile deschise de pe Glovo, Bolt Food, Tazz (starea comenzii, minute până la livrare) și Flashscore, LiveScore (scor, minut, echipe) și le trimite ca activitate persistentă. Permisiuni de host doar pentru aceste site-uri; text limitat la 120 de caractere; nicio comandă spre pagină. Pagina schimbată → activitatea dispare discret. Noua versiune a extensiei → alerta existentă „apasă ↻”. Comutator „browser-live”.

După P46: oprire. Autorul folosește aplicația 2 săptămâni, apoi alegem din „Idei pentru mai târziu”.

## Idei pentru mai târziu (nu se fac acum)
Semnare SignPath, ghid la prima pornire, traducere în engleză (pentru când aplicația merge la alții); Notification Inbox (cere package identity); Workflows; Undo Center; desktopuri virtuale; Dev Mode; OBS / Recording HUD; API local / CLI; rutare audio per aplicație; control monitoare DDC/CI; Network Intelligence; FPS în Game Mode.

## Revizie (R1)
Revizor independent, nu a scris codul. Pe git diff main...ramura și pe fișierele atinse în întregime. Verifică: bug-uri și cazuri limită; fire de execuție (UI pe Dispatcher, async void, excepții); scurgeri de memorie (evenimente, hook-uri, timere, COM); standby (polling sub 2 s); securitate (secțiunea 14 din DOCUMENTATIE.md, nimic sensibil în log, intrări din extensie verificate); regulile din PLAN și CLAUDE.md; teste lipsă sau goale; cod WPF care nu compilează. Nu modifică nimic. Raport: verdict, teste, CI, apoi probleme (severitate · fișier:linie · reparație).
