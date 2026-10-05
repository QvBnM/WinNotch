// Runs inside the page itself (MAIN world), before the page's own scripts. Remembers the handlers the page registers
// for the media keys (next / previous track), so WinNotch's buttons do exactly what the keyboard's media keys do:
// next video on YouTube, next song on YouTube Music, Spotify, SoundCloud... instead of guessing buttons.
(() => {
  try {
    const ms = navigator.mediaSession;
    if (!ms || !ms.setActionHandler || window.__winnotchMS) return;
    const handlers = {};
    Object.defineProperty(window, "__winnotchMS", { value: handlers, enumerable: false });
    const orig = ms.setActionHandler.bind(ms);
    ms.setActionHandler = function (action, handler) {
      if (action === "nexttrack" || action === "previoustrack") handlers[action] = handler || null;
      return orig(action, handler);
    };
  } catch (e) { /* the page locked mediaSession: buttons fall back to the player's own controls */ }
})();
