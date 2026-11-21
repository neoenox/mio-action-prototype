# 状態遷移の第 2 段階監査（敵 AI / 拘束 / カメラ / 姿勢）

- 監査日: 2026-11-21
- 対象: `docs/state-transition-audit` 作成時点の `main`（`3c565b4`）
- 対象 Issue: #58「[P2] EnemyBrain / PlayerGrapple / カメラ / CharacterAnimator の状態遷移を第2段階監査する」
- 関連: `REVIEW.md` 4 章 P2「状態遷移の継続監査（第 2 段階）」

## 0. 前提（重要）

**これは静的に読んだ結果であり、実行時検証は未実施である。**

- Unity は起動していない。`tools/run_tests.py` / `tools/preview.py` も実行していない。
- したがって本書には「撮って確かめた画」「テストが通った事実」は無い。
- 記述は 2 種類に分けて書く。
  - **【コード上】** … ソースを読めば確定する事実（行番号つき）。
  - **【要検証】** … コードからはそう読めるが、実際の画・タイミングは未確認。
- 挙動を変えるコード修正はしていない。確定的な不具合も修正せず「要修正候補」に条件と行番号だけを書く。
- 行番号は 2026-11-21 時点のファイル内容と一致することを `awk` で突き合わせて確認した。

### 表記

| 記号 | 意味 |
| --- | --- |
| 【コード上】 | 行番号つきで確定。読めば誰でも同じ結論になる |
| 【要検証】 | コードから読めるが、画・実時間の挙動は未確認 |
| 要修正候補 | 再現条件と該当行が特定できた欠陥。Issue 化の候補 |
| 留意 | 欠陥とまでは言えないが、触るときに知っておくべき非対称 |
| 略記 | `.HandOff.cs` / `.Wall.cs` などは `PlayerGrapple.*` / `EnemyBrain.*` の partial ファイルを指す |
| §4 の裸の行番号 | `:NNN` だけのものは `ThirdPersonCamera.cs` を指す |

---

## 1. 状態の所有者一覧

| 状態 | 所有者 | 書き換え入口 | 主な読み手 |
| --- | --- | --- | --- |
| `EnemyBrain.Phase`（`EnemyPhase`） | `EnemyBrain`（`private set` / `EnemyBrain.cs:125`） | 自クラス内の 21 箇所（§2） | `MatchHud` / `PlayerGrapple` 間接 / `EnemyFigure` / 各 Pose / `PinballDirector` |
| 攻撃トークン | `EnemyGroup`（`AI/EnemyGroup.cs`） | `TryAcquireAttackToken` / `ReleaseAttackToken` | `EnemyBrain.HasAttackToken` → `DesiredRing` |
| 拘束の所有（誰が掴んでいるか） | `GrappleSession.Captor`（`GrappleSession.cs:71`） | `BeginHold`（`PlayerGrapple.Hold.cs:29`）/ `HandOff`（`.HandOff.cs:25`）/ `ReceiveFromPin`（`.HandOff.cs:98`）/ `Release`（`.Release.cs:34`）/ `Escape`（`.Escape.cs:20`） | `EnemyBrain.CanTurn` 以外のほぼ全部（`IsHolding` 判定、`CanStartFrontGrapple`、`TickHeldRecover`） |
| 拘束の種類・段階 | `GrappleSession.Kind` / `.Stage` | `BeginHold` / `HandOff` / `SetWallStage` / `AdvanceStage` | `CharacterAnimator` / `PinHoldPose` / `WallSplitCamera` |
| 拘束の残り時間・もがき | `PlayerGrapple`（`_session.LimitRemaining`, `_resistance`） | `Update` / `WallPause` | HUD、`Fail` の分岐 |
| ダウン | `PlayerStats.State.IsDown` + `PlayerMotor` | `ForceDown` / `BeginGetUp` | `CharacterAnimator` / `GroundMountAttack` |
| 腹ダメージ（拘束中被弾の蓄積） | `PlayerStats._heldBellyDamage`（`PlayerStats.cs:207`） | `PlayerStats.ReceiveHit`（`:384`）/ `ClearHeldBellyDamage` | `HasHeldBellyDown`（`Fail` / `TryPinChain`） |
| カメラの寄り引き | `ThirdPersonCamera`（`_pushScale` / `_pullScale` / `_pivotMeters` / `_focus*`） | `PushDistance` / `PushPivot` / `PushFocus` / `HoldFocus` | `TickEffects` → `LateUpdate` |
| 固定ショット | `ThirdPersonCamera._cinematic*` | `RequestCinematicShot`（**毎フレーム呼ぶ契約**） | `ApplyCinematicShot` |
| 姿勢 | `CharacterAnimator`（プレイヤー） / `EnemyFigure`（敵） | `ApplyPose` / 各 `*Pose.cs` | カメラ（顔可視率）、`FaceVisibility` |

---

## 2. EnemyBrain

### 2.1 `EnemyPhase` を書き換えている箇所（全 21 箇所）

宣言と初期値は `EnemyBrain.cs:125`（`= Chase`）。以下は**代入**のみを数えたもの（`==` 比較は除く）。

| # | ファイル:行 | 変更 | 発生条件 | 呼び出し元 |
| --- | --- | --- | --- | --- |
| 1 | `EnemyBrain.Attack.cs:34` | `Windup → Chase` | フェイント成立（`!_tagPunchPending && Id != Grab && !_feinted && 後ろ拘束中でない && Random < FeintChance`） | `TickWindup` |
| 2 | `EnemyBrain.Attack.cs:39` | `Windup → Attack` | `_timer <= 0`（フェイントを通らなかった） | `TickWindup` |
| 3 | `EnemyBrain.Attack.cs:130` | `Attack → Recover` | `Strike` の末尾（常に）。`_timer` は掴めた場合 `GrappleLimitSeconds+0.2`、外した場合 `RecoverSeconds` | `Update` の `case Attack` |
| 4 | `EnemyBrain.AttackSelection.cs:224` | `任意 → Windup` | `BeginAttackWindup` の末尾 | `TryBeginAttack` / `TryStartTagPunch` / `TryStartPinballPunch` / `TryStartHeldThrow` |
| 5 | `EnemyBrain.Grapple.cs:159` | `任意 → Recover` | `TakeOverHold(seconds)`（引き渡しの受け取り） | `PlayerGrapple.HandOff` の `Shove` / `ReceiveFromPin` |
| 6 | `EnemyBrain.Grapple.cs:199` | `任意 → Stagger` | `ForceStagger(seconds)`（`Dead` なら何もしない） | `PlayerGrapple.Escape` / 外部 |
| 7 | `EnemyBrain.Grapple.cs:230` | `Recover → Chase` | `EndRecover`（`TickHeldRecover` で `_timer <= 0`、または保持解除を検出して `_timer = 0`） | `TickHeldRecover` |
| 8 | `EnemyBrain.Grapple.cs:255` | `任意 → Chase` | `CancelWindup`（硬直を挟まない中断） | `Update` の壁待ち / アシスト / 倒れ / 冗長掴み、`BeginWallAssist` |
| 9 | `EnemyBrain.Grapple.cs:814` | `任意 → Recover` | `ForceStartBackGrab`（デバッグ・テスト） | `Debug/*` |
| 10 | `EnemyBrain.Grapple.cs:849` | `任意 → Recover` | `ForceStartPin`（同上） | `Debug/*` |
| 11 | `EnemyBrain.Grapple.cs:862` | `任意 → Recover` | `ForceStartBearHug`（同上） | `Debug/*` |
| 12 | `EnemyBrain.Lifecycle.cs:49` | `任意 → Stagger` | `EnterStagger`（`TakeHit` で `Stagger != None`） | `TakeHit` |
| 13 | `EnemyBrain.Lifecycle.cs:68` | `Stagger → Chase` | `EndStagger`（`TickStagger` で `_timer <= 0`） | `TickStagger` |
| 14 | `EnemyBrain.Lifecycle.cs:91` | `任意 → Dead` | `Die`（`State.IsAlive == false`） | `Update` の `!State.IsAlive` / `TakeHit` |
| 15 | `EnemyBrain.Lifecycle.cs:138` | `Dead → Chase` | `Revive`（波の再出現） | `EncounterDirector` / テスト |
| 16 | `EnemyBrain.Lifecycle.cs:184` | `任意 → Chase` | `ResetForMotionPreview`（撮影） | `MotionShortcut` 等 |
| 17 | `EnemyBrain.Locomotion.cs:88` | `Strafe → Chase` | トークン未所持 && `Time >= _attackReadyAt` && `Group.TryAcquireAttackToken` 成功 | `TickStrafe` |
| 18 | `EnemyBrain.Locomotion.cs:100` | `Strafe → Chase` | `distance > desired * 1.45`（引き離された） | `TickStrafe` |
| 19 | `EnemyBrain.Locomotion.cs:119` | `任意 → Strafe` | `EnterStrafe`（タイマー満了 / トークンを返した / 殴れなかった） | `TickChase` / `TickStrafe` |
| 20 | `EnemyBrain.cs:600` | `任意 → Recover` | `LockForHandOff(seconds, releaseToken)`（引き渡しの固定、位置合わせ） | `PlayerGrapple.HandOff` / `TryStartPinHandOff` / `HeldBellyPunchDirector.Attack.cs:150` / `PinballDirector` |
| 21 | `EnemyBrain.cs:616` | `任意 → Recover` | `BeginEvade(distance, seconds)`（顔トドメ回避） | `HeldBellyPunchDirector` |

