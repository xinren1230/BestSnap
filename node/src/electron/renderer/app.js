const q = new URLSearchParams(location.search);
const view = q.get("view") || "toolbar";
const sf = window.sf;
const app = document.getElementById("app");

const COLORS = ["#e11d48", "#f97316", "#eab308", "#22c55e", "#3b82f6", "#8b5cf6", "#111827", "#ffffff"];

function el(html) {
  const t = document.createElement("template");
  t.innerHTML = html.trim();
  return t.content.firstElementChild;
}

async function toolbar() {
  document.body.style.background = "transparent";
  app.innerHTML = "";
  const bar = el(`<div class="toolbar" id="bar">
    <img class="logo" src="./assets/logo_24.png" alt="" title="拖动移动工具栏" />
    <button id="btn-region" data-m="region" title="矩形区域">▢</button>
    <button id="btn-window" data-m="window" title="窗口/对象">▣</button>
    <button id="btn-full" data-m="full" title="全屏">⛶</button>
    <button id="btn-record" data-m="record" title="录屏">●</button>
    <button class="more" id="more">更多 ▾</button>
    <button id="btn-hide" data-a="hide" title="最小化到托盘">—</button>
  </div>`);
  const menu = el(`<div class="menu hidden" id="menu">
    <button id="menu-ocr" data-m="ocr">识别文字 (OCR)</button>
    <button id="menu-latex" data-m="latex">公式转 LaTeX</button>
    <button id="menu-qr" data-m="qr">识别二维码/条码</button>
    <button id="menu-history" data-a="history">截图历史…</button>
    <div class="hr"></div>
    <button data-m="active">截取活动窗口</button>
    <button data-m="freehand">手绘区域截图</button>
    <button data-m="fixed">固定大小区域截图</button>
    <button data-m="scroll">滚动窗口截图</button>
    <button data-m="repeat">重复上次截图</button>
    <button data-m="redact">自动打码</button>
    <div class="hr"></div>
    <button data-a="picker">屏幕取色器</button>
    <button data-a="mag">屏幕放大镜</button>
    <button data-a="ruler">屏幕标尺</button>
    <button data-a="open">打开图片到编辑器</button>
    <button data-a="timed">定时截图…</button>
    <button data-a="stitch">拼接长图…</button>
    <button data-a="compare">图片对比…</button>
    <div class="hr"></div>
    <button data-a="settings">设置</button>
  </div>`);
  bar.appendChild(menu);
  app.appendChild(bar);
  let drag = null;
  bar.querySelector(".logo").addEventListener("mousedown", (e) => { drag = { x: e.screenX, y: e.screenY }; });
  window.addEventListener("mouseup", () => { drag = null; });
  window.addEventListener("mousemove", (e) => {
    if (!drag) return;
    sf.invoke("toolbar:drag", e.screenX - drag.x, e.screenY - drag.y);
    drag = { x: e.screenX, y: e.screenY };
  });
  sf.on("recording", (m) => {
    const rec = document.getElementById("btn-record");
    if (rec) rec.classList.toggle("rec-on", !!(m && m.on));
  });
  bar.addEventListener("click", async (e) => {
    const b = e.target.closest("button");
    if (!b) return;
    if (b.id === "more") {
      menu.classList.toggle("hidden");
      sf.invoke("toolbar:menu", !menu.classList.contains("hidden"));
      return;
    }
    menu.classList.add("hidden");
    await sf.invoke("toolbar:menu", false);
    if (b.dataset.m === "record") return sf.invoke("record:toggle");
    if (b.dataset.m === "repeat") return sf.invoke("capture:start", "region");
    if (b.dataset.m) return sf.invoke("capture:start", b.dataset.m);
    const a = b.dataset.a;
    if (a === "hide") return sf.invoke("toolbar:hide");
    if (a === "settings") return sf.invoke("view:open", "settings");
    if (a === "history") return sf.invoke("view:open", "history");
    if (a === "picker") return sf.invoke("capture:start", "picker");
    if (a === "mag") return sf.invoke("capture:start", "mag");
    if (a === "ruler") return sf.invoke("capture:start", "ruler");
    if (a === "timed") return sf.invoke("capture:start", "timed");
    if (a === "stitch") return sf.invoke("view:open", "stitch");
    if (a === "compare") return sf.invoke("view:open", "compare");
    if (a === "open") return sf.invoke("view:open", "editor");
  });
}

function openPage(name) {
  // toolbar window cannot host full pages; main process opens them via capture:start side-effects.
  // Fallback: ask main to open via a dummy capture mode handled as views from other windows.
  window.open(`index.html?view=${name}`);
}

