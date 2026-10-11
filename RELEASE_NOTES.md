# WinNotch 0.6.33

## Nou
- **Cele patru pagini din notch au forma din planșele de design** — nu doar fontul, ca până acum, ci forma: două
  coloane de carduri în loc de o listă de rânduri.
  - **Acasă:** muzica umple coloana din stânga, iar cardul ei ia culoarea copertei, stinsă spre dreapta; versurile apar
    în chihlimbar sub titlu. În dreapta, ceasul, ce urmează în calendar și vremea, cu orele dedesubt.
  - **Sistem:** procesorul cu graficul lui pe ultimul minut — încărcarea plină, temperatura punctată. Memoria și placa
    video pe un rând, bateria dedesubt; în dreapta, internetul și cine consumă acum.
  - **Dispozitive:** confidențialitatea prima — microfonul și camera, fiecare cu cine le folosește și de cât timp, iar
    pătratul lor se aprinde cât sunt în uz. În dreapta, tot ce e conectat, fiecare rând terminat cu ce știm despre el.
  - **Unelte:** căutarea cu rezultatele și calculatorul, patru plăci de acțiune, fereastra activă; în dreapta,
    clipboard-ul, spațiile de lucru și notița.
- **Două culori, fiecare cu un singur sens:** chihlimbar = ce se mișcă acum, albastru-rece = date reci (temperaturi,
  procese). Un număr care nu e niciuna rămâne alb — de-aia se citesc amândouă dintr-o privire.
- *Experimentală și oprită:* o pornești din Setări → „Funcții noi” → „Forma paginilor”. **Nu e pornită implicit
  intenționat:** prima verificare automată cu ea pornită a prins notch-ul închizându-se singur după un click, și nu
  pornesc implicit ceva despre care testele spun asta. Paginile de până acum rămân neatinse și revin la o debifare.

## Reparat
- Testul automat de fum nu mai pică din cauza unei scurtături pierdute de Windows pe mașina de build.
