# 全ソースレビュー（2026-11-22）

> ステータス: 履歴。現行ルールは AGENTS.md、修正済み／未対応の判別は REVIEW.md。文書一覧は docs/README.md。


## 対象と方法

- 対象: `MioPrototype/Assets/**` の C# 201 ファイル（Runtime / RyonaCore / Editor / Tests）、`MioPrototype/tools/*.py` 10 ファイル、`ProjectSettings` / CI workflow / シーン参照。
- 方法: 領域別の静的レビュー（5 並列）+ 手動での呼び出し側照合。コードからの到達性・不成立条件を確認できたものだけを載せる。
- 制約: **修正はしていない**。実行時の再現撮影・テストは未実施。既知フレーク／設計意図は `AGENTS.md` / `Motion-Notes.md` を優先する。
- 外部 AI レビュー（`google-ai-mode-source-review.md`）の指摘のうち作業ツリーで直済みの項目は再掲しない。

## 検出結果

| # | 重大度 | 領域 | 概要 |
|---|---|---|---|
| H1 | High | Config | 基底の `OnEnable`/`OnDisable` が派生クラスで隠され、設定ホットリロードが死に、購読がリークする |
| H2 | High | EnemyBrain | 顔トドメ回避の `_evadeOffset` / `_evadeLateral` が 0 に戻らず、敵が永続的に固定される |
| H3 | High | Rig | ヒットストップ中 `_blend` が進まず、被弾・ダウン姿勢が当たる前の姿勢に戻される |
| M1 | Medium | PlayerStats | 死亡中にゴッドモードを付けると `Revived` が発火せず、倒れ姿勢が残る |
| M2 | Medium | Encounter | `MaxWaves` が `ArenaCount × WavesPerArena` と同期せず、波数設定が効かない |
| M3 | Medium | HUD | `MatchHud.Enemy` が先頭の敵に固定され、他の敵の HP・予兆が表示されない |
| M4 | Medium | VRM | 敵 VRM の `Setup` 失敗時に、親付けしたモデルが破棄されず残る |
| M5 | Medium | Rig | 顔のけぞりが `Time.time` 基準で、ヒットストップ中に進まない |
| M6 | Medium | Editor | `PrototypeSceneBuilder` の RenderTexture が `Release()` のみでリーク |
| M7 | Medium | Editor | MeshDump のサブメッシュ番号 -1 が無チェックで `GetTriangles(-1)` へ渡る |
| M8 | Medium | Editor | 約 20 のメニューが VRM ロード結果を null チェックせず参照 |
| M9 | Medium | Editor | スタンス撮影が gameplay の static 姿勢パラメータを書き換えたまま戻さない |
| M10 | Medium | Editor | ほぼ全メニューが未保存シーンを確認なしで破棄する |
| M11 | Medium | Core | `AuthoredGrappler` が複数あると列挙順で役割が変わり、コメントと矛盾する |
| L1 | Low | Debug | 手首ロックの撮影コルーチンが `CanAttach` 判定より前に起動し、失敗のたびに積む |
| L2 | Low | Core | `BodyOverlap.KeyOf()` が毎フレーム文字列を生成する |
| L3 | Low | Core | `GrappleResistance.Progress` の分母が脱出判定の `Ceiling` と一致しない |
| L4 | Low | Core | `WristLockRules` が `endedAt <= 0` を未発生のセンチネルに使っている |
| C1 | High | Grapple | 壁拘束が `BeginHold` の失敗を無視し、拘束されていないのに壁状態だけが残る |
| C2 | Medium | Grapple | 二人がかりの脱出必要数が HUD だけ変わり、実際の抵抗値は据え置き |
| C3 | Medium | Grapple | 壁シーケンス開始時に、待機させたはずの二体目を解放してしまう |

## 詳細

### H1 設定購読の基底メソッドが隠されている

- `Assets/RyonaGame/Runtime/Config/ConfigurableMonoBehaviour.cs:24-38` は `protected virtual OnEnable/OnDisable` で静的イベントを購読／解除する。
- 次の派生クラスが `override` なしの同名 private メソッドを宣言しており、Unity のメッセージ送出は最派生の宣言だけを呼ぶ（基底は呼ばれない）:
  - `Feel/CameraDirector.cs:122,130`、`Feel/EnemyReaction.cs:67,74` → そもそも購読されず、Inspector での設定変更が反映されない。
  - `AI/EnemyGroup.cs:193`、`ThirdPersonCamera.cs:744` → 購読はされるが解除されず、シーン再読み込みごとに破棄済みオブジェクトが静的イベントに残る。
- 修正: 派生側を `protected override void OnEnable/OnDisable` にして先頭/末尾で `base.*()` を呼ぶ。

### H2 回避状態の解除漏れ

