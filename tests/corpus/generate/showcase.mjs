// Sample documents for the README screenshots (docs/images/showcase), made with the same independent producers as
// the test corpus. Not part of the corpus manifest.  node showcase.mjs
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import ExcelJS from "exceljs";
import { Document, Packer, Paragraph, TextRun, HeadingLevel, Table, TableRow, TableCell, WidthType, AlignmentType, BorderStyle, ShadingType } from "docx";
import JSZip from "jszip";
import PptxGenJS from "pptxgenjs";
import { stableZip, FIXED_DATE } from "./lib.mjs";
import { chartXml } from "./xlsx.mjs";

const out = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..", "..", "..", "docs", "images", "showcase");
fs.mkdirSync(out, { recursive: true });

// ---- A three-page newsletter ----
const green = "2E7D32";
const heading = (text, pageBreakBefore = false) => new Paragraph({ heading: HeadingLevel.HEADING_1, pageBreakBefore, spacing: { after: 160 }, children: [new TextRun({ text, color: green })] });
const body = text => new Paragraph({ spacing: { after: 140, line: 300 }, children: [new TextRun({ text, size: 23 })] });
const cell = (text, header = false) => new TableCell({
  width: { size: 3000, type: WidthType.DXA },
  shading: header ? { type: ShadingType.CLEAR, color: "auto", fill: "E8F5E9" } : undefined,
  children: [new Paragraph({ children: [new TextRun({ text, bold: header, size: 22 })] })]
});
const doc = new Document({
  creator: "Plain Viewer showcase", title: "Riverside Community Garden — Spring newsletter", created: FIXED_DATE,
  // Segoe UI, which every Windows PC has, rather than the library's default serif.
  styles: { default: { document: { run: { font: "Segoe UI" } }, heading1: { run: { font: "Segoe UI Semibold", size: 30 } } } },
  sections: [{ properties: {}, children: [
    new Paragraph({ alignment: AlignmentType.LEFT, spacing: { after: 60 }, children: [new TextRun({ text: "RIVERSIDE COMMUNITY GARDEN", bold: true, size: 20, color: "6B7280" })] }),
    new Paragraph({ spacing: { after: 240 }, border: { bottom: { color: green, style: BorderStyle.SINGLE, size: 12, space: 6 } }, children: [new TextRun({ text: "Spring newsletter", bold: true, size: 52, color: green })] }),
    body("Welcome back, gardeners! The frost has lifted, the tool shed has a new roof, and the first seedlings are already up in the greenhouse. This issue covers the planting calendar, the new rainwater tanks and our open day on Saturday 18 April."),
    heading("Planting calendar"),
    body("The table below lists what can go into the ground over the next few weeks. Beds 1 to 12 are shared; beds 13 to 30 belong to members."),
    new Table({ width: { size: 9000, type: WidthType.DXA }, rows: [
      new TableRow({ children: [cell("Crop", true), cell("Sow or plant", true), cell("Where", true)] }),
      new TableRow({ children: [cell("Potatoes"), cell("Late March"), cell("Beds 1–4")] }),
      new TableRow({ children: [cell("Peas and beans"), cell("April"), cell("Beds 5–8, with supports")] }),
      new TableRow({ children: [cell("Salad leaves"), cell("Every two weeks"), cell("Greenhouse, then beds 9–10")] }),
      new TableRow({ children: [cell("Tomatoes"), cell("Mid May"), cell("Greenhouse")] }),
      new TableRow({ children: [cell("Pumpkins"), cell("Late May"), cell("Beds 11–12")] }),
    ] }),
    new Paragraph({ spacing: { before: 240 }, children: [] }),
    heading("Rainwater tanks"),
    body("Thanks to a grant from the local council, four new 1,000-litre tanks now collect rain from the shed and greenhouse roofs. Last spring we used about 40% mains water; this year we hope to need almost none."),
    new Table({ width: { size: 9000, type: WidthType.DXA }, rows: [
      new TableRow({ children: [cell("Month", true), cell("Rain collected", true), cell("Mains water used", true)] }),
      new TableRow({ children: [cell("January"), cell("2,850 litres"), cell("None")] }),
      new TableRow({ children: [cell("February"), cell("3,120 litres"), cell("None")] }),
      new TableRow({ children: [cell("March"), cell("2,460 litres"), cell("180 litres")] }),
    ] }),
    new Paragraph({ spacing: { before: 120, after: 200 }, children: [new TextRun({ text: "Readings from the tank gauges, taken on the last Saturday of each month.", italics: true, size: 20, color: "6B7280" })] }),
    heading("Open day", true),
    body("Our open day is on Saturday 18 April from 10:00 to 15:00. There will be seedling swaps, a composting workshop and soup from the garden kitchen. Please bring friends and neighbours."),
    heading("Volunteer rota"),
    ...["Watering: Mondays and Thursdays, 18:00", "Compost team: first Saturday of the month", "Greenhouse: ask Maria for the key", "Tool repair: Wednesdays, 17:00"].map(item => new Paragraph({ bullet: { level: 0 }, children: [new TextRun({ text: item, size: 23 })] })),
    heading("Contact", true),
    body("Questions and ideas are always welcome at the Saturday coffee morning in the shed. See you in the garden!"),
  ] }]
});
fs.writeFileSync(path.join(out, "Garden newsletter.docx"), await stableZip(await Packer.toBuffer(doc)));

