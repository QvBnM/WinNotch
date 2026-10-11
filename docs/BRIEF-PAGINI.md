# Brief — cele patru pagini standard din notch (P75)

Planșele sunt în `docs/design/` (`02-pagina-acasa.png` … `05-pagina-unelte.png`). Ele sunt specificația: numerele de mai
jos sunt măsurate din ele, la lățimea de referință a notch-ului deschis, **720 px**.

De ce există brieful: planșele au fost făcute într-o sesiune și au rămas doar acolo, deci s-au pierdut. 0.6.27 → 0.6.29
au dus în aplicație sertarul, colțurile, bulina și **tipografia** paginilor (P74), dar nu și **forma** lor. Asta e P75.

---

## 1. Limbajul comun (se aplică la toate patru)

### Fețe
| Rol | Față | Unde |
|---|---|---|
| Cuvinte | **Archivo** (`PageFont`) 400 / 600 | titluri de rând, nume, butoane |
| Numere și etichete gravate | **Chivo Mono** (`PageMono`) 400 / 500 | **orice** măsurătoare, plus numele de grup |

Amândouă sunt înglobate în exe (`Fonts/`, SIL OFL): niciuna nu vine cu Windows. Cifrele lui Chivo Mono au aceeași
lățime, deci coloanele de numere se aliniază și niciun rând nu tremură când i se schimbă valoarea.

### Scara de text
| Ce | Mărime | Greutate | Față |
|---|---|---|---|
| Numărul mare al unui card (28%, 41%) | 20 | 500 | mono |
| Numărul secundar (17 px: memorie, placă video) | 17 | 500 | mono |
| Valoare de rând (48,2 · 12 ms · 84%) | 12–13 | 500 | mono |
| Titlu de rând („Microfonul e folosit”) | 13 | 600 | cuvinte |
| Nume în listă | 11,5 | 600 | cuvinte |
| Text secundar, sub titlu | 11,5 | 400 | cuvinte |
| Etichetă de rând („descărcare”, „ieșire”) | 10,5 | 400 | cuvinte |
| **Eticheta gravată de grup** („PROCESOR”, „CONECTATE”) | 9,5 | 400 | mono, MAJUSCULE, spațiere 0,085 em |
| Unitatea de după număr („MB/S”, „4K · 60 HZ”) | 9,5 | 500 | mono, MAJUSCULE |

### Culori — toate din temă, niciuna scrisă în cod
| Rol în planșe | Jeton | Înțeles |
|---|---|---|
| chihlimbar | `AccentBrush` | **ce se mișcă acum**: piesa, încărcarea, descărcarea, bateria care crește |
| ger | `InfoBrush` | **date reci**: temperaturi, procesele care consumă |
| text | `InkBrush` · `MutedBrush` · `DimBrush` | titlu · secundar · etichetă gravată |
| suprafețe | `NotchBrush` (panoul) · `ChipBrush` (cardul) · `ChipHoverBrush` (pastila/chip) · `TrackBrush` (șina barei) | |
| stări | `HotBrush` (microfon, prea cald) · `OkBrush` | |

Regula de citire a planșelor: **două accente, fiecare cu un singur sens.** Dacă un număr nu se mișcă și nu e temperatură,
rămâne alb.

### Geometrie
- Panoul: colț **20**, padding **18**.
- Antetul: înalt **26**, filele într-o pastilă `SegBrush` colț **10**, fila activă `ChipBrush` colț **7**; sub antet **12**.
- Card: colț **16**, padding **12–14**; între carduri **10**.
- Chip / buton-pastilă: colț **999** (capsulă), înălțime 19–22.
- Bară: înaltă **3**, colț 1, șina `TrackBrush`.
- Coloane (din 684 de conținut, cu șanț 10): **Acasă** 440 / 232 · **Sistem** 394 / 280 · **Dispozitive** 374 / 300 ·
  **Unelte** 394 / 280. În cod sunt proporții, nu pixeli: notch-ul are lățimea aleasă de utilizator.
- Înălțimea panoului, în planșe: Acasă 310 · Sistem 320 · Dispozitive 330 · Unelte 360.

---

## 2. Acasă (`docs/design/02-pagina-acasa.png`)

Două coloane: **muzica**, mare, în stânga; **ceasul, agenda și vremea**, una sub alta, în dreapta.

**Cardul de muzică** (440 × 190) e singurul card colorat din toată aplicația: fundalul lui ia culoarea copertei, stinsă,
într-o bandă care se topește spre dreapta (în planșă, 120 px de bandă). Înăuntru:
- copertă 46, colț 11 · titlu 13/600 · artist · sursă 11,5 muted · în dreapta, un buton rotund 26 pentru ieșirea audio;
- **versurile**, dacă există: rândul curent 12,5/600 în **chihlimbar**, următorul 11,5 în `DimBrush`;
- vizualizatorul (bare subțiri) la stânga, pe linia de progres;
- progres: `1:12` mono stânga, `3:28` mono dreapta, firul între ele;
- transport: ⏮ ⏯ ⏭ — butonul de pauză e un **disc plin chihlimbar**, 26, singurul buton plin din pagină — apoi volumul,
  cu numărul la capăt.

Sub card, **sursele care se aud acum**, ca pastile: punct colorat + nume, iar ultima e `+N` dacă nu încap toate.