【コード上】`Phase` の setter は `private`（`EnemyBrain.cs:125`）で、partial クラス外からは書き換えられない。書き換えは必ず上表の 21 箇所のどれかを通る。

### 2.2 Attack → Recover → Chase の遷移条件

| 遷移 | 条件（【コード上】） | 該当行 |
| --- | --- | --- |
| `Windup → Attack` | `_timer <= 0`。ただし直前にフェイント判定（`_feinted` は 1 回の接近につき 1 度だけ） | `EnemyBrain.Attack.cs:25-40` |
| `Attack → Recover` | `Strike` が必ず末尾で代入。掴めた場合のみ `ReleaseToken` + `_grabReadyAt = Time + 7.2f` | `:130-145` |
| `Recover → Chase` | `TickHeldRecover` で `_timer <= 0` → `EndRecover`。`EndRecover` は `ReleaseToken` + `_attackReadyAt = Time + AttackCooldown` | `EnemyBrain.cs:1181-1183` / `EnemyBrain.Grapple.cs:222-231` |
| `Recover` の凍結 | `IsHolding && hold.IsGrappled && hold.Captor == this` の間は `TickHeldRecover` が `return` し、**`_timer` を減らさない**（掴んでいる間は硬直が明けない） | `EnemyBrain.cs:1171-1179` |
| `Recover` の強制解除 | 保持が解けたことを検出したら `IsHolding = false; ClearGrappleState(); _timer = 0f;` → 同フレームで `EndRecover` | `EnemyBrain.cs:1176-1178` |

【コード上】掴みが成立した `Recover` は「タイマー」ではなく「拘束の終了」で明ける。`Escape` は `ForceStagger` で `Stagger` へ飛ぶため `EndRecover` を通らない（`PlayerGrapple.Escape.cs:28-29`）。この非対称は意図的（振りほどいた側をよろけさせる）。

### 2.3 攻撃トークンの取得と返却

#### 取得（4 経路 + 1）

| 箇所 | 条件 |
| --- | --- |
| `EnemyBrain.Grapple.cs:380`（`TryBeginAttack`） | 抽選が決まった後。取れなければ `return false`（`Strafe` 継続） |
| `EnemyBrain.Grapple.cs:110`（`TryStartTagPunch`） | タッグ腹パン。取れなければ演出側が 0.3 秒後に選び直す |
| `EnemyBrain.Grapple.cs:49`（`TryStartPinballPunch`） | ピンポンの起点 |
| `EnemyBrain.cs:177`（`TryReserveTagPunchToken`）→ `AI/EnemyGroup.cs:116` | 位置合わせの前に先取り（`HeldBellyPunchDirector.Attack.cs:120` / `EnemyBrain.Attack.cs:166`） |
| `EnemyBrain.Locomotion.cs:86`（`TickStrafe`） | 順番待ちから `Chase` へ移るときに先取り |

#### 返却（【コード上】すべての離脱経路に存在する）

| 箇所 | 契機 |
| --- | --- |
| `EnemyBrain.Grapple.cs:225`（`EndRecover`） | 硬直明け |
| `EnemyBrain.Grapple.cs:253`（`CancelWindup`） | 予備動作の中断 |
| `EnemyBrain.Lifecycle.cs:47`（`EnterStagger`） | 被弾で怯んだ |
| `EnemyBrain.Lifecycle.cs:97`（`Die`） | 死亡 |
| `EnemyBrain.cs:974` / `:983`（`OnDisable` / `OnDestroy`） | 無効化・破棄 |
| `EnemyBrain.Attack.cs:33` / `:142` | フェイント成立 / 掴み成立 |
| `EnemyBrain.cs:597` / `:614` | `LockForHandOff(releaseToken:true)` / `BeginEvade` |
| `EnemyBrain.cs:753`（`HoldFire`） | `Phase` が `Chase` / `Strafe` のときだけ返す（**`Recover` 中は返さない**） |
| `EnemyBrain.Locomotion.cs:16` | 手前の仲間に塞がれた |
| `EnemyBrain.Grapple.cs:707`（`BeginWallAssist`） | アシスト役に降りた（`Group.ReleaseAttackToken` 直呼び） |
| `AI/EnemyGroup.cs:104`（`Unregister`） | グループ離脱時に `_attackers` からも落とす |

【コード上】「先取りしたトークンが返らない」経路は見つからなかった。`HeldBellyPunchDirector.Attack.cs` の `PlaceThenAttack` が途中で `yield break` しても、先に `LockForHandOff(..., releaseToken:false)`（`EnemyBrain.cs:588-603`）で `Phase = Recover` / `_timer = length` に落としているため、タイマー満了の `EndRecover` が返す。

**留意（【コード上】）** `HoldFire` は `Chase` / `Strafe` のときしかトークンを返さない（`EnemyBrain.cs:752-753`）。`PlayerGrapple.Fail` は周囲 6m の全員に `HoldFire(grace)` をかけるが（`PlayerGrapple.Escape.cs:66-76`）、ちょうど `Recover` 中だった敵はトークンを保持したまま猶予に入る。`DesiredRing` はトークン保持者を間合いまで寄せるので、その 1 体だけが前に出る。実害は未測定【要検証】。

### 2.4 拘束中の行動禁止が全経路で一貫しているか

`Update` の早期 `return` を全部並べたもの（`EnemyBrain.cs:988-1092`）。

| 行 | 条件 | 止まるもの | 備考 |
| --- | --- | --- | --- |
| `990` | `Phase == Dead` | 全部 | `Die()` が `enabled = false` も立てるので二重 |
| `1005-1009` | `!State.IsAlive` | 全部（`Die()` を呼ぶ） | HP 0 の検出 |
| `1014-1027` | `_riseTimer > 0` | 移動・攻撃（`Face` のみ） | 立ち上がり |
| `1029` | `Target == null \|\| Player == null` | 全部 | 参照切れ |
| `1032` | `Player.GroundMount.Owns(this)` | 全部 | マウント中の敵は AI 停止 |
| `1039-1045` | `_wallAssistHold != null` | 通常 AI（持ち場へ移動） | 二体目 |
| `1049-1053` | `IsPinning && IsWallSplitActive` | 通常 AI（横へ回り込む） | 壁の拘束役 |
| `1055-1067` | 他人が壁押さえつけ中 | `Windup`/`Attack` を中断し、`Chase`/`Strafe` なら退避 | 待機組 |
| `1069` | `Chase`/`Strafe` かつ `TryApproachForPin` | 移動のみ（正面へ回り込む） | |
| `1071` | `Chase`/`Strafe` かつ `TryFlank` | 移動のみ | |
| `1091` | `IsPlanted` かつ `Chase`/`Strafe` | 移動・攻撃 | 演出側が位置を決めている間 |
| `1095` | `playerDown` | `Chase`/`Strafe`/`Windup` を止める。`Recover`/`Stagger` は進む | フェアネス規定 |