async function overlay() {
  document.documentElement.style.background = "#4a4d55";
  document.body.style.background = "#4a4d55";
  app.innerHTML = `<div class="overlay"><canvas id="cv"></canvas><div class="hint" id="hint">拖选区域 · Esc 取消</div><div class="size hidden" id="sz"></div></div>`;
  const cv = document.getElementById("cv");
  const ctx = cv.getContext("2d");
  const hint = document.getElementById("hint");
  let mode = "region", sw = 1920, sh = 1080, shot = "";
  let img = new Image();
  let a = null, b = null, poly = [];
  let wins = [];
  let fixedW = 400, fixedH = 300;
  function fit() {
    cv.width = innerWidth > 8 ? innerWidth : sw;
    cv.height = innerHeight > 8 ? innerHeight : sh;
    draw();
  }
  function draw() {
    ctx.clearRect(0, 0, cv.width, cv.height);
    if (img.naturalWidth) {
      ctx.drawImage(img, 0, 0, cv.width, cv.height);
      ctx.fillStyle = "rgba(0,0,0,.45)";
      ctx.fillRect(0, 0, cv.width, cv.height);
    } else {
      ctx.fillStyle = "#4a4d55";
      ctx.fillRect(0, 0, cv.width, cv.height);
    }
    if (mode === "freehand" && poly.length) {
      ctx.save(); ctx.beginPath();
      ctx.moveTo(poly[0].x, poly[0].y);
      poly.slice(1).forEach((p) => ctx.lineTo(p.x, p.y));
      if (poly.length > 2) ctx.closePath();
      ctx.clip();
      if (img.naturalWidth) ctx.drawImage(img, 0, 0, cv.width, cv.height);
      ctx.restore();
      ctx.strokeStyle = "#7c3aed"; ctx.lineWidth = 2;
      ctx.beginPath(); ctx.moveTo(poly[0].x, poly[0].y); poly.slice(1).forEach((p) => ctx.lineTo(p.x, p.y)); ctx.stroke();
      return;
    }
    if (!a || !b) return;
    const x = Math.min(a.x, b.x), y = Math.min(a.y, b.y), w = Math.abs(b.x - a.x), h = Math.abs(b.y - a.y);
    ctx.save(); ctx.beginPath(); ctx.rect(x, y, w, h); ctx.clip();
    if (img.naturalWidth) ctx.drawImage(img, 0, 0, cv.width, cv.height);
    ctx.restore();
    ctx.strokeStyle = "#7c3aed"; ctx.lineWidth = 2; ctx.strokeRect(x + 1, y + 1, w - 2, h - 2);
    const sz = document.getElementById("sz");
    sz.classList.remove("hidden");
    sz.style.left = x + "px"; sz.style.top = Math.max(0, y - 22) + "px";
    sz.textContent = Math.round(w * sw / cv.width) + " × " + Math.round(h * sh / cv.height);
  }
  async function prepare(p) {
    mode = p.mode || "region";
    sw = Number(p.sw) || 1920;
    sh = Number(p.sh) || 1080;
    shot = p.shot || "";
    a = null; b = null; poly = [];
    if (hint) hint.textContent = `拖选区域 · Esc 取消${mode === "window" ? " · 单击窗口" : ""}${mode === "freehand" ? " · 单击加点，Enter 完成" : ""}`;
    const bg = shot ? `url(file://${shot}) center / 100% 100% no-repeat #4a4d55` : "#4a4d55";
    document.documentElement.style.background = bg;
    document.body.style.background = bg;
    img = new Image();
    if (shot) {
      img.src = "file://" + shot;
      await img.decode().catch(() => {});
    }
    if (mode === "window") wins = (await sf.invoke("windows:list")) || [];
    if (mode === "fixed") {
      const s = await sf.invoke("settings:get");
      fixedW = s.fixedRegionWidth; fixedH = s.fixedRegionHeight;
      const sx = (cv.width || sw) / sw, sy = (cv.height || sh) / sh;
      a = { x: ((cv.width || sw) - fixedW * sx) / 2, y: ((cv.height || sh) - fixedH * sy) / 2 };
      b = { x: a.x + fixedW * sx, y: a.y + fixedH * sy };
    }
    if (mode === "picker") cv.style.cursor = "crosshair";
    else cv.style.cursor = "crosshair";
    fit();
    await new Promise((r) => requestAnimationFrame(() => requestAnimationFrame(r)));
    try { await sf.invoke("overlay:painted"); } catch { /* main may already have shown */ }
  }
  addEventListener("resize", fit);
  sf.on("overlay:prepare", (p) => { void prepare(p); });
  fit();
  if (q.get("shot")) {
    await prepare({ mode: q.get("mode"), shot: q.get("shot"), sw: q.get("sw"), sh: q.get("sh") });
  }
  function finishRect() {
    if (!a || !b || !shot) return;
    const x = Math.min(a.x, b.x), y = Math.min(a.y, b.y), w = Math.abs(b.x - a.x), h = Math.abs(b.y - a.y);
    if (w < 3 || h < 3) return;
    const sx = sw / cv.width, sy = sh / cv.height;
    const action = ["ocr", "latex", "qr", "redact", "pin"].includes(mode) ? mode : undefined;
    sf.invoke("capture:result", { file: shot, x: Math.round(x * sx), y: Math.round(y * sy), w: Math.round(w * sx), h: Math.round(h * sy), mode, action });
  }
  cv.addEventListener("mousedown", (e) => {
    if (mode === "freehand") { poly.push({ x: e.offsetX, y: e.offsetY }); draw(); return; }
    if (mode === "window" || mode === "picker") return;
    a = { x: e.offsetX, y: e.offsetY }; b = { ...a };
  });
  cv.addEventListener("mousemove", (e) => {
    if (mode === "fixed") {
      const sx = cv.width / sw, sy = cv.height / sh;
      a = { x: e.offsetX - fixedW * sx / 2, y: e.offsetY - fixedH * sy / 2 };
      b = { x: a.x + fixedW * sx, y: a.y + fixedH * sy };
      draw();
      return;
    }
    if (!a || mode === "freehand" || mode === "window" || mode === "picker") return;
    b = { x: e.offsetX, y: e.offsetY }; draw();
  });
  cv.addEventListener("mouseup", () => { if (mode !== "freehand" && mode !== "window" && mode !== "picker" && mode !== "fixed") finishRect(); });
  cv.addEventListener("click", async (e) => {
    if (mode === "fixed") { finishRect(); return; }
    if (mode === "window") {
      const sx = sw / cv.width, sy = sh / cv.height;
      const px = e.offsetX * sx, py = e.offsetY * sy;
      const hit = (wins || []).find((w) => px >= w.x && py >= w.y && px <= w.x + w.w && py <= w.y + w.h);
      if (!hit) return;
      sf.invoke("capture:result", { file: shot, x: hit.x, y: hit.y, w: hit.w, h: hit.h, mode: "window" });
      return;
    }
    if (mode === "picker") {
      const sx = sw / cv.width, sy = sh / cv.height;
      const c = await sf.invoke("color:at", Math.round(e.offsetX * sx), Math.round(e.offsetY * sy));
      const hex = "#" + [c.r, c.g, c.b].map((n) => n.toString(16).padStart(2, "0")).join("");
      await sf.invoke("clipboard:writeText", hex);
      alert(hex + `  RGB(${c.r}, ${c.g}, ${c.b})`);
      sf.invoke("capture:cancel");
    }
  });
  addEventListener("keydown", (e) => {
    if (e.key === "Escape") sf.invoke("capture:cancel");
    if (e.key === "Enter" && mode === "freehand" && poly.length > 2) {
      const xs = poly.map((p) => p.x), ys = poly.map((p) => p.y);
      a = { x: Math.min(...xs), y: Math.min(...ys) }; b = { x: Math.max(...xs), y: Math.max(...ys) };
      finishRect();
    }
  });
}

