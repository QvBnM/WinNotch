# ADR 0004 — Motorul de context (Context Engine)

- **Stare:** Acceptată
- **Data:** 2026-10-05
- **Sarcina din roadmap:** P12

## Context
Quick Actions, Game Mode, Dev Mode și paginile alese după context (0.8+) trebuie toate să știe „ce face utilizatorul acum”:
ce aplicație e în față, dacă e un joc sau un film pe tot ecranul, dacă e într-o întâlnire, dacă ascultă în căști, dacă e pe
baterie. Până acum fiecare loc își citea singur starea (notch-ul scanează monitoarele la 500 ms, alerta de piesă se uită la
fereastra din față, panoul Dispozitive citește microfonul). Repetat în fiecare funcție nouă, asta ar însemna polling dublat,
reguli diferite pentru același lucru („e întâlnire?”) și titluri de ferestre scăpate în log.

## Decizie
- **Un singur loc:** `Core/Context/ContextEngine` (fără WPF, testat) ține un `ContextSnapshot` imutabil (`record`, schimbat
  cu `with`) și ridică `Changed(Old, New, Fields)`, unde `Fields` sunt exact câmpurile care diferă (`ContextField`, flags).
  `ContextEngine.Current` e instanța aplicației.
- **Surse prin interfețe** (`IForegroundSource`, `IMediaContextSource`, `IPrivacyContextSource`, `IAudioContextSource`,
  `INetworkContextSource`, `IPowerContextSource`, `IDisplayContextSource`, `IUsbDriveContextSource`, `IIdleContextSource`),
  fiecare `IContextSource<T>`: `Start`/`Stop`, `Changed` („citește-mă din nou”) și `Read()`. Implementările Windows
  (`Features/Context/WindowsSources.cs`) refolosesc serviciile existente (NowPlaying, PrivacyService, NetService, Native),
  fără să le schimbe comportamentul.
- **Evenimente Windows, nu polling:** aplicația din față prin `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)` (pus și scos pe firul
  UI, care are bucla de mesaje); ieșirea audio prin `IMMNotificationClient`; rețeaua prin `NetworkChange`; alimentarea și
  monitoarele prin `SystemEvents`; stick-urile prin WMI `Win32_VolumeChangeEvent`; media prin `NowPlaying.Changed`. Doar ce nu
  are eveniment e citit la 3 s (`PollInterval`, regula: niciodată sub 2 s): inactivitatea (`GetLastInputInfo`), microfonul și
  camera (din `PrivacyService`, cu cache de 2 s, pe care notch-ul îl citește oricum în standby) și dreptunghiul ferestrei din
  față (pentru o fereastră care trece pe tot ecranul fără să-și schimbe locul).
- **Debounce de 300 ms** (trailing), dar cel mult 1 s după prima schimbare (`MaxDelay`): un Alt+Tab rapid prin zece ferestre
  dă un singur eveniment, iar o schimbare care nu se oprește tot ajunge la abonați. Sursele sunt citite după debounce, pe un
  fir de timer, nu pe UI.
- **Reguli pure** în `ContextRules`: tipul de ecran complet (joc: D3D exclusiv sau joc cunoscut; video: player sau browser care
  redă; altceva), întâlnirea (o aplicație de întâlniri folosește microfonul/camera; fereastra de ședință Teams/Zoom; Meet /
  Teams / Zoom pe web) și ieșirea audio (form factor, dispozitivul Bluetooth din spate, apoi numele). Teams doar deschis nu e
  întâlnire.
- **Categorii** în `AppCategories`: tabel proces → Dev, Browser, Meeting, Game, Media, Office, Creator (necunoscut = Other),
  cu nume noi și vechi, comparat fără cale, majuscule și „.exe”; rânduri cu prefix pentru exe-uri cu versiune (`gimp-2.10`).
  Se extinde cu un rând în `DefaultEntries` sau cu `With(...)`.
- **Comutator „context-engine”** (Beta, pornit implicit, fiindcă nu are interfață; `--safe-mode` îl oprește). Oprit: toate
  sursele se opresc (hook-uri și abonamente scoase), snapshot-ul devine `Empty`, nu se mai ridică nimic, nici ce era în
  așteptare.
- **Erori:** o sursă care aruncă își păstrează ultima valoare bună și e reîncercată după 30 s, apoi tot mai rar (până la
  10 minute); e raportată la `FeatureFlags.ReportError` **o singură dată pe sursă și pornire**, ca o singură sursă stricată
  să nu oprească tot motorul. Trei surse diferite stricate opresc funcția (regula comună a comutatoarelor). Un abonat care
  aruncă e scris în log și ignorat.
- **Confidențialitate:** titlul ferestrei e în snapshot (îl folosesc regulile), dar nu ajunge niciodată în log: `ToLogString`
  are doar procesul și categoria, iar din excepții se scrie doar tipul. Motorul scrie în log doar schimbările rare (ecran
  complet, întâlnire, ieșire audio, rețea, monitoare, stick USB), nu fiecare schimbare de fereastră.
- **Acțiunea `context.show`** (Sigură, pe firul UI, ține de comutator) arată în notch ce vede motorul, pentru depanare.

## Alternative
- **Fiecare funcție citește singură starea:** exact duplicarea pe care ADR 0001 o interzice.
- **Polling la 500 ms pentru tot (ca `MonitorTick`):** simplu, dar încalcă regula „fără polling sub 2 s în standby” și ar
  dubla munca deja făcută de notch.
- **Mutarea scanării monitoarelor din notch în motor:** ar schimba comportamentul existent (ascunderea notch-ului peste
  jocuri); rămâne un pas separat, după ce motorul e folosit de funcții.
- **Raportarea fiecărei erori la `ReportError`:** o sursă citită la 3 s ar opri tot motorul în 9 secunde.

## Consecințe
- Funcțiile noi se abonează la `ContextEngine.Current.Changed` (vezi `CLAUDE.md`, „Cum folosești contextul”) și nu mai citesc
  singure fereastra din față, microfonul sau rețeaua.
- `Changed` vine pe un fir de timer: cine atinge interfața trece prin Dispatcher; fiecare abonare are dezabonarea ei.
- Notch-ul își păstrează încă propriile citiri (monitoare la 500 ms, microfonul în pastila mică); mutarea lor peste motor
  e un pas separat, fără grabă.
