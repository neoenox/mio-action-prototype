# 確認用の道具（現在使うもの）

作業中に「見て確かめる」ための一覧です。**ビルド不要**（Editor メニューから実行）。

## Editor メニュー（`Ryona/Diagnostics/`）

| メニュー | 出る画像 | 何を見るか |
|---|---|---|
| Capture Combat Strip | `combat_strip.png` | 攻撃の軌道・ダウンの形。**light2 左 当たり(正面)** で左右の打ち分けが分かる |
| Capture Hurt Sheet | `hurt_sheet.png` | 彼女の崩れ方（正面/右/左/背中）、疲労の構え、振りほどき後の突き放し。**7 行目は正面ヒットを真横から**（のけぞりの角度） |
| Capture Hurt Tiers | `hurt_tiers.png` | 彼女の食らい **弱・中・強 × 顔・腹**（＋横から殴られた 2 行）× 4 時刻を真横から。上体と頭の角度・向き（yaw）をログ出力。顔 = 首がカメラ側へ振れ、腹 = 上体が折れて両手が腹へ |
| Capture Pain Faces | `pain_faces.png` | 苦悶の表情の**顔ヒットと腹パンの違い**。左から 顔・強 / 腹・弱 / 腹・中 / 腹・強 / 無表情。`PlayerFace.DefaultFacePain` / `DefaultBellyPain` をそのまま使うので、配合を変えたらこのシートで見る |
| Capture Stagger | `enemy_stagger.png` | 敵の食らい（のけぞり → こらえる の2段） |
| Check Enemy VRM | `enemy_vrm_front/side/quarter.png` | **敵 VRM を置いた直後の検品**。置き場所・背丈・骨17本・見た目を確認 |
| Capture Enemy Attacks | `enemy_attacks.png` | 敵の攻撃4種（突き／振り下ろし／横薙ぎ／掴み）× 3 フェーズ。列＝攻撃、行＝振りかぶり／当てる／振り抜き。肩角・肘角・両手の位置をログ出力 |
| Capture Enemy Wrist | `enemy_wrist.png` | 敵の右手首の折れ（構え／振りかぶり／当てる／振り抜き）＋折れ角ログ |
| Capture Walk Cycle | `walk_cycle.png` | 歩き 7 コマ × 2 段（上＝前進を真横から、下＝横歩きを正面から） |
| Capture Player Walk Cycle | `player_walk_cycle.png` | 彼女の歩き 7 コマ × 3 段（前進＝真横／横歩き＝正面／後退り＝真横） |
| Capture Enemy GetUp | `enemy_getup.png` | 敵の立ち上がり 7 コマ（仰向け → しゃがみ → 構え）＋腰高・倒れ角ログ |
| Capture Enemy Fall | `enemy_fall.png` | 敵が倒れる瞬間 8 コマ（立ち → 崩れ → 仰向け）＋倒れ角・頭/胸/足首の高さログ |
| Capture Attack Sheet | `attack_sheet.png` | 3段コンボの各段の姿勢（横・正面）。引き手が顎前に残るか、後ろ足のかかとが上がるか |
| Capture Grabbed | `grabbed_sheet.png` | 掴まれて もがく姿勢を正面・真横 × 5 時刻。2・4 列目はもがきの脈 1（肘が開く）。手の高さ・腰高をログ出力 |
| Capture Guard Strip | `guard_strip.png` | ガードの姿勢と押し戻され方 |
| Capture Down Sheet | `down_sheet.png` | ダウンの倒れ方と着地 |
| Scan Sfx | （波形ログ） | 効果音の長さ・ピークの確認 |

実行方法（コマンドから）:

```
"C:\Users\neoen\Unity\Editor\6000.4.5f1\Editor\Unity.exe" -batchmode -quit ^
  -projectPath "C:\gemini-desktop\ryona-action\MioPrototype" ^
  -executeMethod Ryona.EditorTools.CharacterPreview.CaptureHurtSheet ^
  -logFile "C:\Users\neoen\AppData\Local\Temp\opencode\hurt.log"
```

**存在しないメソッド名を渡すと Unity がハングします。** 上の表のメソッド名をそのまま使ってください。

`Check Enemy VRM` のメソッド名は `Ryona.EditorTools.EnemyVrmCheck.Check` です（`enemy_v1.vrm` が見つからないときは、
置くべき場所をログに出すだけで終わります）。

## 実機のフラグ（`MioPrototype.exe -xxx`）

