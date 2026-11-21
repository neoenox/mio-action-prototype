# mio-action-prototype ソースレビュー（照合版）

- 記録日: 2026-11-21
- 対象コミット: `4eb3e06`（main / 作業ツリークリーン）
- 元資料: 利用者が提示した「全ソースコードレビュー総括サマリー」および「全ソースレビュー」UTF-8 テキスト
- 照合方法: リポジトリの実ファイルを静的に確認。Unity のコンパイル・PlayMode・プレイヤービルド・画面確認は未実施。
- 方針: 元レビューの指摘を実コードと突き合わせ、**事実・誤記・未確認の仮説を分離**する。

---

## 1. 総合評価

プロトタイプ基盤としての設計品質と検証自動化は良好である。

- `RyonaCore`（純粋 C# ロジック）と `RyonaGame`（Unity 実行層）が分離され、EditMode / PlayMode のテストが安全網として機能している。
- `RyonaConfig.asset`（ScriptableObject）を真実源とし、コンポーネント側の調整値は `[System.NonSerialized]` でシーンに保存しない設計。
- `preview.py` / `motion_audit.py` / `run_tests.py` など、撮影・画像差分・テスト実行の自動化が整備されている。

一方で、拘束アクションとカメラ演出を中心に状態遷移が多数のクラスへ分散しており、**異常終了・割り込み経路の監査**が今後の主要リスクである。ビルド運用については、失敗時に旧成果物を起動する経路を確認し、今回修正した（後述）。

---

## 2. 今回の修正（P1 / 確定バグ）

### ビルド失敗後に古い EXE を起動する

- 対象: `MioPrototype/tools/BuildAndPlay.cmd`
- 修正前: バッチビルドがエラーを返しても 35 行目で `echo` するだけであり、37 行目の `:launch` に落下していた。以前成功した `Build/MioPrototype.exe` が残っている場合、古いゲームが起動する。
- 修正後:

```bat
"%UNITY%" -batchmode -nographics -quit -projectPath "%PROJECT%" -executeMethod Ryona.EditorTools.PlayerBuild.BuildWindowsFromCli -logFile "%PROJECT%\Logs\batchbuild.log"
if errorlevel 1 (
  echo [build] build failed - see Logs\batchbuild.log
  popd
  exit /b 1
)

:launch
```

- 補足: `PlayerBuild.BuildWindowsFromCli()` はビルド失敗時にスタンプを更新せず、再判定した `IsStale()` が真なら `EditorApplication.Exit(1)` を返す。したがって `errorlevel 1` による失敗検知が機能する。
- 補足: Unity Editor が起動中の場合は「バッチビルドせず現行ビルドを起動する」のがこの CMD の既定動作であり、今回の修正対象ではない。
- 検証: CRLF を維持したまま差分を確認し、`cmd.exe` で同一構造の分岐が `fail-branch` → 終了コード 1（`:launch` に到達しない）ことを確認した。Unity 自体は起動していない。

---

## 3. 元レビューを修正すべき項目（現コードと矛盾）

| 元の主張 | 照合結果 | 対応 |
| --- | --- | --- |
| 約 109 MB の `.exe` が Git にコミットされている（Critical） | `.gitignore` に `MioPrototype/Build/` があり、`git ls-files` に `.exe` / `Build/` 配下は 0 件。`git log --all -- MioPrototype/Build` も履歴なし。ローカルには 111 MB の未追跡成果物があるのみ | **Critical を削除**。Releases 移行は方針として任意 |
| `LaunchOptions.cs` を `TryParse` 化すべき | `TryFloat()` / `TryInt()` が `float.TryParse` / `int.TryParse`（InvariantCulture）で既に実装済み | 削除 |
| `BodyOverlap.cs` の完全一致時にゼロ除算（NaN） | `SegmentSegmentDistance()` に `Eps = 1e-8f` があり、線分の縮退・`len2Sq`・`denom` をすべてガード済み | 削除 |
| 0.15 秒の攻撃暴発マスクを追加 | `PlayerCombat.InputBufferSeconds = 0.15f` は「硬直中の先行入力」であり、拘束解除直後の暴発マスクではない | 「解除直後の入力受付遅延を**新設**」と書き換え |
| `AutoPlayerBuild.cs` に Play モード終了トリガーを追加 | `isPlayingOrWillChangePlaymode` でビルドを延期し 30 秒後に再スケジュール、`isCompiling / isUpdating` も待機済み | 「エディタ非アクティブ時の判定のみ残る」と修正 |
| LateUpdate 内の `Renderer.materials` 複製 | Runtime 全体で `.materials` の使用は 0 件。`new Vector3` 等は構造体で GC 対象外 | 「GC Alloc の実測を行い、必要なら最適化」に格下げ |

---

## 4. 優先度別アクション一覧（再設定後）

### P1 — 完了

- [x] `BuildAndPlay.cmd`: ビルド失敗時に `exit /b 1` で終了し、旧 EXE を起動しないようにした。

### P2 — 成功判定とリソース管理

- [ ] **ビルド成功判定の統一** — `MioPrototype/tools/preview.py` の `build_player()` は Unity の戻り値 `code` を捨て、ログ文字列（`build result=Succeeded` / `up to date`）で成否を判定している。失敗時は `return 1` で撮影へ進まないため P1 ほど深刻ではないが、終了コードを採用して `BuildAndPlay.cmd` と判定基準を揃える。
- [ ] **VRM の破棄処理の実機確認** — `EnemyVrm.cs` / `VrmCharacterSlot.cs` は `Destroy(vrm.gameObject)` のみで `RuntimeGltfInstance.Dispose()` / `Resources.UnloadUnusedAssets()` を呼んでいない。コード上は未呼び出しで確定だが、実際の VRAM リークは Unity 実行環境で未確認。差し替え・シーン再読込を繰り返して計測してから実装を判断する。