#### 拘束されている側（Captor）の禁止

- Captor は掴み成立時に `Phase = Recover` になる（`EnemyBrain.Attack.cs:130`）ので、`TickHeldRecover` 以外は走らない。再攻撃の入口である `TickChase` / `TickStrafe` には到達しない。
- `TryStartTagPunch` は `grapple.Captor == this` を明示的に弾く（`EnemyBrain.Grapple.cs:94`）。
- したがって「Captor 自身がもう一発殴る」経路は無い【コード上】。

#### 拘束されていない側（Bystander）は止めない

- 意図的に止めない。`TryBeginAttack` は「プレイヤーが掴まれている」ことを条件に入れておらず、`CollectCandidates` が除外するのは掴み攻撃だけ（`EnemyBrain.AttackSelection.cs:126-131`）。タッグ腹パン（`HeldBellyPunchDirector`）がこの経路を使う。
- ただし `PlayerStats.DamageScaleAgainst` が「掴んでいる本人以外」のダメージを減らす（`PlayerStats.cs:421`）。拘束中の袋叩きは設計上あり。

#### 一貫していない 1 点（要修正候補 A-3）

`DoPinKnee`（`EnemyBrain.Grapple.cs:617-621`）は `Player.IsDead` を見ていない。同じ「拘束中に HP を削る」処理である `DoBearHugSqueeze`（`EnemyBrain.Grapple.cs:590-593`）と `DoWallSplitPunch`（`:782-783`）は見ている。

### 2.5 死亡後の処理停止

| 項目 | 【コード上】の事実 | 行 |
| --- | --- | --- |
| `Update` | `Phase == Dead` で即 `return` | `EnemyBrain.cs:990` |
| コンポーネント | `enabled = false`（`Update` / `LateUpdate` ごと止まる） | `EnemyBrain.Lifecycle.cs:99` |
| コライダー | `GetComponent<Collider>().enabled = false`（死体を障害物にしない） | `:106-108` |
| 攻撃トークン | `ReleaseToken()` | `:97` |
| グループ | `UnregisterGroup()`（`_attackers` からも外れる） | `:98` |
| 拘束フラグ | `IsHolding = false` / `ClearGrappleState(clearAttackState:true)` | `:93-94` |
| イベント | `Died?.Invoke(this)` | `:114` |
| **掴んでいた相手の解放** | **`hold.Release()` は呼ばない** | （無し） |

【コード上】死亡時に `PlayerGrapple` のセッションは解放されない。解放するのは `PlayerGrapple.Update` の
`if (Captor == null || !Captor.IsAlive) { Release(); }`（`PlayerGrapple.cs:636-641`）で、**遅くとも 1 フレーム後**。
`Captor` が Unity の `null`（破棄済み）でも `== null` が真になるため同じ経路で解放される。

【要検証】その 1 フレームの間に `PlayerGrapple` 側が `SnapToCaptor(Captor, ...)` などを通るかは実行順（`DefaultExecutionOrder` 無し）次第。`SnapToCaptor` は `captor == null` をガードしている（`PlayerGrapple.Release.cs:51-52`）ので例外にはならない。

### 2.6 リセット（`Revive` / `ResetForMotionPreview`）

| 項目 | `Revive`（`EnemyBrain.Lifecycle.cs:118-149`） | `ResetForMotionPreview`（`EnemyBrain.Lifecycle.cs:151-185`） |
| --- | --- | --- |
| HP・部位・怯み | `State.RestoreAll()` | `RestoreAll()`（`Dead` なら `Revive`） |
| `Phase` | `Chase`（`:138`） | `Chase`（`:184`） |
| `IsHolding` / 拘束フラグ | 落とす | 落とす |
| `_riseTimer` | `RiseSeconds × 1〜1.4` | `0` |
| コライダー | 戻す | 戻す |
| `HoldsBellyPunchGuard` | **落とさない**（要修正候補 A-6） | `false`（`:172`） |
| フランキング・ランジ・壁アシスト | 落とさない | `StopFlanking` / `AbsorbLungeOffset` / `ReleaseWallAssist` |

---

## 3. PlayerGrapple

### 3.1 所有権（Captor / session）の開始と返却

| 操作 | 箇所 | `_session.Captor` | 新 Captor 側の状態 | 旧 Captor 側の状態 |
| --- | --- | --- | --- | --- |
| 掴み開始 | `PlayerGrapple.Hold.cs:9-76`（`BeginHold`） | `= captor`（`:29`） | 呼び出し元（`EnemyBrain.TryGrab` / `TryPin` / `TryBearHug` / `TryWallPin`）が `IsHolding` 等を立てる | — |
| 壁押さえつけ開始 | `PlayerGrapple.Wall.cs:13-46`（`BeginWallPin`） | `BeginHold` 経由 | `IsWallPinning = true`（`EnemyBrain.Grapple.cs:551`） | — |
| 引き渡し（押し出し有） | `PlayerGrapple.HandOff.cs:13-51` | `= next`（`:25`）→ `Shove` 完了時に `TakeOverHold`（`:255`） | `LockForHandOff` で `Recover` 固定（`:38`） | `LockForHandOff` で `Recover` 固定 + `PushAway`（`:36-37`） |
| 引き渡し（押し出し無） | 同上（`shove == false`、`:46-50`） | `= next`（`:25`） | **何も立てない**（要修正候補 A-4） | 何もしない（次フレームに `Captor != this` を検出して自分で落とす） |
| 正面拘束 → 後ろ拘束 | `ReceiveFromPin`（`.HandOff.cs:71-118`） | `= receiver`（`:98`） | `TakeOverHold`（`:104`） | `LockForHandOff(0.24f)`（`:105`） |
| 罰なし解放 | `Release`（`.Release.cs:12-43`） | `= null`（`:34`） | — | 何もしない（相手は `PlayerGrapple` を見て自分で落とす） |
| 振りほどき | `Escape`（`.Escape.cs:9-33`） | `= null`（`:20`） | — | `ForceStagger`（`:29`） |
| 時間切れ | `Fail`（`.Escape.cs:45-82`） | `Release` 経由 | — | `Release` → 周囲の敵に `HoldFire` |

【コード上】`Release` / `Escape` は `Captor` に何も通知しない。Captor 側は次の自分の `Update` で
`hold.Captor != this` を見て `IsHolding` を落とす（`EnemyBrain.cs:1171-1179`）。したがって
「プレイヤー側の解放」と「敵側のフラグ」は **1 フレーム遅れて一致する**。`Escape` だけは `ForceStagger` で即座に揃う。

### 3.2 敵が無効化された場合

| ケース | 検出 | 行 | 結果 |
| --- | --- | --- | --- |
| HP 0（死亡） | `!Captor.IsAlive` | `PlayerGrapple.cs:636` | `Release()`（罰なし） |
| GameObject 破棄 | `Captor == null`（Unity の擬似 null） | `:636` | `Release()` |
| コンポーネントの `enabled = false` のみ | **検出しない** | `:636` は `IsAlive` しか見ない | 拘束は続く【要検証】 |
| プレイヤー死亡 | `Stats.IsDead` | `:643-647` | `Release()` |
| 引き渡し中に受け手が消えた | `CanContinueHandOff` | `.HandOff.cs:275-287` | `Release()` + `yield break` |
| 正面拘束 → 後ろ拘束中に受け手が無効化 | コルーチンの `while` 条件（`receiver.isActiveAndEnabled`） | `.HandOff.cs:78` | ループ離脱 → `ClearPinHandOff`（拘束は元の Captor のまま） |

