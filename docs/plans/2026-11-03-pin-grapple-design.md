# 押さえつけ（GrappleKind.Pin）— 設計

日付: 2026-11-03
状態: 実装済み。**ただし時間切れの扱いは当時から変更された**（2026-11-06）:
制限時間は 4.2 秒（`CombatConstants.GrappleLimitSeconds`）、時間切れは「投げ」ではなく
**追加ダメージなしの解放**（`PlayerGrapple.Fail`）。失敗時の部位ダメージ・スタミナ消費・
投げ・「振りほどけなかった」表示は現行実装には無い。以下の本文は当時の設計記録。

## 目的

仕様書 §4.3 の抵抗システムの 3 種目、**押さえつけ**を実装する。
後ろ拘束（Back）が「時間切れなし・腹パンで解放」なのに対し、押さえつけは
**3 秒の制限時間内に 12 回もがけたら逃げられる／逃げられなければ投げられる**。

「もがきを演出ではなく入力に落とす」という本作の核を、後ろ拘束とは違う圧力で作る。

## いまの状態（器だけある）

| 部分 | 状況 |
|---|---|
| `GrappleKind.Pin` | 定義済み（`GrappleResistance.cs`） |
| 必要入力 12 | `CombatConstants.MashBasePin` と `ResistanceMath` は実装済み |
| 制限時間 3.0 秒（現行 4.2 秒） | `GrappleLimitSeconds` / `PlayerGrapple.HasTimeLimit` は実装済み |
| 失敗（投げ） | `PlayerGrapple.Fail()` が実装済み。腕に部位 +30、スタミナ −50、1.5m ノックバック、他の敵 0.7 秒待機 |
| HUD | `HasTimeLimit` のとき「あと X.Xs」を出す分岐が実装済み |
| **開始する攻撃** | **無い**（敵は `TryGrab` = Back しか出さない） |
| **押さえつけの姿勢** | `CharacterAnimator` の `State.Grabbed` に仮の else 分岐（立ったまま押し返す形）があるだけ |
| **押さえる側の姿勢** | 無い |
| **カメラ** | 無い |

つまり、**入口（敵の攻撃）と見た目（両者の姿勢・カメラ）と HUD の文言**を足せば動く。

## 流れ

```
巨躯が正面 1.0m で両腕を上げる（0.45 秒の予備動作 + TellHeavy）
├─ 下がる / 殴る → 空振り（巨躯は 0.7 秒硬直）
└─ 掴まれる → 3.0 秒
      ├─ 12 回もがく → 振りほどく（巨躯が 0.7 秒よろける = 反撃の隙）
      └─ 時間切れ → 投げ（腕 +30 / スタミナ −50 / 1.5m 吹き飛び）
```

- 1 回の押さえつけは **3.0 秒**。必要入力 12 は毎秒 4 回でぎりぎり間に合う
- 腕の部位ダメージで必要入力が最大 +25%（`ResistanceMath` の既存式）
- 押さえつけ中は**他の敵は殴らない**（`HoldOthers`）。読ませる演出にする
- プレイヤーが倒れた／巨躯が倒れた／ポーズ中は即終了（`PlayerGrapple.Update` の既存処理）

## トリガー

`EnemyBrain` に `CanPin`（既定 false）を足し、巨躯だけ true にする。
巨躯の VRM が入るまでは、検証用に**3 体目の敵**へ `-pintest` で立てて確認する。

押さえつけを始める条件（すべて満たすとき）:

- `CanPin` かつ `Phase == Chase`
- プレイヤーが掴まれていない・ダウンしていない・死んでいない
- 距離 0.90〜1.30m、正面（`dot(forward, toPlayer) > 0.6`）
- 攻撃トークンを持っている（`EnemyGroup`。同時攻撃は最大 2 体のフェアネス規定）
- 前回の押さえつけから 10 秒以上（連発で「何もできない時間」にしない）
- 他の敵が押さえつけ中でない

予備動作は **0.45 秒**。両腕を上げて半歩踏み込む（`SfxId.TellHeavy`）。
この間に下がれば空振りし、巨躯は 0.7 秒硬直する（**避けられる**こと）。

## 姿勢

### 彼女（`CharacterAnimator` / `State.Grabbed` の Pin 分岐）

立ったまま両肩を下へ押される。膝が折れかけて耐え、両手で巨躯の手首を剥がそうとする。

