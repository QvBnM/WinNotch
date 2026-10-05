# ADR 0002 — Revenire automată la o versiune stricată

- **Stare:** Acceptată
- **Data:** 2026-10-05
- **Sarcina din roadmap:** P01

## Context
Versiunile 0.7 → 1.0 sunt construite de agenți AI și publicate automat. Partea WPF nu poate fi compilată și rulată de
agenți (lucrează pe Linux), deci o versiune care se închide imediat la pornire poate ajunge la utilizator. Până la 0.6.8,
versiunea nouă ștergea `WinNotch.old.exe` la prima pornire: după o actualizare stricată nu mai exista la ce reveni.
Comparația versiunilor era `System.Version`, care nu înțelege sufixe ca „-rc.1”.

## Decizie
- Toată logica stă în `Core/Update/`, fără WPF și fără I/O direct în decizie (ceasul și stocarea sunt injectate), ca fiecare
  ramură să fie testată automat: `AppVersion`, `ReleaseFeed`, `StartupGuard` + `IStartupStore`, `Rollback`.
- **Pornire monitorizată** în `startup.json`: o pornire rămâne „deschisă” până la o închidere curată (Ieșire, oprirea
  Windows, repornirea pentru actualizare). 3 porniri deschise în 5 minute → o repornire în modul sigur; o închidere
  bruscă a acestui mod sigur automat în primele 5 minute → revenire. Un mod sigur care a mers mai mult sau unul pornit de
  mână se numără doar ca o închidere obișnuită (o pană de curent după ore nu trebuie să refuze o versiune bună). Pornirea se înregistrează imediat după verificarea „o singură instanță” (altfel a doua
  copie, care iese imediat, ar fi numărată ca închidere bruscă), înaintea setărilor și a oricărui serviciu.
- **Revenirea** doar redenumește fișiere (`exe → rejected`, `old → exe`); nimic nu se șterge înainte ca schimbarea să reușească.
  Se revine doar la un `WinNotch.old.exe` care e un WinNotch mai vechi (versiunea din fișier). `rollback.json` se scrie
  înaintea schimbării (un proces oprit la jumătate tot lasă nota); un eșec anulează nota și refuzul. Mutex-ul „o singură
  instanță” se eliberează abia înainte de a porni celălalt proces. Ordinea pașilor stă în `StartupCoordinator` (testat),
  nu în codul WPF.
- O pornire care eșuează la jumătate (excepție în `OnStartup`) închide procesul (`Environment.Exit(1)`), deci se numără;
  „sănătos” începe să fie măsurat abia după ce notch-ul și iconița există. Cel mult o revenire la 30 de minute, ca două versiuni stricate să
  nu se înlocuiască la nesfârșit.
- **„Sănătos”** = 10 minute fără erori neprinse, în afara modului sigur, numărate minut cu minut (un somn sau o schimbare a
  ceasului le reia). Abia atunci se șterge `WinNotch.old.exe`.
- **Versiunile refuzate** se țin minte în `startup.json` (comun tuturor versiunilor) și se aduc din `rollback.json`, scris de
  versiunea care renunță și citit o dată de cea restaurată; nu mai sunt oferite, dar o versiune mai nouă decât ele da.
- **Canalul beta** e o setare (nu un feature flag): oprit = `releases/latest` (GitHub nu întoarce niciodată pre-release-uri
  acolo); pornit = lista ultimelor 20 de release-uri. `release.yml` publică automat ca pre-release o versiune care conține „-”.
- Protecția nu are comutator: e o plasă de siguranță și trebuie să funcționeze tocmai când o funcție nouă strică pornirea.

## Alternative
- **Un proces separat de supraveghere (watchdog):** detectează și blocajele, nu doar închiderile, dar înseamnă încă un proces
  pornit permanent și o suprafață nouă de atac. Respins pentru acum.
- **Revenire imediată, fără mod sigur:** mai simplu, dar ar refuza o versiune întreagă din cauza unei singure funcții
  experimentale, care poate fi oprită de modul sigur.
- **Ținerea mai multor versiuni vechi:** mai multă siguranță, dar exe-ul are ~190 MB; una singură ajunge.

## Consecințe
- Orice ieșire curată nouă din aplicație trebuie să apeleze `App.Guard.MarkCleanExit()`.
- Revenirea la 0.6.8 sau mai veche funcționează (fișierele se schimbă), dar acele versiuni nu afișează mesajul și pot
  propune din nou versiunea refuzată.
- O versiune care se blochează fără să se închidă nu e detectată.
