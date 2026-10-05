# ADR 0001 — Arhitectura 0.7: fundația pentru funcțiile noi

- **Stare:** Acceptată
- **Data:** 2026-10-05
- **Sarcina din roadmap:** P00 (pregătire), implementată în P10–P13

## Context
Până la 0.6.6, fiecare funcție s-a legat direct în `NotchWindow.xaml.cs` și `EditorWindow.cs` (fișiere de peste 900 de
rânduri), cu propriile setări, alerte și cronometre. Pentru 0.7 → 1.0 urmează multe funcții noi, construite de agenți AI.
Dacă fiecare își face propriile sisteme, apar duplicări, conflicte între alerte și regresii greu de găsit.

## Decizie
Patru piese de infrastructură, în `Core/`, peste care se construiesc toate funcțiile noi (`Features/<NumeFuncție>/`):

1. **Flags** (`Core/Flags/`) — fiecare funcție nouă are un comutator declarat într-un catalog central, salvat în
   `settings.json`, oprit implicit până la anunț. Poate fi oprită automat după erori repetate și ignorată în mod sigur.
2. **Actions** (`Core/Actions/`) — registru central de acțiuni (ID, nume, iconiță, execuție). Notch-ul, scurtăturile și
   Command Bar-ul apelează acțiuni, nu cod direct.
3. **Context** (`Core/Context/`) — o singură sursă pentru „ce face utilizatorul acum” (aplicația din față, media,
   fullscreen, întâlniri, rețea), publicată ca evenimente. Funcțiile nu mai citesc fiecare starea Windows separat.
4. **Activity** (`Core/Activity/`) — managerul activităților live din notch: prioritate, coadă, durată, înlocuire.
   Funcțiile cer afișarea unei activități; nu desenează singure peste notch.

**Regula:** funcțiile noi se construiesc peste aceste patru piese. Fișierele mari existente primesc doar puncte de legătură
de câteva rânduri. O funcție care are nevoie de ceva ce lipsește extinde piesa potrivită, nu își face un sistem propriu.

## Alternative
- **Continuăm direct în fișierele existente:** cel mai rapid pe termen scurt, dar crește riscul de regresii și conflicte între agenți.
- **Plugin-uri încărcate dinamic (DLL-uri):** flexibil, dar contrazice livrarea ca un singur exe semnat și regulile de
  securitate (cod încărcat din afara exe-ului verificat).

## Consecințe
- Fiecare funcție poate fi oprită fără repornire și fără să afecteze restul aplicației.
- Comportamentul existent rămâne neschimbat; piesele noi se adaugă alături.
- Costă puțin cod de infrastructură înainte de primele funcții vizibile (P10–P13 înaintea Command Bar-ului).
