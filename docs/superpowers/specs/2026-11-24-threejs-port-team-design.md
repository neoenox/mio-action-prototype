# Unity → Three.js 移植エージェントチーム設計

作成日: 2026-11-24

## 目的

`C:\gemini-desktop\ryona-action` の Unity プロジェクト（以下「Unity 版」）を、Three.js + TypeScript へ移植するためのエージェントチームの構成と、移植作業の進め方を定義する。

ゴールは「Unity と同じ見た目・挙動の再現」を前提とする。見た目の変更は Unity 版の連番キャプチャと同じフレームで突き合わせ、画像差分と数値ログの両方で検証する。

## 作業ルール（既存 AGENTS.md を継承）

- 作業は必ず新しい worktree で行う。作業用: `C:\gemini-desktop\ryona-action-three-port`（ブランチ `port/threejs`）。
- ローカルで重いテストバッチは回さない。軽いテストと撮影・ログで確認する。
- 変更はコミット → push → PR → マージ → main を最新化、の流れを守る。
- 応答は日本語。
- カメラは「彼女の顔が映る」ことを既定要件とする。

## エージェント構成

| 名前 | 担当 | 主な入力 | 成果物 |
|---|---|---|---|
| `lead` | 調整・worktree 管理・最終判断・PR | 全体 | 設計・計画・マージ |
| `architect` | C# → TS の対応表と移植順序の管理 | 調査結果 | `docs/**` の設計・対応表 |
| `core-port` | `RyonaCore` の純粋ロジックを TS 化 | `RyonaCore/Runtime/**` | `src/core/**` + Vitest |
| `game-port` | ゲームループ・入力・状態遷移の移植 | `EnemyBrain*`, `PlayerGrapple*`, `Config/**` | `src/game/**` |
| `rig-port` | 手続きリグ・ポーズ・関節制限の移植 | `CharacterAnimator*`, `Rig/**` | `src/rig/**` |
| `visual-port` | カメラ・演出・可視判定の移植 | `Feel/**`, `ThirdPersonCamera` | `src/visual/**` |
| `verifier` | Three.js 側の撮影・差分検証パイプライン | `tools/preview.py`, `motion_audit.py` | `tools/**`, `tests/visual/**` |

## 移植先プロジェクト

- 構成: Vite + TypeScript + Three.js + Vitest（新規）
- 置き場所: worktree 直下（`package.json`, `src/`, `tools/`, `tests/`）
- 座標系: glTF/Three.js は Y-up・右手系。Unity の X-up/Y-up 差と forward の符号を `src/core/math.ts` で吸収する。

## 調査で判明した事実

### RyonaCore（core-survey より）

- Runtime 16 ファイル・型は 40 近く。UnityEngine 名前空間の `using` は 1 件もなし。Mathf / Vector3 / MonoBehaviour / Transform / GameObject / Time / Debug / SerializeField の実利用もゼロ。
- `BodyVec3` が既に UnityEngine.Vector3 非依存の自前実装。TS の `{x,y,z}` へ 1:1 マッピング可能。
- EditMode テスト 10 ファイル・59 ケースは NUnit の `Assert.AreEqual` 等だけで、Vitest へ 1:1 変換可能。
- 注意点: float → number（IEEE754 double）による境界値の差。`GrappleResistance` は Ceiling 回数、`PinOwnerRules` / `EnemyRoleRules` は `HeightEpsilon=0.0001` で同点判定しており、そのまま移植してテストで固定する。
- 依存の根は `CombatConstants` と各種 enum。`DamageResolver` が最上位（演出側への唯一の入口）。

移植順（依存の少ない順）:

1. `CombatConstants` + 全 enum + `ArenaPlan` / `WristLockRules` / `PinballRules`
2. `BodyDamage` / `JointLimit` + `HumanJointLimits` / `BodyVec3` + `BodyCapsule` + `BodyPartSpecs` / DTO と選択ロジック / `AttackData` + `AttackLibrary`
3. `CombatState` / `HitResult` / `GrappleResistance` + `ResistanceMath` / `JointRom*` / `BodyOverlap*`
4. `DamageResolver`

