# WinNotch 0.6.20

## Nou
- Setări › Funcții noi: „Notch lipit de ramă” — notch-ul atinge marginea de sus a ecranului, e rotunjit doar jos și are racordări în stânga și dreapta, ca o prelungire a ramei monitorului (oprit implicit).
- Setări › Funcții noi: „Fereastra WinNotch v2” — fereastra nouă, cu antetul în forma notch-ului, categorii în stânga, carduri pentru acțiunile care există și o coloană cu clipboard-ul fixat, microfonul și camera (oprit implicit; paginile, temele și setările se deschid în fereastra clasică).

## Reparat
- Peste un joc sau un film pe tot ecranul notch-ul dispare complet. Înainte rămânea cocoțat acolo, micșorat: un browser care intră în fullscreen păstrează starea de „maximizat”, iar WinNotch o citea pe ea. Cât e ascuns, nu se mai deschide la mișcarea mouse-ului și nici când tragi fișiere.
- Panourile se închid la fel: „Ieșire audio”, raftul, galeria de widget-uri și mărimile unui widget se închid acum și la un click în afara lor și la `Esc`, nu doar cu butonul lor. `Esc` închide doar panoul de deasupra și, fără niciun panou deschis, WinNotch nu atinge tastatura.
- O alertă nu mai stă în calea ta: dacă începi să tragi fișiere peste notch, apeși `Win + Alt + N` sau deschizi Command Bar-ul, alerta se dă la o parte imediat și acțiunea se execută. Pauza pentru ochi nu mai blochează raftul.
- Dacă WinNotch se închide singur, afli de ce: fiecare ieșire scrie motivul în log, iar dacă s-a închis neexplicat de două ori la rând, notch-ul te anunță și îți deschide log-ul. Cauza găsită pe 0.6.19 (biblioteca de senzori NVIDIA, la o schimbare de monitoare) nu mai poate închide aplicația.
- Titlurile ferestrelor nu mai ajung în `log.txt`.

## Îmbunătățit
- Peste fullscreen, alertele importante (baterie, temperatură, memorie, rezultatul unei unelte pornite de tine) apar scurt, 2,5 secunde, iar piesa nouă se arată o singură dată după ce ieși din fullscreen.
