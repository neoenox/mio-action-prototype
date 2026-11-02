# 後ろ拘束中の腹パン演出 Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 後ろ拘束中に別の敵の腹パンチが当たったら、「く」の字の被弾姿勢と斜め前カメラで**5連続**の腹パンを再生し、終了後に拘束を解放する。

**Architecture:** `HeldBellyPunchDirector`（Feel 層）が拘束開始・腹パンの指示・コンボ・解放を調停する。`PlayerGrapple` は後ろ拘束の時間切れを廃止してもがき抑制APIを持ち、`CharacterAnimator` は Grabbed 状態の上に被弾オーバーレイを重ね、`EnemyBrain` はタッグ技用の腹パンチを開始する。カメラは既存の `PushDistance/PushPivot` に、プレイヤー中央へ寄せる `PushFocus`（ヨー・ピッチ付き）を足す。

**Tech Stack:** Unity 6000.4.5f1 / C# / NUnit PlayMode tests / Unity batchmode

---

## 実装結果（2026-11-02）

- 後ろ拘束は時間切れなし。`PlayerGrapple.HasTimeLimit` は Pin のみ true
- 拘束開始 1.6 秒後、拘束者以外の敵を斜め前（前方 0.98m・側方 0.68m）へ置き、Body 狙いのジャブ
- 最初の一発が当たると **5 発コンボ**
  - 間隔 0.45 秒
  - 1 発ごとに 0.55 秒の「く」の字（`torsoPitch 50°`、`headPitch −40°`、顔はカメラ側、腰を深く落とす）
  - 1 発ごとにヒットストップ 0.09 秒、腹ヒットは `hurt_ku` の声
  - コンボ中はもがき入力停止
  - カメラは距離 0.36 倍（約1.9m）、`PushFocus` ヨー −25°（左向きは +25°）・ピッチ −16°
- 5 発後に `PlayerGrapple.Release()`。他の敵は 0.6 秒待機
- HUD は「腹パンまで X.Xs」→ コンボ中「腹パン！ n/5」
- デバッグ: `-grappletest -nofeint -heldpunchshot=<png>`
- テスト: `HeldBellyPunchTests` 6件、既存の投げテストを「時間切れなし」に更新

## 検証

- EditMode 19/19
- PlayMode 44/44
- Windows ビルド 109MB / errors=0
- 実機: `[HeldPunch] combo start 1/5` → `2/5` → `3/5` → `4/5` → `5/5 → release`、例外0
- スクリーンショットで腹と顔が同じ画面に入ることを確認

## 主なファイル

- `MioPrototype/Assets/RyonaGame/Runtime/PlayerGrapple.cs`
- `MioPrototype/Assets/RyonaGame/Runtime/Rig/CharacterAnimator.cs`
- `MioPrototype/Assets/RyonaGame/Runtime/EnemyBrain.cs`
- `MioPrototype/Assets/RyonaGame/Runtime/Feel/HeldBellyPunchDirector.cs`（新規）
- `MioPrototype/Assets/RyonaGame/Runtime/ThirdPersonCamera.cs`
- `MioPrototype/Assets/RyonaGame/Runtime/MatchHud.cs`
- `MioPrototype/Assets/RyonaGame/Runtime/Debug/GrappleTestDriver.cs`
- `MioPrototype/Assets/RyonaGame/Tests/PlayMode/HeldBellyPunchTests.cs`（新規）
- `MioPrototype/Assets/RyonaGame/Tests/PlayMode/EnemyAiRuntimeTests.cs`
- `MioPrototype/Assets/RyonaGame/Tests/PlayMode/PlayabilityRegressionTests.cs`
- `SPEC.md` / `README.md` / `Diagnostics.md` / `Motion-Notes.md`