// ---- A household budget workbook ----
const wb = new ExcelJS.Workbook();
wb.creator = "Plain Viewer showcase"; wb.created = FIXED_DATE; wb.modified = FIXED_DATE;
const ws = wb.addWorksheet("Budget 2026", { views: [{ state: "frozen", ySplit: 3 }] });
ws.columns = [{ width: 26 }, { width: 13 }, { width: 13 }, { width: 13 }, { width: 13 }, { width: 14 }];
ws.mergeCells("A1:F1");
Object.assign(ws.getCell("A1"), { value: "Household budget 2026", font: { bold: true, size: 16, color: { argb: "FF1D4ED8" } } });
const headers = ["Category", "January", "February", "March", "April", "Total"];
headers.forEach((text, i) => Object.assign(ws.getRow(3).getCell(i + 1), {
  value: text, font: { bold: true, color: { argb: "FFFFFFFF" } }, alignment: { horizontal: i ? "right" : "left" },
  fill: { type: "pattern", pattern: "solid", fgColor: { argb: "FF1D4ED8" } }
}));
const lines = [["Rent", 950, 950, 950, 950], ["Groceries", 412.5, 389.2, 430.75, 405.1], ["Electricity and gas", 138.4, 126.9, 101.3, 84.6],
  ["Internet and phone", 55, 55, 55, 55], ["Transport", 96, 88.5, 102, 94.25], ["Savings", 300, 300, 350, 350], ["Other", 64.9, 120, 45.5, 88]];
lines.forEach((values, r) => {
  const row = ws.getRow(4 + r);
  row.getCell(1).value = values[0];
  for (let c = 1; c <= 4; c++) Object.assign(row.getCell(c + 1), { value: values[c], numFmt: "$#,##0.00" });
  Object.assign(row.getCell(6), { value: { formula: `SUM(B${4 + r}:E${4 + r})`, result: values.slice(1).reduce((a, b) => a + b, 0) }, numFmt: "$#,##0.00", font: { bold: true } });
  if (r % 2) for (let c = 1; c <= 6; c++) row.getCell(c).fill = { type: "pattern", pattern: "solid", fgColor: { argb: "FFEFF6FF" } };
});
const total = ws.getRow(4 + lines.length);
total.getCell(1).value = "Total";
for (let c = 2; c <= 6; c++) {
  const letter = String.fromCharCode(64 + c);
  const sum = lines.reduce((a, l) => a + (c === 6 ? l.slice(1).reduce((x, y) => x + y, 0) : l[c - 1]), 0);
  Object.assign(total.getCell(c), { value: { formula: `SUM(${letter}4:${letter}${3 + lines.length})`, result: sum }, numFmt: "$#,##0.00" });
}
for (let c = 1; c <= 6; c++) Object.assign(total.getCell(c), { font: { bold: true }, border: { top: { style: "thin" }, bottom: { style: "double" } } });
const notes = wb.addWorksheet("Notes");
notes.getCell("A1").value = "Budget notes"; notes.getCell("A1").font = { bold: true };
notes.getCell("A2").value = "Energy costs fall in spring; savings rise from March.";
fs.writeFileSync(path.join(out, "Household budget.xlsx"), await withBudgetCharts(Buffer.from(await wb.xlsx.writeBuffer())));