async function editor() {
  const file = q.get("file");
  document.documentElement.style.background = "#f4f4f5";
  app.className = "shell";
  app.innerHTML = `<div class="bar" id="bar"></div><div class="stage"><canvas id="cv"></canvas></div><div class="status" id="st"></div>`;
  const cv = document.getElementById("cv");
  const ctx = cv.getContext("2d");
  const img = new Image();
  img.src = "file://" + file;
  await img.decode();
  cv.width = img.width; cv.height = img.height;
  const tools = ["select", "arrow", "line", "rect", "ellipse", "text", "highlight", "step", "pen", "spotlight", "pixelate", "blur", "crop", "eyedropper", "eraser"];
  let tool = "arrow", color = COLORS[0], thick = 3, zoom = 1, stepN = 1;
  let anns = [];
  let undo = [], redo = [];
  let draft = null, sel = null, drag = null;
  const bar = document.getElementById("bar");
  bar.innerHTML = `
    <button data-a="copy">复制</button>
    <button data-a="save">保存</button>
    <button data-a="undo">撤销</button>
    <button data-a="redo">重做</button>
    ${[["arrow","箭头"],["rect","矩形"],["text","文字"],["pixelate","马赛克"],["eraser","橡皮"],["crop","裁剪"]].map(([t,l]) => `<button id="tool-${t}" data-t="${t}">${l}</button>`).join("")}
    <button class="accent" id="tool-ocr" data-a="ocr">OCR</button>
    <button class="accent" id="tool-latex" data-a="latex">公式LaTeX</button>
    <button id="tool-qr" data-a="qr">二维码</button>
    <button data-a="redact">自动打码</button>
    <button data-a="wm">水印</button>
    <select id="moreT">${["select","line","ellipse","highlight","step","pen","spotlight","blur","eyedropper"].map((t) => `<option value="${t}">${t}</option>`).join("")}</select>
    ${COLORS.map((c) => `<span class="swatch" data-c="${c}" style="background:${c}"></span>`).join("")}
    <select id="th">${[1,2,3,5,8,12].map((n) => `<option ${n===3?"selected":""}>${n}</option>`).join("")}</select>
  `;
  function snap() { undo.push(JSON.stringify({ anns, w: cv.width, h: cv.height })); if (undo.length > 50) undo.shift(); redo = []; }
  function draw() {
    ctx.setTransform(1,0,0,1,0,0);
    ctx.clearRect(0,0,cv.width,cv.height);
    ctx.drawImage(img, 0, 0);
    for (const a of anns) drawAnn(a);
    if (draft) drawAnn(draft, true);
    if (sel) { const r = bounds(sel); ctx.setLineDash([4,3]); ctx.strokeStyle = "#1d4ed8"; ctx.strokeRect(r.x-3,r.y-3,r.w+6,r.h+6); ctx.setLineDash([]); }
    document.getElementById("st").textContent = `${cv.width}×${cv.height}  工具:${tool}  标注:${anns.length}`;
  }
  function drawAnn(a, dash) {
    ctx.save();
    ctx.strokeStyle = a.color; ctx.fillStyle = a.color; ctx.lineWidth = a.thick; ctx.lineCap = "round";
    if (dash) ctx.setLineDash([5, 4]);
    const x = Math.min(a.x, a.x2), y = Math.min(a.y, a.y2), w = Math.abs(a.x2-a.x), h = Math.abs(a.y2-a.y);
    if (a.kind === "line" || a.kind === "arrow") { ctx.beginPath(); ctx.moveTo(a.x,a.y); ctx.lineTo(a.x2,a.y2); ctx.stroke();
      if (a.kind === "arrow") { const ang = Math.atan2(a.y2-a.y,a.x2-a.x); ctx.beginPath(); ctx.moveTo(a.x2,a.y2); ctx.lineTo(a.x2-12*Math.cos(ang-0.4), a.y2-12*Math.sin(ang-0.4)); ctx.lineTo(a.x2-12*Math.cos(ang+0.4), a.y2-12*Math.sin(ang+0.4)); ctx.closePath(); ctx.fill(); } }
    else if (a.kind === "rect") ctx.strokeRect(x,y,w,h);
    else if (a.kind === "ellipse") { ctx.beginPath(); ctx.ellipse(x+w/2,y+h/2,w/2,h/2,0,0,6.28); ctx.stroke(); }
    else if (a.kind === "highlight") { ctx.globalAlpha = 0.35; ctx.fillRect(x,y,w,h); }
    else if (a.kind === "text") { ctx.font = `${12+a.thick*3}px sans-serif`; ctx.fillText(a.text||"", a.x, a.y+16); }
    else if (a.kind === "step") { ctx.beginPath(); ctx.arc(a.x,a.y,10+a.thick*2,0,6.28); ctx.fill(); ctx.fillStyle="#fff"; ctx.font="bold 14px sans-serif"; ctx.textAlign="center"; ctx.fillText(String(a.n), a.x, a.y+5); }
    else if (a.kind === "pen" && a.pts) { ctx.beginPath(); a.pts.forEach((p,i) => i?ctx.lineTo(p.x,p.y):ctx.moveTo(p.x,p.y)); ctx.stroke(); }
    else if (a.kind === "spotlight") { ctx.fillStyle = "rgba(0,0,0,.55)"; ctx.fillRect(0,0,cv.width,cv.height); ctx.clearRect(x,y,w,h); ctx.drawImage(img,x,y,w,h,x,y,w,h); ctx.strokeRect(x,y,w,h); }
    else if (a.kind === "redact") { ctx.fillStyle = "#111"; ctx.fillRect(x,y,w,h); }
    else if (a.kind === "wm") { ctx.globalAlpha = (a.opacity??35)/100; ctx.font = `${a.size||28}px sans-serif`; ctx.translate(a.x, a.y); ctx.rotate((a.rot||-30)*Math.PI/180); ctx.fillText(a.text||"SnapForge", 0, 0); }
    ctx.restore();
  }
  function bounds(a) {
    if (a.kind === "text") return { x: a.x, y: a.y, w: 80, h: 24 };
    if (a.kind === "step") return { x: a.x-16, y: a.y-16, w: 32, h: 32 };
    if (a.pts) { const xs=a.pts.map(p=>p.x), ys=a.pts.map(p=>p.y); return { x:Math.min(...xs), y:Math.min(...ys), w:Math.max(...xs)-Math.min(...xs), h:Math.max(...ys)-Math.min(...ys)}; }
    return { x: Math.min(a.x,a.x2), y: Math.min(a.y,a.y2), w: Math.abs(a.x2-a.x), h: Math.abs(a.y2-a.y) };
  }
  function hit(p) {
    for (let i = anns.length-1; i>=0; i--) {
      const r = bounds(anns[i]);
      if (p.x>=r.x-4 && p.y>=r.y-4 && p.x<=r.x+r.w+4 && p.y<=r.y+r.h+4) return anns[i];
    }
    return null;
  }
  function pos(e) { const r = cv.getBoundingClientRect(); return { x: (e.clientX-r.left)*cv.width/r.width, y: (e.clientY-r.top)*cv.height/r.height }; }
  bar.addEventListener("click", async (e) => {
    const t = e.target;
    if (t.dataset && t.dataset.t) { tool = t.dataset.t; document.querySelectorAll("[data-t]").forEach((b)=>b.classList.toggle("active", b.dataset.t===tool)); }
    if (t.dataset.c) color = t.dataset.c;
    const a = t.dataset.a;
    if (a === "undo" && undo.length) { redo.push(JSON.stringify({anns})); const s=JSON.parse(undo.pop()); anns=s.anns; draw(); }
    if (a === "redo" && redo.length) { undo.push(JSON.stringify({anns})); const s=JSON.parse(redo.pop()); anns=s.anns; draw(); }
    if (a === "copy") { const p = cv.toDataURL("image/png"); const bin = atob(p.split(",")[1]); const arr=[...bin].map(c=>c.charCodeAt(0));
      const tmp = file.replace(/\.png$/,"-out.png"); await sf.invoke("save:image", arr, tmp); await sf.invoke("clipboard:write", tmp); }
    if (a === "save") { const dest = file.replace(/\.png$/,"-edit.png"); const p = cv.toDataURL("image/png"); const bin=atob(p.split(",")[1]); await sf.invoke("save:image", [...bin].map(c=>c.charCodeAt(0)), dest); alert("已保存 "+dest); }
    if (a === "ocr" || a === "latex" || a === "qr") {
      const p = cv.toDataURL("image/png").split(",")[1];
      const cmd = a === "ocr" ? "ocr" : a === "latex" ? "latex" : "qr";
      const r = await sf.invoke("worker", cmd, { image: p });
      const text = a === "qr"
        ? JSON.stringify(r.hits || [])
        : (r.text || "");
      await sf.invoke("result:show", { kind: a, text });
    }
    if (a === "redact") {
      const p = cv.toDataURL("image/png").split(",")[1];
      const r = await sf.invoke("worker", "redact", { image: p });
      snap();
      for (const h of r.hits || []) anns.push({ kind:"redact", x:h.x, y:h.y, x2:h.x+h.w, y2:h.y+h.h, color:"#111", thick:1 });
      draw();
    }
    if (a === "wm") {
      snap();
      const s = await sf.invoke("settings:get");
      anns.push({ kind:"wm", x: 40, y: cv.height-40, x2:40, y2:40, color:"#222", thick:1, text:s.watermark.text, size:s.watermark.fontSize, rot:s.watermark.rotation, opacity:s.watermark.opacity });
      draw();
    }
  });
  document.getElementById("moreT").onchange = (e) => { tool = e.target.value; };
  document.getElementById("th").onchange = (e) => { thick = parseFloat(e.target.value); };
  cv.addEventListener("mousedown", (e) => {
    const p = pos(e);
    if (tool === "select") { sel = hit(p); drag = sel ? { x:p.x, y:p.y, ox:sel.x, oy:sel.y, ox2:sel.x2, oy2:sel.y2 } : null; draw(); return; }
    if (tool === "eyedropper") { const d = ctx.getImageData(p.x|0,p.y|0,1,1).data; color = "#"+[d[0],d[1],d[2]].map(n=>n.toString(16).padStart(2,"0")).join(""); sf.invoke("clipboard:writeText", color); return; }
    if (tool === "text") {
      document.getElementById("sf-text-input")?.remove();
      const inp = document.createElement("input");
      inp.id = "sf-text-input";
      inp.placeholder = "输入文字后回车";
      inp.style.cssText = `position:fixed;left:${e.clientX}px;top:${e.clientY}px;z-index:30;font-size:18px;padding:4px 8px;border:2px solid #7c3aed;border-radius:6px;`;
      document.body.appendChild(inp);
      inp.focus();
      inp.onkeydown = (ev) => {
        if (ev.key !== "Enter") return;
        const text = inp.value.trim();
        inp.remove();
        if (!text) return;
        snap();
        anns.push({ kind:"text", x:p.x, y:p.y, x2:p.x+40, y2:p.y+20, color, thick, text });
        draw();
      };
      return;
    }
    if (tool === "step") { snap(); anns.push({ kind:"step", x:p.x, y:p.y, x2:p.x, y2:p.y, color, thick, n: stepN++ }); draw(); return; }
    draft = { kind: tool === "rect" ? "rect" : tool, x:p.x, y:p.y, x2:p.x, y2:p.y, color, thick, pts: tool==="pen"?[p]:undefined };
  });
  cv.addEventListener("mousemove", (e) => {
    const p = pos(e);
    if (drag && sel) { const dx=p.x-drag.x, dy=p.y-drag.y; sel.x=drag.ox+dx; sel.y=drag.oy+dy; sel.x2=drag.ox2+dx; sel.y2=drag.oy2+dy; draw(); return; }
    if (!draft) return;
    draft.x2 = p.x; draft.y2 = p.y;
    if (draft.pts) draft.pts.push(p);
    draw();
  });
  cv.addEventListener("mouseup", async () => {
    drag = null;
    if (!draft) return;
    if (tool === "crop") {
      const x=Math.min(draft.x,draft.x2)|0, y=Math.min(draft.y,draft.y2)|0, w=Math.abs(draft.x2-draft.x)|0, h=Math.abs(draft.y2-draft.y)|0;
      if (w>4 && h>4) { const url = cv.toDataURL(); const i2 = new Image(); i2.src = url; await i2.decode(); cv.width=w; cv.height=h; ctx.drawImage(i2, x,y,w,h,0,0,w,h); img.src = cv.toDataURL(); await img.decode(); anns=[]; }
      draft=null; draw(); return;
    }
    if (tool === "pixelate" || tool === "blur") {
      const x=Math.min(draft.x,draft.x2)|0, y=Math.min(draft.y,draft.y2)|0, w=Math.abs(draft.x2-draft.x)|0, h=Math.abs(draft.y2-draft.y)|0;
      const tmp = file + ".fx.png";
      const p = cv.toDataURL("image/png"); const bin=atob(p.split(",")[1]);
      await sf.invoke("save:image", [...bin].map(c=>c.charCodeAt(0)), tmp);
      const buf = await sf.invoke(tool==="pixelate"?"fx:pixelate":"fx:blur", tmp, x,y,w,h);
      const out = tmp+".out.png"; await sf.invoke("save:image", [...buf], out);
      img.src = "file://"+out; await img.decode(); cv.width=img.width; cv.height=img.height;
      draft=null; draw(); return;
    }
    if (tool === "eraser") { /* pixel fill handled as white rect */ draft.kind = "highlight"; draft.color = "#ffffff"; }
    snap(); anns.push(draft); draft=null; draw();
  });
  addEventListener("keydown", (e) => {
    const typing = ["INPUT","TEXTAREA","SELECT"].includes(document.activeElement?.tagName);
    if (e.key === "Delete" && sel) { snap(); anns = anns.filter((a)=>a!==sel); sel=null; draw(); }
    if ((e.ctrlKey||e.metaKey) && e.key==="z") { e.preventDefault(); bar.querySelector('[data-a="undo"]').click(); }
    if (!typing && !e.ctrlKey && !e.altKey && !e.shiftKey && e.key.toLowerCase()==="e") tool="eraser";
  });
  draw();
}

