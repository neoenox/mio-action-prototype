/** ウェーブ（アリーナ）の配置を決める純ロジック。readonly struct 相当。 */
export class ArenaPlan {
  readonly startX: number;
  readonly pitch: number;
  readonly wavesPerArena: number;
  readonly arenaCount: number;

  constructor(startX: number, pitch: number, wavesPerArena: number, arenaCount: number) {
    this.startX = startX;
    this.pitch = pitch > 0 ? pitch : 10;
    this.wavesPerArena = Math.max(1, wavesPerArena);
    this.arenaCount = Math.max(1, arenaCount);
    Object.freeze(this);
  }

  static get Default(): ArenaPlan {
    return new ArenaPlan(-6, 10, 2, 5);
  }

  /** アリーナ arena（0 始まり）の中心 X。 */
  centerOf(arena: number): number {
    return this.startX + Math.max(0, arena) * this.pitch;
  }

  /** 波番号（1 始まり）が属するアリーナ。最後を超える場合は最後に留まる。 */
  arenaOf(wave: number): number {
    const index = Math.floor(Math.max(1, wave) - 1) / this.wavesPerArena;
    // C# は整数除算。Math.floor で同じ結果にする。
    return Math.min(Math.floor(index), this.arenaCount - 1);
  }

  /** この波が最後のアリーナか。 */
  isLastArenaOf(wave: number): boolean {
    return this.arenaOf(wave) >= this.arenaCount - 1;
  }

  /** 敵が入ってくる X。アリーナ右端の少し外。 */
  entryX(arena: number, screenHalfWidth: number, extra: number): number {
    return this.centerOf(arena) + Math.max(0, screenHalfWidth) + Math.max(0, extra);
  }
}
