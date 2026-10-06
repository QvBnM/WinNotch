# ADR 0010 — Smart Clipboard: recunoaștere pură, acțiuni fără parametri, ultimul text copiat, nimic în log

- **Stare:** Acceptată
- **Data:** 2026-10-06
- **Sarcina din roadmap:** P21

## Context
P21 cere ca textul copiat să fie recunoscut (URL cu curățarea parametrilor de urmărire, JSON de formatat / compactat, culoare
hex cu mostră, e-mail, IP, cale de fișier, JWT decodat local, număr de telefon), cu acțiunile ca chip-uri în widget-ul
Clipboard și, opțional, un peek la copiere (implicit oprit). Parolele rămân ignorate ca azi. Constrângeri: comutator
„smart-clipboard” (Experimental, oprit, widget-ul neschimbat cât e oprit), logica fără WPF și testată, capabilitățile ca
acțiuni în registru (ADR 0003), ce apare singur în pastilă doar prin Activity Manager (ADR 0006), fără polling, nimic din
conținutul clipboard-ului în log, fișierele mari atinse doar cu legături de un rând, procese doar prin `Shell.Open`.

## Decizie
- **Recunoașterea** e o clasă pură, `SmartClipRecognizer` (`Features/SmartClipboard/SmartClip.cs`), scrisă de mână, **fără
  expresii regulate** (nimic nu poate face backtracking): textul e tăiat la margini; peste 64 KB nu e analizat; în afară de
  JSON, doar un singur rând. Ordinea fixă (`Priority`): **JSON → JWT → URL → e-mail → culoare → IP → cale → telefon**;
  primul tip care se potrivește câștigă. Fiecare regulă e strictă, ca să nu apară chip-uri pe texte obișnuite: JSON doar
  obiect sau listă, tot textul (`JsonDocument`, adâncime 64; scrierea înapoi cu `UnsafeRelaxedJsonEscaping`, ca diacriticele
  să rămână și numerele să fie scrise exact cum erau); JWT cu antet JSON care are „alg” și conținut obiect JSON; URL doar
  http/https cu o gazdă reală; culoarea doar cu „#” (iar „#RGB” doar cu o literă: „#123” e mai des un număr de issue); IPv4
  strict (fără zerouri în față, fără formele scurte pe care `IPAddress.TryParse` le acceptă); calea doar locală, cu literă de
  unitate; telefonul doar cu „+”, „00” sau 0 la început, fără puncte (deci nu IP-uri, sume sau date). Rezultatul
  (`SmartClip`) are forma normalizată calculată o dată; `SmartClipCache` ține ultimul text (după referință), ca widget-ul și
  registrul să nu-l analizeze din nou.
- **Curățarea URL** lucrează pe șir, nu pe un `Uri` refăcut: se împarte la primul „#” și la primul „?”, se scot doar
  segmentele al căror nume e în listă (orice `utm_…` și `fbclid`, `gclid`, `dclid`, `gbraid`, `wbraid`, `msclkid`, `mc_cid`,
  `mc_eid`, `yclid`, `igshid`, `igsh`, `_hsenc`, `_hsmi`, `mkt_tok`, `ref_src`, `twclid`, `ttclid`, `li_fat_id`), restul se
  lipește la loc neatins. Numele se compară **exact, cu litere mici** (`UTM_SOURCE`, `Fbclid` rămân: pot fi ai site-ului; a scoate
  mai puțin nu strică niciodată un link) și nedecodat. Nimic scos → același șir; nimic rămas → fără „?”.
- **Ultimul text copiat** e singurul pe care lucrează chip-urile și acțiunile: textul pe care istoricul tocmai l-a înregistrat
  (deci deja trecut prin verificarea formatelor private) sau pe care WinNotch l-a scris singur. Legăturile din
  `NotchWindow.xaml.cs` sunt de câte un rând: `SmartClipboardForget()` la începutul `OnClipboard` (o imagine sau un text privat
  golește rândul), `SmartClipboardCopied(t)` după înregistrare, `SmartClipboardOurs(text)` în `CopyToClipboard`,
  `StopSmartClipboard()` în `Cleanup`. Câmpul e `volatile`, citit de registru de pe orice fir.
- **Parolele:** verificarea care era scrisă în `IsPrivateClip` e acum `ClipboardPrivacy.IsPrivate` (pură, aceeași logică:
  „ExcludeClipboardContentFromMonitorProcessing” / „Clipboard Viewer Ignore” prezente, DWORD 0 în „CanIncludeInClipboardHistory” /
  „CanUploadToCloudClipboard”, orice excepție = privat), apelată de `IsPrivateClip` cu `d.GetDataPresent` / `d.GetData`. Un text
  privat iese din `OnClipboard` înainte de istoric și înainte de Smart Clipboard, deci nu e analizat niciodată.
- **Acțiunile** (`SmartClipboardActions`, zece, `clipboard.<verb>`): Safe, `FeatureId = "smart-clipboard"`, `RequiresUiThread`
  (clipboard-ul), **fără parametri** — conținutul nu trece prin registru, deci nu poate ajunge în log-ul lui, iar un apelant
  din afară nu poate da un text. Fiecare e disponibilă doar când ultimul text e de tipul ei și ar schimba ceva; la execuție
  textul e citit din nou. Rezultatul intră în clipboard prin `SmartClipboardWrite`, cu `_ignoreClip` (notificarea care urmează
  e sărită: fără intrare nouă în istoric, fără a doua recunoaștere, fără peek — **fără bucle**), iar chip-urile îl urmează.
  `clipboard.open-url` verifică din nou http/https și apelează `Shell.Open`; `clipboard.open-folder` refuză căile și unitățile
  de rețea (`DriveType.Network`), deschide doar un folder existent (pentru un fișier, folderul lui: fișierul nu e pornit).
  `clipboard.decode-jwt` copiază `{"header": …, "payload": …}` formatat; semnătura nu e verificată și nu e copiată.
