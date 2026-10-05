// Runs extension/background.js with a fake chrome API and a fake WebSocket.
const fs = require("fs"), vm = require("vm");
const src = fs.readFileSync(require("path").join(__dirname, "..", "..", "extension", "background.js"), "utf8");
const sent = [], scripts = [], updates = [];
let tabs = [
  { id: 1, title: "(1) A - YouTube", url: "https://www.youtube.com/watch?v=x", audible: true, mutedInfo: { muted: false }, windowId: 9 },
  { id: 2, title: "B", url: "https://music.youtube.com/watch?v=y", audible: false, mutedInfo: { muted: false }, windowId: 9 },
  { id: 3, title: "Bank", url: "https://bank.example/account", audible: false, mutedInfo: { muted: false }, windowId: 9 }
];
const listeners = {};
const ev = name => ({ addListener: f => (listeners[name] = f) });
class FakeWS { constructor(u) { this.url = u; this.readyState = 0; FakeWS.last = this; setTimeout(() => { this.readyState = 1; this.onopen && this.onopen(); }, 5); }
  send(d) { sent.push(JSON.parse(d)); } close() { this.closed = true; } }
FakeWS.OPEN = 1; FakeWS.CONNECTING = 0;
const chrome = {
  tabs: { query: async () => tabs, update: async (id, p) => { updates.push([id, p]); return tabs.find(t => t.id === id); },
          onUpdated: ev("onUpdated"), onRemoved: ev("onRemoved") },
  windows: { update: async () => {} },
  scripting: { executeScript: async o => { scripts.push(o); } },
  alarms: { create: () => {}, onAlarm: ev("onAlarm") },
  runtime: { onMessage: ev("onMessage"), onStartup: ev("onStartup"), onInstalled: ev("onInstalled"), getURL: p => "chrome-extension://id/" + p },
  storage: { local: { _d: {}, get: async k => ({ [k]: chrome.storage.local._d[k] }), set: async o => Object.assign(chrome.storage.local._d, o) } }
};
const fetch = async url => ({ json: async () => ({ token: "T0KEN" }) });
const timers = [];
const ctx = { chrome, fetch, crypto: require("crypto").webcrypto, TextEncoder, btoa, WebSocket: FakeWS, navigator: { userAgentData: { brands: [{ brand: "Google Chrome" }] } }, console,
  setTimeout, clearTimeout, setInterval: () => 0, Date, JSON, Math, Number, Object, Array, String };
