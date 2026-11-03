# 後ろ拘束中の腹パン演出 — 設計

日付: 2026-11-02
状態: 実装済み（2026-11-02）

## 目的

後ろからの羽交い締め（`GrappleKind.Back`）中に、別の敵の腹パンチが当たったとき、
「く」の字の被弾姿勢と、腹と顔が同時に見える斜め前カメラを再生する。

時間切れで自動解除しないことで、**「連打で逃げる」か「腹パンを5発食らって解放される」** の
駆け引きを作る。

## 流れ

```
後ろ拘束スタート（時間切れなし）
├─ 連打で振りほどく → 拘束者がよろけて通常へ
└─ 1.6 秒後、別の敵が腹パン
      → 最初の一発でコンボ開始（1/5）
      → 0.55 秒間隔で 5 発。1 発ごとに「く」の字をやり直す
      → コンボ中はもがき停止、斜め前カメラをキープ
      → 5 発食らったら拘束を解放
      → 他の敵は 0.6 秒待機
```

- 腹パンは 1 回の拘束につき **5 発**。外れた／中断された場合だけ 0.6 秒後に再試行する
- 他の敵がいない場合は腹パンを予約せず、連打で自分から解除するしかない
- 腹パンは通常のダメージ・スタミナ・部位ダメージ・怯みを与える（追加ダメージは足さない）
- 拘束者が倒れた／プレイヤーが倒れた／ポーズ中は、予約を取り消して即終了

## トリガー

`HeldBellyPunchDirector` が `PlayerStats.HitReceived` を監視し、以下をすべて満たすときに演出を開始する。

- `PlayerGrapple.IsGrappled` かつ `Kind == GrappleKind.Back`
- `PlayerStats.LastHitZone == PunchZone.Body`
- `HitResult.Damage > 0` かつガードされていない
- `LastAttacker != null` かつ `LastAttacker != Captor.transform`
- 攻撃者がプレイヤーの前方（`dot(player.forward, attacker - player) > 0.1`）
- 演出中ではない／クールダウン中ではない
- プレイヤーが死んでいない／`Time.timeScale > 0`

## 拘束の扱い

- 後ろ拘束に時間制限を設けない。`Fail()`（投げ）は後ろ拘束では呼ばない
- 押さえつけ（`GrappleKind.Pin`、未実装）用の時間制限コードは残す
- 演出中は `PlayerGrapple.SuppressStruggle(コンボ中)` で入力を受け付けない
- 5 発食らった後（または中断時）に `PlayerGrapple.Release()` を呼び、拘束を解く
- HUD は残り時間の代わりに「腹パンまで X.Xs」、コンボ中は「腹パン！ n/5」を出す（敵がいなければ「別の敵なし」）

## カメラ

- `ThirdPersonCamera.PushDistance(0.36f, ...)` → 5.2m × 0.36 ≒ 1.87m
- ピボットは通常の 0.86m のまま（`PushPivot` は 0）
- 画面ロック中はプレイヤーが端にいることがあるため、`PushFocus(player.x)` で中央へ寄せる
- `PushFocus` のヨー −25°で**プレイヤーの斜め前**へ回り込む。左向きのときは **+25°** に反転する
  （固定値のままだと背中側へ回り、背中アップになる）
- 俯角は `BeltPitch` から −16°。折れて下を向いた顔を画面に入れる
- 1.87m / FOV 40°で腹（約 1.0m）と顔（約 1.5m）が同じ画面に入る
- ヒットごとに効果を押し直し、5 発の間は同じ構図をキープする
- `Distance` は書き換えない。既存の被弾引き（1.10 倍 / ピボット −0.10〜−0.18）より
  寄り（0.36）が優先される
- 死亡時は演出を発動しない（死亡フレーミングを邪魔しない）

## 姿勢（CharacterAnimator）

`State.Grabbed` の上に、1 発ごとに 0.55 秒の被弾オーバーレイを重ねる。