- 入り 0.12 秒で腰が **−0.18m** 沈み、膝が曲がる（`hipsOffset.y` と足の位置で作る）
- 上体は前へ **18°**、首は上げて巨躯を見る（`headPitch −12°`、`headYaw` は相手方向）
- 両腕は胸の高さで前へ出し、巨躯の手首を掴む（既存の仮分岐の `gripUpper/gripFore` を流用）
- もがくたびに膝が伸びかけて、また押し戻される（`strain` の脈。既存の Grabbed と同じ作り）
- **腰・胸をカメラへ向けない**。上体は前傾のまま、横から見た「押し返し」にする

### 巨躯（新規 `PinHoldPose`。`BackHoldPose` と同じ LateUpdate IK）

- 両手を彼女の両肩（`ProceduralRig.LUpperArm/RUpperArm` の少し内側）へ置き、下へ押す
- 肘は外へ開く（腕が体の前で交差すると「抱きしめている」画になる）
- 彼女がもがくと肩が浮くので、手は毎フレーム肩を追う（押し返されているのが見える）
- 振りほどかれたら既存の `ForceStagger` の姿勢へ

## カメラ

- 通常のベルトカメラ（横）を維持する。**真上・真後ろから撮らない**
- 押さえつけ中は `PushDistance(0.72f)`（約 3.7m）と `PushPivot(−0.10f)` で少し寄る
- `PushFocus` は使わない（中央固定）。ヨーは 0（横から見る）
- 二人の腕と顔が同じ画面に入ることを確認する

## HUD

`MatchHud.UpdateGrapple()` の `where` と文言を分岐する。

- `where`: Pin のとき「（押さえつけ）」
- 本文: 「押さえつけられている ─ 連打！ n/12　あと X.Xs」
- 残り 1 秒を切ったら赤字（既存の `danger` と同じ）
- 失敗時は 1.5 秒だけ「振りほどけなかった」を出す（`PlayerGrapple.Failed` を購読）

## 検証

新規 PlayMode `PinGrappleTests`:

1. 押さえつけ開始で `HasTimeLimit` が true、残り 3.0 秒
2. 12 回入力で解放され、巨躯が `Stagger` になる（反撃の隙）
3. 時間切れで `Fail()`: 腕の部位 +30、スタミナ −50、ノックバック、他の敵が 0.7 秒待機
4. 正面以外・間合い外・トークン無しでは開始しない
5. 押さえつけ中、他の敵は攻撃しない
6. 腕の損傷が大きいと必要入力が増える

既存テストへの影響:

- `EnemyAiRuntimeTests` / `PlayabilityRegressionTests` の掴み系は Back 前提なので、
  `CanPin` の既定 false により影響なし（確認する）

目視:

- `-pintest`（3 体目に `CanPin`）で、開始 → もがき → 失敗の投げ を繰り返す
- `-pintest -holdmash` で振りほどき → 巨躯がよろける
- `-pinshot=<png>` で開始 0.3 秒後を撮影（`-heldpunchshot` と同じ仕組み）

## 実装フェーズ

| 段階 | 内容 | 規模 |
|---|---|---|
| 1 | 入口（`TryPin` / 攻撃選択 / `-pintest`）と HUD 文言、テスト | 1〜1.5 日 |
| 2 | 彼女と巨躯の姿勢（`PinHoldPose`）、カメラ | 1.5〜2 日 |
| 3 | 巨躯の VRM・ archetype・波への混ぜ方 | 別タスク（`Enemy-VRM-Spec.md` の発注とセット） |

段階 1 と 2 を分けるのは、**先に「3 秒 / 12 回 / 投げ」の手触りを確かめる**ため。
姿勢は仮でも、数値が理不尽なら直す。

## 影響ファイル

- `RyonaCore/Runtime/CombatConstants.cs`（コメントの「未実装」を外す）
- `RyonaCore/Runtime/GrappleResistance.cs`（コメント）
- `RyonaGame/Runtime/PlayerGrapple.cs`（Pin の向き・SnapToCaptor の距離）
- `RyonaGame/Runtime/EnemyBrain.cs`（`CanPin` / `TryPin` / 攻撃選択 / 押さえ中の停止）
- `RyonaGame/Runtime/Rig/CharacterAnimator.cs`（Pin の姿勢）
- `RyonaGame/Runtime/AI/EnemyFigure.cs`（`PinHoldPose`）
- `RyonaGame/Runtime/MatchHud.cs`（文言・失敗表示）
- `RyonaGame/Runtime/ThirdPersonCamera.cs`（呼ぶだけ。API 追加は不要）
- `RyonaGame/Runtime/Debug/PinTestDriver.cs`（新規）
- `RyonaGame/Tests/PlayMode/PinGrappleTests.cs`（新規）
- `SPEC.md` / `README.md` / `Diagnostics.md`

