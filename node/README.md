# BestSnap（Node.js / Linux）

Windows 版 BestSnap 的 Linux 移植：离线中英文 OCR（PP-OCRv4）、公式 → LaTeX（pix2text-mfr）、截图、标注编辑、二维码、录屏、录音、录后剪辑、仅转写（不保存音频）。  
界面用 Electron，引擎和 CLI 是普通 Node.js（`onnxruntime-node`），可在无显示器的机器上跑识别。

许可证 [Apache-2.0](../LICENSE)。第三方组件保留原许可，见 [licenses/THIRD-PARTY-NOTICES.txt](licenses/THIRD-PARTY-NOTICES.txt)。功能对照：[FEATURES.md](FEATURES.md)。

**目标系统：** Ubuntu / Debian x64，X11（或 XWayland），已装 Chromium/Electron 能跑的普通桌面。

---

## 在一台全新 Ubuntu 桌面机上从零运行

```bash
sudo apt-get update
sudo apt-get install -y git curl ca-certificates ffmpeg xdotool x11-utils xvfb \
  libgtk-3-0 libnotify4 libnss3 libxss1 libxtst6 xdg-utils libsecret-1-0 \
  fonts-wqy-microhei fonts-dejavu-core

# Node.js 20+
curl -fsSL https://deb.nodesource.com/setup_22.x | sudo -E bash -
sudo apt-get install -y nodejs

git clone https://github.com/xinren1230/BestSnap.git
cd BestSnap

cd node
bash scripts/setup.sh          # apt 已装则可跳过 sudo 部分；npm ci、拷贝 OCR 模型、下载公式模型并校验 SHA-256
npm test                       # OCR / LaTeX / QR 等
npm start                      # Electron 工具栏
```

命令行（不启 UI）：

```bash
cd node
npx tsx src/cli/index.ts ocr path/to.png -o out.txt
npx tsx src/cli/index.ts latex path/to-formula.png
npx tsx src/cli/index.ts qr path/to-qr.png
npx tsx src/cli/index.ts capture --full out.png
npx tsx src/cli/index.ts capture --region 100,100,800,600 -o crop.png
npx tsx src/cli/index.ts record --duration 5 --out rec.mp4
```

首次公式识别会从 [Hugging Face pix2text-mfr](https://huggingface.co/breezedeus/pix2text-mfr) 拉取约 112 MB，并按与 C# 版相同的 SHA-256 校验。也可预先执行：

```bash
bash scripts/download-models.sh
```

模型缓存目录：`~/.local/share/BestSnap/models/pix2text-mfr`。  
设置与历史：`~/.config/BestSnap/`。首次启动会把旧版 `~/.local/share/SnapForge` 与 `~/.config/SnapForge` 迁到 BestSnap 目录。

### AppImage

```bash
cd node
bash scripts/build-appimage.sh
# 产物在 dist-app/*.AppImage
```

公式模型不打进 AppImage（体积）；运行时仍按上面的规则下载。

---

## 浏览器版源码（仅本地运行）

`web/` 是浏览器版源码，目前不提供在线托管，只在本机构建运行。截图用 `getDisplayMedia` / 粘贴 / 拖放 / 上传；OCR 与公式用 `onnxruntime-web` 在本地 WASM 推理；历史进 IndexedDB。

横版演示视频（1920×1080，约 56 秒）嵌在页面介绍区：[`web/public/bestsnap-linux-demo.mp4`](web/public/bestsnap-linux-demo.mp4)。

```bash
cd node/web
npm install
npx playwright install chromium
npm test
npm run preview    # http://127.0.0.1:4173
```

详情见 `web/README.md`。

---

## 架构

- `src/core` — OCR / 公式 / 二维码 / 截图 / 录屏 / 录音 / 剪辑 / 导出，CLI 与 UI 共用
- `src/cli` — 无头命令
- `src/electron` — 托盘、工具栏、选区、编辑器、设置、演示模式 `--demo`
- OCR 在 **系统 Node** 子进程里跑，避开 Electron ABI 与 `onnxruntime-node` 不匹配

截图与录屏依赖 **ffmpeg x11grab**，因此需要 X11。Wayland 纯会话不支持。

---

## 测试与演示

```bash
cd node
npm test
bash scripts/smoke-electron.sh          # Xvfb 下启动并截一张图
bash demo/run-demo.sh                   # 录 1920×1080 演示 MP4
```

演示脚本说明见 [demo/README.md](demo/README.md)。成品横版 MP4：[`web/public/bestsnap-linux-demo.mp4`](web/public/bestsnap-linux-demo.mp4)。
