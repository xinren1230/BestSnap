# BestSnap

BestSnap：用 C# / WPF / .NET 8 从零编写的独立 Windows 工具。对外主打两件事：**离线中英文 OCR**（内置 PaddleOCR PP-OCRv4）和 **公式 → LaTeX**（默认 pix2text-mfr）。截图、标注编辑、像素橡皮、水印、录屏是配套能力。

仓库首页说明（能力边界、模型是否离线）见 [README.md](../README.md)。

## 环境要求
- Windows 10 1903+ / Windows 11（x64）
- .NET 8 SDK（https://dotnet.microsoft.com/download/dotnet/8.0）
- NuGet 依赖：`ScreenRecorderLib 6.6.0`（录屏用，基于 Windows Media Foundation，**不需要 FFmpeg**；运行时需要 VC++ 2015-2022 x64 运行库）、`NAudio 2.2.1`（MIT，纯音频录制：WASAPI 采集/环回 + Media Foundation 编码 MP3）、`Whisper.net 1.8.1` + `Whisper.net.Runtime`（MIT，本机语音转写；模型首次使用下载到 `%LocalAppData%\BestSnap\models\whisper`）

## 编译与运行
```powershell
cd BestSnap
dotnet build -c Release            # 平台已默认为 x64（ScreenRecorderLib 不支持 AnyCPU）
dotnet run --project src/BestSnap -c Release
# 发布单目录可执行文件
dotnet publish src/BestSnap -c Release -r win-x64 --self-contained false -o publish
```
也可直接用 Visual Studio 2022 打开 `BestSnap.sln`，平台选 x64。
在 Linux/macOS 上只能做编译检查：`dotnet build -p:EnableWindowsTargeting=true`。一键安装包（Inno Setup）只能在 Windows 上构建：先 `winget install --id JRSoftware.InnoSetup -e`，再 `.\build.ps1 -Installer`。脚本在 `installer\bestsnap.iss`。

启动后会在屏幕顶部显示**浮动截图工具栏**（无边框、置顶、可拖动左侧 Logo 移动，位置自动记忆），同时驻留系统托盘：右键托盘图标打开菜单（含“显示/隐藏工具栏”），左键托盘图标显示/还原工具栏。工具栏最右侧为最小化到托盘；设置中可关闭“启动时显示截图工具栏”。

主工具栏按钮：矩形区域、窗口/对象、全屏、录屏、**更多**、最小化。OCR、公式 → LaTeX、延时、手绘、滚动、取色、放大镜、标尺、历史、设置等在 **更多** 菜单（热键仍可用）。图标使用 Windows 自带的 Segoe Fluent Icons / Segoe MDL2 Assets 字体。

## Logo
原创 Logo（渐变圆角方块 + 白色取景框 + 镜头 + 黄色星芒），源文件 `src/BestSnap/Assets/bestsnap_logo.svg`，由 Python cairosvg/Pillow 生成 16–256 PNG 和多尺寸 `bestsnap.ico`，用作 exe 图标、托盘图标（嵌入资源）、所有窗口图标和工具栏 Logo。

## 默认全局热键（可在“设置”中修改，保存到 JSON）
| 功能 | 热键 |
|---|---|
| 全屏 | Shift+PrintScreen |
| 活动窗口 | Alt+PrintScreen |
| 窗口/对象 | Ctrl+Shift+W |
| 矩形区域 | Ctrl+PrintScreen |
| 手绘区域 | Ctrl+Shift+F |
| 固定大小区域 | Ctrl+Shift+X |
| 滚动窗口 | Ctrl+Alt+PrintScreen |
| 重复上次截图 | Ctrl+Shift+R |
| 开始/停止录屏 | Ctrl+Shift+V |
| 识别文字 (OCR) | Ctrl+Shift+O |
| 公式转 LaTeX | Ctrl+Shift+L |
| 钉在桌面 | Ctrl+Shift+P |
| 历史 | Ctrl+Shift+H |
| 定时截图 | Ctrl+Shift+T |
| 识别二维码/条码 | Ctrl+Shift+Q |

热键被其他程序占用时会弹托盘气泡提示。可在“设置 → 热键”修改。

## 单实例
程序只允许运行一个实例；重复启动时会通知已运行的实例显示工具栏后退出。

