// next / previous inside the page: the page's media-key handlers first, visible buttons next, seeking last.
const test = require("node:test"), assert = require("node:assert");
const fs = require("fs"), vm = require("vm"), path = require("path");
const src = fs.readFileSync(path.join(__dirname, "..", "..", "extension", "background.js"), "utf8").replace(/\r\n/g, "\n");   // Windows checkouts use CRLF
const pick = name => src.slice(src.indexOf("function " + name + "("), src.indexOf("\n}\n", src.indexOf("function " + name + "(")) + 3);

function page({ playing = true, time = 30, handlers = {}, buttons = {} }) {
  const media = { paused: !playing, currentTime: time, duration: 200 };
  const calls = [];
  const document = { querySelectorAll: s => s === "video, audio" ? [media] :
    (buttons[s] ? [{ offsetParent: buttons[s].visible ? {} : null, getAttribute: () => null, click: () => calls.push("btn:" + s) }] : []) };
  const window = {};
  window.top = window;
  window.__winnotchMS = Object.fromEntries(Object.entries(handlers).map(([k]) => [k, () => calls.push("handler:" + k)]));
  const ctx = { document, window, isFinite, Array, Math, location: { hostname: "www.youtube.com", pathname: "/watch" }, history: { length: 1 } };
  vm.createContext(ctx);
  vm.runInContext(pick("nextInPage") + pick("prevInPage"), ctx);
  return { ctx, media, calls };
}

test("Next folosește handler-ul paginii (ca tasta media), nu sare 10 s", () => {
  const p = page({ handlers: { nexttrack: 1 } });
  p.ctx.nextInPage();
  assert.deepStrictEqual(p.calls, ["handler:nexttrack"]);
  assert.strictEqual(p.media.currentTime, 30);
});
test("Next ignoră butoanele ascunse și apasă doar unul vizibil", () => {
  const hidden = page({ buttons: { ".ytp-next-button": { visible: false } } });
  hidden.ctx.nextInPage();
  assert.ok(!hidden.calls.length && hidden.media.currentTime === 40, "fără buton vizibil: +10 s");
  const shown = page({ buttons: { ".ytp-next-button": { visible: true } } });
  shown.ctx.nextInPage();
  assert.deepStrictEqual(shown.calls, ["btn:.ytp-next-button"]);
});
test("Previous: după 3 s repornește piesa, la început trece la cea anterioară", () => {
  const mid = page({ time: 30, handlers: { previoustrack: 1 } });
  mid.ctx.prevInPage();
  assert.ok(mid.media.currentTime === 0 && !mid.calls.length);
  const start = page({ time: 1, handlers: { previoustrack: 1 } });
  start.ctx.prevInPage();
  assert.deepStrictEqual(start.calls, ["handler:previoustrack"]);
});
test("Un frame fără media care cântă nu face nimic", () => {
  const p = page({ playing: false, time: 0, handlers: {} });
  p.ctx.nextInPage();
  assert.strictEqual(p.media.currentTime, 0);
});
