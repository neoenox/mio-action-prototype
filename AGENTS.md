# AGENTS.md

このリポジトリで作業する人（と AI）が前提にする、演出と検証の方針。

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
`wallpin` / `frontpin` / `belly` / `face` / `ankle`。新しい場面は `preview.py` の `SCENARIOS` に足す。

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
- 既存テストには**設計意図が書かれている**ことがある（例：顔パンの首振りは 40° 以上必要）。
  値を変えて落ちたときは、まずテストの意図を読む。
