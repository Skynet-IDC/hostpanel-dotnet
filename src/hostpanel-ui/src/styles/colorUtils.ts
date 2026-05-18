/**
 * Hex <-> OKLCH-hue converters for the design system's themeable `--p-h` variable.
 *
 * Server stores `primaryColor` as hex (#RRGGBB); the design tokens consume a hue
 * (0-360, OKLCH). These helpers approximate the round-trip well enough for theming
 * (hue is dominant; lightness/chroma are constants from tokens.css).
 */

export function hexToHue(hex: string): number | null {
  const match = /^#?([0-9a-fA-F]{6})$/.exec(hex.trim());
  if (!match) return null;
  const v = parseInt(match[1], 16);
  const r = ((v >> 16) & 0xff) / 255;
  const g = ((v >> 8) & 0xff) / 255;
  const b = (v & 0xff) / 255;
  const max = Math.max(r, g, b);
  const min = Math.min(r, g, b);
  const d = max - min;
  if (d === 0) return 268;
  let h: number;
  if (max === r) h = ((g - b) / d) % 6;
  else if (max === g) h = (b - r) / d + 2;
  else h = (r - g) / d + 4;
  h = Math.round(h * 60);
  if (h < 0) h += 360;
  return h;
}

export function hueToHex(hue: number, lightness = 0.58, chroma = 0.16): string {
  const radians = (hue % 360) * (Math.PI / 180);
  const a = chroma * Math.cos(radians);
  const bComp = chroma * Math.sin(radians);
  const L = lightness;
  const lp = L + 0.3963377774 * a + 0.2158037573 * bComp;
  const mp = L - 0.1055613458 * a - 0.0638541728 * bComp;
  const sp = L - 0.0894841775 * a - 1.291485548 * bComp;
  const cube = (x: number) => x * x * x;
  const lR = 4.0767416621 * cube(lp) - 3.3077115913 * cube(mp) + 0.2309699292 * cube(sp);
  const lG = -1.2684380046 * cube(lp) + 2.6097574011 * cube(mp) - 0.3413193965 * cube(sp);
  const lB = -0.0041960863 * cube(lp) - 0.7034186147 * cube(mp) + 1.707614701 * cube(sp);
  const toSrgb = (u: number) => {
    const v = u <= 0 ? 0 : u >= 1 ? 1 : u;
    return v <= 0.0031308 ? 12.92 * v : 1.055 * Math.pow(v, 1 / 2.4) - 0.055;
  };
  const r = Math.round(toSrgb(lR) * 255);
  const g = Math.round(toSrgb(lG) * 255);
  const b = Math.round(toSrgb(lB) * 255);
  const pad = (n: number) => n.toString(16).padStart(2, '0');
  return `#${pad(r)}${pad(g)}${pad(b)}`;
}
