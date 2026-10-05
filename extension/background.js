// WinNotch: reports the tabs that play sound to the WinNotch app on this PC (ws://127.0.0.1:47811, local only)
// and carries out its commands: mute, pause/play, volume, go to tab. Nothing leaves the computer.
const URL = "ws://127.0.0.1:47811/";
const VERSION = "1.6";
let ws = null;
let pushTimer = null;
let retryTimer = null;
let retryDelay = 4000;
let nextTry = 0;
let TOKEN = "";        // written by WinNotch into this folder; proves to the app that this is its own extension
let INSTANCE = "";     // one per browser profile, so two Chrome profiles don't push each other off
let TRUSTED = false;   // the app proved it knows the token too (see onChallenge); until then nothing is sent or obeyed
let clientNonce = "";
let serverNonce = "";

// Mutual proof without ever sending the token: both sides sign the two random nonces with it (HMAC-SHA-256).
// A program that just listens on the port (WinNotch closed) gets neither the token nor any tab.
async function hmac(text) {
  const key = await crypto.subtle.importKey("raw", new TextEncoder().encode(TOKEN), { name: "HMAC", hash: "SHA-256" }, false, ["sign"]);
  const sig = new Uint8Array(await crypto.subtle.sign("HMAC", key, new TextEncoder().encode(text)));
  return Array.from(sig, b => b.toString(16).padStart(2, "0")).join("");
}
function randomHex(n) {
  return Array.from(crypto.getRandomValues(new Uint8Array(n)), b => b.toString(16).padStart(2, "0")).join("");
}
async function onChallenge(msg) {
  if (typeof msg.n !== "string" || !/^[0-9a-f]{64}$/.test(msg.n)) { try { ws.close(); } catch (e) {} return; }
  serverNonce = msg.n;
  clientNonce = randomHex(32);
  const proof = await hmac("client|" + serverNonce + "|" + clientNonce);
  send({ t: "hello", browser: BROWSER, inst: INSTANCE, v: VERSION, cn: clientNonce, proof });
}
async function onWelcome(msg) {
  const expected = await hmac("server|" + serverNonce + "|" + clientNonce);
  if (typeof msg.p !== "string" || msg.p !== expected) { try { ws.close(); } catch (e) {} return; }
  TRUSTED = true;
  sentArt.clear();
  push();
}

async function identity() {
  if (!TOKEN) {
    try { TOKEN = (await (await fetch(chrome.runtime.getURL("token.json"))).json()).token || ""; } catch (e) { TOKEN = ""; }
  }
  if (!INSTANCE) {
    try {
      const st = await chrome.storage.local.get("instance");
      INSTANCE = st.instance || crypto.randomUUID();
      if (!st.instance) await chrome.storage.local.set({ instance: INSTANCE });
    } catch (e) { INSTANCE = crypto.randomUUID(); }
  }
}
const paused = new Set();          // tabs paused from WinNotch (still listed so they can be resumed)
const volumes = {};                 // tabId -> 0..1
const media = {};                   // tabId -> { frameId -> state reported by content.js, at }

function tabMedia(tabId) {
  const frames = media[tabId];
  if (!frames) return null;
  // A frame that plays re-reports at least every 5 s; one silent for longer was removed (an ad, an embedded player).
  const now = Date.now();
  for (const [k, f] of Object.entries(frames)) if (f.state.playing && now - f.at > 9000) delete frames[k];
  const all = Object.values(frames);
  if (!all.length) return null;
  const pick = all.find(f => f.state.playing) || all.sort((a, b) => b.at - a.at)[0];
  return { ...pick.state, age: Date.now() - pick.at };
}

function browserName() {
  const brands = (navigator.userAgentData && navigator.userAgentData.brands || []).map(b => b.brand);
  const has = s => brands.some(b => b.includes(s));
  if (has("Edge")) return "msedge";
  if (has("Opera")) return "opera";
  if (has("Brave")) return "brave";
  if (has("Vivaldi")) return "vivaldi";
  return "chrome";
}
const BROWSER = browserName();

async function connect(force) {
  if (ws && (ws.readyState === WebSocket.OPEN || ws.readyState === WebSocket.CONNECTING)) return;
  if (!force && Date.now() < nextTry) return;          // respect the back-off, whatever asked for the connection
  nextTry = Date.now() + 1000;
  await identity();
  if (!TOKEN) return;                                  // no token yet: WinNotch hasn't written its files
  try { ws = new WebSocket(URL); } catch (e) { ws = null; return; }
  TRUSTED = false;
  ws.onopen = () => { retryDelay = 4000; };            // the app speaks first (challenge)
  ws.onmessage = e => {
    let msg;
    try { msg = JSON.parse(e.data); } catch (err) { return; }
    if (msg.t === "challenge" && !TRUSTED) { onChallenge(msg); return; }
    if (msg.t === "welcome" && !TRUSTED) { onWelcome(msg); return; }
    if (TRUSTED) { try { handle(msg); } catch (err) {} }
  };
  // WinNotch closed (or rebuilding): try again later, less and less often, so Chrome's error list stays short.
  ws.onclose = () => {
    ws = null;
    if (!TRUSTED) TOKEN = "";          // the handshake failed: read token.json again next time (it may have been renewed)
    TRUSTED = false;
    nextTry = Date.now() + retryDelay;
    clearTimeout(retryTimer);
    retryTimer = setTimeout(() => connect(true), retryDelay);
    retryDelay = Math.min(retryDelay * 2, 60000);
  };
  ws.onerror = () => {};
}

