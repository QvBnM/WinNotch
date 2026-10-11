# WinNotch 0.6.30

## Nou
- **Design-ul nou e pornit, nu ascuns.** Tot ce s-a desenat în 0.6.27 → 0.6.29 era experimental și oprit, așa că după
  actualizare arăta exact ca înainte și părea că nu merge nimic. De acum pornesc singure patru funcții, deodată, fiindcă
  împreună sunt un singur design: **notch-ul lipit de ramă** (sertarul crescut din marginea de sus, cu umeri drepți și o
  dungă de lumină pe muchie), **colțurile continue** (se rotunjesc treptat, nu dintr-odată), **bulina de stare** (ce e
  pornit acum: microfonul, piesa, încărcătorul) și **paginile redesenate** (etichete gravate, toate măsurătorile în
  fontul cu cifre de aceeași lățime, bare mai subțiri, un fir pe muchia de jos pentru lucrul care rulează).
- Oricare dintre ele se poate opri din Setări → „Funcții noi”, iar `--safe-mode` le oprește pe toate.

## Reparat
- **Notch-ul nu se mai deschide singur când tragi o fereastră spre marginea de sus.** De la 0.6.20, orice buton de mouse
  ținut apăsat și plimbat peste pastilă (o fereastră trasă ca să se lipească sus, o bară de derulare, o selecție de
  text) era luat drept tragere de fișiere: notch-ul se deschidea peste ce trăgeai, nu se mai dădea la o parte peste o
  fereastră maximizată, iar alerta de pe pastilă dispărea. Acum presupunerea aceea contează doar cât „Raft” e pornit —
  singurul loc unde o tragere peste notch are unde să cadă.
