/** 決定論的な疑似乱数。CameraShake / コンボの左右 / FacePunchChance で共有する。 */
export class Prng {
  private state: number;

  constructor(seed = 0x2f6e2b1) {
    this.state = seed >>> 0;
  }

  next(): number {
    let x = this.state;
    x ^= x << 13;
    x ^= x >>> 17;
    x ^= x << 5;
    this.state = x >>> 0;
    return this.state / 0x100000000;
  }

  range(min: number, max: number): number {
    return min + (max - min) * this.next();
  }
}
