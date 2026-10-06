# ADR 0009 — Quick Actions: tabelul de reguli, doar acțiuni sigure, sugestiile prin Activity Manager

- **Stare:** Acceptată
- **Data:** 2026-10-06
- **Sarcina din roadmap:** P20

## Context
P20 cere 2–4 acțiuni sub pastilă, la hover, după context (întâlnire + căști → mută microfonul / volum 40%; media → pauză /
următoarea; stick USB → deschide / scoate; baterie sub 20% → economisire), reguli într-un tabel ușor de extins și, nesolicitat,
cel mult o sugestie la 10 minute, cu „Nu mai arăta”. Constrângeri: contextul doar din motorul de context (ADR 0004), acțiunile
doar din registru (ADR 0003, pornite prin `InvokeAsync`), tot ce apare singur în pastilă doar prin Activity Manager (ADR 0006),
fără polling, `NotchWindow.xaml.cs` atins doar cu legături de un rând, logica testată fără WPF, test de fum pe ambele căi.

## Decizie
- **Tabel de date** (`QuickActionRules.Table`, `Features/QuickActions/QuickActions.cs`): fiecare regulă are un id stabil
  (cheia „Nu mai arăta” din `settings.json`), un titlu, condiția pe `ContextSnapshot`, câmpurile citite, dacă poate fi sugerată
  și 2–4 referințe la acțiuni (id exact sau prefix, pentru acțiunile câte una pe stick; etichetă scurtă; parametri ficși, ca
  `valoare=40`). Ordinea din tabel e prioritatea: **se arată doar prima regulă care se potrivește** și mai are cel puțin
  **o** acțiune utilizabilă (`MinActions = 1`; cel mult 4). Regulile: `meeting-headphones`, `battery-low`, `media-playing`, `usb-drive`.
- **Doar acțiuni sigure și disponibile**, filtrate în clasa pură (`QuickActionRules.Usable`): `Safety == Safe` (Confirm și
  Dangerous niciodată), `AllowedInvokers` conține `QuickAction`, parametrii cunoscuți, cei obligatorii dați și valizi, iar
  catalogul (registrul real prin `RegistryQuickActionCatalog`: comutatorul acțiunii și `IsAvailable`) spune că merge acum.
  Consecință: „Scoate stick-ul” (`device.eject-*`, Confirm) nu apare niciodată; rămâne în tabel ca intenție, filtrată.
- **Acțiuni noi, simple și sigure:** `device.open-<literă>` (Explorer pe rădăcina stick-ului, prin `Shell.Open`, ca „Deschide”
  din vechea alertă) și `settings.battery-saver` (`ms-settings:batterysaver`: Windows nu are un API documentat pentru
  pornirea economisirii, deci butonul deschide pagina). Plus `quick-actions.show-hidden` (anulează toate „Nu mai arăta”).
- **Rândul se alege la deschidere** (legătura de un rând din `Expand`, după `_pane.Refresh()`, înainte de `ApplyMode`):
  comutatorul și `ContextEngine.Current?.Snapshot` (null → `Empty`, care nu se potrivește cu nimic) sunt citite atunci, o dată.
  Rândul stă în `OverlayHost` (ca nota din modul de editare), centrat jos; `PanelH` adaugă locul lui (o legătură de un rând),
  iar `PaneHost` primește margine jos, ca pagina să nu fie acoperită. Dispare la `Collapse`, la oprirea comutatorului și în
  modul de editare (legătura din `UpdateHeader`). Este același pe ambele căi ale alertelor. Butoanele: stilul `GhostPill`,
  pensulele temei, id UI Automation `qa-<id acțiune>`; un click → `ActionRegistry.Current.InvokeAsync(id, args, QuickAction)`,
  fără confirmare, excepțiile → `ReportError("quick-actions")`. Notch-ul rămâne deschis.
- **Sugestiile nesolicitate doar cu „activity-manager” pornit**, ca **peek Low** (`ActivityManager.Post`, id `quick-actions`),
  niciodată ca alertă paralelă. Abonare la `ContextEngine.Changed` doar cât e pornit comutatorul (dezabonare la oprire și la
  închidere); pe firul timerului se trec mai departe doar schimbările câmpurilor regulilor care sugerează (Meeting, AudioOutput,
  UsbDrive), restul pe Dispatcher. Se sugerează o regulă care **abia începe** să se potrivească (nu se potrivea în `Old`),
  marcată `Suggest`, neascunsă. Muzica și bateria nu sunt sugerate: au deja alertele lor (piesă nouă, baterie 20 % / 10 %).
- **Limita de 10 minute** e una pentru toate regulile, în `QuickActionSuggester` (ceas injectat, în memorie): 9:59 → „amânată”,
  10:00 → da; un ceas dat înapoi blochează în continuare (partea sigură). Pornește doar dacă peek-ul a fost primit de manager
  (nu „Dropped”: notch deschis, ecran complet).
