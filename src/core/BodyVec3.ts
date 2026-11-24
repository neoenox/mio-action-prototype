/** UnityEngine 非依存の 3 次元ベクトル。 */
export class BodyVec3 {
  readonly x: number;
  readonly y: number;
  readonly z: number;

  constructor(x: number, y: number, z: number) {
    this.x = x;
    this.y = y;
    this.z = z;
    Object.freeze(this);
  }

  static add(a: BodyVec3, b: BodyVec3): BodyVec3 {
    return new BodyVec3(a.x + b.x, a.y + b.y, a.z + b.z);
  }

  static sub(a: BodyVec3, b: BodyVec3): BodyVec3 {
    return new BodyVec3(a.x - b.x, a.y - b.y, a.z - b.z);
  }

  static mul(a: BodyVec3, s: number): BodyVec3 {
    return new BodyVec3(a.x * s, a.y * s, a.z * s);
  }

  static dot(a: BodyVec3, b: BodyVec3): number {
    return a.x * b.x + a.y * b.y + a.z * b.z;
  }

  get sqrMagnitude(): number {
    return this.x * this.x + this.y * this.y + this.z * this.z;
  }
}