function send(obj) {
  if (ws && ws.readyState === WebSocket.OPEN && (TRUSTED || obj.t === "hello")) ws.send(JSON.stringify(obj));
}

function schedulePush() {
  clearTimeout(pushTimer);
  pushTimer = setTimeout(push, 40);
}

async function push() {
  if (!ws || ws.readyState !== WebSocket.OPEN || !TRUSTED) return;
  const all = await chrome.tabs.query({});
  const tabs = all
    .filter(t => t.audible || paused.has(t.id) || media[t.id])
    .map(t => ({
      id: t.id,
      title: t.title || "",
      url: t.url || t.pendingUrl || "",
      audible: !!t.audible,
      muted: !!(t.mutedInfo && t.mutedInfo.muted),
      paused: paused.has(t.id) && !t.audible,
      vol: volumes[t.id] ?? 1,
      media: tabMedia(t.id) || undefined
    }));
  send({ t: "tabs", browser: BROWSER, inst: INSTANCE, v: VERSION, tabs });
  for (const t of tabs) sendArt(t);
}

// Covers: the browser downloads them (through its own proxy / VPN, like the page itself would) and hands WinNotch
// the bytes, so WinNotch never connects to addresses chosen by web pages. Only PNG / JPEG / WebP, at most 300 KB.
const sentArt = new Set();
function youTubeThumb(url) {
  // the same rule as WinNotch (NetSafety.YouTubeThumb), so both name the cover with the same URL
  const m = /(?:youtube\.com\/(?:watch\?(?:.*&)?v=|shorts\/|live\/)|youtu\.be\/)([A-Za-z0-9_-]{11})/.exec(url || "");
  return m ? "https://i.ytimg.com/vi/" + m[1] + "/mqdefault.jpg" : "";
}
async function sendArt(t) {
  const art = (t.media && t.media.art) || youTubeThumb(t.url);
  if (!art || !art.startsWith("https://") || art.length > 2048 || sentArt.has(art)) return;
  sentArt.add(art);                    // also stops a second fetch while this one runs; removed again if it fails
  if (sentArt.size > 50) sentArt.clear();   // WinNotch keeps the latest 60: never assume more is still there
  try {
    const r = await fetch(art, { credentials: "omit", referrerPolicy: "no-referrer", cache: "force-cache" });
    const type = (r.headers.get("content-type") || "").split(";")[0].trim();
    if (!r.ok || !["image/png", "image/jpeg", "image/webp"].includes(type)) { sentArt.delete(art); return; }
    const buf = new Uint8Array(await r.arrayBuffer());
    if (buf.length === 0 || buf.length > 300 * 1024) return;      // too big: don't try again
    let bin = "";
    for (let i = 0; i < buf.length; i += 8192) bin += String.fromCharCode.apply(null, buf.subarray(i, i + 8192));
    send({ t: "art", url: art, data: btoa(bin) });
  } catch (e) { sentArt.delete(art); /* offline, blocked by the site: try again later */ }
}

async function run(tabId, func, args) {
  try {
    await chrome.scripting.executeScript({ target: { tabId, allFrames: true }, func, args: args || [], world: "MAIN" });
  } catch (e) { /* chrome:// pages, closed tabs */ }
}

