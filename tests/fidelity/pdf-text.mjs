// Prints each page's text of a PDF as JSON: { pages: n, text: ["page 1 text", ...] }.
// Used by scripts/compare-office.ps1 to compare Plain Viewer's conversion with the PDF Microsoft Office produced.
import fs from "node:fs";
console.warn = () => { };   // pdf.js warns that it cannot draw pages in Node; only the text is needed here
const { getDocument } = await import("pdfjs-dist/legacy/build/pdf.mjs");

const data = new Uint8Array(fs.readFileSync(process.argv[2]));
const loading = getDocument({ data, isEvalSupported: false, useSystemFonts: false, verbosity: 0 });
const pdf = await loading.promise;
const text = [];
for (let n = 1; n <= pdf.numPages; n++) {
  const page = await pdf.getPage(n);
  const content = await page.getTextContent();
  text.push(content.items.map(item => item.str ?? "").join(" ").replace(/\s+/g, " ").trim());
  page.cleanup();
}
process.stdout.write(JSON.stringify({ pages: pdf.numPages, text }));
await loading.destroy();