【コード上】「HP は残っているが `EnemyBrain` が無効」という状態を作るのは `EnemyBrain.Lifecycle.cs:99`（死亡時）だけ。したがって現状のゲームプレイでは到達しない。`SetActive(false)` を外から行う検証コードを足すと、拘束が解放されないまま残る。

### 3.3 ダウン割り込み

| 入口 | ガード | 行 |
| --- | --- | --- |
| 予備動作中に倒れた | `playerDown` → `CancelWindup()`（硬直を挟まず `Chase`） | `EnemyBrain.cs:1109-1114` |
| 判定の瞬間に倒れた | `Strike` 冒頭で `Player.IsDown` → `CancelWindup` | `EnemyBrain.Attack.cs:47-52` |
| 追跡・回り込み中 | `TickChase` / `TickStrafe` を呼ばない | `EnemyBrain.cs:1095-1108` |
| 拘束中にプレイヤーがダウン | `PlayerGrapple.Update` は `IsDown` で止めない。`WallSequenceAlive` は `!Stats.IsDown` を含む | `PlayerGrapple.Wall.cs:323-326` |
| 壁拘束中にダウン | 段階進行が停止 → `WallSequenceActive` が落ちる → 時間切れで `Fail` → 投げ捨て or `Release` | `PlayerGrapple.Wall.cs:357-366` / `PlayerGrapple.cs:706-715` |
| 拘束中にダウンした場合の姿勢 | `CharacterAnimator.LateUpdate` は `!Grapple.IsGrappled` のときだけ `Down` へ移す | `CharacterAnimator.LateUpdate.cs:145-151` |

【コード上】「拘束中はダウンしない」設計（`PlayerStats.CanTakeFlinch` が `IsDown` を見る、`PlayerGrapple` が `IsGrappled` 中は倒さない）と、上の表は矛盾しない。拘束が解けた瞬間に `ResolveHeldBellyDown`（`Fail`）または `ForceDown`（壁投げ捨て）で倒れる。

### 3.4 連続拘束・引き渡し

| 段階 | 実装 | 行 | 終了条件 |
| --- | --- | --- | --- |
| コンボ後の抽選 | `GrappleHandOff.DecideAfterCombo` → `TryChain` | `Feel/GrappleHandOff.cs:92-152` | `IsGrappled` でない / `IsHandingOff` / 受け手なし |
| 押し出し引き渡し | `Shove` コルーチン | `.HandOff.cs:148-258` | `CanContinueHandOff` が false / 完了 |
| 正面拘束の受け渡し | `ReceiveFromPin` コルーチン | `.HandOff.cs:71-118` | `1.05 秒` / 条件崩れ |
| ピンポンの往復 | `PinballDirector.RunRounds` → `HandOff(shove:true)` | `Feel/PinballDirector.cs:347-370` | `_active == false` / 最終ラウンド |

【コード上】連続拘束の入口はすべて `HandOff` を通り、`HandOff` は
`!IsGrappled || IsHandingOff || next == null || !next.IsAlive || next == Captor || Stats.IsDead` を弾く（`.HandOff.cs:15-17`）。
同じ敵が自分自身へ引き渡す経路は無い。

### 3.5 腹ダメージ上限

| 項目 | 値・式 | 箇所 |
| --- | --- | --- |
| 上限 | `MaxHeldBellyDamage = 100f` | `RyonaCore/Runtime/CombatConstants.cs:147` |
| 1 発 | `HeldBellyPunchDamage = 16f` | `:148` |
| 加算 | `_heldBellyDamage = Min(上限, 現在 + heldBellyDamage)`。条件は `CanTakeFlinch && IsGrappledNow && heldBellyDamage > 0 && result.Damage > 0` | `PlayerStats.cs:379-386` |
| 判定 | `HasHeldBellyDown => _heldBellyDamage >= 100` | `PlayerStats.cs:210` |
| 消化 | `ResolveHeldBellyDown()`（`Fail` からのみ） | `PlayerStats.cs:214-222` / `PlayerGrapple.Escape.cs:63-64` |
| 消去 | `ClearHeldBellyDamage()`（壁投げ捨て・トドメ・`RestoreAll`） | `PlayerGrapple.Wall.cs:780` / `HeldBellyPunchDirector.cs:1036,1151` / `PlayerStats.cs:409` |

【コード上】5 発コンボ（`ComboHits = 5`, `HeldBellyPunchDirector.cs:30`）×16 = 80 では上限に届かない。2 回目の拘束で 100 に達し、**次に `Fail` したとき**にダウンする。`Escape` では `ResolveHeldBellyDown` を呼ばないので蓄積は残る（§7 未確認 1）。

**条件の穴（【要検証】）** `CanTakeFlinch` は `State.IsDown` / `IsDead` / `Time < _downImmuneUntil` で false になる（`PlayerStats.cs:231-242`）。したがって**起き上がり直後の無敵中に掴まれて腹パンされると、腹ダメージが 1 も加算されない**。上限到達が遅れるだけで、ダウンは他の怯み蓄積が担う。意図的かは未確認（§7 未確認 2）。

### 3.6 コルーチンの終了条件

| コルーチン | 開始 | 終了条件（【コード上】） | 停止手段 |
| --- | --- | --- | --- |
| `WallSplitRoutine` | `TryBeginWallSplit`（`.Wall.cs:212`） | アシスト到着待ち 2.4s / `!IsSplitActive` / 6 発完了 / `Stats.IsDown` | 明示停止なし。`_session.ClearSplit()` で `Assist` が `null` になり `IsSplitActive == false` になるため自然終了 |
| `WallSequenceRoutine` | `StartWallSequence`（`.Wall.cs:336`） | `WallSequenceSteps` の完了、または `yield break` | `StopWallSequence` → `StopCoroutine(_wallSeq)`（`:339-355`） |
| `WallGang` | `WallSequenceSteps` 内で `yield return` | `WallSequenceAlive == false` / 巡回終了 | 親コルーチン停止に巻き込まれる |
| `WallGangStrike` | 同時打撃のときだけ `StartCoroutine`（`PlayerGrapple.Wall.cs:556`） | `WallSequenceAlive == false` / `finally` で枠を返す | 独立コルーチン。停止しないが 1 フレームで自滅 |
| `WallPause` | `WallSequenceSteps` | `LimitRemaining == 0` / `WallSequenceAlive == false` | 親に巻き込まれる |
| `Shove` | `HandOff(shove:true)`（`.HandOff.cs:39`） | `CanContinueHandOff` 失敗 / 完了 | なし（`Release` で条件が崩れる） |
| `ReceiveFromPin` | `TryStartPinHandOff`（`.HandOff.cs:67`） | `while` 条件のいずれか / 1.05s | なし（`ClearPinHandOff` で条件が崩れる） |
| `PlaceThenAttack` | `HeldBellyPunchDirector.Attack.cs:153` | `CanContinuePlacement` 失敗 / 発注成功 | なし |
| `PlayBodyDropAfter` | 壁投げ捨て（`.Wall.cs:783`） | 0.62s 後 | なし（`IsGrappled` / `!IsDown` で音を止めるだけ） |
| `ReleaseWithFinisherDown` / `HoldEvadeUntilLanded` | `HeldBellyPunchDirector.cs:943,964,998,1045,1119` | 各自の待ち時間 | なし |

【コード上】`WallSequenceRoutine` は `try/finally` で `FinishWallSequence` を必ず通す（`.Wall.cs:367-378`）。
`FinishWallSequence` が `WallSequenceActive` を落とさないと「時間切れの解放」が止まり、壁拘束が永続する（`:357-366` のコメント）。ここは二重の安全（`StopWallSequence` と `finally`）になっている。

### 3.7 カメラ要求の解除

