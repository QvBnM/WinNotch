# ADR 0012 — Căști/boxe: lista ieșirilor prin NAudio, schimbarea prin `IPolicyConfig` izolat, oprire automată la prima eroare

- **Stare:** Acceptată
- **Data:** 2026-10-06
- **Sarcina din roadmap:** P30

## Context
P30 cere lista ieșirilor audio la click pe volum și acțiuni `audio.output.<dispozitiv>` care fac un dispozitiv ieșirea
implicită, prin `IPolicyConfig` (nedocumentat: try/catch, la prima eroare `FeatureFlags.Disable` cu mesaj), fără rutare per
aplicație, comutator „audio-switch” (Experimental). Constrângeri: Windows nu are un API public pentru ieșirea implicită
(doar pentru citire); nimic elevat, nicio pornire de proces; fără polling sub 2 s; UI doar pe Dispatcher; COM fără obiecte
nereleasate; numele dispozitivelor pot fi personale (nu în log); cu comutatorul oprit, nimic nou vizibil și nicio acțiune.
Cod existent: `Services/AudioService.cs` (volumul ieșirii implicite, `CheckDevice` o urmează), `Features/Context/WindowsSources.cs`
(`AudioOutputSource` + `EndpointWatcher`, care anunța doar schimbarea ieșirii implicite, pentru tipul ei: căști / boxe /
Bluetooth). Nu exista nicio listă a dispozitivelor.

## Decizie
- **Unde e lista:** un buton mic cu căști **lângă volumul de pe pagina Acasă** (slotul `HomePane.OutputSlot`, gol cu
  comutatorul oprit) deschide o listă mică, rotunjită (16), peste pagină (`OverlayHost`, ca raftul), cu pensulele temei.
  Alerta de volum și pastila închisă lasă click-urile să treacă (`WS_EX_TRANSPARENT`), deci „click pe volum” e controlul de
  volum din notch-ul deschis, nu alerta; afișarea nu trece prin Activity Manager, deci nu are două drumuri. Implicita are bifă,
  numele repetate sunt numerotate („Speakers (2)”), lista goală spune „Nicio ieșire audio”. Click → `ActionRegistry.Current.InvokeAsync`.
- **Lista:** NAudio (`MMDeviceEnumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)`, documentat), fiecare
  `MMDevice` eliberat imediat (`using`); implicita = `GetDefaultAudioEndpoint(Render, Multimedia)`, rolul folosit și de volum.
  O `COMException` la citire (fără serviciu audio: un server, mașina de CI) = listă goală, spus o dată în log.
- **Schimbarea:** `PolicyConfigSwitcher` (`Features/AudioSwitch/PolicyConfigSwitcher.cs`, singurul fișier cu COM pentru
  P30) implementează `IAudioEndpointSwitcher` (`List` / `GetDefault` / `SetDefault`). `IPolicyConfig` e declarat cu
  `[ComImport]` (IID `f8679f50-850a-41cf-9c72-430f290290c8`, Windows 7 → 11; metodele dinainte de `SetDefaultEndpoint` doar ca
  locuri în vtable), obiectul `PolicyConfigClient` (CLSID `870af99c-171d-4f9e-af0d-e63df40c2bc9`) e creat, folosit și eliberat
  (`Marshal.FinalReleaseComObject`) în același apel; orice HRESULT ≠ 0 aruncă.
- **Rolurile:** `SetDefaultEndpoint` pentru **eConsole, eMultimedia și eCommunications**, adică „Setează ca implicit” plus
  „Dispozitiv de comunicare implicit” din panoul Sunet. Motiv: cine trece pe căști vrea și apelurile pe căști (Teams / Zoom
  cu „implicit”); a lăsa comunicațiile pe boxe ar fi o surpriză. Cine vrea altfel le poate separa din panoul Sunet.
- **Firul COM:** toate apelurile (listă, schimbare, abonare, dezabonare) rulează pe un fir MTA din thread pool
  (`OnMta`: dacă firul curent e STA, de exemplu UI-ul, `Task.Run`). Obiectele sunt create și folosite pe același tip de
  apartament, deci fără proxy și fără așteptarea firului UI (un `Stop` din fundal nu se blochează în UI). Acțiunile nu cer
  firul UI (`RequiresUiThread = false`) și își fac lucrul cu `Task.Run`.
- **Refresh-ul:** `EndpointWatcher` (existent, partajat cu motorul de context) primește opțiunea `includeDeviceEvents`
  (adăugat / scos / stare); contextul îl folosește neschimbat. Callback-ul vine pe un fir COM și doar re-armează un
  `System.Threading.Timer` one-shot de 400 ms (fără lock, fără COM înapoi); tick-ul recitește lista. Fără polling. Dezabonarea
  (`UnregisterEndpointNotificationCallback`) la oprirea comutatorului și la ieșire.
- **Logica pură, testată** (`Features/AudioSwitch/AudioSwitch.cs`, fără NAudio / COM / WPF): `AudioOutputRules` (ordinea după
  nume apoi cheie, dublurile de id ignorate, numerotarea numelor, textele), `AudioSwitchService` (Start / Stop, debounce,
  `Select`, oprirea automată), `AudioOutputActions : IActionProvider` (cu `Changed` din serviciu). Teste AS1–AS24 cu un
  `IAudioEndpointSwitcher` fals.
