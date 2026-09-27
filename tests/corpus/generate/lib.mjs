// Shared helpers for the fixture generator. Everything here is deterministic apart from timestamps
// that the producing libraries embed; expected results never depend on those.
import fs from "node:fs";
import path from "node:path";
import zlib from "node:zlib";
import { fileURLToPath } from "node:url";

export const CORPUS = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
export const GENERATED = path.join(CORPUS, "generated");      // large or bulky files; ignored by git
export const LISTENER = "http://127.0.0.1:47831";             // tests/harness/request-listener.mjs
export const WEBDAV_UNC = "\\\\127.0.0.1@47831\\share";       // UNC path Windows would try over WebDAV on the same port

// Some packages do not export package.json, so read it from disk.
export function packageVersion(name) {
  const file = path.join(path.dirname(fileURLToPath(import.meta.url)), "node_modules", name, "package.json");
  return JSON.parse(fs.readFileSync(file, "utf8")).version;
}

const entries = [];
export function record(entry) { entries.push(entry); }
export function manifestEntries() { return entries; }

export function write(relative, data) {
  const target = path.join(CORPUS, relative);
  fs.mkdirSync(path.dirname(target), { recursive: true });
  fs.writeFileSync(target, data);
  return target;
}

export function streamToBuffer(doc) {
  return new Promise((resolve, reject) => {
    const chunks = [];
    doc.on("data", c => chunks.push(c));
    doc.on("end", () => resolve(Buffer.concat(chunks)));
    doc.on("error", reject);
    doc.end();
  });
}

// Minimal RGB PNG encoder, so image fixtures need no third-party image files.
export function png(width, height, pixel) {
  const raw = Buffer.alloc((width * 3 + 1) * height);
  for (let y = 0; y < height; y++) {
    raw[y * (width * 3 + 1)] = 0;
    for (let x = 0; x < width; x++) {
      const [r, g, b] = pixel(x, y);
      const o = y * (width * 3 + 1) + 1 + x * 3;
      raw[o] = r; raw[o + 1] = g; raw[o + 2] = b;
    }
  }
  const chunk = (type, data) => {
    const len = Buffer.alloc(4); len.writeUInt32BE(data.length);
    const body = Buffer.concat([Buffer.from(type, "ascii"), data]);
    const crc = Buffer.alloc(4); crc.writeUInt32BE(zlib.crc32(body) >>> 0);
    return Buffer.concat([len, body, crc]);
  };
  const ihdr = Buffer.alloc(13);
  ihdr.writeUInt32BE(width, 0); ihdr.writeUInt32BE(height, 4); ihdr[8] = 8; ihdr[9] = 2;
  return Buffer.concat([
    Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
    chunk("IHDR", ihdr), chunk("IDAT", zlib.deflateSync(raw)), chunk("IEND", Buffer.alloc(0))
  ]);
}

export const chartPng = () => png(240, 120, (x, y) => {
  const bar = Math.floor(x / 60), heights = [60, 95, 40, 110];
  return y > 120 - heights[bar] && x % 60 > 8 ? [[0x26, 0x6d, 0xb3], [0x2e, 0x9e, 0x6b], [0xd9, 0x8c, 0x1f], [0xb3, 0x3b, 0x3b]][bar] : [255, 255, 255];
});

// Hand-written PDF for cases a PDF library will not produce (JavaScript, Launch actions, UNC URIs).
// Objects are strings; the writer computes the cross-reference table.
export function rawPdf(objects) {
  let out = "%PDF-1.7\n%\xE2\xE3\xCF\xD3\n";
  const offsets = [];
  objects.forEach((body, i) => { offsets.push(Buffer.byteLength(out, "latin1")); out += `${i + 1} 0 obj\n${body}\nendobj\n`; });
  const xref = Buffer.byteLength(out, "latin1");
  out += `xref\n0 ${objects.length + 1}\n0000000000 65535 f \n`;
  for (const o of offsets) out += `${String(o).padStart(10, "0")} 00000 n \n`;
  out += `trailer\n<< /Size ${objects.length + 1} /Root 1 0 R >>\nstartxref\n${xref}\n%%EOF\n`;
  return Buffer.from(out, "latin1");
}

export function textStream(lines) {
  const body = "BT /F1 14 Tf 72 740 Td 18 TL\n" + lines.map(l => `(${l.replace(/[()\\]/g, m => "\\" + m)}) Tj T*`).join("\n") + "\nET";
  return `<< /Length ${Buffer.byteLength(body, "latin1")} >>\nstream\n${body}\nendstream`;
}

export const SAFE_RULES = { network: "none", sourceUnchanged: true, filesBesideSource: "none" };