## 单文件发布
```powershell
dotnet publish src/BestSnap -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
```
ScreenRecorderLib.dll 和 Ijwhost.dll 是混合模式程序集，csproj 中的 `ExcludeSRL` 目标会把它们留在 exe 旁边（不打包进单文件）。

## 设置
保存在 `%AppData%\BestSnap\settings.json`：保存目录、文件名模板（`{yyyy}{MM}{dd}{HH}{mm}{ss}{n}`）、默认格式、JPEG 质量、截图延时、是否包含鼠标、截图后动作（打开编辑器/复制/自动保存/保存+复制）、固定区域尺寸、录屏帧率/声音、热键表。首次启动会把旧版 `%AppData%\SnapForge` 与 `%LocalAppData%\SnapForge`（设置、历史、模型缓存）迁到 BestSnap 目录；开机自启注册表项从 `SnapForge` 改为 `BestSnap`。

## 功能状态
| 功能 | 状态 | 说明 |
|---|---|---|
| 托盘程序 + 全局热键 | ✅ 完整 | RegisterHotKey + 消息窗口 |
| 浮动截图工具栏 | ✅ 完整 | 置顶、可拖动、记忆位置、可隐藏到托盘 |
| 全屏截图（多显示器） | ✅ 完整 | 截取整个虚拟桌面 |
| 活动窗口 | ✅ 完整 | 使用 DWM 扩展边框，去掉阴影 |
| 窗口/对象 | ✅ 完整 | 鼠标悬停高亮顶层窗口或子控件，单击截取 |
| 矩形区域 | ✅ 完整 | 冻结屏幕后拖选，显示尺寸和放大预览 |
| 手绘区域 | ✅ 完整 | 多边形外部透明（建议存 PNG） |
| 固定大小区域 | ✅ 完整 | 尺寸在设置中配置 |
| 滚动窗口 | ⚠️ 部分 | 发送滚轮消息+逐帧比对拼接；对普通网页/文档有效，对自绘或硬件加速应用（部分 Chrome/Electron 页面、有悬浮工具栏的页面）可能失败或出现拼缝 |
| 重复上次截图 | ✅ 完整 | 区域类模式重复同一坐标，其他模式重跑同一模式 |
| 延时截图 | ✅ 完整 | 0–60 秒 |
| 包含鼠标指针 | ⚠️ 部分 | 总是绘制标准箭头，不是当前实际光标形状 |
| 编辑器：箭头/直线/矩形/椭圆/文字/高亮 | ✅ 完整 | 8 色调色板、线宽可选 |
| 编辑器：马赛克/模糊 | ✅ 完整 | |
| 编辑器：裁剪/调整大小/缩放/撤销重做 | ✅ 完整 | 撤销 50 步；Ctrl+滚轮缩放 |
| 编辑器：已画标注再次选中修改 | ✅ 完整 | 矢量对象：选择工具可再选中、拖动、改颜色/线宽、Delete；复制/保存时才画进位图 |
| 编辑器：像素橡皮 | ✅ 完整 | 默认擦截图像素；次要模式擦标注对象 |
| 编辑器：水印 | ✅ 完整 | 文字或 PNG；Auto 对比度；九宫格或平铺（上限 80）；非平铺可拖动 |
| 输出：剪贴板 / PNG/JPG/BMP/GIF/TIFF | ✅ 完整 | |
| 输出：PDF | ✅ 完整 | 内置极简 PDF 写入（单页、JPEG 嵌入），无第三方依赖 |
| 自动保存 + 文件名模板 | ✅ 完整 | |
| OCR（PaddleOCR PP-OCRv4 / Windows OCR） | ✅ 完整 | 需 OCR 插件（默认嵌在单文件 exe）。内置模型离线。版面与中英混排见 README |
| 公式 → LaTeX | ✅ 完整 | 默认 pix2text-mfr；安装包把模型放到 `{app}\models\pix2text-mfr`，优先用安装目录，缺失才下载约 112 MB 并 SHA-256 校验；可选 OpenAI 兼容视觉 API / Mathpix |
| 录屏 MP4 / GIF | ✅ 基本完整 | 工具栏录制菜单可选屏幕/音频/仅转写。录制前弹出选项窗口：全屏(选显示器或所有显示器)/框选区域/指定窗口、系统声音、麦克风+设备、鼠标指针、点击高亮、帧率(10/15/24/30/60)、画质、倒计时、记住设置；录制时显示置顶控制条（开始/暂停/继续/停止 + 计时，Win10 2004+ 不会被录进视频，区域边框同样排除）；跨显示器区域用全部显示器源 + 虚拟桌面裁剪。停止后写入 `%LocalAppData%\BestSnap\temp\recordings`，完成窗预览 + 剪辑 + 转写 + 摘要 + 另存；关闭未保存会确认丢弃。超过 7 天的临时文件启动时清理 |
| 纯音频录制 WAV / MP3 | ✅ 完整 | 托盘/工具栏/热键 Ctrl+Shift+A。麦克风、WASAPI 环回或混合；设备选择；实时电平；暂停/继续；记住上次文件夹与设备。同样先写临时文件再另存。MP3 经 Media Foundation（N 版 Windows 可能缺少编码器） |
| 仅转写（不保存音频） | ✅ 完整 | 工具栏录制菜单与音频窗口。麦克/系统设备同录音。8s 窗口 + 1.5s 重叠滚动送入本机 Whisper；VAD（RMS）；置顶可缩放字幕窗（捕获排除）。音频只在内存，转写后立即丢弃。停止后复制 / 保存 txt、srt、摘要。暂停/继续 |
| 本机转写 Whisper | ✅ 完整 | Whisper.net（MIT）。默认 base 多语言模型，首次下载到 `%LocalAppData%\BestSnap\models\whisper`。ffmpeg 抽 16 kHz 单声道（WAV 可本机抽）。时间戳、复制、txt/srt。后台线程可取消 |
| 摘要 | ✅ 完整 | 转写旁摘要按钮。可配 OpenAI 兼容 chat-completions（设置里的 Chat，或复用公式 OpenAI）；长转写分块 map-reduce。未配置时用本机 TextRank 抽取式摘要并标明。Copy / 保存 .md。首次联网前一行提示。音频从不上传 |
| 录后剪辑（中间剪切） | ✅ 完整 | 预览 + 时间轴；可标记多段删除（含中间）；WAV 波形。默认导出 `原名_trimmed`，覆盖原文件需确认。优先 ffmpeg 重编码切段保 A/V 同步；PCM WAV 可无 ffmpeg 直切；单段 MP4 可回退 WinRT 插件。完成窗 Trim 作用于临时文件，另存的是剪辑结果 |
| 取色器 | ✅ 完整 | 显示 HEX/RGB/坐标，单击复制 HEX |
| 屏幕放大镜 | ✅ 完整 | 跟随鼠标，滚轮调整倍率 2–16x，Esc/双击关闭 |
| 屏幕标尺 | ✅ 完整 | 物理像素刻度，R 旋转，滚轮调整长度 |
| 设置界面 + JSON 持久化 | ✅ 完整 | |
| 多显示器 + DPI 感知 | ✅ 基本完整 | Per-Monitor V2 清单；选区覆盖层按物理像素定位。混合 DPI 下覆盖层上的提示文字大小可能不一致 |
| 上传 FTP/FTPS/WebDAV | ✅ 基本完整 | 密码 DPAPI（当前用户）；邮件/打印未做 |