| 要求元 | 要求 | 解除 |
| --- | --- | --- |
| `PinHoldPose`（正面拘束・壁拘束・Y 字） | `RequestCinematicShot`（`AI/PinHoldPose.cs:103,241,255,330`）/ `HoldFocus`（`:275,337`） | 冒頭の「拘束が解けたら `return`」（`:62-67`）で要求が止まり、`CinematicExitSeconds = 0.35f` で戻る（`ThirdPersonCamera.cs:398-420`） |
| `HeldBellyPunchDirector` | `PushDistance` / `PushPivot` / `PushFocus` / `HoldFocus`（`Feel/HeldBellyPunchDirector.Camera.cs:113-152`） | `ResetHold` の `PushFocus(0f,0f)`（`HeldBellyPunchDirector.cs:441-443`）。距離・ピボットは `hold=0.5f` の減衰に任せる |
| `PinballDirector` | `HoldFocus`（`Feel/PinballDirector.cs:100,491`） | `Cancel()` の `HoldFocus(1f,0f,0f,0f)`（`:483`）→ `ResetDistanceEffect` + `ResetPivotEffect`（`ThirdPersonCamera.cs:704-710`） |
| `GroundMountAttack` | `RequestCinematicShot`（`Feel/GroundMountAttack.cs:539,654`） | `Stage` が終わると要求が止まる → 0.35s で戻る |
| `GroundHaulUp` | `RequestCinematicShot`（`Feel/GroundHaulUp.cs:570`） | 同上 |
| `CameraDirector` | `PushDistance` / `PushPivot`（`Feel/CameraDirector.cs:201`, `:214-215`, `:226-227`, `:254-255`） | 時間減衰のみ（保持しない） |
| プレイヤーのホイール | `SetDistance` | 演出とは独立（`Distance` を書き換えない） |

【コード上】**解除漏れは見つからなかった**。`RequestCinematicShot` は「呼ばれたフレームだけ有効」で、
呼ばれなくなった次のフレームに退出ブレンドへ入る（`ThirdPersonCamera.cs:400-408`）。要求を続けるコンポーネントが
`OnDisable` されれば要求も止まる。唯一の例外は `cut: true` で、この場合は退出ブレンドをスキップする（意図的）。

### 3.8 入力の消費とリセット

| 項目 | 【コード上】の事実 | 箇所 |
| --- | --- | --- |
| もがき | `InputReader.StrugglePressed()` = `Light/Heavy/Break/Space` のいずれかの**エッジ**。消費しない（同じフレームで何度呼んでも true） | `InputReader.cs:206-212` |
| 呼び出し元 | `PlayerGrapple`（`PlayerGrapple.cs:661`, `:717`）/ `PlayerStats`（`:101`）/ `GroundMountAttack`（`:259`）/ `GroundHaulUp`（`:283`） | 各ファイル |
| 二重反応 | 1 回の押下が「もがき 1 回」と「起き上がりキャンセル」の両方に効く。設計上そうなっている | — |
| 抑制 | `SuppressStruggle(seconds)` / `_session.SuppressRemaining`（腹パン・トドメ中）。0 で `Struggle()` が早期 return | `PlayerGrapple.cs:389-406`, `:698-703` |
| ピンポン中の入力 | `_session.PinballActive` の間は `Struggle` 系を通さず、`PinballDirector` が `Brace()` で判定する | `PlayerGrapple.cs:693-694` / `PinballDirector.cs:61-70` |
| リセット | `BeginHold` が `StrugglePulse` / `FrontEscapeEffort` / `_frontEffortHold` / `KneePulse` / `KneeCount` を初期化（`PlayerGrapple.Hold.cs:52-56`）。`Escape` は `KneeCount` / `PullPulse` を戻さない（要修正候補ではなく留意） | `PlayerGrapple.Escape.cs:9-33` |

**留意（デバッグ時のみ）** `StrugglePressed` は内部で `LightPressed` → `AutoPressed(0)` を呼ぶ。`-holdattack` の自動攻撃予約はここで**消費される**（`InputReader.cs:95-109`）ので、拘束中は自動攻撃の順番が飛ぶ。製品ビルドでは発生しない。

---

## 4. カメラ

### 4.1 Push 値の優先順位（実装の実態）

| 種類 | フィールド | 合成規則 | 行 |
| --- | --- | --- | --- |
| 寄り（`scale < 1`） | `_pushScale` / `_pushRemaining` / `_pushDuration` | 既に寄っていれば**より強い（小さい）倍率**、時間は**長い方**を採用 | `ThirdPersonCamera.cs:620-628` |
| 引き（`scale >= 1`） | `_pullScale` / `_pullRemaining` / `_pullDuration` | より強い（大きい）倍率、時間は長い方 | `:629-638` |
| 最終合成 | `_distanceScale` | **寄りが生きていれば寄りが勝つ**：`pushIn < 1f ? pushIn : pullOut` | `:766` |
| ピボット | `_pivotMeters` | **絶対値の大きい方**、時間は長い方 | `:658-662` |
| フォーカス | `_focusX/Yaw/Pitch` | 残り時間の長い方。値は最後に書いたものが残る | `:672-687` |
| 固定ショット | `_cinematic*` | `LateUpdate` の最後に上書き。`_distanceScale` の影響は受けない（別経路） | `:432-457` |
| 保持 API | `HoldFocus` | **合成規則を迂回して直接代入**（要修正候補 A-5） | `:712-721` |

**重要（【コード上】）** 「当てた寄りを被弾の引きで消さない」ための規則は `_distanceScale` の合成（`:766`）にだけ入っている。
`HoldFocus` は `_pushScale` / `_pullScale` を直接書き換えるので、**呼び出し順が優先順位になる**。
実例：`CameraDirector.ApplyDeathFraming` は死亡中ずっと `PushDistance(1.25, hold)` を押すが（`Feel/CameraDirector.cs:239-256`）、
同じフレームに `PinballDirector.FrameCamera` が `HoldFocus(0.46, ...)` を呼ぶと `_pullScale = 1` に戻され、死亡の引きが見えなくなる。
`PinballDirector` / `HeldBellyPunchDirector` / `PinHoldPose` はいずれも `HoldFocus` を使うため、
**死亡演出と拘束演出が同時に走ると、後から `HoldFocus` を呼んだ方が勝つ**。

### 4.2 演出の同時開始

| 組み合わせ | 【コード上】の調停 |
| --- | --- |
| 通常ヒット vs 被弾 | `CameraDirector.OnHitReceived` は `Stagger >= Large` を `Staggered` 側に譲る（`Feel/CameraDirector.cs:204-229`） |
| 被弾演出 vs 腹パン演出 | `IsHeldPunchCameraActive()` が真なら被弾の寄り引きを出さない（`:206-212`） |
| 腹パン演出 vs ピンポン | `HeldBellyPunchDirector.Update` が `_grapple.IsPinballActive` を見て自分の演出を始めない（`HeldBellyPunchDirector.cs:502-513`） |
| ピンポン vs 他演出 | `PinballDirector.IsOtherSequenceBusy` が拘束・腹パン・マウント・ダウンを弾く（`Feel/PinballDirector.cs:188-203`） |
| 正面拘束 vs 壁拘束 | 同じ `PinHoldPose` が `hold.IsWallPin` で分岐。同時には走らない（Captor は 1 体） |
| 固定ショット vs 固定ショット | `RequestCinematicShot` は最後の要求が勝つ（`_cinematicPosition` 上書き） |

【要検証】`CameraDirector` の被弾演出と `PinHoldPose` の `RequestCinematicShot` は経路が違う（前者は `_distanceScale`、後者は固定ショット）。
固定ショット中は `LateUpdate` の最後で位置・回転を丸ごと上書きするため（`:432-457`）、被弾の寄りは画に出ない。

### 4.3 終了時の復帰

| 演出 | 復帰 |
| --- | --- |
| 寄り・引き・ピボット | `Envelope(remaining, duration)` で 0 まで減衰（`:774-784`）。`seconds <= 0` を渡すと即リセット（`:611-615` / `:649-653`） |
| 固定ショット | 要求が止まった次フレームから `CinematicExitSeconds = 0.35f` で現在の画へ戻す（`:173`, `:400-420`）。`cut: true` の直後は戻さない |
| フォーカス | `_focusRemaining` の減衰。`HoldFocus` を呼び続けている間は減らない（`:515-525`） |
| 死亡演出 | 戻さない（意図的。`Stats.IsDead` が false になるとリセット：`Feel/CameraDirector.cs:149-154`） |
| コンポーネント無効化 | `OnDisable` が全演出をリセット（`:736-748`） |