### 演出・リグ・検証（visual-survey より）

- `ThirdPersonCamera` が基盤。`PushDistance` / `PushPivot` / `PushFocus` / `HoldFocus` / `RequestCinematicShot` の「押し込み API」だけを演出側が呼ぶ。距離を直接書かない設計。
- `FaceVisibility.Measure` は顔中心＋カメラ right/up 方向の 5 点について、画面内（固定アスペクト 16/9、FrameMargin=0.03）と敵プリミティブとの線分交差（NearFaceSlack=0.10m）を判定。`Readable = Visible >= 0.6 && FacingAngle <= 60°`。段階ごとの下限 `RequiredRatio = {0.75, 0.75, 0.90, 0.75}` をテストで固定。
- 物理レイキャストは使わない（敵にコライダが無いため）。Three.js でも CPU 側のプロキシ判定をそのまま移植可能。
- `GroundMountAttack.FrameMount` は 150 候補（横2 × ヨー5 × 距離4 × 高さ5）を毎フレーム 12 個ずつ採点。レンズ余裕 0.25m、ヒステリシス 0.15 点、0.6 秒ロック、再選択条件（隠れ 0.4s／背け 0.6s）を満たす。
- `HitStop` は `timeScale=0` ＋ unscaled 時間。`CameraShake` は前フレームのオフセットを引いてから足す（DefaultExecutionOrder=30000）。Three.js では scaled delta と real delta の二系統の clock が必要。
- `preview.py` の SCENARIOS は実コードでは 19 場面（AGENTS.md の 8 場面説明より多い）。`--reel` は `bellybody` / `frontpin` / `haul` の 3 本。
- `motion_audit.py` はグレースケール 160×90 リサイズ後の平均輝度差。`ratio = peak / avg`、運用しきい値 5 倍。打撃由来の 3〜4 倍と `cut:true` の意図的カットは正常。
- 最難関: `HeldBellyPunchDirector` の 7 状態＋段の表、カメラ構図の競合解決、二系統 clock、手続きリグの角度計算、フェード例外と「測れないもの」の扱い。

### RyonaGame（game-survey より）

- `GrappleSession` は Unity 非依存の純粋コンテナ。**2 打撃スロット** `SplitStrike[]` を持ち、`ClaimStrike(who)` は空きが無ければ `null` を返す（満杯時に既存打撃を壊すバグの修正）。各スロットは `Puncher/Age/Windup/Left/Face/Kick/Finisher`。
- **WallStage は 12 段階**: `None, Grab, Rise, Throw, Slam, Lean, Regrip, Restrain, Held, TossWindup, TossAir, TossLand`。各段の秒数は `Grab=0.22 / Rise=0.46 / Throw=0.30 / Slam=0.13 / Lean=0.57 / Regrip=0.28 / Restrain=0.52`。`WallRestraintWeight` 等は段階と進捗の純粋関数。
- 壁シーケンス: 膝5発 → 二体目入場(0.40s溜め) → 猶予1(10入力/1.5s) → 二人がかり腹パン6発 → 猶予2(14入力) → 三人がかり15発（2枠同時） → 投げ捨て。`try/finally` で `FinishWallSequence()` を必ず通し、`WallSequenceActive=false` にする後始末が必須。
- `BearHug`: `Rest→Pull(0.12s)→Contact(0.10s)→Release(0.30s)→Rest`。位置は `captor.pos + dir*(0.54 - 0.16*phase)`、12 入力で脱出。
- `PlayerMotor`: 横1列＋`LaunchBack` は `v=√(2gh)`, `air=2v/g`。`EnemyBrain` フェーズは `Chase/Windup/Recover/Dead/Attack/Stagger/Strafe`。
- `RyonaConfig` は ScriptableObject 17 セクション。起動引数 > JSON アセット > コード既定値の順で解決する。
- `InputReader` は intent（`move`, `light/heavy/break/struggle`）へ変換してからロジックへ渡す。もがきは J/K/L/Space を 1 回としてカウントし、左右移動は数えない。
- 高難易度は Rig（VRM/GLTF ボーン・全ポーズ）と Feel（Coroutine の状態機械化、二系統 clock）に集中。`Mathf`/`Vector3` は低難易。

