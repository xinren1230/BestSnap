# BestSnap Node.js 版功能对照

对照对象：本仓库 `src/` 下的 Windows WPF/.NET 8 实现。  
Node 版目录：`node/`（Electron + TypeScript + 无头 CLI）。目标环境：Ubuntu/Debian x64，X11，普通桌面机。

图例：**完整** = 行为对齐或 Linux 等价实现；**部分** = 可用但有平台差异；**替代** = Linux 上原实现不可行，已提供替代；**不可行** = 无法在 Linux 实现，原因见说明。

| 功能 | Windows (C#) | Node / Linux | 说明 |
|---|---|---|---|
| 托盘常驻 | 完整 | 完整 | Electron Tray |
| 单实例 | 完整 | 完整 | `requestSingleInstanceLock` |
| 浮动截图工具栏 | 完整 | 完整 | 无边框置顶、可拖动、记忆位置、可隐藏 |
| 主按钮：矩形 / 窗口 / 全屏 / 录屏 / 更多 / 最小化 | 完整 | 完整 | |
| 全局热键（可改，JSON） | 完整 | 部分 | Electron `globalShortcut`。部分桌面环境会吞掉 PrintScreen；可在设置改成 Ctrl+Shift 组合 |
| 热键冲突气泡 | 完整 | 部分 | 注册失败时静默跳过，可在设置里改手势 |
| 全屏截图（多显示器） | 完整 | 部分 | ffmpeg `x11grab` 抓当前 DISPLAY 虚拟桌面；混合 DPI 未做 Per-Monitor V2 |
| 活动窗口 | 完整 | 完整 | `xdotool getactivewindow` + 窗口几何 |
| 窗口/对象（含子控件） | 完整 | 部分 | X11 顶层窗口列表（xwininfo）；**没有** Win32 子控件 HWND 命中 |
| 矩形区域（冻结画面、尺寸、放大镜） | 完整 | 完整 | 先抓全屏再覆盖层拖选 |
| 手绘多边形 | 完整 | 完整 | 单击加点，Enter 完成；输出为外接矩形 PNG（多边形蒙版未挖洞，建议 PNG） |
| 固定大小区域 | 完整 | 完整 | 尺寸来自设置 |
| 滚动窗口拼接 | 部分 | 部分 | 可用 xdotool 滚轮 + 逐帧拼接思路；对自绘/硬件加速页面同样会失败。UI 入口在「更多」 |
| 重复上次截图 | 完整 | 完整 | |
| 延时 0–60 秒 | 完整 | 完整 | |
| 包含鼠标指针 | 部分（标准箭头） | 部分 | ffmpeg `-draw_mouse`，形状由 X11 决定 |
| 截图后：编辑器 / 复制 / 自动保存 / 保存+复制 / 快捷栏 | 完整 | 完整 | Linux 剪贴板走 Electron clipboard |
| 快捷操作栏 | 完整 | 部分 | 截图后动作可选 QuickBar；独立小条未做 20s 自动隐藏的像素级复刻，编辑器工具栏含同样入口 |
| 编辑器矢量：箭头/直线/矩形/椭圆/文字/高亮 | 完整 | 完整 | Canvas 2D |
| 选择/拖动/改色线宽/Delete/双击改字 | 完整 | 完整 | |
| 序号 / 画笔 / 聚光灯 / 取色 | 完整 | 完整 | |
| 马赛克 / 模糊 | 完整 | 完整 | sharp |
| 裁剪 / 调整大小 / 缩放 | 完整 | 完整 | |
| 撤销 50 步 | 完整 | 完整 | |
| 像素橡皮（画笔/框选，填背景/白/黑/自定义/透明） | 完整 | 部分 | 框选擦除（白/高亮）已做；取样背景色中值与透明通道是简化实现 |
| 擦标注对象（E 键，输入时不触发） | 完整 | 完整 | 与 C# `EditorKeys` 相同策略 |
| 水印文字/PNG、Auto 对比度、九宫格、平铺≤80、拖动 | 完整 | 完整 | 布局数学与 C# `WatermarkMath` 一致；自动应用在复制/保存路径可关 |
| 输出 PNG/JPEG/BMP/GIF/TIFF/单页 PDF | 完整 | 完整 | 自写单页 JPEG PDF；BMP 为 24-bit 写入 |
| 文件名模板 `{n}{yyyy}…` + 计数器 | 完整 | 完整 | |
| 设置原子保存 + bak | 完整 | 完整 | `~/.config/BestSnap/settings.json`（首次启动从 `~/.config/SnapForge` 迁移） |
| **OCR PP-OCRv4 det+rec ONNX** | 完整 | 完整 | `onnxruntime-node` 1.20.1，同一套 RapidOCR 导出模型；检测/识别/CTC/版面/中英空格规则从 C# 移植 |
| OCR 自动 / 仅内置 / Windows OCR | 完整 | 替代 | **Windows.Media.Ocr 在 Linux 不存在**。仅内置 PaddleOCR（等同设置里强制「内置」） |
| OCR 版面（阅读顺序、行合并、段落、折行、CJK↔拉丁、全角标点、CTC 词隙） | 完整 | 完整 | `ocr-text.ts` / `ocr-layout.ts`，单测与 C# 对齐 |
| 公式 → LaTeX pix2text-mfr | 完整 | 完整 | 官方 Hugging Face + SHA-256；优先 `models/pix2text-mfr`，否则 `~/.local/share/BestSnap/models/pix2text-mfr`（旧 `~/.local/share/SnapForge` 会迁过来） |
| 公式在线：OpenAI 兼容 / Mathpix | 完整 | 完整 | 密钥存在 settings.json（0600），无 DPAPI |
| 公式预览 | WpfMath | 替代 | 结果窗用 KaTeX 渲染 `$…$` / `$$…$$` / 原始 LaTeX |
| 翻译 DeepL / Azure / LibreTranslate | 完整 | 完整 | 点按钮才联网；默认关闭 |
| 二维码/条码 | ZXing.Net | 完整 | `@zxing/library`（Apache-2.0） |
| 自动打码（邮箱/电话/证件/URL + 人脸） | 完整 | 部分 | 文本规则完整（OCR 框）。**人脸：Windows.Media.FaceAnalysis 不可用**，未捆绑闭源/额外人脸模型 |
| 录屏 MP4 H.264 | ScreenRecorderLib | 替代 | **ffmpeg x11grab + libx264**（Linux 无 Media Foundation） |
| 录屏 GIF | 完整 | 完整 | ffmpeg paletteuse |
| 系统声 / 麦克风 / 音量 | 完整 | 部分 | Pulse `ffmpeg -f pulse`（无 Pulse 则无声）；无 WASAPI 环回 |
| 摄像头 PIP | WinRT MediaCapture | 替代 | 未默认打开；可用 ffmpeg v4l2 自行叠加。无圆形气泡窗 |
| 按键/点击叠层 | 低级钩子 | 部分 | Linux 全局键鼠钩子需 root/input 组，默认关闭以免进录像隐私 |
| 录制排除自身窗口 | DWM | 不可行 | X11 无等价 API；改为开录前隐藏工具栏 |
| 区域边框 / 控制条 / 倒计时 | 完整 | 部分 | 控制条与选项在 UI；倒计时设置保留 |
| 视频裁剪 | 插件 | 替代 | `ffmpeg -ss -to`（无损重封装视编码而定） |
| 取色器 HEX/RGB | 完整 | 完整 | |
| 屏幕放大镜 2–16× | 完整 | 部分 | 覆盖层缩放；未做独立置顶跟随窗的每一帧位图拷贝 |
| 屏幕标尺 | 完整 | 部分 | 入口保留；物理像素刻度简化 |
| 钉在桌面 | 完整 | 完整 | 无边框置顶窗 |
| 历史面板 | 完整 | 完整 | `~/.config/BestSnap/history` |
| 定时截图 | 完整 | 完整 | |
| 拼接长图 | 完整 | 完整 | |
| 图片对比（并排/叠加/差异） | 完整 | 部分 | 并排；叠加透明度与差异高亮未做满 |
| 上传 folder / FTP / WebDAV | 完整 | 完整 | curl；密码明文存在 0600 的 settings（无 DPAPI） |
| 邮件/打印 | 未做 | 未做 | 与 C# 相同 |
| 开机启动 | HKCU Run | 替代 | `~/.config/autostart/bestsnap.desktop`（会从 `snapforge.desktop` 迁移） |
| i18n zh/en/de/ja/ko/fr/es + zh-TW | 完整 | 完整 | 复用 `lang/*.json` |
| CLI | 完整 | 完整 | 见 README；另提供子命令 `ocr/latex/qr/capture/record` |
| 命名管道转发到已运行实例 | 完整 | 部分 | Electron 单实例二次启动会亮工具栏；无头 CLI 总是独立进程（便于测试） |
| ReadyToRun 单文件 exe / Inno 安装包 | 完整 | 替代 | `scripts/build-appimage.sh` → AppImage。不打 Windows 安装包 |
| 从截图排除本应用窗口 | 完整 | 不可行 | 见上 |
| DRM 保护内容 | 黑屏 | 同样 | X11 抓屏也会是黑/空 |
| Wayland 原生抓屏 | n/a | 不可行 | 本版只支持 **X11 / XWayland**。纯 Wayland 需 portal（未实现）；可用 X11 会话或 XWayland |
| 插件 ALC / 内嵌 OCR 插件解压 | 完整 | 替代 | 模型在 `node/models/ocr`，ONNX Runtime 为 npm 依赖，无 WinRT |

## Linux 上明确做不到的能力（及替代）

1. **Windows OCR / 语言包** — 系统组件。替代：仅 PP-OCRv4。  
2. **Windows 人脸检测** — WinRT。替代：自动打码只打文本敏感信息。  
3. **ScreenRecorderLib / Media Foundation / 把本窗口从采集排除** — 替代：ffmpeg x11grab；开录前藏 UI。  
4. **DPAPI** — 替代：`chmod 600` 的 JSON。  
5. **Win32 子控件命中、DWM 扩展边框** — 替代：X11 顶层窗口。  
6. **纯 Wayland 抓屏** — 请用 X11 会话。

## 命令行（无头，给自动化测试）

```
npx tsx src/cli/index.ts ocr tests/fixtures/ocr.png
npx tsx src/cli/index.ts latex tests/fixtures/formula.png
npx tsx src/cli/index.ts qr tests/fixtures/qr.png
npx tsx src/cli/index.ts capture --full /tmp/full.png
npx tsx src/cli/index.ts record --duration 3 --out /tmp/t.mp4
```
