/** 戦闘の数値。仕様書 SPEC.md §4 の値をそのまま持つ。 */

/** 部位ダメージの対象。 */
export enum BodyPart {
  Arms = 0,
  Legs = 1,
  Torso = 2,
}

/** 怯みの段階。持続時間は stunSeconds() で解決する。 */
export enum StaggerTier {
  None = 0,
  Small = 1,
  Medium = 2,
  Large = 3,
  GuardBreak = 4,
}

export const CombatConstants = {
  // --- リソース ---
  MaxHp: 100,
  PartMax: 100,

  // --- 部位ダメージの劣化下限 ---
  LegsSpeedFloor: 0.7,
  ArmsDamageFloor: 0.75,

  // --- 怯み・ダウン（秒） ---
  StunSmall: 0.2,
  StunMedium: 0.4,
  StunLarge: 0.8,
  StunGuardBreak: 1.2,
  DownDuration: 2.0,
  GetUpVulnerable: 0.6,

  // --- 体の寸法と、拘束中の間合い（m） ---
  EnemyCapsuleRadius: 0.4,
  PlayerBodyRadius: 0.3,
  /** 半径の和。0.4 + 0.3 = 0.7（C# の const 式と同じ）。 */
  BodyGap: 0.4 + 0.3,
  GrabStandoff: 0.6,
  BearHugStandoff: 0.54,
  BackGrabStandoff: 0.34,

  // --- 掴み・抵抗 ---
  MashBaseBack: 10,
  MashBasePin: 12,
  MashBaseBearHug: 12,
  GrappleLimitSeconds: 2.7 + 3.1,
  FrontGrappleEscapeSeconds: 2.7,
  BearHugLimitSeconds: 2.7 + 3.6,
  GrappleEscapeStun: 0.7,

  // --- 押さえつけ（Pin）の入口と膝蹴り ---
  PinMinDistance: 0.8,
  PinMaxDistance: 1.3,
  PinCooldown: 2.5,
  PinKneeCount: 5,
  PinKneeInterval: 0.65,
  PinKneeDamage: 4.0,

  // --- ベアハッグ ---
  BearHugChance: 0.5,
  BearHugSqueezeInterval: 0.65,
  BearHugSqueezeDamage: 2.0,
  BearHugSqueezeFlinch: 3.0,
  GrabbedDamageFromOthers: 0.5,
  GrappleReleaseGraceSeconds: 0.7,
  HitStunGrace: 0.35,

  // --- ひるみ（ダウンゲージ） ---
  MaxFlinch: 100,
  FlinchDecayDelay: 1.5,
  FlinchDecayPerSecond: 10,
  MaxHeldBellyDamage: 100,
  HeldBellyPunchDamage: 16,
  HeldKneeBellyDamage: 12,

  // --- 壁・二体がかり（Y字拘束） ---
  WallSplitPunchCount: 6,
  WallSplitPunchInterval: 0.55,
  WallSplitPunchDamage: 4,
  WallSplitPunchBelly: 10,
  WallSplitOpeningHits: 3,
  WallSplitBuildHits: 6 - 1,

  // --- 3 人がかり ---
  WallGangStrikes: 15,
  WallGangOpeningHits: 4,
  WallGangThirdLateral: 0.7,
  WallGangThirdDepth: 0.7,
  WallGangCaptorLateral: 0.55,
  WallGangCaptorDepth: 0.3,
  WallGangBellyScale: 0.45,
  WallGangFaceScale: 0.4,
  WallGangKneeScale: 0.8,
  WallGangFinisherScale: 1.4,
  WallGangCameraBack: 0.55,
  WallGangCameraShift: 0.15,
  WallGangKickScale: 0.9,
  WallSplitEscapeInputs: 12 * 1.5,
  WallSplitHandHalf: 0.48,
  WallSplitAssistGap: 0.62,
  WallSplitStandGap: 0.45,
  WallSplitStandFront: 0.55,
  WallSplitKneeGap: 0.3,
  WallSplitKneeFront: 0.34,
  DownImmunitySeconds: 1.0,

  // --- ダウンの吹き飛ばし ---
  DownLaunchDistance: 0.35,
  DownLaunchHeight: 0.16,

  /** 腹パン i 発目の段階別の間隔。 */
  wallSplitHitInterval(index: number): number {
    return index < this.WallSplitOpeningHits ? 0.5 : index < this.WallSplitBuildHits ? 0.65 : 0.95;
  },
  /** 振りかぶりの秒数。 */
  wallSplitHitWindup(index: number): number {
    return index < this.WallSplitOpeningHits ? 0.22 : index < this.WallSplitBuildHits ? 0.26 : 0.45;
  },
  /** ダメージ・腹ダメージの倍率。 */
  wallSplitHitScale(index: number): number {
    return index < this.WallSplitOpeningHits ? 0.75 : index < this.WallSplitBuildHits ? 1.0 : 2.0;
  },
  /** 被弾姿勢の重さ。 */
  wallSplitHitImpact(index: number): number {
    return index < this.WallSplitOpeningHits ? 0.85 : index < this.WallSplitBuildHits ? 1.1 : 1.7;
  },
  /** カメラの寄り（0..1）。 */
  wallSplitCameraPush(index: number): number {
    return index < this.WallSplitOpeningHits ? 0 : index < this.WallSplitBuildHits ? 0.1 : 0.15;
  },
  /** 締めの 1 発か。 */
  isWallSplitFinisher(index: number): boolean {
    return index >= this.WallSplitBuildHits;
  },
  WallPauseSeconds: 1.5,
  WallPauseInputsAfterKnees: 10,
  WallPauseInputsAfterSplit: 14,

  /** 3 人がかりの段階。0 = 序盤 / 1 = 中盤 / 2 = 締め。 */
  wallGangStage(index: number): number {
    return index < this.WallGangOpeningHits ? 0 : index < this.WallGangStrikes - 1 ? 1 : 2;
  },
  wallGangWindup(index: number): number {
    switch (this.wallGangStage(index)) {
      case 0: return 0.12;
      case 1: return 0.15;
      default: return 0.2;
    }
  },
  wallGangInterval(index: number): number {
    switch (this.wallGangStage(index)) {
      case 0: return 0.34;
      case 1: return 0.44;
      default: return 0.6;
    }
  },
  wallGangLegWindup(index: number): number {
    switch (this.wallGangStage(index)) {
      case 0: return 0.26;
      case 1: return 0.36;
      default: return 0.48;
    }
  },
  wallGangLegInterval(index: number): number {
    switch (this.wallGangStage(index)) {
      case 0: return 0.52;
      case 1: return 0.78;
      default: return 1.05;
    }
  },
  wallGangImpactScale(index: number): number {
    switch (this.wallGangStage(index)) {
      case 0: return 0.75;
      case 1: return 1.0;
      default: return 1.25;
    }
  },

  /** 怯みの持続秒。 */
  stunSeconds(tier: StaggerTier): number {
    switch (tier) {
      case StaggerTier.Small: return this.StunSmall;
      case StaggerTier.Medium: return this.StunMedium;
      case StaggerTier.Large: return this.StunLarge;
      case StaggerTier.GuardBreak: return this.StunGuardBreak;
      default: return 0;
    }
  },
} as const;
