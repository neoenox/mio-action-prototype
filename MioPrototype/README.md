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

### 大きなファイルの分割（partial）

読み切れない大きさだった 2 クラスは、責務ごとに partial で分けてあります。
ファイル名で中身が分かるので、変更するときは該当ファイルだけを開けば足ります。

- `EnemyBrain.cs`（コア: フィールド・ライフサイクル・Update の分岐）
  - `EnemyBrain.Locomotion.cs` … 追跡・回り込み・間合いの維持
  - `EnemyBrain.Attack.cs` … 予備動作・判定・掴みかかり
  - `EnemyBrain.Grapple.cs` … 掴み・押さえつけ・ベアハッグ・壁の二体がかり
  - `EnemyBrain.AttackSelection.cs` … 攻撃の抽選とデータ表
  - `EnemyBrain.Lifecycle.cs` … 被弾・怯み・死亡・復活
  - `EnemyBrain.Group.cs` / `.Spacing.cs` / `.Presentation.cs` … 群管理・押し合い・予告表示
- `EnemyFigure.cs`（コア: フィールド・プレビュー）
  - `EnemyFigure.Step.cs` … 姿勢の組み立て（毎フレームの本体）
  - `EnemyFigure.Mount.cs` … 地面マウントの姿勢
  - `EnemyFigure.HoldPose.cs` / `.AttackPose.cs` … 拘束・攻撃の腕
  - `EnemyFigure.Build.cs` / `.Primitives.cs` … プリミティブの組み立て
  - `BackHoldPose.cs` / `PinHoldPose.cs` / `BearHugPose.cs` / `OverheadHoldPose.cs` /
    `WallAssistPose.cs` / `WallSplitArm.cs` … 拘束ごとの腕ソルバ（それぞれ独立）

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

