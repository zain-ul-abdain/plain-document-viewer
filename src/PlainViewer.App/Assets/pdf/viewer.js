// Plain Viewer PDF page. Runs inside a locked-down WebView2: every request that is not this app's
// own files or the host-supplied document is blocked by the host. The PDF itself is untrusted data.
import * as pdfjsLib from "./pdfjs/build/pdf.min.mjs";

globalThis.pdfjsLib = pdfjsLib;
const { EventBus, PDFLinkService, PDFFindController, PDFViewer, FindState } = await import("./pdfjs/web/pdf_viewer.mjs");

const DOCUMENT_URL = "https://doc.plainviewer.invalid/document.pdf";
document.documentElement.dataset.theme = new URLSearchParams(location.search).get("theme") === "dark" ? "dark" : "light";
const asset = path => new URL(path, location.href).href;
pdfjsLib.GlobalWorkerOptions.workerSrc = asset("pdfjs/build/pdf.worker.min.mjs");

const host = window.chrome?.webview;
const post = message => host?.postMessage(message);
const container = document.getElementById("viewerContainer");
const eventBus = new EventBus();
const linkService = new PDFLinkService({ eventBus, externalLinkRel: "noopener noreferrer nofollow" });
const findController = new PDFFindController({ eventBus, linkService, updateMatchesCountOnProgress: false });
const viewer = new PDFViewer({
  container, eventBus, linkService, findController,
  textLayerMode: 1,                                  // text layer on: search, selection and copy
  annotationMode: pdfjsLib.AnnotationMode.ENABLE,    // links and display annotations only; no form editing
  removePageBorders: false
});
linkService.setViewer(viewer);

let loadingTask = null;
let answerPassword = null;
let lastQuery = "";

function state() {
  post({ type: "state", page: viewer.currentPageNumber, pages: viewer.pagesCount, scale: viewer.currentScale });
}
eventBus.on("pagechanging", state);
eventBus.on("scalechanging", state);
eventBus.on("pagesinit", () => { viewer.currentScaleValue = "page-width"; container.focus(); state(); });
// With progress updates off, PDF.js sends the match count once, after every page has been searched.
eventBus.on("updatefindmatchescount", e => post({ type: "find", current: e.matchesCount.current, total: e.matchesCount.total, done: true }));
eventBus.on("updatefindcontrolstate", e => {
  if (e.state === FindState.PENDING) return;
  post({ type: "find", current: e.matchesCount.current, total: e.matchesCount.total, done: true, notFound: e.state === FindState.NOT_FOUND });
});

// Links: internal destinations ("#...") stay inside PDF.js. Everything else goes to the host, which applies
// its link policy and asks the user. Nothing is followed from here.
function interceptLink(event) {
  const anchor = event.target instanceof Element ? event.target.closest("a") : null;
  if (!anchor) return;
  const href = anchor.getAttribute("href") ?? "";
  if (href.startsWith("#")) return;
  event.preventDefault();
  event.stopImmediatePropagation();
  if (event.type === "click" && href) post({ type: "link", href });
}
container.addEventListener("click", interceptLink, true);
container.addEventListener("auxclick", interceptLink, true);
container.addEventListener("dragstart", event => event.preventDefault(), true);

function classify(error) {
  const name = error?.name ?? "";
  if (name === "PasswordException") return "password";
  if (name === "InvalidPDFException") return "damaged";
  return "damaged";
}

async function open() {
  let data;
  try {
    const response = await fetch(DOCUMENT_URL, { cache: "no-store" });
    if (!response.ok) throw new Error("document unavailable");
    data = new Uint8Array(await response.arrayBuffer());
  } catch {
    post({ type: "error", kind: "unavailable" });
    return;
  }
  loadingTask = pdfjsLib.getDocument({
    data,
    docBaseUrl: null,
    cMapUrl: asset("pdfjs/cmaps/"),
    cMapPacked: true,
    standardFontDataUrl: asset("pdfjs/standard_fonts/"),
    wasmUrl: asset("pdfjs/wasm/"),
    iccUrl: asset("pdfjs/iccs/"),
    enableXfa: false,
    disableRange: true,
    disableStream: true,
    disableAutoFetch: true,
    useSystemFonts: true
  });
  loadingTask.onPassword = (update, reason) => {
    answerPassword = update;
    post({ type: "password", incorrect: reason === pdfjsLib.PasswordResponses.INCORRECT_PASSWORD });
  };
  try {
    const pdf = await loadingTask.promise;
    answerPassword = null;
    viewer.setDocument(pdf);
    linkService.setDocument(pdf, null);
    post({ type: "loaded", pages: pdf.numPages });
  } catch (error) {
    post({ type: "error", kind: classify(error) });
  }
}

host?.addEventListener("message", event => {
  const m = event.data ?? {};
  switch (m.type) {
    case "password":
      if (answerPassword) { const answer = answerPassword; answerPassword = null; answer(String(m.value ?? "")); }
      break;
    case "password-cancel":
      answerPassword = null;
      loadingTask?.destroy();
      post({ type: "error", kind: "password-cancelled" });
      break;
    case "find": {
      const query = String(m.query ?? "");
      eventBus.dispatch("find", {
        source: null, type: query === lastQuery ? "again" : "", query,
        caseSensitive: false, entireWord: false, highlightAll: true, matchDiacritics: false,
        findPrevious: !!m.previous
      });
      lastQuery = query;
      break;
    }
    case "zoom":
      if (m.value === "in") viewer.increaseScale();
      else if (m.value === "out") viewer.decreaseScale();
      else if (m.value === "page-width" || m.value === "page-fit") viewer.currentScaleValue = m.value;
      else if (typeof m.value === "number") viewer.currentScale = Math.min(5, Math.max(0.25, m.value));
      break;
    case "page":
      if (Number.isInteger(m.number) && m.number >= 1 && m.number <= viewer.pagesCount) viewer.currentPageNumber = m.number;
      break;
    case "theme":
      document.documentElement.dataset.theme = m.dark ? "dark" : "light";
      break;
    case "focus":
      container.focus();
      break;
  }
});

// Errors before the document finishes loading mean it could not be read. Later rendering errors
// affect one page only, and PDF.js already shows that page as blank.
let loaded = false;
eventBus.on("pagesinit", () => { loaded = true; });
window.addEventListener("unhandledrejection", () => { if (!loaded) post({ type: "error", kind: "damaged" }); });
open();
