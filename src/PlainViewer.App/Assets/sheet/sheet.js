// Plain Viewer spreadsheet page. Runs inside the same locked-down WebView2 as the PDF view. The workbook JSON is
// display text prepared by the worker process; nothing here evaluates formulas or loads anything else.
// Large sheets (with a "store") keep only the rows near the viewport in the page and fetch others from the app.
const DOC = "https://doc.plainviewer.invalid";
const DATA_URL = `${DOC}/workbook.json`;
const CHUNK = 200;                                   // rows per <tbody>; off-screen chunks are skipped by the browser
const PAGE = 256;                                    // rows per request for large sheets
const MARGIN = 60;                                   // rows rendered above and below the viewport for large sheets
const host = window.chrome?.webview;
const post = message => host?.postMessage(message);
const scroller = document.getElementById("scroller");
const tabs = document.getElementById("tabs");
document.documentElement.dataset.theme = new URLSearchParams(location.search).get("theme") === "dark" ? "dark" : "light";

let workbook = null, active = 0, zoom = 1;
let hits = [], hitIndex = -1, lastQuery = "";
let big = null;                                      // state of the active large sheet
let bigHits = null;                                  // search results for the active large sheet

const columnName = index => { let name = ""; for (let v = index + 1; v > 0; v = Math.floor((v - 1) / 26)) name = String.fromCharCode(65 + (v - 1) % 26) + name; return name; };
const pixels = width => width <= 0 ? 0 : Math.round(width * 7 + 5);   // Excel character width to pixels (Calibri 11, 96 DPI)

function state() {
  const sheet = workbook.sheets[active];
  post({ type: "state", sheet: active + 1, sheets: workbook.sheets.length, name: sheet.name, scale: zoom });
}

function renderTabs() {
  tabs.replaceChildren(...workbook.sheets.map((sheet, i) => {
    const button = document.createElement("button");
    button.type = "button";
    button.setAttribute("role", "tab");
    button.setAttribute("aria-selected", String(i === active));
    button.tabIndex = i === active ? 0 : -1;
    button.textContent = sheet.name;
    button.addEventListener("click", () => show(i));
    return button;
  }));
}

tabs.addEventListener("keydown", event => {
  if (event.key !== "ArrowLeft" && event.key !== "ArrowRight") return;
  const step = (event.key === "ArrowRight") !== (getComputedStyle(tabs).direction === "rtl") ? 1 : -1;
  show((active + step + workbook.sheets.length) % workbook.sheets.length);
  tabs.children[active]?.focus();
  event.preventDefault();
});

function show(index) {
  active = index;
  hits = []; hitIndex = -1; lastQuery = ""; big = null; bigHits = null;
  renderTabs();
  scroller.scrollTo(0, 0);
  render(workbook.sheets[index]);
  state();
}

// Values and alignment of row r, or null while a large sheet's row is still being fetched.
function cellsOf(sheet, r) {
  if (r < sheet.rows.length) return [sheet.rows[r], sheet.align[r] ?? ""];
  const fields = big?.cache.get(Math.floor(r / PAGE))?.[r % PAGE];
  return fields ? [fields.slice(1), fields[0] ?? ""] : null;
}

// The table with its column widths; returns the left offset of each column (for frozen columns).
function frame(sheet) {
  const columns = sheet.columnWidths.length;
  const table = document.createElement("table");
  table.className = "grid";
  table.setAttribute("aria-label", `Sheet ${sheet.name}`);
  if (sheet.rightToLeft) table.dir = "rtl";
  const group = document.createElement("colgroup");
  const rowHeaderColumn = document.createElement("col");
  rowHeaderColumn.style.width = "52px";
  group.append(rowHeaderColumn);
  const offsets = [];
  let x = 52;
  for (let c = 0; c < columns; c++) {
    const col = document.createElement("col");
    col.style.width = pixels(sheet.columnWidths[c]) + "px";
    group.append(col);
    offsets.push(x); x += pixels(sheet.columnWidths[c]);
  }
  table.append(group);
  return { table, offsets };
}

// Column letters plus frozen rows, in <thead> so they stay visible while scrolling.
function header(sheet, table, offsets, frozen, spans, covered, hiddenRows) {
  const head = document.createElement("thead");
  const letters = document.createElement("tr");
  letters.className = "letters";
  const corner = document.createElement("th");
  corner.className = "corner";
  corner.setAttribute("aria-label", "Row and column headers");
  letters.append(corner);
  for (let c = 0; c < sheet.columnWidths.length; c++) {
    const th = document.createElement("th");
    th.scope = "col";
    th.textContent = columnName(c);
    if (sheet.columnWidths[c] <= 0) th.className = "hidden-col";
    if (c < sheet.frozenColumns) { th.classList.add("frozen-col"); th.style.insetInlineStart = offsets[c] + "px"; }
    letters.append(th);
  }
  head.append(letters);
  for (let r = 0; r < frozen; r++) head.append(row(sheet, r, spans, covered, hiddenRows, offsets, true));
  table.append(head);
  return head;
}