### 4.4 シーン切替時の残留

【コード上】`ThirdPersonCamera.OnDisable`（`:736-748`）が
`ResetDistanceEffect` / `ResetPivotEffect` / `_focusRemaining = 0` / `_cinematicRequested = _cinematicActive = false` を実行する。
タイトルへ戻る経路ではこのクラスが無効化されるため、**演出の残留は無い**。
`CameraDirector.OnDisable` は購読解除と死亡演出のリセットのみ（`Feel/CameraDirector.cs:130-137`）。

**留意** `HeldBellyPunchDirector.OnDisable` は `SetPunchGuard(null)` だけで、`_phase` を戻さない（`HeldBellyPunchDirector.cs:340`）。
再有効化された場合、次の `Update` で「拘束されていなければ `ResetHold`」が走るため復帰する（`:500-513`）。

### 4.5 ターゲット破棄

| 箇所 | ガード |
| --- | --- |
| `ThirdPersonCamera.LateUpdate` | `if (Target == null) return;`（`:268-269`）。`TickEffects` はその前に走るので演出の減衰は進む |
| `TickBelt` | `Target.forward` / `Target.position` を直接参照（`:469-470`, `:496`）。呼ばれる前に `LateUpdate` の null ガードを通るので到達しない |
| `TickDown` | `Target != null` を確認してから `GetComponentInChildren`（`:572-580`） |
| `LogFaceVisibility` | `_anim` / `Rig` / `Head` / `Camera` をすべて null チェック（`:374-380`） |
| `CameraDirector.Update` | `Stats == null` で `return`（`Feel/CameraDirector.cs:144-147`） |
| `Pivot()` | `Target.position` を参照（`:555-568`）。`LateUpdate` の null ガードの後でしか呼ばれない |

【コード上】カメラ側の Target 破棄は安全。**例外は `ThirdPersonCamera` が有効なまま Target だけが破棄された場合**で、
その場合も `LateUpdate` の先頭で止まるため例外にならない。

---

## 5. CharacterAnimator（姿勢）

### 5.1 姿勢パラメータの所有者

| ボーン群 | 主な書き手 | 更新順の根拠 |
| --- | --- | --- |
| 全身（初期化） | `CharacterAnimator.ApplyPose` → `Rig.ResetPose()`（`Rig/CharacterAnimator.Pose.cs:70`） | `DefaultExecutionOrder(20000)` |
| 胴・頭・脚 | `PoseIdle` / `PoseWalk` / `PoseStagger` / `PoseDown` / `PoseGetUp` / `PoseAttack` / `PoseGrabbed` | 同上 |
| 腕（拘束中の上書き） | `PinHoldPose` / `BackHoldPose` / `BearHugPose` / `OverheadHoldPose` / `AnkleHoldPose` / `HaulUpPose` / `WallAssistPose` / `WallKneeStrikePose` | すべて `21000`（`CharacterAnimator` の後） |
| 手のひら・指 | `Rig/Grip`（`pose.Grip`） | `ApplyPose` 内 |
| 敵の姿勢 | `EnemyFigure`（`11500`）→ 各 `*Pose`（`21000`） | 敵側は独立したリグ |
| 髪・揺れ | `VrmHairSpring` / `VrmHairWallClamp`（`26000`） / `VrmHairBodyMargin`（`25900`） | 全部の後 |
| カメラ | `ThirdPersonCamera`（`22500`） | 姿勢が確定した後に読む |
| 診断 | `JointRomMonitor` / `BodyOverlapWatch`（`25000`, `25100`） | 最終姿勢を測る |

### 5.2 更新順（`LateUpdate` の順序依存）

【コード上】実行順は `DefaultExecutionOrder` の昇順。

| 順序 | クラス | 何をする |
| --- | --- | --- |
| 100 | `HitFlash` | マテリアル |
| 11500 | `EnemyFigure` | 敵リグの基本姿勢 |
| 20000 | `CharacterAnimator` | プレイヤーの全身姿勢を書く |
| 21000 | `PinHoldPose` ほか拘束ポーズ | **プレイヤーの腕を上書き**し、敵の腕を IK で合わせる |
| 21100 | `GroundMountAttack` / `GroundHaulUp` | 位置と固定ショット要求 |
| 22000 | `EnemyVrm` | VRM の SpringBone 後処理 |
| 22500 | `ThirdPersonCamera` | 最終姿勢を読んで画を決める |
| 23000 | `OcclusionFade` | 遮蔽フェード |
| 25000-26000 | `JointRomMonitor` / `BodyOverlapWatch` / `VrmHair*` | 診断・髪 |

**所有権の分割（【コード上】）** プレイヤーの腕は
`CharacterAnimator.ApplyPose`（20000）が書き、その直後に `PinHoldPose.Solve`（21000）が上書きする（`AI/PinHoldPose.cs:165-168`、壁の引き上げ中は `:80-81`。定義は `:393`）。
つまり**拘束中だけ腕の所有者が事実上 `PinHoldPose` へ移る**。`CharacterAnimator` 側の腕の値は捨てられる。
これは意図的な設計（掴まれた腕は相手の手の位置で決まる）だが、
「どちらが最終値を持つか」が状態（`Grapple.IsGrappled`）で切り替わるため、片方だけ直すと腕が飛ぶ。

#### 順序依存の具体例（【要検証】）
- `CharacterAnimator.LateUpdate` は `ApplyPose` の**後**に `PushPulse` を上体へ足す（`Rig/CharacterAnimator.LateUpdate.cs:321-331`）。
  `PinHoldPose`（21000）はその後に走るため、**引き渡しの押し出し中は `PinHoldPose` の腕の解が押し出し姿勢を上書きする**。
- `EnemyFigure`（11500）が書いた敵の腕を `PinHoldPose`（21000）が IK で動かす。`EnemyFigure` の `LungeOffset` は
  `EnemyBrain.AbsorbLungeOffset`（`EnemyBrain.Grapple.cs:209-220`）が `Update` で吸収するので、フレーム内で位置がずれない。

### 5.3 同じボーンを書く組み合わせ（衝突しうる箇所）

| ボーン | 書き手 A | 書き手 B | 結果 |
| --- | --- | --- | --- |
| プレイヤー上腕・前腕 | `CharacterAnimator`（20000） | `PinHoldPose` / `BackHoldPose` / `OverheadHoldPose`（21000） | B が勝つ（意図） |
| プレイヤー両腕（壁） | `CharacterAnimator.ApplyWallBrace` は腕を書かない | `PinHoldPose.Solve` | B のみ。壁では `Solve` を呼ばない分岐あり（`PinHoldPose.cs:165-168`） |
| 敵の上腕 | `EnemyFigure`（11500） | `PinHoldPose` / `WallAssistPose`（21000） | B が勝つ |
| 敵の膝 | `EnemyFigure` | `PinHoldPose.AlignPinKnee`（`:144`） | B が勝つ |
| プレイヤーの腰・脚 | `CharacterAnimator` | `PinHoldPose.BraceHoldUpperBody`（上体のみ） | 分担 |

---

## 6. 要修正候補（Issue 化候補）— 7 件

すべて【コード上】で確定。**修正はしていない**（この PR はドキュメントのみ）。

### A-1. `ApplyHaulDaze` が同じフレームに 2 回掛かる（確定・影響小）

- 箇所: `MioPrototype/Assets/RyonaGame/Runtime/Rig/CharacterAnimator.Pose.cs:93-101`
- 内容: 同一条件の `if` と `ApplyHaulDaze(ref pose)` が丸ごと 2 回書かれている。
- 効果: `ApplyHaulDaze` は `Vector3.Lerp(..., 0.85f)` / `Mathf.Lerp(..., 0.6f)` の**上書き型**ブレンド（`Rig/CharacterAnimator.Hauled.cs:140-150`）。
  2 回掛けると腕の重みは `1-(1-0.85)^2 = 0.9775`、上体は `0.84` になり、意図した 0.85 / 0.6 より強く効く。