| フラグ | 動き |
|---|---|
| `-holdlight` | 弱だけ押し続ける（3段コンボの確認） |
| `-leftpunch` | 弱を必ず左で打つ（2段目の姿勢確認） |
| `-holdattack [-attackinterval=秒]` | 強も混ぜて自動で殴る |
| `-holdright` / `-holdmove` | 右へ歩き続ける（波の進行確認） |
| `-holdmash` | もがき入力を自動で入れる |
| `-grappletest [-holdmash] [-grappleback\|-grapplefront\|-grappleside]` | 数秒ごとに最寄りの敵を彼女の**背後 1m**（既定）に立たせて掴みを指示する。後ろ拘束は時間切れで解けない。付けなければ **1.6 秒後に腹パン5連続 → くの字 → 3 秒のクールタイム → また 5 連続**を、**振りほどく（`-holdmash`）まで繰り返す**。`-grapplefront` は「正面からでは掴めない」ことの確認用（掴みは背後からのみ成立する）。掴まれている間は `[GrappleContact]` を 0.25 秒ごとに出力（敵ローカル座標で手首・胸・肩・彼女の両手） |
| `-heldpunchshot=<png>` | 腹パン演出の開始 0.12 秒後を撮影（`-grappletest` と併用） |
| `-heldpunchcamdist=` / `-heldpunchcampivot=` / `-heldpunchcampitch=` / `-heldpunchcamyaw=` | 腹パン中のカメラ値を起動時だけ差し替える（顔が入る構図の撮り比べ用）。既定は 0.34 / 0.27 / −12 / −40 |
| `-heldpunchface=<0〜1>` | タッグ技が顔面を狙う確率を差し替える（既定 0.5）。`1` で顔面のみ、`0` で腹パンのみ |
| `-nofeint` | 敵のフェイントを切る。掴みもフェイント対象（22%）なので、数秒の検証では「掴めなかった」のか「引かれた」のか区別できない。切ってから回す |
| `-encountertest [-encounterkills=N]` | 2秒ごとに敵を1体倒して決着と波の進行を回す |
| `-screenspan=N` | 波の「1画面ぶんの距離」を起動時だけ差し替える（既定 8）。7/9/11 を再ビルドなしで撮り比べられる |
| `-maxwaves=N` | 何波で STAGE CLEAR にするか（既定 10、`0` で無限＝耐久試験用） |
| `-camdist=N` | カメラ距離を差し替える（既定 5.2）。構図の撮り比べ用 |
| `-titleshot=<png>` | タイトル表示を 1 秒後に撮影し、2.2 秒後に自動開始（押せない環境での目視確認用） |
| `-pauseshot=<png>` | 2 秒後にポーズ画面を開いて撮影し、0.4 秒後に自動再開（見た目の確認用） |
| `-occlog` | 遮蔽フェードの判定値（角度・距離比・画面隔たり）を毎秒ログ |
| `-statlog` | HP/スタミナ/体勢を毎秒ログ |
| `-camlog` | カメラ追尾の実測（彼女のX / カメラ中心X / 画面内の横位置 / ロック状態 / 歩ける範囲）を 0.25 秒ごとログ |
| `-vrmlog` | プリミティブの手首と VRM の手の骨の距離を 0.25 秒ごとログ（敵 VRM の追従確認） |
| `-hudshot <png> [-hudshotdelays 6.5,7,7.5]` | 指定のゲーム内時刻で画面を保存（`_1` 連番） |
| `-attacktest [-attacktestdist=N]` | 最寄りの敵を彼女の N m 前（既定 1.15）に固定し、攻撃を封じて殴られ役にする。他の敵は 12m 以上後ろへ退避。彼女が攻撃中は置き直さない（踏み込みで詰めた距離を戻さないため）。`-holdattack` と併用 |
| `-hitlog` | 攻撃 1 フレームごとに `[Fist]`（拳の z / 高さ / アニメ経過 / 肩・肘の位置 / 打っていない手の位置）を出力。腹パン中は `[BellyHit]`（攻撃者の位置・向き、拳と手首から腹の狙いまでの距離・bias）も出力 |
| `-fistshot <png>` | **拳が前に出たフレームだけ**を 6 枚保存して終了（`_1` 連番）。`[FistShot]` に両手・両肘のローカル座標と `StateTime` を出す。`-hudshotdelays` の連写は撮影間隔が 0.15 秒空いて攻撃の位相と合わず、伸び切った画が撮れないときに使う |
| `-sfxaudition` | 効果音を1つずつ試聴（数字キーで単発再生、Spaceで自動送り停止） |

ログ: `C:\Users\neoen\AppData\LocalLow\DefaultCompany\MioPrototype\Player.log`