- `EnemyBrain.cs:629-643` の `BeginEvade` は `_evadeOffset` / `_evadeLateral` を目標値へ動かす。`EndEvade` (`:647-658`) は目標値だけを 0 にし、現在値は戻さない。
- `LateUpdate` (`:1323-1343`) は `_evadeOffset > 0.001f` の間ずっと「戻る」側の処理を実行して `return` するため、通常の z 合わせ・AI 移動が永久に上書きされる。`IsEvading` (`:721`) も永続 true になり、`PinballDirector` はこの敵を候補から外し続ける。
- `Die` / `Revive` / `ResetForMotionPreview` もこれらのフィールドをクリアしない。
- 呼び出し側: `Feel/HeldBellyPunchDirector.cs:974` で `BeginEvade`、`:1005` で `EndEvade`。
- 修正: `EndEvade` とリセット系で現在値も 0 にする（戻り完了時の `_evadeActive = false` と同じ場所でよい）。

### H3 ヒットストップ中に被弾姿勢が戻る

- `Rig/CharacterAnimator.LateUpdate.cs:17-24` は Stagger/Down のとき `dt` を `Time.unscaledDeltaTime` に差し替え、状態遷移時に `Rig.SnapshotPose(); _blend = 0f;` とする。
- しかし `Rig/CharacterAnimator.ApplyPose.cs:275-277` は `_blend` を **`Time.deltaTime`** で進める。`timeScale = 0` の間は 0 のままで、`BlendFromSnapshot(SmoothStep(0,1,0))` が毎フレームボーンをスナップショット（被弾前）へ戻す。
- コメントが「ヒットストップ中に衝撃を出す」と明言しているため、意図と逆。ダウン移行も同様。
- 修正: `ApplyPose` に調整済み `dt` を渡すか、`_stateTime` の増分を使う。

### M1 ゴッドモード復帰で `Revived` が出ない

- `PlayerStats.SetGodMode` (`:27-43`) は `EnsureHpAtLeast(1f)` のあとで `RestoreAll()` を呼ぶ。`RestoreAll` (`:399-414`) はその時点の `IsDead` を見るので false になり、`Revived` を発火しない。
- 唯一の購読者 `CharacterAnimator.Lifecycle.cs:88-92` の `OnRevived` は `_stateDuration = 0f` で倒れ姿勢を切り上げる。結果、移動はできるが姿勢がダウン中に残る。
- 修正: `SetGodMode` が `wasDead` を `RestoreAll` へ渡す、または HP を上げる前にイベントを発火する。

### M2 波数設定が同期しない

- `EncounterDirector.cs:94` の `MaxWaves = 10` は `[NonSerialized]` で、`ApplySettings` (`:104-109`) は `ArenaCount` / `WavesPerArena` だけを写す。`:324-326` は `Wave >= MaxWaves` で終了判定するため、設定を 6×2（12 波）にしても 10 波で終了、2×2（4 波）なら 5 波以降も最終アリーナで延々と湧く。
- 修正: `ApplySettings` で `MaxWaves = s.ArenaCount * s.WavesPerArena` を同期する（設定側にフィールドが無ければ追加）。

### M3 敵 HUD が先頭の敵に固定

- `Editor/PrototypeSceneBuilder.cs:287` が `hud.Enemy = firstBrain` を設定し、シーンにもその参照が保存されている (`Assets/Scenes/Prototype.unity:1704`)。リポジトリ内に他への代入は無い。
- `MatchHud.Update` (`:85-92`) はその 1 体だけを見て HP バーと「▲ 来るぞ」を出す。3 体の波で先頭を倒すと、残り 2 体がいてもバー 0・予兆なしのままになる。
- 修正: `EncounterDirector` の生存 enemies から「現在の脅威」を選ぶか、`MatchHud` 側でプレイヤーに最も近い生存敵を解決する。

### M4 敵 VRM の Setup 失敗時にモデルが残る

- `AI/EnemyVrm.cs:233-235` で `_instance = vrm.gameObject` を親付けし、`:254-259` で `_rig.Ready` が false だと `return false`。`LoadAsync` の `:136-147` は `Setup` の false / 例外をログだけにして `DisposeVrm` を呼ばない。
- 結果、壊れた VRM がプリミティブと重なって表示され続け、`_ready` は false のまま動かない。
- 修正: `Setup` が false を返した経路と catch で `DisposeVrm(vrm.gameObject)` し、`_instance` も null に戻す。

### M5 顔のけぞりがヒットストップで止まる

- `Rig/CharacterAnimator.Reaction.cs:45` が `float t = Time.time - _faceFlickAt;` を使う。`HitStop.Play()` が `timeScale = 0` にしている間は `Time.time` も進まず、`env` が 0 のままになる。
- 0.05 秒の「顔が弾かれる」演出が、停止が明けてから始まる。H3 と同じく `unscaled` 時間（または調整済み dt）で進める必要がある。

