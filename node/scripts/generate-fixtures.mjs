import { execFileSync } from "node:child_process";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import QRCode from "qrcode";
import sharp from "sharp";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const demo = path.join(root, "demo");
const tests = path.join(root, "tests", "fixtures");
fs.mkdirSync(demo, { recursive: true });
fs.mkdirSync(tests, { recursive: true });

const font = "/usr/share/fonts/truetype/wqy/wqy-microhei.ttc";
const latin = "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf";

function drawTextPng(out, w, h, lines) {
  const tmp = out + ".raw.png";
  const vf = lines
    .map(
      (L, i) =>
        `drawtext=fontfile=${L.font || font}:text='${L.text.replace(/'/g, "’")}':fontcolor=${L.color || "black"}:fontsize=${L.size || 36}:x=${L.x || 24}:y=${L.y ?? 40 + i * 48}`,
    )
    .join(",");
  execFileSync("ffmpeg", ["-y", "-f", "lavfi", "-i", `color=white:s=${w}x${h}`, "-vf", vf, "-frames:v", "1", tmp], {
    stdio: "ignore",
  });
  fs.copyFileSync(tmp, out);
  fs.rmSync(tmp, { force: true });
}

drawTextPng(path.join(demo, "ocr.png"), 1100, 320, [
  { text: "升级到 Windows 11 系统", size: 40, y: 48 },
  { text: "Hello World, open source tool", size: 36, y: 120, font: latin },
  { text: "BestSnap 离线 OCR 与公式识别", size: 32, y: 200 },
]);
fs.copyFileSync(path.join(demo, "ocr.png"), path.join(tests, "ocr.png"));

drawTextPng(path.join(demo, "formula.png"), 900, 220, [
  { text: "x = (-b +/- sqrt(b^2 - 4ac)) / 2a", size: 40, y: 90, font: latin, x: 40 },
]);

const qrPng = await QRCode.toBuffer("https://example.com/bestsnap", { type: "png", width: 320, margin: 2 });
fs.writeFileSync(path.join(demo, "qr.png"), qrPng);
fs.writeFileSync(path.join(tests, "qr.png"), qrPng);

// demo/page.html is hand-authored (target boxes for the live toolbar demo). Do not overwrite.

// A denser formula bitmap for the recognizer: white bg, large latin glyphs.
const formulaHtml = `<!DOCTYPE html><html><head><meta charset="utf-8"/>
<link rel="stylesheet" href="../node_modules/katex/dist/katex.min.css"/>
<style>html,body{margin:0;background:#fff}#m{padding:48px 40px;font-size:28px}</style>
</head><body><div id="m"></div>
<script src="../node_modules/katex/dist/katex.min.js"></script>
<script>katex.render("x = \\\\frac{-b \\\\pm \\\\sqrt{b^{2}-4ac}}{2a}", document.getElementById("m"), {throwOnError:false, displayMode:true});</script>
</body></html>`;
const formulaPage = path.join(demo, "formula.html");
fs.writeFileSync(formulaPage, formulaHtml);
const chrome = ["/usr/local/bin/google-chrome", "/usr/bin/google-chrome", "/usr/bin/chromium", "/usr/bin/chromium-browser"].find((p) => fs.existsSync(p));
const shot = path.join(demo, "formula-chrome.png");
if (chrome && !(fs.existsSync(shot) && fs.statSync(shot).size > 2000)) {
  try {
    execFileSync(
      chrome,
      ["--headless=new", "--disable-gpu", "--no-sandbox", "--window-size=1100,360", `--screenshot=${shot}`, "file://" + formulaPage],
      { stdio: "ignore", timeout: 45000 },
    );
  } catch {
    /* screenshot file may still have been written */
  }
}
if (fs.existsSync(shot) && fs.statSync(shot).size > 2000) {
  await sharp(shot)
    .trim({ background: "#ffffff", threshold: 12 })
    .extend({ top: 28, bottom: 28, left: 36, right: 36, background: "#ffffff" })
    .png()
    .toFile(path.join(demo, "formula.png"));
  fs.copyFileSync(path.join(demo, "formula.png"), path.join(tests, "formula.png"));
}
if (!fs.existsSync(path.join(demo, "formula.png"))) {
  const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="960" height="240">
    <rect width="100%" height="100%" fill="white"/>
    <text x="40" y="140" font-size="64" font-family="DejaVu Sans, serif">x = (-b ± √(b²-4ac)) / 2a</text>
  </svg>`;
  await sharp(Buffer.from(svg)).png().toFile(path.join(tests, "formula.png"));
  await sharp(Buffer.from(svg)).png().toFile(path.join(demo, "formula.png"));
}

console.log("fixtures written to", demo, "and", tests);