- 入り 0.07 秒 → 保持 約 0.30 秒 → 戻り 0.18 秒
- `torsoPitch` +50°、`headPitch` −40°（上体は深く折れるが、顔は上げてカメラ側へ向ける）
- 腰を下へ 0.15m、後ろへ 0.13m（脚の IK が膝を曲げて「く」の字になる）
- 羽交い締めの腕は解かない。肘を少し落として抵抗感を出す
- 演出中は `strain` と `pulse` を 0 にして、もがきの揺れを止める
- 戻りは 0.12 秒で通常の Grabbed 姿勢へ

## 腹パンの指示

- 拘束開始から 1.6 秒後、生存していて拘束者でない最寄りの敵を選ぶ
- プレイヤーの正面 0.98m に置き（奥行きはベルト制約で彼女に合わせられる）、腹の座標へ向かせる
- `EnemyBrain.TryStartBellyPunch(allowRecover: true)` で Body 狙いのジャブを開始する
  （コンボ中は前の一発の硬直を切って次を打たせる）
- 拳は腹の 0.03m 内側まで入れる（手前で止めると「当たっていない」ように見える）
- 当たった瞬間から 0.6 秒は拳を抜かず、彼女が折れる間ずっと腹に押し込む
  （発注時から数えると予備動作の 0.35 秒を食い、折れている間に拳が引いてしまう）
- 攻撃トークンは尊重する。取れない／攻撃中／怯み中なら 0.25 秒後に再試行
- 最初の一発が当たったら 5 発までコンボ。外れた／中断されたら 0.6 秒後に再試行（最大 8 回）
- 1 発ごとにヒットストップ 0.09 秒。腹ヒットの声は `hurt_ku` を使う

## テスト

新規 PlayMode `HeldBellyPunchTests`:

1. 後ろ拘束が 3 秒を超えても自動解除されない
2. 腹パンが当たると `IsPerforming`、`IsHeldBellyPunch`、カメラ寄り（距離倍率 < 0.6、ピボット > 0.2、`IsFocusing`）になる
3. コンボ中はもがき入力が抑制され、5 発食らった後に拘束が解放される
4. 顔パンチ／拘束者自身の攻撃では演出が発動しない
5. 連打で先に振りほどいた場合、腹パンは発動しない
6. 拘束中の敵に腹パンを指示できる（`TryStartBellyPunch`）

既存テスト:

- `EnemyAiRuntimeTests.掴みの投げが終わった後に敵が攻撃を再開する` を
  「時間切れでは解けず、振りほどくと敵が攻撃を再開する」に更新
- `PlayabilityRegressionTests.OtherEnemyCanDamagePlayerDuringBackGrab` は
  腹パンで解放される新仕様に合わせて、顔ヒットを使うか、解放後の挙動を検証する形に更新

## 目視確認

- `-grappletest`（マッシュなし）: 1.6 秒後に腹パン5連続 → くの字 → 解放、を繰り返す
- `-grappletest -holdmash`: 腹パンの前に振りほどく
- `-heldpunchshot=<png>`: コンボ開始 0.12 秒後を撮影
- `Diagnostics.md` と `SPEC.md §4.3` を更新

## 影響ファイル

- `RyonaCore/Runtime/CombatConstants.cs`（コメント）
- `RyonaGame/Runtime/PlayerGrapple.cs`
- `RyonaGame/Runtime/Rig/CharacterAnimator.cs`
- `RyonaGame/Runtime/EnemyBrain.cs`
- `RyonaGame/Runtime/Feel/HeldBellyPunchDirector.cs`（新規）
- `RyonaGame/Runtime/MatchHud.cs`
- `RyonaGame/Runtime/Debug/GrappleTestDriver.cs`
- `RyonaGame/Tests/PlayMode/HeldBellyPunchTests.cs`（新規）
- `RyonaGame/Tests/PlayMode/EnemyAiRuntimeTests.cs`
- `RyonaGame/Tests/PlayMode/PlayabilityRegressionTests.cs`
- `SPEC.md` / `README.md` / `Diagnostics.md` / `Motion-Notes.md`
