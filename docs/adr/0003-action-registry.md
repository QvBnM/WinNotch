# ADR 0003 — Registrul de acțiuni (Action Registry)

- **Stare:** Acceptată
- **Data:** 2026-10-05
- **Sarcina din roadmap:** P11

## Context
Command Bar (P14), Quick Actions (P20), API-ul local (P40), Workflows (P50) și Undo Center (P52) trebuie toate să pornească
aceleași lucruri (volum, capturi, ferestre, spații de lucru…). Până acum fiecare buton apela direct un serviciu sau o metodă
din `NotchWindow`, fără un nume comun, fără verificări comune și fără un loc unde să fie găsite.

## Decizie
- Fiecare capabilitate e o **acțiune** (`Core/Actions/ActionDescriptor`) cu un **id stabil `zonă.verb`**: litere mici, cifre
  și cratime, un singur punct (`audio.mute-mic`, `window.half`, `workspace.open-lucru`). Id-ul nu se schimbă după publicare
  (îl vor folosi workflow-uri salvate și API-ul local). Acțiunile dinamice primesc un sufix din nume, fără diacritice.
- Un singur registru (`ActionRegistry.Current`), umplut o dată la pornire (`App.RegisterActions`, după FeatureFlags).
  Acțiunile care vin și pleacă (spații de lucru, stick-uri USB) vin din `IActionProvider`, citite la cerere și păstrate până
  la un eveniment `Changed` sau `Refresh()`.
- **Toate verificările stau în registru,** nu în apelanți: existența, cine are voie, disponibilitatea, comutatorul funcției
  (`FeatureId`), parametrii tipizați (Int, Percent, Enum, Text cu lungime maximă), timpul maxim (10 s implicit), firul
  interfeței (`IUiDispatcher`). `InvokeAsync` nu aruncă niciodată; o excepție devine `Failed` și `FeatureFlags.ReportError`.
- **Reguli de siguranță pentru apelanți:**
  - `Safety`: Safe / Confirm / Dangerous. Apelantul (Command Bar, Quick Actions) cere confirmare pentru Confirm și Dangerous.
  - `AllowedInvokers` implicit: UI, CommandBar, QuickAction, Workflow. **API-ul local (`LocalApi`) nu are acces implicit:**
    o acțiune trebuie să-l ceară explicit, iar registrul refuză la înregistrare orice acțiune ne-sigură care îl cere.
  - Jurnalul conține doar id-ul, apelantul și rezultatul, niciodată valorile parametrilor (pot fi texte personale).
- Acțiunile incluse (`Features/Actions/BuiltInActions`) doar apelează codul existent, printr-o interfață (`IBuiltInHost`),
  ca lista să fie testată fără WPF. Butoanele existente rămân pe calea lor; mutarea lor pe registru e un pas separat.
- Căutarea e în registru (fără diacritice, exactă > început de cuvânt > subșir > litere în ordine, apoi cele recente), ca
  toate interfețele viitoare să găsească la fel.

## Alternative
- **Comenzi WPF (`ICommand`/`RoutedCommand`):** legate de interfață, fără parametri tipizați, fără verificări comune și greu de
  pornit din API-ul local sau din workflow-uri. Respins.
- **Fiecare funcție își ține lista ei de acțiuni:** ar duplica căutarea și verificările; exact ce regula „nu duplica
  sisteme” interzice.
- **Ordonare după frecvență salvată pe disc:** utilă, dar ar însemna date de utilizare păstrate; pentru acum istoricul e doar
  în memorie (ultimele 50).

## Consecințe
- Orice capabilitate nouă din P12+ se expune și ca acțiune (vezi `CLAUDE.md`, „Cum adaugi o acțiune”).
- „Jumătate” rămâne o singură acțiune care alternează stânga / dreapta, ca butonul existent: acțiuni separate pentru
  stânga și dreapta cer o schimbare în `WindowTools`, lăsată pentru un pas în care serviciile pot fi modificate.
- Listele dinamice nu se reîmprospătează singure la schimbarea spațiilor de lucru sau la conectarea unui stick: cine le
  folosește (Command Bar) apelează `Refresh()` la deschidere.