// Two charts below the table (exceljs cannot write charts, so they are added as DrawingML): monthly spending on the
// items that change, and where the money went. The values are the chart's own cache, as Excel saves them.
async function withBudgetCharts(buffer) {
  const zip = await JSZip.loadAsync(buffer);
  const sheet = "'Budget 2026'";
  const months = ["January", "February", "March", "April"];
  const changing = [[1, "2563EB"], [2, "F59E0B"], [4, "10B981"], [6, "8B5CF6"]];   // rows of `lines`
  zip.file("xl/charts/chart1.xml", chartXml({ kind: "barChart", stacked: true, title: "Monthly spending that changes", categories: months,
    series: changing.map(([r, colour]) => [lines[r][0], lines[r].slice(1), colour]),
    refs: { name: i => `${sheet}!$A$${4 + changing[i][0]}`, cat: `${sheet}!$B$3:$E$3`, val: i => `${sheet}!$B$${4 + changing[i][0]}:$E$${4 + changing[i][0]}` } }));
  zip.file("xl/charts/chart2.xml", chartXml({ kind: "doughnutChart", title: "Where the money went", categories: lines.map(l => l[0]),
    series: [["Total", lines.map(l => Math.round(l.slice(1).reduce((a, b) => a + b, 0) * 100) / 100)]],
    refs: { name: () => `${sheet}!$F$3`, cat: `${sheet}!$A$4:$A$10`, val: () => `${sheet}!$F$4:$F$10` } }));
  const emu = px => Math.round(px * 9525);
  const anchor = (id, [c1, x1, r1], [c2, x2, r2], name) => `<xdr:twoCellAnchor><xdr:from><xdr:col>${c1}</xdr:col><xdr:colOff>${emu(x1)}</xdr:colOff><xdr:row>${r1}</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:from>` +
    `<xdr:to><xdr:col>${c2}</xdr:col><xdr:colOff>${emu(x2)}</xdr:colOff><xdr:row>${r2}</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:to>` +
    `<xdr:graphicFrame macro=""><xdr:nvGraphicFramePr><xdr:cNvPr id="${id + 1}" name="${name}"/><xdr:cNvGraphicFramePr/></xdr:nvGraphicFramePr><xdr:xfrm><a:off x="0" y="0"/><a:ext cx="0" cy="0"/></xdr:xfrm>` +
    `<a:graphic><a:graphicData uri="http://schemas.openxmlformats.org/drawingml/2006/chart"><c:chart xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart" r:id="rId${id}"/></a:graphicData></a:graphic></xdr:graphicFrame><xdr:clientData/></xdr:twoCellAnchor>`;
  // Spending beside the table (from column H, below the three frozen rows, which would cover it), the doughnut under it.
  zip.file("xl/drawings/drawing1.xml", `<?xml version="1.0" encoding="UTF-8" standalone="yes"?><xdr:wsDr xmlns:xdr="http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing" xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">` +
    anchor(1, [7, 0, 4], [14, 0, 20], "Monthly spending") + anchor(2, [0, 0, 12], [6, 0, 20], "Where the money went") + `</xdr:wsDr>`);
  const rel = (id, type, target) => `<Relationship Id="${id}" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/${type}" Target="${target}"/>`;
  zip.file("xl/drawings/_rels/drawing1.xml.rels", `<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">${rel("rId1", "chart", "../charts/chart1.xml")}${rel("rId2", "chart", "../charts/chart2.xml")}</Relationships>`);
  const sheetRels = "xl/worksheets/_rels/sheet1.xml.rels";
  const existing = zip.file(sheetRels) ? await zip.file(sheetRels).async("string") : `<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"></Relationships>`;
  zip.file(sheetRels, existing.replace("</Relationships>", rel("rIdShowcaseDrawing", "drawing", "../drawings/drawing1.xml") + "</Relationships>"));
  const sheetXml = await zip.file("xl/worksheets/sheet1.xml").async("string");
  zip.file("xl/worksheets/sheet1.xml", sheetXml.replace("</worksheet>", '<drawing r:id="rIdShowcaseDrawing"/></worksheet>'));
  const override = (part, type) => `<Override PartName="/${part}" ContentType="application/vnd.openxmlformats-officedocument.${type}"/>`;
  zip.file("[Content_Types].xml", (await zip.file("[Content_Types].xml").async("string")).replace("</Types>",
    override("xl/drawings/drawing1.xml", "drawing+xml") + override("xl/charts/chart1.xml", "drawingml.chart+xml") + override("xl/charts/chart2.xml", "drawingml.chart+xml") + "</Types>"));
  return stableZip(await zip.generateAsync({ type: "nodebuffer" }));
}

