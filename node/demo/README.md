# 演示录像

在 Linux（X11 或 Xvfb）上启动 **真实 Electron 浮动工具栏**，用 `xdotool` 把鼠标移到按钮上、高亮并点击，再进入对应功能。

`run-demo.sh` 会：

1. 起 1920×1080 Xvfb（若当前 DISPLAY 不可用）
2. 用 ffmpeg `x11grab`（含鼠标指针）录像
3. 启动 `electron . --demo`：演示页 + 底部字幕条 + 顶部真实工具栏
4. 跑 `drive.mjs` 驱动真实交互

## 片段（均为真实窗口 / 真实点击）

| 步骤 | 从工具栏 | 交互 |
|---|---|---|
| 1 区域截图 | ▢ | 真实覆盖层拖选 OCR 文本块 → 打开真实编辑器 |
| 2 编辑标注 | （编辑器工具栏） | 箭头 / 文字 / 马赛克 |
| 3 OCR | 更多 → 识别文字 | 覆盖层拖选 → 结果窗 |
| 4 公式 → LaTeX | 更多 → 公式转 LaTeX | 覆盖层拖选公式 → 结果窗 + KaTeX |
| 5 二维码 | 更多 → 识别二维码 | 覆盖层拖选 → 结果窗 |
| 6 录屏 | ● | 真实 ffmpeg x11grab 数秒后再点一次停止 |
| 7 历史 | 更多 → 截图历史 | 本次演示写入的真实历史条目 |

结果窗 / 历史窗在演示模式里靠右打开，左侧演示卡片在截取后仍保持原样（不是被结果窗挡住的空白白框）。

字幕条是独立的始终置顶窗（`view=caption`），不是假 UI 走查。

不含个人账号；二维码内容为 `https://example.com/snapforge`。

## 复现

```bash
cd node
bash scripts/setup.sh          # 若尚未安装依赖与模型
bash demo/run-demo.sh
# 输出：demo/snapforge-linux-demo.mp4
```

可选环境变量：

- `SNAPFORGE_DEMO_OUT` 输出路径（默认 `demo/snapforge-linux-demo.mp4`）
- `SNAPFORGE_DEMO_ARTIFACT` 额外拷贝路径（默认 `/opt/cursor/artifacts/snapforge-linux-demo.mp4`）
- `SNAPFORGE_DEMO_DISPLAY` 已有 X 服务器时复用（默认 `:97`）
