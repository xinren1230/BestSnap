/** Port of BestSnap.Plugins.FormulaLatex.Tidy. */

export function tidyLatex(s: string | null | undefined): string {
  let sb = "";
  let prev = "";
  for (const t of (s ?? "").split(/[ \t\n\r]+/).filter(Boolean)) {
    if (sb.length > 0 && /\\[A-Za-z]+$/.test(prev) && /[A-Za-z]/.test(t[0])) sb += " ";
    sb += t;
    prev = t;
  }
  return sb;
}

/** Strip $…$, \[…\], ```latex fences that chat models like to add. */
export function cleanChatLatex(s: string | null | undefined): string {
  let t = (s ?? "").trim();
  if (t.startsWith("```")) {
    t = t.slice(t.indexOf("\n") + 1);
    const e = t.lastIndexOf("```");
    if (e >= 0) t = t.slice(0, e);
    t = t.trim();
  }
  const pairs: [string, string][] = [
    ["$$", "$$"],
    ["\\[", "\\]"],
    ["\\(", "\\)"],
    ["$", "$"],
  ];
  for (const [a, b] of pairs) {
    if (t.length > a.length + b.length && t.startsWith(a) && t.endsWith(b)) {
      t = t.slice(a.length, t.length - b.length).trim();
      break;
    }
  }
  return t;
}
