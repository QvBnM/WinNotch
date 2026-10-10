# ADR 0017 — Fereastra WinNotch e un aparat, nu o foaie de setări

- **Stare:** Acceptată
- **Data:** 2026-10-10
- **Sarcina din roadmap:** P70

## Context

Fereastra v2 (P52) rezolvase structura: o singură navigare, patru file, nimic găzduit din fereastra clasică. Nu
rezolvase **cum arată**. Autorul a cerut „o regândire completă de la 0” a zonei de setări, apoi „reimplementare de la 0”.

Ce era de reparat, concret:

- **Carduri peste carduri.** Fiecare grup de opțiuni era un dreptunghi rotunjit, pe un fundal care era și el un
  dreptunghi rotunjit. Pagina se citea ca o grămadă de cutii, nu ca o suprafață.
- **Nu se vedea ce faci.** „Poziție: Centru” e un cuvânt. Notch-ul e un obiect care stă într-un loc anume pe un
  monitor anume. Fereastra descria obiectul în loc să-l arate.
- **Listele erau albe.** Niciun `ComboBox` din aplicație nu avea stil: pe tema întunecată, fiecare listă era un
  dreptunghi alb de Windows. Exact plângerea „foaia albă lipită în fereastra întunecată” care dusese la P52, rămasă
  nereparată într-un colț.
- **Numerele dansau.** „400 ms” scris cu fontul de text își schimba lățimea la fiecare cifră, deci rândul tremura
  cât trăgeai de un cursor.
- **Nu scria nicăieri ce s-a întâmplat.** Setările se aplică pe loc (corect), dar nimic nu confirma asta. Pentru un
  om obișnuit cu un buton „Salvează”, tăcerea se citește ca „nu s-a aplicat”.

Constrângerile: nicio culoare scrisă în cod (totul prin jetoanele temei, verificat de testul WV16), nimic elevat,
fără fonturi adăugate în exe (sunt megabytes într-un fișier care se descarcă la fiecare actualizare), și cele peste
treizeci de pagini care deja apelau `V2Controls` nu aveau de ce să fie rescrise.

## Decizie

**Fereastra e un monitor. Notch-ul stă sus pe el.**

1. **Semnătura: o previzualizare adevărată.** Banda de sus (`LayoutRules.BezelHeight`, 132 px) e rama ferestrei, iar
   pe ea stă o felie de monitor cu notch-ul desenat pe margine. Nu e o imagine: conturul vine din
   `AnchoredShape.Silhouette` — **același traducător** prin care se desenează notch-ul adevărat (P50) — la raza aleasă
   de utilizator, cu urechile lui P50 dacă funcția e pornită, iar ce scrie pe pastilă sunt chiar lucrurile ținute în
   standby. Schimbi poziția, se mută. Schimbi rotunjirea, se rotunjește.
   - Unde cade pastila e o regulă pură și testată: `Features/WindowV2/PreviewModel.cs` (fără WPF).
   - Desenul e `Features/WindowV2/NotchPreview.cs` și nu are nicio regulă proprie.
2. **Rândul, nu cardul.** Un grup de opțiuni e o etichetă gravată peste un șir de rânduri despărțite de o linie de
   un pixel. `V2Controls.Card(...)` și-a păstrat numele (paginile nu s-au rescris) dar nu mai desenează un card.
3. **Trei fonturi, trei roluri.** Titluri și etichete: `LabelFont` — Bahnschrift, fața de tip DIN care vine cu
   Windows 10/11, în capitale, mică și liniștită. Textul rândurilor: `UiFont` (Segoe). Orice număr, cale sau
   scurtătură: `MonoFont` (Cascadia Mono), ca o coloană de valori să se alinieze și să nu tremure.
4. **Un `ComboBox` pe temă** (`V2Combo` în `Theme.xaml`), cu cheie, nu implicit: notch-ul și fereastra clasică își
   păstrează controalele lor.
5. **Două–trei variante se arată toate** (`Segmented`); de la patru în sus se strâng într-o listă. O variantă pe care
   n-o vezi e o variantă pe care n-o știi.
6. **Subsolul spune ce tocmai s-a scris.** Fiecare control cu etichetă raportează prin `V2Controls.Report`, iar
   fereastra scrie „Poziție → Centru” lângă „se aplică pe loc”. Legătura e tăiată la închiderea ferestrei.
7. **Tema „Aparat”** (`Themes.cs`): ramă aproape neagră, ecran puțin mai deschis, linii în loc de contururi groase și
   o singură lumină de chihlimbar. Desenul ține în orice temă; cu asta arată cum a fost gândit.
8. **Workspace spune cât crește notch-ul**: sub pagină, patru segmente, aprinse cât rândurile folosite. Înălțimea la
   care se va deschide notch-ul nu se putea afla altfel decât deschizându-l.

## Alternative

- **Doar reculoarea cardurilor.** Ar fi lăsat problema: nu culoarea era greșită, ci faptul că fereastra descria
  notch-ul în loc să-l arate.
- **O a doua fereastră, „Setări v3”, cu comutatorul ei.** Ar fi însemnat două ferestre de întreținut pentru același
  lucru — exact ce interzice regula „nu duplica sisteme”. Comutatorul `window-v2` există deja și, oprit, dă înapoi
  fereastra clasică: plasa de siguranță era deja acolo.
- **Fonturi proprii în exe** (Archivo, IBM Plex, cele din machete). Câțiva megabytes la fiecare actualizare pentru o
  diferență pe care Bahnschrift + Cascadia o acoperă. Refuzat.
- **Un `ComboBox` implicit, pe toată aplicația.** Ar fi înnegrit și listele ferestrei clasice, care e o foaie
  luminoasă. Stil cu cheie.
- **Previzualizare la trecerea cu mouse-ul peste fiecare rând** (machetele o arătau). Ar fi cerut ca fiecare rând
  să-și declare ce demonstrează. Previzualizarea arată **starea de acum** și se schimbă la fiecare modificare, ceea
  ce acoperă același lucru fără un canal nou prin toate paginile. Rămâne de făcut dacă se dovedește că lipsește.

## Consecințe

- Orice pagină nouă din fereastră arată corect fără efort: apelezi `Card` + `Row` și ai desenul.
- `V2Controls.Report` e o legătură statică. Aplicația are o singură fereastră v2, iar fereastra o pune la deschidere
  și o șterge la închidere (verificat de testul WV34). O a doua fereastră v2 ar trebui s-o schimbe într-o legătură pe
  instanță — de ținut minte.
- Previzualizarea se redesenează la fiecare schimbare de setare și la fiecare redimensionare. Sunt câteva segmente de
  geometrie; nu rulează niciun cronometru pentru ea.
- Înălțimea ferestrei pierde 132 px în partea de sus. Pe un ecran mic asta se simte: `MinHeight` rămâne 600, iar dacă
  autorul spune că e prea mult, banda e un singur număr (`LayoutRules.BezelHeight`).
- `Bahnschrift` nu există pe Windows mai vechi de 1709; stiva de fonturi cade atunci pe Segoe UI Semibold.
- Regula de acum înainte: **în fereastra WinNotch nu se scriu culori în cod** (testul WV16 o ține), **numerele se scriu
  cu `MonoFont`**, și **un grup de opțiuni nu e o cutie**.