### M6〜M10 エディタツール

- **M6** `Editor/PrototypeSceneBuilder.cs:48-64`: `RenderTexture` を `Release()` するだけで `DestroyImmediate` していない。同じディレクトリの他ツールは `Release()` + `DestroyImmediate(rt)` をしている。例外時に `RenderTexture.active` / `cam.targetTexture` も戻らない。
- **M7** `Editor/CharacterPreview.MeshDump.cs:42-68, 613-639, 874-923`: `skinSlot` / `pantsSlot` が -1 のまま `mesh.GetTriangles(-1)` に渡る。`Editor/CharacterPreview.cs:309-314` には同じ検索に `if (skinSlot < 0 || pantsSlot < 0) return;` のガードがあり、抜け落ちている。
- **M8** `Editor/CharacterPreview.Hurt.cs:37-50, 178-191, 291-304` ほか多数: `Vrm10.LoadPathAsync(...).GetAwaiter().GetResult()` の戻りを null チェックせず `vrm.gameObject` に触れる。`CharacterPreview.Capture()` (`:31-46`) だけが try/catch と null チェックをしている。
- **M9** `Editor/CharacterPreview.Sheets.cs:182-185, 272-273, 350-351`: `CharacterAnimator.StanceArmLUpper` / `StanceHandRollL` などの **public static** を書き換える。これらは `CharacterAnimator.Pose.cs:193,208,351` など gameplay 側で参照され、撮影後に Play すると最後のプレビュー値が残る。
- **M10** エディタの `[MenuItem]` は `EditorSceneManager.NewScene(... Single)` を直接呼び、`SaveCurrentModifiedScenesIfUserWantsTo()` が 1 箇所も無い。未保存のシーン編集が黙って捨てられる。

### M11 / L1〜L4 補足

- **M11** `RyonaCore/Runtime/EnemyRole.cs:43-53`: コメントは「列挙順には依存しない」だが、`AuthoredGrappler` が複数だと最初に見つかった index を優先する。`EncounterDirector.cs:197,225` の `FindObjectsByType(..., None)` は順序保証がないため、実行ごとに Grappler が変わり得る。
- **L1** `PlayerWristLock.cs:88-93`: `_burstPrefix` が設定されているとき、`CanAttach` 判定の前に `StartCoroutine(BurstShots())` を呼ぶ。撮影フラグ付きでアタッチに失敗するたびにコルーチンが積む（本番ビルドでは不活性）。
- **L2** `RyonaCore/Runtime/BodyOverlap.cs:317-318`: `KeyOf()` が毎フレーム補間文字列を作る。値タプルをキーにすれば解消できる。
- **L3** `RyonaCore/Runtime/GrappleResistance.cs:49-54`: `IsEscaped` は `Ceiling(_requiredInputs)` なのに `Progress` は生値で割る。ゲージ 99.9% で逃げられない表示になる。
- **L4** `RyonaCore/Runtime/WristLockRules.cs:28-30`: `endedAt <= 0f` を未発生センチネルに使うため、時刻 0 近傍で解除するとクールダウンが消える（実影響は起動直後のみ）。

### C1 壁拘束の開始が失敗しても壁状態が残る

- `PlayerGrapple.Wall.cs:13-55` の `BeginWallPin` は `_session.IsWall = true`、壁サイト、レーン上書きを先に設定し、`BeginHold` (`:9`、void) の成否を見ない。`BeginHold` は手首ロックの 1.5 秒猶予中は何もせず戻る (`Hold.cs:17-19`)。
- その場合でも `:46-56` の `BeginRise()` / `SetWallStage(Grab)` / `PickWallAssistCandidate()` が走る。`IsGrappled` が false なので `PlayerGrapple.Update` は `:726` で早期 return し、`Release()` も呼ばれない。`IsWallPin => _session.IsWall` (`:83`) は true のまま。
- 結果: 自由に動けるのに壁のカメラ・姿勢・`HasWallFace` が効き続け、次の拘束に古い壁状態が引き継がれる。敵側 `EnemyBrain.Grapple.cs:528-544` の `TryBeginFrontGrapple` も同じく戻り値未チェック。
- 修正: `BeginHold` を bool 化するか、呼び出し前に同じガードを掛け、失敗時は壁の設定をロールバックする。

### C2 二人がかりの必要入力数が実効値と違う

- `PlayerGrapple.Wall.cs:203` は `RequiredInputs = CombatConstants.WallSplitEscapeInputs`（18）を代入するが、`_resistance` を作り直さない。`Struggle()` は `_resistance.RegisterInput()` を使い、`GrappleResistance` は生成時の必要回数を固定する (`RyonaCore/Runtime/GrappleResistance.cs:31-56`)。
- 直前の `WallPause` (`:718-721`) は 10 入力で抵抗を作る。そのまま二人がかりへ入ると、HUD は 18 と表示しながら 10 回で脱出できる。
- 修正: `TryBeginWallSplit` でも `_resistance = new GrappleResistance(Kind, RequiredInputs)` を実行する。

