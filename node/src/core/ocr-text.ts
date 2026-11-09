/** Port of SnapForge.Plugins.OcrText — CJK/Latin spacing, CTC word gaps, post-process. */

const LETTER_DIGIT = /(?<=[A-Za-z]{4,})(?=\d{2,})|(?<=\d{2,})(?=[A-Za-z]{4,})/g;

export function isCjk(c: string): boolean {
  const u = c.codePointAt(0) ?? 0;
  if (u >= 0x4e00 && u <= 0x9fff) return true;
  if (u >= 0x3400 && u <= 0x4dbf) return true;
  if (u >= 0xf900 && u <= 0xfaff) return true;
  if (u >= 0x2e80 && u <= 0x2fff) return true;
  if (u >= 0x3040 && u <= 0x30ff) return true;
  if (u >= 0x3100 && u <= 0x312f) return true;
  if (u >= 0x31a0 && u <= 0x31bf) return true;
  if (u >= 0x1100 && u <= 0x11ff) return true;
  if (u >= 0x3130 && u <= 0x318f) return true;
  if (u >= 0xac00 && u <= 0xd7af) return true;
  if (u >= 0x3001 && u <= 0x303f) return true;
  if (u >= 0xff66 && u <= 0xff9d) return true;
  if (u >= 0xff01 && u <= 0xff0f) return true;
  if (u >= 0xff1a && u <= 0xff20) return true;
  if (u >= 0xff3b && u <= 0xff40) return true;
  if (u >= 0xff5b && u <= 0xff65) return true;
  return false;
}

export function isLatinTokenChar(c: string): boolean {
  const u = c.codePointAt(0) ?? 0;
  return u < 0x2e80 && /[0-9A-Za-z]/.test(c);
}

export function isCjkPunct(c: string): boolean {
  const u = c.codePointAt(0) ?? 0;
  if (u >= 0x3001 && u <= 0x303f) return true;
  if (u >= 0xff01 && u <= 0xff0f) return true;
  if (u >= 0xff1a && u <= 0xff20) return true;
  if (u >= 0xff3b && u <= 0xff40) return true;
  if (u >= 0xff5b && u <= 0xff65) return true;
  if (u >= 0x2018 && u <= 0x201f) return true;
  return "、。；：！？，．「」『』《》〈〉【】（）".includes(c);
}

function isGapSpace(c: string): boolean {
  return c === " " || c === "\t" || c === "\u00a0" || c === "\u3000";
}

export function joinCjk(s: string | null | undefined): string {
  if (!s) return s ?? "";
  let out = "";
  for (let i = 0; i < s.length; i++) {
    const c = s[i];
    if (isGapSpace(c) && out.length > 0 && isCjk(out[out.length - 1])) {
      let j = i;
      while (j < s.length && isGapSpace(s[j])) j++;
      if (j < s.length && isCjk(s[j])) {
        i = j - 1;
        continue;
      }
    }
    out += c;
  }
  return out;
}

function isSpaceGlyph(s: string): boolean {
  return !!s && startsWithSpace(s) && endsWithSpace(s);
}

function endsWithSpace(s: string): boolean {
  if (!s) return false;
  const c = s[s.length - 1];
  return isGapSpace(c) || /\s/.test(c);
}

function startsWithSpace(s: string): boolean {
  if (!s) return false;
  const c = s[0];
  return isGapSpace(c) || /\s/.test(c);
}

function lastSignificant(s: string): string {
  if (!s) return "";
  for (let i = s.length - 1; i >= 0; i--) if (!/\s/.test(s[i])) return s[i];
  return "";
}

function firstSignificant(s: string): string {
  if (!s) return "";
  for (let i = 0; i < s.length; i++) if (!/\s/.test(s[i])) return s[i];
  return "";
}

function median(xs: number[]): number {
  const n = xs.length;
  if (n === 0) return 0;
  const a = [...xs].sort((x, y) => x - y);
  if (n & 1) return a[n >> 1];
  return (a[n / 2 - 1] + a[n / 2]) / 2;
}

function isSentencePunct(c: string): boolean {
  return ",.;:!?，。；：！？、".includes(c);
}