function pausePage() {
  document.querySelectorAll("video, audio").forEach(m => {
    if (!m.paused) { m.pause(); m.dataset.winnotchPaused = "1"; }
  });
}
function playPage() {
  const marked = document.querySelectorAll("[data-winnotch-paused]");
  if (marked.length) marked.forEach(m => { delete m.dataset.winnotchPaused; m.play().catch(() => {}); });
  else { const m = document.querySelector("video, audio"); if (m) m.play().catch(() => {}); }
}
// These run in every frame of the tab: only the frame whose media plays acts. First choice: the page's own
// "next / previous track" handlers (what the keyboard's media keys call); then a visible player button; last, a seek.
function nextInPage() {
  const all = Array.from(document.querySelectorAll("video, audio"));
  const m = all.find(x => !x.paused) || all.find(x => x.currentTime > 0);
  const h = window.__winnotchMS && window.__winnotchMS.nexttrack;
  if (!m && !(h && window === window.top)) return;
  if (h) { try { h({ action: "nexttrack" }); return; } catch (e) {} }
  const seen = s => Array.from(document.querySelectorAll(s)).find(b => b.offsetParent !== null && b.getAttribute("aria-disabled") !== "true");
  const b = seen(".ytp-next-button") || seen("ytmusic-player-bar .next-button") || seen('[data-testid="control-button-skip-forward"]') ||
            seen('button[aria-label*="Next" i]') || seen('button[aria-label*="Următor" i]');
  if (b) { b.click(); return; }
  if (m && isFinite(m.duration)) m.currentTime = Math.min(m.duration - 0.2, m.currentTime + 10);
}
function prevInPage() {
  const all = Array.from(document.querySelectorAll("video, audio"));
  const m = all.find(x => !x.paused) || all.find(x => x.currentTime > 0);
  const h = window.__winnotchMS && window.__winnotchMS.previoustrack;
  if (!m && !(h && window === window.top)) return;
  // Like every player: past the first 3 seconds "previous" restarts the song; pressed again, it goes back.
  if (m && m.currentTime > 3) { m.currentTime = 0; return; }
  if (h) { try { h({ action: "previoustrack" }); return; } catch (e) {} }
  const seen = s => Array.from(document.querySelectorAll(s)).find(b => b.offsetParent !== null && b.getAttribute("aria-disabled") !== "true");
  const b = seen(".ytp-prev-button") || seen("ytmusic-player-bar .previous-button") || seen('[data-testid="control-button-skip-back"]') ||
            seen('button[aria-label*="Previous" i]') || seen('button[aria-label*="Anterior" i]');
  if (b) { b.click(); return; }
  if (m) m.currentTime = 0;
}
function seekInPage(v) {
  const m = Array.from(document.querySelectorAll("video, audio")).find(x => !x.paused) ||
            Array.from(document.querySelectorAll("video, audio")).find(x => x.currentTime > 0);
  if (m) m.currentTime = v;
}
function setPageVolume(v) {
  document.querySelectorAll("video, audio").forEach(m => { m.volume = v; });
}

async function handle(msg) {
  const id = msg.id;
  switch (msg.t) {
    case "mute":
      await chrome.tabs.update(id, { muted: !!msg.on }).catch(() => {});
      break;
    case "pause":
      paused.add(id);
      await run(id, pausePage);
      break;
    case "play":
      paused.delete(id);
      await run(id, playPage);
      break;
    case "vol":
      volumes[id] = Math.max(0, Math.min(1, Number(msg.v)));
      await run(id, setPageVolume, [volumes[id]]);
      break;
    case "next":
      await run(id, nextInPage);
      break;
    case "prev":
      await run(id, prevInPage);
      break;
    case "seek":
      await run(id, seekInPage, [Number(msg.v) || 0]);
      break;
    case "focus": {
      const tab = await chrome.tabs.update(id, { active: true }).catch(() => null);
      if (tab) await chrome.windows.update(tab.windowId, { focused: true }).catch(() => {});
      break;
    }
  }
  schedulePush();
}

chrome.tabs.onUpdated.addListener((tabId, info) => {
  if (info.status === "loading" && media[tabId] && "url" in info) delete media[tabId];   // a different page
  if ("audible" in info || "mutedInfo" in info || "title" in info || "url" in info) {
    if (info.audible === true) paused.delete(tabId);
    // a new page in the same tab: keep the user's volume for it
    if ("url" in info && volumes[tabId] !== undefined && volumes[tabId] !== 1) setTimeout(() => run(tabId, setPageVolume, [volumes[tabId]]), 1500);
    connect();
    schedulePush();
  }
});
chrome.tabs.onRemoved.addListener(tabId => { paused.delete(tabId); delete volumes[tabId]; delete media[tabId]; schedulePush(); });

// Instant play / pause / title from the pages.
chrome.runtime.onMessage.addListener((msg, sender) => {
  if (!msg || msg.t !== "media" || !sender.tab) return;
  const tabId = sender.tab.id;
  (media[tabId] = media[tabId] || {})[sender.frameId || 0] = { state: msg.state, at: Date.now() };
  if (msg.state.playing) paused.delete(tabId);
  connect();
  schedulePush();
});

// Keeps trying to reach WinNotch (e.g. when the app starts after the browser).
chrome.alarms.create("winnotch", { periodInMinutes: 0.5 });
chrome.alarms.onAlarm.addListener(() => { connect(); push(); });
chrome.runtime.onStartup.addListener(() => connect(true));
chrome.runtime.onInstalled.addListener(() => connect(true));

// Talking every 20 s keeps the extension awake while connected.
setInterval(() => send({ t: "ping" }), 20000);
connect(true);