## モジュール境界

```
src/
  config/  ConfigStore, RyonaConfig(JSON), onConfigChanged   ※依存なし
  input/   InputReader, LaunchOptions(URLSearchParams)       ※config/clockのみ
  core/    Clock, math, PRNG, CombatConstants, GrappleResistance ※Unity非依存
  logic/   GrappleSession, PlayerMotor, PlayerGrapple(*),
           EnemyBrain(*), EnemyGroup/Registry, EncounterDirector,
           HeldBellyPunchDirector の状態機械                ※three非依存
  rig/     ProceduralRig, CharacterAnimator(*), EnemyFigure(*),
           pose/*, FaceVisibility                            ※three＋ボーン名依存
  feel/    HitStop, CameraShake, HitFlash, ProceduralSfx,
           CameraDirector, WallSplitCamera, OcclusionFade,
           HeldBellyPunchDirector.Camera                     ※three/WebAudio/DOM
  app/     GameLoop(tick/lateTick), bootstrap, scene切替, HUD/DOM
```

境界の要点:

1. `GrappleSession` は `logic` に置き、`WallRestraintWeight` 等の純粋関数は残す。`rig`/`feel` は session を読むだけにする。
2. `PlayerGrapple.Wall` のコルーチンは明示的な段階状態機械へ。後始末を `stop()` に集約する。
3. `EnemyBrain` の命中判定は `logic` に残すが、アンカーは rig から `getPunchAnchor(zone)` を注入し、logic → rig の直接参照を切る。
4. `HeldBellyPunchDirector` は段階遷移（`logic`）とカメラ（`feel`）に分割する（Unity 版の `.Camera/.Attack/.HandOff` の境界を活かす）。
5. Config は起動引数クエリ > JSON > コード既定値。DOM/Scene 側に値を持たせない。
6. `logic` は Three.js なしで単体テスト可能にする。PlayMode のタイミング前提はフェイク Clock で `tick(dt)` を回す決定論的テストへ。

## 作業の進め方（ウェーブ）

### Wave 0: 足場（lead + verifier）— 着手済み

- Vite + TS + Three.js + Vitest の初期化（完了）。
- `src/core/math.ts`（座標系・ベクトル変換）と二系統 clock の骨格。
- 撮影ドライバ `window.__portCapture = { step(n), render(), state() }` と `f_000.png` 命名規則。
- `motion_audit.ts`（sharp で grayscale → 160×90 → 平均差分）と `SCENARIOS` の 19 キー。

### Wave 1: Core（core-port）

- 上記移植順 1〜4 を TS 化。EditMode 59 ケースを Vitest へ変換。
- `npm test` で 59 ケースが通ることを確認。

### Wave 2: Game（game-port）

- `GrappleSession` / `PlayerMotor` / `EnemyBrain` / `Config` / `InputReader` の移植。
- WallStage 12 段階と 2 打撃スロットを含む拘束シーケンスの状態機械化。
- `GrappleSessionTests` を Three.js なしで再現し、壁シーケンスの後始末をテストで固定。

### Wave 3: Rig（rig-port）

- `JointSkeletonBuilder` / `JointRomProbe` / `CharacterAnimator` の手続きポーズ。
- Rest 姿勢の取り方を glTF の Y-up 右手系に合わせる。
- JointRom の 12 関節 × 左右の Tally ログを Unity と同じ書式で出す。

### Wave 4: Visual（visual-port）

- `ThirdPersonCamera` の押し込み API、`CameraDirector`、`WallSplitCamera`、`HeldBellyPunchDirector` のカメラ部分。
- `FaceVisibility.Measure`（5 点、線分交差、RequiredRatio）と `OcclusionFade`。
- `HitStop` / `CameraShake` / `HitFlash` の二系統 clock 対応。