// Merged cells within rows [first, last]; a merge that continues past `last` is cut there and the rest of it
// shows as empty cells. `position` maps a row to its place among rendered rows (hidden rows are not rendered).
function mergesWithin(sheet, first, last, position) {
  const spans = new Map(), covered = new Set();
  for (const [r1, c1, r2, c2] of sheet.merges) {
    if (r1 < first || r1 > last) continue;
    const lastRow = Math.min(r2, last);
    spans.set(`${r1},${c1}`, [position(lastRow) - position(r1) + 1, c2 - c1 + 1]);
    for (let r = r1; r <= lastRow; r++) for (let c = c1; c <= c2; c++) if (r !== r1 || c !== c1) covered.add(`${r},${c}`);
  }
  return { spans, covered };
}

function render(sheet) {
  if (!(sheet.rowCount ?? sheet.rows.length)) {
    const note = document.createElement("p");
    note.className = "empty";
    note.textContent = "This sheet is empty.";
    scroller.replaceChildren(note);
    return;
  }
  if (sheet.store) { renderLarge(sheet); return; }
  const { table, offsets } = frame(sheet);
  const frozen = Math.min(sheet.frozenRows, sheet.rows.length);
  const sections = [[0, frozen]];
  for (let start = frozen; start < sheet.rows.length; start += CHUNK) sections.push([start, Math.min(start + CHUNK, sheet.rows.length)]);
  const hiddenRows = new Set(sheet.hiddenRows);
  // Merged cells span within their section.
  const spans = new Map(), covered = new Set();
  for (const [s, e] of sections) {
    const part = mergesWithin(sheet, s, e - 1, r => r);
    part.spans.forEach((v, k) => spans.set(k, v)); part.covered.forEach(k => covered.add(k));
  }
  header(sheet, table, offsets, frozen, spans, covered, hiddenRows);
  for (const [start, end] of sections.slice(1)) {
    const body = document.createElement("tbody");
    body.className = "chunk";
    body.style.containIntrinsicSize = `auto ${(end - start) * 22}px`;
    for (let r = start; r < end; r++) body.append(row(sheet, r, spans, covered, hiddenRows, offsets, false));
    table.append(body);
  }
  scroller.replaceChildren(table);
}

function row(sheet, r, spans, covered, hiddenRows, offsets, isFrozen) {
  const tr = document.createElement("tr");
  tr.dataset.r = r;
  if (hiddenRows.has(r + 1)) tr.hidden = true;
  if (isFrozen) { tr.className = "frozen"; tr.style.setProperty("--top", `calc(var(--row) * ${r + 1})`); }
  const header = document.createElement("th");
  header.scope = "row";
  header.textContent = String(r + 1);
  if (isFrozen) header.style.top = `calc(var(--row) * ${r + 1})`;
  tr.append(header);
  const data = cellsOf(sheet, r);
  if (!data) { tr.classList.add("loading"); tr.setAttribute("aria-busy", "true"); }
  const [values, align] = data ?? [[], ""];
  const current = big && bigHits?.list[hitIndex] ? bigHits.list[hitIndex].join(",") : null;
  for (let c = 0; c < values.length; c++) {
    const key = `${r},${c}`;
    if (covered.has(key)) continue;
    const td = document.createElement("td");
    td.dataset.c = c;
    td.textContent = values[c];                                    // text only, never HTML
    if (align[c] === "r") td.className = "r"; else if (align[c] === "c") td.className = "c";
    if (values[c] === "Result unavailable") td.classList.add("unavailable");
    if (sheet.columnWidths[c] <= 0) td.classList.add("hidden-col");
    const span = spans.get(key);
    if (span) { td.rowSpan = span[0]; td.colSpan = span[1]; td.classList.add("merged"); }
    if (c < sheet.frozenColumns) { td.classList.add("frozen-col"); td.style.insetInlineStart = offsets[c] + "px"; }
    if (isFrozen) td.style.top = `calc(var(--row) * ${r + 1})`;
    if (big && bigHits?.set.has(key)) td.classList.add(key === current ? "current" : "hit");
    tr.append(td);
  }
  return tr;
}

// ---- Large sheets: only rows near the viewport are in the page; spacers stand in for the rest. ----