## 代码结构
```
src/BestSnap/
  App.xaml(.cs)            启动入口（无主窗口，托盘常驻）
  Models/AppSettings.cs    设置模型
  ViewModels/              EditorDocument(图片+撤销栈)、SettingsViewModel
  Services/                AppController(托盘/流程)、HotkeyService、CaptureService、
                           ScrollCaptureService、ImageExporter、Effects、RecorderService、SettingsService
  Views/                   SelectionOverlay(选区/窗口/取色)、EditorWindow、MagnifierWindow、
                           RulerWindow、SettingsWindow、InputDialog
  Native/                  Win32 P/Invoke、窗口枚举
```

## 已知限制
- 运行时只支持 Windows。Linux/macOS 仅可做 `dotnet build -p:EnableWindowsTargeting=true` 编译检查。
- 截图基于 GDI（CopyFromScreen），受保护内容（DRM 视频）会是黑色。
- OCR 没有表格/多栏还原；公式识别质量随裁剪和所选引擎变化。内置 OCR 离线；安装包已含 pix2text-mfr，绿色包或缺模型时才下载；翻译和在线公式会把内容发到你配置的服务。
- 安装包不在 `{app}` 旁放置 `plugins\`。OCR 插件完整嵌在单文件 exe 里。残缺的 side-by-side `plugins\BestSnap.Ocr` 会挡住内嵌副本，导致公式识别找不到 OnnxRuntime。

## 日志与设置补充
- 未处理异常会弹窗提示并写入 `%AppData%\BestSnap\logs\yyyy-MM-dd.log`。
- 设置“编辑器/设置窗口也从截图录屏中排除”（默认开）；工具栏、录屏控制条、倒计时、放大镜、选区遮罩、区域边框、录音窗口、完成对话框、实时字幕窗始终排除。
- 转写用本机 Whisper（MIT）。音频不会上传。摘要仅在你配置了聊天接口且确认过提示后才会发送**文本**。
- 多段 MP4/MP3 剪辑需要本机 PATH 上的 ffmpeg（不随安装包分发，避免 GPL）。PCM WAV 可无 ffmpeg 直切。Windows N 版可能没有 Media Foundation MP3 编码器，此时录音会留下 WAV。
- 发布默认启用 ReadyToRun（仅增加约 180 KB，启动更快）。

## 第二批改进（编辑器与截图）
- 编辑器标注（箭头/直线/矩形/椭圆/文字/高亮）为可编辑对象：「选择」工具可选中、拖动、方向键微调、改颜色/线宽、Delete 删除、双击改文字；撤销/重做基于命令。马赛克/模糊/裁剪/调整大小作用于底图（标注随裁剪/缩放同步移动）。
- 滚动截图：真实滚轮输入（SendInput），自动识别固定页眉/页脚，右下角预览面板可“完成/取消”。
- 文件名模板占位符：{n} {yyyy} {yy} {MM} {dd} {HH} {mm} {ss} {fff} {yyyyMMdd} {HHmmss} {date} {time}；其余花括号原样保留。序号只在真正保存后递增。
- 设置原子保存（settings.json.bak 备份），损坏时自动从备份恢复并提示。

## Batch 3
- History panel, pin-to-desktop, editor: step numbers / text font, size, background / pen / spotlight / eyedropper.
- OCR + video trimmer are an optional plugin (`src/BestSnap.Ocr`, net8.0-windows10.0.19041.0). Publish:
  `dotnet publish src/BestSnap.Ocr -c Release -r win-x64 --self-contained false -o publish/plugins/BestSnap.Ocr`
  and put the output in `<exe dir>\plugins\BestSnap.Ocr\` (BestSnap.Ocr.dll + Microsoft.Windows.SDK.NET.dll + WinRT.Runtime.dll + .deps.json).
- Upload targets: save-to-folder, FTP/FTPS, WebDAV; passwords DPAPI (CurrentUser).
- Recorder: region border (excluded from capture), GIF (no audio), trim preview (needs plugin), separate mic/system volume.
- Startup warmup, physical-px toolbar position, press-to-record hotkey box with conflict check, HKCU Run autostart, tray left-click option, zh/en UI (restart required).

## Batch 4
- **Quick action bar**: Settings → 截图后动作 → "显示快捷操作栏". A small bar appears next to the capture (copy / save / save as / pin / OCR / upload / edit / close; Esc closes, Ctrl+C copies, auto-hides after 20 s if untouched). Choose any other after-capture action to turn it off.
- **Stitch images** (tray → 拼接长图…): add from history / files / clipboard / drag-drop, reorder by drag or ↑/↓, vertical or horizontal, optional overlap detection, copy / save / open in editor.
- **Compare images** (tray → 图片对比…): side-by-side (synced scroll), overlay with opacity slider, difference highlight with tolerance and bounding box.
- **Timed capture** (tray → 定时截图…, hotkey Ctrl+Shift+T toggles): every N s, M times (0 = until stopped), full screen / region / window, own folder + name template + format. While running the tray icon gets a red dot and the menu item becomes "停止定时截图".

## Command line
```
BestSnap.exe [--fullscreen | --window | --region x,y,w,h | --interactive-region]
              [--delay s] [--save path|folder\] [--format png|jpg] [--clipboard] [--ocr out.txt]
