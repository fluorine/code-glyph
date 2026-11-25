/** A stable hue from 0 to 359 for an icon name. Equal concepts get equal colors in all files. */
export function iconHue(name: string): number {
  let h = 2166136261;
  for (let i = 0; i < name.length; i++) {
    h ^= name.charCodeAt(i);
    h = Math.imul(h, 16777619);
  }
  return (h >>> 0) % 360;
}

export function hslToHex(h: number, s: number, l: number): string {
  const a = s * Math.min(l, 1 - l);
  const f = (n: number) => {
    const k = (n + h / 30) % 12;
    const c = l - a * Math.max(-1, Math.min(k - 3, 9 - k, 1));
    return Math.round(255 * c).toString(16).padStart(2, '0');
  };
  return '#' + f(0) + f(8) + f(4);
}

/** Light colors on dark themes and dark colors on light themes. */
export function iconColor(name: string, darkTheme: boolean): string {
  return hslToHex(iconHue(name), 0.55, darkTheme ? 0.68 : 0.38);
}
