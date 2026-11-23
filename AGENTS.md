# AGENTS.md

このリポジトリで作業する人（と AI）が前提にする、演出と検証の方針。

## 作業フロー（worktree 必須・2026-11-22 の指定）

- **作業を開始するときは必ず worktree を作る**。メインの作業ディレクトリ（リポジトリルート）で直接変更しない。既存の worktree は再利用しない。
  - 例: `git worktree add ../ryona-action-<作業名> -b <作業ブランチ> origin/main` のち `cd ../ryona-action-<作業名>`。
- worktree 内で変更し、コンパイル・撮影（`preview.py` / `motion_audit.py`）・ログで確認する。ローカルでテストバッチは回さない（CI が PR で全バッチを回す）。
- コミットして push し、`gh pr create` で PR を作る。必須チェックが既知フレークで落ちている場合は一時的に外し、マージ後に必ず元へ戻す。
- 元のディレクトリへ戻り、`gh pr merge <番号>` でマージしたあと `git switch main && git pull --ff-only` で最新化する（マージ後にも必ず Pull する）。
- **main を最新化したら main でプレイヤーをビルドする**（`Ryona.EditorTools.PlayerBuild.BuildWindowsFromCli`）。worktree 内のビルドは検証用で、完了扱いにしない。
- 最後に worktree がクリーン（`git status --porcelain` が空）であることを確認して `git worktree remove <worktree のパス>` で削除し、一時ブランチも整理する。未コミット変更がある場合は削除しない。

## 応答言語

- AI は常に日本語で応答する（2026-11-19 の指定）。

## 演出の原則

### カメラは「彼女（プレイヤー）の顔が映る」ことを前提にする

- カメラワークを設計・変更するときは、**プレイヤーの顔が画面に入っていること**を既定の要件にする。
  顔が切れる・隠れる画は、演出として成立していないものとして扱う。
- 具体的に避けるもの：
  - 顔が画面外へ切れる（フレームアウト）
  - 手前の敵の体・腕で顔が覆われる
  - 前髪が顔に落ちて表情が読めない
  - HUD の帯が顔や、その場面で見せたい部位に重なる
- 顔と一緒に見せたいもの（腹・膝・拳など）がある場合、**画角・距離・高さ・注視点で両立させる**。
  片方だけを寄る変更はしない。
- 「彼女を中心にカメラを回す」と、正面に立つ敵が遮蔽物になる。その場合は
  **二人の関係軸に対して横から撮る**（`WallSplitCamera` / `PinHoldPose` の膝ショットと同じ方式）。

### 変更したら撮って確かめる

見た目の変更は、**プレイヤービルドで連番キャプチャを撮って確認する**。
数値やテストが通っても、画で読めなければ未達。

**1 コマンドで「ビルド → 撮影 → コマ送り → GIF → 比較」まで出る**（推奨）:

```bash
cd MioPrototype
python tools/preview.py frontpin                      # レーンの正面拘束
python tools/preview.py belly --no-build              # ビルドを省く
python tools/preview.py face --compare ../../preview-face2   # 変更前と横に並べる
python tools/preview.py --list                        # 撮れる場面の一覧
```

出力は `MioPrototype/Verify/preview/<場面>-<時刻>/`（`Verify/` は `.gitignore` 済み）に
`sheet.png`（0.25 秒ごとのコマ送り）・`preview.gif`・`compare.png`。場面は
`wallpin` / `wallsplit` / `frontpin` / `belly` / `bellybody` / `face` / `ankle` / `haul`。新しい場面は
`preview.py` の `SCENARIOS` に足す。

### 毎回のプレビュー（1 コマンド）

見た目に触る変更は、最後にこれを実行して **1 本の GIF** を残す:

```bash
cd MioPrototype
python tools/preview.py --reel
```

- 腹パン（体）・正面拘束の膝・髪の 3 場面を撮り、タイトル付きで
  `Verify/preview/reel/preview-reel.gif` にまとめる。
- 各場面の `sheet.png` / `preview.gif` も `Verify/preview/<場面>-<時刻>/` に残る。
- before/after の比較シートも作る場合:
  `python tools/preview_reel.py --belly-before <dir> --pin-before <dir>`。
- 「撮ったが見ていない」を防ぐため、変更を完了とする前に GIF を開いて確認する。
- **AI は `preview.gif` を画像として読み込まない**。`sheet.png`・`compare.png`・`motion_audit.py --strip` の PNG で確認する
  （アニメーション GIF を会話に入れるとプロバイダーが 400 を返し続け、作業が止まった。実例 2026-11-21）。

### 動きの不自然さは数字で拾う

目視で全コマを見る前に、**隣り合うフレームの画像差分**で「飛んでいる場所」を絞る:

```bash
python tools/motion_audit.py Verify/preview/<dir>              # 上位の不連続を出す
python tools/motion_audit.py Verify/preview/<dir> --threshold 5  # 5倍超で終了コード1
python tools/motion_audit.py --compare <before> <after>        # 良くなったか比べる
python tools/motion_audit.py Verify/preview/<dir> --strip 110 117 -o seq.png
```

- カメラの飛び・キャラのワープは平均の 3〜7 倍として出る。**打撃ごとの 3〜4 倍は
  ヒットストップ由来で正常**なので、番号を見て中身を確かめてから判断する。