BestSnap.exe --record --duration s --out file.mp4|file.gif [--fullscreen | --window | --region x,y,w,h | --interactive-region] [--delay s]
```
- Default source is `--fullscreen` (primary monitor when recording). `--window` = current foreground window. Region coordinates are physical pixels in virtual-screen coordinates.
- `--save` with an existing folder or trailing `\` uses the file-name template; otherwise the given file name (extension adjusted to `--format`). Relative paths are relative to the caller's current directory.
- No output option → the running app's normal after-capture action (or clipboard if BestSnap was not running).
- `--ocr` needs the OCR plugin; text is written as UTF-8.
- If BestSnap is already running, the command is forwarded via the named pipe `BestSnap_Cli_Pipe_v1` (current user only; same name as the previous BestSnap build) and executed there; the caller waits and gets the exit code. Otherwise a temporary instance runs the command and exits.
- Exit codes: 0 ok, 1 error, 2 bad arguments, 3 cancelled (Esc / no window), 4 OCR plugin missing, 5 another CLI command is still running.
- BestSnap is a GUI exe: in cmd use `start /wait BestSnap.exe --fullscreen --save C:\shots\ & echo %errorlevel%`; in PowerShell `(Start-Process BestSnap.exe -ArgumentList '--fullscreen','--clipboard' -Wait -PassThru).ExitCode`.

## Batch 5

- **OCR + 翻译 / translate**: the OCR result window has *译为中文 / 译为英文*. Provider is configured in Settings → Capture → 翻译服务设置: None (default, offline), DeepL API, Azure Translator (key + region), or a LibreTranslate URL. Keys are stored DPAPI-encrypted (current user). Text is only sent when you click translate.
- **QR / barcode** (ZXing.Net core, no imaging bindings): tray *识别二维码/条码* (Ctrl+Shift+Q), editor and quick bar button. Results offer copy and *open link* (http/https only).
- **Auto-redact**: editor *自动打码* / quick bar *打码*. Uses the OCR plugin's word boxes (`IOcrLayoutEngine`) to find e-mails, phone numbers, CN ID / card numbers and URLs, plus faces (`IFaceDetector`, Windows.Media.FaceAnalysis). Adds live-pixelate *Redact* annotations as one undo step; they can be moved, resized or deleted. Regex detection is heuristic; check the result.
- **Webcam picture-in-picture**: in the recording options, pick a camera, corner, size and circle or rectangle. A rectangle in MP4 uses the ScreenRecorderLib `VideoCaptureOverlay`. Circle, and any GIF recording, uses a draggable webcam bubble window that is recorded. It is fed by the plugin's `IWebcamSource` (MediaCapture), so it needs the OCR plugin. Window-source recordings always use the rectangle overlay.
- **Keystroke / click overlay**: low-level keyboard and mouse hooks show combos at the bottom centre of the recorded area. This window is intentionally **not** excluded from capture, so it appears in the video. Plain typing (letters and digits without Ctrl/Alt/Win) is not shown, so typed text never ends up in the recording. It is not visible in window-source recordings, which capture only the target window.

## Languages (i18n)

UI strings live in JSON files (`src/BestSnap/Lang/*.json`, embedded in the exe): zh-CN, zh-TW, en, de, ja, ko, fr, es.
Format: `{ "_name": "Deutsch", "_code": "de", "strings": { "<Chinese source text>": "<translation>" } }`.
To add or override a language, put `<code>.json` in `<exe dir>\lang\`; it shows up in Settings and the tray's language submenu.
"Auto" follows the Windows UI language (zh-TW/HK/MO → 繁體中文). Missing keys fall back to English, then to the source text.
Switching applies live: the tray menu and toolbar are rebuilt; other open windows need to be reopened (you'll be told).
de/ja/ko/fr/es were machine-translated offline from English (Argos Translate) and zh-TW converted with OpenCC (s2twp); corrections are welcome via a lang\ override file.

## Single-exe build

`.\build.ps1` (framework-dependent, needs .NET 8 Desktop Runtime x64, ≈28 MB) or `.\build.ps1 -SelfContained` (≈80 MB) → exactly one `publish\BestSnap.exe`.
ScreenRecorderLib (mixed-mode C++/CLI) and the OCR plugin with its WinRT projection are embedded as resources and extracted on first run to
`%LocalAppData%\BestSnap\runtime\<build id>\` (old build folders are cleaned up), then resolved via AssemblyLoadContext + SetDllDirectory.
A `plugins\` folder next to the exe still takes precedence over the embedded plugin.
