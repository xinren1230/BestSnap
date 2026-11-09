#!/usr/bin/env node
/** Drive the real Electron toolbar + windows with xdotool. Used by run-demo.sh. */
import { execFileSync } from "node:child_process";

const BASE = process.env.SNAPFORGE_DEMO_CTL || "http://127.0.0.1:38471";
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const notes = [];

function xd(args) {
  execFileSync("xdotool", args, { stdio: "ignore" });
}

async function caption(t) {
  try {
    await fetch(`${BASE}/caption?t=${encodeURIComponent(t)}`);
  } catch {
    /* caption window optional */
  }
  console.log("caption:", t);
}

async function layout() {
  const r = await fetch(`${BASE}/layout`);
  if (!r.ok) throw new Error("layout http " + r.status);
  return r.json();
}

async function waitServer(timeout = 15000) {
  const t0 = Date.now();
  while (Date.now() - t0 < timeout) {
    try {
      const r = await fetch(BASE);
      if (r.ok) return;
    } catch { /* retry */ }
    await sleep(200);
  }
  throw new Error("demo control server not listening on " + BASE);
}

async function waitWin(titlePart, timeout = 12000) {
  const t0 = Date.now();
  while (Date.now() - t0 < timeout) {
    const all = await layout();
    if (all[titlePart]) return { title: titlePart, ...all[titlePart] };
    const k = Object.keys(all).find((n) => n.includes(titlePart));
    if (k) return { title: k, ...all[k] };
    await sleep(200);
  }
  throw new Error("window not found: " + titlePart);
}

async function hoverClick(sx, sy, hoverMs = 650) {
  xd(["mousemove", "--sync", String(Math.round(sx)), String(Math.round(sy))]);
  await sleep(hoverMs);
  xd(["click", "1"]);
  await sleep(220);
}

async function drag(x1, y1, x2, y2, steps = 16) {
  xd(["mousemove", "--sync", String(Math.round(x1)), String(Math.round(y1))]);
  await sleep(200);
  xd(["mousedown", "1"]);
  for (let i = 1; i <= steps; i++) {
    const x = x1 + ((x2 - x1) * i) / steps;
    const y = y1 + ((y2 - y1) * i) / steps;
    xd(["mousemove", "--sync", String(Math.round(x)), String(Math.round(y))]);
    await sleep(28);
  }
  xd(["mouseup", "1"]);
  await sleep(280);
}

function closeNamed(part) {
  try {
    xd(["search", "--onlyvisible", "--name", part, "windowclose"]);
  } catch {
    /* gone */
  }
}

async function toolbar() {
  const all = await layout();
  const tb = all.SnapForge;
  if (!tb) throw new Error("toolbar window SnapForge missing; have " + Object.keys(all));
  return tb;
}

async function clickToolbar(id, hoverMs = 700) {
  const tb = await toolbar();
  const b = tb.buttons?.[id];
  if (!b) throw new Error(`toolbar button ${id} missing (${Object.keys(tb.buttons || {})})`);
  await hoverClick(b.sx, b.sy, hoverMs);
}

async function clickMoreThen(id) {
  await clickToolbar("more", 550);
  await sleep(400);
  const tb = await toolbar();
  const b = tb.buttons?.[id];
  if (!b) throw new Error(`menu button ${id} missing (${Object.keys(tb.buttons || {})})`);
  await hoverClick(b.sx, b.sy, 650);
}

async function targetBox(id) {
  const page = await waitWin("Demo Page", 8000);
  const t = page.buttons?.[id];
  if (!t) throw new Error(`target ${id} missing`);
  const pad = 8;
  return {
    x1: t.sx - t.w / 2 + pad,
    y1: t.sy - t.h / 2 + pad,
    x2: t.sx + t.w / 2 - pad,
    y2: t.sy + t.h / 2 - pad,
  };
}

async function overlayGone() {
  const all = await layout();
  return !Object.keys(all).some((n) => /Capture/i.test(n));
}

async function selectOverlay(targetId) {
  await waitWin("Capture", 8000);
  await sleep(350);
  const box = await targetBox(targetId);
  await drag(box.x1, box.y1, box.x2, box.y2, 18);
  const t0 = Date.now();
  while (Date.now() - t0 < 3500) {
    if (await overlayGone()) return;
    await sleep(150);
  }
  try { xd(["key", "Escape"]); } catch { /* ignore */ }
  await sleep(250);
  if (!(await overlayGone())) throw new Error("overlay still open after drag " + targetId);
}

async function waitResultReady(timeout = 18000) {
  const t0 = Date.now();
  let last = "";
  while (Date.now() - t0 < timeout) {
    const all = await layout();
    const k = Object.keys(all).find((n) => /Result/i.test(n));
    if (k) {
      last = String(all[k].text || "");
      const ready = !/识别中/.test(last) && (
        /升级到|Hello World|离线 OCR|frac|sqrt|-b|example\.com|https?:\/\//i.test(last)
        || last.replace(/\s+/g, "").length > 48
      );
      if (ready) return last;
    }
    await sleep(250);
  }
  if (!last) throw new Error("result window never appeared");
  return last;
}