### Wave 5: 検証（verifier 主導、全員協力）

- Unity 版の `preview.py` で撮った 19 場面の `sheet.png` を基準に、Three.js 版を同じフレームで撮る。
- Motion audit で peak/avg を比較。差分が下がったことを数字で示す。
- 前髪・腕・HUD の重なりは image sheet で人が確認する運用を残す。

## 承認状況

- 2026-11-24: ユーザーが「今後の作業はこのチームで作業割り振って行って」と承認。設計・実装の進行を許可済み。
- 3 体の調査（core / game / visual）すべて反映済み。

## 次の作業（2026-11-24 チーム議論で決定）

PR #88 マージ後、全員の投票で **(D) 実リグ/カメラへの差し替えと Unity 版とのフレーム単位画像比較** を最優先に決定した（3票: core / rig / verifier。他は (A) 1票、(C) 1票）。

投票の内訳:
- core-port: (D) — 数値が揃った今、画像比較だけが「Unity と同じ見た目」を検証できる
- rig-port: (D) — pose/* は差分の出た箇所から手を付ける。手始めは Down の地面ロールとマウント反転
- verifier: (D) の前半 — プロキシのままでは motion_audit も sheet.png も意味のある比較にならない
- game-port: (C) — punch anchor の配線（logic → rig）が先
- visual-port: (A) — HeldBellyPunchDirector.Camera が先

採用した手順:
1. verifier が Unity 版 `preview.py` で比較基準画像を撮って保全（最低でも reel の3本 bellybody / frontpin / haul）
2. rig / visual / game が実リグ・カメラ・punch anchor を `src/app/main.ts` に接続する API を用意（(C)(A) は (D) の入力として並行）
3. Three.js 側で同じシナリオを撮り、`motion_audit --compare` で peak/avg を比較
4. 差分の大きいフレームを `--strip` で開き、(A)(B)(C) のどこを直すか絞る
5. 最後に (E) 19 場面すべてを撮影

作業用 worktree: `C:\gemini-desktop\ryona-action-wave1`（ブランチ `port/threejs-wave1`）。

## Wave 2: 構図・スケール合わせ（2026-11-25 完了）

投票: (A) 5票（rig / visual / verifier / test-player / core）、(B) 実ゲーム配線 1票（game）。

Unity の実値: `Prototype.unity` の FOV 40、`RyonaConfig.Camera` の
Distance 5.2 / BeltPitch 14 / BeltPivotHeight 0.86 / BeltMinZoomDistance 3.8。
シナリオごとに別カメラ（三人称 / BeltScreenLock / PinHoldPose / WallSplitCamera /
HeldBellyPunch / 地面カメラ）を使うため、単一の既定値では合わなかった。

`src/feel/scenario-camera.ts` にシナリオ別プロファイルを追加し belt モードで切り替える。
frontpin の占有率は 20% → 75%、haul は 60%。punch 系のプロファイルは 5.2m にしないと、
`HeldBellyPunchCamera` の倍率（Unity の基本距離 5.2m 前提）で実距離が詰まり頭だけになる。

## Wave 3: 実ゲーム配線（2026-11-25 完了）

投票: (B) 4票（game / verifier / test-player / core）、(C) 19場面比較 2票（rig / visual）。

`src/logic/world.ts` の `GameWorld` が 入力 → 移動 → AI → 拘束/壁 → ヒット解決 の順で回し、
`snapshot()` で rig に渡す。app はシナリオ名からの決め打ちポーズをやめ、
`world.tick(dt, intent)` と `snapshot()` だけで動く。`state().world` に snapshot を出す。

verifier が Unity の 19 場面すべての基準画像を撮って `Verify/baseline/manifest.json` に保全した。

残課題: プレイヤーの攻撃（light/heavy/break）が未配線、膝 5 発が logic に無い、
壁の 1 発のダメージが仮値、`DamageResolver` を経由していない。