## 未決事項

- 巨躯の見た目を先に用意するか、既存の敵で Pin だけ先に入れるか
  → **既存の敵で先に入れる**（数値と姿勢を先に固める）
- 失敗時の部位は腕か胴か → **腕**（肩を押さえつけられて腕を捻るため）
- ガードで防げるか → **防げない**（掴みはガード不能。代わりに予備動作 0.45 秒で下がれる）
- 波に巨躯を混ぜる割合 → 段階 3 で決める（例: 4 波目以降に 1 体）

## 実装メモ（2026-11-05）

設計から変えた点・実測で分かったこと。

| 項目 | 設計 | 実装 |
|---|---|---|
| 入口の敵 | 巨躯（`CanPin`） | 巨躯の VRM がまだ無いので、`CanPin` は既定 false のまま。検証は `-grappletest -pintest` で正面から指示する |
| 予備動作 | 0.45 秒 + `TellHeavy` | 既存の掴み（`AttackLibrary.GrappleAttack` の 0.55 秒 + 既存のテレグラフ）を流用 |
| カメラ | `PushDistance(0.72)` / `PushPivot(−0.10)` | `PinHoldPose.LateUpdate` から毎フレーム押し直す（0.2 秒で減衰するため） |
| 失敗表示 | `Failed` を購読して 1.5 秒「振りほどけなかった」 | `MatchHud.OnGrappleFailed`。掴みが解けたあとも 1.5 秒だけパネルを残す |

**もがきの判定を回数に変えた**: `GrappleResistance` は進捗を `1/必要数` の累積で作っていたため、
必要数 12 でも浮動小数の誤差で `0.9999…` になり **13 回目まで逃げられなかった**。
`InputCount >= Ceil(必要数)` で判定するようにして、12 回ちょうどで逃げられるようにした
（`PinGrappleTests.もがき12回で振りほどけ押さえた側がよろける` が検出）。

**確認したこと**（PlayMode 58/58 通過、ビルド 110MB）:
- HUD は「掴まれている（押さえつけ）─ 連打！ 0/12 あと 2.7s」
- 3.0 秒放置で投げ（`[Grapple] fail inputs=0 ... staminaCost=50 partDamage=30`）
- `-holdmash` で 12 回もがくと 1.29 秒で逃げ、押さえた側が 0.70 秒よろける
- 押さえつけ中は他の敵が `Windup` に入らない

### 追記（2026-11-05 その2）

- **膝蹴りは 5 発**（`PinKneeDelay 0.35` / `PinKneeInterval 0.50` / 1 発 4 ダメージ）。
  3.0 秒の制限時間に 5 発入る
- 膝は**カメラ側の脚**で入れる。奥の脚だと彼の体に隠れて見えない。腿を水平近くまで上げ
  （`legRPitch -95`）、骨盤を 0.10m ずらして膝を彼女の腹の中心線へ入れる
  （膝の関節と腹の中心の距離が実測 0.11m まで詰まる）
- 膝蹴り中は**タッグ腹パンと同じ寄りのカメラ**（距離 0.38 / ピボット +0.12 / ヨー −40 / 俯角 8）に切り替え、
  5 発のあいだ保持する（腹パンの 0.28 だと膝が画面の下に落ちるため、少し引いてピボットを下げた）
- **正面から掴まれない問題**: 10 秒クールダウン・間合い 0.90〜1.30・押さえ役が背後にいる、の 3 つが重なっていた。
  `CombatConstants` に移して 5 秒 / 0.80〜1.30 にし、準備ができた押さえ役は**彼女の正面へ回り込む**
  （`TryApproachForPin`）ようにした。50 秒の自動プレイで Pin 1 → 2 回、Back 6 → 4 回
- 間合い・クールダウン・膝蹴りの値は `CombatConstants` の定数にした。public フィールドにすると
  シーンに保存された古い既定値がコードの値を上書きする（実測で min=0.90 / cd=10 のままになっていた）

