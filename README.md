# BestSnap

**最好用的截图神器，OCR、LaTeX公式识别一键搞定**

[🌐 在线网页版 / Try it online](https://xinren1230.github.io/BestSnap/) · [⬇️ 下载安装包 / Download installer](https://github.com/xinren1230/BestSnap/releases/latest/download/BestSnap-Setup-1.1.1.exe) · [🎬 演示视频 / Demo video](docs/demo.mp4)

[![BestSnap 演示 / demo](docs/demo.gif)](docs/demo.mp4)

> 点击上图播放完整演示视频（约 56 秒）。Click the animation to watch the full demo (~56 s).

---

## 中文

### 功能

- 📸 **截图**：框选区域、窗口、全屏；常驻顶部工具栏，一点就用
- ✏️ **标注**：箭头、矩形、文字，复制或保存 PNG
- 🔤 **OCR 文字识别**：中英文混排一键提取文字（PP-OCRv4）
- ∑ **公式转 LaTeX**：截一下公式，直接得到 LaTeX 代码并实时渲染预览
- 🔳 **二维码 / 条码识别**：框一下，链接马上出来
- 🎥 **录屏**：一键开始、再点一次停止
- 🕘 **截图历史**：自动保存，随时找回

### 在线网页版

不用安装，打开即用：**https://xinren1230.github.io/BestSnap/**

支持上传、粘贴或拖放图片，也可以直接在浏览器里截取屏幕、窗口或标签页。首次使用公式识别时会下载约 112 MB 模型，之后浏览器会缓存。推荐使用新版 Chrome / Edge。Linux、macOS 用户可以直接用网页版。

### 下载

- **Windows**（Windows 10/11 x64）当前版本 **1.1.1**：
  - [⬇️ 直接下载安装包 BestSnap-Setup-1.1.1.exe](https://github.com/xinren1230/BestSnap/releases/latest/download/BestSnap-Setup-1.1.1.exe)
  - 或打开 [Releases 页面](https://github.com/xinren1230/BestSnap/releases/latest) 查看校验文件与说明
  - 一键安装，所有依赖（运行时、OCR 和公式模型）已打包在内，已用 Certum 代码签名
- **Linux / macOS**：使用[在线网页版](https://xinren1230.github.io/BestSnap/)。

### 隐私

所有识别都在本地完成：Windows 版在你的电脑上离线运行，网页版的模型在你的浏览器里运行。**截图和图片不会上传到任何服务器。**

### 许可

BestSnap 使用 [Apache-2.0](LICENSE) 许可，© 2026 Chen Xinren。第三方组件（PP-OCRv4 / RapidOCR、pix2text-mfr、ONNX Runtime、KaTeX、ZXing 等）保留各自许可，见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。

---

## English

**The best screenshot tool — OCR and formula-to-LaTeX in one click.**

### Features

- 📸 **Capture** a region, a window or the full screen from an always-on top toolbar
- ✏️ **Annotate** with arrows, rectangles and text; copy or save as PNG
- 🔤 **OCR**: extract mixed Chinese/English text (PP-OCRv4)
- ∑ **Formula → LaTeX**: snip an equation and get LaTeX code with a live preview
- 🔳 **QR / barcode** decoding
- 🎥 **Screen recording** with one click
- 🕘 **Capture history**, saved automatically

### Web version

No install needed: **https://xinren1230.github.io/BestSnap/**

Upload, paste or drop an image, or capture a screen, window or tab right in the browser. The formula model (~112 MB) is downloaded on first use and then cached. A recent Chrome or Edge is recommended. Linux and macOS users can use the web version.

### Download

- **Windows** (10/11, x64), current version **1.1.1**:
  - [⬇️ Direct download BestSnap-Setup-1.1.1.exe](https://github.com/xinren1230/BestSnap/releases/latest/download/BestSnap-Setup-1.1.1.exe)
  - Or open the [Releases page](https://github.com/xinren1230/BestSnap/releases/latest) for the checksum and notes
  - One-click installer with every dependency (runtime, OCR and formula models) bundled, Certum code-signed, SHA256 on the release page
- **Linux / macOS**: use the [web version](https://xinren1230.github.io/BestSnap/).

### Privacy

Everything runs locally: the Windows app works offline on your PC, and the web version runs its models inside your browser. **Your screenshots and images are never uploaded.**

### License

BestSnap is licensed under [Apache-2.0](LICENSE), © 2026 Chen Xinren. Third-party components (PP-OCRv4 / RapidOCR, pix2text-mfr, ONNX Runtime, KaTeX, ZXing, …) keep their own licenses; see [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
