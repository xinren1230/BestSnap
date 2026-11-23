export type WhisperBackend = "cpu" | "cuda" | "vulkan";

export type WhisperHardware = {
  adapters: { name: string; vendor: "nvidia" | "amd" | "intel" | "other"; dedicatedBytes: number }[];
  ramBytes: number;
  cpuCores: number;
  cudaRuntime: boolean;
  vulkanRuntime: boolean;
};

const GiB = 1024 * 1024 * 1024;

export function smallerWhisper(id: string): string {
  const s = (id || "").toLowerCase();
  if (s === "large-v3-turbo") return "large-v3-turbo-q5_0";
  if (s === "large-v3-turbo-q5_0" || s === "medium") return "medium-q5_0";
  if (s === "medium-q5_0") return "small";
  if (s === "small") return "base";
  return "tiny";
}

export function pickWhisper(hw: WhisperHardware, live: boolean): { modelId: string; backend: WhisperBackend; reason: string } {
  const nvidia = best(hw.adapters, (a) => a.vendor === "nvidia");
  const discrete = best(hw.adapters, (a) => a.dedicatedBytes >= 4 * GiB);
  if (hw.cudaRuntime && nvidia && nvidia.dedicatedBytes >= 6 * GiB) {
    const model = live && nvidia.dedicatedBytes < 8 * GiB ? smallerWhisper("large-v3-turbo") : "large-v3-turbo";
    return gpu(model, "cuda", nvidia);
  }
  if (hw.cudaRuntime && nvidia && nvidia.dedicatedBytes >= 4 * GiB) {
    const model = live ? smallerWhisper("large-v3-turbo-q5_0") : "large-v3-turbo-q5_0";
    return gpu(model, "cuda", nvidia);
  }
  if (hw.vulkanRuntime && discrete && discrete.dedicatedBytes >= 4 * GiB) {
    const model = live && discrete.dedicatedBytes < 6 * GiB ? smallerWhisper("large-v3-turbo-q5_0") : "large-v3-turbo-q5_0";
    return gpu(model, "vulkan", discrete);
  }
  const cores = hw.cpuCores || 1;
  const strong = hw.ramBytes >= 16 * GiB && cores >= 8;
  const model = strong ? (live ? "small" : "medium-q5_0") : live ? "tiny" : "base";
  return { modelId: model, backend: "cpu", reason: `${fmtGb(hw.ramBytes)} RAM, ${cores} cores → ${model}, CPU` };
}

function best<T extends { dedicatedBytes: number }>(arr: T[] | undefined, pred: (a: T) => boolean): T | undefined {
  let b: T | undefined;
  for (const a of arr || []) {
    if (!pred(a)) continue;
    if (!b || a.dedicatedBytes > b.dedicatedBytes) b = a;
  }
  return b;
}

function gpu(modelId: string, backend: WhisperBackend, a: { name: string; dedicatedBytes: number }) {
  const name = a.name.replace(/^NVIDIA GeForce /i, "").replace(/^NVIDIA /i, "").replace(/^AMD /i, "");
  return { modelId, backend, reason: `${name} ${fmtGb(a.dedicatedBytes)} → ${modelId}, ${backend === "cuda" ? "CUDA" : "Vulkan"}` };
}

function fmtGb(bytes: number): string {
  const gb = bytes / GiB;
  const n = gb >= 9.95 ? gb.toFixed(0) : String(+gb.toFixed(1));
  return `${n} GB`;
}

export function parseNvidiaSmi(csv: string): WhisperHardware["adapters"] {
  const out: WhisperHardware["adapters"] = [];
  for (const raw of (csv || "").split(/\r?\n/)) {
    const line = raw.trim();
    if (!line || /^name/i.test(line)) continue;
    const comma = line.lastIndexOf(",");
    if (comma <= 0) continue;
    const name = line.slice(0, comma).trim().replace(/^"|"$/g, "");
    const mib = parseFloat(line.slice(comma + 1).replace(/MiB/i, "").trim());
    if (!Number.isFinite(mib) || mib <= 0) continue;
    out.push({ name, vendor: "nvidia", dedicatedBytes: Math.round(mib * 1024 * 1024) });
  }
  return out;
}
