# MioPrototype — 神楽澪 戦闘プロトタイプ

Unity 6000.4.5f1 / Built-in Render Pipeline のベルトアクション試作です。
主人公には VRM モデルを使用し、敵はプリミティブモデルまたは外部 VRM を使用します。

## 遊び方

実行版の起動方法、操作、ゲームの流れは [親フォルダーの README](../README.md) を参照してください。

## 開発

Unity でこのフォルダーを開き、`Assets/Scenes/Prototype.unity` を使用します。
Windows 実行版は Unity の `Ryona > Build Windows Player` から生成できます。

戦闘ルールは `Assets/RyonaCore/Runtime`、ゲーム内の制御と表示は
`Assets/RyonaGame/Runtime` にあります。Unity Test Runner で EditMode と
PlayMode のテストを実行できます。

### テスト

Unity Test Runner を 1 回で回すと 30〜60 分かかるため、クラス分割で
一括実行するスクリプトを用意しています（Python 3 / Unity を閉じてから実行）。

```
python tools/run_tests.py              # コンパイル確認 + 全バッチ（10〜15分）
python tools/run_tests.py --only edit  # EditMode だけ
python tools/run_tests.py --only grapple,motion   # 一部だけ
python tools/run_tests.py --list       # バッチ一覧
```

結果は `Logs/tests-<batch>-<日時>.xml` に残り、失敗時はテスト名とメッセージを表示します。

### 大きなファイルの分割（partial）

読み切れない大きさだったクラスは、責務ごとに partial で分けてあります。
ファイル名で中身が分かるので、変更するときは該当ファイルだけを開けば足ります。

- `EnemyBrain.cs`（コア: フィールド・ライフサイクル・Update の分岐）
  - `EnemyBrain.Locomotion.cs` … 追跡・回り込み・間合いの維持
  - `EnemyBrain.Attack.cs` … 予備動作・判定・掴みかかり
  - `EnemyBrain.Grapple.cs` … 掴み・押さえつけ・ベアハッグ・壁の二体がかり
  - `EnemyBrain.AttackSelection.cs` … 攻撃の抽選とデータ表
  - `EnemyBrain.Lifecycle.cs` … 被弾・怯み・死亡・復活
  - `EnemyBrain.Group.cs` / `.Spacing.cs` / `.Presentation.cs` … 群管理・押し合い・予告表示
- `EnemyFigure.cs`（コア: フィールド・プレビュー）
  - `EnemyFigure.Step.cs` … 姿勢の組み立て（毎フレームの本体）。
    `Step`（前処理）→ `BuildTargetPose`（数値を決める）→ `ApplyPoseTarget`（ボーンへ流す）の
    3 段構造で、`BuildTargetPose` はさらにフェーズ・掴み・手首の小さなメソッドに分かれている。
    姿勢パラメータは `PoseTarget` 構造体で受け渡す。
  - `EnemyFigure.Mount.cs` … 地面マウントの姿勢
  - `EnemyFigure.HoldPose.cs` / `.AttackPose.cs` … 拘束・攻撃の腕
  - `EnemyFigure.Build.cs` / `.Primitives.cs` … プリミティブの組み立て
  - `BackHoldPose.cs` / `PinHoldPose.cs` / `BearHugPose.cs` / `OverheadHoldPose.cs` /
    `WallAssistPose.cs` / `WallSplitArm.cs` … 拘束ごとの腕ソルバ（それぞれ独立）
- `PlayerGrapple.cs`（コア: 状態・入力・Update）
  - `PlayerGrapple.Wall.cs` … 壁押さえつけ（掴み→壁→叩きつけ→手首固定→投げ捨て）
  - `PlayerGrapple.Hold.cs` … 拘束開始（距離・制限時間・もがき必要数）
  - `PlayerGrapple.HandOff.cs` … 引き渡し・押し出し（Shove）・ピン受け渡し
  - `PlayerGrapple.Release.cs` … 解放・寄せ位置（SnapToCaptor / GrabSpot）
  - `PlayerGrapple.BearHug.cs` / `.Escape.cs` … ベアハッグの締め付け・振りほどき/失敗
- `CharacterAnimator.cs`（コア: フィールド・プレビュー補助）
  - `CharacterAnimator.HeldPunch.cs` … 拘束中の腹パン/顔面パンチの被弾姿勢
  - `CharacterAnimator.ArmAxes.cs` … 腕の向き（スケルトン差を吸収）
  - `CharacterAnimator.Lifecycle.cs` … Awake/Bind/イベント購読
  - `CharacterAnimator.Reaction.cs` … 被弾の向き・痛み顔・不安そうな視線
  - `CharacterAnimator.State.cs` … 状態遷移とエイム
  - `CharacterAnimator.LateUpdate.cs` … 毎フレームの姿勢適用と足音
  - `CharacterAnimator.Pose*.cs` / `.ApplyPose.cs` / `.MountRoll.cs` /
    `.OverheadArms.cs` … 姿勢の中身（別項のとおり）
- `Editor/CharacterPreview.cs`（コア: 撮影の入口・共通処理）
  - `.MeshDump.cs` / `.Stance.cs` / `.Motion.cs` / `.Hand.cs` / `.Face.cs` /
    `.Hurt.cs` / `.Enemy.cs` / `.Joints.cs` / `.Sheets.cs` / `.Measure.cs`
    … 検証用の撮影・計測コマンドを種類ごとに分割（Editor 専用）

### 拘束の状態（GrappleSession）

「誰が・どの種類で・どの段階か」は `GrappleSession` が持ちます。
カメラ・ポーズ・HUD は `PlayerGrapple.Session` を見てください。
壁拘束の段階（`WallStage`）は `GrappleSession` 側の enum です。

カメラとポーズが使う重み（壁への押さえつけ `WallRestraintWeight`、
ずり落ち `WallSlideWeight`、手首の掴み直し `WallGripWeight`、
投げ下ろし `WallTossLower`、Y 字の見た目 `SplitWeight`）も
`GrappleSession` が段階から計算します。`PlayerGrapple` はそれを中継するだけです。

### 起動引数（LaunchOptions）

`-flag=value` / `-flag value` の読み取りは `LaunchOptions` に集約しています。
新しい検証用の引数を足すときは、ここに 1 行足すだけで済みます。