- **Chip-urile** (`SmartClipChips`, un `Border` în widget-ul Clipboard, între titlu și listă): legătura e de trei rânduri în
  `ToolWidgets.cs` (câmpul, rândul nou `Auto` din grilă, `_smart.Refresh()` în `Refresh`). Ascuns (înălțime 0) cu comutatorul
  oprit sau pentru text simplu, deci widget-ul arată ca înainte. Se redesenează din `Refresh`-ul widget-ului (cam o dată pe
  secundă cât e vizibilă pagina; fără timer propriu) doar când s-a schimbat textul sau comutatorul. Stilul `GhostPill`,
  pensulele temei; mostra e culoarea copiată (`Ui.Rgb`), adică conținut, nu temă. Click → `ActionRegistry.Current.InvokeAsync(id,
  null, UI)`; rezultatul 3 s la capătul rândului (un `DispatcherTimer` oprit la prima tragere). Id-uri UI Automation `sc-<id acțiune>`.
- **Peek la copiere:** setarea `SmartClipboardPeek` (implicit oprită, Setări › Smart Clipboard, `settings.clipboard-peek`),
  doar cu Activity Manager pornit: `ActivityManager.Post`, Low, durata de peek, titlu fix (`PeekTitle`: „JSON copiat ·
  Formatează”, „Link cu urmărire copiat · Curăță”, „Token JWT copiat · Decodează”), doar unde chip-ul chiar ajută.
- **Log:** doar „Smart Clipboard: pornit.” și „Smart Clipboard: oprit (N recunoașteri).”; registrul scrie id-ul și rezultatul.
- **Testul de fum** rulează o singură dată (cu „activity-manager” oprit; cealaltă rulare scrie SKIP): comanda
  `smoke-clipboard-page on|off` (doar cu `--smoke`) adaugă / scoate o pagină cu widget-ul Clipboard, fiindcă paginile standard
  nu au widget-ul; testul copiază singur un JSON și verifică, prin UI Automation și clipboard, „Formatează” → JSON indentat
  echivalent, apoi „Compactează” și lipsa marcajului din `log.txt`.

## Alternative
- **Expresii regulate cu timeout:** mai scurte, dar tot ar trebui testate pe intrări patologice și ar putea expira
  (rezultat nesigur); parsarea manuală e liniară și ușor de citit.
- **Analiza conținutului curent al clipboard-ului, nu a ultimului text înregistrat:** ar fi cerut încă o citire a
  clipboard-ului (și a formatelor private) la fiecare redesenare; ultimul text e deja verificat și în memorie.
- **Textul ca parametru al acțiunilor:** ar fi trecut conținutul prin registru (limită de 200 de caractere, risc de log) și
  ar fi permis apelanților din afară să dea orice text.
- **Chip-uri pentru fiecare element din istoric:** mai mult cod și mai multă analiză pe redesenare; ultimul text acoperă cazul
  obișnuit („tocmai am copiat”).
- **Chip-urile și în lista paginii Unelte:** `ToolsPane` e altă listă; poate fi adăugat cu aceeași clasă, dar sarcina cere
  widget-ul, iar testul de fum îl acoperă doar pe el.
- **Decodarea JWT într-un panou / tooltip:** ar fi arătat conținutul pe ecran fără ca utilizatorul s-o ceară explicit; copierea
  ca text formatat e simplă și se vede doar unde lipești.
- **`UTM_SOURCE` scos și el:** posibil util, dar riscă să strice link-uri ale unor site-uri care au parametri proprii cu acele
  nume; am ales partea sigură.

## Consecințe
- Un tip nou = o metodă `TryX` pură în `SmartClipRecognizer`, locul ei în `Priority`, chip-urile în `ChipsFor` și o acțiune în
  `Create`, plus testele lui (recunoscut și fals). Un parametru de urmărire nou = un rând în `TrackingParameters` (SC9 numără lista).
- Orice funcție nouă care citește clipboard-ul trebuie să treacă prin `ClipboardPrivacy` (testul SC31 fixează ordinea din `OnClipboard`).
- Textele din log „Smart Clipboard: pornit.” / „oprit (…)”, comanda `smoke-clipboard-page`, id-urile `sc-*` și pagina
  `smoke-clipboard` sunt folosite de testul de fum.
- După o repornire nu există „ultimul text” până la prima copiere (clipurile fixate nu au chip-uri).

## Note după revizia R1
- **Liste JSON mici:** o listă e oferită doar dacă are înăuntru un obiect sau o listă, ori are cel puțin 8 caractere
  (`MinPlainArray`): „[1]” (notă de subsol) și „[1,2]” nu mai au chip-uri.
- **„::”** singur nu mai e IP: IPv6 cere cel puțin o cifră hex („::1” rămâne IP).
- **Link fără browser:** `Shell.Open` poate arunca `Win32Exception`; `OpenUrl` o prinde și întoarce „Link-ul nu a putut fi
  deschis.” (ca `OpenFolder`), deci nu se numără ca eroare a funcției. `ISmartClipboardHost.OpenUrl` întoarce acum motivul.
- **Folderul** e deschis cu `\` la final (`folder.TrimEnd('\\', '/') + "\\"`), ca să fie clar un director; „C:\” rămâne „C:\”.
- **O singură memorie a recunoașterii:** notch-ul își dă `SmartClipCache` acțiunilor (`Register(registry, host, cache)`), deci
  un text e analizat o dată pentru widget și registru.
- **Testul de fum:** `smoke-clipboard-page off` mută notch-ul pe Acasă dacă era pe pagina scoasă și o scoate și din `_userPanes`.
