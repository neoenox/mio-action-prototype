# ドキュメント一覧

このリポジトリの文書は「現行のルール」「仕様」「履歴（レビュー）」「検証素材」に分ける。
迷ったら **AGENTS.md → MioPrototype/README.md → SPEC.md** の順に読む。

## 現行のルール・運用

- [`AGENTS.md`](../AGENTS.md) — 演出・カメラ・検証・作業フローの唯一のルール。テストは CI が PR で実行し、ローカルでは回さない。
- [`MioPrototype/README.md`](../MioPrototype/README.md) — 起動方法、操作、クラス構成。
- [`docs/GRAPPLE_NOTES.md`](GRAPPLE_NOTES.md) — 拘束・ダウン・カメラの設計メモ。
- [`docs/motion-audit.md`](motion-audit.md) — フレーム間差分による不連続検出の使い方。末尾の件数は 2026-11-15 時点の履歴。

## 仕様・キャラクター

- [`SPEC.md`](../SPEC.md) — ゲーム仕様全般。
- [`CHARACTER.md`](../CHARACTER.md) / [`Enemy-VRM-Spec.md`](../Enemy-VRM-Spec.md) / [`Joint-ROM.md`](../Joint-ROM.md) — モデル・関節の制約。
- [`PIPELINE.md`](../PIPELINE.md) / [`Outfit-Fix.md`](../Outfit-Fix.md) / [`VRoid-20min.md`](../VRoid-20min.md) — アセット作成と衣装の手順。
- [`SFX-Audition.md`](../SFX-Audition.md) / [`Tempo-Check.md`](../Tempo-Check.md) — 音・テンポの調整記録。
- [`Diagnostics.md`](../Diagnostics.md) — 診断用ツールとフラグ。

## 設計・レビュー（履歴）

- [`docs/plans/`](plans/) — 機能ごとの設計・実装計画。実装後の現状はコードと AGENTS.md が優先。
- [`REVIEW.md`](../REVIEW.md) — 全ソースレビューの照合結果。**修正済み・未対応の判別はここを見る**。
- [`docs/review/`](review/) — レビューの生資料と監査記録。
  - `state-transitions.md` — 状態遷移の第 2 段階監査（2026-11-21）。
  - `self-source-review-2026-11-22.md` — 静的レビューの検出結果。
  - `google-ai-mode-source-review.md` — 外部 AI による統合レビュー。
  - `google-ai-mode-full-chunks.md` — 上記の元になったチャンク別回答（アーカイブ）。
  - 指摘の反映はコミット `0645d31` 以降。古い記述を現行仕様として読まないこと。

## 検証素材

- [`docs/verification/`](verification/) — before/after 画像・GIF・動画をコミット済み。
- `MioPrototype/Verify/` — `preview.py` / `motion_audit.py` が作る生成物（gitignore 済み）。`Verify/preview/reel/preview-reel.gif` が毎回の確認用。
- `MioPrototype/Verify/test-baseline.txt` — 全バッチ緑の記録（日付・HEAD・結果）。
