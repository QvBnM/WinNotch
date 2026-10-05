
const fs = require("fs"), vm = require("vm");
const src = fs.readFileSync(require("path").join(__dirname, "..", "..", "extension", "content.js"), "utf8");
const handlers = {}; const sent = [];
const video = { paused: false, ended: false, muted: true, volume: 1, currentTime: 3, duration: 60 };
Object.setPrototypeOf(video, function HTMLMediaElement() {}.prototype);
class HTMLMediaElement {}
Object.setPrototypeOf(video, HTMLMediaElement.prototype);
const document = { addEventListener: (ev, f) => (handlers[ev] = f), querySelectorAll: () => [video] };
const window = { addEventListener: () => {} }; window.top = window;
const ctx = { document, window, navigator: {}, HTMLMediaElement, chrome: { runtime: { sendMessage: m => sent.push(m) } },
  setTimeout, clearTimeout, setInterval: () => 0, Date, Array, String, Math, parseInt, isFinite };
vm.createContext(ctx); vm.runInContext(src, ctx);
let pass = 0, fail = 0;
const check = (id, n, ok, d = "") => { ok ? pass++ : fail++; console.log((ok ? "PASS" : "FAIL") + "  " + id + "  " + n + (ok ? "" : "  -> " + d)); };
const wait = ms => new Promise(r => setTimeout(r, ms));
(async () => {
  handlers.play({ target: video }); await wait(100);
  check("E15", "Video pe mut care rulează singur (previzualizare) NU e raportat ca „se aude”", sent.length === 0 || sent[sent.length - 1].state.playing === false, JSON.stringify(sent));
  video.muted = false; handlers.volumechange({ target: video }); await wait(100);
  check("E16", "Când îi dai sunet, e raportat imediat ca „se aude”", sent.length > 0 && sent[sent.length - 1].state.playing === true, JSON.stringify(sent));
  video.paused = true; handlers.pause({ target: video }); await wait(100);
  const n = sent.length;
  handlers.timeupdate({ target: video }); handlers.timeupdate({ target: video });
  check("E17", "Pe pauză și fără schimbări nu mai trimite nimic", sent.length === n && sent[n - 1].state.playing === false);
  console.log(`\nTOTAL ${pass + fail}: ${pass} PASS, ${fail} FAIL`); process.exitCode = fail ? 1 : 0;
})();
