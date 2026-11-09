import sharp from "sharp";

export async function pixelate(png: Buffer, x: number, y: number, w: number, h: number, block = 12): Promise<Buffer> {
  const img = sharp(png);
  const meta = await img.metadata();
  const W = meta.width ?? 1,
    H = meta.height ?? 1;
  x = Math.max(0, x | 0);
  y = Math.max(0, y | 0);
  w = Math.min(W - x, w | 0);
  h = Math.min(H - y, h | 0);
  if (w < 2 || h < 2) return png;
  const sw = Math.max(1, Math.round(w / block));
  const sh = Math.max(1, Math.round(h / block));
  const region = await sharp(png).extract({ left: x, top: y, width: w, height: h }).resize(sw, sh, { kernel: "nearest" }).resize(w, h, { kernel: "nearest" }).png().toBuffer();
  return sharp(png)
    .composite([{ input: region, left: x, top: y }])
    .png()
    .toBuffer();
}

export async function blurRegion(png: Buffer, x: number, y: number, w: number, h: number, sigma = 8): Promise<Buffer> {
  const img = sharp(png);
  const meta = await img.metadata();
  const W = meta.width ?? 1,
    H = meta.height ?? 1;
  x = Math.max(0, x | 0);
  y = Math.max(0, y | 0);
  w = Math.min(W - x, w | 0);
  h = Math.min(H - y, h | 0);
  if (w < 2 || h < 2) return png;
  const region = await sharp(png).extract({ left: x, top: y, width: w, height: h }).blur(sigma).png().toBuffer();
  return sharp(png)
    .composite([{ input: region, left: x, top: y }])
    .png()
    .toBuffer();
}

export async function crop(png: Buffer, x: number, y: number, w: number, h: number): Promise<Buffer> {
  return sharp(png).extract({ left: Math.max(0, x | 0), top: Math.max(0, y | 0), width: Math.max(1, w | 0), height: Math.max(1, h | 0) }).png().toBuffer();
}

export async function resizeTo(png: Buffer, width: number): Promise<Buffer> {
  return sharp(png).resize({ width: Math.max(1, width | 0) }).png().toBuffer();
}

export async function stitch(images: Buffer[], vertical = true, overlap = 0): Promise<Buffer> {
  const metas = await Promise.all(images.map((b) => sharp(b).metadata()));
  if (vertical) {
    const w = Math.max(...metas.map((m) => m.width ?? 1));
    let y = 0;
    const comps = images.map((input, i) => {
      const c = { input, left: 0, top: y };
      y += (metas[i].height ?? 1) - (i < images.length - 1 ? overlap : 0);
      return c;
    });
    return sharp({ create: { width: w, height: y + overlap, channels: 4, background: { r: 255, g: 255, b: 255, alpha: 1 } } })
      .composite(comps)
      .png()
      .toBuffer();
  }
  const h = Math.max(...metas.map((m) => m.height ?? 1));
  let x = 0;
  const comps = images.map((input, i) => {
    const c = { input, left: x, top: 0 };
    x += (metas[i].width ?? 1) - (i < images.length - 1 ? overlap : 0);
    return c;
  });
  return sharp({ create: { width: x + overlap, height: h, channels: 4, background: { r: 255, g: 255, b: 255, alpha: 1 } } })
    .composite(comps)
    .png()
    .toBuffer();
}

export async function sizeOf(png: Buffer): Promise<{ w: number; h: number }> {
  const m = await sharp(png).metadata();
  return { w: m.width ?? 0, h: m.height ?? 0 };
}