### C3 待機中のアシストが開始時に追い出される

- `BeginWallPin` の `:55-57` は二体目を `WallAssistCandidate` として選び、横で待たせる。
- `StartWallSequence()` (`:330-338`) は最初に `StopWallSequence()` → `FinishWallSequence()` を呼ぶ。この `:368-372` は `WallAssistCandidate != null` を見て `EndWallSplit()` を呼び、`:733-748` が候補をクリアして `ReleaseWallAssist()` する。
- 結果: 膝が終わる前に待機役が通常 AI へ戻り、`TryBeginWallSplit` が別の（遠い）敵を選び直すか、候補が居なければ二人がかり自体が消える。
- 修正: シーケンス開始時は「前回の実行中のコルーチンだけ」を止め、候補の選択状態はクリアしない（または `FinishWallSequence` に「開始前のクリーンアップ」フラグを渡す）。

## 外部レビューの照合結果

- 既に修正済み（再掲しない）: `GroundHaulUp.OnDisable`、`FinishWallSequence` の `EndWallSplit`、`ReleaseHandOffLock`＋try/finally、コルーチン参照保持、`BeginHold` ガード順序、ゼロ除算、`ThirdPersonCamera.DeathFramingActive`、Python ツール／CI の修正。
- 設計上既に対応済みで指摘が古い: VRM ロードの直列化・破棄・`timeScale` 復帰（`VrmLoadGate` / `VrmCharacterSlot.cs:120-158`）、`PlayerBuild.IsStale()` のパス区切り（`:58`）。
- 今回のレビューで新たに確認した未対応項目は上の表のとおり。

## 未確認・範囲外

- PlayMode / EditMode テスト 197 件の中身は個別に監査していない（既知フレークは `AGENTS.md` に記載済み）。
- 実機 VRAM 計測・撮影による目視確認は未実施。H3 / M5 はコード上の矛盾を指摘したもので、画での再現は次ステップ。
- `FindObjectsByType<EnemyBrain>` のレジストリ化は性能改善であり、今回の「不具合」には数えていない（列挙順依存の M11 は除く）。

## 修正状況（2026-11-22 夜）

すべての検出項目を `fix/source-review-2026-11-22` ブランチで修正済み。

- H1: 派生クラスの `OnEnable` / `OnDisable` を `protected override` 化し `base.*()` を呼ぶ（`CameraDirector` / `EnemyReaction` / `EnemyGroup` / `ThirdPersonCamera`）。
- H2: `EndEvade` と `ClearGrappleState` / `ResetForMotionPreview` で現在値も 0 に戻す。
- H3: `ApplyPose(float dt)` に `LateUpdate` の調整済み dt を渡し、ヒットストップ中もブレンドが進むようにした。ついでに `Stats.GroundMount` の null ガードを追加。
- C1: `BeginHold` を bool 化し、壁拘束・前方掴み・引き寄せの各呼び出しで成否を確認。失敗時は壁設定をロールバック。
- C2: `TryBeginWallSplit` で抵抗インスタンスを作り直し、HUD と実効値を一致。
- C3: シーケンス開始時にコルーチンとフラグだけを止め、待機中のアシスト候補を解放しない。
- M1: 死亡中のゴッドモードは HP を上げる前に `RestoreAll()` を呼び、`Revived` を発火させる。
- M2: `MaxWaves = ArenaCount * WavesPerArena` を同期。
- M3: `MatchHud` が生存敵を都度解決するようにした。
- M4: 敵 VRM の `Setup` 失敗・例外時に `DisposeVrm` を呼ぶ。
- M5: 顔のけぞりを `Time.unscaledTime` 基準に変更。
- M6〜M10: RenderTexture の try/finally 破棄、MeshDump のサブメッシュガード、エディタ VRM ロードの null チェック（25 箇所）、姿勢 static の `StanceOverrideGuard` による復元、81 箇所のメニューに未保存シーン確認を追加。
- M11: `AuthoredGrappler` が複数でも身長→名前で決め、列挙順依存を解消。
- L1: 手首ロック撮影コルーチンを `CanAttach` 成功後に起動。
- L2: `BodyOverlapTally` のキーを値タプル化（`KeyOf()` は外部 API として維持）。
- L3: `Progress` の分母を脱出判定と同じ `Ceiling` に統一。
- L4: 手首ロック未発生センチネルを -1 に変更（EditMode テストも更新）。

検証: Unity コンパイル ok、EditMode 63/63、PlayMode 4 バッチ、Python ツール unittest 29 件＋ruff、プレビュー撮影（`--reel` と関連場面）を実施。
