/** Public model URLs, sizes, SHA-256. Safe to import from the browser build. */

export const FORMULA_FOLDER = "pix2text-mfr";
export const OCR_FOLDER = "ocr";

export const FORMULA_FILES: { name: string; sha256: string; size: number }[] = [
  { name: "encoder_model.onnx", sha256: "bd8d5c322792e9ec45793af5569e9748f82a3d728a9e00213dbfc56c1486f37d", size: 87496990 },
  { name: "decoder_model.onnx", sha256: "fd0f92d7a012f3dae41e1ac79421aea0ea888b5a66cb3f9a004e424f82f3daed", size: 30114937 },
  { name: "tokenizer.json", sha256: "3e2ab757277d22639bec28c9d7972e352d3d1dba223051fa674002dc5ab64df3", size: 39161 },
];

export const OCR_FILES: { name: string; size: number }[] = [
  { name: "ch_PP-OCRv4_det_infer.onnx", size: 4745517 },
  { name: "ch_PP-OCRv4_rec_infer.onnx", size: 10857958 },
];

export const MODEL_REPO = "https://huggingface.co/breezedeus/pix2text-mfr";
export const MODEL_REV = "main";

/** RapidOCR GitHub release of PaddleOCR PP-OCRv4 mobile ONNX (Apache-2.0). */
export const OCR_MODEL_BASE =
  "https://github.com/RapidAI/RapidOCR/releases/download/v1.2.0";