async function showResultThenClose(holdMs = 2200, timeout = 18000) {
  await waitResultReady(timeout);
  await wander(holdMs, 1180, 300, 160, 90);
  closeNamed("SnapForge Result");
  await sleep(800);
}

async function wander(ms, cx = 360, cy = 280, rx = 200, ry = 110) {
  const t0 = Date.now();
  let i = 0;
  while (Date.now() - t0 < ms) {
    const x = cx + Math.round(Math.cos(i / 2.4) * rx);
    const y = cy + Math.round(Math.sin(i / 2.4) * ry);
    xd(["mousemove", "--sync", String(x), String(y)]);
    await sleep(110);
    i++;
  }
}

async function step(name, fn) {
  try {
    await fn();
    notes.push(`${name}: ok`);
  } catch (e) {
    const msg = e instanceof Error ? e.message : String(e);
    notes.push(`${name}: FAIL ${msg}`);
    console.error("step failed", name, msg);
    await caption(`失败 · ${name} · ${msg.slice(0, 80)}`);
    await sleep(1200);
  }
}

await waitServer();
await waitWin("SnapForge", 12000);
await caption("SnapForge  ·  浮动工具栏");
for (const id of ["btn-region", "btn-window", "btn-full", "btn-record", "more"]) {
  try {
    const tb = await toolbar();
    const b = tb.buttons?.[id];
    if (b) {
      xd(["mousemove", "--sync", String(Math.round(b.sx)), String(Math.round(b.sy))]);
      await sleep(600);
    }
  } catch { /* intro hover is best-effort */ }
}

await step("region", async () => {
  await caption("1 / 7  区域截图  ·  点击工具栏 ▢");
  await clickToolbar("btn-region", 750);
  await selectOverlay("ocr-target");
  await waitWin("Editor", 8000);
  await sleep(1000);
});

await step("annotate", async () => {
  await caption("2 / 7  编辑标注  ·  箭头 / 文字 / 马赛克");
  const ed = await waitWin("Editor", 4000);
  const arrow = ed.buttons?.["tool-arrow"];
  const text = ed.buttons?.["tool-text"];
  const pix = ed.buttons?.["tool-pixelate"];
  const cv = ed.buttons?.cv;
  if (!arrow || !text || !pix || !cv) throw new Error("editor tools missing");
  await hoverClick(arrow.sx, arrow.sy, 500);
  await drag(cv.sx - cv.w * 0.35, cv.sy + cv.h * 0.25, cv.sx + cv.w * 0.05, cv.sy - cv.h * 0.2);
  await sleep(400);
  await hoverClick(text.sx, text.sy, 500);
  await hoverClick(cv.sx + cv.w * 0.12, cv.sy - cv.h * 0.18, 320);
  await sleep(350);
  const ed2 = await waitWin("Editor", 4000);
  const inp = ed2.buttons?.["sf-text-input"];
  if (inp) await hoverClick(inp.sx, inp.sy, 280);
  await fetch(`${BASE}/clip?t=${encodeURIComponent("重点")}`);
  await sleep(120);
  try { xd(["key", "--clearmodifiers", "ctrl+v"]); } catch { /* ignore */ }
  await sleep(180);
  xd(["key", "--clearmodifiers", "Return"]);
  await sleep(500);
  await hoverClick(pix.sx, pix.sy, 550);
  await drag(cv.sx - cv.w * 0.22, cv.sy + 4, cv.sx + cv.w * 0.28, cv.sy + cv.h * 0.3);
  await sleep(2000);
  closeNamed("SnapForge Editor");
  await sleep(1400);
});

await step("ocr", async () => {
  await caption("3 / 7  OCR  ·  更多 → 识别文字");
  await clickMoreThen("menu-ocr");
  await selectOverlay("ocr-target");
  await showResultThenClose(2200, 15000);
});

await step("latex", async () => {
  await caption("4 / 7  公式 → LaTeX  ·  更多 → 公式转 LaTeX");
  await clickMoreThen("menu-latex");
  await selectOverlay("math");
  await showResultThenClose(2500, 20000);
});

await step("qr", async () => {
  await caption("5 / 7  二维码  ·  更多 → 识别二维码");
  await clickMoreThen("menu-qr");
  await selectOverlay("qr-target");
  await showResultThenClose(4200, 12000);
});

await step("record", async () => {
  await caption("6 / 7  录屏  ·  点击工具栏 ●");
  await clickToolbar("btn-record", 750);
  await wander(2500, 320, 260, 70, 40);
  await clickToolbar("btn-record", 600);
  await sleep(500);
});

await step("history", async () => {
  await caption("7 / 7  截图历史");
  await clickMoreThen("menu-history");
  await waitWin("History", 8000);
  await wander(4200, 1180, 280, 180, 100);
  closeNamed("SnapForge History");
  await sleep(600);
});

await caption("SnapForge  ·  Apache-2.0  · npm start");
await sleep(2500);
console.log("demo-notes:");
for (const n of notes) console.log(" -", n);
process.exit(notes.some((n) => n.includes("FAIL")) ? 2 : 0);
