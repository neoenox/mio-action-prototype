import type { EnemyActor } from './types';

/** 攻撃トークンの仲裁。Unity の EnemyGroup 相当（CONFIG の MaxAttackers は初期値）。 */
export class EnemyGroup {
  maxAttackers = 2;
  private readonly members: EnemyActor[] = [];
  private readonly attackers: EnemyActor[] = [];

  get memberCount(): number { return this.members.length; }
  get attackerCount(): number { return this.attackers.length; }
  get allMembers(): readonly EnemyActor[] { return this.members; }
  get hasFreeSlot(): boolean { return this.attackers.length < Math.max(1, this.maxAttackers); }

  register(brain: EnemyActor): void {
    if (!this.members.includes(brain)) this.members.push(brain);
  }

  unregister(brain: EnemyActor): void {
    const mi = this.members.indexOf(brain);
    if (mi >= 0) this.members.splice(mi, 1);
    const ai = this.attackers.indexOf(brain);
    if (ai >= 0) this.attackers.splice(ai, 1);
  }

  indexOf(brain: EnemyActor): number { return this.members.indexOf(brain); }

  hasAttackToken(brain: EnemyActor): boolean { return this.attackers.includes(brain); }

  /** 同じ側のより手前に仲間がいて前へ出られないか。 */
  isBlockedByNearer(brain: EnemyActor, targetX: number): boolean {
    const own = brain.position.x - targetX;
    return this.members.some((other) => {
      if (other === brain || !other.isAlive || !other.isActive) return false;
      const offset = other.position.x - targetX;
      return own * offset > 0 && Math.abs(offset) < Math.abs(own) - 0.05;
    });
  }

  tryAcquireAttackToken(brain: EnemyActor, targetX: number): boolean {
    if (this.attackers.includes(brain)) return true;
    if (this.isBlockedByNearer(brain, targetX)) return false;
    if (this.attackers.length >= Math.max(1, this.maxAttackers)) return false;
    this.attackers.push(brain);
    return true;
  }

  /** 演出が順番を決めている一発用。並びや上限を見ずに取る。 */
  forceAcquireAttackToken(brain: EnemyActor): void {
    if (!this.attackers.includes(brain)) this.attackers.push(brain);
  }

  releaseAttackToken(brain: EnemyActor): void {
    const i = this.attackers.indexOf(brain);
    if (i >= 0) this.attackers.splice(i, 1);
  }

  releaseAllTokens(): void { this.attackers.length = 0; }
}

/** 有効な敵の登録簿。Unity の EnemyRegistry 相当。インスタンスごとに分ける。 */
export class EnemyRegistry {
  private readonly all: EnemyActor[] = [];

  get snapshot(): readonly EnemyActor[] { return this.all; }
  get first(): EnemyActor | null { return this.all[0] ?? null; }
  get count(): number { return this.all.length; }

  register(brain: EnemyActor): void {
    if (!this.all.includes(brain)) this.all.push(brain);
  }

  unregister(brain: EnemyActor): void {
    const i = this.all.indexOf(brain);
    if (i >= 0) this.all.splice(i, 1);
  }

  clear(): void { this.all.length = 0; }
}