- **„Nu mai arăta”** e un buton în rând, doar pentru regulile care pot sugera și doar cu managerul pornit; salvează id-ul în
  `AppSettings.QuickActionsHidden` (listă înlocuită, nu modificată pe loc; curățată la citire). Oprește doar sugestiile:
  butoanele regulii rămân la deschidere (le-ai cerut tu, prin hover).
- **Testul de fum** pune contextul în motor, ca la P27: `ContextEngine.ForceMeetingForSmoke` (internal, apelat doar din
  `NotchWindow.Smoke.cs` la `fake-meeting <headphones|speakers|bluetooth|none>`, doar cu `--smoke`) face ca o întâlnire „Test”
  cu ieșirea dată să treacă prin flush-ul obișnuit. Rulează pe ambele căi: oprit → niciun buton; pornit → „Microfon” și
  „Volum 40%” prin UI Automation, click (Invoke) pe „Microfon” de două ori (microfonul rămâne cum era), verificat prin log-ul
  registrului și contorul `;qa=` din starea notch-ului; cu managerul pornit, exact o sugestie (`;qs=1`), a doua amânată, și
  „Nu mai arăta”. `;qa=` / `;qs=` sunt la sfârșitul stării, ca formatul vechi să rămână valid.

## Alternative
- **Toate regulile potrivite, amestecate în același rând:** mai multe butoane, dar „Nu mai arăta” n-ar mai ține de o regulă
  clară, iar ordinea ar fi greu de prezis. Prima regulă potrivită e simplu de explicat și de testat.
- **„Scoate” cu confirmare în rând:** ar însemna un al doilea click „Sigur?” sub pastilă; cerința e ca Quick Actions să nu
  aibă acțiuni Confirm. Scoaterea rămâne în pagina Dispozitive și în Command Bar.
- **Un peek cu butoane (Interactive):** managerul tratează Low ca peek fără interacțiune (2 s, click-through); a-l schimba ar fi
  atins regulile P13. Butoanele sunt în rândul de la hover, unde e și „Nu mai arăta”.
- **Rândul ca pastilă separată, sub notch:** ar cere schimbări în `PollTick` (ieșirea mouse-ului închide notch-ul) și în
  înălțimea ferestrei; în `OverlayHost` merge cu o legătură de un rând.
- **Pornirea economisirii bateriei direct:** doar prin API-uri nedocumentate; pagina din Setări Windows e sigură.
- **Sugestii pentru muzică și baterie:** dublură cu alertele existente și prea dese (fiecare piesă pornită).

## Consecințe
- O regulă nouă = un rând în tabel (testele QA2–QA4 verifică id-urile, numărul de acțiuni, existența lor în registru și că
  regula se potrivește doar pe contextul ei). O acțiune nouă devine buton doar dacă e Safe și disponibilă.
- `ForceMeetingForSmoke` e doar pentru testele de fum (testul QA27 pică la orice alt apelant).
- Textele din log „Quick Actions: pornit/oprit.”, „sugestie <regulă>.”, „sugestie amânată (<regulă>…”, „sugestiile pentru
  „<regulă>” nu mai apar.”, comanda `fake-meeting`, id-urile `qa-*` și câmpurile `;qa=` / `;qs=` sunt folosite de testul de fum.
- Ora ultimei sugestii nu se păstrează după repornire (cel mult o sugestie în plus după un restart).

## Note după revizia R1
- **Rezultatul click-ului** apare 4 s la capătul rândului (propoziția acțiunii, tăiată la 60 de caractere, `DimBrush` / `WarnBrush`
  la eșec), ascuns de un `DispatcherTimer` oprit la prima tragere și la dispariția rândului (nu e polling). Nu ajunge în log.
- **Stick-ul fără citiri pe firul UI:** referințele cu prefix ale regulii `usb-drive` au `TrustContext`: disponibilitatea vine din
  condiția regulii (`UsbDriveConnected` din motor), nu din `IsAvailable` (care citește unitățile); comutatorul acțiunii tot contează,
  iar registrul verifică din nou la click. `QuickActionsOnOpen` nu mai cheamă `Refresh`; lista acțiunilor de stick e citită din nou
  pe firul timerului motorului, la o schimbare `UsbDrive`, și o dată la pornirea funcției (`Task.Run`). Rămâne: după Command Bar
  (care cere `Refresh` la deschidere), prima deschidere a notch-ului citește lista o dată pe firul UI.
- **Limita de 10 minute** pornește doar pentru `PostResult.Shown` / `Updated` (`QuickActionSuggestions.ConsumesInterval`):
  o sugestie pusă la coadă sau strânsă în „N noutăți” nu o consumă (rar, poate apărea mai târziu din coadă).
- Rândul are 1–4 butoane (stick: doar „Deschide”); eticheta microfonului e „Mută / pornește microfonul”; la ieșirea din modul
  de editare, cu notch-ul deschis, rândul e ales din nou (o legătură de un rând în `ExitEdit`).