### P2 — 状態遷移の継続監査（第 2 段階）

実行時検証に進む前に、ファイル・行番号・発生条件まで追う。

- `EnemyBrain`（`EnemyBrain.cs` + `.Attack` / `.AttackSelection` / `.Grapple` / `.Group` / `.Lifecycle` / `.Locomotion` / `.Presentation` / `.Spacing`）: `EnemyPhase` の変更経路、Attack → Recover → Chase の遷移条件、拘束中の行動禁止、死亡後の停止、攻撃トークンの取得／返却、リセット。
- `PlayerGrapple`（`.Wall` / `.BearHug` / `.HandOff` / `.Escape` / `.Hold` / `.Release`）: 所有権の開始・返却、敵の無効化、ダウン割り込み、連続拘束、腹ダメージ上限、コルーチンの終了条件、カメラ要求の解除、入力の消費とリセット。
- カメラ（`ThirdPersonCamera` / `CameraDirector` / `WallSplitCamera` / `GroundMountAttack` / `HeldBellyPunchDirector.Camera` / `PinballDirector`）: Push の優先順位、演出の同時開始、終了時の復帰、シーン切替時の残留、ターゲット破棄。
- `CharacterAnimator`（多数の partial ファイル）: 姿勢パラメータの所有者と更新順。

partial 分割自体はファイル整理として妥当である。状態の所有者と変更入口を限定する方針を維持し、過度な抽象化は避ける。

### P3 — 開発体験・安定性

- [ ] 拘束解除直後の入力受付遅延（暴発防止）を新設する。既存の `InputBufferSeconds` とは目的が異なるため別設定にする。
- [ ] PlayMode テストの決定論化: `WaitForSeconds` が 57 箇所あり、`WaitUntil` / フレーム待機は 0 件。物理を伴う箇所を除き、フレーム待機へ寄せる。
- [ ] `run_tests.py` の単体テストを拡充: XML 破損・欠落、Skipped のみ、タイムアウト、`parse_results()` の異常入力、`--only` の不正値、複数バッチ途中失敗。現状は 3 件で、CI はこの 3 件のみを Python 3.11 / 3.12 で実行している。
- [ ] エディタが非アクティブな間の自動ビルド抑止（Play モード・コンパイル中は既に対応済み）。

---

## 5. 照合済みの事実

| 項目 | 内容 |
| --- | --- |
| C# ソース | 197 ファイル / 2,685,742 bytes |
| テスト属性 | `[Test]` 5 + `[UnityTest]` 192 = 197 箇所（元レビューの「40 本以上」は過小） |
| 主要バッチ | `edit` / `grapple` / `motion` / `misc`。主要 PlayMode テストクラスはフィルタに収まっている |
| テストランナー | コンパイル確認、Unity 終了コード確認、0 件実行の失敗扱い、XML 解析、失敗テスト名表示、ログ保存を実装 |
| Unity | 6000.4.5f1（ProjectVersion.txt） |
| 主なパッケージ | Input System 1.19.0 / Timeline 1.8.12 / uGUI 2.0.0 / VRM・UniVRM 0.131.4 / Visual Scripting / Multiplayer Center |
| ビルドシーン | `Assets/Scenes/Prototype.unity` |
| 自動ビルド | `PlayerBuild.IsStale()` で差分判定。監視拡張子に `.wav` / `.mp3` / `.ogg` を含む |
| 顔可視性 | `FaceVisibility.Measure()` が 5 点の画面内・敵遮蔽を計算し、`RequiredRatio` は `{0.75, 0.75, 0.90, 0.75}` |
| CI | `.github/workflows/runner-unit-tests.yml` は `run_tests.py` / `test_run_tests.py` 変更時にのみ起動 |

---

## 6. 検証結果と未検証の範囲

2026-11-21 に HEAD `4eb3e06` に対して `python tools/run_tests.py` を実行した（コンパイル確認込み）。

| バッチ | 結果 |
| --- | --- |
| compile | ok |
| edit | 63/63 ok |
| grapple | 100/100 ok |
| motion | 48/48 ok |
| misc | 60/61（`PlayerFaceRuntimeTests` の「おふ」口形が既知フレーク） |

失敗 1 件は AGENTS.md 記載の既知フレークであり、`--filter PlayerFaceRuntimeTests` で 3 回連続 10/10 パスを確認した。記録は `MioPrototype/Verify/test-baseline.txt` に追記済み。Unity はコンパイル・テストともに実行済みだが、プレイヤービルドと画面確認は未実施である。

未実施の項目:

1. 拘束の割り込み・解除・再入場の実行時確認
2. カメラ演出の競合と復帰の撮影確認（`python tools/preview.py --reel` と `motion_audit.py`）
3. VRM 差し替え時の VRAM 計測
4. プレイヤービルドと実機起動による確認

コードから確定できる問題は P1 の 1 件のみで、それ以外は監査項目または改善提案である。プロジェクト全体を作り直すべき問題は確認していない。

なおプロジェクトルール（AGENTS.md）により、Unity テストはコミット直前に影響バッチを回す。開発中は撮影とログで挙動を確認する。
