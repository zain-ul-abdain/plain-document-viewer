// Plain Viewer picture page. Runs inside the same locked-down WebView2 as the PDF view. The picture's bytes come from
// the app (already identified by their content); this page only lays the picture out: zoom, fit, rotate.
const DOC = "https://doc.plainviewer.invalid";
const host = window.chrome?.webview;
const post = message => host?.postMessage(message);
const scroller = document.getElementById("scroller");
const stage = document.getElementById("stage");
const picture = document.getElementById("picture");
document.documentElement.dataset.theme = new URLSearchParams(location.search).get("theme") === "dark" ? "dark" : "light";

let width = 0, height = 0;        // the picture's own size in pixels
let scale = 1, rotation = 0;      // rotation in quarter turns, clockwise
// A drawing (SVG) scales without losing detail, so it opens fitted to the window; a photo is never enlarged at first.
const vector = new URLSearchParams(location.search).has("vector");
let mode = vector ? "page-fit" : "auto";  // "auto" (fit, never enlarged), "page-fit", "page-width" or "fixed"
let loaded = false;

const turned = () => rotation % 2 === 1;
const shownWidth = () => turned() ? height : width;
const shownHeight = () => turned() ? width : height;

function fitScale(which) {
  const room = { w: Math.max(1, scroller.clientWidth - 32), h: Math.max(1, scroller.clientHeight - 32) };
  const across = room.w / shownWidth(), down = room.h / shownHeight();
  return which === "page-width" ? across : Math.min(across, down);
}

function layout() {
  if (!loaded) return;
  if (mode !== "fixed") scale = mode === "auto" ? Math.min(1, fitScale("page-fit")) : fitScale(mode);
  scale = Math.min(16, Math.max(0.02, scale));
  stage.style.width = `${shownWidth() * scale}px`;
  stage.style.height = `${shownHeight() * scale}px`;
  picture.style.width = `${width * scale}px`;
  picture.style.height = `${height * scale}px`;
  picture.style.transform = `translate(-50%, -50%) rotate(${rotation * 90}deg)`;
  picture.classList.toggle("pixelated", scale >= 3);
  post({ type: "state", page: 1, pages: 1, scale, width, height, rotation: rotation * 90 });
}

picture.addEventListener("load", () => {
  // An SVG without its own size has no natural size; show it at a common default instead of nothing.
  width = picture.naturalWidth || 800; height = picture.naturalHeight || 600;
  picture.alt = `Picture, ${width} by ${height} pixels`;
  loaded = true;
  layout();                                  // reports the size before "loaded", so the app never shows a picture without one
  post({ type: "loaded", pages: 1 });
  requestAnimationFrame(() => post({ type: "rendered" }));
});
picture.addEventListener("error", () => post({ type: "error", kind: "image" }));
picture.src = `${DOC}/picture`;

new ResizeObserver(() => { if (mode !== "fixed") layout(); }).observe(scroller);

host?.addEventListener("message", event => {
  const m = event.data ?? {};
  switch (m.type) {
    case "zoom":
      if (m.value === "in" || m.value === "out") { scale *= m.value === "in" ? 1.25 : 0.8; mode = "fixed"; }
      else if (m.value === "page-fit" || m.value === "page-width") mode = m.value;
      else if (typeof m.value === "number") { scale = m.value; mode = "fixed"; }
      layout();
      break;
    case "rotate":
      rotation = (rotation + (m.delta < 0 ? 3 : 1)) % 4;
      layout();
      break;
    case "theme": document.documentElement.dataset.theme = m.dark ? "dark" : "light"; break;
    case "focus": scroller.focus(); break;
    case "find": post({ type: "find", current: 0, total: 0, done: true }); break;
  }
});