- 発生条件: `_state == Grabbed` かつ（`Time.time < _haulDazeUntil` または `Grapple.HaulHandoff`）。つまり吊り上げ・引きずりの場面。
- 確認方法（未実施）: `python tools/preview.py haul` で `sheet.png` を見て、腕の垂れが強すぎないか。

### A-2. 二人がかりの Y 字が 1 フレームで消える（確定・見た目）

- 箇所: `GrappleSession.cs:326-338`（`ClearSplit` が `ResetSplitWeight()` を呼ぶ）→ `:300-304`（`SplitWeight = 0f`）。
  呼び出しは `PlayerGrapple.Escape.cs:103`（`EndWallPin`）と `PlayerGrapple.Release.cs:14`（`Release` → `EndWallSplit`）。
- 内容: `TickSplitWeight` は「入りも抜きも 0.60 秒で SmoothStep」する設計（`GrappleSession.cs:288-297` のコメント）だが、
  `ClearSplit` が位相を無視して 0 を代入するため、**脱出・解放の瞬間に Y 字の重みが 1 → 0 へ飛ぶ**。
- 発生条件: 壁拘束の二人がかり中に (a) 連打で脱出, (b) 拘束時間切れ, (c) 壁投げ捨てへ移行。
- 影響: `WallSplitWeight` を読む姿勢（`PinHoldPose`）とカメラ（`WallSplitCamera` 経由の `hold.WallSplitWeight > 0.01f` 分岐）が
  同時に通常へ戻るため、画が 1 コマで切り替わる可能性がある【要検証】。
- 参考: `AGENTS.md` の「動きの不自然さは数字で拾う」に従うなら、`motion_audit.py` で脱出フレームの差分が跳ねるはず（未実施）。

### A-3. 膝蹴りだけ死亡ガードが無い（確定・影響小）

- 箇所: `EnemyBrain.Grapple.cs:617-621`（`DoPinKnee`）。
- 内容: `DoBearHugSqueeze` は `if (Player.IsDead) return;`（`:590-593`）、`DoWallSplitPunch` も `Player.IsDead` を見る（`:782-783`）が、
  `DoPinKnee` は見ていない。
- 発生条件: 押さえつけの膝で HP が 0 になった同じフレームに、次の膝のタイマーが来た場合。
- 影響: `DamageResolver` に死亡ガードが無い（`RyonaCore/Runtime/DamageResolver.cs:26-33`）ため、
  `Player.ReceiveHit` が `HitReceived` を発火し、`CameraDirector` の被弾演出・`HitFlash`・SFX・
  `LastHitZone` の更新がもう 1 回走る。HP は 0 のまま（`CombatState.ApplyHpDamage` がクランプ）。
  `PlayerGrapple.Update` の死亡検出（`PlayerGrapple.cs:643-647`）で拘束自体は解放されるため、窓は 1 フレーム。

### A-4. `HandOff(shove:false)` で新しい Captor の `IsHolding` が立たない（確定・現状未使用）

- 箇所: `PlayerGrapple.HandOff.cs:13-51`。`shove == false` の分岐（`:46-50`）は `_session.Captor = next`（`:25`）/ `SnapToCaptor` /
  `ApplyHandOffHold` だけを行い、`next.TakeOverHold(...)` を呼ばない。
- 内容: 押し出し経路（`:255`）と `ReceiveFromPin`（`:104`）は `TakeOverHold` を呼ぶので `next.IsHolding = true` / `Phase = Recover` になるが、
  `shove:false` では次の敵が `Chase` のまま `Captor` だけを持つ。
- 発生条件: 現状の呼び出しは `Feel/GrappleHandOff.cs:174` と `Feel/PinballDirector.cs:365` の **`shove:true` のみ**。
  デフォルト引数（`shove = false`）を使う呼び出しは無い。テスト・デバッグから呼ぶと再現する。
- 影響: 新 Captor が通常 AI を続け、掴んだまま別の攻撃を始めうる（`TryBeginAttack` は `_grapple.IsGrappled` を見ない）。

### A-5. `HoldFocus` が Push の優先順位規則を迂回する（確定・同時発生時）

- 箇所: `ThirdPersonCamera.cs:712-721`。
- 内容: `PushDistance` は「寄りは小さい方 / 引きは大きい方 / 時間は長い方」で合成する（`:620-638`）が、
  `HoldFocus` は `_pushScale` / `_pushRemaining` / `_pushDuration` を直接代入し、`_pullScale = 1f` / `_pullRemaining = 0f` に**消す**。
- 発生条件: `HoldFocus` を使う演出（`PinballDirector.FrameCamera`、`HeldBellyPunchDirector.HoldFraming`、
  `PinHoldPose`（レーン押さえつけの `HoldFocus`））と、`PushDistance` を押す演出（`CameraDirector` の死亡・被弾、
  `HeldBellyPunchDirector.RefreshCamera`）が同じフレームに走る場合。
- 影響: 最終値は「最後に書いた方」になる。とくに `CameraDirector.ApplyDeathFraming`（`Feel/CameraDirector.cs:239-256`）は
  毎フレーム `PushDistance(1.25, hold)` を押し直すが、`HoldFocus` が同フレームに居ると `_pullScale` が 1 に戻され、
  **死亡の引き・見下ろしが効かない**。逆順なら効く。フレーム順に依存する。

### A-6. `HoldsBellyPunchGuard` が `Die` / `Revive` で落ちない（確定・見た目）

- 箇所: 設定は `EnemyBrain.cs:330`、`Feel/HeldBellyPunchDirector.cs:342-352`。落とすのは
  `HeldBellyPunchDirector.SetPunchGuard(null)`（`HeldBellyPunchDirector.cs:340`, `HeldBellyPunchDirector.cs:450`）と `EnemyBrain.Lifecycle.cs:172`（`ResetForMotionPreview`）だけ。
- 内容: `Die()`（`:86-118`）と `Revive()`（`:118-149`）は `HoldsBellyPunchGuard` を触らない。
- 発生条件: 腹パン役が `HoldsBellyPunchGuard = true` のまま死亡（例: トドメの巻き添え）→ 再出現（`Revive`）。
- 影響: `AI/EnemyFigure.Step.cs:593` が `Brain.HoldsBellyPunchGuard` を見て腕の形を作るため、
  死体・再出現後に「腹パンの腕」が残る可能性がある【要検証】。

### A-7. `PinballDirector.StartPinball` が `Begin()` の成否を見ない（確定・低）

- 箇所: `Feel/PinballDirector.cs:285-300`。
- 内容: `_grapple.SetPinball(true)`（もがき抑制 10 秒）→ `_grapple.Begin(rear, Back)` の順に呼ぶが、
  `Begin` は既に掴まれていると**何もせず return** する（`PlayerGrapple.Hold.cs:20-21`）。
  戻り値が無いので失敗を検知できない。
- 発生条件: `ForceStart()`（`:55-59`）は `IsOtherSequenceBusy` を通さずに `StartPinball` を呼ぶ。
  デバッグドライバから拘束中に叩くと、拘束されていないのに `_active == true` のピンポンが残る。
- 影響: `_round == 0` の `_openingDeadline`（2.5 秒）で `_grapple.Release()` + `Cancel()` に落ちるため**復帰はする**（`PinballDirector.cs:108-116`）。
  通常経路（`Update` の抽選）は `IsOtherSequenceBusy` が `IsGrappled` を弾くので到達しない。

---

## 7. 問題なしと判断した項目 — 12 件

