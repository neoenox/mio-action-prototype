export const clamp01 = (v: number): number => (v < 0 ? 0 : v > 1 ? 1 : v);
export const clamp = (v: number, min: number, max: number): number =>
  v < min ? min : v > max ? max : v;
export const lerp = (a: number, b: number, t: number): number => a + (b - a) * t;
export const smoothstep = (t: number): number => {
  const x = clamp01(t);
  return x * x * (3 - 2 * x);
};
export const invLerp = (a: number, b: number, v: number): number =>
  a === b ? 0 : clamp01((v - a) / (b - a));
export const moveTowards = (current: number, target: number, maxDelta: number): number => {
  const d = target - current;
  return Math.abs(d) <= maxDelta ? target : current + Math.sign(d) * maxDelta;
};
