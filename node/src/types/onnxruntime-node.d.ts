declare module "onnxruntime-node" {
  export class Tensor {
    constructor(type: string, data: ArrayBufferView | number[], dims?: readonly number[]);
    readonly data: ArrayBufferView;
    readonly dims: number[];
  }
  export class InferenceSession {
    static create(path: string, options?: InferenceSession.SessionOptions): Promise<InferenceSession>;
    readonly inputNames: string[];
    readonly outputNames: string[];
    readonly modelMetadata?: { customMetadata?: Record<string, string>; customMetadataMap?: Record<string, string> };
    run(feeds: Record<string, Tensor>): Promise<Record<string, Tensor>>;
    release(): Promise<void>;
  }
  export namespace InferenceSession {
    interface SessionOptions {
      graphOptimizationLevel?: "disabled" | "basic" | "extended" | "all";
      intraOpNumThreads?: number;
    }
  }
}