- **Id-urile acțiunilor:** `audio.output-<cheie>`, cheia = primele 10 cifre hex din SHA-256 al id-ului de endpoint (cu
  majuscule): stabilă la fiecare pornire, scurtă, fără acoladele și punctele id-ului brut; „-2” la o coliziune. **Abatere de la
  plan:** forma `audio.output.<dispozitiv>` are două puncte și e refuzată de formatul registrului (`zonă.verb`, un singur
  punct), deci dispozitivul e legat cu o cratimă. Titlu „Ieșire audio: <nume>” („(implicită)” la cea de acum), Safe,
  `FeatureId = "audio-switch"`, aliasuri RO + EN („căști”, „boxe”, „headphones”, „speakers”…) și numele dispozitivului.
- **Oprirea automată:** înainte de `SetDefault`, lista e recitită: un dispozitiv scos între timp dă „Dispozitivul nu mai e
  conectat.” (nu e o eroare a interfeței). Prima excepție din `SetDefault` → `FeatureFlags.Current.Disable("audio-switch",
  "Windows a refuzat schimbarea ieșirii audio implicite (interfață nedocumentată).")` (motiv fix, 79 de caractere, niciodată
  `ex.Message`), o singură dată pe pornire, apoi evenimentul `Failed`. Notch-ul arată „Ieșirea audio nu a putut fi
  schimbată — „Căști/boxe” s-a oprit; o poți porni din nou din Setări › Funcții noi.” prin `Alert` (deci prin Activity
  Manager când e pornit, altfel calea veche); cu notch-ul deschis, mesajul stă în listă și alerta vine după închidere.
  Erorile de citire a listei merg la `ReportError` (3 în 10 minute opresc funcția).
- **Fără rutare per aplicație:** nu se folosesc `IAudioPolicyConfigFactory` și nici sesiunile per aplicație.
- **Testul de fum** (o dată, cu „activity-manager” oprit): comanda `smoke-audio-outputs` deschide lista pe Acasă (aceeași
  metodă ca butonul), lista trebuie să fie „Nicio ieșire audio” pe mașina fără audio (sau rânduri, cel mult o implicită),
  butonul real o închide și o redeschide, comutatorul nu se oprește singur. Nu alege nimic (ar schimba ieșirea mașinii).

## Alternative
- **Butonul pe alerta de volum sau pe pastila închisă:** sunt click-through; ar fi cerut o alertă interactivă nouă și ar fi
  schimbat comportamentul vechi al alertei de volum.
- **Doar eConsole + eMultimedia (ca „Setează ca implicit”):** apelurile ar fi rămas pe vechiul dispozitiv; mai puțin ce
  așteaptă cine „trece pe căști”.
- **Un watcher COM nou:** ar fi dublat `EndpointWatcher`; o opțiune în constructor l-a făcut refolosibil fără să schimbe contextul.
- **Polling al listei (de exemplu la 2 s):** interzis în standby; notificările Windows sunt suficiente.
- **Id-ul brut al endpoint-ului în id-ul acțiunii:** are acolade și puncte; un hash scurt e valid și stabil.
- **Apelul COM pe firul UI (STA):** ar fi blocat interfața și ar fi legat obiectele de firul UI (dezabonarea din fundal ar fi
  așteptat UI-ul).
- **`ReportError` (3 erori) în loc de oprirea la prima eroare:** o interfață nedocumentată care refuză o dată va refuza mereu;
  planul cere oprirea la prima.

## Consecințe
- `IPolicyConfig` rămâne în `PolicyConfigSwitcher.cs`; orice altă interfață nedocumentată viitoare se izolează la fel, cu
  oprire automată.
- Id-urile UI Automation `audio-outputs-*` / `audio-output-<cheie>` și comanda `smoke-audio-outputs` sunt folosite de testul de fum.
- Un dispozitiv redenumit își păstrează id-ul de acțiune (cheia vine din id-ul endpoint-ului, nu din nume); același
  dispozitiv pe alt port USB poate primi alt endpoint, deci altă cheie.
- Limită cunoscută: butonul e doar pe Acasă (și în Command Bar); paginile tale nu au un widget pentru el.

## Note după revizia R1
- **Dispozitiv scos chiar în timpul schimbării:** dacă `SetDefault` aruncă, lista e recitită; când dispozitivul nu mai e în
  ea, rezultatul e „Dispozitivul nu mai e conectat.”, fără oprire automată (AS22). Doar o eroare pe un dispozitiv încă prezent
  oprește funcția.
- **Fără scurtătura „e deja ieșirea”:** implicita e citită doar pentru Multimedia, iar comunicațiile pot fi pe alt
  dispozitiv; `SetDefault` (toate trei rolurile) e chemat mereu, fiind idempotent (AS8).
- **Oprirea din UI nu așteaptă COM-ul:** comutatorul oprit golește lista imediat, iar dezabonarea (`OnMta`, sincronă) rulează
  în fundal (`Stop(wait: false)`). Abonarea are generația pornirii care a făcut-o: o dezabonare întârziată nu atinge abonarea
  unei porniri mai noi (care o preia). Doar `Cleanup` la ieșire oprește sincron și termină o dezabonare rămasă (AS23).
- **Numele ilizibil:** orice excepție la `FriendlyName` dă numele generic „Ieșire audio fără nume”.
- **Microfoanele:** cu `includeDeviceEvents`, `EndpointWatcher` ignoră evenimentele endpoint-urilor de captură după forma
  id-ului („{0.0.1.…}”, fără COM în callback; orice altă formă contează ca ieșire). Fără opțiune (motorul de context),
  comportamentul e identic (AS24).
- **Debounce-ul injectabil:** `IAudioDebounce` (în aplicație `TimerDebounce`, 400 ms); testul AS16 îl declanșează de mână, fără ceas.