async function settingsView() {
  document.documentElement.style.background = "#f6f6f7";
  const s = await sf.invoke("settings:get");
  app.className = "page";
  app.innerHTML = `<h1>SnapForge 设置</h1>
    <h2>截图</h2>
    <div class="grid">
      <label>保存目录</label><input id="saveFolder" value="${s.saveFolder||""}" />
      <label>文件名模板</label><input id="tmpl" value="${s.fileNameTemplate||""}" />
      <label>默认格式</label><select id="fmt"><option>png</option><option>jpg</option><option>bmp</option><option>gif</option><option>tif</option><option>pdf</option></select>
      <label>JPEG 质量</label><input id="jq" type="number" min="1" max="100" value="${s.jpegQuality}" />
      <label>截图延时(秒)</label><input id="delay" type="number" min="0" max="60" value="${s.captureDelaySeconds}" />
      <label>包含鼠标</label><input id="cur" type="checkbox" ${s.includeCursor?"checked":""} />
      <label>截图后动作</label><select id="after"><option>OpenEditor</option><option>CopyToClipboard</option><option>AutoSave</option><option>AutoSaveAndClipboard</option><option>QuickBar</option></select>
      <label>固定区域</label><span><input id="fw" type="number" value="${s.fixedRegionWidth}" style="width:80px"> × <input id="fh" type="number" value="${s.fixedRegionHeight}" style="width:80px"></span>
      <label>语言</label><select id="lang"><option>auto</option><option>zh-CN</option><option>zh-TW</option><option>en</option><option>de</option><option>ja</option><option>ko</option><option>fr</option><option>es</option></select>
      <label>开机启动</label><input id="auto" type="checkbox" ${s.autostart?"checked":""} />
    </div>
    <h2>识别 / 公式</h2>
    <div class="grid">
      <label>公式引擎</label><select id="feng"><option>Local</option><option>OpenAI</option><option>Mathpix</option></select>
      <label>OpenAI Base</label><input id="obase" value="${s.formula.apiBase||""}" />
      <label>OpenAI 模型</label><input id="omodel" value="${s.formula.apiModel||""}" />
      <label>OpenAI Key</label><input id="okey" type="password" value="${s.formula.apiKey||""}" />
      <label>Mathpix app_id</label><input id="mid" value="${s.formula.mathpixAppId||""}" />
      <label>Mathpix key</label><input id="mkey" type="password" value="${s.formula.mathpixKey||""}" />
      <label>翻译服务</label><select id="tp"><option>None</option><option>DeepL</option><option>Azure</option><option>LibreTranslate</option></select>
      <label>翻译 Endpoint</label><input id="tep" value="${s.translate.endpoint||""}" />
      <label>翻译 Key</label><input id="tkey" type="password" value="${s.translate.key||""}" />
    </div>
    <h2>录屏</h2>
    <div class="grid">
      <label>格式</label><select id="rfmt"><option>Mp4</option><option>Gif</option></select>
      <label>帧率</label><input id="rfps" type="number" value="${s.recording.fps}" />
      <label>画质</label><input id="rq" type="number" value="${s.recording.quality}" />
      <label>麦克风</label><input id="rmic" type="checkbox" ${s.recording.microphone?"checked":""} />
      <label>保存文件夹</label><input id="rdir" value="${s.recording.saveFolder||""}" />
    </div>
    <h2>热键</h2>
    <div id="hk"></div>
    <p><button id="save">保存</button></p>`;
  document.getElementById("fmt").value = s.defaultFormat || "png";
  document.getElementById("after").value = s.afterCapture;
  document.getElementById("lang").value = s.language || "auto";
  document.getElementById("feng").value = s.formula.engine;
  document.getElementById("tp").value = s.translate.provider;
  document.getElementById("rfmt").value = s.recording.format;
  const hk = document.getElementById("hk");
  hk.innerHTML = s.hotkeys.map((h,i)=>`<div class="grid"><label>${h.action}</label><input data-hk="${i}" value="${h.gesture}"></div>`).join("");
  document.getElementById("save").onclick = async () => {
    const next = {
      ...s,
      saveFolder: document.getElementById("saveFolder").value,
      fileNameTemplate: document.getElementById("tmpl").value,
      defaultFormat: document.getElementById("fmt").value,
      jpegQuality: +document.getElementById("jq").value,
      captureDelaySeconds: +document.getElementById("delay").value,
      includeCursor: document.getElementById("cur").checked,
      afterCapture: document.getElementById("after").value,
      fixedRegionWidth: +document.getElementById("fw").value,
      fixedRegionHeight: +document.getElementById("fh").value,
      language: document.getElementById("lang").value,
      autostart: document.getElementById("auto").checked,
      formula: { ...s.formula, engine: document.getElementById("feng").value, apiBase: document.getElementById("obase").value, apiModel: document.getElementById("omodel").value, apiKey: document.getElementById("okey").value, mathpixAppId: document.getElementById("mid").value, mathpixKey: document.getElementById("mkey").value },
      translate: { ...s.translate, provider: document.getElementById("tp").value, endpoint: document.getElementById("tep").value, key: document.getElementById("tkey").value },
      recording: { ...s.recording, format: document.getElementById("rfmt").value, fps: +document.getElementById("rfps").value, quality: +document.getElementById("rq").value, microphone: document.getElementById("rmic").checked, saveFolder: document.getElementById("rdir").value },
      hotkeys: s.hotkeys.map((h,i)=>({ action:h.action, gesture: document.querySelector(`[data-hk="${i}"]`).value })),
    };
    await sf.invoke("settings:save", next);
    alert("已保存");
  };
}