| # | 項目 | 根拠（【コード上】） |
| --- | --- | --- |
| 1 | 死亡後に AI が動かない | `Update` の `Phase == Dead` 早期 return（`EnemyBrain.cs:990`）と `enabled = false`（`EnemyBrain.Lifecycle.cs:99`）の二重。`LateUpdate` も止まる |
| 2 | 死亡でトークン・グループ・コライダーが片付く | `EnemyBrain.Lifecycle.cs:97-108`（`ReleaseToken` / `UnregisterGroup` / `Collider.enabled = false`） |
| 3 | 死体が障害物にならない | 同上。`Revive` でコライダーを戻す（`:143-145`） |
| 4 | 予備動作・判定・怯み中は旋回しない | `CanTurn()` が `Windup` / `Attack` / `Stagger` を除外（`EnemyBrain.cs:1333-1338`） |
| 5 | 攻撃トークンの返却漏れが無い | §2.3 の 11 経路。とくに `TryReserveTagPunchToken` の先取りは `LockForHandOff` の `Recover` タイマーが必ず `EndRecover` に落とす（`EnemyBrain.cs:588-603`） |
| 6 | Captor 自身の再攻撃が無い | 掴み成立で `Phase = Recover`（`EnemyBrain.Attack.cs:130`）＋ `TryStartTagPunch` が `Captor == this` を弾く（`EnemyBrain.Grapple.cs:94`） |
| 7 | 敵の死亡・破棄で拘束が解放される | `PlayerGrapple.cs:636-641`（`Captor == null \|\| !IsAlive` → `Release()`）。Unity の擬似 null も `== null` で拾う |
| 8 | プレイヤー死亡で拘束が解放される | `PlayerGrapple.cs:643-647` |
| 9 | ダウン割り込みで予備動作が中断される | `Strike` 冒頭（`EnemyBrain.Attack.cs:47-52`）と `Update` の `playerDown` 分岐（`EnemyBrain.cs:1095-1108`）。硬直を挟まない `CancelWindup` を使う |
| 10 | 壁拘束の段階進行が必ず後始末を通る | `WallSequenceRoutine` の `try/finally`（`.Wall.cs:367-378`）＋ `StopWallSequence`（`:339-355`）＋ `FinishWallSequence` が `WallSequenceActive` を落とす（`:357-366`） |
| 11 | 固定ショットの解除漏れが無い | §3.7。`RequestCinematicShot` はフレーム単位で、呼ばれなければ 0.35 秒で退出（`ThirdPersonCamera.cs:398-420`）。`OnDisable` でも全リセット（`:736-748`） |
| 12 | カメラの Target 破棄が安全 | `LateUpdate` 先頭の null ガード（`:268`）、`TickDown` / `LogFaceVisibility` の null チェック（`:572-580`, `:374-380`） |

補足（問題なしに近いが非対称なもの・留意）:
- `Escape` は `Release` と違って `KneeCount` / `PullPulse` / `LiftPulse` / 腹ダメージを初期化しない（`PlayerGrapple.Escape.cs:9-33` vs `.Release.cs:12-43`）。
  見た目は `Update` の減衰で消える（`PlayerGrapple.cs:602-630`）が、腹ダメージの蓄積は残る（§8-1）。
- `HoldFire` は `Chase` / `Strafe` のときだけトークンを返す（`EnemyBrain.cs:752-753`）。

---

## 8. 未確認のまま残った項目 — 8 件

実行時検証（撮影・テスト）をしないと白黒つかないもの。**推測で断定しない**ため、ここへ分離する。

| # | 項目 | 何が未確認か | 確認手段（未実施） |
| --- | --- | --- | --- |
| 1 | `Escape` 後も腹ダメージが残る | 脱出で蓄積を消さない（`ResolveHeldBellyDown` は `Fail` からのみ）。2 回目の拘束で即ダウンするのが意図か | `HeldBellyPunchTests` / 手動で「掴まれる → 腹パン 3 発 → 脱出 → 再度掴まれる」 |
| 2 | 起き上がり無敵中の腹パンが加算されない | `CanTakeFlinch` が false の間は `_heldBellyDamage` を足さない（`PlayerStats.cs:379-386`）。上限到達が遅れる | ダウン → 起き上がり直後に掴ませる。`[Grapple] begin` と `HeldBellyDamage` のログ |
| 3 | 二体がかり中、拘束役の `Recover` タイマーが止まる | `Update` が `UpdateWallSplitCaptor` で return するため `_timer` が減らない（`EnemyBrain.cs:1049-1053`）。解除後に硬直が残らないか | `preview.py wallsplit` + `[Pin]` ログの時刻 |
| 4 | 固定ショットと寄り引きの同時使用 | 固定ショットは `LateUpdate` 最後に位置を丸ごと上書き（`ThirdPersonCamera.cs:432-457`）。同時に `PushDistance` が効いている場合の画 | `preview.py belly --audit` と `-facelog` |
| 5 | プレイヤー死亡直後のピンポンのカメラ保持 | `PinballDirector.Update` の `_active` 分岐は `_stats.IsDead` を見ない（`Feel/PinballDirector.cs:102-138`）。`Cancel()` までは `HoldFocus(0.46)` が続く | `PinballRuntimeTests` の死亡ケース、または手動 |
| 6 | 敵の無効化（死亡以外）で拘束が残る | `PlayerGrapple.cs:636` は `IsAlive` しか見ない。`SetActive(false)` を外から行う経路が無いため現状は未到達 | 検証コードで `brain.enabled = false` を強制 |
| 7 | 押し出し失敗時に送り手が 1.3 秒硬直したまま | `LockForHandOff(1.30f)` は `Release` でも解除されず、`_timer` 満了で `EndRecover`（`EnemyBrain.cs:588-603` / `EnemyBrain.Grapple.cs:222-231`）。意図した「一拍」か | `preview.py haul` 系の引き渡しシーン |
| 8 | A-2（Y 字の即時消灯）が画として跳ねるか | コード上は `SplitWeight` が 1 フレームで 1→0。`motion_audit` で「飛び」として出るかは未測定 | `python tools/preview.py wallsplit --audit` → `motion_audit.py --compare` |

---

## 9. 実行時検証に回すときの最小手順（提案・未実施）

`AGENTS.md` の「変更したら撮って確かめる」に合わせる場合の順序。**この PR では実行していない。**

1. `python tools/preview.py --list` で場面名を確認（`wallpin` / `wallsplit` / `frontpin` / `belly` / `face` / `haul`）。
2. A-1: `python tools/preview.py haul` → `sheet.png` で腕の垂れ。
3. A-2: `python tools/preview.py wallsplit --audit` → `motion_audit.py --compare` で脱出フレームの差分。
4. A-5: `preview.py belly` と `wallpin` を同時に走らせた画（死亡演出との競合は手動）。
5. 既存テスト（コミット直前のみ）:
   - `grapple`: `GrappleSessionTests` / `PinGrappleTests` / `BearHugTests` / `WallPinRuntimeTests` / `HeldBellyPunchTests` / `WallGangTempoTests`
   - `misc`: `CameraExitBlendTests` / `PinballRuntimeTests` / `DownRuntimeTests`
   - `motion`: `HaulUpRuntimeTests` / `OverheadRuntimeTests`

---

## 10. 付録: 監査で読んだファイル

| 領域 | ファイル |
| --- | --- |
| EnemyBrain | `EnemyBrain.cs`（1343 行）/ `.Attack` / `.AttackSelection` / `.Grapple` / `.Group` / `.Lifecycle` / `.Locomotion` / `.Presentation` / `.Spacing` / `AI/EnemyGroup.cs` |
| PlayerGrapple | `PlayerGrapple.cs` / `.BearHug` / `.Escape` / `.HandOff` / `.Hold` / `.Release` / `.Wall` / `GrappleSession.cs` / `PlayerStats.cs` / `InputReader.cs` |
| カメラ | `ThirdPersonCamera.cs` / `Feel/CameraDirector.cs` / `Feel/WallSplitCamera.cs` / `Feel/GroundMountAttack.cs` / `Feel/HeldBellyPunchDirector*.cs` / `Feel/PinballDirector.cs` / `AI/PinHoldPose.cs` |
| 姿勢 | `Rig/CharacterAnimator*.cs`（14 ファイル）/ `AI/EnemyFigure*.cs` |
| 定数 | `RyonaCore/Runtime/CombatConstants.cs` / `RyonaCore/Runtime/DamageResolver.cs` |
