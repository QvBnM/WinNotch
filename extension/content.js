// WinNotch: tells the extension the moment audio/video in this page starts or stops, plus title, cover and position.
// Chrome's own "tab is playing" flag lags 2-3 seconds; media events are instant.
(() => {
  let played = false;
  let lastSent = "";
  let lastPosAt = 0;
  let timer = null;

  // Heard = playing, not muted, volume above 0. Muted autoplay (YouTube hover previews, feed videos) doesn't count.
  const audible = m => !m.paused && !m.ended && !m.muted && m.volume > 0;

  function mediaEls() {
    return Array.from(document.querySelectorAll("video, audio"));
  }

  function main() {
    const els = mediaEls();
    const playing = els.filter(audible);
    if (playing.length) return playing.sort((a, b) => (b.duration || 0) - (a.duration || 0))[0];
    return els.filter(m => m.currentTime > 0).sort((a, b) => (b.duration || 0) - (a.duration || 0))[0] || null;
  }

  function art(md) {
    try {
      const list = (md && md.artwork) ? Array.from(md.artwork) : [];
      let best = null, bestSize = -1;
      for (const a of list) {
        const size = parseInt(String(a.sizes || "0").split("x")[0], 10) || 0;
        if (size > bestSize) { best = a.src; bestSize = size; }
      }
      return best || "";
    } catch (e) { return ""; }
  }

  function report(force) {
    const m = main();
    if (!m && !played) return;
    const isPlaying = !!(m && audible(m));
    if (isPlaying) played = true;
    let md = null;
    try { md = navigator.mediaSession && navigator.mediaSession.metadata; } catch (e) {}
    const state = {
      playing: isPlaying,
      title: (md && md.title) || "",
      artist: (md && md.artist) || "",
      art: window === window.top ? art(md) : "",        // covers only from the page itself, not from embedded frames (ads)
      duration: m && isFinite(m.duration) && m.duration > 0 ? Math.min(m.duration, 604800) : 0,      // at most 7 days
      position: m ? m.currentTime : 0
    };
    const key = [state.playing, state.title, state.artist, state.art, Math.round(state.duration)].join("|");
    const now = Date.now();
    // Same state: only resync the position every few seconds (WinNotch extrapolates in between).
    // Paused and unchanged: nothing to send (keeps the browser and WinNotch idle).
    if (!force && key === lastSent && (!isPlaying || now - lastPosAt < 5000)) return;
    lastSent = key;
    lastPosAt = now;
    try { chrome.runtime.sendMessage({ t: "media", state }); } catch (e) { /* extension reloaded */ }
  }

  function soon(force) {
    clearTimeout(timer);
    timer = setTimeout(() => report(force), 60);
  }

  for (const ev of ["play", "playing", "pause", "ended", "emptied", "loadedmetadata", "seeked", "durationchange", "volumechange"])
    document.addEventListener(ev, e => { if (e.target instanceof HTMLMediaElement) soon(true); }, true);
  document.addEventListener("timeupdate", e => { if (e.target instanceof HTMLMediaElement) report(false); }, true);
  // Leaving the page (or an embedded player being removed): it no longer plays.
  window.addEventListener("pagehide", () => {
    if (!played) return;
    try { chrome.runtime.sendMessage({ t: "media", state: { playing: false, title: "", artist: "", art: "", duration: 0, position: 0 } }); } catch (e) {}
  });
  // Titles change without media events on single-page sites (YouTube going to the next video).
  setInterval(() => { if (played) report(false); }, 3000);
})();