function spacer(columns) {
  const body = document.createElement("tbody");
  const tr = document.createElement("tr");
  tr.className = "spacer";
  tr.setAttribute("aria-hidden", "true");
  const td = document.createElement("td");
  td.colSpan = columns + 1;
  tr.append(td);
  body.append(tr);
  return body;
}

function renderLarge(sheet) {
  const hidden = new Set(sheet.hiddenRows);
  const frozen = Math.min(sheet.frozenRows, sheet.rowCount);
  const order = new Int32Array(sheet.rowCount - frozen);
  let count = 0;
  for (let r = frozen; r < sheet.rowCount; r++) if (!hidden.has(r + 1)) order[count++] = r;
  const { table, offsets } = frame(sheet);
  table.setAttribute("aria-rowcount", String(sheet.rowCount + 1));
  const top = spacer(sheet.columnWidths.length), body = document.createElement("tbody"), bottom = spacer(sheet.columnWidths.length);
  big = { sheet, rows: order.subarray(0, count), hidden, offsets, top, body, bottom, start: -1, end: -1, cache: new Map(), pending: new Map(), head: null, frozen };
  const heads = mergesWithin(sheet, 0, frozen - 1, r => r);
  big.head = header(sheet, table, offsets, frozen, heads.spans, heads.covered, hidden);
  table.append(top, body, bottom);
  scroller.replaceChildren(table);
  update(true, 0);
}

// First row index (into big.rows) under the sticky header, from the rendered geometry (works at any zoom).
function firstVisible() {
  const rowHeight = big.body.rows[0]?.getBoundingClientRect().height || 22 * zoom;
  const view = scroller.getBoundingClientRect();
  const underHeader = view.top + big.head.getBoundingClientRect().height;
  const origin = big.top.getBoundingClientRect().top;                 // where row index 0 would start
  return { first: Math.max(0, Math.floor((underHeader - origin) / rowHeight)), count: Math.ceil(view.height / rowHeight) + 1 };
}

function lowerBound(array, value) {
  let low = 0, high = array.length;
  while (low < high) { const middle = (low + high) >> 1; if (array[middle] < value) low = middle + 1; else high = middle; }
  return low;
}

// Renders the rows around the viewport (or around `center`, an index into big.rows) and fetches missing pages.
function update(force, center) {
  if (!big) return;
  const { rows } = big;
  let first, count;
  if (center === undefined) ({ first, count } = firstVisible());
  else { count = Math.ceil(scroller.clientHeight / 22) + 1; first = Math.max(0, center - (count >> 1)); }
  const covered = first >= big.start && first + count <= big.end
    && (big.start === 0 || first - big.start >= MARGIN / 3) && (big.end === rows.length || big.end - first - count >= MARGIN / 3);
  if (!force && covered) return;
  big.start = Math.max(0, first - MARGIN);
  big.end = Math.min(rows.length, first + count + MARGIN);
  big.top.rows[0].cells[0].style.height = `calc(var(--row) * ${big.start})`;
  big.bottom.rows[0].cells[0].style.height = `calc(var(--row) * ${rows.length - big.end})`;
  const rendered = [];
  if (big.end > big.start) {
    const firstRow = rows[big.start], lastRow = rows[big.end - 1];
    const { spans, covered: hiddenCells } = mergesWithin(big.sheet, firstRow, lastRow, r => lowerBound(rows, r));
    for (let i = big.start; i < big.end; i++) {
      const r = rows[i];
      const tr = row(big.sheet, r, spans, hiddenCells, big.hidden, big.offsets, false);
      tr.setAttribute("aria-rowindex", String(r + 2));
      rendered.push(tr);
      if (r >= big.sheet.rows.length) fetchPage(Math.floor(r / PAGE));
    }
  }
  big.body.replaceChildren(...rendered);
}

// Fetches one page of rows (once); resolves when it is in the cache or has failed.
function fetchPage(page) {
  const state = big;
  if (!state || state.cache.has(page)) return Promise.resolve();
  if (state.pending.has(page)) return state.pending.get(page);
  const start = page * PAGE, count = Math.min(PAGE, state.sheet.rowCount - start);
  const request = fetch(`${DOC}/rows?sheet=${active}&start=${start}&count=${count}`, { cache: "no-store" })
    .then(response => response.ok ? response.json() : Promise.reject(new Error("rows")))
    .then(data => {
      if (big !== state) return;
      state.cache.set(page, data.rows);
      if (state.cache.size > 400) state.cache.delete(state.cache.keys().next().value);
      refreshSoon();
    })
    .catch(() => { })
    .finally(() => state.pending.delete(page));
  state.pending.set(page, request);
  return request;
}

