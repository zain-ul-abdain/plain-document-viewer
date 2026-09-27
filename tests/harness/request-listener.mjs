// Records every request that reaches 127.0.0.1:47831, the address the hostile fixtures point at (HTTP, and
// WebDAV when Windows turns a \127.0.0.1@47831\share path into a web request). Any recorded request other
// than the harness's own canary means a document caused network access.
//   node request-listener.mjs <log-file>
import http from "node:http";
import fs from "node:fs";

const port = Number(process.env.LISTENER_PORT ?? 47831);
const log = process.argv[2] ?? "request-log.jsonl";
fs.writeFileSync(log, "");
const server = http.createServer((request, response) => {
  const entry = { time: new Date().toISOString(), method: request.method, url: request.url, agent: request.headers["user-agent"] ?? "" };
  fs.appendFileSync(log, JSON.stringify(entry) + "\n");
  response.writeHead(404, { "Content-Type": "text/plain" });
  response.end("recorded by the Plain Viewer test listener");
});
server.listen(port, "127.0.0.1", () => console.log(`Listening on 127.0.0.1:${port}; recording to ${log}`));
for (const signal of ["SIGINT", "SIGTERM"]) process.on(signal, () => server.close(() => process.exit(0)));
