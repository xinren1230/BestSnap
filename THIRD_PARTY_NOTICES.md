# Third-Party Notices / 第三方组件声明

BestSnap itself is licensed under the Apache License 2.0 (Copyright 2026 Chen Xinren).
The components below are used or redistributed (in the Windows installer and/or the web version)
under their own licenses. This file is not a license grant for those components.

BestSnap 本身使用 Apache-2.0 许可；下列第三方组件保留各自原有许可。

| Component / 组件 | Used for / 用途 | License / 许可 | Source |
|---|---|---|---|
| PaddleOCR PP-OCRv4 (det / rec models) | OCR 文字识别 | Apache-2.0 | https://github.com/PaddlePaddle/PaddleOCR |
| RapidOCR (ONNX export of PP-OCRv4) | OCR ONNX models | Apache-2.0 | https://github.com/RapidAI/RapidOCR |
| pix2text-mfr (encoder/decoder ONNX + tokenizer), © 2022 BreezeDeus | 公式 → LaTeX | MIT | https://huggingface.co/breezedeus/pix2text-mfr · https://github.com/breezedeus/Pix2Text |
| ONNX Runtime (Microsoft.ML.OnnxRuntime, onnxruntime-web), © Microsoft Corporation | 模型推理 / inference | MIT | https://github.com/microsoft/onnxruntime |
| KaTeX | LaTeX 公式渲染 / rendering | MIT | https://github.com/KaTeX/KaTeX |
| ZXing / @zxing/library (zxing-js) | 二维码、条码 / QR & barcodes | Apache-2.0 | https://github.com/zxing/zxing · https://github.com/zxing-js/library |
| ScreenRecorderLib | 录屏 / screen recording (Windows) | MIT | https://github.com/sskodje/ScreenRecorderLib |
| NAudio | 纯音频录制（WASAPI + MP3）/ audio-only recording | MIT | https://github.com/naudio/NAudio |
| Whisper.net / whisper.cpp | 本机语音转写 / on-device speech-to-text | MIT | https://github.com/sandrohanea/whisper.net · https://github.com/ggml-org/whisper.cpp |
| whisper.cpp `samples/jfk.wav` (unit-test fixture only) | 实时转写管线测试 / live STT pipeline tests | Public-domain JFK recording; sample shipped with whisper.cpp (MIT) | https://github.com/ggml-org/whisper.cpp/tree/master/samples |
| .NET 8 runtime (bundled in the Windows single-file app), © .NET Foundation and Contributors | Windows 运行时 | MIT | https://github.com/dotnet/runtime |
| Inno Setup (installer engine), © Jordan Russell & Martijn Laan | Windows 安装程序 | Inno Setup License | https://jrsoftware.org/isinfo.php |

The full license texts of these components are available at the links above. The Windows
installer also ships `licenses/THIRD-PARTY-NOTICES.txt` next to the program.