Coloana din dreapta, trei carduri:
1. **ceasul** 22/700 mono (`09:41`), data dedesubt;
2. **următorul eveniment**: o linie verticală de 2 px `InfoBrush` la stânga, ora `11:00 – 11:30` mono deasupra titlului;
3. **vremea**: temperatura 20/500 mono + starea în cuvinte, minima/maxima la dreapta, iar jos patru ore, ca o coloană de
   numere (ora mică, deasupra, gravată; gradele dedesubt).

## 3. Sistem (`docs/design/03-pagina-sistem.png`)

Stânga: **procesorul**, cu grafic. Dreapta: **internetul** și **cine consumă**.

- **Cardul procesorului** (394 × 127): eticheta gravată, apoi `28%` 20/500 și, la dreapta, `54 °C` 13/500 în **ger**.
  Dedesubt, un grafic pe **ultimul minut**, cu două linii: încărcarea, chihlimbar, plină; temperatura, ger, punctată.
  Sub grafic, legenda (două liniuțe colorate + cuvânt, 10) și, la dreapta, „ULTIMUL MINUT”, gravat.
- **Memorie** și **Placă video**, două carduri pe un rând (212 / 172): eticheta, numărul 17/500, iar lângă el secundarul
  gravat (`13,1 / 32 GB`) sau temperatura în ger. Fiecare cu bara lui de 3 px dedesubt.
- **Bateria**, un card scund, lat: eticheta la stânga, bara pe mijloc, `84%` și starea în cuvinte la dreapta.
- **Internet** (280 × 145): „INTERNET · WI-FI” gravat, latența `12 ms` la dreapta; două rânduri (descărcare, încărcare),
  fiecare cu eticheta la stânga, numărul 12,5/500 și unitatea `MB/S` gravată la dreapta, cu bara dedesubt. Jos, un chip
  cu ultimul test de viteză: `8 OCT  412 / 54 Mbps  ›`.
- **Consumă acum** (280 × 100): patru rânduri, nume în cuvinte la stânga, bară **ger** pe mijloc, procent mono la dreapta.
- În antet, la dreapta, butonul-capsulă „Test de viteză”.

## 4. Dispozitive (`docs/design/04-pagina-dispozitive.png`)

Stânga: **confidențialitatea**, ca două carduri mari, unul pentru microfon, unul pentru cameră — iconiță 34 (colț 11,
fundal chihlimbar stins cât e în uz, neutru cât e liberă), titlu 13/600, sub el cine și de cât timp; la dreapta, butonul
„Oprește pentru toate”. Sub ele, cardul **AUDIO**: două rânduri, `ieșire` / `intrare` ca etichete, numele în 12/600 și
butonul „Schimbă” pe rândul de ieșire.

Dreapta, un singur card înalt: **CONECTATE**, cu numărul lor gravat în colț. Fiecare rând: pătrat 20 (colț 7) pentru
iconiță, numele 11,5/600, iar la dreapta **ce știm despre el**, gravat: `4K · 60 HZ`, `1080P · 144 HZ`, `BT`, `USB` —
sau, când e viu, chihlimbar: `72%` pentru bateria căștilor. Stick-ul are al doilea rând (`41 GB LIBERI`) și butonul
„Scoate”.

## 5. Unelte (`docs/design/05-pagina-unelte.png`)

Singura pagină cu câmpuri: notch-ul cere focus aici și îl dă înapoi când pleci.

- **Căutarea**, sus-stânga: câmp lat, iar sub el rezultatele — pătrat colorat, nume, iar la dreapta tipul, gravat
  (`SETARE`, `FOLDER`, `APLICAȚIE`). Ultimul rând e **calculatorul**: `= 250*1,19` la stânga, rezultatul `297,5` în
  chihlimbar la dreapta, cu „click = copiat” lângă el.
- **Patru plăci de acțiune** pe un rând: Captură · Zonă · Text din ecran · Eliberează RAM. Fiecare are iconița sus și
  numele jos; a patra arată în loc de iconiță procentul de memorie, în chihlimbar.
- **Fereastra activă**, un rând scund: eticheta gravată la stânga, apoi chipuri — Deasupra · Monitor 2 · Jumătate · Mini.
- Dreapta: **CLIPBOARD** (numărul în colț; primul rând într-un chip, cu steluță dacă e fixat; dedesubt, ultimele; jos,
  „Parolele nu se înregistrează.”), **SPAȚII DE LUCRU** (chipuri) și **NOTIȚA** (cu „SALVAT” gravat în colț).

---

## 6. Reguli de implementare

- Comutator `pagini-format`, Experimental, oprit până la versiunea care îl anunță. Cât e pornit, el decide forma;
  `pagini-redesenate` (P74) rămâne pentru tipografie și nu se bat între ele: formatul nou își aduce propria tipografie.
- Nimic nou în `NotchWindow.xaml.cs`: paginile se construiesc în `Panes/`, iar jetoanele și constructorii comuni stau
  într-un singur loc (`Features/PagesFormat/`), ca să nu se repete patru rețete.
- Regulile pure (scara de text, proporțiile coloanelor, ce culoare primește un număr) se testează fără WPF.
- Fără cronometre noi: paginile se actualizează pe tick-ul de o secundă care există deja.
- Toate culorile prin `DynamicResource` / `SetResourceReference` — temele luminoase trebuie să rămână citibile.