async function historyView() {
  document.documentElement.style.background = "#f6f6f7";
  const items = await sf.invoke("history:list");
  app.className = "page";
  app.innerHTML = `<h1>截图历史</h1><div class="hist">${(items||[]).map((i)=>`<div><img data-p="${i.path}" src="file://${i.path}" /><div>${new Date(i.time).toLocaleString()}</div></div>`).join("")||"<p>暂无</p>"}</div>`;
  app.onclick = (e) => { const im = e.target.closest("img"); if (im) sf.invoke("history:open", im.dataset.p); };
}

function resultView() {
  document.documentElement.style.background = "#f6f6f7";
  app.className = "page";
  const kind = q.get("kind") || "ocr";
  app.innerHTML = `<h1>${kind === "latex" ? "公式 → LaTeX" : kind === "qr" ? "二维码/条码" : "OCR"}</h1>
    <pre id="tx"></pre>
    <div id="kv" class="${kind==="latex"?"":"hidden"}" style="background:#fff;border:1px solid #ddd;padding:20px;min-height:80px;margin:12px 0;font-size:28px;color:#111"></div>
    <p>
      <button id="copy">复制</button>
      <button id="inline" class="${kind==="latex"?"":"hidden"}">复制 $…$</button>
      <button id="block" class="${kind==="latex"?"":"hidden"}">复制 $$…$$</button>
      <button id="trzh" class="${kind==="ocr"?"":"hidden"}">译为中文</button>
      <button id="tren" class="${kind==="ocr"?"":"hidden"}">译为英文</button>
      <button id="open" class="hidden">打开链接</button>
    </p>`;
  let text = "";
  function showResult(m) {
    if (!m) return;
    if (m.kind === "qr") {
      try {
        const hits = JSON.parse(m.text || "[]");
        if (Array.isArray(hits)) {
          text = hits.map((h) => (h.format || "QR") + "\n" + h.text).join("\n\n");
          const url = hits.find((h) => /^https?:\/\//i.test(h.text || ""));
          if (url) { document.getElementById("open").classList.remove("hidden"); document.getElementById("open").onclick = () => sf.invoke("open:external", url.text.trim()); }
        } else text = String(m.text || "");
      } catch {
        text = String(m.text || "");
      }
    } else text = m.text || "";
    document.getElementById("tx").textContent = text;
    const kv = document.getElementById("kv");
    if (kv && kind === "latex" && window.katex && text) {
      try { window.katex.render(String(text).replace(/^\$+|\$+$/g, ""), kv, { throwOnError: false, displayMode: true }); }
      catch { kv.textContent = text; }
    }
  }
  sf.on("result", showResult);
  document.getElementById("copy").onclick = () => sf.invoke("clipboard:writeText", text);
  document.getElementById("inline").onclick = () => sf.invoke("clipboard:writeText", "$" + text + "$");
  document.getElementById("block").onclick = () => sf.invoke("clipboard:writeText", "$$" + text + "$$");
  document.getElementById("trzh").onclick = async () => { const r = await sf.invoke("worker", "translate", { text, target: "zh" }); document.getElementById("tx").textContent = r.text; };
  document.getElementById("tren").onclick = async () => { const r = await sf.invoke("worker", "translate", { text, target: "en" }); document.getElementById("tx").textContent = r.text; };
}

function pinView() {
  app.innerHTML = `<div class="pin"><img src="file://${q.get("file")}" /></div>`;
}

async function stitchView() {
  document.documentElement.style.background = "#f6f6f7";
  const items = await sf.invoke("history:list");
  app.className = "page";
  app.innerHTML = `<h1>拼接长图</h1><p>从历史选择两张以上，再点拼接。</p>
    <div class="hist">${(items||[]).map((i)=>`<label><input type="checkbox" value="${i.path}" /><img src="file://${i.path}" /></label>`).join("")}</div>
    <p><label><input type="checkbox" id="vert" checked /> 纵向</label> <button id="go">拼接并打开编辑器</button></p>`;
  document.getElementById("go").onclick = async () => {
    const files = [...app.querySelectorAll("input[type=checkbox]:checked")].map((i) => i.value).filter((v)=>v && v.endsWith(".png") || v.endsWith(".jpg"));
    const real = [...app.querySelectorAll(".hist input:checked")].map((i)=>i.value);
    if (real.length < 2) return alert("请至少选两张");
    const out = await sf.invoke("stitch", real, document.getElementById("vert").checked);
    sf.invoke("history:open", out);
  };
}

async function compareView() {
  const items = await sf.invoke("history:list");
  app.className = "page";
  app.innerHTML = `<h1>图片对比</h1>
    <p><select id="a">${(items||[]).map(i=>`<option value="${i.path}">${i.file}</option>`).join("")}</select>
       <select id="b">${(items||[]).map(i=>`<option value="${i.path}">${i.file}</option>`).join("")}</select></p>
    <div style="display:flex;gap:8px"><img id="ia" style="max-width:48%"><img id="ib" style="max-width:48%"></div>`;
  const sync = () => { document.getElementById("ia").src = "file://"+document.getElementById("a").value; document.getElementById("ib").src="file://"+document.getElementById("b").value; };
  app.onchange = sync; sync();
}

async function demoView() {
  document.documentElement.style.background = "#0f1115";
  document.body.style.background = "#0f1115";
  app.innerHTML = `
    <div id="demo" style="position:fixed;inset:0;display:flex;flex-direction:column;background:#0f1115;color:#f4f4f5;font-family:Noto Sans CJK SC,WenQuanYi Micro Hei,sans-serif;">
      <div id="cap" style="height:64px;display:flex;align-items:center;padding:0 28px;background:#1a1030;font-size:28px;letter-spacing:.04em;border-bottom:3px solid #7c3aed;"></div>
      <div id="stage" style="flex:1;position:relative;overflow:hidden;display:flex;align-items:center;justify-content:center;"></div>
    </div>`;
  const cap = document.getElementById("cap");
  const stage = document.getElementById("stage");
  const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
  function title(s) { cap.textContent = s; }
  function html(s) { stage.innerHTML = s; }
  const root = await sf.invoke("root");

  title("SnapForge for Linux");
  html(`<div style="text-align:center">
    <img src="./assets/logo_128.png" width="128" height="128" alt="" />
    <h1 style="font-size:56px;margin:16px 0 8px">SnapForge</h1>
    <p style="font-size:24px;color:#c4b5fd">离线 OCR · 公式 → LaTeX · 截图 / 标注 / 录屏</p>
    <p style="color:#8e8e93;margin-top:24px">Apache-2.0 · Node.js + Electron · Ubuntu / X11</p>
  </div>`);
  try { await sf.invoke("worker", "ping", {}); } catch { /* worker still starting */ }
  await sleep(7000);

  title("1 / 7  区域截图");
  html(`<div style="position:relative;width:1280px;height:720px;background:#fff;color:#111;border-radius:8px;overflow:hidden;box-shadow:0 12px 40px #0008">
    <iframe src="${"file://" + root + "/demo/page.html"}" style="width:1280px;height:720px;border:0" id="pg"></iframe>
    <div id="mask" style="position:absolute;inset:0;background:rgba(0,0,0,.45);pointer-events:none"></div>
    <div id="sel" style="position:absolute;border:3px solid #7c3aed;box-shadow:0 0 0 9999px rgba(0,0,0,.45);left:80px;top:70px;width:40px;height:30px"></div>
  </div>`);
  const sel = document.getElementById("sel");
  for (let i = 0; i <= 24; i++) {
    sel.style.width = 40 + i * 22 + "px";
    sel.style.height = 30 + i * 12 + "px";
    await sleep(90);
  }
  await sleep(4000);

  title("2 / 7  编辑标注  ·  箭头 / 文字 / 马赛克");
  const shot = "file://" + root + "/demo/ocr.png";
  html(`<div style="background:#d8d8dc;padding:20px;border-radius:8px">
    <canvas id="ed" width="1100" height="360" style="background:#fff;display:block"></canvas>
    <div style="margin-top:8px;color:#333">工具：箭头 → 文字 → 马赛克</div>
  </div>`);
  const cv = document.getElementById("ed");
  const ctx = cv.getContext("2d");
  const im = new Image();
  im.src = shot;
  await im.decode().catch(() => {});
  ctx.fillStyle = "#fff"; ctx.fillRect(0,0,cv.width,cv.height);
  if (im.naturalWidth) ctx.drawImage(im, 20, 20, 1060, 280);
  ctx.strokeStyle = "#e11d48"; ctx.lineWidth = 5; ctx.lineCap = "round";
  ctx.beginPath(); ctx.moveTo(80, 300); ctx.lineTo(260, 90); ctx.stroke();
  ctx.beginPath(); ctx.moveTo(260,90); ctx.lineTo(230,120); ctx.lineTo(215,95); ctx.closePath(); ctx.fillStyle="#e11d48"; ctx.fill();
  await sleep(2500);
  ctx.fillStyle = "#1d4ed8"; ctx.font = "bold 32px sans-serif"; ctx.fillText("重点", 280, 80);
  await sleep(2500);
  const mx = 40, my = 150, mw = 520, mh = 56;
  const imgd = ctx.getImageData(mx, my, mw, mh);
  const b = 14;
  for (let y = 0; y < mh; y += b) for (let x = 0; x < mw; x += b) {
    const i = ((y * mw) + x) * 4;
    const r = imgd.data[i], g = imgd.data[i+1], bl = imgd.data[i+2];
    ctx.fillStyle = `rgb(${r},${g},${bl})`;
    ctx.fillRect(mx+x, my+y, b, b);
  }
  await sleep(4000);

  title("3 / 7  OCR  ·  中英文识别（PP-OCRv4 离线）");
  html(`<div style="display:flex;gap:24px;align-items:stretch;width:1600px">
    <img src="${shot}" style="max-width:720px;background:#fff;border-radius:8px" />
    <pre id="ocrOut" style="flex:1;background:#16161a;padding:20px;border-radius:8px;font-size:22px;white-space:pre-wrap;line-height:1.5">识别中…</pre>
  </div>`);
  try {
    const b64 = await sf.invoke("demo:file", "ocr.png");
    const r = await sf.invoke("worker", "ocr", { image: b64 });
    document.getElementById("ocrOut").textContent = r.text || "(empty)";
  } catch (e) {
    document.getElementById("ocrOut").textContent = "OCR 失败: " + e;
  }
  await sleep(12000);

  title("4 / 7  公式 → LaTeX  ·  pix2text-mfr 离线识别 + 渲染");
  html(`<div style="display:flex;gap:24px;width:1700px;align-items:flex-start">
    <div style="flex:1;background:#fff;color:#111;border-radius:8px;padding:16px">
      <iframe src="${"file://" + root + "/demo/page.html#formula"}" style="width:100%;height:280px;border:0"></iframe>
      <img src="${"file://" + root + "/demo/formula.png"}" style="width:100%;background:#fff" alt="formula" />
    </div>
    <div style="flex:1">
      <div style="background:#16161a;padding:16px;border-radius:8px;margin-bottom:16px">
        <div style="color:#a78bfa;margin-bottom:8px">识别结果（LaTeX）</div>
        <pre id="tex" style="font-size:20px;white-space:pre-wrap">识别中（首次加载约 112MB 模型）…</pre>
      </div>
      <div style="background:#fff;color:#111;padding:24px;border-radius:8px;min-height:140px">
        <div style="color:#666;margin-bottom:8px">渲染预览</div>
        <div id="texview" style="font-size:28px"></div>
      </div>
    </div>
  </div>`);
  try {
    const b64 = await sf.invoke("demo:file", "formula.png");
    const r = await sf.invoke("worker", "latex", { image: b64 });
    const tex = r.text || "";
    document.getElementById("tex").textContent = tex;
    const view = document.getElementById("texview");
    if (window.katex) {
      try { window.katex.render(tex.replace(/^\$+|\$+$/g, ""), view, { throwOnError: false, displayMode: true }); }
      catch { view.textContent = tex; }
    } else view.textContent = tex;
  } catch (e) {
    document.getElementById("tex").textContent = "公式识别失败: " + e;
  }
  await sleep(14000);

  title("5 / 7  二维码 / 条码识别");
  html(`<div style="display:flex;gap:40px;align-items:center">
    <img src="${"file://" + root + "/demo/qr.png"}" style="width:280px;height:280px;background:#fff;padding:12px;border-radius:8px" />
    <pre id="qrOut" style="font-size:24px;background:#16161a;padding:24px;border-radius:8px;min-width:480px">识别中…</pre>
  </div>`);
  try {
    const b64 = await sf.invoke("demo:file", "qr.png");
    const r = await sf.invoke("worker", "qr", { image: b64 });
    const hits = r.hits || [];
    document.getElementById("qrOut").textContent = hits.length ? hits.map((h) => h.format + "\n" + h.text).join("\n\n") : "未识别到二维码或条码。";
  } catch (e) {
    document.getElementById("qrOut").textContent = "识别失败: " + e;
  }
  await sleep(9000);

  title("6 / 7  录屏  ·  ffmpeg x11grab  ·  MP4 / GIF");
  html(`<div style="width:960px">
    <div style="background:#111;border-radius:10px;padding:16px 20px;display:flex;align-items:center;gap:16px">
      <div id="recdot" style="width:18px;height:18px;border-radius:50%;background:#ef4444"></div>
      <div style="font-size:22px">录制中  <span id="rct">00:00</span></div>
      <div style="margin-left:auto;opacity:.7">1920×1080 · 30 fps · H.264</div>
    </div>
    <p style="color:#a1a1aa;margin-top:24px;font-size:20px">区域 / 全屏 / 窗口 · 可选麦克风 · 结束后可裁剪为 GIF</p>
  </div>`);
  for (let i = 0; i <= 7; i++) {
    document.getElementById("rct").textContent = "00:0" + i;
    document.getElementById("recdot").style.opacity = i % 2 ? "0.3" : "1";
    await sleep(800);
  }
  await sleep(2500);

  title("7 / 7  截图历史");
  html(`<div style="display:grid;grid-template-columns:repeat(3, 280px);gap:16px">
    ${["ocr.png","formula.png","qr.png"].map((f,i)=>`<div style="background:#16161a;border-radius:8px;overflow:hidden">
      <img src="${"file://"+root+"/demo/"+f}" style="width:280px;height:160px;object-fit:cover;background:#fff" />
      <div style="padding:10px 12px;color:#d4d4d8">示例 ${i+1}</div>
    </div>`).join("")}
  </div>`);
  await sleep(9000);

  title("SnapForge  ·  开源 Apache-2.0");
  html(`<div style="text-align:center">
    <p style="font-size:32px;margin:0 0 12px">npm start 启动界面  ·  npx tsx src/cli/index.ts 命令行</p>
    <p style="font-size:20px;color:#a1a1aa">ocr / latex / qr / capture / record</p>
  </div>`);
  await sleep(7000);
  await sf.invoke("quit");
}

function captionView() {
  document.documentElement.style.background = "#1a1030";
  document.body.style.background = "#1a1030";
  app.innerHTML = `<div id="cap" style="height:56px;display:flex;align-items:center;padding:0 28px;font-size:22px;color:#f4f4f5;letter-spacing:.04em;border-top:3px solid #7c3aed;">SnapForge for Linux</div>`;
  const cap = document.getElementById("cap");
  sf.on("caption", (t) => { cap.textContent = String(t ?? ""); });
}

function backdropView() {
  document.documentElement.style.background = "#eef0f4";
  document.body.style.background = "#eef0f4";
  app.innerHTML = `<iframe id="pg" src="../demo-page.html" style="position:fixed;inset:0;width:100%;height:100%;border:0"></iframe>`;
}

const pages = { toolbar, overlay, editor, settings: settingsView, history: historyView, result: resultView, pin: pinView, stitch: stitchView, compare: compareView, demo: demoView, caption: captionView, backdrop: backdropView };
void Promise.resolve((pages[view] || toolbar)()).catch((e) => { app.textContent = String(e); });
