// Generates the Plain Viewer test corpus and its manifest.
//   node generate.mjs           small fixtures, committed to git
//   node generate.mjs --large   also the large performance fixtures under tests/corpus/generated (ignored by git)
import fs from "node:fs";
import path from "node:path";
import { CORPUS, manifestEntries, SAFE_RULES } from "./lib.mjs";
import { generatePdf } from "./pdf.mjs";

const large = process.argv.includes("--large");
await generatePdf({ large });

// Codex's hand-written fixtures (tests/corpus/*.txt|csv|md|markdown) keep their own provenance table in SOURCES.md.
const codexLicence = "Authored for this project by Codex (see SOURCES.md)";
const codex = [
  { id: "codex-simple-txt", file: "simple.txt", format: "txt", category: "simple", expect: { result: "open", text: ["Hello from Plain Viewer."] } },
  { id: "codex-complex-txt", file: "complex.txt", format: "txt", category: "complex", expect: { result: "open" } },
  { id: "codex-simple-csv", file: "simple.csv", format: "csv", category: "simple", expect: { result: "open", rows: 3, columns: 3, cells: [{ row: 2, column: 1, text: "001" }] } },
  { id: "codex-complex-csv", file: "complex.csv", format: "csv", category: "complex", expect: { result: "open", columns: 3 } },
  { id: "codex-simple-md", file: "simple.md", format: "md", category: "simple", expect: { result: "open", text: ["Hello"] } },
  { id: "codex-complex-markdown", file: "complex.markdown", format: "md", category: "complex", expect: { result: "open", text: ["Hello"] } }
].map(e => ({ ...e, producer: "Codex file-writing tool", licence: codexLicence, rules: SAFE_RULES }));

const all = [...codex, ...manifestEntries()];
const manifest = {
  description: "Plain Viewer test corpus. Paths are relative to tests/corpus. 'generated: true' files are created by 'npm run generate:large' and are not in git.",
  listener: "Hostile fixtures point at http://127.0.0.1:47831 and \\\\127.0.0.1@47831\\share. Run tests/harness/request-listener.mjs while testing; any recorded request is a failure.",
  expectedResults: "result: open | error | password. error: damaged | empty | mismatch | unsupported | password-cancelled.",
  fixtures: all
};
fs.writeFileSync(path.join(CORPUS, "manifest.json"), JSON.stringify(manifest, null, 2) + "\n");

// Refresh the generated section of SOURCES.md, leaving Codex's section untouched.
const sourcesPath = path.join(CORPUS, "SOURCES.md");
const start = "<!-- generated-fixtures:start -->", end = "<!-- generated-fixtures:end -->";
const rows = manifestEntries().map(e => `| ${e.file} | ${e.category} | ${e.producer} | ${e.licence} | ${describe(e)} |`);
const section = [start, "", "## Generated fixtures (Agent 2, tests/corpus/generate)", "",
  "Produced by `tests/corpus/generate` with libraries independent of the viewer's rendering engines, so a pass says something about fidelity. Regenerate with `npm ci` then `npm run generate` in that folder. Full expected results are in `manifest.json`.", "",
  "| File | Category | Producer | Licence | Expected result |", "|---|---|---|---|---|", ...rows, "", end].join("\n");
let sources = fs.readFileSync(sourcesPath, "utf8");
sources = sources.includes(start) ? sources.replace(new RegExp(`${start}[\\s\\S]*?${end}`), section) : sources.trimEnd() + "\n\n" + section + "\n";
fs.writeFileSync(sourcesPath, sources);

function describe(e) {
  const x = e.expect;
  if (x.result === "error") return `Clear "${x.error}" message; no crash`;
  if (x.result === "password") return "Asks for the password; opens with it";
  const parts = [];
  if (x.pages) parts.push(`${x.pages} page${x.pages === 1 ? "" : "s"}`);
  if (x.text?.length) parts.push(`text "${x.text[0]}"`);
  return `Opens; ${parts.join(", ")}${e.category === "attack" ? "; no network request" : ""}`;
}

console.log(`Wrote ${manifestEntries().length} generated fixtures (${large ? "including" : "without"} large files) and manifest.json with ${all.length} entries.`);