let refreshQueued = false;
function refreshSoon() {
  if (refreshQueued) return;
  refreshQueued = true;
  requestAnimationFrame(() => { refreshQueued = false; update(true); });
}

let scrollQueued = false;
scroller.addEventListener("scroll", () => {
  if (!big || scrollQueued) return;
  scrollQueued = true;
  requestAnimationFrame(() => { scrollQueued = false; update(false); });
});
window.addEventListener("resize", () => { if (big) update(true); });

// The app searches large sheets (they are not all in the page) and returns up to 10,000 cells.
async function findLarge(query, previous) {
  if (!bigHits || bigHits.query !== query) {
    const state = big;
    let result = { total: 0, hits: [] };
    if (query) {
      try {
        const response = await fetch(`${DOC}/find?sheet=${active}&q=${encodeURIComponent(query)}`, { cache: "no-store" });
        if (response.ok) result = await response.json();
      } catch { }
    }
    if (big !== state) return;
    bigHits = { query, list: result.hits, total: result.total, set: new Set(result.hits.map(([r, c]) => `${r},${c}`)) };
    hitIndex = previous ? 0 : -1;
  }
  if (bigHits.list.length) {
    hitIndex = (hitIndex + (previous ? -1 : 1) + bigHits.list.length) % bigHits.list.length;
    const [r, c] = bigHits.list[hitIndex];
    const state = big;
    if (r >= big.sheet.rows.length) await fetchPage(Math.floor(r / PAGE));   // the cell must exist to scroll to it
    if (big !== state) return;
    const i = lowerBound(big.rows, r);
    if (big.rows[i] === r) update(true, i); else update(true);
    scroller.querySelector(`tr[data-r="${r}"]`)?.scrollIntoView({ block: "center" });
    cellElement(r, c)?.scrollIntoView({ block: "nearest", inline: "center" });
  } else update(true);
  post({ type: "find", current: bigHits.list.length ? hitIndex + 1 : 0, total: bigHits.total, done: true });
}

function cellElement(r, c) {
  const tr = scroller.querySelector(`tr[data-r="${r}"]`);
  return tr?.querySelector(`td[data-c="${c}"]`) ?? null;
}

function find(query, previous) {
  if (big) { findLarge(query, previous); return; }
  const sheet = workbook.sheets[active];
  if (query !== lastQuery) {
    for (const el of scroller.querySelectorAll("td.hit, td.current")) el.classList.remove("hit", "current");
    const needle = query.toLocaleLowerCase();
    hits = [];
    if (needle) sheet.rows.forEach((values, r) => values.forEach((text, c) => { if (text && text.toLocaleLowerCase().includes(needle)) hits.push([r, c]); }));
    hitIndex = previous ? 0 : -1;
    lastQuery = query;
    for (const [r, c] of hits) cellElement(r, c)?.classList.add("hit");
  }
  if (hits.length) {
    cellElement(...(hits[hitIndex] ?? [-1, -1]))?.classList.remove("current");
    hitIndex = (hitIndex + (previous ? -1 : 1) + hits.length) % hits.length;
    const target = cellElement(...hits[hitIndex]);
    target?.classList.add("current");
    target?.scrollIntoView({ block: "center", inline: "center" });
  }
  post({ type: "find", current: hits.length ? hitIndex + 1 : 0, total: hits.length, done: true });
}

host?.addEventListener("message", event => {
  const m = event.data ?? {};
  switch (m.type) {
    case "find": find(String(m.query ?? ""), !!m.previous); break;
    case "sheet": if (workbook) show((active + (m.delta > 0 ? 1 : -1) + workbook.sheets.length) % workbook.sheets.length); break;
    case "zoom":
      if (m.value === "in") zoom = Math.min(4, Math.round((zoom + 0.1) * 10) / 10);
      else if (m.value === "out") zoom = Math.max(0.5, Math.round((zoom - 0.1) * 10) / 10);
      else if (typeof m.value === "number") zoom = Math.min(4, Math.max(0.5, m.value));
      scroller.style.zoom = String(zoom);
      if (big) update(true);
      state();
      break;
    case "theme": document.documentElement.dataset.theme = m.dark ? "dark" : "light"; break;
    case "focus": scroller.focus(); break;
  }
});

try {
  const response = await fetch(DATA_URL, { cache: "no-store" });
  if (!response.ok) throw new Error("unavailable");
  workbook = await response.json();
  if (!workbook?.sheets?.length) throw new Error("empty");
  post({ type: "loaded", sheets: workbook.sheets.length });
  show(0);
  requestAnimationFrame(() => post({ type: "rendered" }));    // first sheet drawn (used for timing)
} catch {
  post({ type: "error", kind: "unavailable" });
}
