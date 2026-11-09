import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const dest = path.join(root, "dist", "electron", "renderer");
fs.mkdirSync(dest, { recursive: true });
for (const f of ["index.html", "styles.css", "app.js"]) {
  fs.copyFileSync(path.join(root, "src", "electron", "renderer", f), path.join(dest, f));
}
fs.copyFileSync(path.join(root, "src", "electron", "preload.cjs"), path.join(root, "dist", "electron", "preload.cjs"));
const assetsDest = path.join(dest, "assets");
fs.mkdirSync(assetsDest, { recursive: true });
for (const f of fs.readdirSync(path.join(root, "assets"))) {
  fs.copyFileSync(path.join(root, "assets", f), path.join(assetsDest, f));
}
const vendor = path.join(dest, "vendor");
fs.mkdirSync(vendor, { recursive: true });
const katex = path.join(root, "node_modules", "katex", "dist");
if (fs.existsSync(path.join(katex, "katex.min.js"))) {
  fs.copyFileSync(path.join(katex, "katex.min.js"), path.join(vendor, "katex.min.js"));
  fs.copyFileSync(path.join(katex, "katex.min.css"), path.join(vendor, "katex.min.css"));
  const fontsSrc = path.join(katex, "fonts");
  const fontsDst = path.join(vendor, "fonts");
  if (fs.existsSync(fontsSrc)) {
    fs.mkdirSync(fontsDst, { recursive: true });
    for (const f of fs.readdirSync(fontsSrc)) fs.copyFileSync(path.join(fontsSrc, f), path.join(fontsDst, f));
  }
}
console.log("copied renderer static files →", dest);