- **大きな値＝不具合ではない**。`RequestCinematicShot(..., cut: true)` の意図的なカットも
  同じ値になる（例: `GroundMountAttack` のマウント開始は 9.9 倍）。
  値を見たら、そのフレームを `--strip` で開き、コードに `cut: true` が無いかを確かめる。
- `preview.py --audit` を付けると、撮影のあとに同じ検査まで走る。
- 直したら `--compare` で**下がったことを数字で示す**。下がっていなければ直っていない。

生の引数で撮るとき: `-grappletest` / `-pintest` / `-nowallpin` ＋
`-pinburst=<dir>\pin.png -pinburstframes=200 -pinburstinterval=0.05`。
比較は「変更前後を同じフレーム番号で並べる」と判断しやすい。

### 顔が映っているかを目視で判定しない

`FaceVisibility.Measure(camera, head, enemies)` が、顔の 5 点について
**画面内か／敵の体に隠れていないか**を計算し、可視率と遮蔽した敵の名前を返す
（`EnemyFigure` のプリミティブ形状に対して線分交差を取る。コライダーは無いので物理は使わない）。
`Readable` はさらに**顔の向き（`FacingLimit` 60° 以内）**も見る。

- 目視の「なんとなく隠れている」ではなく、**段階ごとの可視率の下限**をテストで固定する
  （`WallPinRuntimeTests` の `[FaceVisible]` ログと `FaceVisibility.RequiredRatio`）。
- 下げてよいのは実測があるときだけ。基準を緩めるのは「直した」ではなく「見逃した」。
- 測れないもの: 彼女自身の前髪・腕が顔にかかること、HUD の帯が重なること。

## テスト

- EditMode は `Ryona.Tests`（`Ryona.Core` のみ参照）、PlayMode は `Ryona.PlayTests`（`Ryona.Game` を参照）。
- まとめて回すときは `MioPrototype/tools/run_tests.py`（コンパイル確認込み）。
- 速回しの例: `python tools/run_tests.py --filter "BearHugTests|OverheadRuntimeTests" --skip-compile`（数十秒）。
- **テストは CI（GitHub Actions `unity-tests.yml`）が PR で実行する**（2026-11-22 の指定）。開発中・コミット前ともローカルでは回さない。見た目と挙動の確認は撮影（`preview.py`・`motion_audit.py`）とログで行う。
  下の表は CI のバッチ割り当ての目安（`edit` は常に含める）。CI は常に全バッチを回す。ローカルで確認したいときだけ `run_tests.py --only ...` を使う。
  CI は self-hosted Windows runner（`runs-on: [self-hosted, Windows]`）で `run_tests.py` の全バッチを実行する。ランナーに Unity と Python 3.11 が必要で、実行中は Unity Editor を閉じておく。
  gitignore 済みの音声素材は workflow の `VOICE_SOURCE` から作業ツリーへコピーする。ランナーを構築したら `UNITY_EXE` と `VOICE_SOURCE` を実パスに合わせる。

| 変更した場所 | 回すバッチ |
|---|---|
| `RyonaCore/**` | `edit` |
| `EnemyBrain*` / `PlayerGrapple*` / `GrappleSession` / `HeldBellyPunch*` / `Feel/**` | `grapple` |
| `CharacterAnimator*` / `Rig/**` / `AI/**`（姿勢・関節） | `motion` ＋ `grapple` |
| `Config/**` / `OcclusionFade` / `ThirdPersonCamera` / `PlayerStats` | `misc` |

- 複数領域に触れたら和集合。CI は全バッチを回すので、下表は影響範囲の目安として読む。
- 全バッチで落ちたテストが再実行で通っても即「フレーク」と決めず、`--filter` で同一テストを3回連続回す。3回とも通れば既知フレークとしてここに1行追加し、1回でも落ちれば実バグとして原因を直す。
- 既知フレーク（2026-11-21）: `VrmRuntimeTests.Vrmが読み込まれリグが組み上がる` が全バッチで1回だけ「肘が下りていない」で失敗。`--filter VrmRuntimeTests` で3回連続パス（各2/2）を確認済み。
- 既知フレーク（2026-11-21）: `WallPinRuntimeTests.壁拘束の各段階で彼女の顔が画面に映っている`（3 人がかり 74% vs 下限 75%）と `PlayerFaceRuntimeTests` の口形 3 件が全バッチで各 1 回失敗。`--filter` で 3 回連続パス（1/1×3、10/10×3）を確認済み。
- 既知フレーク（2026-11-22）: `DownRuntimeTests.ダウンは後ろへ放物線で飛ぶ` が全バッチで1回だけ「浮いていない（y=0.02）」で失敗。`--filter DownRuntimeTests.ダウンは後ろへ放物線で飛ぶ` で3回連続パス（1/1×3）を確認済み。
- CI が全緑なら、記録を `MioPrototype/Verify/test-baseline.txt`（日付・HEAD・各バッチ結果）に1行追記する。
- 既存テストには**設計意図が書かれている**ことがある（例：顔パンの首振りは 40° 以上必要）。
  値を変えて落ちたときは、まずテストの意図を読む。