function shouldSpaceScripts(prev: string, next: string): boolean {
  if (!prev || !next) return false;
  if (isCjkPunct(prev) || isCjkPunct(next)) return false;
  if (isCjk(prev) && isCjk(next)) return false;
  if ((isCjk(prev) && isLatinTokenChar(next)) || (isLatinTokenChar(prev) && isCjk(next))) return true;
  if (isSentencePunct(prev) && !isCjkPunct(prev) && /[A-Za-z]/.test(next) && (next.codePointAt(0) ?? 0) < 0x2e80) return true;
  return false;
}

function shouldInsertLatinSpace(prev: string, next: string, gap: number, med: number, _em: number): boolean {
  if (shouldSpaceScripts(prev, next)) return true;
  if (!isLatinTokenChar(prev) || !isLatinTokenChar(next)) return false;
  const letterDigit =
    (/[A-Za-z]/.test(prev) && /\d/.test(next)) || (/\d/.test(prev) && /[A-Za-z]/.test(next));
  if (letterDigit) return gap > med * 2.4;
  return gap > med * 1.8;
}

export function restoreLatinSpaces(
  glyphs: ReadonlyArray<{ text: string; position: number } | [string, number]>,
  em = 0,
): string {
  const g = glyphs.map((x) => (Array.isArray(x) ? { text: x[0], position: x[1] } : { text: x.text, position: x.position }));
  if (g.length === 0) return "";
  if (g.length === 1) return g[0].text ?? "";

  const gaps = new Array<number>(g.length - 1);
  const samples: number[] = [];
  for (let i = 1; i < g.length; i++) {
    gaps[i - 1] = g[i].position - g[i - 1].position;
    if (gaps[i - 1] > 1e-6 && !isSpaceGlyph(g[i - 1].text) && !isSpaceGlyph(g[i].text)) samples.push(gaps[i - 1]);
  }
  let med = median(samples);
  if (med <= 0) med = 1;

  let sb = "";
  for (let i = 0; i < g.length; i++) {
    const t = g[i].text ?? "";
    if (i > 0) {
      const prev = g[i - 1].text ?? "";
      if (!endsWithSpace(prev) && !startsWithSpace(t) && shouldInsertLatinSpace(lastSignificant(prev), firstSignificant(t), gaps[i - 1], med, em)) {
        sb += " ";
      }
    }
    sb += t;
  }
  return sb;
}

export function squeezeCjkPunctSpaces(s: string | null | undefined): string {
  if (!s) return s ?? "";
  let out = "";
  for (let i = 0; i < s.length; i++) {
    const c = s[i];
    if (c !== "\n" && c !== "\r" && (isGapSpace(c) || /\s/.test(c))) {
      if (out.length > 0 && isCjkPunct(out[out.length - 1])) continue;
      let j = i;
      while (j < s.length && s[j] !== "\n" && s[j] !== "\r" && (isGapSpace(s[j]) || /\s/.test(s[j]))) j++;
      if (j < s.length && isCjkPunct(s[j])) {
        i = j - 1;
        continue;
      }
    }
    out += c;
  }
  return out;
}

export function insertScriptAndPunctSpaces(s: string | null | undefined): string {
  if (!s) return s ?? "";
  let out = "";
  for (let i = 0; i < s.length; i++) {
    const c = s[i];
    if (i > 0 && !/\s/.test(s[i - 1]) && !/\s/.test(c) && shouldSpaceScripts(s[i - 1], c)) out += " ";
    out += c;
  }
  return out;
}

export function splitLetterDigit(s: string | null | undefined): string {
  if (!s) return s ?? "";
  return s.replace(LETTER_DIGIT, " ");
}

export function postProcess(s: string | null | undefined): string {
  return squeezeCjkPunctSpaces(insertScriptAndPunctSpaces(splitLetterDigit(joinCjk(s))));
}

export function countCjk(s: string | null | undefined): number {
  let n = 0;
  if (!s) return 0;
  for (const c of s) {
    const u = c.codePointAt(0) ?? 0;
    if ((u >= 0x4e00 && u <= 0x9fff) || (u >= 0x3400 && u <= 0x4dbf) || (u >= 0x3040 && u <= 0x30ff) || (u >= 0xac00 && u <= 0xd7af)) n++;
  }
  return n;
}