vm.createContext(ctx); vm.runInContext(src, ctx);
const wait = ms => new Promise(r => setTimeout(r, ms));
let pass = 0, fail = 0;
const check = (id, name, ok, d = "") => { ok ? pass++ : fail++; console.log((ok ? "PASS" : "FAIL") + "  " + id + "  " + name + (ok ? "" : "  -> " + d)); };
(async () => {
  await wait(30);
  const H = t => require("crypto").createHmac("sha256", "T0KEN").update(t).digest("hex");
  check("E15", "Înainte ca WinNotch să-și dovedească identitatea nu trimite nimic (nici token, nici tab-uri)", sent.length === 0, JSON.stringify(sent));
  // a fake server (port squatting) that can't prove it knows the token
  const n0 = "a".repeat(64);
  FakeWS.last.onmessage({ data: JSON.stringify({ t: "challenge", n: n0 }) });
  await wait(30);
  const h0 = sent.find(m => m.t === "hello");
  FakeWS.last.onmessage({ data: JSON.stringify({ t: "welcome", p: "0".repeat(64) }) });
  await wait(30);
  check("E16", "Server fals (fără token): conexiunea e închisă, nu primește niciun tab", FakeWS.last.closed === true && !sent.some(m => m.t === "tabs"));
  // the real WinNotch
  FakeWS.last.onclose();
  await wait(4300);
  const n = "b".repeat(64);
  FakeWS.last.onmessage({ data: JSON.stringify({ t: "challenge", n }) });
  await wait(30);
  const hello = sent.filter(m => m.t === "hello").pop();
  FakeWS.last.onmessage({ data: JSON.stringify({ t: "welcome", p: H("server|" + n + "|" + hello.cn) }) });
  await wait(80);
  check("E1", "Se conectează la 127.0.0.1:47811 și se prezintă ca chrome", FakeWS.last.url === "ws://127.0.0.1:47811/" && hello && hello.browser === "chrome");
  check("E13", "Dovedește token-ul cu HMAC (fără să-l trimită) și are un id de profil", hello && !("token" in hello) && hello.proof === H("client|" + n + "|" + hello.cn) && h0 && !("token" in h0) && typeof hello.inst === "string" && hello.inst.length > 10, JSON.stringify(hello));
  let last = sent.filter(m => m.t === "tabs").pop();
  check("E2", "Trimite doar tab-urile care se aud (nu toate tab-urile)", last && last.tabs.length === 1 && last.tabs[0].id === 1, JSON.stringify(last));
  check("E3", "Tab-ul cu banca (fără sunet) nu e trimis deloc", !JSON.stringify(sent).includes("bank.example"));
  // content script reports media in tab 2 (top frame) and an ad frame in tab 1
  listeners.onMessage({ t: "media", state: { playing: true, title: "Piesa", artist: "X", art: "https://i.ytimg.com/a.jpg", duration: 180, position: 3 } }, { tab: { id: 2 }, frameId: 0 });
  await wait(80);
  last = sent.filter(m => m.t === "tabs").pop();
  const t2 = last.tabs.find(t => t.id === 2);
  check("E4", "Play raportat de pagină apare imediat (fără întârzierea Chrome)", t2 && t2.media && t2.media.playing === true && t2.media.title === "Piesa", JSON.stringify(t2));
  listeners.onMessage({ t: "media", state: { playing: false, title: "Piesa", artist: "X", art: "", duration: 180, position: 9 } }, { tab: { id: 2 }, frameId: 0 });
  await wait(80);
  last = sent.filter(m => m.t === "tabs").pop();
  check("E5", "Pauza raportată de pagină apare imediat", last.tabs.find(t => t.id === 2).media.playing === false);
  // stale frame: playing but silent > 9 s
  listeners.onMessage({ t: "media", state: { playing: true, title: "Ad", artist: "", art: "", duration: 15, position: 1 } }, { tab: { id: 3 }, frameId: 7 });
  const realNow = Date.now; ctx.Date.now = () => realNow() + 10000;
  listeners.onAlarm({});
  await wait(80);
  ctx.Date.now = realNow;
  last = sent.filter(m => m.t === "tabs").pop();
  const t3 = last.tabs.find(t => t.id === 3);
  check("E6", "Un frame care a tăcut >9 s (reclamă scoasă) nu mai e „playing”", !t3 || !t3.media || t3.media.playing === false, JSON.stringify(t3));
  // commands
  FakeWS.last.onmessage({ data: JSON.stringify({ t: "mute", id: 1, on: true }) });
  FakeWS.last.onmessage({ data: JSON.stringify({ t: "pause", id: 1 }) });
  FakeWS.last.onmessage({ data: JSON.stringify({ t: "vol", id: 1, v: 5 }) });
  FakeWS.last.onmessage({ data: "nu e json" });
  await wait(50);
  check("E7", "Comanda mute ajunge la tab", updates.some(u => u[0] === 1 && u[1].muted === true));
  check("E8", "Pauza rulează scriptul în pagina tab-ului", scripts.some(s => s.target.tabId === 1 && s.func.name === "pausePage"));
  const volScript = scripts.find(s => s.func.name === "setPageVolume");
  check("E9", "Volumul e limitat la 0..1 (5 → 1)", volScript && volScript.args[0] === 1, JSON.stringify(volScript && volScript.args));
  FakeWS.last.onmessage({ data: JSON.stringify({ t: "mute", id: 2, on: true }) });
  await wait(30);
  check("E10", "După un mesaj invalid, comenzile următoare tot merg", updates.some(u => u[0] === 2 && u[1].muted === true));
  const before = scripts.length;
  FakeWS.last.onmessage({ data: JSON.stringify({ t: "eval", id: 1, code: "alert(1)" }) });
  await wait(30);
  check("E11", "Comandă necunoscută (ex. „eval”) nu rulează nimic", scripts.length === before);
  // reconnect backoff
  const first = FakeWS.last;
  first.onclose();
  await wait(1000);
  const soon = FakeWS.last === first;
  await wait(3300);
  check("E12", "Reconectare automată după ~4 s (nu imediat) când WinNotch se închide", soon && FakeWS.last !== first);
  // back-off also holds for events that ask for a connection (tab updates, media reports)
  await wait(50);
  const second = FakeWS.last;
  second.onclose();
  listeners.onUpdated(1, { audible: true });
  listeners.onMessage({ t: "media", state: { playing: true, title: "x", artist: "", art: "", duration: 1, position: 0 } }, { tab: { id: 1 }, frameId: 0 });
  await wait(300);
  check("E14", "Evenimentele din tab-uri nu ocolesc pauza de reconectare", FakeWS.last === second);
  console.log(`\nTOTAL ${pass + fail}: ${pass} PASS, ${fail} FAIL`); process.exitCode = fail ? 1 : 0;
  process.exit(fail ? 1 : 0);
})();
