/** Regex auto-redact on OCR word boxes (emails, phones, CN ID, URLs). Face detection is Windows-only. */

export interface WordBox {
  text: string;
  line: number;
  x: number;
  y: number;
  width: number;
  height: number;
}

export interface RedactHit {
  kind: string;
  x: number;
  y: number;
  w: number;
  h: number;
}

const PATTERNS: { kind: string; rx: RegExp }[] = [
  { kind: "email", rx: /[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}/g },
  { kind: "url", rx: /(?:https?:\/\/|www\.)[^\s]+|\b[a-z0-9\-]+(?:\.[a-z0-9\-]+)*\.(?:com|net|org|cn|io|dev|app|gov|edu|co|me|info)(?:\/[^\s]*)?/gi },
  { kind: "id", rx: /(?<!\d)\d{6}\s?(?:19|20)\d{2}\s?\d{2}\s?\d{2}\s?\d{3}[\dXx](?!\d)/g },
  { kind: "phone", rx: /(?<!\d)1[3-9]\d[\s\-]?\d{4}[\s\-]?\d{4}(?!\d)/g },
  { kind: "phone", rx: /(?<![\w.])\+?\(?\d[\d\s\-().]{6,}\d(?![\w.])/g },
  { kind: "id", rx: /(?<!\d)(?:\d[\s\-]?){12,19}(?!\d)/g },
];

export function findInWords(words: WordBox[]): RedactHit[] {
  const hits: RedactHit[] = [];
  const byLine = new Map<number, WordBox[]>();
  for (const w of words) {
    const arr = byLine.get(w.line) ?? [];
    arr.push(w);
    byLine.set(w.line, arr);
  }
  for (const ws of byLine.values()) {
    let s = "";
    const starts: number[] = [];
    for (const w of ws) {
      if (s.length > 0) s += " ";
      starts.push(s.length);
      s += w.text;
    }
    const used: [number, number][] = [];
    for (const { kind, rx } of PATTERNS) {
      rx.lastIndex = 0;
      let m: RegExpExecArray | null;
      const r = new RegExp(rx.source, rx.flags.includes("g") ? rx.flags : rx.flags + "g");
      while ((m = r.exec(s))) {
        const a = m.index,
          b = m.index + m[0].length;
        if (kind === "phone" && (m[0].match(/\d/g)?.length ?? 0) < 7) continue;
        if (used.some((u) => a < u[1] && b > u[0])) continue;
        used.push([a, b]);
        let x = Infinity,
          y = Infinity,
          x1 = -Infinity,
          y1 = -Infinity;
        for (let i = 0; i < ws.length; i++) {
          const ws0 = starts[i],
            we = starts[i] + ws[i].text.length;
          if (ws0 < b && we > a) {
            x = Math.min(x, ws[i].x);
            y = Math.min(y, ws[i].y);
            x1 = Math.max(x1, ws[i].x + ws[i].width);
            y1 = Math.max(y1, ws[i].y + ws[i].height);
          }
        }
        if (Number.isFinite(x)) hits.push({ kind, x: x - 3, y: y - 3, w: x1 - x + 6, h: y1 - y + 6 });
      }
    }
  }
  return hits;
}