## 計測のコツ

- `-hudshot` の時刻は**ゲーム内時間**。起動直後のシェーダコンパイルで遅れるので、
  `Start-Sleep` は 22〜24 秒取る（短いと画像が出ない）
- 遮蔽・間合いの数値を見たいときは `-occlog -statlog` を併用

## 被弾の見た目を変えたら（毎回）

同じ穴を 3 回踏んだ（顔の上体ねじれ / 腹のねじれ / 投げの持ち越し）ので、**2 つセットで**回す。

1. `Ryona/Diagnostics/Capture Hurt Tiers` → `hurt_tiers.png` を目で見る
   （弱・中・強 × 顔・腹 ＋ 横から 2 行。ログに `[Tier]` で角度が出る）
2. PlayMode テスト `HurtPoseRuntimeTests`（4 件）を通す

## 表情の配合を変えたら（毎回）

1. `Ryona/Diagnostics/Capture Pain Faces` → `pain_faces.png` を目で見る
   （顔・強 / 腹・弱 / 腹・中 / 腹・強 / 無表情。コードの既定値をそのまま描く）
2. PlayMode テスト `PlayerFaceRuntimeTests`（3 件）を通す
   （腹パンで腹パン用の配合へ寄る／顔ヒットで戻る）
3. 実機で撮るなら `-shotpain 1 -shotbelly 1`（腹）と `-shotbelly 0`（顔）で見比べる

```
"<Unity.exe>" -batchmode -runTests -projectPath "<MioPrototype>" ^
  -testPlatform PlayMode -testResults "%TEMP%\play.xml"
```

`HurtPoseRuntimeTests` が見ているもの:

| テスト | 期待 |
|---|---|
| 顔を殴られたら首がカメラ側へ振れ上体は折れない | `headYaw` > 40°、`torsoPitch` < 32° |
| 腹を殴られたら上体が折れて首は振れない | `torsoPitch` > 50°、`headYaw` は顔の半分未満 |
| 横から殴られた腹ヒットは上体がねじれる | 正面との `torsoYaw` の差 > 4° |
| 場所が分からない被弾は腹の折れ方にならない | 腹（70°）より 15° 以上浅い |

`CharacterAnimator.PreviewHitZone` に顔 / 腹を入れると、敵を待たずに 1 フレームで姿勢を作れる
（実機では常に null。設定した値が `Stats.LastHitZone` より優先される）。

## 当たり判定の読み方（`-hitlog`）

攻撃の発生の瞬間に 1 行出る。

```
[HitGeom] heavy step=0 hit fistZ=0.68 fistY=1.14 enemyDist=0.96 fistToAxis=0.28 gapBody=-0.08
```

| 項目 | 意味 |
|---|---|
| `fistZ` | 拳の前方向の位置（彼女の root 基準）。伸び切ると light 0.59 / heavy 0.68 / break 0.65 |
| `fistToAxis` | 拳と相手の軸との**水平距離** |
| `gapBody` | `fistToAxis - 0.24(体の半径) - 0.12(拳の半径)`。**正＝届いていない、負＝体に入っている** |
| `enemyDist` | 彼女と相手の距離（`0.00` は生きている敵がいない＝空振り） |

判定は `gapBody <= 0` で当たる。**-0.08〜0 が狙い**（拳が体表面ちょうど）。
`fistZ` が 0.2 台なら「アニメが伸びる前に判定だけ出ている」＝ 状態遷移の遅れを疑う。


## 配布物の通し確認（2026-10-28 実施）

| 見るもの | 撮り方 | 結果 |
|---|---|---|
| タイトル | `-titleshot=title.png` | MIO / 操作説明 / ベストタイムが出る。黒背景で文字は読める |
| 開始 → ポーズ | `-pauseshot=pause.png` | PAUSE・音量・カメラ感度・R/Q/Esc が出る。時間は止まる |
| STAGE CLEAR | `-maxwaves=1 -encountertest -encounterkills=3 -hudshot … -hudshotdelays 7` | 「STAGE CLEAR / タイム / ベスト」と「撃破 n 全 n 波」が出る |
| ベストタイムの引き継ぎ | 上の実行を 2 回 | 2 回目のベストに 1 回目のタイムが残る（PlayerPrefs） |
| 例外 | 全実行 | `Player.log` に Exception / NullReference は 0 件 |

**起動直後の 1〜2 秒**は、彼女の VRM を読み終えるまで仮のカプセルが立っている
（タイトルが手前を覆うので実プレイでは見えない）。`-encountertest` のようにタイトルを
飛ばす検証では写る。

