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
| .NET 8 runtime (bundled in the Windows single-file app), © .NET Foundation and Contributors | Windows 运行时 | MIT | https://github.com/dotnet/runtime |
| Inno Setup (installer engine), © Jordan Russell & Martijn Laan | Windows 安装程序 | Inno Setup License | https://jrsoftware.org/isinfo.php |

The full license texts of these components are available at the links above. The Windows
installer also ships `licenses/THIRD-PARTY-NOTICES.txt` next to the program.
