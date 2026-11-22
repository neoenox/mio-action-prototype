# Google AI モードによる全ソースレビュー（2026-11-22）

- サービス: Google AI モード（https://www.google.com/ai → `search?udm=50`）
- 対象: 作業ツリーの全ソース（C# 200 ファイル / 2,746,253 bytes、Python ツール、Unity 設定、ドキュメント）
-  method: 当初は ZIP を添付したが、AI モードは **ZIP のメタデータ（ファイル一覧とサイズ）しか読めない** ため、行番号付きテキスト 26 チャンク（最大約 198 KB）に分割して順次添付し、チャンクごとにレビューを取得。全回答を再添付して統合総括を生成させた。
- 完全性: チャンク 01–22, 24–26 の **25 チャンクをレビュー済み**。チャンク 23 は `MioPrototype/Logs/addr-meta.json`（ログ）のみで除外。
- 留意: AI モードはログインなしで利用。生成結果には事実と推測が混在するため、コードと照合してから対応する。既知フレーク・設計意図は AGENTS.md / REVIEW.md を優先する。
- 生データ: `MioPrototype/Verify/source-review/`（ZIP、チャンク、各チャンク回答、統合回答）。全チャンク回答は `docs/review/google-ai-mode-full-chunks.md` に収録。

---

全 26 チャンク（回答欠番の 23 を除く全レビュー結果）の指摘事項を重複排除・統合し、重大度別に整理した最終レビュー総括です。 
コードレビュー最終統合総括（ryona-action）
1. 重大度別・去重済み指摘リスト
【Critical】致命的（クラッシュ・進行不能・データ破壊・リリース混入）
確定事実（コード単体で不整合・例外・リークが確定しているもの）
拘束・死亡割り込み時の状態・コルーチン残留
GroundHaulUp.cs:253-267 / GroundMountAttack.cs:216: GroundHaulUp に OnDisable() がなく、無効化時に Attacker / Stage が残留しプレイヤーが起き上がれなくなる。提案: OnDisable() => End(false); を追加。
PlayerGrapple.Wall.cs:357-365, 721-738: 壁シーケンス中断（ダウン・死亡）の finally で EndWallGang() のみ呼び EndWallSplit() を呼んでおらず、2人目（Assist）が壁アシスト状態で固着する。提案: FinishWallSequence() 内で EndWallSplit() も呼ぶ。
HeldBellyPunchDirector.Attack.cs:150-153, 312-364: PlaceThenAttack が CanContinuePlacement == false で yield break する際、直前に呼んだ best.LockForHandOff(..., releaseToken: false) を解除していない。提案: try / finally でロックと攻撃トークンを確実に解放する。
PinballDirector.cs:352, 355-381, 596-608 / PlayerGrapple.Wall.cs:206, 721-738: RunRounds() / Sandwich() / WallSplitRoutine() の Coroutine 参照を保持せず StopCoroutine していないため、キャンセル直後の再入場で古いコルーチンが新セッションを上書きする。提案: フィールドに Coroutine を保持し、停止・再開始時に明示的に StopCoroutine する。
HeldBellyPunchDirector.cs:943, 964, 998, 1119: ReleaseWithFinisherDown() を再入ガードなしで StartCoroutine している。提案: 同期メソッド化するか _releasing フラグで再入を防ぐ。 
ガード条件の順序ミスによる状態破壊・スパイク
PlayerGrapple.Hold.cs:11-21: if (IsGrappled || Stats == null || Stats.IsDead) return; より前に wrist.DetachOnGrab() を実行しており、すでに拘束中・死亡中でも他敵の接触で手首ロックだけが解除される。提案: ガード条件をメソッド先頭へ移動する。
PlayerWristLock.cs:89-93: CanAttach 判定より前に StartCoroutine(BurstShots()) を呼んでおり、アタッチ失敗のたびに撮影コルーチンが無限増殖する。提案: _locked = true; 確定後に移動する。
PlayerGrapple.Wall.cs:686-690, 693-703: EndWallGang() が _session.SuppressRemaining = 0f を無条件代入し、他演出の入力抑制まで消去する。提案: WallGang 専用の抑制フラグへ分離する。 
NullReferenceException 確定バグ
ProceduralRig.cs:101, 133, 737, 764-768: Ready 判定が左脚（LUpperLeg, LFoot）しか見ないのに RFoot.position へ無条件アクセスし、また CaptureFingers 内で _animator == null || !_animator.isHuman をチェックせず GetBoneTransform を呼ぶ。提案: Ready に右脚・下腿ボーンを含め、CaptureFingers 冒頭に _animator ガードを入れる。
EnemyFigure.Mount.cs:11, 55-59, 87: ApplyGroundMount 冒頭で mount == null や TargetRig 各ボーンの null チェックがない。提案: mount == null || rig == null || !rig.Ready のガードを追加する。
CharacterAnimator.PoseDown.cs:13: ApplyFrontEscapePose 内で Grapple == null チェックなしに Grapple.HeldSeconds へアクセスしている。提案: if (waiting <= 0f || Grapple == null) return; を追加する。
OverheadHoldPose.cs:123-129: -overheadlog 有効時に WristBone(true).position や _targetRig.LLowerArm.position へ null チェックなしでアクセスしている。提案: ?.position ?? Vector3.zero を使用する。
CharacterPreview.Hurt.cs:37-42, 178-183, 291-296: Vrm10.LoadPathAsync の戻り値 vrm == null チェックなしで vrm.gameObject へアクセスしている。提案: if (vrm == null) return; を追加する。 
IK 回転の上書き順序バグ・ポーズ二重適用・ゼロ除算
OverheadHoldPose.cs:166-170 / WallAssistPose.cs:89-93 / WallSplitArm.cs:66-70, 149-153: SettleElbowHinge の直後に親の upper.rotation を Slerp 上書きしているため、子である elbow.rotation が連動して回転しヒンジ計算が破綻する。提案: aimedUpper と aimedElbow を先に退避し、親→子の順で Slerp 代入する。
CharacterAnimator.Pose.cs:93-101: 同一の if (_state == State.Grabbed && ...) ApplyHaulDaze(ref pose); が2回連続で記述され、補間率が 0.85 ではなく 0.9775 に過剰適用される。提案: 重複ブロックを削除する。
CharacterAnimator.PoseDown.cs:130: _stateTime / _downFallSeconds にゼロ除算ガードがなく、0 設定時に NaN でリグが消失する。提案: Mathf.Max(0.05f, _downFallSeconds) で割る。 
リソースリーク・リリース混入・エディタ破壊
MotionShortcut.cs:13-15, 283: #if UNITY_EDITOR || DEVELOPMENT_BUILD がなくリリースビルドにデバッグ UI が常駐し、キー誤爆で SetGodMode(true) が発動・未解除になる。提案: プリプロセッサ指令でビルド除外し、リセット時に SetGodMode(false) を呼ぶ。
EnemyVrm.cs:128-147 / VrmCharacterSlot.cs:95-159, 287-306: VRM ロード失敗・例外・再入場時に DisposeVrm やキャンセルが行われずオブジェクト・VRAM がリークし、VrmCharacterSlot は Time.timeScale = 0f のままソフトロックする恐れがある。提案: 再入ガード・CancellationToken・finally での DisposeVrm と時間停止スタックを導入する。
PrototypeSceneBuilder.cs:48-64: new RenderTexture 後に rt.Release() のみで Object.DestroyImmediate(rt) がなくリークする。提案: try-finally 内で DestroyImmediate(rt) を呼ぶ。
CharacterPreview*.cs（全メニュー項目）: EditorSceneManager.NewScene を未保存確認なしで呼び、作業中シーンを破壊する。提案: 冒頭に if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return; を入れる。
WallPinSites.cs:136, 159-168: SiteX が static readonly で初回ロード時に固定され、シーン再ロードや設定変更が反映されない。提案: Build() 内で毎回 BuildSiteX() を呼ぶ。
motion_audit.py:84-113: audit_sheet() の worst_ratio が一度も更新されず常に 0.0 を返し、異常検知が機能していない。提案: ループ内で比率を更新し、--threshold 超過時に終了コード 1 を返す。
grapple_verify.py:208-215: --baseline が未追跡ファイルや破損ワークツリーで失敗しリカバリできない。提案: git clean -fd と git worktree prune による再作成を追加する。 
呼び出し側・実行時依存の仮説
CombatState.cs:65-73, 82-87: ApplyHpDamage で HP 0 になった後に AddFlinch が呼ばれる経路だと、死亡遷移ではなくダウン状態へ遷移する可能性がある。提案: AddFlinch 冒頭に if (!IsAlive || IsDown) return false; を追加する。 
EnemyBrain.Grapple.cs:575-624 / EnemyBrain.cs:1262-1274: TryWallPin 後に Player が null 化されると IsWallPinning が true のまま LateUpdate で位置固定され続ける。提案: LateUpdate で wallHold == null || !wallHold.IsGrappled || wallHold.Captor != this 時に ClearGrappleState() を呼ぶ。 
PlayerCombat.cs:233-236, 289-293: 先行入力消費時に Time.time > _comboUntil でも古い _bufferedStep（2〜3段目）のまま発動する可能性がある。提案: 消費時に猶予切れなら step = 0 にリセットする。 
EncounterDirector.cs:33-34, 94, 324: MaxWaves（10固定）と ArenaCount * WavesPerArena が連動しておらず、Config で波数を変えると早期終了または範囲外座標へ進む。提案: ApplySettings で MaxWaves = s.ArenaCount * s.WavesPerArena; を同期する。 
JointRomCheck.cs:177-178 / CharacterPreview*.cs: エディタメインスレッド上で Vrm10.LoadPathAsync(...).GetAwaiter().GetResult() を同期ブロックしており、UniVRM 内部のコンテキストポスト次第でエディタがデッドロックする。提案: EditorApplication.update による非同期ポーリングへ移行する。 
【High】高（状態破綻・カメラ演出競合・計算誤差・リソースリーク）
確定事実
カメラ演出の競合・優先度迂回
ThirdPersonCamera.cs:389-460, 666-734 / docs/review/state-transitions.md:327-335: HoldFocus が PushDistance の合成規則を迂回し _pullScale = 1f に直接リセットするため、死亡カメラ（ApplyDeathFraming）の引き演出が拘束カメラに上書きされる。また CinematicExitSeconds 途中の再リクエストでカメラが跳ねる。提案: 優先度付き単一カメラスタック（Cinematic > BeltFocus > Normal）へ統合する。
AnkleHoldPose.cs:40-47 / BearHugPose.cs:73-84: 複数敵が同時に HoldFocus を呼ぶと後勝ちでカメラが暴れる。提案: PlayerGrapple.Captor のみがカメラ要求を出すよう制限する。
CombatFeel.cs:170-202 / CameraDirector.cs:271-276: Camera.main のみに依存しており、未設定時に FindFirstObjectByType<ThirdPersonCamera>() フォールバックがなくシェイクが消失する。提案: フォールバック検索を共通化する。
WallSplitCamera.cs:125: target -= right * lateralShift * camSide * -1f; の二重否定により、コメント意図と逆方向にカメラが寄っている。提案: 符号を整理し意図通りにする。 
拘束・ダウン・引き渡し時の状態リセット漏れ
BackHoldPose.cs:28-34, 47-51: 顔パン中のダウン・吹っ飛び中断や OnDisable で _hasFaceChamber 等がクリアされず、次回拘束時に前回座標から拳がワープする。提案: OnDisable および非拘束時に履歴フラグをリセットする。
PlayerGrapple.HandOff.cs:36-43, 177-188 / docs/review/state-transitions.md:476-484: Shove 中断時に送り手（previous）の LockForHandOff が解除されず 1.3 秒棒立ちになり、また shove: false 経路で next.TakeOverHold が呼ばれない。提案: try / finally で双方のロックを解除し、shove: false でも TakeOverHold を呼ぶ。
EnemyBrain.Lifecycle.cs:151-185 / EnemyBrain.Grapple.cs:156-185: ResetForMotionPreview と TakeOverHold で _feinted や _evadeOffset、IsWallPinning 等が未クリアになる。提案: 冒頭で ClearGrappleState(true) と回避状態をリセットする。
CharacterAnimator.State.cs:22-49 / CharacterAnimator.Preview.cs:40-49: ForceState が SetState を通らないため State.Down 時の _downFallSeconds や _downEndHipsY/Z が初期化されず前回状態を引き継ぐ。提案: ForceState 内でフラグ初期化共通メソッドを呼ぶ。
ProceduralRig.cs:188-219: ResetPose() に指ボーン（_fingerRest）と LToe / RToe が含まれておらず、フレームを跨いで指の回転が累積・残留する。提案: ResetPose() で指とつま先も初期値へ復元する。
GroundHaulUp.cs:42, 181-185: Forced（static）が sceneLoaded でリセットされず、リロード後も引き起こしが暴発する。提案: BeforeSceneLoad でリセットする。 
計算誤差・境界値バグ
BodyOverlap.cs:208-263: SegmentSegmentDistance で短い線分（3mm程度）が denom <= Eps により誤って平行扱いになり、NaN 入力時も NaN を返す。提案: 相対誤差（denom > Eps * len1Sq * len2Sq）にし、Clamp01 で NaN を 0f へ倒す。
EnemyRole.cs:38-73: AuthoredGrappler が複数いる場合に列挙順で選んでおり、仕様コメント「列挙順非依存」に違反している。提案: ソート条件の最優先キーに AuthoredGrappler を組み込む。
WristLockRules.cs:28-30: endedAt <= 0f を未発生判定に使っているため、0.0 秒フレームで解除されると猶予・クールタイムが消滅する。提案: センチネル値を -1f に変更する。
GrappleResistance.cs:34, 49-51: IsEscaped は切り上げ（Ceiling）なのに Progress は生値除算のため、ゲージ 99.9% でも脱出できないズレが生じる。提案: Progress の分母も Math.Ceiling(_requiredInputs) に統一する。
CharacterAnimator.ApplyPose.cs:210, 262: _downFallSeconds と _stateDuration のゼロ除算ガードが一部抜けており NaN 発生リスクがある。提案: Mathf.Max(0.05f, ...) で保護する。 
リソースリーク・イベント二重購読・毎フレーム全探索
WristLockVisual.cs:19-30 / ContactShadow.cs:37-38, 112-113: OnDestroy で生成した Material / Mesh / Texture2D を Destroy していない。提案: OnDestroy で明示破棄する。
PlayerFace.cs:280-355: static Dictionary<AudioClip, VoiceEnvelope> s_envelopes が破棄されずリークし、ステレオ音声の右チャンネルを無視している。提案: シーンアンロード時に Clear() する。
EnemyReaction.cs:67-72: OnEnable で Brain.Hit -= OnHit; なしで購読しており二重発火の恐れがある。提案: 購読直前に -= を入れる。
HeldBellyPunchDirector.HandOff.cs:90 / CharacterAnimator.Lifecycle.cs:144-145: 毎フレームや被弾時に FindObjectsByType / FindFirstObjectByType を実行し GC スパイクを起こしている。提案: EnemyBrain.ActiveEnemies 静的レジストリへの登録方式へ移行する。
PlayerMotor.cs:200-205, 280: CharacterController.Move の後に LateUpdate で transform.position を直接書き換えており、物理コライダーキャッシュと不整合を起こす。提案: Move() 内の移動ベクトルに Z 補正を含める。 
呼び出し側・実行時依存の仮説
WristPullDirector.cs:86-93, 126: 引き寄せ構え中に輪が割れたりダウンしても中断されず、また _puller が Destroy されると MissingReferenceException になる。提案: TickPull 冒頭で _puller == null || !_wrist.IsLocked || _stats.IsDown を検査しキャンセルする。 
EnemyFigure.Step.cs:1002-1015: UsesLeftPunch による左右反転が、距離計算で選んだ WallSplitKneePose の脚や腰位置まで反転させてしまう可能性がある。提案: 左右反転をパンチ打撃時のみに限定する。 
PlayerCombat.cs:141-242: PauseMenu.IsPaused ガードがなく、ポーズ中の入力で攻撃が開始・暴発する。提案: Update() 冒頭に if (PauseMenu.IsPaused) return; を追加する。 
PlayerBuild.cs:58-66: IsStale() のパス区切りが OS 依存で、.vrm 更新やファイル削除を検知できない。提案: / 正規化と .vrm 監視、ファイル一覧ハッシュ比較を追加する。 
【Medium】中（保守性・GC/パフォーマンス・境界値・テスト信頼性）
確定事実
GC アロケーション・毎フレーム検索
BodyOverlap.cs:278-279: KeyOf が毎フレーム文字列補間でヒープ確保し、A/B 入れ替えで二重集計する。提案: ValueTuple キーへ変更する。
EnemyBrain.Grapple.cs:654-693, 844-855: 拘束打撃のたびに new AttackData を生成している。提案: 事前生成インスタンスを使い回す。
AnkleHoldPose.cs:24 / BackHoldPose.cs:82 / BearHugPose.cs:29 / PinHoldPose.cs:61 / OcclusionFade.cs:166: LateUpdate で毎フレーム GetComponent<PlayerGrapple>() や Camera.main を呼んでいる。提案: フィールドにキャッシュする。
MatchHud.cs:318-458: 値が変わっていないフレームも毎フレーム文字列補間で .text を更新している。提案: 前回値をキャッシュし変化時のみ更新する。
CharacterPreview*.cs: セル描画ごとに GetPixels() で大量の Color[] をアロケートしている。提案: Graphics.CopyTexture へ置き換える。 
状態・カメラ・演出の不整合
docs/review/state-transitions.md:454-463 (PlayerGrapple.Wall.cs): ClearSplit が SplitWeight = 0f を即時代入し、解除時 0.60 秒の補間をスキップしてカメラと姿勢が跳ねる。提案: 退出フェーズで減衰させる。
docs/review/state-transitions.md:465-474, 498-505: DoPinKnee に Player.IsDead ガードがなく死亡フレームで二重発火し、Die() / Revive() で HoldsBellyPunchGuard が未リセットになる。提案: IsDead ガードとフラグクリアを追加する。
EnemyReaction.cs:222-224: renderer.material アクセスで複製されたマテリアルが破棄されずリークする。提案: OnDestroy で破棄するか共有マテリアル設定を使う。
PoseGrabbed.cs:25-780 / GrappleTestDriver.cs:1-726: 単一メソッド・単一クラスが 700 行超で責務が過密。提案: 拘束種別・計測プローブ単位へ分割する。
SfxAudition.cs:7-42: SfxId.WallSlam = 14 が Entries に未登録で試聴できず、- キーのインデックスもずれている。提案: Entries に WallSlam を追加する。 
Python ツール・テスト基盤の欠陥
grapple_verify.py:249-260, 523-527: ログ全文スキャンによる誤検知と、[0-9][0-9].png 固定パターンによる 100 枚目以降の欠落。提案: オフセット追記読み込みと \d+ 正規表現へ変更する。
preview.py:240-245: files が空のとき make_sheet() が IndexError で落ちる。提案: if not picked: return を追加する。
PlayMode Tests 全般（WallPinRuntimeTests.cs, HeldBellyPunchTests.cs 等）: 待機に Time.realtimeSinceStartup や固定 WaitForSeconds を多用し、タイムアウト時のサイレント通過や Destroy 前の Assert 失敗によるオブジェクトリークがある。提案: TestTime / TestWait への統一、タイムアウト時 Assert.Fail、try-finally での破棄を徹底する。 
呼び出し側・実行時依存の仮説
VrmHairWallClamp.cs:19, 39-70: UniVRM SpringBone 計算後に座標を直接書き換えるため、次フレームの速度差分で髪が跳ね返る可能性がある。提案: VRM10SpringBoneCollider の動的配置を検討する。
CharacterPreview.Measure.cs:81, 145-151: MeasureExposure の背景色判定が Linear/Gamma 色空間の差で誤判定する恐れがある。提案: アルファチャンネル判定へ変更する。 
【Low】低（可読性・コメント乖離・軽微な改善）
未使用コード・定数の残存: EnemyFigure.Build.cs（BuildWeapon 等）、EnemyFigure.Primitives.cs:45（Sphere）、CombatConstants.cs:156（WallSplitPunchInterval）、ProceduralRig.cs:633（_palmNormalL/R）、GroundHaulUp.cs:29（Beat.Release）、HeldBellyPunchDirector.cs:125, 1107。提案: 削除または [Obsolete] 整理。
ドキュメント・コメントの乖離: CHARACTER.md（168cm）と Enemy-VRM-Spec.md（1.55m）の身長矛盾、SPEC.md と Motion-Notes.md の数値乖離、ローカル絶対パス（C:\Users\neoen\...）のハードコード。提案: 現行実装値と相対パス表記へ統一。
Domain Reload 無効時の static 汚染: GameLog.cs, InputReader.cs, LaunchOptions.cs, MatchHud.cs で [RuntimeInitializeOnLoadMethod(SubsystemRegistration)] による初期化が欠落。提案: サブシステム登録時にリセットする。
CI ワークフローの範囲不足: .github/workflows/runner-unit-tests.yml が motion_audit.py や grapple_verify.py の変更をトリガーせず、ruff チェックもない。提案: MioPrototype/tools/*.py 全体を対象にし ruff check と単体テストを追加する。 
2. 要確認（既知フレーク・設計意図との矛盾があるため隔離）
以下の項目はレビュー中に指摘されたものの、AGENTS.md / Motion-Notes.md / docs/review/state-transitions.md の設計意図や既知の事情と衝突するため、機械的な修正を行わず仕様確認を優先すべき項目です。 
WristPullDirector.cs:139 の BreakPressed() と攻撃ボタンの競合
指摘: 連打用の BreakPressed() が頭突きカウンターの「1回限り権利」を消費してしまう。
要確認: README.md / REVIEW.md にある通り、現状は「もがき連打と攻撃ボタン（J/K/L）が同一キー割り当て」の仕様である可能性が高い。専用ボタンが別にあるのか、同一キー前提で猶予/マスクを設ける設計なのかを確認する必要がある。 
カメラ切り替え（入口ブレンド vs 顔可視率 AC）
指摘: 押さえつけ系と腹パン系のカメラ切り替え時に画が跳ぶため補間を入れるべき。
要確認: Motion-Notes.md:765-801 に「入り口側の補間を遅くすると『顔が画面に映っている率』が 84% → 59% へ悪化して既定 AC に落ちたため意図的に取り消した」経緯がある。補間導入は顔可視率 AC を割るリスクがあるため、単なる補間ではなくカット演出やカメラスタック設計の再検討が必要。 
3人がかり壁拘束の敵による遮蔽と OcclusionFade 除外
指摘: 壁の3人がかりで約4割のフレームで敵がプレイヤーを隠す。
要確認: Motion-Notes.md:635-639 にて OcclusionFade から意図的に除外されている。カメラ角・配置変更で対応するか、VRM 用のディザ抜きを新規導入するかの仕様判断が必要。 
起床無敵時間（_downImmuneUntil）中の腹ダメージ未加算
指摘: 無敵時間中に掴まれると腹パン 5 発でも腹ダメージが溜まらない。
要確認: docs/review/state-transitions.md:550-551 にて「拘束成立時に無敵を解除すべきか、腹ダメージのみ無敵を貫通させるべきか」が未確定仕様として保留されている。 
非公開 API InternalEditorUtility.isApplicationActive の使用
指摘: AutoPlayerBuild.cs:109 で内部 API を使っており将来壊れるリスクがある。
要確認: REVIEW.md:56, 88 にて「エディタ非アクティブ時の自動ビルド暴発を防ぐため」に意図的に導入された対策であるため、削除ではなくリフレクションフォールバック付きラッパーへの隔離に留めるべき。 
PlayMode テストの既知フレーク（PlayerFaceRuntimeTests「おふ」口形など）
指摘: 全体実行時にタイミング依存テストが落ちる。
要確認: REVIEW.md:119-121 に既知フレークとして記録済み。時間経過の WaitForSeconds を状態待機やピークサンプリングへ変える際、本来検出すべき表現タイミングの AC を緩めすぎないよう注意が必要。 
3. 次スプリント（1スプリント）で着手すべき上位 10 項目（優先順位順）
順位	対象領域	具体的なタスク	解決するリスク
1	リリース保護	MotionShortcut.cs 全体を #if UNITY_EDITOR || DEVELOPMENT_BUILD で囲み、リセット時の SetGodMode(false) を追加する。	出荷ビルドへのデバッグ UI 混入および無敵化によるゲーム進行破壊の防止。
2	Null/IK 致命バグ修正	ProceduralRig.Ready（右脚・下腿検証と _animator ガード）、EnemyFigure.Mount の null ガード、および OverheadHoldPose / WallAssistPose / WallSplitArm の SettleElbowHinge 回転退避順序を修正する。	VRM/敵モデル読み込み時の即座のクラッシュと、肘 IK 逆折れ・破綻の解消。
3	拘束中断時の残留解消	GroundHaulUp.OnDisable 追加、FinishWallSequence での EndWallSplit() 呼び出し、HeldBellyPunchDirector / PlayerGrapple.HandOff の try-finally ロック解除を実装する。	ダウン・死亡割り込み時にプレイヤーが起き上がれなくなる、または敵が硬直・Assist 固着する進行不能バグの根絶。
4	コルーチン多重起動防止	PinballDirector（RunRounds / Sandwich）と PlayerGrapple.Wall（WallSplitRoutine）で Coroutine 参照を保持し、停止・再入場時に確実に StopCoroutine する。	キャンセル直後の再拘束で古いコルーチンが新セッションを破壊する競合バグの防止。
5	ガード順序・ゼロ除算修正	PlayerGrapple.Hold の DetachOnGrab をガード条件の後ろへ移動し、CharacterAnimator.Pose.cs の ApplyHaulDaze 重複削除、および PoseDown / ApplyPose のゼロ除算ガードを統一する。	拘束中の他敵接触による不当な手首ロック解除と、NaN 発生によるモデル消失の防止。
6	カメラ演出競合の統合	ThirdPersonCamera.HoldFocus が _pullScale を直接リセットする処理を改め、PushDistance の合成規則・優先度（死亡カメラ・シネマティック優先）へ統合する。	死亡演出や複数敵拘束が重なった際のカメラ上書き・画の飛びの解消。
7	VRM ロード安全化とリーク対策	VrmCharacterSlot と EnemyVrm に再入ガード・キャンセル・失敗時 DisposeVrm を実装し、Time.timeScale 操作をスタック管理化してソフトロックを防ぐ。	シーン再読込・モデル差し替え時の VRAM リークと timeScale == 0 固着の防止。
8	敵レジストリ導入（脱 FindObjectsByType）	EnemyBrain.ActiveEnemies 静的リスト（OnEnable/OnDisable 登録）を導入し、HeldBellyPunchDirector・PlayerCombat・PinballDirector 等の毎フレーム全探索を置換する。	戦闘中の毎フレーム配列アロケーションによる GC スパイクとフレーム落ちの解消。
9	CI・検証ツールの正常化	motion_audit.py の worst_ratio 更新修正、grapple_verify.py のワークツリー復旧・3桁連番対応、および runner-unit-tests.yml への全 .py 対象化と ruff 追加を行う。	品質ゲート（モーション監査・回帰検証）のサイレント無効化を防ぎ、自動検証の信頼性を回復する。
10	PlayMode テストの安定化と境界追加	テスト基底クラスで静的フラグ（ForcedGrabKind, GroundHaulUp.Forced, RyonaConfig）と try-finally 破棄を共通化し、「壁拘束/Pin 中の死亡割り込み」回帰テストを追加する。	テスト間汚染によるフレークをなくし、修正した割り込みバグの再発を CI で自動検知する。

---

## 適用済みパッチ（2026-11-22）

上位 1〜6・9 項を作業ツリーに適用。ビルド成功（`build result=Succeeded errors=0`）、`preview.py --reel` と `face` の撮影、`motion_audit.py --threshold 5` で確認済み（haul の 5.1 倍はカメラカットによる意図的なもの）。

- 1: `MotionShortcut.ExecuteResetOrToggleAi()` で `SetGodMode(false)`。ファイル全体の `#if UNITY_EDITOR || DEVELOPMENT_BUILD` 化は見送り — `preview.py` は非 Development のプレイヤービルド（`BuildOptions.None`）で `-motionseq` を使うため、除外すると撮影フローが壊れる。
- 2: `ProceduralRig.Ready` に右脚系を追加、`CaptureFingers()` に `_animator` ガード、`EnemyFigure.Mount` に `mount` / `TargetRig.Ready` ガード、`_downFallSeconds` のゼロ除算ガード。肘 IK の回転代入順序は変更せず — 現コードは `aimedElbow` を退避したうえで親→子の順に代入しており、指摘の前提がコードと一致しなかった。
- 3: `GroundHaulUp.OnDisable` 追加、`FinishWallSequence()` で `EndWallSplit()`、`EnemyBrain.ReleaseHandOffLock()` を新設し `Shove` / `ReceiveFromPin` / `PlaceThenAttack` を try/finally 化。
- 4: `PinballDirector._rounds`、`PlayerGrapple._wallSplit` でコルーチンを保持し、停止時に明示的に `StopCoroutine`。
- 5: `BeginHold` のガードを手首ロック処理より前に移動、`ApplyHaulDaze` の重複呼び出しを削除、ゼロ除算をガード。
- 6: `ThirdPersonCamera.DeathFramingActive` を追加し、死亡中は `HoldFocus` と寄り `PushDistance` が死亡の引きを消さないようにした。`CameraExitBlendTests` に回帰テストを追加し 3 回連続パス、`BearHugTests|UiRuntimeTests` も 9/9。カメラスタック全体の優先度統合は見送り — 出口ブレンドと顔可視率 AC を壊すリスクがあり、実害が確定している死亡時の上書きだけを直した。
- 7: コード照合で既に対応済みだった（`VrmLoadGate.Semaphore` で直列化、`_disposed` を判定、失敗時 `DisposeVrm`、`timeScale` は TitleGate / PauseMenu を見て復帰）。変更なし。
- 8: 未対応。`FindObjectsByType<EnemyBrain>` は本編 15 箇所＋デバッグ専用が多数で、レジストリ化は列挙順の変化を通して敵選択・テストに効く。性能改善であり進行不能バグではないため、今回は触らない。
- 9: `motion_audit.audit_sheet()` が最大比率を返すようにし `--threshold` を終了コードへ接続、`grapple_verify` の連番 glob を 3 桁以上対応の `numbered_frames()` に置換、壊れた基準ワークツリーを再生成。CI は `MioPrototype/tools/**` 全体を対象にし `ruff check` を追加。`unittest` 29 件＋ ruff 緑。
