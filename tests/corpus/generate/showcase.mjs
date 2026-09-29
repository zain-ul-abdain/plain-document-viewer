// Sample documents for the README screenshots (docs/images/showcase), made with the same independent producers as
// the test corpus. Not part of the corpus manifest.  node showcase.mjs
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import ExcelJS from "exceljs";
import { Document, Packer, Paragraph, TextRun, HeadingLevel, Table, TableRow, TableCell, WidthType, ImageRun, AlignmentType, BorderStyle, ShadingType } from "docx";
import { stableZip, chartPng, FIXED_DATE } from "./lib.mjs";

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
    new Paragraph({ children: [new ImageRun({ type: "png", data: chartPng(), transformation: { width: 360, height: 180 } })] }),
    new Paragraph({ spacing: { after: 200 }, children: [new TextRun({ text: "Rain collected per month (litres, estimate)", italics: true, size: 20, color: "6B7280" })] }),
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
ws.getCell(`A${6 + lines.length}`).value = "Values are the saved results: Plain Viewer never recalculates formulas.";
ws.getCell(`A${6 + lines.length}`).font = { italic: true, color: { argb: "FF6B7280" } };
const notes = wb.addWorksheet("Notes");
notes.getCell("A1").value = "Budget notes"; notes.getCell("A1").font = { bold: true };
notes.getCell("A2").value = "Energy costs fall in spring; savings rise from March.";
fs.writeFileSync(path.join(out, "Household budget.xlsx"), Buffer.from(await wb.xlsx.writeBuffer()));
console.log("Wrote", out);
