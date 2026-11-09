# BestSnap 浏览器版（源码，本地运行）

在本机浏览器里运行的静态站点源码（Vite + TypeScript，目前不提供在线托管）：截图、标注、离线 OCR、公式 → LaTeX、二维码、录屏。  
推理用 [onnxruntime-web](https://github.com/microsoft/onnxruntime)（WASM，可选 WebGPU），**图片不上传**。许可证 [Apache-2.0](../../LICENSE)。

模型文件不进 git（公式 encoder 约 84 MB）。构建时从本仓库已有的 PP-OCRv4 ONNX 拷贝，并从 [Hugging Face pix2text-mfr](https://huggingface.co/breezedeus/pix2text-mfr)（MIT）拉取公式模型，校验 SHA-256。

横版演示（1920×1080，约 56 秒）在 [`public/snapforge-linux-demo.mp4`](public/snapforge-linux-demo.mp4)，页面顶部介绍区用 `<video controls playsinline>` 嵌入。

```bash
cd node/web
npm install
npx playwright install chromium
npm test          # 构建 + Playwright（上传测试图 → OCR / LaTeX / QR）
npm run preview   # http://127.0.0.1:4173
```

本地开发：`npm run dev`。

第三方许可见 [../licenses/THIRD-PARTY-NOTICES.txt](../licenses/THIRD-PARTY-NOTICES.txt)。
