// Large CSV and plain-text performance fixtures (only with --large; written to tests/corpus/generated, not in git).
// Deterministic: a fixed pseudo-random sequence, so every run writes the same bytes.
import fs from "node:fs";
import path from "node:path";
import { CORPUS, record, SAFE_RULES } from "./lib.mjs";

const licence = "Generated for this project (public domain test data)";
const producer = "tests/corpus/generate/large-text.mjs (Node)";

function writeUntil(file, targetBytes, header, line, last) {
  fs.mkdirSync(path.dirname(file), { recursive: true });
  const fd = fs.openSync(file, "w");
  let written = 0, count = 0, chunk = [];
  const flush = () => { const data = Buffer.from(chunk.join(""), "utf8"); fs.writeSync(fd, data); written += data.length; chunk = []; };
  if (header) chunk.push(header);
  while (written + 1024 * 1024 < targetBytes) {
    for (let i = 0; i < 10000; i++) chunk.push(line(++count));
    flush();
  }
  chunk.push(last(++count)); flush();
  fs.closeSync(fd);
  return { count, written };
}

export function generateLargeText({ large }) {
  if (!large) return;
  let seed = 20260927;
  const next = () => (seed = (seed * 1103515245 + 12345) % 2147483648) / 2147483648;
  const regions = ["North", "South", "East", "West", "سنڌ"];

  // About 200 MB of CSV: leading zeros, quoted fields with commas and line breaks, Unicode.
  const csv = path.join(CORPUS, "generated", "csv-large-200mb.csv");
  const csvResult = writeUntil(csv, 200 * 1024 * 1024, "id,date,region,amount,quantity,note\r\n",
    n => {
      const note = n % 1000 === 0 ? `"quoted, with comma and ""quotes"""` : n % 5003 === 0 ? `"two\r\nlines"` : `note ${Math.floor(next() * 1000)}`;
      return `${String(n).padStart(8, "0")},2026-${String(1 + n % 12).padStart(2, "0")}-${String(1 + n % 28).padStart(2, "0")},${regions[n % 5]},${(next() * 100000).toFixed(2)},${n % 97},${note}\r\n`;
    },
    n => `${String(n).padStart(8, "0")},2026-12-31,North,1.00,1,Hello last row\r\n`);
  record({ id: "csv-large", file: "generated/csv-large-200mb.csv", format: "csv", category: "large", producer, licence, generated: true,
    expect: { result: "open", rows: csvResult.count + 1, columns: 6, text: ["Hello last row"] }, rules: SAFE_RULES,
    notes: `${csvResult.written} bytes. Performance target: a 200 MB CSV opens without exhausting memory.` });

  // About 100 MB of log-style text with some Unicode lines.
  const text = path.join(CORPUS, "generated", "text-large-100mb.txt");
  const textResult = writeUntil(text, 100 * 1024 * 1024, "Hello large text file\r\n",
    n => n % 997 === 0 ? `${n} اردو: یہ ایک لمبی فائل ہے۔ 中文：大文件测试\r\n`
      : `2026-09-27 12:${String(n % 60).padStart(2, "0")}:00.${String(n % 1000).padStart(3, "0")} INFO [worker-${n % 8}] request ${n} handled in ${Math.floor(next() * 500)} ms\r\n`,
    n => `${n} Needle at the end of the large text file\r\n`);
  record({ id: "text-large", file: "generated/text-large-100mb.txt", format: "txt", category: "large", producer, licence, generated: true,
    expect: { result: "open", lines: textResult.count + 1, text: ["Hello large text file", "Needle at the end"] }, rules: SAFE_RULES,
    notes: `${textResult.written} bytes. Large plain text must stream rather than load into memory.` });
}