// ---- A short presentation ----
const deck = new PptxGenJS();
deck.layout = "LAYOUT_WIDE"; deck.author = "Plain Viewer showcase"; deck.title = "Riverside Community Garden open day";
const title = deck.addSlide();
title.background = { color: "2E7D32" };
title.addText("Open day", { x: 0.8, y: 2.2, w: 11.7, h: 1.2, fontSize: 54, bold: true, color: "FFFFFF", fontFace: "Segoe UI" });
title.addText("Riverside Community Garden · Saturday 18 April, 10:00–15:00", { x: 0.8, y: 3.5, w: 11.7, h: 0.6, fontSize: 22, color: "E8F5E9", fontFace: "Segoe UI" });
const plan = deck.addSlide();
plan.addText("What's on", { x: 0.6, y: 0.4, w: 12, h: 0.9, fontSize: 36, bold: true, color: "2E7D32", fontFace: "Segoe UI" });
plan.addText([
  { text: "Seedling swap in the greenhouse", options: { bullet: true } },
  { text: "Composting workshop at 11:00 and 14:00", options: { bullet: true } },
  { text: "Soup from the garden kitchen", options: { bullet: true } },
  { text: "Tours of the new rainwater tanks", options: { bullet: true } },
], { x: 0.8, y: 1.5, w: 6.2, h: 3.8, fontSize: 24, color: "1F2937", fontFace: "Segoe UI", paraSpaceAfter: 14 });
plan.addChart(deck.charts.BAR, [{ name: "Visitors", labels: ["2023", "2024", "2025"], values: [120, 185, 260] }],
  { x: 7.3, y: 1.5, w: 5.4, h: 4.2, barDir: "col", chartColors: ["2E7D32"], showTitle: true, title: "Visitors at past open days", titleFontSize: 16, showValue: true });
const rota = deck.addSlide();
rota.addText("Helpers needed", { x: 0.6, y: 0.4, w: 12, h: 0.9, fontSize: 36, bold: true, color: "2E7D32", fontFace: "Segoe UI" });
rota.addTable([
  [{ text: "Time", options: { bold: true, fill: { color: "E8F5E9" } } }, { text: "Task", options: { bold: true, fill: { color: "E8F5E9" } } }, { text: "Helpers", options: { bold: true, fill: { color: "E8F5E9" } } }],
  ["9:00", "Set up tables and signs", "4"], ["10:00", "Welcome desk", "2"], ["12:00", "Soup kitchen", "3"], ["15:00", "Tidy up", "5"],
], { x: 0.8, y: 1.6, w: 11.7, fontSize: 20, fontFace: "Segoe UI", border: { type: "solid", color: "C8E6C9", pt: 1 }, colW: [2, 7.2, 2.5] });
fs.writeFileSync(path.join(out, "Open day.pptx"), await stableZip(await deck.write({ outputType: "nodebuffer" })));

// ---- A Markdown document ----
fs.writeFileSync(path.join(out, "Trip notes.md"), `# Lake District trip notes

Four days of walking, **three** youth hostels and one very steep pass. Booked for the second week of June.

## Packing list

- [x] Waterproof jacket and trousers
- [x] Map and compass (phones lose signal in the valleys)
- [ ] Spare socks
- [ ] Midge repellent

## Route

| Day | From | To | Distance |
|---|---|---|---:|
| 1 | Keswick | Borrowdale | 14 km |
| 2 | Borrowdale | Grasmere | 18 km |
| 3 | Grasmere | Patterdale | 13 km |
| 4 | Patterdale | Penrith (bus) | 9 km |

> Tip from the hostel warden: start the Grasmere day early, before the clouds come down.

## Budget

\`\`\`text
Hostels     3 × £32  = £96
Food                   £70
Buses                  £24
Total                 £190
\`\`\`

More walks: [Lake District National Park](https://www.lakedistrict.gov.uk/).
`);
console.log("Wrote", out);
