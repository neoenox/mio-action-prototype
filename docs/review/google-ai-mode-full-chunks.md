# Google AI モード チャンク別レビュー（全 25 チャンク）

生データ: `MioPrototype/Verify/source-review/answers/`

---


## answer-01

# Chunk 01

Files:
- MioPrototype/Assets/RyonaCore/Runtime/ArenaPlan.cs
- MioPrototype/Assets/RyonaCore/Runtime/AttackData.cs
- MioPrototype/Assets/RyonaCore/Runtime/BodyDamage.cs
- MioPrototype/Assets/RyonaCore/Runtime/BodyOverlap.cs
- MioPrototype/Assets/RyonaCore/Runtime/BodyPart.cs
- MioPrototype/Assets/RyonaCore/Runtime/CombatConstants.cs
- MioPrototype/Assets/RyonaCore/Runtime/CombatState.cs
- MioPrototype/Assets/RyonaCore/Runtime/DamageResolver.cs
- MioPrototype/Assets/RyonaCore/Runtime/EnemyRole.cs
- MioPrototype/Assets/RyonaCore/Runtime/GrappleResistance.cs
- MioPrototype/Assets/RyonaCore/Runtime/HitResult.cs
- MioPrototype/Assets/RyonaCore/Runtime/JointRom.cs
- MioPrototype/Assets/RyonaCore/Runtime/PinOwnerRules.cs
- MioPrototype/Assets/RyonaCore/Runtime/PinballRules.cs
- MioPrototype/Assets/RyonaCore/Runtime/StaggerTier.cs
- MioPrototype/Assets/RyonaCore/Runtime/WristLockRules.cs
- MioPrototype/Assets/RyonaCore/Tests/EditMode/ArenaPlanTests.cs
- MioPrototype/Assets/RyonaCore/Tests/EditMode/BodyOverlapTests.cs
- MioPrototype/Assets/RyonaCore/Tests/EditMode/DamageResolverTests.cs
- MioPrototype/Assets/RyonaCore/Tests/EditMode/EnemyRoleRulesTests.cs
- MioPrototype/Assets/RyonaCore/Tests/EditMode/FlinchTests.cs
- MioPrototype/Assets/RyonaCore/Tests/EditMode/JointRomTests.cs
- MioPrototype/Assets/RyonaCore/Tests/EditMode/PinOwnerRulesTests.cs
- MioPrototype/Assets/RyonaCore/Tests/EditMode/PinballRulesTests.cs
- MioPrototype/Assets/RyonaCore/Tests/EditMode/WallGangTempoTests.cs
- MioPrototype/Assets/RyonaCore/Tests/EditMode/WristLockRulesTests.cs

---

ご提示いただいた RyonaCore（26ファイル）のソースコードレビューを行いました。
全体として、UnityEngine に依存しない純粋な C# ロジックとして分離されており、テストコードも日本語で意図が明確に記述されていて非常に保守性の高い設計です。
その上で、不具合・境界値・null参照・例外・状態遷移・テスト不足の観点から網羅的に洗い出した指摘事項を重大度別にご報告します。
Critical（致命的：クラッシュやロジック破綻に直結）
現時点の単体ロジック内で即座にゲームがクラッシュするような致命的バグは見当たりませんが、呼び出し側の状態遷移と組み合わさることで破綻するリスクが 1 点あります。
1. CombatState の状態不整合（死亡状態でのひるみ・ダウン遷移）
対象: CombatState.cs (65-73行目, 82-87行目)
確定事実: ApplyHpDamage で Hp が 0 になっても、AddFlinch は通常通り Flinch を加算し、上限に達すると true（ダウンさせるべき）を返します。また IsDown = true の間も AddFlinch や Tick は制限なく進行します。
推測（仮説）: 呼び出し元（ゲーム層）が DamageResolver.Resolve → AddFlinch の順で呼んだ場合、とどめの一撃（HP 0）と同時にダウン判定が true になり、死亡モーションではなくダウン状態へ遷移してしまう不具合が起きる可能性があります。
修正提案: AddFlinch の冒頭に if (!IsAlive || IsDown) return false; を追加するか、仕様上ダウン中も蓄積させる場合は死亡時（!IsAlive）のみガードを入れてください。
High（高：計算誤差・仕様との食い違い・意図しない挙動）
2. BodyOverlap.SegmentSegmentDistance における平行線分・縮退時の計算誤差と NaN リスク
対象: BodyOverlap.cs (208-263行目)
確定事実:
len1Sq <= Eps は判定していますが、len1Sq が 1e-5f 程度の極小値（例: 3mm の線分）だった場合、len1Sq * len2Sq は 1e-10f となり denom > Eps（1e-8f）を下回ります。
結果として「平行ではない短い線分」が強制的に平行扱い（s = 0f）になり、最短距離が不正確になります。
また、入力座標に float.NaN が混入した場合、denom > Eps が false になり s = 0f になりますが、t = (bDot * s + fDot) / len2Sq の計算結果が NaN となり、t < 0f も t > 1f も false をすり抜けて最終的に NaN を返します。
修正提案: Eps の比較は相対誤差（denom > Eps * len1Sq * len2Sq）にするか、長さが極端に短い場合は点として扱う閾値を調整してください。また Clamp01 内で NaN を 0f に倒す（!(value >= 0f) なら 0f）ようにすると安全です。
3. PinOwnerRules と EnemyRoleRules のロジック重複と仕様のズレ
対象: PinOwnerRules.cs (45-86行目), EnemyRole.cs (38-73行目)
確定事実:
PinOwnerRules では CanPin が複数いる場合「背丈 → 名前」で 1 人を選びますが、EnemyRoleRules では AuthoredGrappler が複数いる場合、ループで最初に見つかった個体（列挙順）を採用しており（44-51行目）、クラスのコメント「列挙順には依存しない」に違反しています。
また、EnemyRoleRules で AuthoredGrappler が存在する場合、残りの敵は全員 Striker になり、3人いても Tools が割り当てられません（53行目）。
推測（仮説）: 役割決定が PinOwnerRules から EnemyRoleRules へ移行する過渡期にあると推測されますが、複数の敵に AuthoredGrappler = true を設定すると、シーンのオブジェクト順に依存するバグが発生します。
修正提案: EnemyRoleRules でも AuthoredGrappler をソート条件の最優先キー（bool 降順 → Height 降順 → Name 昇順）に組み込むことで、列挙順非依存と Tools 割り当てを両立できます。
4. WristLockRules.CanAttach における endedAt == 0f の境界バグ
対象: WristLockRules.cs (28-30行目)
確定事実: IsInGrace と CanAttach で「未発生（初回）」を endedAt <= 0f で判定しています。ゲーム開始直後（now = 0.5f）に手首ロックが解除されて endedAt = 0.5f なら動きますが、もしゲーム開始フレーム（Time.time == 0f）で解除された場合、endedAt <= 0f が true となり、犹予時間（GraceSeconds）もクールタイム（CooldownSeconds）も完全に無視されて即座に再装着可能になります。
修正提案: 未発生を表すセンチネル値には 0f ではなく負数（例: -1f や float.NegativeInfinity）を使用し、endedAt >= 0f で発生済みを判定してください。
5. GrappleResistance における整数切り上げと Progress 表示のズレ
対象: GrappleResistance.cs (34行目, 49-51行目)
確定事実:
IsEscaped は InputCount >= Ceiling(_requiredInputs) で判定しています。例えば _requiredInputs = 12.5f（腕損傷100%の背面掴み）の場合、13回目の入力で IsEscaped == true になります。
しかし Progress は InputCount / _requiredInputs で計算されるため、12回目の時点で 12 / 12.5 = 0.96f、13回目で 1.0f になります。一方、もし _requiredInputs = 12.01f だった場合、12回目の入力で Progress は 12 / 12.01 = 0.99916f（UI上ほぼ100%）なのに脱出できていない状態が発生します。
修正提案: Progress の分母にも Math.Ceiling(_requiredInputs)（必要整数回数）を使うことで、UIゲージの満タンと脱出成立のタイミングを完全に一致させるべきです。
Medium（中：例外リスク・設計上の落とし穴・保守性）
6. BodyOverlapTally の辞書キー衝突リスクと毎フレームの文字列アロケーション
対象: BodyOverlap.cs (278-279行目, 317-318行目)
確定事実:
KeyOf が $"{reading.SubjectA}:{(int)reading.PartA}|{reading.SubjectB}:{(int)reading.PartB}" を生成しています。
パフォーマンス: Add や CountOf が呼ばれるたびに文字列補間によるヒープアロケーションが発生します。10部位×10部位×敵3体＝300ペアを毎フレーム計測する場合、GCスパイクの原因になります。
ロジック: SubjectA と SubjectB を入れ替えて渡された場合（例: (プレイヤー, 敵1) と (敵1, プレイヤー)）、別ペアとして二重集計されます。
修正提案: キーを string ではなく (string, OverlapPart, string, OverlapPart) の ValueTuple にすることで、文字列アロケーションをゼロにできます（JointRomTally のように整数やタプルをキーにする設計に揃えるのが望ましいです）。
7. BodyOverlapReading および JointLimit の default 構造体生成時の NullReferenceException
対象: BodyOverlap.cs (165-172行目), JointRom.cs (225-235行目)
確定事実: どちらも readonly struct であり、C# の仕様上 new BodyOverlapReading()（引数なしデフォルトコンストラクタ）で生成可能です。その場合、SubjectA や Limit.Label が null になります。JointRomReading で default に対して Describe() や Verdict を呼ぶと、Limit.Label は空文字扱いになりますが、BodyOverlapReading の Describe() はそのまま動きますが意図しない文字列になります。
修正提案: プロパティのゲッターで SubjectA ?? "A" のようにフォールバックさせるか、コンストラクタ経由の生成を徹底してください。
8. ResistanceMath.AnkleMashInputs の静的ミュータブル状態（スレッド安全性・テスト汚染）
対象: GrappleResistance.cs (68行目)
確定事実: public static int AnkleMashInputs { get; set; } = 8; とグローバルな静的プロパティになっています。
推測（仮説）: 編集モードテストやプレイモードテストでテストケースの1つがこの値を書き換えると、後続のテストに状態がリーク（テスト汚染）します。またマルチスレッド環境では競合します。
修正提案: テストの [TearDown] で必ず初期値に戻す仕組みを入れるか、RequiredInputs の引数や設定オブジェクト経由で渡す設計を検討してください。
9. DamageResolver.Resolve の戻り値 HitResult にひるみ値（Flinch）が含まれていない
対象: DamageResolver.cs (9-31行目), HitResult.cs (6-18行目)
確定事実: DamageResolver のコメントに「ヒット解決の唯一の入口」と書かれていますが、HPダメージと部位ダメージは defender に適用する一方、attack.Flinch の加算は行っていません。
推測（仮説）: 「掴まれ中の他敵からの攻撃はダメージ半減（GrabbedDamageFromOthers）」や「ダウン無敵時間（DownImmunitySeconds）」の制御をゲーム層で行うために意図的に分離していると推測されますが、呼び出し側で AddFlinch を呼び忘れるリスクがあります。
修正提案: 意図的な分離であれば、DamageResolver.Resolve の XML ドキュメントコメントに「ひるみ（Flinch）の加算は状態機械側で行うこと」と明記しておくと保守性が上がります。
10. CombatConstants における WallSplitPunchInterval の未使用・重複定義
対象: CombatConstants.cs (156行目, 174-177行目)
確定事実: public const float WallSplitPunchInterval = 0.55f; が定義されていますが、段階別の WallSplitHitInterval(int index)（174行目）では 0.50f / 0.65f / 0.95f を返しており、定数 0.55f が参照されていません。また WallSplitEscapeInputs（303行目）と WallPauseInputsAfterSplit（213行目）など、似た概念の定数が離れて配置されています。
修正提案: 旧仕様の定数であれば [Obsolete] を付けるか削除し、段階別の基準値として使うなら WallSplitHitInterval の計算式に組み込んでください。
Low（低：軽微な改善点・エッジケース・テスト不足）
11. ArenaPlan の負数パラメータに対する防御の不均一さ
対象: ArenaPlan.cs (22-28行目)
確定事実: Pitch、WavesPerArena、ArenaCount はコンストラクタで不正値（0以下）をクランプしていますが、StartX に float.NaN や float.PositiveInfinity が渡された場合のチェックがありません。また Default プロパティ（20行目）が呼ばれるたびに new ArenaPlan(...) を生成しています（値型なのでコストは極小ですが static readonly にできます）。
12. BodyPartSpecs のキャッシュ効率と配列境界
対象: BodyOverlap.cs (109-123行目)
確定事実: OverlapPart が 0〜9 の連番であることを前提に配列アクセスしています。将来 OverlapPart に欠番や順序変更が加わると IndexOutOfRangeException ではなく別の部位の名前を返してしまうリスクがあります（JointRomTests には順序一致のテストがありますが、BodyOverlapTests にはありません）。
13. CombatState.SetMaxHp の挙動
対象: CombatState.cs (26-30行目)
確定事実: SetMaxHp を呼ぶと現在 Hp も MaxHp に全快します。戦闘中に最大HPだけを増減させるケースでは意図しない全快になります。メソッド名を InitializeMaxHp にするか、現在HPを維持するオーバーロードがあると安全です。
テスト不足の指摘（追加すべき単体テストケース）
現在のテストスイート（10ファイル）は正常系と主要な仕様の固定が非常に良く書けていますが、以下の境界値・異常系のテストが不足しています。
DamageResolverTests のエッジケース:
attacker / attack / defender に null を渡したときに例外を出さず default を返すこと。
scale に負数（-1f）や 0f を渡したときに HP が回復したり部位ダメージがマイナスにならないこと。
attack.partDamage が 0f（GrappleAttack など）のときに部位ダメージが加算されないこと。
PinballRulesTests の境界値:
inputTime - catchTime がちょうど -HalfWindowOf(round) および +HalfWindowOf(round) と一致する境界値（InWindow になること）。
round に負数（-1）を渡したときに RoundClosed になること。
WristLockRulesTests の境界値:
now == endedAt のとき IsInGrace が true になること。
猶予時間（1.5秒）経過後、クールタイム（25秒）未満の区間で CanAttach が false になること。
BodyOverlapTests の追加ケース:
BodyPartSpecs の OverlapPart enum の値と Labels / HeightRatios のインデックスが全要素で一致していること（JointRomTests.定義済みの関節はすべてテーブルにある と同等の検証）。
未定義の (OverlapPart)99 を渡したときに ArgumentOutOfRangeException がスローされること。
2つの線分が同一直線上で重なっている場合や交差している場合（最短距離 0）に SegmentSegmentDistance が 0f を返すこと。
CombatState / FlinchTests の追加ケース:
Tick や AddFlinch に 0f や負数を渡しても状態が壊れないこと。
EnsureHpAtLeast が MaxHp を超える値を渡されたときに MaxHp でクランプされること。
総評
RyonaCore 層は Unity の MonoBehaviour やフレームレート依存から綺麗に切り離されており、仕様書の数値や意図がコメントとテストで丁寧に言語化されています。上記の High（特に EnemyRoleRules の列挙順依存と SegmentSegmentDistance の極小線分/NaN耐性） および 境界値テストの拡充 を行うことで、ゲーム層（RyonaGame）の実装が進んでも揺るがない堅牢な基盤になります。
もしよろしければ、次のステップとして以下のお手伝いが可能です：
High・Medium 指摘事項の修正コード案（EnemyRoleRules や BodyOverlap などの具体的な書き換えパッチ）の作成
指摘した不足テストケースを追加した NUnit テストコードの生成
次のチャンク（RyonaGame 側の実装ファイル群）のコードレビュー
どちらを進めましょうか？


## answer-02

# Chunk 02

Files:
- MioPrototype/Assets/RyonaGame/Runtime/AI/AnkleHoldPose.cs
- MioPrototype/Assets/RyonaGame/Runtime/AI/BackHoldPose.cs
- MioPrototype/Assets/RyonaGame/Runtime/AI/BearHugPose.cs
- MioPrototype/Assets/RyonaGame/Runtime/AI/EnemyFigure.AttackPose.cs
- MioPrototype/Assets/RyonaGame/Runtime/AI/EnemyFigure.Build.cs
- MioPrototype/Assets/RyonaGame/Runtime/AI/EnemyFigure.HoldPose.cs
- MioPrototype/Assets/RyonaGame/Runtime/AI/EnemyFigure.Mount.cs
- MioPrototype/Assets/RyonaGame/Runtime/AI/EnemyFigure.Primitives.cs
- MioPrototype/Assets/RyonaGame/Runtime/AI/EnemyFigure.Step.cs
- MioPrototype/Assets/RyonaGame/Runtime/AI/EnemyFigure.cs

---

ご提示いただいた 10 ファイル（敵のアニメーション・IK・拘束ポーズ制御群）のコードレビューを行います。
全体の設計として、物理や Animator に頼らずプロシージャルに全身の骨格と 2-Bone IK を組み立てており、コメントから実測に基づいた丁寧な調整の意図が伝わります。一方で、初期化タイミング・null ガードの抜け・実行順（ExecutionOrder）による競合・状態遷移時の残存ステートに起因する潜在的な不具合がいくつか見られます。
Critical（クラッシュ・NullReferenceException の確定バグ）
EnemyFigure.Mount.cs:11, 55-59, 87
事実: ApplyGroundMount(GroundMountAttack mount) の冒頭で mount == null のチェックがなく、続けて var rig = mount.TargetRig; の後に rig == null、rig.Hips == null、rig.Chest == null、rig.LUpperArm == null、rig.RUpperArm == null をチェックせずに .position へアクセスしています。
修正提案: 冒頭に if (mount == null || _hips == null) return; を入れ、55行目付近で if (rig == null || !rig.Ready || rig.Hips == null || rig.Chest == null) return; のガードを追加してください。
EnemyFigure.Build.cs:103-113 と EnemyFigure.cs:50-51, 488, 503-514
事実: Build() 内で BuildWeapon(...) が一度も呼ばれておらず、_weapon は常に null のままです（素手仕様へ変更された痕跡と思われます）。結果として WeaponPivot は常に null を返し、SetWeaponVisible は即 return します。また、Build() 内で Arm() を呼んだ直後に _fistR.GetComponent<Renderer>().sharedMaterial = _weaponSteel; としていますが、Arm() 内の Blob() は CreatePrimitive を使うため Renderer は存在しますが、万一プリミティブ生成を変更した際に落ちます。
修正提案: 武器（鉄パイプ）が完全に廃止されているのであれば、未使用の BuildWeapon・_weapon・_weaponRenderers・SetWeaponVisible を削除するか、_weaponSteel の命名を _fistMaterial 等に整理してください。
High（状態異常・ポーズ破綻・演出競合・境界値バグ）
BackHoldPose.cs:28-34, 47-51, 204-210（状態遷移の割り込み・解除不備）
事実: 腹パン連撃の左右切り替え時の補間用ステート _prevBellyFade が、punchWeight <= 0f && !_brain.KeepsBellyPunchArm の分岐（47-51行目）で 0f にリセットされていますが、拘束が強制解除された場合や、BackHoldPose 自体が enabled = false になった場合、あるいは _brain.Player == null で早期 return した場合に _hasFaceChamber・_hasFaceContactPose・_hasLastBellyPose がリセットされません。特に _hasFaceChamber は w < 0.04f（281行目）を通過しないとリセットされないため、顔面パンチ中にダウンや吹っ飛びで中断され、次の拘束でいきなり w >= 0.04f から始まった場合、前回の拘束時の古い _faceChamberLocal から拳がワープします。
修正提案: OnDisable() や、拘束解除検知時（!_brain.IsHolding かつ !_brain.IsTagPunch 時）に _hasFaceChamber = false; _hasFaceContactPose = false; _hasLastBellyPose = false; _prevBellyFade = 0f; を確実にクリアしてください。
AnkleHoldPose.cs:40-47 と BearHugPose.cs:73-84（カメラ演出の競合・毎フレーム検索）
事実: 両コンポーネントとも [DefaultExecutionOrder(21000)] で毎フレーム _camera.HoldFocus(...) を無条件に呼び出しています。2体の敵が同時に（または連続してフレームを跨いで）異なる拘束状態に入った場合、後から LateUpdate が走った方の HoldFocus でカメラパラメータが上書きされ続けます。また、シーンに ThirdPersonCamera が存在しない場合、毎フレーム FindFirstObjectByType<ThirdPersonCamera>() が走り続けます（GC・CPUスパイク）。
修正提案: カメラ制御の優先度・所有者（Captor）を PlayerGrapple または ThirdPersonCamera 側で調停させてください。また、FindFirstObjectByType は Awake で1回だけ行うか、失敗フラグ _searchedCamera を持って毎フレーム検索を防いでください。
EnemyFigure.Step.cs:1199-1208（タイムスケール・フレームレート境界値）
事実: TickMotion 内で dt > 0f のチェックは Step 冒頭（11行目）にありますが、Time.timeScale を極小値（例: 1e-6f）にした場合、flat.magnitude / dt が非常に大きな値になり _speed がスパイクする可能性があります。また、Step 内で Time.time（176行目 EnemyFigure.Step.cs、41行目 EnemyFigure.Mount.cs、62行目 BearHugPose.cs、25行目 EnemyFigure.AttackPose.cs）を直接参照しているため、ポーズ中（Time.timeScale == 0）やヒットストップ中に Frozen = true で Step(0f, true) を呼ぶと、実時間経過で呼吸や震えが動き続けます。
修正提案: Time.time ではなく dt を積算したローカルタイマー（_animTime += dt）を使用することで、ヒットストップや一時停止時に振動が止まるようにしてください。
EnemyFigure.Mount.cs:146, 153-158（ゼロ除算・特異点での回転フリップ）
事実: Vector3 toGoal = Vector3.Lerp(wrist.position, goal, weight) - shoulder.position; において、toGoal がゼロベクトルに近い場合、toGoal.normalized が Vector3.zero になり、target が shoulder.position と一致して SolveTwoBone に長さ 0 のターゲットが渡ります。また、156行目の Quaternion.LookRotation(bendDirection, -upper) は、bendDirection と -upper が直交している前提ですが、微小誤差や極端な曲げ角で外積が退化すると Unity のコンソールに Look rotation viewing vector is zero 警告が出ます。
修正提案: Vector3 dir = toGoal.sqrMagnitude > 1e-6f ? toGoal.normalized : transform.forward; とし、LookRotation 前にも外積のノルムチェックを入れてください。
EnemyFigure.Step.cs:1002-1015（左右反転ロジックの非対称性・不整合）
事実: BuildWristPose 内で if (Brain != null && !Brain.IsTagFacePunch && Brain.UsesLeftPunch) のときに左右のボーン目標値をスワップしていますが、BuildGrabPose（509-512行目・523-526行目）や BuildHugPinPose（843-858行目）や BuildWallSplitKneePose（899-912行目）で設定した左右非対称な脚・腰の姿勢もまとめて反転されてしまいます。特に WallSplitKneePose では「彼女に近い側の脚（leftKnee）」を距離計算で選んでいるにもかかわらず、UsesLeftPunch が true だと直後の BuildWristPose で脚と HipsX が左右反転され、外側の脚で膝蹴りをして腰が逆方向にずれるバグが発生する恐れがあります（推測：UsesLeftPunch が拘束中の膝蹴りフェーズで true になり得る場合）。
修正提案: 左右反転処理は「パンチ打撃（IsBellyPunch または通常パンチ）」のときだけに限定し、拘束・膝蹴り・掴み姿勢には適用しないように条件を絞ってください。
Medium（保守性・実行順依存・GC/パフォーマンス）
コンポーネントの動的追加と DefaultExecutionOrder の罠 (EnemyFigure.cs:125-140)
事実: EnemyFigure.Awake() 内で gameObject.AddComponent<BackHoldPose>() など8つの Pose コンポーネントを動的に追加しています。実行時に AddComponent されたコンポーネントは、そのフレームの Awake / OnEnable は即座に走りますが、同一オブジェクトに既にアタッチされているスクリプトとの LateUpdate 実行順が、シーンロード直後の1フレーム目だけ不安定になるケースが Unity にはあります。また、毎回 GetComponent を8回呼んでいます。
修正提案: プレハブに最初からアタッチしておくか、EnemyFigure がこれら8つの Pose クラスの更新メソッドを明示的な順序で直接呼び出すオーケストレーター構造にすると、実行順（21000番台）の暗黙の依存が減り、CPUオーバーヘッドも削減できます。
毎フレームの GetComponent 呼び出し（CPU負荷）
事実:
AnkleHoldPose.cs:24: 毎フレーム _brain.Player.GetComponent<PlayerGrapple>() を呼んでいます。
BackHoldPose.cs:82: 毎フレーム _brain.Player.GetComponent<PlayerGrapple>() を呼んでいます。
BearHugPose.cs:29: 毎フレーム _brain.Player.GetComponent<PlayerGrapple>() を呼んでいます。
修正提案: _targetGrapple としてフィールドにキャッシュしてください（EnemyFigure.cs:37 では既に Brain.Hold を参照しているので、それに統一するのが最もクリーンです）。
EnemyFigure.Build.cs:345-380 と EnemyFigure.Primitives.cs:12-65（マテリアルとメッシュの生成コスト）
事実: 敵1体スポーンするごとに GameObject.CreatePrimitive を約40回呼び出し、その都度 Destroy(collider) を行っています。さらに Solid() / Tint() で敵ごとに新規 Material インスタンスを約6個生成しています（_ownedMaterials で OnDestroy 時に破棄しているためメモリリークは防げています）。
推測: 敵が同時に数体であれば問題ありませんが、ウェーブ制などで頻繁に Instantiate / Destroy すると、プリミティブ生成とコライダー破棄でフレームドロップ（スパイク）の原因になります。
修正提案: 共通のカラーバリエーション（Striker / Grappler / Tools）のマテリアルは static 辞書で共有し、敵オブジェクト自体はオブジェクトプールで使い回すことを推奨します。
BackHoldPose.cs:109, 136-137 の未使用パラメータ
事実: float face = _targetAnimator != null ? _targetAnimator.HeldFacePunchFlick : 0f; を取得して Solve(...) に渡していますが、Solve メソッド内で引数 face は1箇所も使われていません（belly, lift, duck, dangle のみ使用）。
修正提案: 意図的に外したのであれば face 引数を削除し、反映漏れであれば goal や pole の計算式に組み込んでください。
マジックナンバーの散在とハードコードされた高さ基準 (EnemyFigure.Mount.cs:34-35, EnemyFigure.Step.cs:89)
事実: EnemyFigure.Build.cs では Height（デフォルト 1.75f）に比例させて骨格サイズを計算していますが、EnemyFigure.Mount.cs:35 の Mathf.Lerp(0.48f, 0.39f, seated) や EnemyFigure.Step.cs:89 の restY - 0.55f はメートル単位の固定値（ハードコード）になっています。
修正提案: Height が異なる敵（大柄・小柄など）を登場させる場合、腰が床にめり込んだり宙に浮いたりするため、Height 比率に置き換えておくと安全です。
Low（可読性・警告抑制・テスト観点）
不要な using ディレクティブの重複
事実: BackHoldPose.cs, BearHugPose.cs, EnemyFigure.AttackPose.cs, EnemyFigure.Build.cs, EnemyFigure.HoldPose.cs, EnemyFigure.Mount.cs, EnemyFigure.Primitives.cs, EnemyFigure.Step.cs のすべてに using System.Collections.Generic; と using AttackId = ...; がコピペされており、多くのファイルで未使用です。
EnemyFigure.Primitives.cs:45 の Sphere メソッド
事実: Sphere ヘルパーが定義されていますが、チャンク内では一度も呼ばれていません（すべて Blob を使用）。
テスト不足の観点（自動テスト推奨項目）
提案: Preview / PreviewStagger / PreviewRise / PreviewDead / PreviewWalkAt という素晴らしい検証用APIが既に用意されています。これを利用し、PlayMode テストまたは EditMode テストで以下を自動検証するテストスイートを作成するとリグレッションを防げます。
全フェーズ・全 GrappleKind 遷移テスト: 任意のタイミング（例: 顔面パンチ Windup 50% や GroundMount 中）で強制的に Stagger や Dead に遷移させた際、1フレーム後にボーン位置・回転に NaN や極端なワープ（1フレームで 0.5m 以上の移動）が発生しないか。
接地テスト: PreviewWalkAt(u, speed) を u = 0.0 〜 0.62 で回した際、接地側の足（Foot(false)）のワールド座標が滑っていないか。
左右パンチ対称性テスト: UsesLeftPunch = true/false それぞれで Step を実行した際、膝蹴りや掴み動作の脚が逆転していないか。
全体の挙動をさらに正確に把握するため、よろしければ次のチャンクのコードも共有いただけますか？特に以下の情報があると、より踏み込んだ検証が可能です：
EnemyBrain と PlayerGrapple の実装（状態遷移とフラグのライフサイクル確認のため）
GroundMountAttack と GroundHaulUp の実装（TargetRig の null 安全性と進行度計算の確認のため）
特定の不具合や違和感（現在実機で発生している関節の跳ねやカメラのブレなど）
必要なファイルや気になる点があればお知らせください。


## answer-03

# Chunk 03

Files:
- MioPrototype/Assets/RyonaGame/Runtime/AI/EnemyGroup.cs
- MioPrototype/Assets/RyonaGame/Runtime/AI/EnemyVrm.cs
- MioPrototype/Assets/RyonaGame/Runtime/AI/HaulUpPose.cs
- MioPrototype/Assets/RyonaGame/Runtime/AI/OverheadHoldPose.cs
- MioPrototype/Assets/RyonaGame/Runtime/AI/PinHoldPose.cs
- MioPrototype/Assets/RyonaGame/Runtime/AI/WallAssistPose.cs
- MioPrototype/Assets/RyonaGame/Runtime/AI/WallKneeStrikePose.cs
- MioPrototype/Assets/RyonaGame/Runtime/AI/WallSplitArm.cs

---

Chunk 03（全8ファイル）のコードレビューでは、計18件の改善点を特定しました。特に、IK計算（腕の角度設定）における回転の上書き順序のバグや、Null参照リスクを優先して修正することを推奨します。 
Critical（致命的な不具合・実行時エラー・演出破綻）
1. EnemyVrm.cs: VRMロード失敗時のオブジェクトリーク（残骸の放置）
箇所: EnemyVrm.cs 128–147行目
事実: Vrm10.LoadPathAsync が成功して vrm != null となった後、Setup(vrm) 内で _rig.Ready == false（254–259行目）により return false した場合、または Setup 内で例外が発生した場合（143–147行目）、生成済みの vrm.gameObject が DisposeVrm されずにシーン上に残ります。
修正提案: Setup が false を返した際や catch ブロック内で DisposeVrm(vrm.gameObject) を呼び出し、_instance = null にクリアしてください。 
2. OverheadHoldPose.cs / WallAssistPose.cs / WallSplitArm.cs: SettleElbowHinge の回転結果の破棄（IK計算のバグ）
箇所:
OverheadHoldPose.cs 166–170行目
WallAssistPose.cs 89–93行目
WallSplitArm.cs 66–70行目、149–153行目 
事実: EnemyFigure.SettleElbowHinge(upper, elbow, wrist) を呼んだ直後、upper.rotation = Quaternion.Slerp(previousUpper, upper.rotation, weight); で upper.rotation を更新しています。elbow は upper の子Transformであるため、親である upper.rotation を後から変更すると、子のワールド回転 elbow.rotation も一緒に回り、SettleElbowHinge が計算した肘のヒンジ合わせがズレます。（WallSplitArm.Pull の99–102行目のように先に aimedUpper と aimedElbow を退避し、親→子の順で代入する必要があります）。 
修正提案: SettleElbowHinge の直後に Quaternion aimedUpper = upper.rotation; Quaternion aimedElbow = elbow.rotation; と両方のワールド回転を退避してから、upper.rotation → elbow.rotation の順で Slerp 代入してください。
3. OverheadHoldPose.cs: ログ出力時の NullReferenceException リスク
箇所: OverheadHoldPose.cs 123–129行目
事実: -overheadlog 有効時、_figure.WristBone(true).position や _targetRig.LLowerArm.position に null チェックなしで直接アクセスしています。43–45行目の事前チェックでは LHand と RHand しか確認していません。
修正提案: ログ出力ブロック内でも ?.position ?? Vector3.zero を使用して安全に座標を取得してください。 
High（高確率で発生する不具合・設計上の欠陥）
4. EnemyVrm.cs: LateUpdate 内のボーン群の Null チェック不足
箇所: EnemyVrm.cs 266–287行目、354–367行目
事実: Setup で _rig.Ready を確認していますが、ProceduralRig.Ready が必須ボーン（Chest, Head, 足の指先など）すべての非 null を保証しているか不明です。Setup 内の _hips.rotation 等（283–287行目）や LateUpdate 内の AimArm / AimLeg でボーンが欠損している VRM を読み込むと NullReferenceException で毎フレーム停止します。
修正提案: Setup の Rest 計算および LateUpdate の CopyRotation 呼び出し前に、各ボーンが null でないことを確認するガードを入れてください。 
5. PinHoldPose.cs: Camera.main 依存と Null 安全性
箇所: PinHoldPose.cs 140–141行目
事実: 毎フレーム Camera.main を検索して左右判定を行っています。MainCamera タグが付いていないカメラ構成や演出カメラ切り替え時に cam が null になると、常に left = false（右膝固定）になり、カメラから見て手前側の膝で蹴る意図が外れます。また、Camera.main の毎フレーム呼び出しは負荷要因にもなります。
修正提案: すでに同クラス内で取得している _camera（ThirdPersonCamera）の Transform を使うか、Start/Awake でキャッシュしたメインカメラを参照してください。 
6. PinHoldPose.cs / OverheadHoldPose.cs / WallAssistPose.cs / WallKneeStrikePose.cs: GetComponent の毎フレーム呼び出し
箇所:
OverheadHoldPose.cs 36行目
PinHoldPose.cs 61行目
WallAssistPose.cs 27行目
WallKneeStrikePose.cs 31行目 
事実: LateUpdate のたびに _brain.Player.GetComponent<PlayerGrapple>() を呼び出しています。敵が複数体いる場合、毎フレーム複数回の GetComponent が走ります。 
修正提案: _playerGrapple フィールドにキャッシュし、_brain.Player が変わったときだけ再取得するようにしてください。
7. EnemyGroup.cs: OnConfigChanged における MaxAttackers の下限未保証
箇所: EnemyGroup.cs 76–85行目
事実: HasFreeSlot（71行目）や TryAcquireAttackToken（133行目）では Mathf.Max(1, MaxAttackers) で保護していますが、Awake や OnConfigChanged で設定値を代入する際にクランプしていません。設定ファイルで 0 以下が指定された場合、インスペクタや外部参照から group.MaxAttackers を直接読む箇所があると不整合が起きます。
修正提案: Awake と OnConfigChanged の代入時に MaxAttackers = Mathf.Max(1, config.Group.MaxAttackers); としてください。 
Medium（状態遷移の競合・境界値・カメラ演出の干渉）
8. PinHoldPose.cs: FindFirstObjectByType<ThirdPersonCamera>() の連続実行リスク
箇所: PinHoldPose.cs 83–84行目、176–177行目
事実: シーン上に ThirdPersonCamera が存在しない場合（テストシーンや別カメラ使用時）、_camera == null が成立し続け、拘束中の毎フレームでシーン全体検索（FindFirstObjectByType）が走ります。
修正提案: 初回検索済みフラグ（bool _cameraSearched）を設けるか、キャッシュ機構を用意して1回だけ検索するようにしてください。 
9. PinHoldPose.cs & OverheadHoldPose.cs: 実行順 [DefaultExecutionOrder(21000)] 内の実行順序不定
箇所: HaulUpPose.cs (11行目), OverheadHoldPose.cs (13行目), PinHoldPose.cs (13行目), WallAssistPose.cs (13行目), WallKneeStrikePose.cs (11行目) 
事実: すべて同じ 21000 の実行順が指定されています。 
推測（仮説）: もし同じ敵オブジェクトに複数の Pose コンポーネント（例: OverheadHoldPose と WallKneeStrikePose）が同時にアタッチされており、かつ両方の発動条件が一瞬でも重なった場合、どちらの LateUpdate が先に走るかは Unity のコンポーネント登録順に依存し、ボーン回転の競合や BraceHoldUpperBody の上書きが起きる可能性があります。
修正提案: 同時に有効になり得るコンポーネント間（例: 腕IK系と脚IK系、またはカメラ制御系）で明確に実行順の数値（例: 21000, 21010, 21020）をずらしてください。
10. WallSplitArm.cs: ワールド座標系 Vector3.right のハードコード
箇所: WallSplitArm.cs 37行目、44行目、90行目、144行目 
事実: Vector3.right * (side * 0.30f) のように、キャラクターや壁の向きではなくワールドの X 軸（Vector3.right）を基準に腕の位置（振りかぶり位置や腰の掴み位置）をオフセットしています。 
推測（仮説）: ステージや壁の向きが Z 軸法線（左右が X 軸）である前提で作られていると推測されますが、斜めの壁や異なる向きのステージが追加された場合、腕が壁にめり込んだり不自然な方向へ突き出したりします。
修正提案: Vector3.Cross(Vector3.up, front).normalized など、ターゲットの正面ベクトル front に直交する水平軸を基準にオフセットを計算してください。
11. OverheadHoldPose.cs & WallAssistPose.cs: Vector3.ProjectOnPlane(...).normalized のゼロベクトルリスク
箇所:
OverheadHoldPose.cs 81行目
WallAssistPose.cs 45行目
WallKneeStrikePose.cs 40行目 
事実: Vector3.ProjectOnPlane(_brain.Player.transform.forward, Vector3.up).normalized を直接呼んでいます。プレイヤーが真上・真下を向く姿勢（吹っ飛びや壁叩きつけの極端な前傾・後傾）になった場合、射影ベクトルがゼロになり front が (0,0,0) になってIKターゲットが中心に潰れます。（PinHoldPose.cs の119–120行目では sqrMagnitude < 1e-4f のフォールバックを実装していますが、他ファイルにはありません）。 
修正提案: sqrMagnitude < 1e-4f の場合にデフォルトの前方ベクトルへフォールバックする共通ヘルパーメソッドを用意してください。
12. EnemyVrm.cs: ConfigurableMonoBehaviour 非継承によるホットリロード非対応
箇所: EnemyVrm.cs 26行目
事実: EnemyGroup は ConfigurableMonoBehaviour を継承して設定変更に対応していますが、EnemyVrm は通常の MonoBehaviour です。また、EnemyVrmBootstrap（502–529行目）がシーンロード時に自動アタッチする設計のため、インスペクタからの調整が効きにくくなっています。
修正提案: 武器の握り位置（WeaponLocalPosition / WeaponLocalEuler）などを調整しやすくするため、必要であれば RyonaConfig 連携またはデバッグ用パラメータ公開を検討してください。 
Low（コード品質・保守性・パフォーマンス改善・テスト不足）
13. OverheadHoldPose.cs / PinHoldPose.cs / WallAssistPose.cs / WallSplitArm.cs: 未使用の using とエイリアス
箇所:
OverheadHoldPose.cs 2–3行目
PinHoldPose.cs 2–3行目
WallAssistPose.cs 2–3行目
WallSplitArm.cs 2–3行目 
事実: using System.Collections.Generic; と using AttackId = Ryona.Game.EnemyBrain.AttackId; が4ファイルすべてに記述されていますが、コード内で一切使用されていません。 
修正提案: 不要な using ディレクティブを削除してください。
14. EnemyGroup.cs: TryAcquireAttackToken 内の二重 Prune() 呼び出し
箇所: EnemyGroup.cs 121行目、128行目
事実: TryAcquireAttackToken の冒頭で Prune() を呼んだ直後に IsBlockedByNearer(brain) を呼び出しており、IsBlockedByNearer 内で other == null をチェックしているため実害はありませんが、同クラスの MemberCount / AttackerCount / HasFreeSlot を連続で読むと毎度リスト走査（Prune()）が走ります。
修正提案: Prune() は Update / LateUpdate で1フレームに1回行うか、変更時のみ実行する設計にすると無駄な走査が減ります。 
15. EnemyGroup.cs: ForceAcquireAttackToken 時の上限超過と待機敵への影響
箇所: EnemyGroup.cs 166–170行目
事実: ForceAcquireAttackToken は Prune() を呼ばずに _attackers.Add(brain) を行います。また、上限（MaxAttackers）を超えてトークンを付与するため、この敵がトークンを返すまで通常攻撃の空きスロットが枯渇します（仕様通りであれば問題ありませんが、トークン返却漏れがあると進行不能になります）。
修正提案: 念のため Prune() を先頭に呼ぶことと、強制取得したトークンが中断・解除時に確実に ReleaseAttackToken されるか結合テストを追加してください。 
16. EnemyVrm.cs: BuildSearchPaths のパス結合の堅牢性
箇所: EnemyVrm.cs 178行目
事実: Path.Combine(Application.streamingAssetsPath, "..", "..", "..", "Assets", "StreamingAssets", fileName) は、特定のビルドディレクトリ階層（例: Build/App_Data/StreamingAssets から3階層上）を前提としています。配布形態やフォルダ構成が変わると意図しない上位ディレクトリを探索します。
修正提案: エディタ実行時（#if UNITY_EDITOR）のみ Application.dataPath を使うように分岐すると、リリースビルドで不要な上位パスを探索しなくなります。 
17. EnemyVrm.cs: DisposeVrm の二重破棄の挙動
箇所: EnemyVrm.cs 484–496行目
事実: gltf.Dispose() の後に if (instance != null) Destroy(instance); を呼んでいます。Unity の Destroy() はフレーム末まで実際のオブジェクトを破棄しないため、Dispose() 内で Destroy(gameObject) が呼ばれていても直後の instance != null は true になり、Destroy(instance) が2回呼ばれます（Unity 仕様上、同一フレーム内の重複 Destroy は無害ですが冗長です）。
修正提案: Dispose() が破棄を担うのであれば else Destroy(instance); にするか、コメントに重複呼び出し許容の旨を明記してください。 
18. 全体（テスト不足）: エッジケースの自動テスト不足
対象: 全ファイル 
事実（推測含む）: コメント内に「実測で確認済み」「WallPinRuntimeTests」などの記載があり実機・結合テストは行われていますが、以下のユニット／境界テストが不足している可能性があります。
VRM ロード中断テスト: EnemyVrm.LoadAsync() の await 完了前にシーン遷移・Destroy が起きた際のリソースリーク有無。
トークン再入場テスト: EnemyGroup で敵が Disable → Enable された後、および ForceAcquireAttackToken 中に拘束が中断された後のトークン回収テスト。
IK 回転順序テスト: WallSplitArm や OverheadHoldPose で weight が 0.5（遷移途中）のときに肘のヒンジ軸が破綻しないかの検証。 
優先的に修正パッチを作成しましょうか？ご希望の場合は、対象とする範囲をお知らせください：
Critical / High のバグ修正コード（IK回転順序の修正・VRMリーク対策・キャッシュ化）
全指摘事項を反映したリファクタリング版
自動テスト（EditMode / PlayMode Tests）の追加コード
あなたがアップロードしたデータ
·
このカードは自分だけが閲覧できます
chunk-03.txt


## answer-04

# Chunk 04

Files:
- MioPrototype/Assets/RyonaGame/Runtime/EnemyBrain.Attack.cs
- MioPrototype/Assets/RyonaGame/Runtime/EnemyBrain.AttackSelection.cs
- MioPrototype/Assets/RyonaGame/Runtime/EnemyBrain.Grapple.cs
- MioPrototype/Assets/RyonaGame/Runtime/EnemyBrain.Group.cs
- MioPrototype/Assets/RyonaGame/Runtime/EnemyBrain.Lifecycle.cs
- MioPrototype/Assets/RyonaGame/Runtime/EnemyBrain.Locomotion.cs
- MioPrototype/Assets/RyonaGame/Runtime/EnemyBrain.Presentation.cs
- MioPrototype/Assets/RyonaGame/Runtime/EnemyBrain.Spacing.cs
- MioPrototype/Assets/RyonaGame/Runtime/EnemyBrain.cs

---

EnemyBrain 関連 9 ファイル（チャンク 04/26）のコードレビュー結果を、重大度別にまとめます。
Critical（進行不能・破壊的バグ）
EnemyBrain.cs (953–961行目) / EnemyBrain.Presentation.cs (8–68行目): 機能消滅とデッドコードの混在
確定事実: EnemyBrain.Awake() 内で Telegraph != null の場合に Destroy(Telegraph.gameObject); Telegraph = null; を実行しています。これにより EnemyBrain.Presentation.cs の ShowTelegraph や ApplyTelegraphVisual は常に早期リターンとなり、一切動作しません。また EnemyBrain.cs のクラスコメント（37行目）にある「Telegraph の拡大と色変化を挟む」仕様と矛盾しています。
推測: 武器発光（EnemyFigure.ApplyWeaponCharge）へ移行した際の消し忘れと推測されますが、外部から Telegraph を実行時に再代入するテスト等がない限り完全なデッドコードです。
修正提案: Presentation.cs 内の Telegraph 関連メソッドと関連フィールド（_telegraphRenderer 等）を削除するか、互換テスト用に残す場合は Awake で破棄しないよう条件分岐を設けてください。
EnemyBrain.Grapple.cs (575–624行目) / EnemyBrain.cs (1262–1274行目): TryWallPin での Hold null チェック不整合と未初期化状態での LateUpdate 実行
確定事実: TryWallPin 内で var grapple = Player != null ? Hold : null; を取得して grapple.BeginWallPin(...) を呼んでいますが、その直前に IsPinning = true; IsWallPinning = true; を立てています。また EnemyBrain.cs の LateUpdate（1264行目）では var wallHold = Hold; を取得して wallHold.WallCaptorTravel を参照しています。万が一 TryWallPin 呼び出し後に Player が null 化された場合、IsWallPinning が true のまま LateUpdate が走り続け、t = 1f で位置が固定され続けます。
修正提案: LateUpdate 内で IsWallPinning が true なのに wallHold == null || !wallHold.IsGrappled || wallHold.Captor != this である場合は、ClearGrappleState() を呼んで強制解除するフェイルセーフを入れてください。
High（ロジック破綻・状態遷移の割り込み不具合・Null参照）
EnemyBrain.Attack.cs (77–91行目): インデント崩れによる論理構造の可読性低下と条件の取り違えリスク
確定事実: 77行目の if (Vector3.Dot(transform.forward, toPlayer.normalized) >= 0.7f) に対し、78行目の if (Role == EnemyRole.Tools && ...) が左端にインデント崩れを起こしており、87行目の else が内側の if にぶら下がっています（内積が 0.7 未満の場合は何もせず grabbed = false になる）。
修正提案: 中括弧 {} を明示し、インデントを正しく整形して条件のスコープを明確にしてください。
EnemyBrain.cs (1049, 1343行目): Player.GroundMount の Null 参照リスク
確定事実: Update()（1049行目）および LateUpdate()（1343行目）にて、Player != null の確認後に Player.GroundMount.Owns(this) を直接呼び出しています。GroundMount が未初期化（null）の状態で Update や LateUpdate に到達すると NullReferenceException でフレーム処理が中断します。
修正提案: Player.GroundMount != null && Player.GroundMount.Owns(this) のように null 条件演算子または明示的な null チェックを追加してください。
EnemyBrain.Lifecycle.cs (151–185行目): ResetForMotionPreview での不完全な状態リセット
確定事実: Revive() では _feinted = false; と _run = default; を行っていますが（126–127行目）、生存中の個体をリセットする ResetForMotionPreview()（153行目 else 分岐）では _feinted、_hasLast、_repeatCount、_grabReadyAt、_evadeActive、_evadeOffset、_evadeLateral がリセットされません。
推測: プレビュー連続再生時に、前回フェイント済みだったり、トドメ回避（_evadeActive）のオフセットが残ったまま次のプレビューが始まり、キャラクターが奥行き（+Z）にずれた位置から再生される不具合が起き得ます。
修正提案: ResetForMotionPreview() 内に _feinted = false; _hasLast = false; _repeatCount = 0; _grabReadyAt = 0f; _evadeActive = false; _evadeOffset = 0f; _evadeTargetOffset = 0f; _evadeLateral = 0f; _evadeTargetLateral = 0f; を追加してください。
EnemyBrain.Grapple.cs (156–185行目): TakeOverHold でのフェイント・攻撃履歴状態の未クリア
確定事実: 引き渡しで拘束を受け持つ TakeOverHold では ClearGrappleState() が呼ばれていません（EndCatch() と IsHolding = true のみ）。直前まで壁押さえつけ（IsWallPinning）やベアハッグ（IsBearHugging）を行っていた個体が引き渡しを受けた場合、それらのフラグが残留する可能性があります。
修正提案: TakeOverHold の冒頭で ClearGrappleState(clearAttackState: true); を呼び出してから IsHolding = true や IsPinning = pin を設定してください。
EnemyBrain.cs (1086–1094行目) / EnemyBrain.AttackSelection.cs (58–93行目): TryApproachForPin と TryFlank の競合と無限歩行リスク
確定事実: CanPin が true の敵が Chase または Strafe にいるとき、毎フレーム TryApproachForPin() が先に評価されます（1086行目）。プレイヤーの正面 1.0m（target）へ向かって歩きますが、プレイヤーが移動し続けたり、他の敵や障害物で正面（ドット積 > 0.9）に回り込めない場合、攻撃トークンを持たないままプレイヤーの正面へ割り込み続け、他の敵の攻撃やカメラ視界を塞ぐ可能性があります。また TryApproachForPin 実行中は KeepSpacing()（1147行目）まで到達せずスキップされるため、プレイヤーや他個体と重なります。
修正提案: TryApproachForPin で移動した直後も KeepSpacing() を通すか、一定時間正面に到達できない場合のタイムアウトを設けてください。
Medium（境界値・仕様不整合・パフォーマンス/GC）
EnemyBrain.Grapple.cs (654–665, 682–693, 844–855行目): 毎回の new AttackData による GC アロケーション
確定事実: DoBearHugSqueeze、DoPinKnee、DoWallSplitPunch が呼ばれるたびに new AttackData { ... } をヒープに確保しています。EnemyBrain.cs（201行目）では GC 回避のために配列を使い回す配慮がなされていますが、ここでは毎回インスタンス生成しています。
修正提案: _jab 等と同様に BuildAttacks() で AttackData インスタンスを事前生成してフィールドに保持し、ダメージ値のみ書き換えて使い回してください。
EnemyBrain.cs (233行目) / EnemyBrain.Attack.cs (235行目) / EnemyBrain.Grapple.cs (215行目): 毎フレーム・毎攻撃の GetComponent 呼び出し
確定事実: PunchTarget（GetComponentInChildren<ProceduralRig>()）、PunchTouchesPlayer（GetComponent<EnemyFigure>()）、AbsorbLungeOffset（GetComponent<EnemyFigure>()）、Die/Revive（GetComponent<Collider>()）で毎回コンポーネント検索を行っています。
修正提案: Awake() 時に EnemyFigure や Collider をキャッシュし、PunchTarget 用の ProceduralRig も PlayerStats 側または EnemyBrain 側でキャッシュしてください。
EnemyBrain.Grapple.cs (709行目): FindFirstObjectByType<GrappleHandOff>() の実行時検索
確定事実: 膝蹴り終了時にシーン全体から GrappleHandOff を検索しています。
修正提案: Start() でキャッシュするか、シングルトン/参照渡しにしてシーン全検索を避けてください。
EnemyBrain.cs (1056–1062行目): _wallAssistHold 中のトークン解除と KeepSpacing スキップ
確定事実: _wallAssistHold != null のとき、1061行目で return するため KeepSpacing() が呼ばれません。また、BeginWallAssist（730行目）でトークンを解放していますが、Assist 中に外部から TryStartPinballPunch 等が呼ばれた場合、_wallAssistHold チェックがないため状態が破壊される恐れがあります。
修正提案: TryStartPinballPunch や TryStartTagPunch のガード条件に IsWallAssisting を追加して割り込みを防止してください。
EnemyBrain.Attack.cs (131行目) / EnemyBrain.cs (409, 457行目): _punchVisualTimer のハードコード値 0.18f の重複
確定事実: Strike で _punchVisualTimer = 0.18f; と代入し、EnemyBrain.cs の PunchPoseWeight（409行目）と TagFaceDrive（457行目）で float elapsed = 0.18f - _punchVisualTimer; とハードコードで引き算しています。もし Strike 側の秒数を変更すると、elapsed が負数になりポーズ補間が破綻します。
修正提案: const float PunchVisualDuration = 0.18f; のように定数化し、3箇所すべてで同じ定数を参照してください。
EnemyBrain.Grapple.cs (213–224行目): AbsorbLungeOffset の移動軸の制約
確定事実: position.x += (transform.forward.x >= 0f ? 1f : -1f) * lunge; と X 軸のみに踏み込み量を吸収しています。ベルトアクション（±X 移動）前提の設計ですが、壁押さえつけ直前や斜め向きでキャンセルされた場合に Z 方向とズレる可能性があります。
推測: 意図的なベルト軸固定仕様（SnapToLaneFacing と同様）と推測されますが、コメントに「X軸（ベルト軸）のみへ移す」旨を明記しておくと保守性が上がります。
Low（テスト不足・軽微な改善点）
EnemyBrain.Attack.cs (195–196行目) / EnemyBrain.cs (310–312, 389行目): 補間関数のマジックナンバーと境界値
確定事実: PunchPoseWeight（389行目）で (WindupProgress - 0.60f) / 0.395f のように 0.40f ではなく 0.395f が使われています。WindupProgress が 1.0f になる直前に 1.0 へ到達させる意図と推測されますが、マジックナンバーが多用されています。
修正提案: 定数化または意図を示すコメントを付与してください。
テストカバレッジの不足領域（提案）
確定事実: コード内のコメントから -pintest, -nofeint, -wristlocktest, -nowallpin などの起動オプション駆動の結合テストが存在することが読み取れますが、以下の境界ケースに対する単体テスト（EditMode / PlayMode）の追加を推奨します。
割り込み遷移テスト: Windup（特に IsPreparingHeldThrow / _wristPullPending / _tagPunchPending）中に TakeHit（怯み・死亡）、BeginWallAssist、LockForHandOff が発生した際、一時フラグ（_pinPending, _tagPunchSwing, _bellyPunchPendingHold）と Group の攻撃トークンが確実にクリア・返却されるかの検証。
3連続攻撃禁止の境界テスト: TrySelectAttack（AttackSelection.cs 11–51行目）において、間合い内に 1 種類の攻撃しか届かない状況で 2 回連続実行した後、3 回目で確実に false を返して Strafe へ移行するかの検証。
壁押さえつけ解放後の復帰テスト: IsWallPinning から ForceStagger や時間切れで解放された直後、_wallReturn によるレーン復帰中に再度 TryBeginAttack や BeginEvade が割り込んだ場合の位置飛びが発生しないかの検証。
このレビュー結果をもとに、次のどちらを進めますか？
修正コードのパッチ作成（優先度の高いバグやGC改善の具体的なコード提示）
単体テスト（NUnit / Unity Test Framework）の実装案（状態遷移の割り込み・トークンリーク検証用テストコード作成）
ご希望があればお知らせください。


## answer-05

# Chunk 05

Files:
- MioPrototype/Assets/RyonaGame/Runtime/GrappleSession.cs
- MioPrototype/Assets/RyonaGame/Runtime/PlayerGrapple.BearHug.cs
- MioPrototype/Assets/RyonaGame/Runtime/PlayerGrapple.Escape.cs
- MioPrototype/Assets/RyonaGame/Runtime/PlayerGrapple.HandOff.cs
- MioPrototype/Assets/RyonaGame/Runtime/PlayerGrapple.Hold.cs
- MioPrototype/Assets/RyonaGame/Runtime/PlayerGrapple.Release.cs
- MioPrototype/Assets/RyonaGame/Runtime/PlayerGrapple.Wall.cs
- MioPrototype/Assets/RyonaGame/Runtime/PlayerGrapple.cs
- MioPrototype/Assets/RyonaGame/Runtime/PlayerWristLock.cs

---

提示された9ファイル（拘束・引き渡し・壁拘束・手首ロック関連）に対するコードレビュー結果です。
確定している事実と推測（外部クラス実装に依存する仮説）を分け、重大度別に網羅的に列挙します。 
Critical（致命的な不具合・クラッシュ・進行不能）
BeginHold で状態変更（DetachOnGrab）をガード条件の前に実行している
場所: PlayerGrapple.Hold.cs:11-21
事実: 20行目の if (IsGrappled || Stats == null || Stats.IsDead) return; より前に、15-16行目で wrist.DetachOnGrab() を呼び出しています。すでに別の敵に掴まれている状態（IsGrappled == true）やプレイヤー死亡時に、別の敵が掴み判定を出すと、新規の掴みは成立しないのに手首拘束だけが解除されてペナルティ猶予タイマー（_penaltyUntil）が消費されます。
修正提案: if (IsGrappled || Stats == null || Stats.IsDead) return; のチェックをメソッドの先頭（11行目より前）に移動してください。 
複数人攻撃中の同時打撃における入力抑制の早期解除
場所: PlayerGrapple.Wall.cs:686-690, PlayerGrapple.Wall.cs:693-703
事実: WallGang では2人が同時に WallGangStrike コルーチンを実行します（556-558行目）。各ストライク終了時の finally ブロックでは strike.Reset() しか呼ばないため問題ありませんが、WallGang 全体が途中で中断した場合、FinishWallSequence() が EndWallGang() を呼び出し _session.SuppressRemaining = 0f とします。しかし、外部（別の演出スクリプト等）から別途 SuppressStruggle(seconds) が呼ばれていた場合でも、強制的に 0f で上書きして入力抑制を消してしまいます。
修正提案: WallGang 専用の抑制フラグを GrappleSession に持たせるか、開始時に設定した値のみをクリアする設計にしてください。 
壁拘束の緊急中断（ダウン・死亡）時に敵の壁アシスト状態が残留するリスク
場所: PlayerGrapple.Wall.cs:357-365, PlayerGrapple.Wall.cs:721-738
事実: WallSequenceSteps がダウンや死亡で yield break した場合、finally で FinishWallSequence() が呼ばれます。この中では EndWallGang()（3人目の解放）は呼ばれますが、EndWallSplit()（2人目 Assist および AssistCandidate の解放）は呼ばれません。プレイヤーがダウン中に掴みが解けず Release() が呼ばれなかった場合（例：ダウン後も拘束が継続する仕様の場合）、Assist が壁アシスト状態のまま固着します。
修正提案: FinishWallSequence() 内でも、シーケンス中断時には EndWallSplit() を呼び出して Assist を確実に解放してください。 
High（状態遷移の不整合・Null参照・演出競合）
BeginWallPin での NullReferenceException の可能性
場所: PlayerGrapple.Wall.cs:19-40
事実: 引数 site を _wallSite に代入した後、39行目で _wallSite.BeginRise(); を null チェックなしで呼び出しています。また、37行目の BeginHold が失敗（例：wrist.InGrace 中で早期 return）した場合でも、28行目で _session.IsWall = true; がすでに立っており、39-40行目で壁シーケンスが開始されてしまいます。結果として「掴まれていない（IsGrappled == false）のに壁押さえつけ状態」という矛盾状態に陥ります。
修正提案: if (site == null) return; を先頭に追加し、BeginHold が成功したか（IsGrappled == true になったか）を確認してから _session.IsWall = true や SetWallStage を実行してください。 
WallSplitRoutine のコルーチン多重起動と未停止
場所: PlayerGrapple.Wall.cs:206, PlayerGrapple.Wall.cs:721-738
事実: StartCoroutine(WallSplitRoutine()) の戻り値（Coroutine 参照）を保持しておらず、EndWallSplit() や EndWallPin() で StopCoroutine していません。IsWallSplitActive が false になればループは抜けますが、同一フレーム内での再入場や状態リセットが起きた場合、古いコルーチンが1フレーム遅れて EndWallSplit() を実行し、新しいセッションの _session を破壊する競合が起きます。
修正提案: _wallSplitSeq フィールドでコルーチンを保持し、EndWallSplit() 時に明示的に StopCoroutine してください。 
壁拘束の段階遷移（TickWallTransition）での Captor Null参照
場所: PlayerGrapple.Wall.cs:119-125
事実: WallPhase == WallStage.Restrain 完了時、123行目で Captor.TakeOverHold(...) を null チェックなしで呼んでいます。Update() の660行目で Captor == null チェックを通過していますが、同フレーム内の TickWallTransition() 実行前に他コンポーネントのイベント等で Captor が破棄されるエッジケースで例外が発生します。
修正提案: Captor?.TakeOverHold(...) と null 条件演算子を使用してください。 
カメラ演出の競合（2体同時打撃時の CombatFeel 重複呼び出し）
場所: PlayerGrapple.Wall.cs:552-563, PlayerGrapple.Wall.cs:652-655
事実: simultaneous[cycle] == true のとき、assist と holder の WallGangStrike が同時スタートします。両者の windup 時間が同じ（または近い）場合、同じフレーム（または数フレーム差）で OnWallGangHit が2回呼ばれ、画面揺れ・ヒットストップ・フラッシュが二重に発動してカメラが過剰に暴れる恐れがあります。
修正提案: 同時攻撃時は片方のヒットストップ/カメラシェイク強度を抑えるか、CombatFeel 側で同一フレームの重複発火をマージする仕組みを入れてください。 
PlayerWristLock.TryAttach におけるスクリーンショット撮影コルーチンの多重起動
場所: PlayerWristLock.cs:89-93
事実: CanAttach の判定（92行目）より前に StartCoroutine(BurstShots()) を呼んでいます（89-90行目）。アタッチに失敗するたびに撮影コルーチンが無限に増殖し、I/O 負荷でフレームレートが激減します。
修正提案: BurstShots の起動は CanAttach を通過し、_locked = true; になった後（99行目付近）に移動してください。 
Medium（境界値・パフォーマンス・設計上の懸念）
毎フレーム/頻繁な FindObjectsByType / FindFirstObjectByType の呼び出し（GC・CPU負荷）
場所: PlayerGrapple.Escape.cs:71, PlayerGrapple.Wall.cs:51, PlayerGrapple.Wall.cs:137, PlayerGrapple.Wall.cs:175, PlayerGrapple.Wall.cs:287, PlayerGrapple.Wall.cs:443, PlayerGrapple.Wall.cs:653, PlayerGrapple.Wall.cs:673
事実: 敵の検索や CombatFeel の取得のために、アクションのたびにシーン全検索（FindObjectsByType / FindFirstObjectByType）を実行しています。敵の数が増えると処理落ちの原因になります。
修正提案: CombatFeel は Awake でキャッシュし、敵一覧は敵マネージャ（レジストリ）のリストを参照するように変更してください。 
HandOff における Shove コルーチンの中断時クリーンアップ漏れ
場所: PlayerGrapple.HandOff.cs:36-43, PlayerGrapple.HandOff.cs:177-188
事実: shove == true のとき、previous と next の両方に LockForHandOff(1.30f) をかけています。しかし、181行目の if (!CanContinueHandOff(next)) yield break; で中断した場合、Release() が呼ばれますが、送り手（previous）の LockForHandOff は解除されず、最大1.3秒間棒立ちになります（next は Release() 内の Captor.EndCatch() で解除される推測）。
修正提案: Shove コルーチン全体を try / finally で囲み、正常終了以外（IsHandingOff が true のまま抜けた場合）は EndShove() と双方のロック解除を必ず実行してください。 
WallSplitRoutine でのプロパティ経由アクセスによる枠の混線リスク
場所: PlayerGrapple.Wall.cs:256-265
事実: WallSplitRoutine では _session.SplitPuncher や _session.SplitPunchAge といった「0番枠固定のショートカットプロパティ」に直接書き込んでいます。将来的に2人同時パンチを WallSplit にも導入しようとした際、ClaimStrike を使わずに0番枠を上書きしてバグになる温床です。
修正提案: WallGangStrike と同様に、_session.ClaimStrike(puncher) を使って SplitStrike インスタンスを取得・操作する形に統一してください。 
WallSplitRoutine の疲労度計算のズレ
場所: PlayerGrapple.Wall.cs:284-288
事実: fatigue を計算した後に _session.SplitHits++ をインクリメントしています。1発目（i == 0）は fatigue = 0、最終6発目（i == 5）でも fatigue = 5 / 6 となり、最大値 1.0 に到達しません。
修正提案: 意図通り「打った後の疲労」を表現するなら、インクリメントを先に行うか (float)(WallSplitHits + 1) / ... としてください。 
PlayerWristLock.Update のテストコードの本番混入
場所: PlayerWristLock.cs:32, PlayerWristLock.cs:67-77
事実: _forceTest 用の FindObjectsByType<EnemyBrain> 検索コードがリリースのランタイムコード（Update）に常駐しています。
修正提案: #if UNITY_EDITOR || DEVELOPMENT_BUILD プリプロセッサディレクティブで囲み、本番ビルドから除外してください。 
Escape() と Release() の重複処理
場所: PlayerGrapple.Escape.cs:9-36, PlayerGrapple.Release.cs:12-53
事実: Escape() と Release() で、EndWallPin(), ClearPinHandOff(), IsGrappled = false, ReleasedAt = Time.time, _session のリセットなどをそれぞれ個別に書いています。片方に新しいリセット処理（例：KneeCount = 0 や PullPulse = 0f）を追加した際、もう片方（Escape() 側には KneeCount = 0 や EndWallSplit() が無い！）への反映漏れが発生しています。
修正提案: 共通の内部リセットメソッド ResetGrappleState() を作成し、Escape() と Release() の双方から呼び出してください。 
Low（可読性・保守性・境界値の微調整）
浮動小数点比較の境界値
場所: PlayerGrapple.Wall.cs:236
事実: while (WallSplitWeight < 0.99f ...) とハードコードされたマジックナンバー 0.99f で完了判定をしています。SmoothStep を使用しているため 0.99f 付近での変化が緩やかになり、フレームレートによっては数フレーム余分に待機します。
修正提案: GrappleSession 側に IsSplitWeightComplete => _splitWeightPhase >= 1f; のようなプロパティを持たせて判定してください。 
定数・マジックナンバーの散在
場所: PlayerGrapple.HandOff.cs:36, PlayerGrapple.Wall.cs:82-88, PlayerGrapple.Wall.cs:505
事実: lockSeconds = 1.30f や壁ステージの各秒数（0.22f, 0.46f 等）、無敵抑制用の 999f などがメソッド内に直書きされています。
修正提案: CombatConstants または RyonaConfig に集約し、パラメータ調整やテストからの参照を容易にしてください。 
GrappleSession のコメントと実装の乖離
場所: GrappleSession.cs:62-63
事実: コメントに「まだ移していないもの: 振りほどきの入力数・制限時間」とありますが、LimitRemaining はすでに GrappleSession に移されています。
修正提案: コメントを最新の状態に更新してください。 
推測（外部クラス実装に依存する確認事項）
仮説A（カメラ制御との競合）: PlayerGrapple.cs:259 の WallSplitCameraPushTarget や WallStage を外部のカメラコントローラが参照している場合、指摘3や指摘5の状態残留が起きると、プレイヤーが解放された後もカメラが壁際にズームしたまま戻らなくなる可能性があります。 
仮説B（EnemyBrain のロック解除漏れ）: PlayerGrapple.HandOff.cs:69 で receiver.LockForHandOff(1.2f) を呼んでいますが、ReceiveFromPin コルーチンが Captor の死亡やプレイヤーのダウンで中断した場合、ClearPinHandOff() は呼ばれますが receiver のロック解除メソッドが呼ばれていません。EnemyBrain 側でタイマーによる自動解除を持っていない場合、敵が1.2秒間フリーズします（Shove の181行目も同様）。 
仮説C（InputReader のマルチフレーム入力）: PlayerGrapple.cs:685 と 741 の2箇所に InputReader.StrugglePressed() の呼び出しが存在します。IsWallTransition が true のときは687行目で return するため二重カウントは防がれていますが、状態遷移の順序が変わると1フレームに2回 Struggle() が呼ばれるリスクがあります。 
テスト不足の指摘（追加すべき単体・結合テストケース）
現在のコード構造から、以下の境界値・割り込みケースに対する自動テスト（PlayMode Test）が不足していると推測されます。
割り込み解除テスト: WallGang（3人がかり）の最中に、別の外部要因で Captor が死亡した場合、および Assist / Third が死亡した場合に、すべての敵の拘束状態が解除されプレイヤーが正常にレーン（LaneZ）へ戻るか。 
Escape時の状態リセット網羅テスト: KneeCount が溜まった状態や WallSplit 中に Escape() で脱出した際、次の拘束時に前回の KneeCount や WallAssist が残っていないか（指摘14の検証）。 
手首ロック中の多重掴み判定テスト: PlayerWristLock.IsLocked == true の状態で、すでに掴まれているプレイヤーに2体目の敵が掴み判定を出した際、手首ロックが不当に解除されないか（指摘1の検証）。 
HandOff中断時の敵AI復帰テスト: Shove（突き飛ばし）または ReceiveFromPin の最中にプレイヤーが死亡・ダウンした際、送り手と受け手の両方の EnemyBrain がロック状態から正常に復帰するか。 
修正の優先度付けや具体的なリファクタリング案を作成できます。ご希望があれば以下を教えてください：
最優先で対処したい領域（バグ修正、パフォーマンス改善、テストコード作成など）
関連する EnemyBrain やカメラ制御側のコード（チャンク番号など）
あなたがアップロードしたデータ
·
このカードは自分だけが閲覧できます
chunk-05.txt


## answer-06

# Chunk 06

Files:
- MioPrototype/Assets/RyonaGame/Runtime/Rig/BodyOverlapProbe.cs
- MioPrototype/Assets/RyonaGame/Runtime/Rig/BodyOverlapWatch.cs
- MioPrototype/Assets/RyonaGame/Runtime/Rig/CharacterAnimator.ApplyPose.cs
- MioPrototype/Assets/RyonaGame/Runtime/Rig/CharacterAnimator.ArmAxes.cs
- MioPrototype/Assets/RyonaGame/Runtime/Rig/CharacterAnimator.Hauled.cs
- MioPrototype/Assets/RyonaGame/Runtime/Rig/CharacterAnimator.HeldPunch.cs
- MioPrototype/Assets/RyonaGame/Runtime/Rig/CharacterAnimator.LateUpdate.cs
- MioPrototype/Assets/RyonaGame/Runtime/Rig/CharacterAnimator.Lifecycle.cs
- MioPrototype/Assets/RyonaGame/Runtime/Rig/CharacterAnimator.MountRoll.cs
- MioPrototype/Assets/RyonaGame/Runtime/Rig/CharacterAnimator.OverheadArms.cs
- MioPrototype/Assets/RyonaGame/Runtime/Rig/CharacterAnimator.Pose.cs
- MioPrototype/Assets/RyonaGame/Runtime/Rig/CharacterAnimator.PoseAttack.cs

---

チャンク06の12ファイルについてコードレビューを実施しました。
指摘事項を重大度（Critical / High / Medium / Low）別に整理し、事実と推測を分けて記載します。
Critical（即座に不具合・クラッシュを引き起こす問題）
1. ApplyHaulDaze の二重適用による意図しない姿勢の上書き
場所: CharacterAnimator.Pose.cs:93-101
事実: 93〜95行目と99〜101行目に、完全に同一の if (_state == State.Grabbed && ...) ApplyHaulDaze(ref pose); が重複して記述されています。ApplyHaulDaze 内部は Vector3.Lerp(..., 0.85f) で現在の値を補間しているため、2回実行されると補間率が 1 - (1 - 0.85)² = 0.9775 となり、本来のブレンド設計（85%）を無視してほぼ固定姿勢へ張り付きます。
推測: マージミスまたはコピペによる重複と思われます。
修正提案: 99〜101行目の重複ブロックを削除してください。
2. FindFirstObjectByType のランタイム呼び出しとキャッシュ不備
場所: CharacterAnimator.Lifecycle.cs:144-145
事実: OnHitReceived 内で _punchDirector == null の場合、FindFirstObjectByType<HeldBellyPunchDirector>() を実行しています。
推測: シーンに HeldBellyPunchDirector が存在しない場合、被弾イベントが発生するたびにシーン全体のコンポーネント検索が走り、ヒット時のフレームドロップ（スパイク）の原因になります。
修正提案: Bind() 時に1度だけ検索してフラグ（_searchedPunchDirector 等）を立てるか、依存性注入・シングルトン参照へ切り替えてください。
High（特定の条件下で論理破綻・null参照・視覚的破綻を起こす問題）
3. ApplyPoseToRig 内のゼロ除算（NaN発生）リスク
場所: CharacterAnimator.ApplyPose.cs:210, 235, 262
事実:
210行目: _stateTime / _downFallSeconds で _downFallSeconds に対するゼロ除算ガードがありません（219行目では Mathf.Max(0.05f, _downFallSeconds) としているにもかかわらず、210行目では生のまま除算しています）。
262行目: _stateTime / _stateDuration で _stateDuration に対するゼロ除算ガードがありません（235行目では Mathf.Max(0.01f, _stateDuration) としています）。
推測: 外部から _downFallSeconds や _stateDuration に 0f が渡された場合、NaN が発生し、Quaternion.AngleAxis や Rig.Hips.SetPositionAndRotation に渡ることでキャラクターのモデルが消失（描画破綻）します。
修正提案: 210行目を Mathf.Max(0.05f, _downFallSeconds)、262行目を Mathf.Max(0.01f, _stateDuration) に統一してください。
4. PlayHeldFacePunch におけるフラグの上書き順序バグ
場所: CharacterAnimator.HeldPunch.cs:22-31
事実: keepGroggy == true の場合、28〜29行目で _heldFaceFinalHit = false; とリセットしていますが、直後の31行目で _heldFaceFinalHit = finalHit; と無条件で上書きしています。
推測: keepGroggy 時に「既存のトドメフラグを消す」意図と「引数の finalHit をセットする」意図が競合しています。仮に keepGroggy: true, finalHit: false で呼ばれた場合は結果的に false になりますが、29行目の代入は完全にデッドコード（無意味）です。
修正提案: 意図が「keepGroggy 時は finalHit を維持する」なら _heldFaceFinalHit |= finalHit; にし、「リセットする」なら29行目を削除して整理してください。
5. 構え反転（左パンチ）時の前後足・かかと上げの非対称性
場所: CharacterAnimator.PoseAttack.cs:230-246, 273-281
事実: ComboStep == 1（左パンチ）のとき、腕と体幹のYaw・HeadYawは左右反転（MirrorX や符号反転）していますが、FootL / FootR の前後位置（StanceFootL / StanceFootR）およびかかと上げ（pose.FootPitchR = heel; のみで FootPitchL は0のまま）は反転していません。
推測: 右構え（左足前・右足後ろ）のまま左ストレートを打つ仕様であれば「右足（後ろ足）のかかとが上がる」のは物理的に正しいですが、フック等の体重移動で左足に重心が乗った際も右足のかかとだけが大きく（44°）浮くため、足元の接地感に違和感が出る可能性があります。
修正提案: 左パンチ時の重心移動に合わせて FootPitchL と FootPitchR の配分を調整するか、仕様として意図通りか確認してください。
6. BodyOverlapWatch における Context 更新の欠落
場所: BodyOverlapWatch.cs:252-257
事実: _worstContext の更新条件が !_worstContext.ContainsKey(key) || (_tally.WorstOverall.HasValue && reading.ExcessMeters >= _tally.WorstOverall.Value.ExcessMeters) となっています。
推測: この条件だと、全ペア中の最悪値（WorstOverall） を更新したときしか、個別ペア（key）のコンテキストが更新されません。つまり、ペアAが 0.5m のめり込みを記録した後に、ペアBが 0.3m（ペアBとしては過去最悪だが、全体最悪 0.5m 未満）を記録した場合、ペアBの WorstReadings の数値は更新されるのに、ログに出る _worstContext[key] は古い（初回発生時の）ステート名のままになります。
修正提案: BodyOverlapTally からペアごとの最悪値を取得するか、Add の戻り値で「そのペアの最悪値が更新されたか」を判定して _worstContext[key] を更新してください。
Medium（エッジケース・状態遷移の整合性・保守性の問題）
7. LateUpdate における Time.deltaTime とローカル変数 dt の混在
場所: CharacterAnimator.LateUpdate.cs:15-23, 150
事実: 15〜23行目でヒットストップ中（HitStop.IsActive）に dt = Time.unscaledDeltaTime; として時間を進める工夫をしていますが、150行目の State.Down からの起き上がり判定だけ Time.deltaTime > 0f を直接参照しています。
推測: 150行目の「ヒットストップ中に勝手に起き上がらないようにする」意図はコメント通りで正しいですが、将来 dt を使うようリファクタリングされた際に意図せず挙動が変わるリスクがあります。また、ポーズ中（HitStop.IsActive == false かつ Time.timeScale == 0）は dt == 0f ですが、78行目以降の Mathf.Exp(-18f * dt) などは計算が走ります。
修正提案: ポーズ中（dt <= 0f）は LateUpdate の早期リターンを検討するか、150行目の条件に !HitStop.IsActive を明示して意図を明確にしてください。
8. ApplyPoseToRig における Stats の null 安全性不統一
場所: CharacterAnimator.ApplyPose.cs:192, 194, 269
事実: 269行目では Stats != null && Stats.GroundMount.MountBlocksGetUp と null チェックを行っていますが、192〜194行目の HauledNow（Stats != null を内部でチェック済み）が true のブロック内では Stats.GroundMount.Haul に直接アクセスしています。現状は HauledNow が Stats != null を保証しているためクラッシュしませんが、条件式が変わると NullReferenceException に直結します。
修正提案: HauledNow から GroundHaulUp を out パラメータで受け取るメソッド（TryGetActiveHaul(out GroundHaulUp haul)）にすると安全です。
9. BodyOverlapWatch でのシーン遷移時のアクター参照残留
場所: BodyOverlapWatch.cs:200-210, 341
事実: SampleInterval（0.05秒）ごとに計測し、ScanInterval（0.5秒）ごとにシーン内のキャラクターを再検索しています。203行目で Root == null チェックはしていますが、敵がプールに戻されたり非アクティブ化されたりした際、_actors.Count >= 2 のままだと最大 0.5秒間、新規スポーンした敵が計測対象から漏れます。
修正提案: 敵のスポーン・破棄イベントと連携するか、パフォーマンス影響が軽微であれば検索間隔を短縮してください。
10. BodyOverlapProbe.BuildCapsules の頭カプセル計算におけるスケール未考慮
場所: BodyOverlapProbe.cs:32-35
事実: skeleton.Head.up * (height * 0.040f) のように、ボーンのローカル軸の向き（up）をそのまま使用しています。
推測: VRM 等のモデルによって Head ボーンのローカル Y 軸（up）が頭頂ではなく顔の前方や横を向いているリグが存在する場合、頭部のカプセルが顔の前に突き出す形になります。
修正提案: Head.up ではなく (skeleton.Head.position - skeleton.Chest.position).normalized を基準にするか、モデルの標準ボーン軸規約を明記してください。
Low（パフォーマンス・GC・コード品質の改善提案）
11. BodyOverlapWatch.RefreshActors での毎回の配列アロケーション（GC発生）
場所: BodyOverlapWatch.cs:170
事実: 0.5f 秒ごとに FindObjectsByType<EnemyFigure>(FindObjectsInactive.Exclude) を呼び出しており、毎回新しい EnemyFigure[] 配列がヒープに確保されます（Editor / Development Build 時）。
修正提案: 診断用コンポーネントではありますが、長時間テストでの GC スパイクを防ぐため、敵の登録・解除を EnemyFigure.OnEnable / OnDisable からリストへ登録するプッシュ型に変更することを推奨します。
12. CharacterAnimator.ApplyPose.cs の pole 変数の再計算
場所: CharacterAnimator.ApplyPose.cs:10, 142-148, 281, 284
事実: 10行目で pole を計算し、142〜148行目でうつ伏せ用の poleNow を作っていますが、281・284行目の Rig.RaiseHeel には poleNow ではなく立ち姿勢用の pole を渡しています。
推測: うつ伏せ・起き上がり中は FootPitchL/R が 0f であるため実害はありませんが、将来ダウン中に足首を動かす仕様が入った際に膝が逆折れする原因になります。
修正提案: 281・284行目も poleNow を渡すように統一してください。
13. マジックナンバーの散在とコメント・コードの乖離
場所: CharacterAnimator.LateUpdate.cs:294, 299, CharacterAnimator.PoseAttack.cs:115
事実:
LateUpdate.cs:294: コメントに「0.045 秒で向き終える」とありますが、実際のコード（299行目）は const float aimSeconds = 0.07f; となっています。
PoseAttack.cs:115: コメントに「左腕（引き手）」とありますが、実際のロジック（204〜216行目）ではその後すぐに tuckUpperL / tuckForeL で上書きされており、116〜119行目の cockUpperL 等はほとんど影響していません。
修正提案: 仕様変更に合わせてコメントを最新の数値・挙動に更新してください。
テスト不足の観点（推奨されるテストケース）
ヒットストップ × 状態割り込みの境界テスト:
HitStop.IsActive == true の間に Stats.Revived や Grapple 拘束が発生した場合に、dt の切り替えと _blend の進行が破綻しないか。
極端なパラメータ（ゼロ・負数）の境界値テスト:
DownFallSeconds や GetUpSeconds、PlayHeldFacePunch(0f) などを渡した際に NaN がボーンの localRotation / position に混入しないか。
ロール（ApplyProneMountRoll）の 90°・180° 通過テスト:
体が真横（90°）や仰向け（180°）を通過するフレームで、KeepRollArmInFront や ArmGoalAboveFloor による手首座標の連続性が保たれ、1フレームのワープ（スパイク）が起きないか。
このチャンクのレビューを踏まえ、次のいずれかを進めることができます：
Critical / High 指摘の修正パッチ作成
他チャンク（リグ本体や戦闘ロジック側）との連携部分の整合性確認
境界値・状態遷移の PlayMode テストコード生成
ご希望の作業があればお知らせください。


## answer-07

# Chunk 07

Files:
- MioPrototype/Assets/RyonaGame/Runtime/Rig/CharacterAnimator.PoseDown.cs
- MioPrototype/Assets/RyonaGame/Runtime/Rig/CharacterAnimator.PoseGrabbed.cs
- MioPrototype/Assets/RyonaGame/Runtime/Rig/CharacterAnimator.PoseStagger.cs
- MioPrototype/Assets/RyonaGame/Runtime/Rig/CharacterAnimator.Preview.cs
- MioPrototype/Assets/RyonaGame/Runtime/Rig/CharacterAnimator.Reaction.cs
- MioPrototype/Assets/RyonaGame/Runtime/Rig/CharacterAnimator.State.cs
- MioPrototype/Assets/RyonaGame/Runtime/Rig/CharacterAnimator.cs
- MioPrototype/Assets/RyonaGame/Runtime/Rig/JointRomMonitor.cs
- MioPrototype/Assets/RyonaGame/Runtime/Rig/JointRomProbe.cs
- MioPrototype/Assets/RyonaGame/Runtime/Rig/JointRomWatch.cs

---

チャンク 07（Rig/ 配下の CharacterAnimator 関連 7 ファイルおよび JointRom* 3 ファイル）のソースコードレビュー結果です。
Critical（致命的・確実に不具合や例外を引き起こすもの）
Grapple 未設定（null）時の NullReferenceException
場所: CharacterAnimator.PoseDown.cs:13
確定事実: ApplyFrontEscapePose 内で Grapple の null チェックを行わずに float time = Grapple.HeldSeconds; にアクセスしている。呼び出し元（PoseGrabbed の 594 行目・778 行目）では Grapple != null を前提とした分岐の後に ApplyFrontEscapePose を呼んでおり、Grapple == null かつ frontWait > 0f には通常ならないが、将来的に ApplyFrontEscapePose が別経路から呼ばれた場合や Grapple が途中で破棄された場合に即座に NullReferenceException となる。
修正提案: if (waiting <= 0f || Grapple == null) return; とガードを追加する。
ゼロ除算（NaN 発生）によるリグ破綻のリスク（_downFallSeconds）
場所: CharacterAnimator.PoseDown.cs:130, CharacterAnimator.State.cs:41-43
確定事実: PoseDown() で float rise = Mathf.Clamp01(_stateTime / _downFallSeconds); を計算しているが、インスペクタで DownFallSeconds に 0 以下が設定された場合、または ForceState(State.Down, ...) が SetState を経由せず直接 _state を書き換えて _downFallSeconds が初期値 0f のまま呼ばれた場合、0 / 0 = NaN（または Infinity）となり、以降の Lerp や Quaternion.AngleAxis がすべて NaN になってキャラクターが消失する（321 行目では Mathf.Max(0.05f, _downFallSeconds) で保護しているが 130 行目は未保護）。
修正提案: float fallSec = Mathf.Max(0.05f, _downFallSeconds); を用いて除算する。ForceState 内でも State.Down 時の初期化を担保する。
_heldBellyPunchTotal によるゼロ除算（NaN 発生）のリスク
場所: CharacterAnimator.PoseGrabbed.cs:391
確定事実: if (recoil > 0f || lift > 0f) のブロック内で hitTime を計算しているが、_heldBellyPunchTotal や _heldBellyHoldSeconds が 0 の場合、InverseLerp(holdAt, holdAt + 0.17f, hitTime) は安全でも、外部から HeldBellyCaptorLift のみ正値で _heldBellyPunchTotal == 0f の状態になると hitTime = 0 固定となり想定外の挙動になる。
修正提案: recoil > 0f の場合のみ hitTime ベースの折れ計算を行うか、_heldBellyPunchTotal > 0f のガードを入れる。
High（状態遷移の破綻・意図しない挙動・大きな品質低下）
ForceState によるプレビュー時の状態初期化漏れ
場所: CharacterAnimator.Preview.cs:40-49, CharacterAnimator.State.cs:22-49
確定事実: ForceState は SetState を呼ばず直接 _state = state; を代入しているため、State.Down をプレビューした際に _downFallSeconds、_downForward、_downLaunched、_downEndLay が初期化されない。直前のプレビュー状態（例えば FinishGroundRoll() 後の _downForward = false や未初期化の _downFallSeconds = 0f）を引き継ぎ、正常にダウン姿勢が再生されない。
修正提案: ForceState 内で SetState(state, duration); を呼び出した後に _stateTime = time; を上書きする（ログ出力やイベント発火が不要なら SetState 内のフラグ初期化部分を共通メソッド化する）。
再入場・連続ダウン時の残留フィールド未リセット
場所: CharacterAnimator.State.cs:28-48, CharacterAnimator.PoseDown.cs:371-373
確定事実: SetState(State.Down, ...) で _downLandedAt や _downLandRaised はリセットされるが、_downEndHipsY と _downEndHipsZ はリセットされない。もしダウン開始直後（PoseDown が 1 回も走る前や特殊な経路）で GetUp へ遷移した場合、前回のダウン終了時の座標がそのまま使われる。
推測: 引きずり起こしや即時復帰（BeginQuickGetUp）がダウン遷移の極初期フレームで割り込んだ場合、起き上がり開始位置が不整合になる可能性がある。
修正提案: SetState(State.Down, ...) 時に _downEndHipsY = StanceHipsY; _downEndHipsZ = StanceHipsZ; で初期化する。
吹っ飛びダウン中の浮遊時間超過による着地判定の不整合
場所: CharacterAnimator.PoseDown.cs:52-56, CharacterAnimator.PoseDown.cs:240-247
確定事実: _downLaunched == true のとき、Motor.IsAirborne が true の間は _downLandedAt < 0f のまま待機し、着地した瞬間に _downLandedAt = _stateTime を記録する。しかし、坂道や段差から落ちて滞空時間が _downFallSeconds（DownFallSeconds * 0.34f）を大きく超えた場合、rise と fall はすでに 1.0 に張り付いているため、空中で PoseDown の後半処理（膝を立てる restT や呼吸 rise > 0.97f）が開始されてしまう。
修正提案: _downLaunched && _downLandedAt < 0f の間は rise（または fall）の上限を 0.85f 程度に抑えるか、着地後に restT と呼吸を開始する条件（DownSinceLanding() >= 0f）を追加する。
JointRomWatch の FindObjectsByType による定期的なアロケーションと走査コスト
場所: JointRomWatch.cs:64-111
確定事実: Editor および DEVELOPMENT_BUILD ではデフォルト有効（57-61 行目）であり、0.5 秒ごとに FindFirstObjectByType<PlayerStats>() と FindObjectsByType<EnemyFigure>(FindObjectsInactive.Exclude) を実行している。特に FindObjectsByType は毎回新しい配列 EnemyFigure[] をヒープにアロケーションするため、プロファイリングや長時間テスト時に GC スパイクの原因となる。また、figures の取得順が変わると 敵{i + 1} の番号が入れ替わる可能性がある。
修正提案: EnemyFigure 側で OnEnable/OnDisable 時に静的リストへ登録・解除するレジストリ方式にするか、検索間隔を伸ばす（またはイベント駆動にする）。
Camera.main の毎フレーム参照によるタグ検索コスト
場所: CharacterAnimator.PoseStagger.cs:149, CharacterAnimator.Reaction.cs:145
確定事実: PoseStagger() 内の faceBlow 分岐で Camera.main を直接呼んでおり、さらに CameraYawToMe() でも Camera.main を呼んでいる（PoseGrabbed 内で 1 フレームに複数回呼ばれる箇所がある）。Unity のバージョンによっては Camera.main は内部で FindGameObjectsWithTag("MainCamera") を走るため、毎フレーム複数回の呼び出しは CPU 負荷になる。
修正提案: CharacterAnimator にキャッシュ用の Camera _cachedMainCam; を持ち、null または !isActiveAndEnabled のときだけ再取得する。
Medium（設計上の懸念・保守性・エッジケース）
PoseGrabbed の巨大化と責務の過密（単一メソッド 750 行超）
場所: CharacterAnimator.PoseGrabbed.cs:25-780
確定事実: PoseGrabbed() メソッド単体で 755 行あり、ハンドオフ、壁遷移、足取り、羽交い締め（グロッキー・引き起こし・腹パン・顔パン）、ベアハッグ、押さえつけ（膝蹴り・壁押さえ・3人がかり・2体がかり）のすべてが1つのメソッドに直列に書かれている。変数のスコープが広く、途中での return pose; も混在しているため、後半の共通処理（ApplyFrontEscapePose など）がどのパターンに適用されるか追跡しづらい。
修正提案: GrappleKind や状態ごとに PoseGrabbedBack(), PoseGrabbedBearHug(), PoseGrabbedPin(), PoseGrabbedWallTransition() などのプライベートメソッドへ分割する。
フレームレート依存の減衰・補間処理（Time.deltaTime の直接乗算）
場所: CharacterAnimator.PoseDown.cs:243
確定事実: _airBlend = Mathf.MoveTowards(_airBlend, air, Time.deltaTime * 4f); において、他箇所（Reaction.cs:100 等）では Mathf.Max(0f, dt) や _stateTime ベースで計算しているのに対し、ここは Time.deltaTime を直接使っている。プレビュー等で ForceState を呼んだ際やポーズ中（timeScale == 0）に意図しない挙動になる可能性がある。
修正提案: LateUpdate から渡される dt を使用するか、_stateTime ベースの減衰に統一する。
JointRomProbe における Vector3.up との平行時のフォールバック不整合
場所: JointRomProbe.cs:331-333
確定事実: BendAngles で swingAxis を Vector3.up に直交する平面へ射影しているが、もし swingAxis がほぼ Vector3.up と平行だった場合（純粋なヨー回転成分が残っていた場合）、axis.sqrMagnitude < 1e-8f で (0f, yaw, 0f) を返す。直前の 323-325 行目で Vector3.up まわりの twist を既に抜き出しているため、理論上 swingAxis は Vector3.up と直交するはずだが、浮動小数点誤差で swingDegrees が極小のときに早期リターンするのは妥当。ただし swingDegrees が 180f 付近のとき ToAngleAxis の数値不安定性がある。
修正提案: if (swingDegrees < 1e-4f) の早期リターンを 328 行目の前に追加し、微小回転時の軸の暴れを防ぐ。
イベント購読・解除の安全性（BodyLanded）
場所: CharacterAnimator.PoseDown.cs:36, 65-69
確定事実: internal event System.Action<float> BodyLanded; は内部イベントだが、BodyLanded?.Invoke(...) 時に購読側が例外を投げると、以降の姿勢計算（PoseDown の残り）が中断し、そのフレームのボーン姿勢が中途半端な状態で止まる。
修正提案: 呼び出し順序として PoseDown の計算完了後に通知するか、try-catch で保護する（または状態を先に確定させてから発火する）。
Low（軽微な改善・可読性・テスト容易性）
重複したカメラ方向計算ロジック
場所: CharacterAnimator.PoseStagger.cs:148-156, CharacterAnimator.Reaction.cs:143-156
確定事実: Reaction.cs に CameraYawToMe() が実装されているにもかかわらず、PoseStagger.cs 内にほぼ同じ計算（クランプ値のみ ±75f と ±80f で異なる）がインラインで重複記述されている。
修正提案: CameraYawToMe(float maxAngle = 80f) のように引数化し、PoseStagger.cs からも共通メソッドを呼ぶ。
Time.time の直接参照によるプレビュー再現性の低下
場所: CharacterAnimator.PoseGrabbed.cs:211, 216, 261, 265, 327, 328, 338, 374, 456, 551, 558, 562, 563, 567, 577, 578, 605, 610, 614, 693, 694
確定事実: 拘束中の呼吸・震え・揺れの周期計算に _stateTime ではなく Time.time を使用している。そのため、ForceState で任意の時刻 time を指定して静止画プレビューを撮影する際、アプリケーション起動からの経過時間によって揺れの位相が毎回変わり、決定論的なテスト（スナップショット比較）がしづらい。
修正提案: ForceState によるプレビュー時やアニメーション時間基準で再現性を持たせるため、_stateTime を基準にするか、プレビュー用の時間オフセットを足せるようにする。
JointRomMonitor の StringBuilder アロケーション
場所: JointRomMonitor.cs:184
確定事実: LogSummaryNow() が呼ばれるたびに new System.Text.StringBuilder(160) を生成している（デバッグログ出力時のみなので実害は小さい）。
修正提案: _summaryBuffer と同様にフィールドとして readonly StringBuilder _sb = new StringBuilder(160); を再利用する。
テスト不足の観点（推奨されるユニット/PlayModeテスト）
境界値・ゼロ除算テスト: DownFallSeconds = 0f や _stateDuration = 0f を設定した状態で ForceState(State.Down, 0f) および ForceState(State.GetUp, 0f) を呼び出し、出力される PoseValues の全フィールド（HipsOffset, TorsoPitch, HeadYaw 等）に float.IsNaN や float.IsInfinity が含まれないことを検証するテスト。
ダウン→起き上がり→再ダウンの連続遷移テスト: 仰向けダウン・うつ伏せダウン・吹っ飛びダウンのそれぞれから、完全着地前および着地後に GetUp へ遷移し、再度 Down へ割り込んだ際、_downEndHipsY/Z や _downLandRaised が正しくリセットされ BodyLanded が1ダウンにつき1回だけ発火することの検証。
JointRomProbe のジンバルロック・特異点テスト: BendAngles に対して前屈 90°・180° およびヨー回転 180° を組み合わせたクォータニオンを渡し、期待通りの (pitch, yaw, roll) が返ること（軸が反転・爆発しないこと）の EditMode テスト。
このチャンクのレビューに関連して、次にどの作業を進めましょうか？
修正パッチの作成（Critical / High のゼロ除算・nullガード・ForceState 初期化など）
PoseGrabbed のリファクタリング案（状態・拘束種別ごとのメソッド分割）
EditMode / PlayMode テストコードの作成（JointRomProbe 計算検証・状態遷移境界値テスト）


## answer-08

# Chunk 08

Files:
- MioPrototype/Assets/RyonaGame/Runtime/Rig/ProceduralRig.cs

---

MioPrototype/Assets/RyonaGame/Runtime/Rig/ProceduralRig.cs のコードレビュー結果を重大度別にまとめます。
Critical（致命的・クラッシュや進行不能に直結）
右脚ボーンの null チェック漏れによる起動誤認と NullReferenceException
箇所: MioPrototype/Assets/RyonaGame/Runtime/Rig/ProceduralRig.cs:101, 133
事実: 101行目で Ready = Hips != null && LUpperLeg != null && LFoot != null; と左脚しか検証していないにもかかわらず、133行目で RestFootR = transform.InverseTransformPoint(RFoot.position); と RFoot に無条件アクセスしている。また、LLowerLeg や RLowerLeg の null も未検証である。
推測: 片脚欠損リグや特殊なセットアップのモデルを読み込んだ際、Ready == true のまま 133行目で即座に NullReferenceException が発生する。
修正提案: Ready の判定条件に LLowerLeg != null && RUpperLeg != null && RLowerLeg != null && RFoot != null をすべて含める。 
_animator が null または非 Humanoid の場合の NullReferenceException
箇所: MioPrototype/Assets/RyonaGame/Runtime/Rig/ProceduralRig.cs:737, 764-768, 860, 877-879, 904
事実: Capture() 内の Bone() は _animator == null でもボーン名フォールバックで Hips や LFoot を取得できるため Ready = true になり得る。しかし、その後に呼ばれる CaptureFingers() (737行目) や CaptureFingerAxis() (764行目〜) では _animator の null チェックや _animator.isHuman チェックを行わずに _animator.GetBoneTransform(...) を直接呼び出している。
推測: Animator コンポーネントを持たない階層や Generic リグに ProceduralRig をアタッチすると、Capture() 内の指キャプチャ処理でクラッシュする。
修正提案: CaptureFingers() および CaptureFingerAxis() の先頭に if (_animator == null || !_animator.isHuman) return; を追加する。 
High（高・姿勢破綻や状態不整合を引き起こす不具合）
ResetPose() で指ボーンとつま先ボーンが初期化されない（姿勢の残留・累積汚染）
箇所: MioPrototype/Assets/RyonaGame/Runtime/Rig/ProceduralRig.cs:188-219
事実: All() に LToe、RToe、および全指ボーンが含まれておらず、_rest にも登録されていない（指は _fingerRest に別途保存されている）。そのため ResetPose() を呼んでも指の回転（特に AdductFingers の乗算回転や親指の Slerp 回転）が初期姿勢に戻らない。
推測: フレームごとに ResetPose() → ポーズ構築を行う設計において、CurlFingers の呼び出しが止まった状態遷移（攻撃終了→待機など）でも指が握られたまま残る、あるいは AdductFingers の prox.localRotation = delta * prox.localRotation がフレームを跨いで累積し指がねじれる原因になる。
修正提案: ResetPose() 内で _fingerRest の全要素および LToe・RToe も初期値へ復元する。 
SetArm() 内の null チェック欠如による NullReferenceException
箇所: MioPrototype/Assets/RyonaGame/Runtime/Rig/ProceduralRig.cs:419-438
事実: Ready 判定では腕ボーン（LUpperArm, LLowerArm, LHand 等）を必須としていないにもかかわらず、SetArm() では null チェックなしで hand.position や lower.position にアクセスしている（434行目）。
推測: 腕ボーンの一部が欠けたモデルで SetArm() が呼ばれると例外が発生する。
修正提案: SetArm() の先頭に if (upper == null || lower == null || hand == null) return; を追加する。 
T ポーズ以外の初期姿勢（A ポーズ等）での Capture() による軸計算の破綻
箇所: MioPrototype/Assets/RyonaGame/Runtime/Rig/ProceduralRig.cs:73-141, 783-786, 1038
事実: Capture() は現在の transform 姿勢を T ポーズ（基準姿勢）とみなして _rest や掌法線（下向き前提：785行目）、脚の屈曲軸（1038行目）を計算する。しかし、実行中やアニメーション再生途中に外部から Capture() が再入場（再呼び出し）された場合や、モデルが A ポーズ・座り姿勢で初期化された場合、基準軸がすべて狂う。
推測: 状態リセットやモデル差し替え時に Capture() を呼ぶ際、事前に T ポーズへ戻す保証がないと、肘や指が逆方向に曲がる不具合が再発する。
修正提案: Capture() が呼ばれる前提姿勢（初期バインドポーズ）の検証ログを入れるか、再キャプチャ前にバインドポーズへ戻す導線を設ける。 
Medium（中・エッジケースでの挙動不審やパフォーマンス問題）
SetArm() の handRollDegrees 回転軸が Aim() 後の位置に依存する順序問題と、手首のねじれ超過
箇所: MioPrototype/Assets/RyonaGame/Runtime/Rig/ProceduralRig.cs:432-437
事実: 428行目で Aim(lower, hand, forearmDirection) を呼んだ後、434行目で Vector3 axis = hand.position - lower.position; を回転軸として hand.rotation を回している。hand の回転を変えても hand.position 自体は変わらないが、AlignPalm() と併用した場合、ローカル基準のロールではなくワールド軸乗算であるため、前腕の向きによって手首のねじれ方向が意図とずれるケースがある。
推測: 腕を真上・真下に上げた際にロールの基準が曖昧になり、手首が不自然にねじれる可能性がある。
修正提案: forearmDirection.normalized を直接回転軸に使う（hand.position - lower.position の再計算を省き、ゼロベクトルリスクも減らす）。 
AdductFingers() および CaptureFingerAxis() での毎フレーム配列アロケーション（GC 発生）
箇所: MioPrototype/Assets/RyonaGame/Runtime/Rig/ProceduralRig.cs:552-557, 733, 826-831
事実: AdductFingers() は CurlFingers() 経由で毎フレーム呼ばれる可能性があるメソッドだが、552行目で var targets = new[] { ... }; とタプル配列を毎回ヒープに確保している。また、CaptureFingers() でも配列リテラルを生成している。
推測: アクションゲームで複数キャラクターの指を毎フレーム制御すると、不要な GC Alloc が積み重なりスパイクの原因になる。
修正提案: targets 配列を static readonly (int start, float degrees)[] AdductTargets としてクラスフィールドにキャッシュする。 
AdductThumb() 内の thumbBones 配列長チェック不足
箇所: MioPrototype/Assets/RyonaGame/Runtime/Rig/ProceduralRig.cs:511-513
事実: CaptureFingerAxis では thumbBones.Length < 3 をチェックしているが、AdductThumb ではチェックせずに thumbBones[2] にアクセスしている。
推測: 親指ボーン配列を外部から変更した際に IndexOutOfRangeException になる可能性がある。
修正提案: if (thumbBones == null || thumbBones.Length < 3) return; を追加する。 
単体テストが存在しない（数学・IK・軸計算の検証不足）
箇所: MioPrototype/Assets/RyonaGame/Runtime/Rig/ProceduralRig.cs 全体
事実: SolveTwoBone（余弦定理・縮退フォールバック）、PickFlexionSign、ReflectRightWristToLeft など、純粋な幾何計算ロジックが多いにもかかわらず、EditMode テスト等で境界値（rawDistance == 0、l1 + l2 超過、真上・真下方向での Cross 退化）が自動検証されていない。
推測: リファクタリングや定数調整（reachMargin = 0.02f 等）を行った際、特定ポーズでのみ関節がフリップするデグレードに気づきにくい。
修正提案: ダミーの Transform 階層を組んだ Unity Test Framework (EditMode) を作成し、SolveTwoBone の到達精度と縮退時の連続性をテストする。 
Low（低・可読性・設計上の改善点）
デバッグログの無条件出力によるフレームレート・I/O 負荷
箇所: MioPrototype/Assets/RyonaGame/Runtime/Rig/ProceduralRig.cs:170, 843, 846
事実: Capture() のたびに GameLog.Log(...) が左右・各指分（計10回近く）出力される。
推測: キャラクターのスポーンやリセットが頻繁に起きる場面ではログ出力がボトルネックになる。
修正提案: [Conditional("UNITY_EDITOR")] 属性を付けたログメソッド経由にするか、詳細ログフラグで制御する。 
未使用フィールド _palmNormalL / _palmNormalR の存在
箇所: MioPrototype/Assets/RyonaGame/Runtime/Rig/ProceduralRig.cs:633-634, 728-729, 790, 795
事実: _palmNormalL と _palmNormalR は代入とクリアが行われるのみで、クラス内のどこからも読み取られていない（実際には _palmLocalL / _palmLocalR が使われている）。
修正提案: _palmNormalL と _palmNormalR を削除し、コードを簡潔にする。 
Bone() メソッドのフォールバック検索コストと大文字小文字依存
箇所: MioPrototype/Assets/RyonaGame/Runtime/Rig/ProceduralRig.cs:1111-1127
事実: Bone() が呼ばれるたびに GetComponentsInChildren<Transform>(true) を全走査し、t.name == fallbackName で完全一致比較している。Capture() 内で19回呼ばれるため、ボーン数の多いモデルでは初期化時に 19回の全階層走査が走る。
修正提案: Capture() の冒頭で一度だけ Dictionary<string, Transform> に子孫 Transform をキャッシュし、フォールバック時は辞書引きする。 
このクラスの呼び出し元（ポーズ制御・ステートマシン側）も含めてレビューできますが、以下を教えていただけますか？
優先して解決したい問題（特定の関節破綻、パフォーマンス、null安全性の強化など）
関連チャンクの有無（ProceduralRig を毎フレーム操作しているコントローラ側のファイル）
あなたがアップロードしたデータ
·
このカードは自分だけが閲覧できます
chunk-08.txt


## answer-09

# Chunk 09

Files:
- MioPrototype/Assets/RyonaGame/Runtime/Feel/CameraDirector.cs
- MioPrototype/Assets/RyonaGame/Runtime/Feel/CameraShake.cs
- MioPrototype/Assets/RyonaGame/Runtime/Feel/CombatFeel.cs
- MioPrototype/Assets/RyonaGame/Runtime/Feel/EnemyReaction.cs
- MioPrototype/Assets/RyonaGame/Runtime/Feel/FaceVisibility.cs
- MioPrototype/Assets/RyonaGame/Runtime/Feel/FootstepSfx.cs
- MioPrototype/Assets/RyonaGame/Runtime/Feel/GrappleHandOff.cs
- MioPrototype/Assets/RyonaGame/Runtime/Feel/GroundHaulUp.cs
- MioPrototype/Assets/RyonaGame/Runtime/Feel/GroundMountAttack.cs
- MioPrototype/Assets/RyonaGame/Runtime/Feel/HeldBellyPunchDirector.Attack.cs
- MioPrototype/Assets/RyonaGame/Runtime/Feel/HeldBellyPunchDirector.Camera.cs
- MioPrototype/Assets/RyonaGame/Runtime/Feel/HeldBellyPunchDirector.HandOff.cs

---

チャンク 09（Feel 配下 12 ファイル）のレビュー結果です。
Critical
1. 無効化時に進行中の引き起こし状態が残り、プレイヤーが永久に起き上がれなくなるリスク
ファイル・行: GroundHaulUp.cs:253-267, GroundMountAttack.cs:216
事実: GroundMountAttack.OnDisable() は End() を呼んでリセットしていますが、GroundHaulUp には OnDisable() が実装されていません。また、GroundHaulUp.Update() は Attacker == null の場合のみ早期 return し、コンポーネント無効化（enabled = false）時は Update() 自体が止まるため Attacker と Stage が残り続けます。
推測: 他システムから GroundHaulUp.BlocksGetUp / PosesHer / GroundMountAttack.Owns() が参照され続けた場合、プレイヤーの起き上がり不能や敵 AI の永続停止（Owns が常に true を返す）に直結します。
修正提案: GroundHaulUp に void OnDisable() => End(false); を追加し、無効化時に即座に状態を解放する。
2. 拘束解除・死亡・ダウン割り込み時に攻撃コルーチンと敵のロックが残留する
ファイル・行: HeldBellyPunchDirector.Attack.cs:150-153, 312-364
事実: PlaceThenAttack コルーチン内で CanContinuePlacement が false になると yield break で抜けますが、直前（150行目）で呼んだ best.LockForHandOff(..., releaseToken: false) の解除処理を呼んでいません。
推測: コンボ中にプレイヤーが死亡・拘束脱出・ダウン移行した場合、攻撃役だった敵が指定時間（最大 0.9 秒）ロックされたままになる、あるいは予約した攻撃トークンが解放されず他の敵が攻撃できなくなる恐れがあります。
修正提案: yield break する前に enemy.UnlockHandOff()（またはトークン解放用メソッド）を必ず呼ぶ try / finally 構造にする。
3. FindObjectsByType による毎フレーム全探索と GC アロケーション（スパイク要因）
ファイル・行: HeldBellyPunchDirector.HandOff.cs:74, 90, GroundHaulUp.cs:585-586, GroundMountAttack.cs:311, GrappleHandOff.cs:115, 196
事実: 特に SuppressBystanders（HeldBellyPunchDirector.HandOff.cs:90）は連撃〜トドメの間「毎フレーム」呼ばれており、その中で FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None) を実行しています。Unity の FindObjectsByType は毎回新しい配列をヒープに確保します。
推測: 敵数が多いシーンやモバイル/WebGL環境では、毎フレームの配列確保による GC スパイクと CPU 負荷の増大を引き起こします。
修正提案: EnemyBrain 側に static readonly List<EnemyBrain> ActiveEnemies のような静的レジストリを持たせ、OnEnable / OnDisable で登録・解除してゼロアロケーションで走査する。
High
4. Camera.main が未設定（null）の場合にカメラの揺れが完全に失われる
ファイル・行: CombatFeel.cs:170-202, CameraDirector.cs:271-276
事実: CombatFeel.ResolveCameraRig() と CameraDirector.ResolveReferences() は Camera.main のみに依存しており、HeldBellyPunchDirector.Camera.cs:145 や GroundHaulUp.cs:528 と異なり FindFirstObjectByType<ThirdPersonCamera>() へのフォールバックがありません。
推測: シーン遷移直後やカメラに MainCamera タグが付いていない構成・テスト環境において、ヒットストップと SE だけ鳴ってカメラ演出・シェイクが一切発火しない無音・無反応状態になります。
修正提案: Camera.main が null の場合は FindFirstObjectByType<ThirdPersonCamera>() にフォールバックする共通ヘルパーに統一する。
5. HeldBellyPunchDirector.Camera.cs における _stats の null 参照例外リスク
ファイル・行: HeldBellyPunchDirector.Camera.cs:44, 48
事実: FacingSign プロパティ（44行目）と FocusX プロパティ（48行目）が、null チェックなしで _stats.transform に直接アクセスしています。ResolveCamera() では _stats != null をチェックしていますが、RefreshCamera 等を呼ぶ前やプロパティ単体参照時に _stats が未解決だと NullReferenceException が発生します。
修正提案: FacingSign => _stats != null && _stats.transform.forward.x < -0.1f ? -1f : 1f; のようにガードを入れる。
6. シーン再読み込み時に GroundHaulUp.Forced（テストフラグ）がリセットされない
ファイル・行: GroundHaulUp.cs:42, 181-185
事実: Forced は static bool であり、[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)] で初期化されています。同ファイル内の FootstepSfx.cs:33-42 や GrappleHandOff.cs:38-49 にはコメントで「AfterSceneLoad は最初のシーンだけなので sceneLoaded で再確認する」と明記されていますが、GroundHaulUp には sceneLoaded フックがありません。
推測: エディタ実行時（Domain Reload 無効設定）やテストランナーで複数テストを連続実行した際、前の実行の Forced = true が残留し、以降の通常プレイテストで常に引き起こしが暴発します。
修正提案: BeforeSceneLoad でリセットするか、LaunchOptions.Has("-haultest") を直接評価する（または sceneLoaded で更新する）。
7. EnemyReaction の OnEnable でイベント二重購読になる可能性
ファイル・行: EnemyReaction.cs:67-72
事実: OnEnable() で Brain.Hit += OnHit; を行っていますが、購読前の Brain.Hit -= OnHit; がありません（CameraDirector.cs:170 では Subscribe() の先頭で Unsubscribe() を呼んで対策済み）。
推測: オブジェクト生成直後や非アクティブ状態からの復帰パスで OnDisable を経ずに OnEnable が走った場合、1 回の被弾で OnHit が 2 回発火し、ノックバック距離とフラッシュが倍加します。
修正提案: OnEnable() 内の Brain.Hit += OnHit; の直前に Brain.Hit -= OnHit; を入れる。
Medium
8. CameraDirector が拘束中（IsHeldPunchCameraActive）の被弾では引かないが、プレイヤーの攻撃ヒット（OnHitLanded）ではズームインしてしまう
ファイル・行: CameraDirector.cs:77-82, 196-207, 218-221
事実: OnHitReceived と OnStaggered には IsHeldPunchCameraActive() による抑制ガードがありますが、OnHitLanded（196行目）にはありません。
推測: 通常は拘束中にプレイヤーは攻撃できませんが、デバッグコマンドや状態異常解除の瞬間に攻撃判定が残っていると、HeldBellyPunchDirector の固定構図に対して PushDistance(0.90f, 0.20f) が競合してカメラがガクつきます。
修正提案: OnHitLanded の先頭にも if (Rig == null || IsHeldPunchCameraActive()) return; を追加する。
9. EnemyReaction が実行時にマテリアルを複製し、破棄時にリークする
ファイル・行: EnemyReaction.cs:222-224
事実: EnsureEmission() 内で var mat = renderer.material; にアクセスして _EMISSION キーワードを有効化しています。Unity で renderer.material を参照すると共有マテリアルの複製（インスタンス化）が生成され、OnDestroy() で明示的に Destroy しない限りシーン破棄までメモリに残り続けます。
推測: 敵が大量にスポーン・デスポーンするウェーブ戦では、マテリアルインスタンスが蓄積してメモリリークおよびドローコール増加（バッチング阻害）を招きます。
修正提案: プレハブ側のマテリアルで予め _EMISSION を有効化しておくか、生成したマテリアルインスタンスを配列に保持して OnDestroy() で Destroy する。
10. GroundHaulUp.TickHaul でカメラの注視点が敵の HeadBone に null チェックなしでアクセスしている
ファイル・行: GroundHaulUp.cs:539-540
事実: _figure != null ? _figure.HeadBone.position - Vector3.up * 0.35f : ... と記述されており、_figure 自体は null チェックしていますが _figure.HeadBone が null の場合（リグ未構築の敵や VRM 差し替え直後）に NullReferenceException になります（148行目の EnemyShoulder では s != null をチェックしているのに対し不統一）。
修正提案: _figure != null && _figure.HeadBone != null ? ... に修正する。
11. GroundMountAttack の FaceProbe ダミーカメラがエディタ終了やシーン遷移で残る可能性とレイヤー設定
ファイル・行: GroundMountAttack.cs:73-77, 719-741
事実: FaceProbe() で new GameObject("Mount Face Probe") を生成し、OnDestroy() で破棄しています。しかし、GameObject に HideFlags.HideAndDontSave が付いていないため、ヒエラルキー上に露出します。また、コンポーネントが Destroy されずに無効化（OnDisable）されたまま放置されると、シーン上にダミーカメラが生き残ります。
修正提案: go.hideFlags = HideFlags.HideAndDontSave; を設定し、ヒエラルキーの汚染と誤操作を防ぐ。
12. GroundMountAttack の _faceFigures キャッシュに破棄済みオブジェクト（Unity 擬似 null）が混入する可能性
ファイル・行: GroundMountAttack.cs:555-559
事実: _faceFigures は 0.25f 秒ごとにしか更新されません。その 0.25 秒の間に敵が倒されて Destroy された場合、FaceVisibility.Measure（FaceVisibility.cs:132）や LensClearance（FaceVisibility.cs:259）側で enemy == null チェックを行っているためクラッシュはしませんが、ScreenFaceSize や他の呼び出し元が増えた際に危険です。
事実（確定）: FaceVisibility.cs:132, 259 では enemy == null が正しくガードされています。現状は安全ですが、配列サイズ分の空ループが 0.25 秒間走ります。
Low
13. GroundHaulUp の未使用コード・デッドコード（Beat.Release と PullBack）
ファイル・行: GroundHaulUp.cs:29, 36, 278-279, 295, 408-422
事実: TickHaul（404-405行目）は完了時に Beat.Release を飛ばして直接 BeginStage(Beat.Settle, SettleSeconds) へ遷移しています。そのため Beat.Release 状態へ遷移するパスがコード内のどこにも存在せず、TickRelease（408-422行目）と ReleaseSeconds（29行目）はデッドコードになっています。また PullBack = 0.0f（36行目）のため、395行目の Spot(PullBack * ...) は常に Spot(0f) と同値です。
修正提案: 仕様として Release 段階が不要であれば削除して状態遷移を簡潔にする（将来使う予定があるならコメントで明記する）。
14. GroundMountAttack.FindAttacker のフォーマット乱れと MoveSpeed チェックの重複
ファイル・行: GroundMountAttack.cs:313, 337
事実: 337行目に _side =Vector3.Dot(...) とスペース抜けがあります。また、FindAttacker（313行目）では enemy.MoveSpeed <= 0f のみチェックしていますが、実際の移動速度計算（269行目）では Attacker.MoveSpeed * Attacker.State.MoveSpeedMultiplier を使っており、MoveSpeedMultiplier <= 0f の敵（鈍足・凍結状態など）を選んで即座に End() して 0.2 秒間探索を無駄にするケースがあります。
修正提案: FindAttacker の選定条件でも enemy.MoveSpeed * enemy.State.MoveSpeedMultiplier <= 0f を弾く。
15. HeldBellyPunchDirector.Attack.cs の ChooseFacePunchSide で未使用引数 enemy
ファイル・行: HeldBellyPunchDirector.Attack.cs:168, 272
事実: ChooseFacePunchSide(EnemyBrain enemy) は引数 enemy を受け取っていますがメソッド内で一切使用しておらず、ChooseBellyPunchSide() と全く同じ計算（!_cameraOnHerRight ^ (_hits % 2 == 1)）を行っています。
修正提案: 未使用引数 enemy を削除するか、ChoosePunchSide() として 1 つのメソッドに統合する。
16. テスト不足の指摘（単体テスト・結合テストでカバーすべき境界ケース）
事実: コード内のコメントから -haultest、-mountlog、-handoffburst などの起動オプションによる実機・バッチ撮影テストは充実していることが読み取れますが、以下のエッジケースに対する自動テスト（EditMode / PlayMode テスト）が見当たりません。
カメラ優先度の競合テスト: GroundHaulUp / GroundMountAttack（RequestCinematicShot）と HeldBellyPunchDirector（PushFocus/HoldFocus）と CameraDirector（死亡時 ApplyDeathFraming）が同時に発火した際の優先順位と復帰動作。
連打脱出（EscapeInputs = 6）の境界フレームテスト: GroundHaulUp の Stage == Beat.Haul かつ Progress >= 1f（Settle 遷移直前フレーム）に 6 回目の連打入力が入った場合、脱出成功（End(true)）と拘束移行（Finish()）のどちらが勝つか。
ゼロ時間・極端な deltaTime テスト: HitStop 中（Time.timeScale == 0）に CameraShake と EnemyReaction（unscaledDeltaTime 使用）が完了し、ヒットストップ解除後にスケールやエミッションが初期値へ完全復帰することの検証。
特に優先して修正コードを作成したい項目（例: Critical 1〜3 の状態リーク・GC改善、または 単体テストのひな形作成）はありますか？他チャンク（ThirdPersonCamera や PlayerGrapple など）との連携仕様もあわせて確認したい場合はお知らせください。


## answer-10

# Chunk 10

Files:
- MioPrototype/Assets/RyonaGame/Runtime/Feel/HeldBellyPunchDirector.cs
- MioPrototype/Assets/RyonaGame/Runtime/Feel/HitFlash.cs
- MioPrototype/Assets/RyonaGame/Runtime/Feel/HitStop.cs
- MioPrototype/Assets/RyonaGame/Runtime/Feel/OcclusionFade.cs
- MioPrototype/Assets/RyonaGame/Runtime/Feel/PinballDirector.cs
- MioPrototype/Assets/RyonaGame/Runtime/Feel/ProceduralSfx.cs
- MioPrototype/Assets/RyonaGame/Runtime/Feel/SfxAudition.cs
- MioPrototype/Assets/RyonaGame/Runtime/Feel/WallPinSites.cs
- MioPrototype/Assets/RyonaGame/Runtime/Feel/WallSplitCamera.cs
- MioPrototype/Assets/RyonaGame/Runtime/Feel/WristLockVisual.cs

---

添付された Feel フォルダ内 10 ファイルのコードレビューを行います。全体として設計意図や実測ベースでの不具合修正履歴がコメントで丁寧に残されていますが、コルーチン多重起動による状態汚染、シーン遷移時の再初期化漏れ（static 変数）、Material / GameObject のリーク などに改善余地があります。 
Critical（進行不能・重大な状態不整合・リーク）
1. HeldBellyPunchDirector.cs : コルーチン多重起動による二重ダウン・状態破壊
箇所: HeldBellyPunchDirector.cs:943, 964, 998, 1119 (ReleaseWithFinisherDown())
事実: UpdateFinisher() や UpdateCollapse() 等から StartCoroutine(ReleaseWithFinisherDown()) を呼んでいます。コルーチンは即時実行されますが（末尾の yield break まで同期実行されるため現状は1フレームで終わる）、もし将来 yield return を挟んだ場合や、別経路から同時に呼ばれた場合に多重起動を防げません。また、UpdateFinisher の 940-944 行目では StartCoroutine(ReleaseWithFinisherDown()) を呼んだ後 _punchInFlight こそ false にしていますが、ReleaseWithFinisherDown() 内で _phase = Phase.Idle にしているため、yield return を1つでも追加すると次フレームの UpdateFinisher() が再度 StartCoroutine を走らせます。
修正提案: ReleaseWithFinisherDown に yield が不要なら通常のメソッドにするか、コルーチンにするなら _releasing フラグで再入を完全にガードしてください。 
2. PinballDirector.cs : RunRounds() / Sandwich() コルーチンの多重起動・キャンセル後の残留
箇所: PinballDirector.cs:352, 355-381, 596-608
事実: Cancel() で _active = false にした直後に再度 StartPinball が呼ばれて _active = true になった場合、前の RunRounds() や Sandwich() が yield return new WaitForSeconds(...) から復帰した際、if (!_active) をすり抜けて古いコルーチンが新しいピンボールのラウンドや HandOff を上書き実行します。
修正提案: Coroutine _sequenceRoutine; を保持し、StartPinball 時および Cancel() 時に必ず StopCoroutine(_sequenceRoutine) を呼んでください。 
3. WallPinSites.cs : static フィールドによるシーン再読み込み時の不正キャッシュ
箇所: WallPinSites.cs:136, 159-168 (WallPinProps)
事実: static readonly float[] SiteX = BuildSiteX(); はクラス初回ロード時に1度だけ評価されます。起動引数や RyonaConfig.Instance.Encounter がシーンロード後に変わるケースや、PlayMode テストで設定を書き換えた後にシーンをリロードしても古い SiteX が使われ続けます。また WallPinSites.Sites も OnDisable が呼ばれずにシーン破棄された場合に null 要素が残り続けます（Find 内で site == null チェックはしているもののリストが肥大化します）。
修正提案: SiteX を static readonly ではなく BuildSiteX() を Build() 内で毎回呼ぶ形にし、Sites.RemoveAll(s => s == null) を適宜行ってください。 
High（null参照・メモリリーク・カメラ/演出の競合）
4. WristLockVisual.cs : Material とプリミティブメッシュのリーク・Shader.Find nullリスク
箇所: WristLockVisual.cs:19-30
事実: Awake() で new Material(Shader.Find("Standard")) を生成していますが、OnDestroy() で Destroy(_material) していません（エディタ再生やシーン遷移のたびにマテリアルがリークします）。また、Shader.Find("Standard") がストリッピング等で null を返した場合、new Material((Shader)null) で例外が発生します。さらに LateUpdate（63行目）で delta.normalized を Quaternion.FromToRotation に渡していますが、両手が完全に重なった場合（delta == Vector3.zero）の挙動が不安定になります。
修正提案: OnDestroy() で Destroy(_material) を呼び、Shader.Find のフォールバック（Unlit/Color 等）と delta.sqrMagnitude > 1e-6f チェックを入れてください。 
5. PinballDirector.cs : Escape() および FinishOnHit() での null 参照リスク
箇所: PinballDirector.cs:558, 591-592 
事実:
FinishOnHit()（558行目）で _front.transform を直接参照していますが、直前の FinishFallback（577行目）では _front != null ? _front.transform : null と null チェックしているのに、ここでは抜けています。
Escape()（591-592行目）でも _front.TakeHit(...) / _rear.TakeHit(...) を null チェックなしで呼んでいます。ピンボール中に敵が消滅・破棄されると NullReferenceException になります。 
修正提案: _front?.TakeHit(...) / _rear?.TakeHit(...) および _front != null ? _front.transform : null に統一してください。 
6. HeldBellyPunchDirector.cs : カメラ制御と HitStop の競合・取り残し
箇所: HeldBellyPunchDirector.cs:442-443, 512-518, 1047-1048 
事実:
OnDodgeFinisherHitReceived()（1047-1048行目）で ApplyCollapseCamera() を呼んだ直後に _phase = Phase.Idle にしているため、以降の Update() では HoldFraming() が呼ばれません。一方、PinballDirector.cs:99-100 では _holdFrameUntil で一定時間 HoldFocus を維持しています。ThirdPersonCamera.PushFocus の仕様次第では、1フレームだけ寄って即座にカメラが跳ね戻る可能性があります（推測）。
また、OnGrappleEnded() で ResetHold() が呼ばれた際、すでに HitStop.Play(...) で Time.timeScale = 0f になっている最中に拘束解除イベントが外部から発火すると、カメラだけ戻ってヒットストップは継続します。 
修正提案: ApplyCollapseCamera() が持続的なカメラ指示なのか単発のプッシュなのかを PinballDirector の _holdFrameUntil と仕様統一してください。 
7. WallSplitCamera.cs : static による設定の永続化（テスト・リロード汚染）
箇所: WallSplitCamera.cs:35-41
事実: _loaded = true になった後、コマンドライン引数やモードがプロセス終了まで固定されます。また、125行目の target -= right * lateralShift * camSide * -1f; は二重否定（-= ... * -1f は += ... と同義）になっており、直前の 124行目 target += right * (-camSide * _lateral); と符号の向きが逆転しています。コメント（81行目「拘束役の側（画面右）へ寄せる量」）と実際の移動方向が逆になっている可能性が高いです（推測）。
修正提案: 125行目の演算を target += right * (camSide * lateralShift); のように意図が明確な式に整理し、テスト用にリセットメソッドを用意してください。 
Medium（GC/パフォーマンス・境界値・メンテナンス性）
8. PinballDirector.cs : 毎秒および開始時の FindObjectsByType による GC・CPU 負荷
箇所: PinballDirector.cs:191, 254, 297, 413 
事実:
IsOtherSequenceBusy() が Update() から毎フレーム呼ばれ、_heldPunch == null の間は毎フレーム FindFirstObjectByType<HeldBellyPunchDirector>() を実行します（シーンに存在しない検証環境では毎フレーム全オブジェクト走査）。
TryFindPair（1秒に1回）や StartPinball、Sandwich のたびに FindObjectsByType<EnemyBrain> で配列をアロケーションしています。 
修正提案: EnemyBrain 側に WallPinSites のような静的レジストリ（ActiveEnemies リスト）を持たせて走査することでゼロアロケーション化できます。 
9. OcclusionFade.cs : LateUpdate での GetComponent<PlayerGrapple>() 毎フレーム呼び出し
箇所: OcclusionFade.cs:166-167
事実: 敵の人数 × 毎フレームの LateUpdate() で _brain.Player.GetComponent<PlayerGrapple>() を呼んでいます。また 253, 259, 267, 282, 287 行目で toSelf.magnitude（平方根計算を含む）を何度も再計算しています。
修正提案: PlayerGrapple の参照は ResolvePlayer() 時にキャッシュし、float selfDist = toSelf.magnitude; を1回だけ計算して使い回してください。 
10. SfxAudition.cs : Entries と SfxId の同期ずれ・GUIStyle の毎フレーム破棄リスク
箇所: SfxAudition.cs:7-42, 27-43, 96-113 
事実:
コメント（10行目）に「番号は SfxId と 1 対 1」とありますが、ProceduralSfx.cs:41 に追加された SfxId.WallSlam = 14 が SfxAudition.Entries に含まれていません（14項目しかなく WallSlam が試聴不能）。
109行目の - キー（Key.Minus）は Entries.Length - 1 を再生する実装になっているため、項目が増えた現在では 11 番ではなく 14 番（HurtBelly）が鳴り、画面ヒントの 「- =11：単発」（272行目）と食い違っています。 
修正提案: Entries に SfxId.WallSlam を追加し、キーバインドと表示テキストのインデックスを一致させてください。 
11. ProceduralSfx.cs : 波形生成時の GC アロケーションと PlayOn でのイベント未発火
箇所: ProceduralSfx.cs:255-272, 274-288 
事実:
Play()（197行目）では HurtSmall / HurtLarge / HurtBelly 再生時に HurtVoicePlayed イベントを発火してリップシンク等に通知していますが、PlayAt() と PlayOn() では発火していません。
BuildClips() で毎回 float[] を確保していますが、初期化時1回のみなので許容範囲です。ただし TryMeasureMix は配列を毎回生成するため、テスト頻度が高い場合は注意が必要です。 
修正提案: PlayAt() / PlayOn() でもボイス系 ID の場合は HurtVoicePlayed を発火させるか、ボイスの再生経路を1本化してください。 
Low（軽微な不整合・未使用コード・テスト不足）
12. HeldBellyPunchDirector.cs : 未使用定数とデバッグログの残留
箇所: HeldBellyPunchDirector.cs:124-125, 266-282, 529-530, 1107-1110 
事実:
AttackerSideOffset（125行目）はコメント通り未使用、CaptorBackstepDistance / CaptorBackstepSeconds（1107-1110行目）もコード内で参照されていません。
LogHoistProbe()（530行目）は起動オプション等のフラグなしでフィニッシャー中 0.10 秒ごとに GameLog.Log を出力し続けます。 
修正提案: 未使用定数を削除し、LogHoistProbe() は OcclusionFade の -occlog のように専用フラグ付きの時だけ出力するようにしてください。 
13. HitFlash.cs / HitStop.cs : OnGUI の毎フレーム new Color・FindFirstObjectByType
箇所: HitFlash.cs:129, HitStop.cs:73
事実: HitStop.RestoreNow()（73行目）で復帰のたびに FindFirstObjectByType<TitleGate>() を呼んでいます。TitleGate 側に TitleGate.IsOpen のような静的プロパティを持たせる方が安全かつ高速です。 
14. テスト不足の領域（優先して追加すべき単体/PlayModeテスト）
HitStop の割り込みテスト: ヒットストップ中に PauseMenu.IsPaused = true になった場合、および HitStopRunner が無効化・破棄された場合に Time.timeScale が正しく復元・維持されるかの検証。 
HeldBellyPunchDirector の途中解除・再入場テスト: Combo / Finisher / FinisherDodge の各フェーズ途中でプレイヤーが連打脱出（OnGrappleEnded）した直後に即座に再拘束された場合、カメラ・Time.timeScale・拘束役の回避状態（EndEvade）が残留しないかの検証。 
PinballDirector のキャンセル・リトライテスト: 開始直後の OpeningTimeoutSeconds タイムアウトや Sandwich 途中で相手が死亡した場合に、_grapple.IsPinballActive と他敵の LockForHandOff が確実に解除されるかの検証。 
あなたがアップロードしたデータ
·
このカードは自分だけが閲覧できます
chunk-10.txt


## answer-11

# Chunk 11

Files:
- MioPrototype/Assets/RyonaGame/Runtime/Feel/WristPullDirector.cs

---

MioPrototype/Assets/RyonaGame/Runtime/Feel/WristPullDirector.cs のコードレビュー結果です。
Critical
WristPullDirector.cs:139 — 輪を割る連打（BreakPressed）が頭突きカウンターの「1回限りの権利」を消費してしまう
確定事実: TickPull() 内で InputReader.LightPressed() || InputReader.HeavyPressed() || InputReader.BreakPressed() のいずれかが押されると TryCounter() が呼ばれ、_inputUsed = true にセットされる。CounterWindow（0.16s）より前に押された場合、「早すぎた入力」としてカウンター権が永久に失われる。
推測（仮説）: クラス冒頭コメント（16〜18行目）には「連打で輪を割る（BreakInput）」と「攻撃ボタンを1回だけ押して頭突き（連打していると狙えない）」とあり、仕様上 BreakPressed() は輪を割る専用入力である可能性が高い。同一ボタンでない場合、BreakPressed() を TryCounter() に含めると「輪を割ろうと連打した瞬間にカウンター権が消滅する」という意図しないペナルティになる。
修正提案: BreakPressed() と攻撃ボタンが別割り当てなら、pressed の判定から InputReader.BreakPressed() を除外してください（同一ボタン仕様ならコメントを一致させてください）。 
High
WristPullDirector.cs:86-93 — 引き寄せ中にプレイヤーがダウン・死亡・別敵に拘束されたり、輪が割れたりしても引き寄せが中断されない
確定事実: Update() は _puller != null の場合、_wrist.IsLocked や _stats.IsDown / _stats.IsDead / _grapple.IsGrappled を確認せずに TickPull() へ直行して return する。
推測（仮説）: 構え中（0.9秒間）にプレイヤーが連打で輪を割り切った場合や、別の敵の攻撃でダウンした場合でも、EnemyBrain 側で IsLocked 等を毎フレーム監視していなければ、輪がないのに引き寄せ拘束が成立する、あるいはダウン中のプレイヤーを強制拘束する不具合につながる。
修正提案: TickPull() の冒頭で !_wrist.IsLocked || _stats.IsDown || _stats.IsDead || (_grapple.IsGrappled && _grapple.Captor != _puller) をチェックし、該当したら _puller をキャンセル（CancelBystanderWindup 等）して _puller = null に戻してください。 
WristPullDirector.cs:126 — _puller が引き寄せ構え中に破棄（Destroy）された場合の MissingReferenceException リスク
確定事実: TickPull() では _puller.IsPreparingWristPull に直接アクセスしており、if (_puller == null) の Unity オーバーロード済み null チェックを行っていない。
推測（仮説）: 引き寄せの構え中に _puller の GameObject が Destroy された場合、Update() の if (_puller != null) は true と評価される（またはフレーム途中で破棄される）と、TickPull() 内で MissingReferenceException が発生し、Update() が例外で止まり続ける可能性がある。
修正提案: TickPull() の先頭に if (_puller == null) { _puller = null; return; } を追加してください。 
WristPullDirector.cs:110-122 — シーン再ロードやプレイヤー再スポーン時にコンポーネント参照が古くなる（Stale Reference）
確定事実: Resolve() は _stats == null のときだけ FindFirstObjectByType<PlayerStats>() を実行する。なお WristPullDirector 自体に DontDestroyOnLoad は付いていないため通常はシーン遷移で破棄されるが、外部から DontDestroyOnLoad 化された場合や、プレイヤーのみがデストロイ＆再生成された場合に追従できない。また、PlayerStats だけ先に生成され PlayerGrapple / PlayerWristLock が後から AddComponent されるケースでは、if (_stats == null) ブロックに入らないため _grapple が永遠に null のままになる。
修正提案: if (_grapple == null && _stats != null) _grapple = _stats.GetComponent<PlayerGrapple>(); のように各フィールド個別に null チェックを行ってください。 
Medium
WristPullDirector.cs:128-136 — _puller がヒットせず空振り（外れた）した場合、即座に次の引き寄せが開始される
確定事実: TickPull() で !_puller.IsPreparingWristPull になったとき、Counters 成功時は _nextTryAt = Time.time + 1.2f を設定しているが（168行目）、拘束不成立（外れ・別要因で中断）で 134行目に抜けた場合は _nextTryAt を更新していない。
推測（仮説）: プレイヤーが回避等で引き寄せを外した直後の次フレームで、即座に同じ敵または別の敵が再び引き寄せ構えに入り、連続ハメ状態になる恐れがある。
修正提案: 134行目の _puller = null; の直前に _nextTryAt = Time.time + RetrySeconds;（または適切なクールダウン）を設定してください。 
WristPullDirector.cs:157 — left <= 0（引き寄せ成立フレームと同時）の境界値扱い
確定事実: TryCounter() は float.IsNaN(left) || left > CounterWindow のみを弾いており、left <= 0f の場合もカウンター成功とみなす。また、クラスコメント（17行目）には「掴む前後 CounterWindow 秒」とあるが、実装と XML コメント（33, 147行目）は「掴む瞬間の手前 CounterWindow 秒以内（0 < left <= 0.16）」になっている。
推測（仮説）: EnemyBrain の更新順（Script Execution Order）によっては、EnemyBrain.Update() が先に走って拘束を成立させた同フレームに WristPullDirector.Update() が走ると、すでに掴まれているのに頭突きカウンターが成立し、状態不整合（拘束されながら敵がよろける）が起きる可能性がある。
修正提案: left <= 0f を弾くか、すでに _grapple.IsGrappled になっている場合はカウンター不可とするガードを入れてください。また17行目のコメント「前後」を「手前」に修正してください。 
WristPullDirector.cs:186-188 — 距離チェックにおける CombatConstants.PinMinDistance の境界と内積判定のゼロベクトル
確定事実: distance < CombatConstants.PinMinDistance で近すぎる敵を除外している。
推測（仮説）: PinMinDistance が極端に小さい（0 に近い）定数の場合、to がゼロベクトルになり Vector3.Dot が 0f となる（<= 0f で弾かれるためゼロ除算等は起きない）。ただし、真横（90度付近、Dot が 0.01 など）の敵も「正面」と判定されるため、視界外（真横）から突然引き寄せられる違和感が出る可能性がある。
修正提案: 正面判定を角度で制限したい場合は、正規化したベクトル同士の Vector3.Dot に対し閾値（例: > 0.3f 約72度以内）を設けることを検討してください。 
WristPullDirector.cs:177, 223 — FindObjectsByType<EnemyBrain> のアロケーションと探索コスト
確定事実: 輪が付いている間、候補が見つからない場合は RetrySeconds（0.4秒）ごと、失敗時は 0.1秒ごとに FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None) を呼び出している。
推測（仮説）: 呼び出し頻度は最大 10Hz 程度なので即座にフレーム落ちするほどではないが、敵のスポーン・デスを管理するレジストリ（例: EnemyBrain.All のような静的リスト）がプロジェクト内に存在するなら、そちらを参照する方が GC アロケーション（配列生成）とシーン全走査を避けられる。
修正提案: 既存の敵管理リストがあればそれを利用し、なければ現状維持でも実害は小さいです。 
Low
WristPullDirector.cs:139 — TickPull() 内で毎フレーム計算される left がテストフラグ無効時でも評価されている
確定事実: 138行目で float left = _puller.WristPullSecondsToStrike; を取得しているが、通常プレイ時は 140行目の _testCounter が false のため使用されず、TryCounter() 内で再取得している（156行目）。
修正提案: 138行目を if (_testCounter) ブロック内に移動すると読みやすくなります。 
WristPullDirector.cs:206 — TryStartWristPull 失敗時のマジックナンバー 0.1f
確定事実: 候補が見つからない場合は定数 RetrySeconds（0.4f）を使っているが、TryStartWristPull が false を返した場合の再試行間隔はリテラル 0.1f になっている。
修正提案: 定数化するか、RetrySeconds と意図の違いが分かるようコメントを添えてください。 
テスト不足の観点（テスト用フックはあるが自動検証の網羅性に懸念）
確定事実: -wristpulltest と -wristpullcounter による簡易テスト用の配置・自動入力ロジックが本番クラス内に混在している。一方で、以下のケースは現在のテストフラグでは検証されない。
引き寄せ構え中に BreakInput 連打で輪を割り切ったとき、引き寄せが正しくキャンセルされるか。
CounterWindow より早いタイミング（例: left = 0.5f）で攻撃ボタンを押したとき、1回目が空振り扱いになり、その後の CounterWindow 内の2回目入力が正しく弾かれるか。
引き寄せ構え中に別の敵から攻撃を受けてダウンしたとき、状態が破綻しないか。
修正提案: EditMode / PlayMode テスト（Unity Test Framework）で上記境界値テストを追加し、テスト用コード（ArrangeTestPuller 等）は #if UNITY_EDITOR || DEVELOPMENT_BUILD 等で分離することを検討してください。 
より正確なレビューにするため、もしよろしければ以下を教えていただけますか？
入力仕様: InputReader.BreakPressed() は攻撃ボタン（Light/Heavy）と同じキー/ボタンに割り当てられていますか、それとも別ですか？
EnemyBrain 側の実装: EnemyBrain 側で毎フレーム _wrist.IsLocked やプレイヤーのダウン状態を監視して自動キャンセルする処理が入っていますか？
これらが分かれば、状態不整合や入力競合の修正コードを具体的に提案できます。
あなたがアップロードしたデータ
·
このカードは自分だけが閲覧できます
chunk-11.txt


## answer-12

# Chunk 12

Files:
- MioPrototype/Assets/RyonaGame/Runtime/AssemblyInfo.cs
- MioPrototype/Assets/RyonaGame/Runtime/BeltScreenLock.cs
- MioPrototype/Assets/RyonaGame/Runtime/Config/ConfigurableMonoBehaviour.cs
- MioPrototype/Assets/RyonaGame/Runtime/Config/RyonaConfig.cs
- MioPrototype/Assets/RyonaGame/Runtime/ContactShadow.cs
- MioPrototype/Assets/RyonaGame/Runtime/EncounterDirector.cs
- MioPrototype/Assets/RyonaGame/Runtime/GameLog.cs
- MioPrototype/Assets/RyonaGame/Runtime/IdlePose.cs
- MioPrototype/Assets/RyonaGame/Runtime/InputReader.cs
- MioPrototype/Assets/RyonaGame/Runtime/LaunchOptions.cs
- MioPrototype/Assets/RyonaGame/Runtime/LoadingScreen.cs
- MioPrototype/Assets/RyonaGame/Runtime/MatchHud.cs
- MioPrototype/Assets/RyonaGame/Runtime/PauseMenu.cs
- MioPrototype/Assets/RyonaGame/Runtime/PlayerCombat.cs

---

添付された14ファイル（チャンク12/26）のソースコードレビューを重大度別にまとめます。
Critical（進行不能・重大なロジック破綻）
コンボ段数更新の多重進行（先行入力と実攻撃で2回ステップが進む可能性）
対象: PlayerCombat.cs:233-236, PlayerCombat.cs:248-257, PlayerCombat.cs:289-293
事実: ResolveLight(out int step) は現在の _comboStep を基準に次の段数を計算します。硬直中（_attack != null）の BufferAttackInput() 内で ResolveLight を呼んで _bufferedStep に保存し、TryConsumeBuffered() 経由で Begin(attack, step) を呼ぶことで _comboStep が更新されます。しかし、このとき _comboUntil は現在の攻撃の終了時刻＋猶予時間に設定されているため、硬直中に複数回弱ボタンを押すと _bufferedStep は同じ _comboStep + 1 で上書きされ続けます。一方、硬直が明けたフレームで TryConsumeBuffered() が消費された直後ではなく、「硬直中に先行入力せず、硬直明けフレームの Update() で LightPressed() が true になった場合」は問題ありませんが、「FeetNotSettled && Time.time > _comboUntil の分岐（208行目）」に入ると BufferAttackInput() が呼ばれます。この時点では Time.time > _comboUntil なので ResolveLight は step = 0 を返しますが、直前の攻撃で _comboUntil が切れるギリギリにバッファされた入力が消費される際、Begin 内で _comboUntil = Time.time + attack.TotalSeconds + ComboWindowSeconds; が再設定されます。ここまでは整合していますが、2段目（step == 1）をバッファした後、硬直が明ける前に InputBufferSeconds（0.15s）が経過してバッファが破棄された場合、_comboUntil はまだ有効なので次の押し直しは正しく2段目になりますが、もし「3段目（step == 2、実体は AttackLibrary.Heavy()）」を弱ボタンで先行入力した場合、Begin(AttackLibrary.Heavy(), 2) が呼ばれます。
問題点（事実）: PlayerCombat.cs:341-342 のコメントに「攻撃の種類が違えば必ず 1 段目扱い」とありますが、コード上は Begin(AttackLibrary.Heavy(), 2) により _comboStep = 2 となり、その直後（ComboWindowSeconds 以内）に再度弱ボタンを押すと (_comboStep + 1) % 3 で 0（1段目）に戻ります。しかし、1段目(step=0)の硬直中に弱ボタンを連打して step=1 をバッファし、その攻撃が始まった瞬間にさらに弱ボタンを連打した場合、Begin が呼ばれて _comboStep が 1 に変わった後のフレームで BufferAttackInput が step=2 を計算するのは正常です。 問題は ResolveLight が「現在の _comboStep」のみを見ており、「バッファ済みの _bufferedStep」を考慮していない点ではなく、TryConsumeBuffered() で Begin を呼ぶ際、Time.time > _comboUntil になっていた場合でも、バッファ時に計算した古い step（例えば2段目や3段目）のまま強制的にコンボが繋がってしまう点です。
修正提案: TryConsumeBuffered() 実行時に Time.time > _comboUntil であれば step = 0（かつ attack が3段目フィニッシュなら Light）にリセットするガードを入れてください。 
EncounterDirector のウェーブ上限到達判定と MaxWaves / ArenaCount * WavesPerArena の不整合
対象: EncounterDirector.cs:33-34, EncounterDirector.cs:94, EncounterDirector.cs:324, EncounterDirector.cs:344
事実: ArenaCount = 5、WavesPerArena = 2（計10ウェーブ）と MaxWaves = 10 が独立したフィールドとして定義されており、Awake() で RyonaConfig から読み込まれるのは ArenaCount と WavesPerArena だけで、MaxWaves はハードコードの 10（または -maxwaves 引数）のままです。
推測（仮説）: RyonaConfig 側で ArenaCount や WavesPerArena を変更して総ウェーブ数を変えた場合、MaxWaves が 10 のまま取り残されるため、設定より早くステージクリアになる、あるいは全アリーナが終わった後も Plan.CenterOf(nextArena) で範囲外のアリーナ座標（アリーナ5以降）へ無限に進行しようとする不具合が発生します。
修正提案: ApplySettings 内で MaxWaves = s.ArenaCount * s.WavesPerArena; を同期させる（-maxwaves が指定された場合のみ上書きする）設計にしてください。 
シーンリロード時に RyonaConfig.OnConfigChanged などの static 状態・イベント購読が残留するリスク
対象: RyonaConfig.cs:485, RyonaConfig.cs:508, PauseMenu.cs:155-160
事実: PauseMenu.Restart() で SceneManager.LoadScene(...) によるシーン再読み込みを行っています。ConfigurableMonoBehaviour は OnDisable で RyonaConfig.OnConfigChanged を解除していますが、もし非アクティブのまま破棄されたオブジェクトや他の static イベント購読者があるとリークします。また RyonaConfig._instance は static にキャッシュされ続けます。
修正提案: ConfigurableMonoBehaviour に OnDestroy() を追加して確実に RyonaConfig.OnConfigChanged -= OnConfigChanged; を行ってください（OnDisable が呼ばれないケースへの保険）。 
High（リソースリーク・状態不整合・null参照リスク）
ContactShadow のメッシュリークと複数キャラクター非対応・破棄漏れ
対象: ContactShadow.cs:37-38, ContactShadow.cs:71-75, ContactShadow.cs:95, ContactShadow.cs:112-113
事実:
Build() 内で BuildQuadMesh() と BuildRadialTexture(64) により Mesh と Texture2D を動的生成していますが、OnDestroy() で破棄しているのは _material のみで、生成した Mesh と Texture2D がリークします。
FindFirstObjectByType<CharacterAnimator>() で最初に見つかった1体しか追従しないため、敵キャラクターや2体目以降のキャラクターには接地影が出ません。
修正提案: OnDestroy() で _material.mainTexture と MeshFilter.sharedMesh も Destroy() してください。また、プレイヤー専用であれば PlayerMotor 等から取得する旨を明記するか、キャラクターごとにアタッチする設計に見直してください。 
MatchHud の静的 Texture2D / Sprite / Font リソース管理
対象: MatchHud.cs:672-719
事実: WhiteSprite 内で new Texture2D と Sprite.Create を生成し HideFlags.HideAndDontSave を付けて static 保持しています。アプリ終了まで解放されないため1枚だけなら実害は小さいですが、エディタ上で Domain Reload 無効設定（Enter Play Mode Options）を使用している場合、再生を停止しても static 変数が残り続けます。
修正提案: [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] で _white = null; _jpFont = null; をリセットする初期化メソッドを追加してください（InputReader や GameLog の static 変数も同様）。 
Domain Reload 無効時の static 変数汚染（テスト・エディタ再生時の不具合）
対象: GameLog.cs:33-34, InputReader.cs:29-32, LaunchOptions.cs:24-30, LoadingScreen.cs:15, PauseMenu.cs:13
事実: GameLog._initialized、LaunchOptions._parsed、InputReader._autoAttackTimer などの static フィールドにリセット処理がありません。Unity エディタで「Reload Domain」をオフにして連続再生すると、2回目以降の再生でコマンドライン引数や初期化フラグが前回実行時のまま残ります。
修正提案: 各クラスの static 状態を [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] でリセットしてください。 
PlayerCombat.Update() での毎フレーム GetComponent<PlayerWristLock>() 呼び出しと null チェック順
対象: PlayerCombat.cs:144
事実: Update() の先頭で毎フレーム GetComponent<PlayerWristLock>() を実行しています（_motor や _grapple はキャッシュしているのに wrist だけキャッシュされていません）。
修正提案: Awake() で _wrist = GetComponent<PlayerWristLock>(); としてキャッシュしてください。 
PlayerCombat.TickAttack() と Update() における Time.timeScale == 0（ポーズ中）の入力受付
対象: PlayerCombat.cs:141-242, MatchHud.cs:71-121
事実: PauseMenu で Time.timeScale = 0f にしても MonoBehaviour.Update() は呼び出され続けます。PlayerCombat.Update() には PauseMenu.IsPaused のガードがないため、ポーズ中に J/K/L キーを押すとポーズ解除と同時に攻撃が暴発する、またはポーズ中に攻撃が開始（Begin）して音やステートが変化する危険があります。
修正提案: PlayerCombat.Update() の冒頭に if (PauseMenu.IsPaused) return; を追加してください。 
Medium（境界値・パフォーマンス・GCアロケーション・設計上の懸念）
戦闘中の FindObjectsByType<EnemyBrain> による検索コストとターゲット不整合
対象: PlayerCombat.cs:390, PlayerCombat.cs:492, PlayerCombat.cs:666
事実: 攻撃開始時（AimAtNearestEnemy）、ヒット判定時（ResolveHit）、ログ出力時（FindNearestEnemy）のたびに FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None) でシーン全検索を行っています。また、AimAtNearestEnemy() では「ベルト向き（d.x * _motor.LaneFacingSign <= 0f）の敵のみ」を対象にしているのに対し、ResolveHit() の InFistReach() では背後（逆向き）にいる敵も拳の球範囲に入っていればヒット対象になります。
推測（仮説）: すぐ後ろに敵が重なっている状態で前方にパンチすると、背後の敵に拳の判定がかすって当たってしまう（あるいは、狙った敵より背後の敵の方が拳の原点に近いために背後の敵に吸われる）挙動になり得ます。
修正提案: EncounterDirector 等が管理するアクティブな敵リストを参照するようにして FindObjectsByType を削減し、ResolveHit にも向き（またはターゲット優先度）の考慮を入れることを検討してください。 
BeltScreenLock.LateUpdate() における毎フレームの GameLog 文字列補間リスクと実行順依存
対象: BeltScreenLock.cs:88-119, ContactShadow.cs:107, IdlePose.cs:32
事実: 複数のコンポーネントが LateUpdate() を使用しており、[DefaultExecutionOrder] が指定されていません。
IdlePose.LateUpdate() はボーン姿勢を変更します。
ContactShadow.LateUpdate() はボーン座標（rig.Hips.position 等）を読み取って影の位置・サイズを計算します。
実行順が不定なため、フレームによって影が1フレーム前のボーン位置を参照する場合があります。
修正提案: ContactShadow やカメラ追従系の LateUpdate には [DefaultExecutionOrder] を明示して実行順序を固定してください。 
EncounterDirector.Update() の Collect() 呼び出しとコルーチンの寿命管理
対象: EncounterDirector.cs:186-211, EncounterDirector.cs:299, EncounterDirector.cs:414-431
事実:
Update() の冒頭で毎フレーム Collect() を呼んでいますが、敵が0体のシーンでは1秒ごとに FindObjectsByType<EnemyBrain> が走り続けます。
EnterWave() で開始した VerifyStanding() コルーチン（3秒待機）は、3秒以内に次のウェーブへ進んだ場合やリスタートした場合のキャンセル処理がありません（null チェックはあるため例外は出ませんが、ログが混線します）。
修正提案: VerifyStanding の Coroutine 参照を保持し、次の EnterWave 開始時に StopCoroutine してから再起動してください。 
MatchHud.Update() 内での FindFirstObjectByType フォールバックと GC アロケーション
対象: MatchHud.cs:140-152, MatchHud.cs:351-354, MatchHud.cs:377-378, MatchHud.cs:392-393, MatchHud.cs:318-319, MatchHud.cs:453-458
事実:
_grapple、_bellyPunch、_screenLock、_motor、_wrist、_encounter がシーン上に存在しない場合、毎フレーム FindFirstObjectByType が実行され続けます。
UpdateDownGauges() や UpdateGrapple() で、数値が変わっていないフレームでも毎フレーム文字列補間（$"弱あと..." 等）を行い Text.text に代入しているため、毎フレーム文字列の GC アロケーションと uGUI のメッシュ再構築が発生します。
修正提案:
参照検索は Start() で1回行うか、未検出時の再検索にクールダウンを設けてください。
前回表示した値（整数値や残り秒数の0.1s刻み値）をキャッシュし、変化したフレームだけ .text を更新してください。 
PauseMenu.Update() の FindFirstObjectByType<TitleGate>() 文字列/型検索とキーバインドの二重化
対象: PauseMenu.cs:74-78, PauseMenu.cs:210-287
事実: PauseMenu 内で独自に Keyboard.current / Gamepad.current を直接読む入力判定（TogglePressed, UpPressed, AxisPressed 等）を実装しており、InputReader と入力処理が二重化しています。また、旧 Input Manager のみが有効な環境（ENABLE_INPUT_SYSTEM 無効時）ではポーズメニューの操作が一切効かなくなります。
修正提案: ポーズメニュー用の入力判定も InputReader に集約し、旧 Input Manager フォールバックも統一してください。 
LaunchOptions.EnsureParsed() における負数引数と -key value 形式の誤認
対象: LaunchOptions.cs:130-133
事実: args[i + 1][0] != '-' で次のトークンを値として取り込んでいます。もし -camdist -5 のように負数をスペース区切りで渡すと -5 が別フラグ扱いになり値が取れません（-camdist=-5 なら動作します）。また、Unity 自体のコマンドライン引数（例: -batchmode -nographics）が並んだ場合、Switches["batchmode"] だけでなく Values["batchmode"] = "-nographics" にはなりませんが、-logFile output.txt のようなペアは問題ないものの、単体スイッチの直後に非ハイフンの引数（ファイルパス等）があると Values に誤登録されます。
修正提案: XMLドキュメントに「負数は -key=-5 形式を使うこと」を明記しておくと安全です。 
Low（可読性・保守性・軽微な改善点）
XMLドキュメントコメントの重複
対象: PlayerCombat.cs:647-657, MatchHud.cs:123-127
事実: PlayerCombat.Log と MatchHud._releasedRemaining に <summary> タグが2つ連続して書かれており、コンパイラ警告（CS1570 / ドキュメント構造不正）や IDE のツールチップ表示崩れの原因になります。
修正提案: 古い方の <summary> ブロックを削除して1つにまとめてください。 
BeltScreenLock.LateUpdate() における GateRange の下限クランプとコメントの齟齬
対象: BeltScreenLock.cs:28, BeltScreenLock.cs:115
事実: GateRange の既定値（および RyonaConfig.BeltLockSettings の既定値）が 0.5f で、LateUpdate() 内でも Mathf.Max(0.5f, GateRange) と下限を 0.5f に固定しているため、インスペクタや Config で 0.5 未満（例: 0.2f）を設定しても無視されます。
修正提案: 最小許容値が 0.5f である仕様なら RyonaConfig.BeltLockSettings.GateRange に [Min(0.5f)] 属性を付与して設定画面上でも明示してください。 
IdlePose のボーン検索の汎用性
対象: IdlePose.cs:87-103
事実: Animator.GetBoneTransform が失敗した場合のフォールバック名が VRoid 固有の "J_Bip_L_UpperArm" のみになっています。クラスコメントには「VRoid 以外のモデルでもそのまま動く」と書かれていますが、非 Humanoid 設定の別リグではフォールバックが機能しません。
修正提案: Humanoid リグ前提であることをコメントに明記するか、一般的なボーン名候補を追加してください。 
テスト不足の指摘（AssemblyInfo.cs 関連）
AssemblyInfo.cs により Ryona.PlayTests に internal が公開されていますが、本チャンクのクラス群に対して以下の境界・割り込みテストが特に重要です（未実装であれば追加を推奨します）： 
PlayerCombat の割り込み・バッファ境界テスト:
拘束解除直後（GrappleReleaseLockSeconds 以内）に J/K/L を連打しても攻撃が暴発せず、かつバッファにも残らないこと。
先行入力バッファ保持中に InputBufferSeconds を超えた場合、AttackData と同時に _bufferedStep も 0 にリセットされること（2段目左パンチが残らないこと）。
攻撃発生前（startupSeconds 未満）に被弾スタン・ダウン・拘束を受けた場合、ヒット判定が発生せず CancelAttack() で即座に打ち切られること。 
EncounterDirector と BeltScreenLock のウェーブ遷移テスト:
ArenaCount * WavesPerArena と MaxWaves の一致検証、および最終ウェーブ撃破後に BeltScreenLock.Release() が確実に呼ばれロックが解除されること。 
PauseMenu と HitStop の競合テスト:
HitStop.IsActive == true の最中にポーズし、そのままポーズ解除した際、Time.timeScale が 1f に戻らず 0f のままヒットストップに引き継がれること。 
他のチャンク（ThirdPersonCamera、EnemyBrain、PlayerGrapple、HitStop 等）も順次レビューしますか？それとも、今回指摘した Critical / High 項目の修正コード を先に作成しましょうか？
あなたがアップロードしたデータ
·
このカードは自分だけが閲覧できます
chunk-12.txt


## answer-13

# Chunk 13

Files:
- MioPrototype/Assets/RyonaGame/Runtime/PlayerFace.cs
- MioPrototype/Assets/RyonaGame/Runtime/PlayerMotor.cs
- MioPrototype/Assets/RyonaGame/Runtime/PlayerStats.cs
- MioPrototype/Assets/RyonaGame/Runtime/RenderSetup.cs
- MioPrototype/Assets/RyonaGame/Runtime/ThirdPersonCamera.cs
- MioPrototype/Assets/RyonaGame/Runtime/TitleGate.cs
- MioPrototype/Assets/RyonaGame/Runtime/VrmCharacterSlot.cs
- MioPrototype/Assets/RyonaGame/Runtime/VrmHairBodyMargin.cs
- MioPrototype/Assets/RyonaGame/Runtime/VrmHairSpring.cs
- MioPrototype/Assets/RyonaGame/Runtime/VrmHairWallClamp.cs
- MioPrototype/Assets/RyonaGame/Runtime/VrmLoadGate.cs

---

ご提示いただいた11ファイル（Chunk 13/26）のソースコードレビュー結果です。
確定できる事実（コード上のロジック欠陥・リソース管理不備）と推測（実行タイミングや外部クラスとの連携に依存する懸念）を明確に分けて記載します。
Critical（致命的：クラッシュ、リソースリーク、進行不能リスク）
1. VrmCharacterSlot.cs (L95–159, L287–306): VRMロード中の再入場・キャンセル未対応とVRAMリーク懸念
事実:
LoadAsync() に多重呼び出し（再入場）を防ぐガードや CancellationToken が存在しません。外部から連続して呼ばれた場合、SemaphoreSlim を順番に通過して複数のVRMインスタンスを生成し、Instance フィールドを上書きして古いインスタンスの参照を失います（リーク）。
Vrm10.LoadPathAsync 実行中にシーン破棄（OnDestroy）が走った場合、ロード完了後の L172 で DisposeVrm を呼んでいますが、コメント（L280–282）にある通り RuntimeGltfInstance.Dispose() は Destroy(gameObject) 相当であり、UniGLTF 0.131.4 の仕様上、生成された Mesh / Texture2D / Material の即時破棄（Object.Destroy）や Resources.UnloadUnusedAssets() を伴わない場合、ロードと破棄を繰り返すとVRAMが枯渇します。
修正提案:
bool _isLoading フラグと CancellationTokenSource を導入し、再入場を弾くとともに OnDestroy でキャンセルを通知してください。
DisposeVrm 内で RuntimeGltfInstance の所有リソースを明示的に破棄するか、シーン遷移時に Resources.UnloadUnusedAssets() を呼ぶ設計にしてください。
2. VrmCharacterSlot.cs (L117–122, L153–158): Time.timeScale の競合による進行停止リスク
事実:
held が true の場合に Time.timeScale = 0f とし、finally 節で FindFirstObjectByType<TitleGate>() == null && !PauseMenu.IsPaused のときのみ 1f に戻しています。
もし VRM ロード待機中に TitleGate や PauseMenu 以外のシステム（例：ヒットストップや演出カットシーン、別の VrmCharacterSlot）が Time.timeScale を操作していた場合、元の timeScale を復元できず 0f のまま進行不能（ソフトロック）になる可能性があります。
修正提案:
Time.timeScale を直接書き換えるのではなく、時間停止を管理する専用のスタック/トークン制のマネージャ（例: TimeScaleManager.PushPause(this) / PopPause(this)）を導入してください。
High（高：ロジックの破綻、Null参照、演出の競合）
3. PlayerFace.cs (L280–355): AudioClip をキーとした静的キャッシュのメモリリークと音声データ取得失敗
事実:
static readonly Dictionary<AudioClip, VoiceEnvelope> s_envelopes が静的（static）に保持されており、シーン遷移やアセットアンロード時にも一切クリアされません。動的に生成された AudioClip（ProceduralSfx）が渡され続けると、破棄済みの AudioClip キーと配列がメモリに残り続けます。
また、clip.GetData(data, 0)（L295）は、AudioClipのLoad Typeが Compressed In Memory や Streaming の場合、または Preload Audio Data がオフでロード完了前の場合に false を返して失敗します。
修正提案:
s_envelopes に上限サイズを設けるか、RuntimeInitializeOnLoadMethod やシーンアンロード時に Clear() するフックを追加してください。ProceduralSfx 側で生成時にエンベロープも同時に計算して渡す設計が最も安全で高速です。
4. PlayerFace.cs (L497–506, L586–587, L933–948): Stats 差し替え時のイベント購読漏れリスク
事実:
Update() 内で if (Stats == null) LinkStats(); を呼んでいますが、インスペクタや外部スクリプトから Stats が別の PlayerStats インスタンスに差し替えられた場合（Stats != null && Stats != _subscribed）、Subscribe() が呼ばれないため古い Stats のイベントを購読し続け、新しい Stats の被弾に反応しなくなります。
修正提案:
Update() の判定を if (Stats == null) LinkStats(); else if (_subscribed != Stats) Subscribe(); に変更してください。
5. PlayerMotor.cs (L200–205, L280): CharacterController.Move と LateUpdate の Z座標直接書き換えによる物理干渉
事実:
Update() で _controller.Move(...) を実行した後、LateUpdate() で transform.position = position（Z座標の強制上書き）を行っています。
Unity の CharacterController は transform.position を直接書き換えると内部のコライダー位置キャッシュと不整合を起こし、次のフレームの Move() で壁や敵のコライダーにめり込んだり、意図しない押し出し（テレポーテーション）が発生する原因になります（特に HasLaneZOverride による壁押さえつけ解除時）。
修正提案:
LateUpdate() で座標を直接代入するのではなく、Update() 内の _controller.Move() に z 軸の補正量 (targetZ - transform.position.z) を含めて移動させてください。
6. ThirdPersonCamera.cs (L389–460, L666–734): カメラ演出（HoldFocus と RequestCinematicShot）の競合と状態の不整合
事実:
HoldFocus で _holdingFocusBelt = true にした直後に RequestCinematicShot が呼ばれると、LateUpdate()（L281–282）では TickBelt() でフォーカス時間を消費せず維持しつつ、直後の ApplyCinematicShot() でカメラ座標を上書きします。
さらに、CinematicExitSeconds（L410–420）のブレンド終了直前に別の RequestCinematicShot(cut: false) が来ると、_shotPosition が「ブレンド途中のカメラ位置」から再スタートするため、カメラがガクッと跳ねる挙動になります。
修正提案:
カメラの制御モードを明確なステートマシン（Normal / BeltFocus / Cinematic / CinematicExit）として排他制御し、優先度（例: Cinematic > BeltFocus > Normal）を明文化してください。
7. PlayerStats.cs (L388–389, L269–313): ForceDown 呼び出し時の LastHitZone 残留による誤演出の懸念
推測（外部連携の懸念）:
ReceiveHit（L389）から ForceDown(..., "flinch", attackerTransform) を呼んだ際、LastHitZone はクリアされません。コメント（L145–148）には「投げの経路では明示的に消す」とありますが、スリップダメージや環境ダメージ、または LastHitZone を更新しない攻撃で AddFlinch が閾値に達した場合、直前の殴打の LastHitZone（例: 腹パン）が残ったままダウン表情やモーションが再生される可能性があります。
修正提案:
ReceiveHit の引数に PunchZone? hitZone = null を統合し、呼び出し元が毎回明示的に部位を渡す（または未指定時は null で上書きする）設計にして、状態の持ち越しを構造的に防いでください。
Medium（中：パフォーマンス・GCスパイク、エッジケースのバグ）
8. ThirdPersonCamera.cs (L372–387): -facelog 有効時の毎フレーム全検索（GC・CPU負荷）
事実:
デバッグオプション -facelog 有効時とはいえ、LateUpdate() の中で毎フレーム FindObjectsByType<EnemyFigure>(FindObjectsSortMode.None) を実行しています。敵の数が増えるとフレームレートが著しく低下し、計測ログ自体のタイミングが狂います。
修正提案:
EnemyFigure の生成・破棄時に静的リスト（EnemyFigure.All）へ登録・解除する仕組みにし、そのリストを参照してください。
9. VrmHairSpring.cs (L52–54) & VrmHairWallClamp.cs (L41–42): LateUpdate での FindFirstObjectByType 毎フレーム呼び出し
事実:
シーン上に HeldBellyPunchDirector や PlayerGrapple が存在しない場合（あるいは非アクティブな場合）、_punch == null / _grapple == null が真になり続け、毎フレーム LateUpdate でシーン全体のコンポーネント検索（FindFirstObjectByType）が走ります。
修正提案:
検索間隔にクールダウン（例: 0.5秒ごと、または PlayerStats のように親・ルートから GetComponentInParent で1回取得）を設けてください。
10. VrmHairWallClamp.cs (L19, L39–70): UniVRM 1.0 の SpringBone 実行順との競合
事実:
[DefaultExecutionOrder(26000)] の LateUpdate() で joint.transform.position を直接書き換えています。
推測（UniVRM 1.0 仕様依存）: UniVRM 1.0 の Vrm10RuntimeSpringBone は内部のジョブシステムや LateUpdate（通常デフォルト実行順 0 付近、または Vrm10Instance の更新タイミング）で物理計算を行い、次フレームの初期値として前フレームの座標を保持します。座標を直接 position 代入で押し戻した場合、SpringBone の内部速度（前フレーム差分）が「壁から手前に猛スピードで動いた」と誤認し、次フレームで髪が手前側へ不自然に跳ね返る（バウンスする）現象が起きる可能性があります。
修正提案:
実機で髪がブルブルと振動・跳ね返りをしていないか確認し、問題がある場合は VRM10SpringBoneCollider（Plane または Capsule）を壁の位置に動的配置して、物理エンジン側で衝突解決させる方式を検討してください。
11. PlayerFace.cs (L649–677, L911–927): GetComponentsInChildren<SkinnedMeshRenderer> の重複走査と GC 発生
事実:
FindWorriedBrow() と EyeCloseBlend() で毎回 GetComponentsInChildren<SkinnedMeshRenderer>(true) を呼んで配列をアロケートしています。特に FindWorriedBrow() は必要なメッシュが見つからない場合、WriteShape() 経由で毎フレーム呼ばれます（L1076）。
修正提案:
Vrm 割り当て時に一度だけ SkinnedMeshRenderer[] をキャッシュし、探索リトライにもタイマー（LinkStats と同様の 0.5秒間隔）を設けてください。
12. PlayerMotor.cs (L125, L240, L312): Update() や攻撃ごとの GetComponent 呼び出し
事実:
Update() 内で毎フレーム GetComponent<PlayerWristLock>()（L240）を呼んでいます。また Lunge 開始時（L125）と終了時（L312）に GetComponentInChildren<ProceduralRig>() を呼んでいます。
修正提案:
Awake() または遅延初期化でフィールドにキャッシュしてください。
Low（低：保守性、軽微な境界値、テスト不足）
13. RenderSetup.cs (L29–43): 複数シーンロード・リトライ時のライト重複設定
事実:
[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)] はアプリ起動後の最初のシーンロード時に1回だけ呼ばれます。タイトルからゲーム本編へ別のシーンをロードする構成になった場合、Boot() は再実行されません（単一シーン完結なら問題ありません）。
修正提案:
マルチシーン構成を想定する場合は SceneManager.sceneLoaded にフックしてください。
14. TitleGate.cs (L110–134): AnyPressed() の入力二重検出と入力システム混在
事実:
#if ENABLE_INPUT_SYSTEM と #if ENABLE_LEGACY_INPUT_MANAGER の両方が有効（Unity の Both 設定）な環境では、新Input System側で false だった場合にレガシー側の Input.anyKeyDown にフォールスルーします。また、ゲームパッドのスティック押し込みやトリガーでは反応しません（仕様通りなら問題ありません）。
修正提案:
プロジェクトの Active Input Handling 設定に合わせて #elif に整理すると意図が明確になります。
15. PlayerFace.cs (L291–325): 音声エンベロープ計算のステレオチャンネル無視
事実:
float v = data[f * channels];（L317）としており、ステレオ音声（channels == 2）の場合は左チャンネル（ch 0）のみをサンプリングしています。右チャンネルにのみ音がある特殊効果音やパンニングされたボイスでは無音判定になります。
修正提案:
全チャンネルの二乗和平均を取るか、コメントに「モノラルまたはLチャンネル基準」と明記してください。
16. テスト不足の指摘（全体）
以下の境界条件に対する EditMode / PlayMode テストの追加を推奨します。
VrmCharacterSlot: ロード完了前に Destroy されたケース、およびロード中に PauseMenu.IsPaused = true になったケースで Time.timeScale が 0f に保たれることの検証。
PlayerFace: 拘束中（UsesHeldFace == true）に HurtVoicePlayed が発火した際、_squeezeUntil が延長されず、かつ連撃の合間に目が開くタイミング（HeldSqueezeSeconds）の検証。
ThirdPersonCamera: BeltMode 中に HoldFocus と RequestCinematicShot が連続・重複して呼ばれた際、終了後に BeltDeadZone や GateMaxX のクランプへ滑らかに復帰することの検証。
PlayerMotor: HasLaneZOverride の切り替えと Knockback / LaunchBack が同時に発生した際、壁や床をすり抜けないことの検証。
今回のレビュー結果をもとに、さらに詳しく調査・修正案を作成できます。ご希望の項目があれば教えてください：
特定のファイル（例：VrmCharacterSlot.cs や ThirdPersonCamera.cs）の修正版コードの提示
PlayMode テストコード（NUnit / Unity Test Framework）の具体的な実装例
他チャンク（CharacterAnimator や PlayerGrapple など）との連携部分の整合性チェック


## answer-14

# Chunk 14

Files:
- MioPrototype/Assets/RyonaGame/Runtime/Debug/AttackTestDriver.cs
- MioPrototype/Assets/RyonaGame/Runtime/Debug/CameraProbe.cs
- MioPrototype/Assets/RyonaGame/Runtime/Debug/DownTestDriver.cs
- MioPrototype/Assets/RyonaGame/Runtime/Debug/EncounterTestDriver.cs
- MioPrototype/Assets/RyonaGame/Runtime/Debug/FistShot.cs
- MioPrototype/Assets/RyonaGame/Runtime/Debug/GrappleTestDriver.cs
- MioPrototype/Assets/RyonaGame/Runtime/Debug/GroundMountTestDriver.cs
- MioPrototype/Assets/RyonaGame/Runtime/Debug/HaulDemoDriver.cs
- MioPrototype/Assets/RyonaGame/Runtime/Debug/HeldPunchProbe.cs
- MioPrototype/Assets/RyonaGame/Runtime/Debug/HudShot.cs
- MioPrototype/Assets/RyonaGame/Runtime/Debug/MotionShortcut.cs
- MioPrototype/Assets/RyonaGame/Runtime/Debug/PhotoShot.cs
- MioPrototype/Assets/RyonaGame/Runtime/Debug/PinHotkey.cs
- MioPrototype/Assets/RyonaGame/Runtime/Debug/SpacingProbe.cs

---

チャンク 14（Runtime/Debug/*.cs 14ファイル）のコードレビュー結果を重大度別に整理しました。
（デバッグ用ドライバ群であるため、通常ビルドへの混入リスクや、検証自体の信頼性を損なう不具合・リークを中心に評価しています。） 
Critical（致命的：リリース混入によるゲーム破壊・イベントリーク）
1. リリースビルドでも常駐する MotionShortcut と数字キー誤爆による無敵化・ゲーム進行破壊
対象: MotionShortcut.cs 13–15, 82–93, 122–133, 283 
事実: PinHotkey.cs（45行目）は #if UNITY_EDITOR || DEVELOPMENT_BUILD で保護されていますが、MotionShortcut にはコンパイル条件も起動引数チェックもなく、BeforeSceneLoad / AfterSceneLoad で無条件に生成・UI表示されます。さらに ExecuteShortcut（283行目）内で _player.SetGodMode(true) を呼び出し、リセット（0 キー）でも解除しません。 
影響: 出荷ビルド（プレイヤー環境）で画面左上にデバッグUIが出たままになり、数字キー＋Enterの誤操作でプレイヤーが永久無敵化し、ゲーム進行や敵位置が破壊されます。
修正提案: MotionShortcut.cs 全体を #if UNITY_EDITOR || DEVELOPMENT_BUILD で囲むか、LaunchOptions.Has("-motionshortcut") 等が明示された場合のみ Ensure() で生成するように変更してください。また id == 0 でのリセット時に _player.SetGodMode(false) を呼んでください。
2. GrappleTestDriver のイベント購読解除漏れ（リーク・破棄済み参照エラー）
対象: GrappleTestDriver.cs 201–233
事実: _player.HitReceived、_grapple.Started / Escaped / Failed、_belly.Performed にラムダ式・メソッドを購読していますが、OnDestroy 等での購読解除（-=）が存在しません。
影響: シーン再読み込みやドライバ破棄後も PlayerStats 側にデリゲートが残り、破棄済み MonoBehaviour へのアクセス（MissingReferenceException）や多重撮影・多重ログが発生します。
修正提案: ラムダ式をフィールドに保持し、OnDestroy() 内で必ず -= による購読解除を行ってください。 
High（高：Null参照例外・検証ロジックの破綻・異常終了）
3. 撮影先ディレクトリ未作成による ScreenCapture.CaptureScreenshot の失敗
対象: DownTestDriver.cs 174–176, FistShot.cs 71–77, GrappleTestDriver.cs 386–396, 410–420, 608–618, GroundMountTestDriver.cs 183–189, MotionShortcut.cs 161–168 
事実: HaulDemoDriver.cs（114行目）では Directory.CreateDirectory(_shotDir) を呼んでいますが、他のすべての撮影ドライバではディレクトリを作成せずに ScreenCapture.CaptureScreenshot(...) を呼んでいます。また Path.GetDirectoryName("out.png") のようにディレクトリ部がないパスを渡すと dir が "" や null になり、Path.Combine(null, ...) で ArgumentNullException が発生します（GroundMountTestDriver.cs 185行目のみ空文字チェックあり）。 
影響: 存在しないサブディレクトリを指定した場合の保存失敗や、相対ファイル名のみ指定時の例外クラッシュが起きます。
修正提案: Path.GetFullPath() で正規化した上で Directory.CreateDirectory(dir) を呼ぶ共通ヘルパーを用意し、全ドライバから利用してください。
4. FistShot における LHand の Null 参照リスク
対象: FistShot.cs 44–50
事実: 44–46行目で anim.Rig.RHand は null チェックしていますが、49行目の anim.Rig.LHand.position は null チェックなしでアクセスしています。
影響: 左手ボーンが未割り当てのリグ構成で NullReferenceException が発生します。
修正提案: Transform lhand = anim.Rig.LHand; if (hand == null || lhand == null) return; のように両方チェックしてください。 
5. DownTestDriver における State の Null 参照と敵位置の強制移動による検証の歪み
対象: DownTestDriver.cs 63, 78–81, 135–146, 163
事実: 104行目では _stats.State != null をチェックしていますが、63行目（_target.State.RestoreAll() ※AttackTestDriver）や DownTestDriver.cs 163行目（_stats.State.Flinch）では null チェックがありません。また、DownTestDriver 78–81行目で全敵をプレイヤーの真横（±3.2m）へ強制配置したあと、135–146行目で「一番近い敵」を探して攻撃元にしているため、攻撃元が必ず真横の敵になり、吹き飛び方向が本来の正面からではなく横方向へ歪みます。
推測: 「絵が隠れないように横へ離す」意図が、吹き飛び方向の計算と干渉している可能性が高いです。
修正提案: 攻撃役となる敵1体は正面（または背後）に配置し、それ以外の敵だけを横へ退避させてください。 
6. MotionShortcut の連続実行・シーン遷移時の状態不整合と再入場問題
対象: MotionShortcut.cs 67, 138–141, 261–339, 394–442 
事実:
DontDestroyOnLoad が付いていないため、シーン再読み込み時に OnSceneLoaded で新規生成されますが、static bool _autoParsed がリセットされず、-motionseq 指定時にシーン遷移のたびに RunAutoSequence が再実行されます。
PrepareCleanState（415行目）で _anim?.ResetForMotionPreview() を呼んだ直後、同じフレーム内で ForceStartBackGrab や ForceDown を呼んでいますが、相手側 EnemyBrain のコルーチンやタイマーが完全にはリセットされておらず（ResetForMotionPreview 実装依存）、前モーションの残存状態と競合するリスクがあります。
LaunchWallPin（494行目）で _screenLock.enabled = false にしたあと、PrepareCleanState(keepWallSite: id == 4) を呼ぶ順序は問題ありませんが、壁拘束中に別の番号へ切り替えた際、プレイヤーが壁際（レーン外）に取り残されたまま TeleportPlayer で Z だけ戻されるため、X 座標がステージ端のままになる場合があります。 
修正提案: _autoIds 実行済みフラグ（static bool _autoExecuted）を設けて多重実行を防ぎ、番号切替時にステージ中央付近へ X 座標を戻すオプションまたは基準位置の保持を検討してください。
Medium（中：カメラ競合・コルーチン寿命・境界値・GC負荷）
7. 複数のカメラ制御ドライバ・コンポーネント間の LateUpdate 競合
対象: GrappleTestDriver.cs 632–668, GroundMountTestDriver.cs 128–173, PhotoShot.cs 214–218, 332–335
事実: PhotoShot は ThirdPersonCamera.enabled = false にして競合を回避していますが、GrappleTestDriver（-tagcam）と GroundMountTestDriver（-mountwide）は ThirdPersonCamera を無効化せず、DefaultExecutionOrder(30000) の LateUpdate で Camera.main.transform を直接上書きしています。
影響: ThirdPersonCamera が内部状態（スムージング速度やターゲット追従座標）を更新し続けるため、演出終了後にカメラが急激に跳ねたり、実行順によっては1フレーム単位でカメラがガタつく原因になります。
修正提案: GrappleTestDriver や GroundMountTestDriver がカメラを制御する期間も ThirdPersonCamera を一時無効化するか、カメラ側に外部オーバーライド用の API を設けてください。 
8. PhotoShot の PoseFreezer が破棄されず CharacterAnimator を永久停止させる
対象: PhotoShot.cs 414–425, 493–495
事実: PoseFreezer.Setup で全 CharacterAnimator.enabled = false にします。通常は Application.Quit() でプロセス終了するため問題になりませんが、Unity Editor 上で再生確認した場合、Playモードを止めるまでアニメータが停止したままになり、また Update/LateUpdate 毎フレーム ForceState と ApplyPose を呼び続けます。
修正提案: #if UNITY_EDITOR 時には EditorApplication.isPlaying = false を呼ぶ処理を追加するとエディタでのテスト効率も向上します。 
9. 毎フレームの FindObjectsByType 呼び出しによるパフォーマンス・GC負荷
対象: AttackTestDriver.cs 52, 54, 71, 108, 115 / GrappleTestDriver.cs 269, 330, 356, 642 / MotionShortcut.cs 705, 766
事実: AttackTestDriver.Update() では毎フレーム FindFirstObjectByType<PlayerStats>() と FindObjectsByType<EnemyBrain>() を2回（Update 内と FindNearest 内）実行しています。MotionShortcut.Update() も _freezeAi 中は毎フレーム FindObjectsByType<EnemyBrain>() を走査します。
影響: デバッグ用とはいえ、プロファイリングやフレームレート計測時にノイズとなり、敵の数が増えるとスパイクの原因になります。
修正提案: PlayerStats や EnemyBrain のリストをキャッシュし、必要なタイミング（スポーン時や数秒間隔）でのみ再取得してください。 
10. GrappleTestDriver.MaxSkinnedZ の Mesh 生成と Bounds 計算の冗長性
対象: GrappleTestDriver.cs 499–577 
事実:
LogWallClearance() が呼ばれるたびに new Mesh() を生成・BakeMesh・Object.Destroy(mesh) しています（0.25秒ごと）。
548–572行目で、MeshRenderer の Bounds 計算ブロック（548–562行目）と全く同じ計算（564–572行目）が重複記述されています。また 567行目に + Mathf.Abs(mf.transform.lossyScale.z) * 0f という無意味な式が残っており、ee（ローカルの bounds.extents）に lossyScale が掛けられていないため、スケール変更されたオブジェクトでは Z 範囲が狂います。 
修正提案: 作業用 Mesh インスタンスはフィールドで使い回し（OnDestroy で破棄）、重複ブロックを削除した上で e に lossyScale を乗算してワールド空間の extents を算出してください。
11. DownTestDriver 等のコマンドライン引数解析の境界値・異常値処理
対象: DownTestDriver.cs 43, HudShot.cs 37–54, MotionShortcut.cs 103–119, PhotoShot.cs 97–150 
事実:
HudShot で -hudshotdelays "" や負の秒数（-hudshotdelay -5）が渡された場合、0 秒扱いで即座に撮影・終了します。
PhotoShot で -shotfov 0 や負数が渡されると Camera.fieldOfView に不正値が入ります。
DownTestDriver で -downhits=unknown のような未定義文字列が渡された場合、警告を出さずに黙って AttackLibrary.Light() にフォールバックします。 
修正提案: パース時に Mathf.Clamp で有効範囲を制限し、未知のオプション値には Debug.LogWarning を出力してください。
Low（低：コード品質・保守性・テスト不足）
12. 撮影・連写コルーチンの重複実装
対象: DownTestDriver.cs 166–186, GrappleTestDriver.cs 375–425, 603–625, GroundMountTestDriver.cs 175–199, MotionShortcut.cs 159–171
事実: 「指定間隔・指定枚数で ScreenCapture.CaptureScreenshot を連番保存する」処理が 5 箇所にコピー＆ペーストされており、ファイル名のゼロ埋め桁数（_00 と _000）や終了時 Application.Quit() の有無・待機時間（0.6f と 1.2f）がバラバラです。
修正提案: DebugScreenshotBurst のような共通ユーティリティクラスに抽出し、ファイル命名規則と完了待機を統一してください。 
13. GrappleTestDriver の責務肥大化と未使用変数
対象: GrappleTestDriver.cs 全体の 726 行
事実: 通常の掴みテスト、壁押さえつけテスト、壁貫通メッシュ検査（MaxSkinnedZ）、タッグ腹パン撮影、タッグ専用カメラ（-tagcam）、接触座標ログ（LogContact）が1クラスに混在しています。また、LogContact() 内のローカル変数 aR, aL（714–715行目）はログ出力名 gripR={aR:F2} として使われていますが、コメントや変数名が dR（肩・胸との距離）と紛らわしくなっています。
修正提案: 壁クリアランス計測（WallClearanceProbe）や接触計測は CameraProbe / SpacingProbe と同様に独立した Probe コンポーネントへ分離することを推奨します。 
14. デバッグドライバ自体の結合テスト・Smokeテスト不足
対象: 全14ファイル
事実: これらのスクリプトは他クラス（EnemyBrain, PlayerStats, CharacterAnimator, ProceduralRig, WallPinSites など）の内部状態や実行順（DefaultExecutionOrder(25000) / 26000 / 30000）に強く依存していますが、起動引数を渡してエラーなく完了することを検証する自動テスト（PlayMode Test）が見当たりません。
修正提案: CI やバッチビルド時に -shotpath / -motionseq 1,2,3 等を短時間実行し、Debug.LogError や例外が出ないことを検証する Smoke テストを追加すると、本体側のリファクタリングによるデバッグツールのサイレント破壊を防げます。 
まとめ（確定事実と推測の整理）
確定事実: MotionShortcut のリリースビルド常駐と SetGodMode(true) の未解除、GrappleTestDriver のイベント購読未解除、MaxSkinnedZ のコード重複と lossyScale 欠落、複数箇所でのディレクトリ未作成および LHand の null チェック漏れ。
推測（仮説）: ThirdPersonCamera との競合によるカメラのガタつき、DownTestDriver での敵退避処理による吹き飛び方向の歪み、本体側の ResetForMotionPreview() 実装次第で発生する状態残留。 
特に優先して修正・リファクタリングしたい項目はありますか？以下を指定いただければ、具体的な修正コード案を作成します：
リリース混入・リーク対策（MotionShortcut のビルド除外、GrappleTestDriver の購読解除）
撮影ユーティリティの共通化（ディレクトリ自動生成・連写処理の統合）
本体側との連携確認（PlayerStats や EnemyBrain の ResetForMotionPreview 等の実装確認）
あなたがアップロードしたデータ
·
このカードは自分だけが閲覧できます
chunk-14.txt


## answer-15

# Chunk 15

Files:
- MioPrototype/Assets/RyonaGame/Editor/AutoPlayerBuild.cs
- MioPrototype/Assets/RyonaGame/Editor/CharacterPreview.Enemy.cs
- MioPrototype/Assets/RyonaGame/Editor/CharacterPreview.Face.cs
- MioPrototype/Assets/RyonaGame/Editor/CharacterPreview.Hand.cs
- MioPrototype/Assets/RyonaGame/Editor/CharacterPreview.Hurt.cs
- MioPrototype/Assets/RyonaGame/Editor/CharacterPreview.Joints.cs
- MioPrototype/Assets/RyonaGame/Editor/CharacterPreview.Measure.cs

---

ご提示いただいた7ファイル（エディタ拡張・診断ツール群）について、不具合・null参照・リソースリーク・状態汚染・パフォーマンス/GC・保守性の観点からコードレビューを行いました。 
Critical（致命的・実行時例外やエディタ状態の破壊）
1. vrm == null チェックの欠落による NullReferenceException
該当箇所: CharacterPreview.Hurt.cs L37-42, L178-183, L291-296
事実: Vrm10.LoadPathAsync(...).GetAwaiter().GetResult() の直後、vrm == null のチェックを行わずに vrm.gameObject.AddComponent<ProceduralRig>() を呼んでいます（他のメソッドでは if (vrm == null) return; があります）。
推測: VRM ファイルが破損している場合やロード失敗時に NullReferenceException で停止し、以降の後処理もスキップされます。
修正提案: if (vrm == null) { Debug.LogError("[Ryona] VRM load returned null"); return; } を追加してください。 
2. GetAwaiter().GetResult() によるメインスレッドのデッドロックリスク
該当箇所: CharacterPreview.Enemy.cs L200, L284, L372, L471, L560 / Face.cs L25, L86, L186, L300, L432, L539 / Hand.cs L31, L109, L203, L304, L435, L526 / Hurt.cs L39, L180, L293 / Joints.cs L31, L128, L336 / Measure.cs L33, L234, L338, L435, L522, L608 
事実: Unity エディタの同期コンテキスト（メインスレッド）上で非同期メソッド Vrm10.LoadPathAsync を .GetAwaiter().GetResult() で同期ブロックしています。 
推測: UniVRM のバージョンや内部実装（await Task.Yield() や SynchronizationContext へのポスト）によっては、メインスレッドがブロックされたまま継続処理が実行されずエディタが完全フリーズ（デッドロック）する危険があります。
修正提案: EditorApplication.update を用いたコルーチン風ステートマシンか、async void（完了後に後処理を行う形）へ移行することを推奨します。
High（リソースリーク・テスト条件の破損・異常終了）
3. 動的生成した Material / Texture2D / GameObject のリーク
該当箇所:
CharacterPreview.Enemy.cs L25-32, L116-119（floorMat, skin, cloth 未破棄）
CharacterPreview.Face.cs L132（Fcl_ALL_Angry not found 時の早期 return で vrm.gameObject, camGo, before がリーク）
CharacterPreview.Hand.cs L62（指ボーンが見つからない 時の早期 return で vrm.gameObject がリーク）
CharacterPreview.Joints.cs L360-363（boneMat, jointMat 未破棄）
CharacterPreview.Measure.cs L663（MeasureDownClearance の末尾で vrm.gameObject が未破棄） 
事実: NewScene でシーン内オブジェクトは破棄されますが、スクリプトから new Material(...) や new Texture2D(...) で生成したアセットはシーンに属さないためメモリ（Native/GPU メモリ）に残り続けます。また、早期 return パスで DestroyImmediate が呼ばれていません。 
修正提案: try { ... } finally { ... } ブロックで囲み、生成した Material, Texture2D, GameObject を確実に Object.DestroyImmediate で解放してください。
4. CharacterAnimator の静的オーバーライド状態の残留（再入場・他テストへの汚染）
該当箇所: CharacterPreview.Hurt.cs L235, L250, L338, L355 / Measure.cs L377-380
事実: anim.PreviewHitZone = ... や anim.StrugglePulseOverride = ... を設定した後、メソッドの最後でリセットしていますが、try-finally で保護されていません。
推測: 途中の処理で例外が発生した場合、PreviewHitZone や StrugglePulseOverride が上書きされたまま残り、その後のプレビューやプレイモードの挙動が壊れる可能性があります（特に static フィールドである場合）。
修正提案: try-finally の finally 節で必ず初期値（null や -1f）に戻してください。 
5. ScanJapaneseFont で動的フォントがリークし、判定も不完全
該当箇所: CharacterPreview.Hand.cs L391-407
事実: Font.CreateDynamicFontFromOSFont で生成した Font インスタンスが破棄されていません。また、HasCharacter はテクスチャに文字をリクエスト（RequestCharactersInTexture）する前に呼ぶと、環境によってはキャッシュ未生成で false を返すことがあります。
修正提案: font.RequestCharactersInTexture("あ倒ヒ"); を呼んでから HasCharacter を確認し、最後に Object.DestroyImmediate(font); を呼んでください。 
6. 非公開 API InternalEditorUtility.isApplicationActive への依存
該当箇所: AutoPlayerBuild.cs L109 
事実: UnityEditorInternal 名前空間の内部 API を使用しています。コメントに Unity 6.4 での存在確認済みとありますが、パッチアップデートでシグネチャ変更や削除が行われるリスクがあります。 
修正提案: リフレクションでフォールバックを持たせるか、将来的なアップデート時のコンパイルエラーに備えてラッパーメソッドに隔離しておくと安全です。
Medium（ロジックの不具合・意図しない挙動・GC負荷）
7. 冗長な条件式（コピペミスと思われる箇所）
該当箇所: CharacterPreview.Hand.cs L244
事実: anim.ForceGuardRecoil(c.state == CharacterAnimator.State.Guard ? 0f : 0f); と記述されており、true でも false でも 0f が渡されています。
推測: ガード状態とそれ以外で別の値（例: 1f と 0f）を試そうとして書き換えた名残と考えられます。
修正提案: 意図通り 0f 固定でよければ anim.ForceGuardRecoil(0f); に簡略化し、ガード中の反動も検査したい場合はケースにガード被弾を追加してください。 
8. ループ内での GetPixels() / SetPixels() による大量の GC アロケーション
該当箇所: CharacterPreview.Enemy.cs L85, L162, L246, L334, L433, L524, L616 / Face.cs L258, L362, L491, L599, L677, L747 / Hurt.cs L83, L99, L112, L132, L241, L343, L432, L517, L599 / Joints.cs L401 / Measure.cs L301, L397, L482, L569
事実: 各セルを描画するたびに cell.GetPixels() で Color[] 配列をヒープに確保し、sheet.SetPixels(...) に渡しています（例: HurtTiers では 300×620×32枚 = 約600万要素の Color 構造体＝約96MBのマネージドメモリを一時確保）。
推測: エディタ拡張とはいえ、連続実行時に GC スパイクやメモリ断片化を引き起こします。
修正提案: Graphics.CopyTexture(cell, 0, 0, 0, 0, cellW, cellH, sheet, 0, 0, dstX, dstY) を使うと、CPU 側へのピクセル読み戻しと配列アロケーションをゼロにできます（フォーマット一致時）。 
9. MeasureExposure の背景色判定が RenderTexture の色空間（Linear / Gamma）に影響を受ける
該当箇所: CharacterPreview.Measure.cs L81, L145-151
事実: RenderTextureFormat.ARGB32（デフォルトの色空間設定に依存）で描画し、ReadPixels で取得した値と固定の new Color(0.15f, 0.35f, 0.15f) を 0.06f の誤差範囲で比較しています。
推測: プロジェクトの Color Space が Linear の場合、シェーダ出力と ReadPixels 後のガンマ変換により bg の値にズレが生じ、IsBg が誤判定してシルエット全体のバウンディングボックスが画面全体（0..w, 0..h）になる恐れがあります。
修正提案: RenderTextureReadWrite.sRGB（または Linear）を明示指定するか、背景色と肌色の判定をアルファチャンネル（背景 alpha = 0、メッシュ alpha = 1）で行うと色空間に依存せず確実に判定できます。 
10. AutoPlayerBuild におけるビルド失敗時の無限リトライ防止と間隔設定
該当箇所: AutoPlayerBuild.cs L86-97, L115-127
事実: PlayerBuild.BuildWindows() が例外を投げた場合、_building は finally で false に戻りますが、_dueAt は -1.0 のままなのでリトライされません。一方で、コンパイル失敗時はドメインリロードが走らないため静的コンストラクタが呼ばれず、自動ビルドは走らない仕様になっています。
推測: BuildWindows() 内部でエラーログを出して正常終了した場合、PlayerBuild.IsStale() が true のまま残っていると、次に何かトリガーされた際に何度も失敗ビルドを繰り返す可能性があります。
修正提案: 失敗回数カウンタを設けるか、ビルド成否をログ出力して状態を明確にしておくと運用しやすくなります。 
Low（コード品質・保守性・デバッグ出力の改善）
11. デバッグログの出力内容の誤記・未完成箇所
該当箇所:
CharacterPreview.Enemy.cs L88: Debug.Log($"[EnemyStrip] {s.label} handY={figure.name}"); → handY と書いているのに figure.name を出力しています。
CharacterPreview.Measure.cs L573: Debug.Log($"[Recoil] t={times[i]:F2} head={rig.Head.position} torsoPitchApplied"); → 末尾の文字列が固定リテラルになっています。 
修正提案: figure.WristBone(false).position.y や anim.DebugTorsoPitch など、本来確認したかった値に修正してください。
12. 7ファイル間でボイラープレートコードが大量に重複している
該当箇所: CharacterPreview.*.cs 全般
事実: VRM のロード、ProceduralRig / CharacterAnimator のアタッチ、forceMatrixRecalculationPerRender = true の設定、Floor プリミティブの生成、カメラの初期化、PNG 保存処理がほぼすべてのメソッド（20箇所以上）にコピペされています。
推測: 今後 VRM のロード方法やライト・床の設定を変更する際、20箇所以上を修正する必要があり修正漏れが発生します。
修正提案: LoadPreviewVrm(out Vrm10Instance vrm, out ProceduralRig rig, out CharacterAnimator anim) や CreateFloor()、SaveSheet(Texture2D sheet, string fileName) といったヘルパーメソッドを CharacterPreview.Measure.cs（共通ユーティリティ側）に集約してください。 
13. コメントと実数値の乖離
該当箇所:
AutoPlayerBuild.cs L15: コメントでは Ryona/Auto Build Player と記載されていますが、実際の [MenuItem]（L60）は Ryona/Auto Build Player (On Compile) です。
CharacterPreview.Face.cs L169: コメントでは 上から A〜H、最後の 1 マスは無表情（計9個）とありますが、variants 配列も9個で一致しているものの、L622 の XML ドキュメントコメントが直下の CaptureTorsoPitch ではなく削除されたメソッドのものとして残っています。
CharacterPreview.Measure.cs L418: コメントでは 0.1 秒刻み とありますが、実際の times は 0.02f, 0.18f, 0.34f...（0.16秒刻み）です。 
修正提案: ドキュメントコメントを現在の実装に合わせて更新してください。
他のチャンク（CharacterAnimator や ProceduralRig、PlayerBuild など）との整合性も確認できます。もしよろしければ：
特に重点的に見たい 特定のファイルや機能（例：自動ビルドの安全性、関節チェックの判定ロジックなど）
修正用の リファクタリング済みコード例 が必要な箇所
があれば教えてください。
あなたがアップロードしたデータ
·
このカードは自分だけが閲覧できます
chunk-15.txt


## answer-16

# Chunk 16

Files:
- MioPrototype/Assets/RyonaGame/Editor/CharacterPreview.MeshDump.cs
- MioPrototype/Assets/RyonaGame/Editor/CharacterPreview.Motion.cs
- MioPrototype/Assets/RyonaGame/Editor/CharacterPreview.Sheets.cs
- MioPrototype/Assets/RyonaGame/Editor/CharacterPreview.Stance.cs
- MioPrototype/Assets/RyonaGame/Editor/CharacterPreview.cs
- MioPrototype/Assets/RyonaGame/Editor/EnemyVrmCheck.cs

---

チャンク 16（エディタ用プレビュー・診断ツール群 6 ファイル）のレビュー結果です。全体として未保存シーンへの破壊的遷移リスクや、静的状態・ランタイムリソース（Mesh/Material/VRM）のリークが目立ちます。 
以下に重大度別の指摘事項をまとめます。
Critical（即時修正推奨：データ損失・深刻な不具合）
未保存の作業中シーンが警告なしで破棄される
場所: CharacterPreview.cs:30, 111, 203, 283, 374、CharacterPreview.MeshDump.cs:25, 139, 253, 295, 355, 423, 484, 549, 603, 754, 803, 859, 937、CharacterPreview.Motion.cs:20, 147, 233, 317, 387, 465、CharacterPreview.Sheets.cs:27, 127, 236, 315, 403, 502, 666, 767, 896、CharacterPreview.Stance.cs:32, 117, 252, 340, 476, 576, 666, 782、EnemyVrmCheck.cs:56
事実: 全メニュー項目で EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single) を保存確認なしで直接呼び出しています。
修正提案: 実行前に if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return; を挟み、ユーザーの作業内容保護を行ってください。 
スロット未検出時の IndexOutOfRangeException / NullReferenceException
場所: CharacterPreview.MeshDump.cs:51-68 (SurfaceMap) および 623-642 (DeformMap)、CharacterPreview.cs:954 (IdArms)
事実: skinSlot や pantsSlot が -1 のまま mesh.GetTriangles(sub) や ExtractSubMesh に渡される経路があり、CrotchCheck (CharacterPreview.cs:310) のような事前ガードが欠落しています。IdArms (MeshDump.cs:951) でも body == null チェックがありません。
修正提案: if (body == null || skinSlot < 0 || pantsSlot < 0) { Debug.LogError(...); return; } を各メソッド冒頭に追加してください。 
静的フィールドの汚染による他処理・プレイモードへの副作用
場所: CharacterPreview.Sheets.cs:182-185, 272-273, 350-351、CharacterPreview.Stance.cs:521-522、CharacterPreview.Motion.cs:520
事実: CharacterAnimator.StanceArmLUpper や StanceHandRollL、WalkDirectionOverride などの静的プロパティを上書きした後、例外発生時や一部メソッドの終了時に元の値へ戻していません。
修正提案: 変更前の値を退避し、try { ... } finally { ... } ブロックで確実に復元してください。 
High（リソースリーク・状態不整合・null参照）
VRM・Mesh・Material・GameObject のメモリリーク
場所:
CharacterPreview.Motion.cs:70-75 (CaptureHeavySwing で figureGo, camGo, floor が未破棄)
CharacterPreview.Motion.cs:553-556 (CapturePlayerWalkCycle で vrm.gameObject が未破棄)
CharacterPreview.MeshDump.cs:649-650 (DeformMap の Bake 内で毎回 new Mesh() を生成し破棄していない)
CharacterPreview.cs:240, 420 / CharacterPreview.MeshDump.cs:193, 210, 224 (new Material(...) の未破棄)
CharacterPreview.cs:406 (PantsOnly で ExtractSubMesh した Mesh の未破棄)
事実: NewScene でシーン上のオブジェクトは消えますが、アセット扱いとなる Mesh や Material、および VRM ロード時に生成されるテクスチャ・メッシュは DestroyImmediate や Resources.UnloadUnusedAssets() を呼ばないとエディタプロセスに蓄積します。
修正提案: 生成した Mesh / Material は Object.DestroyImmediate で破棄し、各メソッド末尾で EditorUtility.UnloadUnusedAssetsImmediate() を呼んでください。 
サブメッシュ抽出時の bindposes とボーン配列の互換性リスク
場所: CharacterPreview.cs:447-486 (ExtractSubMesh)
事実: 頂点配列は使用頂点のみに圧縮していますが、boneWeights の boneIndex と bindposes、bones は元メッシュのまま流用しています。元メッシュの bindposes が空（VRM0.x 変換時など稀なケース）だとスキニングが破綻する可能性があります（推測）。
修正提案: src.bindposes が null または長さ 0 の場合のフォールバック（bones の worldToLocalMatrix から再計算）を入れておくと安全です。 
RenderCell の依存先欠落と例外時の RenderTexture リーク
場所: CharacterPreview.Motion.cs:64 ほか多数（RenderCell 本体は別チャンクと推測）、および EnemyVrmCheck.cs:178-194 (Shoot)
事実: EnemyVrmCheck.Shoot では cam.Render() や File.WriteAllBytes で例外が起きると RenderTexture.active が戻らず rt.Release() も呼ばれません。CharacterPreview.Shoot も同様の実装である可能性が高いです（推測）。
修正提案: try { ... } finally { RenderTexture.active = null; cam.targetTexture = null; rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(tex); } で囲んでください。 
Vrm10.LoadPathAsync(...).GetAwaiter().GetResult() のデッドロック・null チェック漏れ
場所: CharacterPreview.MeshDump.cs:27-29 ほか大半のメソッド、EnemyVrmCheck.cs:82-84
事実: CharacterPreview.cs:38 では try-catch と vrm == null チェックがありますが、他の 20 箇所以上ではノーガードで vrm.gameObject にアクセスしています。VRM ファイル破損時に即座に NullReferenceException になります。
修正提案: VRM ロードと ProceduralRig / CharacterAnimator セットアップを共通のヘルパーメソッドに集約し、エラーハンドリングを一元化してください。 
Medium（バグ・メンテナンス性・設計上の懸念）
未使用の using ディレクティブの重複
場所: CharacterPreview.MeshDump.cs:7, CharacterPreview.Motion.cs:7, CharacterPreview.Sheets.cs:7, CharacterPreview.Stance.cs:7, CharacterPreview.cs:7
事実: using AttackId = Ryona.Game.EnemyBrain.AttackId; が全ファイルに記述されていますが、Motion.cs 以外では一切使われていません。
修正提案: 不要な using を削除してください。 
座標・バウンズ依存のマジックナンバーによるカメラクリッピング
場所: CharacterPreview.cs:257-264 (MaterialIdTest), CharacterPreview.MeshDump.cs:94-98 (SurfaceMap), CharacterPreview.Stance.cs:290-298 (CaptureFistCloseup)
事実: 股間の高さを y = 0.70f..0.96f、拳の高さを y = 1.33f とハードコードしています。mio_v1.vrm の身長やプロポーションが変わるとカメラ枠外に外れます。
修正提案: rig.Hips.position や rig.RHand.position を基準にカメラ位置とレイキャスト範囲を算出してください（CaptureHandGrid では対応済み）。 
レイキャスト（SurfaceMap / DeformMap）の計算量と精度
場所: CharacterPreview.MeshDump.cs:94-120, 691-725
事実: 毎グリッド点ごとに全三角形へ総当たり（O(Grid × Tris)）を行っています。エディタ上の単発実行なので致命的ではありませんが、メッシュ頂点数が増えるとエディタが数秒フリーズします。また、Hit 関数は裏面カリングを行っていないため、背面の肌ポリゴンを手前と誤認する可能性があります（推測）。
修正提案: det <= 1e-9f（背面除外）にするか、MeshCollider + Physics.RaycastAll を利用すると高速かつ簡潔になります。 
シェーダー名のハードコードと SRP 環境での互換性
場所: CharacterPreview.cs:216 (Unlit/Color), CharacterPreview.Motion.cs:239 (Standard) ほか
事実: Built-in Render Pipeline 前提のシェーダー名（"Standard", "Unlit/Color"）が直書きされています。
修正提案: プロジェクトが Built-in 固定であれば問題ありませんが、共通のヘルパーからマテリアルを生成するようにすると変更に強くなります。 
Low（軽微な改善・テスト不足）
カメラオブジェクトの破棄漏れ
場所: CharacterPreview.Stance.cs:556, 770 (CaptureStanceRollOptions, CaptureFixShowcase)
事実: floor ゲームオブジェクトを生成していますが、DestroyImmediate(floor) が抜けています（直後にシーンを閉じない限りシーンに残留します）。
修正提案: Object.DestroyImmediate(floor); を追加してください。 
診断ログの言語・プレフィックス不統一
場所: 全ファイル
事実: [Ryona], [Map], [Bisect], [Sfx], [Tell], [EnemyVRM] などログプレフィックスが乱立しており、コンソールフィルタリングがしづらい状態です。
修正提案: [Ryona/Diag] のように階層化するとコンソール検索が容易になります。 
テスト不足（バッチ実行・CI 連携）
事実: これらはすべて [MenuItem] 経由の手動実行ツールであり、NUnit による EditMode テストがありません。CrotchCheck や ScanEnemyWalk、ScanSfx のような数値検証メソッドは、[Test] 属性を付けて自動テスト化することで、VRM 再エクスポートやパラメータ調整時のデグレードを自動検知できます。 
全体の設計改善を進めるにあたり、以下を教えていただけますか？
他のチャンク（CharacterPreview の別ファイルや ProceduralRig）も合わせてリファクタリング案を作成するか
VRM の破損・はみ出しチェックを Unity Test Framework (EditMode Test) として自動化したいか
ご希望に合わせて具体的な共通基盤コードを提案できます。
あなたがアップロードしたデータ
·
このカードは自分だけが閲覧できます
chunk-16.txt


## answer-17

# Chunk 17

Files:
- MioPrototype/Assets/RyonaGame/Editor/JointRomCheck.cs
- MioPrototype/Assets/RyonaGame/Editor/PlayerBuild.cs
- MioPrototype/Assets/RyonaGame/Editor/PrototypeSceneBuilder.cs
- MioPrototype/Assets/RyonaGame/Editor/RyonaConfigSetup.cs
- MioPrototype/Assets/RyonaGame/Editor/VoiceFacePreview.cs

---

全体評価：エディタ拡張スクリプト群として全体的な構造やエラーハンドリングの意図は明快ですが、リソースリークや非同期処理のデッドロックリスク、ビルド漏れにつながる境界値判定の不具合が複数潜んでいます。 
Critical（致命的・ハングや機能不全に直結）
1. Vrm10.LoadPathAsync の同期ブロックによるメインスレッドデッドロックリスク
対象: JointRomCheck.cs (L177-178), VoiceFacePreview.cs (L39-41)
事実: エディタのメインスレッド上で .GetAwaiter().GetResult() を呼んで非同期メソッドを強制同期待機しています。
推測: UniVRM10 のロード処理内部で Task.Yield() や SynchronizationContext へのポストが行われた場合（特にメッシュ表示ありの VoiceFacePreview や特定のテクスチャ設定時）、メインスレッドがブロックされてエディタが完全にフリーズ（デッドロック）します。
修正提案: EditorApplication.update でタスク完了をポーリングするか、Awaiter をカスタム実装してメインスレッドのループを回しながら待機させてください。 
2. RenderTexture の解放漏れ（GPUメモリリーク）
対象: PrototypeSceneBuilder.cs (L48-64)
事実: new RenderTexture(...) でネイティブリソースを確保したあと、rt.Release() は呼んでいますが Object.DestroyImmediate(rt) を呼んでいません。また、ReadPixels や EncodeToPNG で例外が発生した場合、RenderTexture.active や cam.targetTexture の復元および解放が行われません。
推測: プレビューを繰り返し実行すると、未破棄の RenderTexture アセットインスタンスがエディタメモリ上にリークし続けます。
修正提案: try-finally ブロックで囲み、finally 内で rt.Release() に加えて Object.DestroyImmediate(rt) を必ず実行してください。 
High（高・実害のある不具合・設定不整合）
1. IsStale() のフォルダ除外判定がOS・パス区切り文字に依存し、アセット追加・削除を検知できない
対象: PlayerBuild.cs (L58-66) 
事実: string editorDir = Path.DirectorySeparatorChar + "Editor" + Path.DirectorySeparatorChar; とし、file.Contains(editorDir) で判定しています。また、WatchedExtensions に含まれるファイルの「更新日時」のみを見ており、ファイルの削除やリネーム、StreamingAssets（.vrm 等）の更新を見ていません。 
推測:
Assets/Editor/Foo.cs のように直下に Editor がある場合や、パス区切りが / と \ で混在する環境で誤判定する可能性があります。
StreamingAssets/mio_v1.vrm を差し替えた場合や、不要な音声ファイルを削除した場合に IsStale() が false を返し、古いビルドが起動され続けます。 
修正提案: file.Replace('\\', '/').Contains("/Editor/") に統一し、WatchedExtensions に .vrm を追加（または StreamingAssets 配下を無条件監視）してください。さらに、ファイル一覧のハッシュやファイル数も .buildstamp に記録すると削除も検知できます。 
2. シーン生成時に生成した多数の Material アセットのリークと保存不備
対象: PrototypeSceneBuilder.cs (L75-91, L103, L123-124, L244-245, L366-371)
事実: MakeMaterial でランタイム用 new Material(shader) を計11個生成してシーン内のレンダラーに割り当てていますが、アセットとしてディスクに保存（AssetDatabase.CreateAsset）していません。
推測: シーン保存時、メモリ上にのみ存在するマテリアルはシーンファイル内にシリアライズされますが、再ビルド時に古いマテリアルが掃除されず、あるいは参照が切れてピンク色になるリスクがあります。また、JointRomCheck.cs (L330-331) では DestroyImmediate していますが、例外発生時はリークします。
修正提案: PrototypeSceneBuilder では Assets/RyonaGame/Materials/ 等にアセットとして保存するか、プリミティブのデフォルトマテリアルを流用してください。 
3. Player.AttackOrigin のローカル座標設定が VRM 差し替え時と不整合を起こす可能性
対象: PrototypeSceneBuilder.cs (L165-177, L194-198)
事実: AttackOrigin を player.transform の直下に配置し、高さ y = 1.15f としています。一方、VRM モデルは VrmCharacterSlot によって後から Mount（player.transform）にロードされます。
推測: 身長が 1.75m と大きく異なる VRM モデルをロードした場合、AttackOrigin の高さが固定値（1.15m）のままとなり、打点が腰や頭上にズレる不具合が起きます。
修正提案: VrmCharacterSlot のロード完了時に AttackOrigin の高さをモデル身長比で再計算するか、ボーン（胸や右肩）に追従させる設計にしてください。 
4. VoiceFacePreview.Capture() の例外時リソースリークとシーン破壊
対象: VoiceFacePreview.cs (L30, L48-116)
事実: 実行時に EditorSceneManager.NewScene(...) で現在開いているシーンを保存確認なしで破棄しています。また、ループ内（L76-107）や Render 内で例外が発生すると、sheet（Texture2D）や vrm.gameObject がメモリに残留します。
推測: 作業中のシーンが未保存だとデータロストします。
修正提案: 冒頭で EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo() を呼び出し、生成した Texture2D や GameObject は try-finally で確実に DestroyImmediate してください。 
Medium（中・パフォーマンス・保守性・カメラ競合）
1. カメラ FOV の二重設定と調整値のハードコード不整合
対象: PrototypeSceneBuilder.cs (L203, L212, L219) 
事実: L203 で cam.fieldOfView = 42f; と設定した直後、L212 で cam.fieldOfView = 40f; と上書きしています。さらに L214-217 のコメントで「カメラ調整値は RyonaConfig が真実源」と書きつつ、L219 で座標をハードコードしています。 
推測: 初期フレームやエディタ上での見え方と、Play 開始直後（ThirdPersonCamera.Awake 実行後）のカメラ位置・画角が急変し、カメラのガクつき（競合）やプレビュー撮影（L42-43）との乖離を生みます。 
修正提案: L203 の重複代入を削除し、エディタ構築時も RyonaConfig をロードして初期位置・FOV を同期させてください。 
2. VoiceFacePreview における大量の Texture2D 生成・破棄（GC/メモリスパイク）
対象: VoiceFacePreview.cs (L101-105, L126-132)
事実: 表情パターンごとに毎回 new Texture2D(w, h, ...) を生成し、GetPixels()（色配列のヒープ確保）でコピーしたあと DestroyImmediate(cell) しています。
推測: 表情数が増えると、GetPixels() による大量の Color[] 配列アロケーションが発生し、エディタの GC スパイクやメモリ断片化を招きます。
修正提案: 1枚の Texture2D を使い回すか、Graphics.CopyTexture を用いて GPU 上で直接 sheet テクスチャの該当座標へコピーしてください（CPU への ReadPixels も最後の1回だけで済みます）。 
3. 敵のコンポーネント参照の未設定リスク（HUD.Enemy）
対象: PrototypeSceneBuilder.cs (L284-287)
事実: 敵は3体スポーンさせていますが、hud.Enemy = firstBrain; として1体目の敵だけを固定で割り当てています。
推測: 1体目の敵を倒した後、2体目・3体目と戦っている最中も HUD の敵ゲージが1体目（死亡済み）のまま更新されない状態遷移バグが発生する可能性があります。
修正提案: MatchHud 側で「現在交戦中（または最も近い）生存敵」を動的に取得する仕組みにするか、EncounterDirector から通知する設計にしてください。 
4. JointRomCheck の境界値ループにおける浮動小数点誤差
対象: JointRomCheck.cs (L214, L221, L228, L236, L257, L286, L303, L311, L370, L380, L388, L394, L400) 
事実: for (float t = 0f; t <= 1.001f; t += 0.1f) のように 0.001f のイプシロンを足して終端を含めようとしています。 
推測: duration が極端に小さい攻撃データ（0.02f 未満など）が追加された場合、フレームのサンプリングが開始フレームのみになる、あるいは終端 t == duration ぴったりでの特殊ポーズ（ヒットストップや終了フレームの遷移）を跨いでしまう恐れがあります。 
修正提案: ステップ数（整数 int steps = Mathf.CeilToInt(duration / step)）ベースの for ループにし、最終ステップで必ず time = duration を評価するようにしてください。 
Low（低・可読性・テスト不足）
1. JointRomCheck における EnemyFigure の姿勢計測の網羅性不足
対象: JointRomCheck.cs (L358-404)
事実: プレイヤー側は Grabbed（掴まれ）や左右の被弾方向（side = -1, 0, 1）を網羅していますが、敵側は Grab（掴み中）のホールド姿勢や、背丈バリエーション（1.72m〜1.85m）ごとの計測を行っていません。
推測: 敵がプレイヤーを拘束している最中の腕・腰の関節破綻がテストから漏れます。
修正提案: 敵の拘束継続中の姿勢プレビューがあれば CheckEnemies のテストケースに追加してください。 
2. EnsureFolder の階層作成の堅牢性
対象: PrototypeSceneBuilder.cs (L373-379), RyonaConfigSetup.cs (L30-31)
事実: Directory.CreateDirectory(path) を呼んだ後に AssetDatabase.Refresh() を実行しています。
推測: 大規模プロジェクトになると AssetDatabase.Refresh() は全アセットのインポート走査を走らせるため非常に重くなります。
修正提案: AssetDatabase.CreateFolder を使用すると、全体リフレッシュなしで即座に .meta ファイル付きでフォルダを作成できます。 
まとめ（優先対応推奨）
PrototypeSceneBuilder.CapturePreview の RenderTexture 破棄漏れ修正（即時対応推奨）
PlayerBuild.IsStale のパス区切り正規化と .vrm 監視追加（ビルド漏れ防止）
Vrm10.LoadPathAsync の同期ブロック解消（Unity エディタのフリーズ防止） 
このチャンクのレビューを踏まえ、次のどちらを進めますか？
修正コードの具体的なパッチ作成（特定のファイルをご指定ください）
次のチャンク（18/26）のコードレビュー
あなたがアップロードしたデータ
·
このカードは自分だけが閲覧できます
chunk-17.txt


## answer-18

# Chunk 18

Files:
- MioPrototype/Assets/RyonaGame/Tests/PlayMode/AnkleGrabTests.cs
- MioPrototype/Assets/RyonaGame/Tests/PlayMode/ArenaRuntimeTests.cs
- MioPrototype/Assets/RyonaGame/Tests/PlayMode/BearHugTests.cs
- MioPrototype/Assets/RyonaGame/Tests/PlayMode/BodyOverlapRuntimeTests.cs
- MioPrototype/Assets/RyonaGame/Tests/PlayMode/CameraExitBlendTests.cs
- MioPrototype/Assets/RyonaGame/Tests/PlayMode/DownRuntimeTests.cs
- MioPrototype/Assets/RyonaGame/Tests/PlayMode/EnemyAiRuntimeTests.cs
- MioPrototype/Assets/RyonaGame/Tests/PlayMode/EnemyHpRuntimeTests.cs
- MioPrototype/Assets/RyonaGame/Tests/PlayMode/EnemyRoleRuntimeTests.cs
- MioPrototype/Assets/RyonaGame/Tests/PlayMode/FaceVisibilityLensTests.cs
- MioPrototype/Assets/RyonaGame/Tests/PlayMode/FeelRuntimeTests.cs
- MioPrototype/Assets/RyonaGame/Tests/PlayMode/GrappleSessionTests.cs
- MioPrototype/Assets/RyonaGame/Tests/PlayMode/HairBodyRuntimeTests.cs
- MioPrototype/Assets/RyonaGame/Tests/PlayMode/HaulUpRuntimeTests.cs

---

ご提示いただいた PlayMode テストコード群（チャンク 18/26、全14ファイル）に対するコードレビュー結果です。テストコード自体の堅牢性・網羅性・クリーンアップ処理に加え、テストの実装やコメントから読み取れる本体コード（Ryona.Game）側の潜在的な不具合や懸念点も含めて整理しました。 
Critical（致命的：テスト破壊・グローバル状態汚染・確定的なバグ）
1. EnemyBrain.ForcedGrabKind のリセット漏れによるテスト間干渉
対象: BearHugTests.cs (L21-27), DownRuntimeTests.cs (L23-30), HaulUpRuntimeTests.cs (L24-29) など 
事実: AnkleGrabTests.cs (L25) や EnemyAiRuntimeTests.cs (L31) では [TearDown] で静的フィールド EnemyBrain.ForcedGrabKind = null; をリセットしていますが、他のテストクラスではリセットしていません。また、GroundHaulUp.Forced は HaulUpRuntimeTests.cs (L28) のみでリセットされています。 
推測: AnkleGrabTests の途中で Assert 失敗以外の例外や中断が起きたり、他のテストでこれらを変更した場合、後続の全 PlayMode テストで敵の掴み種別が固定されたままになり、連鎖的なテスト失敗（フレーキーテスト）を引き起こします。 
修正提案: TestScene.LoadPrototype() の冒頭、または全 PlayMode テスト共通の基底クラス／[SetUp] で EnemyBrain.ForcedGrabKind = null; と GroundHaulUp.Forced = false; を必ず初期化してください。 
2. LogAssert.Expect 失敗時の GameObject リーク
対象: BodyOverlapRuntimeTests.cs (L92-118)
事実: new GameObject("Test Body Overlap Watch") を生成した後、try-finally や [TearDown] での破棄処理がなく、メソッド末尾（L118）でのみ Object.Destroy(go); を呼んでいます。
推測: L106 の LogAssert.Expect や L109〜116 の Assert でテストが失敗すると、L118 に到達せずシーン内に BodyOverlapWatch コンポーネントが残存します。BodyOverlapWatch がシングルトンや静的リストに登録される設計の場合、後続のテスト（L57 の BodyOverlapWatch.Instance 等）が汚染されます。
修正提案: go をフィールドに保持して [TearDown] で if (_watchGo != null) Object.Destroy(_watchGo); するか、try { ... } finally { Object.Destroy(go); } で囲んでください。 
High（高：テストのフレーキー化・本体ロジックの境界値/割り込みバグの兆候）
3. 物理/VRMメッシュの毎フレーム全頂点ベイクによるフレームレート低下とタイムアウト誘発
対象: HairBodyRuntimeTests.cs (L141-194) 
事実: Audit() 内で全 SkinnedMeshRenderer に対して new Mesh() → smr.BakeMesh(baked, true) を実行し、ヘアマテリアルの全頂点 × 全 VRM10SpringBoneCollider の総当たり距離計算をメインスレッドで同期実行しています。 
推測: 頂点数やコライダー数が多い VRM モデルを読み込んだ際、このフレームだけ極端なスパイク（数百ms〜数秒のフリーズ）が発生し、Time.realtimeSinceStartup ベースのタイムアウト判定や物理演算（SpringBone）のステップ幅に影響を与える可能性があります。また UnityEngine.Object.Destroy(baked) が例外発生時にスキップされるとネイティブメモリリークになります。 
修正提案: try-finally で確実に Destroy(baked) を行ってください。パフォーマンス改善として、コライダーのバウンディングスフィアによる事前枝刈り（Broadphase）を入れると高速化できます。 
4. Time.realtimeSinceStartup 依存のタイムアウトによる低スペック環境・GCスパイク時の誤検知
対象: DownRuntimeTests.cs (L78, 127, 193, 666), BearHugTests.cs (L91, 107), EnemyAiRuntimeTests.cs (L95, 256) ほか多数
事実: 多くの待機ループで Time.realtimeSinceStartup + 秒数 を期限として使っています。
推測: VRM のロード直後やシェーダコンパイル、GC 発生時に 1 フレームが 1〜2 秒止まると、ゲーム内時間（Time.time / Time.deltaTime）がほとんど進んでいないのに実時間だけ経過してタイムアウト失敗します。（実際、FeelRuntimeTests.cs L112-115 のコメントでもシェーダコンパイルによる 1 Tick 遅延の罠に言及されています）。
修正提案: タイムアウト計測には Time.realtimeSinceStartup ではなく、フレーム毎の Mathf.Min(Time.unscaledDeltaTime, 0.05f) を積算するヘルパー（TestWait 側の改善）を使用することを推奨します。 
5. イベント購読の解除漏れとテスト中断時の副作用
対象: AnkleGrabTests.cs (L121), DownRuntimeTests.cs (L116, 177, 316), EnemyAiRuntimeTests.cs (L155, 273)
事実: _grapple.Failed += ... や _stats.HitReceived += ... に匿名ラムダ式を直接登録しており、解除処理がありません。
推測: テストごとに TestScene.LoadPrototype() でシーンを再ロードしているため通常は破棄されますが、もし PlayerStats や PlayerGrapple が DontDestroyOnLoad 化・オブジェクトプール化された場合、過去のテストのラムダ式が発火し、破棄済みのローカル変数やコンポーネント（attacker.transform 等）にアクセスして MissingReferenceException を起こします。
修正提案: シーン再ロードに頼らず、テスト内でイベント購読する場合はハンドラを変数に保持し、try-finally または [TearDown] で -= 解除する習慣をつけるのが安全です。 
6. 【本体コードへの懸念】ダウン後の「起き上がり無敵/ひるみ耐性」と「連続マウント」の境界
対象: DownRuntimeTests.cs (L310-329 StayingDownAfterMountLetsTheEnemyAttackAgain, L478-496 ダウン中はひるみが溜まらない)
事実: マウント攻撃で 5 発殴られた後、プレイヤーが起き上がり入力をしないと 2.5秒 以上の猶予の後に 2 回目のマウントに入ることが仕様としてテストされています。一方、通常打撃に対してはダウン中ひるみが溜まらない仕様です。
推測（状態遷移の競合リスク）: プレイヤーが 2 回目のマウントの Approach〜Mount 移行フレームと全く同じフレームに RequestGetUp() を押した場合、GroundMountAttack のロック（BlocksGetUp）と PlayerStats.RequestGetUp() の状態遷移が競合し、「立ち上がった状態でマウントモーションに入る」または「ダウン状態が解除されないまま硬直する」バグが起きやすい境界です。
修正提案: DownRuntimeTests に「GroundMountAttack.Stage が Approach から Mount に切り替わる直前・直後のフレームで RequestGetUp() を呼んだ場合」の競合テストを追加してください。 
Medium（中：テストの脆弱性・設計上の改善点・テスト不足）
7. 確率依存テストのフレーキーリスク（乱数シード未固定）
対象: AnkleGrabTests.cs (L150-169 走って逃げる彼女の背後からは足取りと羽交い締めが抽選になる)
事実: EnemyAiRuntimeTests.cs (L23) では Random.InitState(20261119); で乱数シードを固定していますが、AnkleGrabTests.cs ではシードを固定せず 60 回ループを回しています。
推測: 確率 2⁻⁵⁹ とはいえ、CI 上で乱数アルゴリズムや他テストからの Random.state 汚染が重なると稀に落ちる原因になります。
修正提案: AnkleGrabTests の [SetUp] でも Random.InitState(...) を呼んで決定論的にしてください。 
8. プライベート enum 状態への .ToString() 文字列比較
対象: DownRuntimeTests.cs (L79, 85, 91, 102, 131, 229, 262, 354)
事実: L613 や L642 では mount.Stage != GroundMountAttack.Beat.Windup と型安全に enum 比較しているのに対し、前半のテストでは mount.Stage.ToString() != "Approach" のように文字列比較を行っています。
推測: GroundMountAttack.Beat の列挙子名をリファクタリングした際、コンパイルエラーにならず実行時にタイムアウト失敗する原因になります。また毎フレーム .ToString() によるボックス化・文字列アロケーション（GC）が発生しています。
修正提案: すべて GroundMountAttack.Beat.Approach 等の enum 値直接比較に統一してください。 
9. EnemyRoleRuntimeTests の敵数ハードコード
対象: EnemyRoleRuntimeTests.cs (L19 Assert.AreEqual(3, enemies.Length);)
事実: プロトタイプシーンの初期配置敵数が厳密に 3 体であることを前提としています。
推測: レベルデザイン調整で初期ウェーブの敵数を 4 体や 2 体に変えただけで、役割割り当てロジック自体が正常でもテストが失敗します。
修正提案: 初期ウェーブのスポーン定義（EncounterDirector 等）から期待人数を取得するか、Assert.GreaterOrEqual(enemies.Length, 3) とした上で各役割が最低 1 体以上割り当てられることを検証する形に緩めると保守性が上がります。 
10. カメラ演出の競合・割り込みテストの不足
対象: CameraExitBlendTests.cs (L10-34), BearHugTests.cs (L106, 199), DownRuntimeTests.cs (L635-735) 
事実: 現在は「シネマティックショット単体の終了時ブレンド」「ベアハッグ中のズーム」「マウント中の顔可視性」が個別にテストされています。 
推測: 以下のカメラ演出の競合・割り込みパターンが未検証です。
ベアハッグやマウントのクローズアップ中に、別の攻撃でダウン（吹き飛び）やステージクリア演出（シネマティックショット）に割り込まれた際、EffectDistanceScale が 0.25f のまま固まらないか。
カメラの出口ブレンド（CameraExitBlendTests）の途中で、即座に次の拘束カメラが要求された場合にカメラ位置がジャンプしないか。 
修正提案: 「拘束中に強制ダウン（ForceDown）または強制解放（Release()）を割り込ませた直後のカメラ距離・角度の遷移」を検証するテストケースを CameraExitBlendTests または BearHugTests に追加してください。 
11. 三人がかり拘束（GrappleSession）の結合・解除・再入場テスト不足
対象: GrappleSessionTests.cs (L10-44)
事実: GrappleSessionTests は純粋なデータ構造（ClaimStrike が 2 枠までであること）の単体テストのみであり、PlayMode での実機挙動テストがありません。
推測: 1 人が拘束し、残り 2 人が打撃枠を奪い合う状況で、「拘束中のプレイヤーがもがき脱出した瞬間」や「打撃担当の敵がプレイヤーの反撃・ダウンで死亡/よろけ状態になった瞬間」に、打撃枠（Slot）が解放されずロックされ続ける（＝以降の戦闘で敵が殴ってこなくなる）バグが潜みやすい箇所です。
修正提案: PlayMode で 3 体の敵を配置し、1 体に拘束させた状態で残り 2 体に殴らせ、途中で拘束脱出・敵撃破を行った後に打撃枠がリークしていない（次の敵が正常に攻撃できる）ことを検証するテストを追加してください。 
Low（低：コード品質・可読性・軽微なリファクタリング）
12. 未使用フィールドの残存
対象: BodyOverlapRuntimeTests.cs (L17 ProceduralRig _rig;)
事実: L35 で _rig = _anim.Rig; と代入していますが、クラス内のどこからも参照されていません。
修正提案: 不要であれば削除してください。 
13. テスト内マジックナンバーと RyonaConfig / CombatConstants の混在
対象: AnkleGrabTests.cs (L74-76) vs BearHugTests.cs (L65-67, 73, 168)
事実: AnkleGrabTests では RyonaConfig.Instance.Grapple から設定値を動的に取得してアサートしていますが、BearHugTests では CombatConstants や 0.54f、0.42f といったマジックナンバーが一部直書きされています。
修正提案: ゲームバランス調整（距離や制限時間の微調整）のたびにテストが壊れないよう、距離や制限時間の閾値は可能な限り RyonaConfig や CombatConstants の定数を参照させてください。 
総評
全体として、単なる数値一致だけでなく「VRM ボーン位置・肘の曲がり角度・髪の貫通深度・画面内の顔可視率（FaceVisibility）・カメラの急激なジャンプ」まで実機 PlayMode で自動検証しており、非常に質の高い回帰テストスイートです。
最優先で対処すべきは 静的フラグ（ForcedGrabKind / GroundHaulUp.Forced）の確実な初期化 と、複数敵・複数演出が重なった際の割り込み解除（カメラ距離の復帰・GrappleSession 打撃枠の解放） のテスト拡充です。 
必要であれば、以下のいずれかを続けて作成できます。
共通テスト基底クラス（静的フラグ初期化・Time.timeScale 復帰・イベント解除ヘルパー）の実装コード
カメラ演出競合および 三人がかり拘束（GrappleSession）割り込み解除 の追加 PlayMode テストコード
どちらから進めましょうか？
あなたがアップロードしたデータ
·
このカードは自分だけが閲覧できます
chunk-18.txt


## answer-19

# Chunk 19

Files:
- MioPrototype/Assets/RyonaGame/Tests/PlayMode/HeldBellyPunchTests.cs
- MioPrototype/Assets/RyonaGame/Tests/PlayMode/HotReloadTests.cs
- MioPrototype/Assets/RyonaGame/Tests/PlayMode/HurtPoseRuntimeTests.cs
- MioPrototype/Assets/RyonaGame/Tests/PlayMode/JointRomRuntimeTests.cs
- MioPrototype/Assets/RyonaGame/Tests/PlayMode/OcclusionFadeTests.cs
- MioPrototype/Assets/RyonaGame/Tests/PlayMode/OverheadRuntimeTests.cs

---

添付された 6 ファイル（PlayMode テスト群）のコードレビュー結果を、重大度別・ファイル名および行番号付きで報告します。
Critical（テスト不安定化・CIハング・状態汚染）
1. Time.realtimeSinceStartup によるタイムアウト監視と Time.timeScale の不整合
対象: HeldBellyPunchTests.cs (303-320, 354-356, 430-448, 514-516, 654-696, 771-818, 903-909, 941-984, 1062-1170, 1624-1658, 1870-1888)
確定事実: ゲーム内演出の進行はスケールされた時間（Time.deltaTime / Time.time）に依存していますが、ほぼすべての while ループのタイムアウト判定に実時間（Time.realtimeSinceStartup）が使われています。さらに HitStop.IsActive（887行目）や VRM 読み込み時の Time.timeScale = 0f（OverheadRuntimeTests.cs 70行目のコメント参照）が発生する環境です。 
推測: テストマシンが高負荷な場合やヒットストップが重なった際、ゲーム内時間が十分に経過する前に実時間タイムアウトに到達し、フレーキー（不安定）な失敗を引き起こします。一方で Time.timeScale が 0 のまま固まった場合は実時間タイムアウトが安全弁になります。
修正提案: ゲーム進行待機は TestTime ヘルパー（OverheadRuntimeTests.cs 136行目にある TestTime.WaitGameSeconds 等）に統一し、Time.timeScale == 0f の固着検知だけを実時間で行うヘルパー（TestWait.UntilOrTimeout の拡張）へ切り出してください。 
2. Application.logMessageReceived による内部ログ文字列への強結合
対象: HeldBellyPunchTests.cs (1024-1029, 1606-1619, 1851-1856)
確定事実: 攻撃の左右判定（[HeldPunch] order ... side=L）やコンボ完了・リトライ判定（[HeldPunch] retry, [StrikeMiss] ... tag=True, [HeldPunch] combo end）を Debug.Log の出力文字列パースに依存しています。
推測: プロダクションコード側のログ文言・フォーマットの微修正や、ログ出力レベル制限（Releaseビルドやログ抑制設定）により、テストの検出ロジックがサイレントに空振りします（1024行目・1851行目ではログが1件も来ないと sides.Count が不足して失敗しますが、1610行目の retries はログ文言が変わると「失敗0件」と誤判定して偽陽性になります）。
修正提案: HeldBellyPunchDirector にテスト参照用のイベント（event Action<PunchZone, bool> OnPunchOrdered / OnPunchMissed）または状態カウンタを公開し、文字列パースを廃止してください。 
High（テスト信頼性・リソースリーク・状態汚染）
3. 動的生成した GameObject / Material の破棄漏れ（リーク）
対象:
OverheadRuntimeTests.cs (107-110): new GameObject("Overhead Test Wall") で生成したオブジェクトを [TearDown] 等で Destroy していません。
JointRomRuntimeTests.cs (105, 125, 163, 256, 306, 348): 各テストで AddComponent<JointRomMonitor>() していますが、途中の Assert で例外が発生した場合、末尾の Object.Destroy(monitor) に到達せずコンポーネントが残存します。 
確定事実: PlayMode テストでシーンをロードし直せば破棄されますが、同一シーン内で次フレーム以降に走査が走る場合や DontDestroyOnLoad 領域に影響する場合、残存オブジェクトが後続の FindObjectsByType に混入します。 
修正提案: try ... finally ブロックまたは [TearDown] で確実に Object.Destroy を実行してください。 
4. TestScene.RestoreAudio() の呼び出し不統一と TearDown での FindObjectsByType 汚染リスク
対象: HeldBellyPunchTests.cs (29-35), HurtPoseRuntimeTests.cs (23-27), OverheadRuntimeTests.cs (23-30), JointRomRuntimeTests.cs (24-29)
確定事実: JointRomRuntimeTests.cs (28行目) では TestScene.RestoreAudio() を呼んでいますが、TestScene.LoadPrototype() を呼んでいる他の3クラスでは呼んでいません。
推測: LoadPrototype() がテスト用にオーディオをミュートまたは変更している場合、他のテストクラスを実行した後にオーディオ設定が戻らない可能性があります。
修正提案: TestScene.LoadPrototype() を使う全テストクラスの [TearDown] に TestScene.RestoreAudio() を入れるか、共通の基底テストクラス（PlayModeTestBase）に集約してください。 
5. ゼロ除算（NaN発生）の危険性があるベクトル演算
対象: HeldBellyPunchTests.cs (835-840) DistanceToSegment
確定事実: float t = Mathf.Clamp01(Vector3.Dot(point - start, span) / span.sqrMagnitude); において、start == end（ボーン長が0、または未初期化座標）の場合、span.sqrMagnitude が 0f となり 0f / 0f = NaN が発生します。Mathf.Clamp01(NaN) は NaN を返し、Vector3.Distance も NaN になるため、nearestArm > 0.15f（826行目）の判定がサイレントに偽になります。
修正提案: if (span.sqrMagnitude < 1e-8f) return Vector3.Distance(point, start); のガードを追加してください。 
Medium（境界値・設計の不整合・メンテナンス性）
6. コメントと実装の食い違い（コピペ残骸・仕様変更の未反映）
対象:
HeldBellyPunchTests.cs (150-153): MinAnkleAboveRoot() の XML ドキュメントコメントが、直前の AssertCaptorArmsHugFromBehind の <param name="reaction"> の直後に割り込んでおり、AssertCaptorArmsHugFromBehind のサマリとシグネチャが分断されています。
HeldBellyPunchTests.cs (1694): Assert.AreEqual(firstYaw, _anim.DebugHeadYaw, 3f, "敵の移動に顔が追従する"); — 1673行目のサマリと1692行目のコメントには「追従しない」と書かれているのに、アサーションの失敗メッセージだけ逆の意味（「追従する」）になっています。
HeldBellyPunchTests.cs (1060): 未使用ローカル変数 EnemyBrain lastPuncher = null; が残っています。 
修正提案: XMLコメントの位置修正、失敗メッセージの修正（「敵の移動に顔が追従してしまっている」等）、未使用変数の削除を行ってください。 
7. HeldBellyPunchTests.cs の過大な責務とコード重複（1,909行）
対象: HeldBellyPunchTests.cs (全体)
確定事実: 1ファイルに20以上のテストメソッドと複雑なフレームループ監視が詰め込まれており、Application.logMessageReceived 購読処理（1024行目, 1851行目）や、Object.FindObjectsByType<EnemyFigure>(FindObjectsSortMode.None) のフレーム毎呼び出し（561, 947, 1067行目）が重複しています。 
推測: while ループ内で毎フレーム FindObjectsByType を呼ぶと GC Alloc が発生し、フレームレート低下によって物理や補間テストの挙動に影響する恐れがあります。 
修正提案: カメラ検証・モーション検証・コンボ進行遷移にテストクラスを分割し、FindObjectsByType の結果はループ外でキャッシュしてください。 
8. 固定 WaitForSeconds によるタイミング依存（フレーキー要因）
対象: HeldBellyPunchTests.cs (93, 101, 118, 127, 130, 386, 391, 525, 699, 743, 847, 914, 922, 1298, 1569, 1693, 1710, 1807, 1816, 1823, 1831)
確定事実: アニメーションのピーク（例: 0.14f 秒後の反動最大値）を yield return new WaitForSeconds(0.14f) でピンポイントに待機して計測しています。
推測: CI環境やエディタのスパイクで1フレームが 30〜50ms 遅延すると、ピークを通り過ぎたフレームで値を取得してアサーションが落ちる可能性があります。
修正提案: 固定秒数で1点サンプルするのではなく、一定区間（例: 0.08s 〜 0.20s）を毎フレーム走査してピーク値（Mathf.Max）を記録する方式（1422行目のループのような手法）に統一すると安定性が大幅に向上します。 
Low（テストカバレッジの不足・軽微な改善点）
9. テストケースの不足観点
対象: 全体
確定事実:
HotReloadTests.cs: RyonaConfig.NotifyChanged に null や別インスタンスが渡された場合の境界値テストがありません。また、CombatFeel 以外の設定購読コンポーネント（カメラやグラップル設定等）のホットリロードが検証されていません。
OcclusionFadeTests.cs: 単体境界値（1.8f, 3.1f, 1.0f）の3パターンのみで、境界線上（閾値ちょうど、負の距離、同距離 1.4f, 1.4f）のテストがありません。
OverheadRuntimeTests.cs: 壁拘束（WallPin）の時間切れ解除（121行目で HasTimeLimit == true を確認しているのみ）や、壁拘束中に別の敵から攻撃を受けた場合の割り込み・解除の挙動がテストされていません。
HeldBellyPunchTests.cs: 「5発の途中で敵の攻撃が中断された場合（撃破・怯み・ダウン等）に拘束が継続し、カメラやHUDが正常に復帰する」ケース（クラス冒頭15行目の仕様コメント「途中で諦めた場合だけ拘束が続く」に対応するテスト）が見当たりません。 
修正提案: 上記の境界値および中断・復帰パス（特に HeldBellyPunchTests の「途中中断時のカメラ・HUD復帰」と OverheadRuntimeTests の「壁拘束タイムアウト解除」）のテスト追加を推奨します。 
10. 不要なステートメント
対象: JointRomRuntimeTests.cs (290行目)
確定事実: Object.FindObjectsByType<EnemyFigure>(FindObjectsInactive.Exclude); の戻り値が捨てられており、直後の while ループ内（295行目）で同じ呼び出しを行っています。
修正提案: 290行目を削除してください。 
優先して改善を進めたい項目に合わせて、次のステップを提案できます。以下からご希望をお知らせください：
プロダクションコード側の修正案（HeldBellyPunchDirector へのイベント追加や OcclusionFade の実装確認）
テスト基盤の共通化（PlayModeTestBase やピーク値サンプリング用ヘルパーの実装コード作成）
不足テストの追加実装（途中中断時の復帰テストや壁拘束タイムアウトテストのコード作成）
あなたがアップロードしたデータ
·
このカードは自分だけが閲覧できます
chunk-19.txt


## answer-20

# Chunk 20

Files:
- MioPrototype/Assets/RyonaGame/Tests/PlayMode/PinGrappleTests.cs
- MioPrototype/Assets/RyonaGame/Tests/PlayMode/PinballRuntimeTests.cs
- MioPrototype/Assets/RyonaGame/Tests/PlayMode/PlayabilityRegressionTests.cs
- MioPrototype/Assets/RyonaGame/Tests/PlayMode/PlayerFaceRuntimeTests.cs
- MioPrototype/Assets/RyonaGame/Tests/PlayMode/TestAudioTests.cs
- MioPrototype/Assets/RyonaGame/Tests/PlayMode/TestOverrides.cs
- MioPrototype/Assets/RyonaGame/Tests/PlayMode/TestRig.cs
- MioPrototype/Assets/RyonaGame/Tests/PlayMode/TestScene.cs
- MioPrototype/Assets/RyonaGame/Tests/PlayMode/TestTime.cs
- MioPrototype/Assets/RyonaGame/Tests/PlayMode/TestWait.cs
- MioPrototype/Assets/RyonaGame/Tests/PlayMode/UiRuntimeTests.cs
- MioPrototype/Assets/RyonaGame/Tests/PlayMode/VrmRuntimeTests.cs

---

提示された PlayMode テストコード群（12 ファイル）のコードレビュー結果を、重大度別に列挙します。
Critical（テストの信頼性崩壊・他テストへの波及）
static 状態・シングルトン状態の変更がテスト失敗時や終了時に復元されない
場所: PinballRuntimeTests.cs:20 (RyonaConfig.Instance.Pinball.Chance = 0f;), PinGrappleTests.cs:205 (FeintChance), UiRuntimeTests.cs:308,353 (director.WaveGap, director.MaxWaves)
確定事実: PinballRuntimeTests.Setup は RyonaConfig.Instance.Pinball.Chance を 0f に書き換えるが、[TearDown] や TestOverrides.ResetAll() で元の値に戻していない。TestOverrides.cs:16-26 がリセットしているのは GroundHaulUp と HitStop / Time.timeScale のみである。
推測（仮説）: RyonaConfig.Instance が ScriptableObject や static インスタンスである場合、このテストの後に実行されるテストではピンポン演出が一切発生しなくなり、テスト順序依存のバグや偽陽性（サイレントな仕様未検証）を引き起こす。
修正提案: TestOverrides.ResetAll() で RyonaConfig.Instance の各設定値を初期値へリセットするか、[TearDown] で元の値を退避・復元する。 
生成した GameObject や追加したコンポーネントが破棄されずリークする
場所: UiRuntimeTests.cs:84,228 (new GameObject("Title Gate (test)"), new GameObject("Pause Menu (test)")), PlayabilityRegressionTests.cs:70,88,369,472 (Object.Destroy(go) が Assert より後ろにある)
確定事実: UiRuntimeTests の 228 行目で生成した Pause Menu (test) は try-finally 内でも Object.Destroy されていない。また、PlayabilityRegressionTests の 70, 88, 369, 472 行目は途中にある Assert が失敗すると例外で抜け、Object.Destroy(go) に到達しない。
推測（仮説）: もし PauseMenu や TitleGate が DontDestroyOnLoad に登録される設計だった場合、後続のテスト画面に UI やポーズ状態が残存して干渉する。
修正提案: テスト内で生成した GameObject は try-finally の finally ブロック、または [TearDown] で確実に Object.Destroy する。 
High（テストのフレーキー化・待機ロジックの欠陥・ヌル参照リスク）
VRM ロード待ちに Time.deltaTime を使用しており、フレーム落ちやスパイクで誤タイムアウトする
場所: VrmRuntimeTests.cs:23-28, VrmRuntimeTests.cs:62-67
確定事実: timeout -= Time.deltaTime; で 20 秒を計測している。一方、同じファイル群にある TestRig.cs:38 では Time.realtimeSinceStartup を使用している。
推測（仮説）: シーンロード直後の重たい初期化フレームで Time.maximumDeltaTime（既定 0.333s）を超過すると時間計測が狂い、また万が一 timeScale == 0 になるとループが無限に終わらなくなる。
修正提案: 自前ループを廃止し、TestRig.WaitReady または TestWait.UntilOrTimeout に置き換える。 
TestWait.UntilOrTimeout が Time.timeScale == 0 のときに hardDeadline まで無駄にブロックする
場所: TestWait.cs:44-47
確定事実: TestTime.WaitGameSeconds（TestTime.cs:22-23）には if (Time.timeScale <= 0f) Time.timeScale = 1f; の救済があるが、TestWait.UntilOrTimeout にはない。
推測（仮説）: テスト中に予期せずポーズやタイトル画面に入って timeScale == 0 になった場合、Time.time が進まず、本来数秒で失敗すべきテストが timeoutSeconds * 10 + 5 秒（例: 25 秒〜）もハングアップする。
修正提案: TestWait.UntilOrTimeout 内でも timeScale == 0 検知時のログ出力またはフェイルファストを検討する（意図的に timeScale == 0 を待つケースがないため）。 
ハードコードされた固定秒数待機（WaitForSeconds / WaitUnscaled）による環境依存のフレーキーさ
場所: PinGrappleTests.cs:226,438, PlayabilityRegressionTests.cs:24,231,326, PlayerFaceRuntimeTests.cs:132,188,201,205,218,221, VrmRuntimeTests.cs:80
確定事実: 状態遷移や補間完了を TestWait.UntilOrTimeout ではなく固定の WaitForSeconds や WaitUnscaled で待っている箇所が多数ある。PlayabilityRegressionTests.cs:323-326 のコメント自身が「0.65 秒だと不安定になったので 1.2 秒に延ばした」と述べている通り、対症療法になっている。
推測（仮説）: CI 環境などフレームレートが低い・不安定な環境では Time.unscaledDeltaTime が大きく飛び、目標のブレンド値（例: 0.2 秒時点の BellyBlend）のピークを通り過ぎてからアサートが評価され、失敗する可能性がある。
修正提案: ピーク値を取る場合は毎フレームサンプリングし（PlayerFaceRuntimeTests.cs:164 のように）、状態到達を待つ場合は TestWait.UntilOrTimeout で条件待機する。 
存在確認なしの直接インデックスアクセス・null チェック漏れによる NullReferenceException / IndexOutOfRangeException
場所: PlayabilityRegressionTests.cs:61-62,209-210,273-274,449-450, PlayerFaceRuntimeTests.cs:131,152, UiRuntimeTests.cs:43-44,112
確定事実: enemies[0] や enemies[1] に Assert.GreaterOrEqual(enemies.Length, 2) なしでアクセスしている箇所がある（PlayabilityRegressionTests.cs:61,209,273,449、PlayerFaceRuntimeTests.cs:131,152）。また UiRuntimeTests.cs:43 で hud の null チェックがなく、UiRuntimeTests.cs:112 で Camera.main の null チェックがない。
推測（仮説）: シーン上の敵配置数やカメラ設定が変わった際、原因が分かりにくい IndexOutOfRangeException や NullReferenceException で落ちる。
修正提案: テスト冒頭で Assert.IsNotNull や Assert.GreaterOrEqual(enemies.Length, 2) を明示する。 
カメラズームの競合テストがイベント発火ではなく直接メソッド呼び出しに依存している
場所: UiRuntimeTests.cs:132,144-147,164
確定事実: camera.HoldFocus(...) と camera.RequestCinematicShot(...) を直接呼び出してズーム距離を検証している。
推測（仮説）: 実際のゲームプレイ中に「演出カメラの自動切り替え（Update での上書き）」と「ユーザーのホイールズーム入力」が毎フレーム競合した際、演出側がズーム値を毎フレームリセットしてしまいホイールが効かなくなる不具合（カメラ演出の競合）をこの単発呼び出しテストでは検知できない。
修正提案: 実際の拘束・膝蹴り演出や複数フレームの Update 経過中にホイールイベントを連続送信し、ズーム状態が維持されるか検証するテストを追加する。 
Medium（境界値・状態遷移・リソース管理の懸念）
PlayerFaceRuntimeTests における PlayerFace コンポーネントの二重付与リスク
場所: PlayerFaceRuntimeTests.cs:38-45
確定事実: LoadFace() で VRM のロード完了を待たずに _stats.gameObject.AddComponent<PlayerFace>() を追加している。
推測（仮説）: もし VrmCharacterSlot の非同期ロード完了後に VRM 側にも PlayerFace が生成される仕様である場合、PlayerStats 配下に 2 つの PlayerFace が並存し、イベント購読や ProceduralSfx 連携が二重に走る可能性がある。
修正提案: テスト対象を純粋に分離するなら VrmCharacterSlot を無効化するか、PlayerFace 側の重複防止を確認する。 
イベント購読の解除漏れ（テストコード上のクリーンアップ不足）
場所: PinGrappleTests.cs:121,291,377,389,483,514,624
確定事実: _grapple.Failed += ... や _player.HitReceived += ... にラムダ式を購読させているが、購読解除（-=）はしていない。
推測（仮説）: 現状は各テストの先頭で SceneManager.LoadScene("Prototype") によりオブジェクトが破棄されるため実害はないが、将来的にシーンを再利用するテストへリファクタリングした際にハンドラが多重実行される温床になる。
修正提案: オブジェクトのライフサイクルがテストを跨がないことを前提とするか、ローカルメソッドにして finally で解除する。 
PinGrappleTests の引き渡し中断テストで enemies[0] / enemies[1] の位置関係と条件が境界値ギリギリ
場所: PinGrappleTests.cs:382-383,582
確定事実: 382 行目で _other を offset = receiverBehind ? -0.82f : 0.82f に配置し、582 行目では Vector3.right * 2.5f に配置している。また、PinGrappleTests.cs:167-171 の CanStartPin 境界値テストは 0.60f（NG）、1.15f（OK）、1.60f（NG）のみをテストしている。
推測（仮説）: クラスコメント（14 行目）には「入口は正面 0.90〜1.30m」とあるが、実際の境界値（0.89f / 0.90f / 1.30f / 1.31f）がテストされていないため、判定式の < と <= のバグを見逃す。
修正提案: CanStartPin のテストに境界値（0.89f, 0.90f, 1.30f, 1.31f）のケースを追加する。 
VrmRuntimeTests におけるワールド座標での拳位置判定
場所: VrmRuntimeTests.cs:49 (Assert.Greater(rig.LHand.position.z, 0.04f, ...))
確定事実: rig.LHand.position.z（ワールド座標の Z 軸）を使って「拳が体の前にある」と判定している。プレイヤーは横スクロールレーン上で +X または -X 方向を向いて戦う（PinGrappleTests.cs:58 では Vector3.right すなわち +X 向き）。
推測（仮説）: プレイヤーが +X を向いている場合、体の前方は +X であり、ワールドの +Z は「体の右側（カメラ手前側）」である。テストのコメント「拳が体の前（構え）に無い」と実際の判定軸（Z 軸）の意味が食い違っている（構えで右側に拳を出しているからたまたま通っているだけの可能性がある）。
修正提案: _player.transform.InverseTransformPoint(rig.LHand.position).z のようにキャラクターのローカル座標系で前方（Z）を測る。 
TestScene.RestoreAudio() がどこからも呼ばれていない
場所: TestScene.cs:56-63
確定事実: SilenceAudio() は LoadPrototype() から呼ばれるが、RestoreAudio() は本チャンクのどのファイルからも呼ばれていない。
推測（仮説）: RuntimeInitializeOnLoadMethod（34 行目）で次の Play 開始時に 1f に戻る仕組みはあるが、テストランナー終了直後にエディタ上の他のオーディオプレビュー等に影響する可能性がある。
修正提案: NUnit の [OneTimeTearDown] を持つテストフィクスチャまたはアセンブリレベルのティアダウンで TestScene.RestoreAudio() を呼ぶ。 
Low（テスト不足・保守性・パフォーマンス/GC）
テストカバレッジの不足領域
場所: 全般
確定事実: 以下の重要な状態遷移・割り込みパターンが本チャンクのテストに含まれていない。
押さえつけ（Pin）の途中で拘束側（_captor）が第三者からの攻撃やイベントで怯み（Stagger）・死亡した場合の即時解放と姿勢復帰（Back については PlayabilityRegressionTests.cs:239 にあるが、Pin にはない）。
拘束（Pin / Back / BearHug）から解放された直後の再入場（クールダウンの境界値テスト）: クラスコメント（PinGrappleTests.cs:14）に「クールダウン外だけ」とあるが、解放直後に再度 CanStartPin が false になり、クールダウン経過後に true になる検証がない。
ピンポン演出（PinballRuntimeTests）中の割り込み: 往復中や挟み撃ち中に、敵の片方が撃破された場合の強制終了・状態復帰テストがない。
ポーズ中の入力遮断: UiRuntimeTests で Time.timeScale = 0f になることは検証しているが、ポーズ中にもがき連打（Struggle）や攻撃・カメラズームが受け付けられないことの検証がない。 
テストヘルパーの重複（車輪の再発明）
場所: PinGrappleTests.cs:315-327, PlayabilityRegressionTests.cs:265-270,305-309,425-429,453-457, VrmRuntimeTests.cs:23-35,62-70
確定事実: VRM と ProceduralRig のロード完了を待つ while ループが各テストメソッドにコピペされている一方、同じ処理を共通化した TestRig.WaitReady（TestRig.cs:36）が用意されているのに使われていない。また、WaitRealtime（UiRuntimeTests.cs:407）や WaitUnscaled（PlayerFaceRuntimeTests.cs:68）も各クラスに個別定義されている。
修正提案: VRM 待機処理は TestRig.WaitReady / TestRig.RequireReady に統一し、待機系ユーティリティは TestWait / TestTime に集約する。 
待機ループ内での毎フレーム GetComponentInChildren 呼び出し（GC・負荷）
場所: PinGrappleTests.cs:320, PinballRuntimeTests.cs:57, PlayerFaceRuntimeTests.cs:115
確定事実: while ループの毎フレームで GetComponentInChildren<T>() を呼んでいる。
推測（仮説）: VRM 読み込み中は階層が深く、毎フレーム呼ぶとテストのフレーム時間が伸びてタイムアウトや時間計測の誤差を助長する。
修正提案: VrmCharacterSlot.Loaded が true になってから 1 回だけ GetComponentInChildren を呼ぶ。 
次のステップに向けて
もしよろしければ、以下の点について教えていただければ、より踏み込んだ修正コードや不足テストの実装案を作成できます。
他チャンクとの重複確認: WallPinRuntimeTests や HairPullTests など、別チャンクにあるテストファイルで既にカバー済みの項目（クールダウンや中断割り込みなど）はあるか。
リファクタリングの優先度: まずは「フレーキーさの解消（待機処理とリセット漏れ修正）」と「不足テストの追加」のどちらを優先したいか。
あなたがアップロードしたデータ
·
このカードは自分だけが閲覧できます
chunk-20.txt


## answer-21

# Chunk 21

Files:
- MioPrototype/Assets/RyonaGame/Tests/PlayMode/WallPinRuntimeTests.cs
- MioPrototype/Assets/RyonaGame/Tests/PlayMode/WristLockRuntimeTests.cs
- MioPrototype/Assets/RyonaGame/Tests/PlayMode/WristPullRuntimeTests.cs

---

提供された PlayMode テストコード（3ファイル）のレビュー結果を、重大度別・網羅的に報告します。
Critical（テストの信頼性喪失・偽陽性・実行停止につながる重大な問題）
実時間（Time.realtimeSinceStartup）によるタイムアウト制御とゲーム内時間（Time.time）の乖離
場所: WallPinRuntimeTests.cs (L54, 113, 221, 232, 268, 277, 339, 469, 506, 698, 711, 747, 761, 774, 798, 821, 826, 834, 844, 881, 910, 1017, 1027, 1076, 1180, 1224, 1235, 1259, 1271, 1335, 1353, 1363, 1420), WristPullRuntimeTests.cs (L84)
事実: 待機ループのタイムアウト判定にすべて Time.realtimeSinceStartup を使用している。一方でゲーム進行や物理・アニメーションは Time.timeScale やフレームレート（FPS低下、GCスパイク、エディタのバックグラウンド化）に依存する。
推測: 低スペック環境やCI環境でFPSが落ちた場合、ゲーム内ではまだ数フレームしか進んでいないのに実時間だけ経過してタイムアウト（または測定サンプル不足での誤判定）を起こす可能性が高い。
修正提案: Time.time ベースのヘルパー（既存の TestTime や TestWait.UntilOrTimeout）に統一し、Time.timeScale == 0f のガードのみ別枠で行う。
タイムアウト発生時のサイレント通過（偽陽性）
場所: WallPinRuntimeTests.cs (L54-76, 221-228, 232-241, 268-273, 277-286, 489-523)
事実: while (... && Time.realtimeSinceStartup < deadline) ループがタイムアウトで抜けた直後に、タイムアウト自体を失敗（Assert.Fail）として扱っていない箇所が複数ある。例えば WaitForGrapple() (L508-522) が15秒でタイムアウトしても例外を投げずそのまま後続処理へ進み、直後の Assert.IsTrue(_grapple.IsWallPin) 等で間接的に落ちるか、条件次第では素通りする。特に L232-241 の着地待ちループは、タイムアウトしても airborne や peak が条件を満たしていればテストがパスしてしまう。
修正提案: すべての待機ループでタイムアウト超過時に明示的に Assert.Fail("タイムアウト: <理由>") を呼ぶ。
アサート失敗時のリソース・シーン状態リーク
場所: WallPinRuntimeTests.cs (L593-620, 1166-1203, 1446-1469)
事実: probeGo（調査用・検証用 Camera を持つ GameObject）が try-finally なしで生成・破棄されている（L1166-1203, L1446-1469）。ループ内の FaceVisibility.Measure や Assert で例外が投げられると Object.Destroy(probeGo) が実行されず、シーン内に無効化された/有効な Camera オブジェクトが残留する。また、TearDown() (L27-34) では _player の位置・姿勢・HPや生成した "Test Wall" を破棄していない。
修正提案: 動的に生成した GameObject や Mesh はすべて try-finally で囲むか、TearDown で一括クリーンアップする。
High（Null参照リスク・境界値不備・状態遷移の競合）
Nullチェックの欠如による NullReferenceException リスク
場所: WallPinRuntimeTests.cs (L40-44, 116, 184, 567-569, 662, 725, 1033-1035, 1287, 1303-1305, 1348-1350, 1483-1484), WristLockRuntimeTests.cs (L19-23), WristPullRuntimeTests.cs (L968)
事実:
WallPinRuntimeTests.cs L44: wall.transform.Find("Visuals/Panel").GetComponent<Renderer>() で Find や GetComponent の戻り値を検証せずに即アクセスしている。
WallPinRuntimeTests.cs L116: figure.HipsBone.position.y で HipsBone が null の場合のガードがない。
WallPinRuntimeTests.cs L184, 567-569, 662, 725, 1033-1035, 1348-1350, 1483-1484: anim.Rig.Ready が true であることを確認しているが、Head, LHand, RHand, Hips, Chest, LLowerArm, RLowerArm の各ボーンが個別に null でない保証がない（VRMモデルのボーン欠損時などに NullReferenceException になる）。
WallPinRuntimeTests.cs L968, 1287, 1303-1305: kickFigure / figure の null チェックが抜けており、GetComponent<EnemyFigure>() が null の場合にクラッシュする。
WristLockRuntimeTests.cs L23: enemies[0] に配列長チェック（Assert.IsNotEmpty(enemies)）がない。
修正提案: 各コンポーネント・Transform取得後に Assert.IsNotNull を挟む。
敵のAIとテストコードによる位置・状態の競合（状態遷移の割り込み）
場所: WallPinRuntimeTests.cs (L310-314, 489-523)
事実:
壁拘束中は待機役が手前を横切らず左右に下がる (L310-314) では、Load() で一度無効化した bystanders[0], bystanders[1] の位置を書き換えた直後に enabled = true にしているが、CanPin や攻撃トークン、AIの初期フェーズがリセットされていない（Load() 内で CanPin = false にはしているが、AI内部のタイマーやターゲットが未初期化の可能性がある）。
WaitForGrapple() (L515-519) で 0.5秒ごと に _captor.QueuePin() を連打しているが、予備動作（Windup）に入った直後に再度 QueuePin() を呼ぶと、実装側の QueuePin の仕様次第でモーションやタイマーがリセット・再入場される危険がある。
修正提案: QueuePin() の再送は _captor.Phase == EnemyPhase.Idle（または移動中）のときのみに限定し、攻撃予備動作中の上書きを防ぐ。
固定待機時間（マジックナンバー）によるフレーキー（不安定）なテスト
場所: WallPinRuntimeTests.cs (L182, 350, 536, 560, 641, 671, 675, 724, 1030, 1346, 1409, 1424), WristPullRuntimeTests.cs (L116)
事実: yield return TestTime.WaitGameSeconds(0.4f) や new WaitForSeconds(0.6f) のように、遷移完了や姿勢ブレンド完了を固定秒数で待っている。
推測: アニメーションのブレンド時間や移動速度の定数がわずかに調整されただけで、テストが意図せず失敗したり、逆に遷移が完了しすぎて中間状態を測れなくなったりする（特に L671 の 0.10f 秒後に「まだ壁際で戻る途中」であることを期待するテストはフレームレート変動に極めて弱い）。
修正提案: 可能な限り状態プロパティ（例: _grapple.WallSplitWeight >= 0.99f や座標の閾値条件）を条件待機する形に書き換える。
WristLockRuntimeTests.cs のテスト名と実装の不一致（テスト不足）
場所: WristLockRuntimeTests.cs (L27-42, 45-54)
事実:
メソッド名 八回で割れ四秒で自然に解ける (L27) に対し、テスト内では「8回入力で割れること」と「猶予中は再装着できないこと」しか検証しておらず、「4秒経過で自然に解けること」および「猶予期間終了後に再装着できること」の検証が完全に抜けている。
掴まれると外れて必要回数が増える (L45) では、var grapple = _stats.GetComponent<PlayerGrapple>(); (L49) を取得して未使用のまま、手動で _lock.DetachOnGrab() を呼んでいる。実際に敵に掴まれた（PlayerGrapple が拘束を開始した）際に連動して _lock が外れ、PlayerGrapple.RequiredInputs に倍率が反映される結合パスがテストされていない。
修正提案: 4秒経過後の自然解除テストを追加し、後半のテストは実際に敵の掴みアクションを発生させて PlayerGrapple.RequiredInputs の増加まで検証する。
Medium（GCスパイク・パフォーマンス・境界値・カメラ競合）
フレームループ内の高頻度アロケーション（GCスパイク）
場所: WallPinRuntimeTests.cs (L966, 1285, 1067-1069)
事実: while ループの毎フレーム内で foreach (bool left in new[] { true, false }) (L966, 1285) を実行しており、毎フレーム bool[] 配列をヒープに確保している。また spotLog.Append($"{...}") (L877) で文字列補間による一時文字列を生成している。
修正提案: 配列は static readonly bool[] Sides = { true, false }; としてフィールドにキャッシュする。
探りカメラ（Probe Camera）による本カメラとの競合・副作用
場所: WallPinRuntimeTests.cs (L1446-1469)
事実: L1168 では probe.enabled = false; として描画を止めているが、L1446-1469 の WallSplit Camera Probe では probe.enabled = false を設定していない。デフォルトで Camera コンポーネントは enabled = true かつ depth = 0（Camera.main と同じかそれ以上）になるため、破棄されるまでの1フレーム間、画面描画やポストプロセス・AudioListener等に競合・上書きが発生する可能性がある。
修正提案: WorldToViewportPoint の計算だけであれば描画は不要なため、probe.enabled = false; を明示する。
カメラ遮蔽テストの早期終了とカバレッジ不足
場所: WallPinRuntimeTests.cs (L1077-1121)
事実: while (_grapple.IsWallPin && _grapple.WallPhase == WallStage.Held && ...) でループを回しているが、_grapple.WallPhase が WallStage.TossWindup（投げ捨て予備動作）に遷移した瞬間にループを抜ける。そのため、投げ捨て動作中のカメラアングルや顔可視率は検証されない。また、requiredRatio の検証（L1141-1148）において if (total[i] == 0) continue; としており、フェーズ0〜2のサンプル数が0フレームであってもテストが通ってしまう（total[3] のみ Assert.Greater(total[3], 0) がある）。
修正提案: すべての段階（i = 0..3）について Assert.Greater(total[i], 0) を課し、計測漏れを防ぐ。
境界値・異常系のテスト不足
場所: 全3ファイル共通
事実:
壁の範囲外（X軸境界）: WallPinSite.HalfWidth (L443 で 3f に設定) の端（例: x = wallX + 2.9f や x = wallX + 3.1f）で掴まれた場合に、壁拘束へ移行するか通常拘束になるかの境界テストがない。
HP 0（KO/ダウン遷移）割り込み: 壁拘束の膝蹴りや二人がかり・三人がかりの途中でプレイヤーの HP が 0 になった際の強制解除・ダウン遷移のテストがない（L249 でHP減少は確認しているが、致死ダメージ時の状態遷移が未検証）。
敵の割り込み・撃破: 二人がかり・三人がかりの最中にアシスト役や3人目が外部要因（もし発生し得るなら）で無効化・よろけ状態になった場合のフェイルセーフ検証がない。
引き寄せのキャンセル・妨害: WristPullRuntimeTests.cs において、引き寄せ動作中にプレイヤーの拘束リングが自然解除（4秒経過）または連打破壊された場合に、引き寄せが正しく中断されるかのテストがない。
Low（保守性・可読性・重複コード）
セットアップ・待機ロジックの重複
場所: WallPinRuntimeTests.cs (L231-241 vs L276-286, L51-52 vs L92-93)
事実:
投げ捨て後の放物線飛行と着地を待つロジック（L231-241 と L276-286）が完全に重複している。
Load() 内で _captor.QueuePin() を呼んだ直後に、テスト側でも _captor.QueuePin() を呼び（L92, 173, 530 等）、さらに WaitForGrapple() 冒頭でも _captor.QueuePin() を呼んでいる（L504）。
修正提案: 飛行着地待ちをヘルパーメソッド（WaitForLanding()）に抽出し、冗長な QueuePin() 呼び出しを整理する。
テストメソッド内の未使用ローカル変数
場所: WristLockRuntimeTests.cs (L49)
事実: var grapple = _stats.GetComponent<PlayerGrapple>(); が宣言されているが一度も使用されていない。
修正提案: 不要であれば削除するか、指摘7の通り実際の拘束連携テストに使用する。
ログ出力の残存
場所: WallPinRuntimeTests.cs (L977, 996-1000, 1036, 1124, 1134, 1209, 1319), WristPullRuntimeTests.cs (L96)
事実: 通常実行される [UnityTest] 内に Debug.Log が多数残っており、テスト実行ログが肥大化する（[Explicit] の調査用テスト L1209 は妥当）。
修正提案: 調査が完了した常設テストの Debug.Log は削除するか、アサート失敗時のメッセージ文字列（Assert.Less(..., $"...")）に集約する。


## answer-22

# Chunk 22

Files:
- .github/workflows/runner-unit-tests.yml
- .gitignore
- .ruff_cache/.gitignore
- AGENTS.md
- CHARACTER.md
- Diagnostics.md
- Enemy-VRM-Spec.md
- Joint-ROM.md
- MioPrototype/.ruff_cache/.gitignore
- MioPrototype/.vscode/extensions.json
- MioPrototype/.vscode/launch.json
- MioPrototype/.vscode/settings.json
- MioPrototype/Assets/RyonaCore/Runtime/Ryona.Core.asmdef
- MioPrototype/Assets/RyonaCore/Tests/EditMode/Ryona.Tests.asmdef
- MioPrototype/Assets/RyonaGame/Editor/Ryona.Editor.asmdef
- MioPrototype/Assets/RyonaGame/Resources/RyonaConfig.asset
- MioPrototype/Assets/RyonaGame/Runtime/Ryona.Game.asmdef
- MioPrototype/Assets/RyonaGame/Tests/PlayMode/Ryona.PlayTests.asmdef
- MioPrototype/Assets/StreamingAssets/README.txt

---

提供されたソース（22/26チャンク：設定・CI・ドキュメント・asmdef・アセット）には C# 実装コードが含まれていないため、実行時ロジックの不具合よりも「ドキュメント間の仕様不整合」「CI/テストカバレッジの抜け」「環境依存のハードコード」「設定値の整合性」に問題が集中しています。 
以下に重大度別のレビュー結果を列挙します。
High（仕様不整合・CIテスト漏れ・運用事故リスク）
1. キャラクター身長の仕様不整合（168cm vs 1.55m 前後）
ファイル・行: CHARACTER.md:13, MioPrototype/Assets/StreamingAssets/README.txt:16, Enemy-VRM-Spec.md:39,42
確定できる事実:
CHARACTER.md と StreamingAssets/README.txt では、神楽澪（Mio）の身長を 168cm (1.68m)、頭身を 7.0頭身 と定義している。
一方、Enemy-VRM-Spec.md では「Mio（1.55m 前後）」「Mio は 6 頭身寄り」と記述されている。 
推測（仮説）: VRM スケール調整やカメラの注視点・当たり判定・拘束位置（HeldBellyPunch / PinHoldPose）の基準身長がどちらの前提で組まれているか混在している可能性があり、モデル差し替えや敵との位置合わせ時にズレ（拳が空振りする・顔がフレームアウトする）の原因になる。 
修正提案: 実機アセット（mio_v1.vrm）の実測値に合わせてどちらかのドキュメントの数値を正とし、関連する注視点・身長前提を統一する。
2. CI ワークフローでの Python ツール類のテストカバレッジ不足
ファイル・行: .github/workflows/runner-unit-tests.yml:4-19,35
確定できる事実:
トリガー対象の paths に含まれているのは preview.py と run_tests.py のみであり、AGENTS.md や Diagnostics.md で運用の中核とされている motion_audit.py、preview_reel.py、grapple_verify.py がトリガー対象に含まれていない（またはこれらに対応する test_*.py が存在しない）。
ワークフロー内に ruff 等の Linter/Formatter チェックが組み込まれていない（リポジトリ内に .ruff_cache が存在するため、ローカルでは ruff を使用している痕跡がある）。 
推測（仮説）: motion_audit.py や grapple_verify.py の引数解析や差分計算ロジックを壊しても CI で検知できず、レビュー基準（AGENTS.md）の検証パイプラインがサイレントに破綻するリスクがある。 
修正提案: MioPrototype/tools/*.py 全体を paths トリガーに追加し、motion_audit.py 等のユニットテスト追加と ruff check ステップの導入を行う。
Medium（設定の矛盾・メンテナンス性・境界値リスク）
3. .gitignore におけるプレビュー用ディレクトリのパターン重複と肥大化
ファイル・行: .gitignore:50-61
確定できる事実:
52行目に /preview-belly*/ とワイルドカード指定があるにもかかわらず、59〜60行目に /preview-belly4/、/preview-belly5/ が重複記載されている。同様に 57行目の /preview-front*/ に対し 58行目の /preview-front5/ が重複している。
一方で、AGENTS.md:41 に記載されているシナリオ名 wallpin / wallsplit / ankle / haul に対応する /preview-wallpin*/ 等は .gitignore に含まれていない。 
推測（仮説）: 過去の手動出力先が場当たり的に追記された結果であり、新しいシナリオをルート直下に出力した場合に未追跡ファイルとしてリポジトリに混入する恐れがある。 
修正提案: 重複行を削除し、/preview-*/ および /sweep-*/ に統合して全シナリオを網羅する。
4. .ruff_cache ディレクトリのトラッキング状態
ファイル・行: .ruff_cache/.gitignore:1-3, MioPrototype/.ruff_cache/.gitignore:1-3
確定できる事実:
ルートと MioPrototype/ の2箇所に .ruff_cache/.gitignore が存在し、リポジトリにコミットされている。 
推測（仮説）: ツールの実行カレントディレクトリがルートと MioPrototype/ で揺れているためキャッシュが分散している。ツール自身が自動生成する .gitignore を Git 管理下に入れると、環境によってディレクトリ削除時に差分が出やすい。 
修正提案: ルートの .gitignore に .ruff_cache/ を追加し、自動生成された .ruff_cache/.gitignore は Git 管理から外す（git rm --cached）。
5. ドキュメント内のローカル絶対パスのハードコード
ファイル・行: Diagnostics.md:32-35,100-102,109, Enemy-VRM-Spec.md:19,31, Joint-ROM.md:99-102,148, MioPrototype/Assets/StreamingAssets/README.txt:4
確定できる事実:
開発者固有のローカルパス（C:\Users\neoen\...、C:\gemini-desktop\ryona-action\...）および特定 Unity バージョン（6000.4.5f1）が複数ドキュメントに直書きされている。 
推測（仮説）: 別環境・別ドライブ構成・Unity アップデート時にコマンド例のコピペ実行が失敗する。 
修正提案: <PROJECT_ROOT> や環境変数（%LOCALAPPDATA%、%UNITY_EDITOR%）を用いた表記に統一する。
6. カメラ距離の境界値設定の食い違い（RyonaConfig.asset vs Diagnostics.md）
ファイル・行: MioPrototype/Assets/RyonaGame/Resources/RyonaConfig.asset:17-20,25-26, Diagnostics.md:65-66
確定できる事実:
RyonaConfig.asset では MinDistance: 1.2、BeltMinZoomDistance: 3.8、BeltCloseUpMinDistance: 0.82 と定義されている。
一方、Diagnostics.md によると、タッグ技の腹パン・顔面パンチカメラの既定距離は 0.94m（0.18）および 0.88m（0.17）と記載されている。 
推測（仮説）: ThirdPersonCamera 側で MinDistance (1.2) と BeltCloseUpMinDistance (0.82) のどちらが優先・クランプされるかによって、タッグ技カメラの寄りが意図せず 1.2m で制限される、あるいは壁際めり込みが発生する境界値競合が起きる可能性がある（C# 実装側での確認が必要）。 
修正提案: RyonaConfig.asset の各距離パラメータの適用優先度とクランプ境界をコード・ドキュメント双方で明文化する。
Low（テスト分離・構成上の軽微な懸念）
7. Ryona.Core のアセンブリ分離とテスト環境依存の注意点
ファイル・行: MioPrototype/Assets/RyonaCore/Runtime/Ryona.Core.asmdef:13, MioPrototype/Assets/RyonaCore/Tests/EditMode/Ryona.Tests.asmdef:6-7,23
確定できる事実:
Ryona.Core は "noEngineReferences": true（Pure C#）として設計されている。
テスト側の Ryona.Tests.asmdef は "noEngineReferences": false で UnityEngine.TestRunner 等を参照している。 
推測（仮説）: 現状の構成自体は Unity Test Framework (EditMode) 上で Pure C# ロジックをテストするための標準的な設定であり問題はないが、Ryona.Tests 側で UnityEngine 型を誤ってテスト前提に使うと、将来的に dotnet test 等の Unity 外高速テストへ移行する際の障壁になる。 
修正提案: Ryona.Tests 内では可能な限り UnityEngine 依存を避け、NUnit 標準アサーションのみで記述する方針を維持する。
8. RyonaConfig.asset の設定項目の不足（チャンク途切れの可能性）
ファイル・行: MioPrototype/Assets/RyonaGame/Resources/RyonaConfig.asset:15-42
確定できる事実:
RyonaConfig.asset の内容が Camera: セクション（42行目 DownPitchBias: 20）のみで終わっている。 
推測（仮説）: 他のパラメータ（HP・攻撃力・拘束時間・カメラのタッグ技用パラメータ等）が ScriptableObject ではなく C# コード側の定数に散在している場合、バランス調整や Diagnostics.md の起動引数オーバーライドとの整合性維持が難しくなる。 
修正提案: ゲームバランスや演出カメラの閾値定数は RyonaConfig に集約し、起動引数での差し替えは RyonaConfig のランタイムコピーを上書きする設計にする。
本チャンク（22/26）には C# ソースコードが含まれていないため、ご指定の「null参照・状態遷移の割り込み・リーク・カメラ競合・GC」の実装レベルの精査を行うには該当 C# ファイルが必要です。
もしよろしければ、以下のファイルが含まれるチャンクをお送りいただけますか？
カメラ制御・競合: ThirdPersonCamera.cs / WallSplitCamera.cs
拘束・状態遷移: PlayerGrapple*.cs / GrappleSession.cs / EnemyBrain*.cs
テスト・計測ツール: JointRom*.cs / FaceVisibility.cs / MioPrototype/tools/*.py
あなたがアップロードしたデータ
·
このカードは自分だけが閲覧できます
chunk-22.txt


## answer-24

# Chunk 24

Files:
- MioPrototype/Packages/manifest.json
- MioPrototype/Packages/packages-lock.json
- MioPrototype/ProjectSettings/AudioManager.asset
- MioPrototype/ProjectSettings/ClusterInputManager.asset
- MioPrototype/ProjectSettings/DynamicsManager.asset
- MioPrototype/ProjectSettings/EditorBuildSettings.asset
- MioPrototype/ProjectSettings/EditorSettings.asset
- MioPrototype/ProjectSettings/GraphicsSettings.asset
- MioPrototype/ProjectSettings/InputManager.asset
- MioPrototype/ProjectSettings/MemorySettings.asset
- MioPrototype/ProjectSettings/MultiplayerManager.asset
- MioPrototype/ProjectSettings/NavMeshAreas.asset
- MioPrototype/ProjectSettings/PackageManagerSettings.asset
- MioPrototype/ProjectSettings/Packages/com.unity.testtools.codecoverage/Settings.json
- MioPrototype/ProjectSettings/Physics2DSettings.asset
- MioPrototype/ProjectSettings/PresetManager.asset
- MioPrototype/ProjectSettings/ProjectSettings.asset
- MioPrototype/ProjectSettings/ProjectVersion.txt
- MioPrototype/ProjectSettings/QualitySettings.asset
- MioPrototype/ProjectSettings/SceneTemplateSettings.json
- MioPrototype/ProjectSettings/TagManager.asset
- MioPrototype/ProjectSettings/TimeManager.asset
- MioPrototype/ProjectSettings/UnityConnectSettings.asset
- MioPrototype/ProjectSettings/VFXManager.asset
- MioPrototype/ProjectSettings/VersionControlSettings.asset
- MioPrototype/ProjectSettings/XRSettings.asset
- MioPrototype/README.md
- MioPrototype/UserSettings/EditorUserSettings.asset
- MioPrototype/UserSettings/Search.settings
- MioPrototype/tools/.ruff_cache/.gitignore
- MioPrototype/tools/BuildAndPlay.cmd
- MioPrototype/tools/RunHaulDemo.cmd
- MioPrototype/tools/frameseq.py
- MioPrototype/tools/grapple_verify.py
- MioPrototype/tools/motion_audit.py
- MioPrototype/tools/preview.py
- MioPrototype/tools/preview_reel.py
- MioPrototype/tools/run_tests.py
- MioPrototype/tools/test_preview_build.py
- MioPrototype/tools/test_run_tests.py

---

チャンク 24（tools / config / docs の 40 ファイル）にはランタイムの C# ゲームコード（カメラ演出・コルーチン・ゲーム状態遷移等）は含まれておらず、Python 製検証ツール群・バッチファイル・Unity プロジェクト設定の不具合とテスト不足を中心に 18 件の指摘を抽出しました。 
Critical（致命的・ツールがクラッシュまたは破壊的状態に陥る不具合）
1. audit_sheet() が worst_ratio を一切更新せず常に 0.0 を返す
場所: MioPrototype/tools/motion_audit.py:84-113
事実: 84行目で worst_ratio = 0.0 を初期化し 113行目で return worst_ratio としていますが、ループ内で値が一度も更新されていません。さらに 203行目の main() でも戻り値を破棄しています。
修正提案: dv / avg の最小比率または異常値を worst_ratio に記録し、--threshold 指定時に終了コード 1 を返せるように修正してください。 
2. grapple_verify.py の --baseline が未追跡ファイル・破損ワークツリーで失敗しリカバリ不能になる
場所: MioPrototype/tools/grapple_verify.py:208-215
事実: BASE_WORKTREE が既に存在する場合に git checkout --force --detach ref のみを実行しており、前回の中断で .git/worktrees/verify-baseline のメタデータが壊れている場合や、未追跡ファイルが残っている場合に失敗して終了します。また、ワークツリーの削除・クリーンアップ手段がありません。
修正提案: git clean -fd の実行と、git worktree list に登録されていない不完全ディレクトリ検出時の git worktree prune ＋再作成を追加してください。 
High（実運用で誤判定・データ欠損・環境依存エラーを起こす不具合）
3. grapple_verify.py の run_scenario() におけるログ全体スキャンによる誤検知とファイルI/O負荷
場所: MioPrototype/tools/grapple_verify.py:249-253
事実: done_marker（"pin burst"）の検出を log.read_text(...) で毎回ログ先頭から全文読み込んで判定しています。もし Unity 起動時の引数エコー等に "pin burst" が含まれると即座に打ち切られ、またログ肥大化時に1秒ごとの全文読み込み・デコード負荷が発生します。
修正提案: ログの読み込みオフセットを保持して追記分のみ読むようにし、完了を示す明確なプレフィックス（例: [PinBurst] done）で判定してください。 
4. grapple_verify.py のフレーム glob パターンが 100 枚以上の連番を取りこぼす
場所: MioPrototype/tools/grapple_verify.py:260, 523-527
事実: フレーム取得に f"{name}_[0-9][0-9].png" と2桁固定のパターンを使用しているため、frames を 100 以上に設定したシナリオを追加すると 100 枚目以降がすべて無視されます（frameseq.py の frame_files も使われていません）。
修正提案: frameseq.frame_files() と同様の数値ソート関数を使うか、桁数に依存しない正規表現 rf"^{re.escape(name)}_(\d+)\.png$" で抽出してください。 
5. RunHaulDemo.cmd に開発環境固有の絶対パスがハードコードされている
場所: MioPrototype/tools/RunHaulDemo.cmd:10-11
事実: C:\gemini-desktop\ryona-action\MioPrototype\Build\MioPrototype.exe が直書きされており、別ドライブや別ディレクトリ・別マシンにリポジトリを配置すると必ず失敗します。
修正提案: BuildAndPlay.cmd と同様に set "PROJECT=%~dp0.." を使って相対パスで解決してください。 
6. preview.py と grapple_verify.py でビルド判定・Unity検索ロジックが重複し不整合がある
場所: MioPrototype/tools/preview.py:138-185 / MioPrototype/tools/grapple_verify.py:176-199
事実: preview.py は終了コード 0 でもログに build result=Succeeded か up to date が無ければ失敗（BUILD_UNCONFIRMED）と判定しますが、grapple_verify.py は終了コードと exe.exists() のみで判定しコンパイルエラーログを確認しません。また unity_exe() と unity_running() が 3 ファイル（preview.py, run_tests.py, grapple_verify.py）にコピペされています。
修正提案: Unity 検出・ビルド実行・結果判定を共通モジュール（例: unity_cli.py）に統合してください。 
7. make_sheet() で files が空リストの場合に IndexError でクラッシュする
場所: MioPrototype/tools/preview.py:240-245
事実: files が空の状態で make_sheet() が呼ばれると picked[0] で IndexError が発生します（make_gif() には if not frames: return のガードがありますが make_sheet() にはありません）。
修正提案: make_sheet() の冒頭に if not picked: return を追加してください。 
Medium（境界値バグ・メンテナンス性・テスト不足）
8. frameseq.frame_index() が想定外のファイル名で IndexError / ValueError を起こす
場所: MioPrototype/tools/frameseq.py:18-27
事実: os.path.splitext(os.path.basename(path))[0].split("_")[1] としているため、ディレクトリ内に f_backup.png や f_001_diff.png などのファイルが混在すると sorted(..., key=frame_index) 全体が例外で落ちます。
修正提案: 正規表現 r"_(\d+)$" に一致するファイルのみを対象にするフィルタを入れてください。 
9. preview_reel.py に Windows 固有フォントパスがハードコードされている
場所: MioPrototype/tools/preview_reel.py:25
事実: FONT_PATH = Path(r"C:/Windows/Fonts/meiryo.ttc") とハードコードされており、macOS/Linux 環境やメイリオ未インストール環境では ImageFont.load_default() にフォールバックして日本語タイトルが文字化け（豆腐化）します（grapple_verify.py:371 は複数候補を探索しています）。
修正提案: grapple_verify.py の _font() と同様に OS 別候補リストから探索する共通ヘルパーを使用してください。 
10. grapple_verify.py の ROM パーサが日本語ログ書式に密結合しており変更に弱い
場所: MioPrototype/tools/grapple_verify.py:122-128 
事実: [JointROM] のログ文字列を日本語表記（（可動域 ...・曲げすぎ|逆方向））の正規表現でパースしており、C# 側のログ文言が1文字でも変わるとサイレントに「逸脱なし」と誤判定されます。 
推測（仮説）: Unity 側の出力フォーマット変更時に rom_report.md が空になり、回帰を見逃すリスクがあります。
修正提案: Unity 側から機械可読な JSONL 形式（または固定キー=値形式）で出力するか、C# 側のフォーマット文字列を検証する契約テストを追加してください。
11. preview.py の capture() がプロセス自発終了後も最大 8 秒間無駄にポーリングを続ける
場所: MioPrototype/tools/preview.py:211-221
事実: ループ内で proc.poll() を確認していないため、キャプチャ枚数が frames に満たずにプレイヤーが自発終了・クラッシュした場合でも stable >= 8（約8秒）まで待機し続けます。
修正提案: ループ内で if proc.poll() is not None: break をチェックしてください。 
12. run_tests.py の --filter が常に PlayMode 固定で EditMode テストを絞り込めない
場所: MioPrototype/tools/run_tests.py:214, 224-226
事実: --filter を指定するとプラットフォームが "PlayMode" にハードコードされるため、EditMode の特定テストクラスだけを高速に回すことができません。
修正提案: --platform EditMode|PlayMode オプションを追加するか、--only edit --filter ... の併用を許可してください。 
13. run_tests.py で全テストが Skipped / Ignored の場合に成功（終了コード 0）扱いになる
場所: MioPrototype/tools/run_tests.py:175-185（test_run_tests.py:204-209 に現行仕様として記載あり）
事実: [Ignore] 属性が付いたテストしかヒットしなかった場合、passed == 0, skipped > 0 でも ok = True となり終了コード 0 を返します。
修正提案: stats["passed"] == 0 の場合は警告を出すか、--strict オプションで失敗扱いにできるようにしてください。 
14. test_preview_build.py と test_run_tests.py 以外の Python ツールに単体テストが一切無い
場所: MioPrototype/tools/ 配下（frameseq.py, motion_audit.py, grapple_verify.py, preview_reel.py）
事実: テストが存在するのはビルド判定（test_preview_build.py）とテストランナー（test_run_tests.py）のみであり、画像合成・差分計算（motion_audit.py）や ROM 集計・差分比較（grapple_verify.py）のロジックに自動テストがありません。
修正提案: grapple_verify.py の parse_rom / aggregate / do_compare と、motion_audit.py の diffs / audit_sheet に対する unittest を追加してください。 
Low（プロジェクト設定・運用上の軽微な懸念）
15. ProjectSettings.asset のバンドル識別子と会社名がデフォルト値のままで、開発者分析が有効
場所: MioPrototype/ProjectSettings/ProjectSettings.asset:15, 92, 167
事実: companyName: DefaultCompany、applicationIdentifier.Standalone: com.DefaultCompany.3D-Project とテンプレート初期値のままになっており、submitAnalytics: 1 が有効です。
修正提案: 配布ビルド前に固有の companyName と applicationIdentifier を設定し、不要であれば submitAnalytics を無効化してください。 
16. manifest.json に実験的パッケージ com.unity.pipeline: 0.9.0-exp.1 が含まれている
場所: MioPrototype/Packages/manifest.json:46
事実: 実験版（-exp.1）パッケージが含まれており、将来の Unity 6 マイナーアップデート時に互換性エラーや API 変更の影響を受ける可能性があります。
推測（仮説）: テンプレート由来で未使用のまま残っている可能性があります。
修正提案: 未使用であれば manifest.json から削除し、依存関係を最小化してください。 
17. BuildAndPlay.cmd で Unity プロセス存在チェックが別プロジェクトの Unity にも反応する
場所: MioPrototype/tools/BuildAndPlay.cmd:27-31（および preview.py:122-128 等）
事実: tasklist /fi "imagename eq Unity.exe" でシステム上の全 Unity.exe を検出しているため、別リポジトリの Unity Editor を開いているだけでもビルドがスキップまたは拒否されます。
修正提案: Temp\UnityLockfile の存在およびロック状態を確認する方式に切り替えると、当該プロジェクトが開かれているかだけを正確に判定できます。 
18. AudioManager.asset の m_SampleRate が 0（システム任せ）でデバイス依存の挙動になり得る
場所: MioPrototype/ProjectSettings/AudioManager.asset:11
事実: m_SampleRate: 0 のため、ハイレゾオーディオデバイス（96kHz/192kHz等）接続環境では DSP 負荷が増大したりピッチ・タイミング計算に差異が出る場合があります。
修正提案: 特別な理由がなければ 48000 に固定することを検討してください。 
※ ご指定の「null参照・状態遷移の割り込み・コルーチンリーク・カメラ演出の競合」については、本チャンク（24/26）に C# ソースコードが含まれていないため該当箇所がありません。該当コードを含むチャンクをご提示いただければそちらも同様にレビュー可能です。
他のチャンク（特に C# の カメラ制御 や 拘束・状態遷移 を含むチャンク）も続けてレビューしますか？
あなたがアップロードしたデータ
·
このカードは自分だけが閲覧できます
chunk-24.txt


## answer-25

# Chunk 25

Files:
- Motion-Notes.md
- Outfit-Fix.md
- PIPELINE.md
- README.md
- REVIEW.md
- SFX-Audition.md
- SPEC.md
- Tempo-Check.md
- VRoid-20min.md

---

本チャンク（25/26）はすべて Markdown ドキュメント群であり C# ソースコードは含まれていないため、ドキュメント間の仕様・数値の不整合、未解決リスクの追跡漏れ、テスト・運用手順の不備をコードレビューの観点（境界値・状態遷移・リソース管理・カメラ競合・テスト不足）に沿って列挙する。
Critical
該当なし（本チャンクはドキュメントのみであり、実行時クラッシュやビルド破壊に直結する確定バグはすでに REVIEW.md 23-44行目で修正済みとして記録されている）。
High
1. 状態遷移の異常終了・割り込み経路が未監査のまま残存
ファイル・行: REVIEW.md 19, 72-80, 123-128行目
区分: 確定（事実）（実装コード自体は本チャンク外のため、バグの有無は推測/未検証）
内容: EnemyBrain（9ファイル分割）・PlayerGrapple（6ファイル分割）・カメラ系（6クラス）において、状態遷移の所有権と異常終了（ダウン割り込み、連続拘束、コルーチン終了条件、ターゲット破棄時の残留）の監査が「第2段階の未実施タスク」として残っている。特に Motion-Notes.md 847-851行目で追加された Phase.FinisherDodge では、EnemyBrain.BeginEvade の早期 return 漏れや ClearFinisherBuckle の解除順序ミスによる位置・腰高の跳ねが過去に実測で発生しており、割り込み・解除時の状態不整合リスクが高い。
修正提案: EnemyBrain と PlayerGrapple の全終了・中断パス（解除・ダウン・死亡・シーンリセット）を状態遷移表に落とし、各出口でロック・トークン・カメラ要求・回避フラグ（EndEvade 等）を必ず解放するテストを追加する。
2. カメラ演出の2系統競合と切り替え時の不連続（飛び）が未解決
ファイル・行: Motion-Notes.md 765-801, 839行目 / REVIEW.md 78, 126行目
区分: 確定（事実）
内容: 押さえつけ系（ApplyCinematicShot）と腹パン/顔パン演出（Push/HoldFocus）の2系統でカメラ制御が分断されており、受け渡し時に平均の 4.9〜6.6 倍の画像差分（画の飛び）が発生している。出口ブレンド（0.35秒）は追加されたが、入り口側の受け渡しは補間を遅くすると「顔が画面に映っている率」が 84% → 59% へ悪化してテストが落ちるため取り消された経緯がある。
修正提案: 同一シーン内で 2 系統のカメラモードを直接受け渡さないよう単一のカメラスタック（優先度付きリクエスト）へ統合するか、仕様として意図的な暗転・ヒットストップを挟むカット演出に統一する。
3. VRM 差し替え・シーン再読込時のリソース（VRAM）リーク懸念
ファイル・行: REVIEW.md 70, 127行目
区分: 確定（コード上未呼び出しの事実） / 推測（実際のVRAMリーク有無は未計測）
内容: EnemyVrm.cs / VrmCharacterSlot.cs が Destroy(vrm.gameObject) のみを呼び出し、RuntimeGltfInstance.Dispose() や Resources.UnloadUnusedAssets() を呼んでいない。VRM の動的差し替えやポーズメニューからの R（シーン再読み込み、SPEC.md 389行目）を繰り返すとテクスチャ・メッシュが蓄積する恐れがある。
修正提案: VRM 破棄時に RuntimeGltfInstance.Dispose() を明示的に呼び出し、シーン再読込前後で Profiler の VRAM 使用量を計測する。
4. 3人がかり壁拘束（リンチ）における被写体遮蔽（視認性破綻）
ファイル・行: Motion-Notes.md 635-639行目
区分: 確定（実測済みの未解決課題）
内容: 壁の3人がかり演出（#288〜#500）において、約4割のフレームで敵の体がプレイヤーを隠しており動作が読めない。OcclusionFade で意図的に除外しているため打撃タイミングの調整だけでは解消できない状態。
修正提案: 立ち位置をカメラ視線を遮らない弧状配置に広げるか、俯瞰角度（斜め上）へのカメラオフセット、または手前側の敵の半透明化（VRM の場合はディザ抜きの代替）を導入する。
Medium
5. 拘束解除直後の入力バッファによる攻撃暴発リスク
ファイル・行: REVIEW.md 55, 85行目 / README.md 32-36行目
区分: 確定（未実装の事実） / 推測（実プレイ時の暴発頻度）
内容: 振りほどき操作が攻撃ボタン（J / K / L）の連打と同一キーに割り当てられている一方、現在の InputBufferSeconds = 0.15f は硬直中の先行入力用であり、拘束解除直後の入力受付遅延（マスク）が存在しない。連打で脱出した直後に意図しない弱・強・崩し攻撃が暴発する境界条件がある。
修正提案: RyonaConfig に拘束解除直後専用の入力マスク時間（例: 0.15〜0.20s）を新設し、連打の余剰入力を破棄する。
6. テスト・仕様書間での数値・パラメータの乖離（ドキュメント陳腐化）
ファイル・行:
SPEC.md 198行目（距離約 1.5m＝5.2×0.28、ピボット +0.27m、ヨー −40°、俯角 6°） vs Motion-Notes.md 445-448, 682, 753-754行目（腹パン距離倍率 0.24・ピボット 0.48・ピッチ -12・ヨー -23 / 顔パン距離倍率 0.22・ピボット 0.58・ピッチ -3・ヨー -36）
SPEC.md 110行目（ダウン 2.0s = 倒れ 1.2s + 起き上がり 0.8s） vs Motion-Notes.md 14行目（DownFallSeconds = 0.95s に統一）
SPEC.md 57, 76行目（画面1枚 9m、既定 10波） vs Tempo-Check.md 3, 38, 53行目（ArenaPitch = 10、5アリーナ×2波）
SPEC.md 38-41行目 / PIPELINE.md 38-40行目（黒＋くすんだ赤、長袖ジャケット＋カーゴパンツ） vs Outfit-Fix.md 30, 38-48行目（白トップス＋濃いグレーのボトムス）
REVIEW.md 97行目（テスト属性 197 箇所） vs REVIEW.md 116-119行目（実実行数 63+100+48+61 = 272 件）
区分: 確定（文書間の矛盾）
内容: 仕様書（SPEC.md / PIPELINE.md）が初期設計のまま更新されておらず、実装済みの調整値（Motion-Notes.md / RyonaConfig.asset）と食い違っている。新規実装やテスト作成時に誤った定数を参照する原因になる。
修正提案: SPEC.md の古い数値記述を削除して RyonaConfig.asset および Motion-Notes.md を参照先として明記するか、現行値へ同期する。
7. ビルド成功判定の不一致（preview.py vs BuildAndPlay.cmd）
ファイル・行: REVIEW.md 69行目
区分: 確定（事実）
内容: BuildAndPlay.cmd は Unity の終了コード（errorlevel 1）で失敗を検知するよう修正されたが、MioPrototype/tools/preview.py の build_player() は終了コード code を無視し、ログ文字列のマッチのみで成否を判定している。
修正提案: preview.py でもプロセス終了コード code != 0 を最優先の失敗条件として扱うよう統一する。
8. PlayMode テストの非決定性と既知フレークの放置
ファイル・行: REVIEW.md 86, 119-121行目
区分: 確定（事実）
内容: PlayMode テスト内に WaitForSeconds が 57 箇所あり、WaitUntil やフレーム待機が使われていないため、マシン負荷やフレームレート変動でタイミングがずれるリスクがある。実際に PlayerFaceRuntimeTests の「おふ」口形テストが既知フレークとして全体実行時に落ちている。
修正提案: 物理演算の経過待ち以外の WaitForSeconds を条件待機（WaitUntil）または固定フレーム数待機へ置き換え、既知フレークのタイミング境界を安定化する。
9. run_tests.py の単体テストと CI カバレッジ不足
ファイル・行: REVIEW.md 87, 105行目
区分: 確定（事実）
内容: テストランナー run_tests.py の異常系（XML 破損・欠落、Skipped のみ、タイムアウト、複数バッチ途中失敗など）の検証が不足しており、CI も run_tests.py 関連ファイル変更時にしか発火しない。
修正提案: test_run_tests.py に異常入力・途中失敗ケースの単体テストを追加し、C# 変更時の簡易静的チェックまたはテスト実行ワークフローを整備する。
10. 背後・正面ロール時の回転順序と座標基準に関する潜在的な罠
ファイル・行: Motion-Notes.md 118, 257, 270-290行目
区分: 確定（過去の不具合記録に基づくリスク）
内容:
敵のダウン床補正（118行目）では「前フレームの持ち上げを 0 に戻してから計測する」必要があり、順序が崩れると毎フレーム高さがドリフトする。
うつ伏せ→仰向けロール（257行目）ではワールド基準の across を使うと 90° で左右符号が反転する。
寝返り後のリグ 180° 反転（270-290行目）では未特定の環境差（別ビルド等）で敵が逆向きに殴る現象が報告されており、BeginResettle の保険処理に依存している。
修正提案: これらの座標変換・リセット順序を RyonaCore 側の純粋関数として切り出し、EditMode テストで 0°/90°/180°/270° の境界角度を網羅検証する。
Low
11. エディタ非アクティブ時の自動ビルド抑制漏れ
ファイル・行: REVIEW.md 56, 88行目
区分: 確定（事実）
内容: AutoPlayerBuild.cs は Play モードやコンパイル中のビルド延期には対応しているが、Unity エディタがバックグラウンド（非アクティブ）状態でのビルド抑制判定が未実装。
修正提案: UnityEditorInternal.InternalEditorUtility.isApplicationActive を確認し、非アクティブ時はビルドを保留する。
12. 顔面パンチ時の前髪貫通・顔隠れ（毛束単位の未制御）
ファイル・行: Motion-Notes.md 691-692行目
区分: 確定（実測済みの制約）
内容: 首を 40° 以上大きく振る顔面パンチ時、髪重力を 0.35 倍にしても横顔に前髪が掛かり表情が見えにくくなるケースが残っている。
修正提案: 顔周辺の VRM SpringBone コライダー配置調整、またはカメラ側毛束の揺れ制限（最大振れ角クランプ）を検討する。
13. 効果音・設定ファイル内の古い記述の残存
ファイル・行:
SFX-Audition.md 44-59行目（1〜14番の表に SfxId.WallSlam［Motion-Notes.md 424行目で作り直された壁叩きつけ音］が未記載）
PIPELINE.md 14-15行目（VRoid Studio・Blender が「未インストール」表記のまま。VRoid-20min.md 12行目では「インストール済み」）
VRoid-20min.md 18行目 vs 103行目（VRM の保存先パスが MioPrototype\Assets\StreamingAssets\mio_v1.vrm と C:\gemini-desktop\ryona-action\models\ の2箇所で異なる）
区分: 確定（文書内の不整合）
内容: 手順書の保存先パスや効果音リストが最新状態と一致しておらず、手作業時の配置ミスや確認漏れを招く。
修正提案: SFX-Audition.md に WallSlam を追記し、VRoid-20min.md の出力パスを Assets\StreamingAssets\mio_v1.vrm に統一する。


## answer-26

# Chunk 26

Files:
- docs/GRAPPLE_NOTES.md
- docs/motion-audit.md
- docs/plans/2026-11-02-held-belly-punch-design.md
- docs/plans/2026-11-02-held-belly-punch.md
- docs/plans/2026-11-03-pin-grapple-design.md
- docs/plans/2026-11-06-grapple-handoff.md
- docs/plans/2026-11-06-player-down.md
- docs/review/state-transitions.md
- viewer/README.md

---

本チャンク（26/26）はすべてドキュメント（.md）で構成されており、C#等のソースコード本体は含まれていません。そのため、ドキュメント内に記録された設計・実装記述の矛盾、状態遷移の欠陥、テストや仕様の不整合を中心にレビューを行います。 
High（設計・仕様上の重大な競合や欠陥の記録）
カメラ演出の優先順位迂回による死亡フレーミングの破綻（A-5）
箇所: docs/review/state-transitions.md:327-335, docs/review/state-transitions.md:486-496
確定事実: ThirdPersonCamera.HoldFocus は PushDistance の合成規則（寄りと引きの競合解決）を迂回し、_pushScale 等へ直接代入して _pullScale = 1f にリセットしている。これにより、死亡演出（ApplyDeathFraming の引き）と拘束演出（HoldFocus の寄り）が同一フレームで走ると、呼び出し順次第で死亡カメラが上書きされて機能しなくなる。
修正提案: HoldFocus 内でも _pullScale を直接リセットせず、PushDistance と同じ合成規則（または明示的な演出優先度フラグ）を通す設計に統一してください。 
壁二人がかり（Y字拘束）の解除時における補間スキップ（A-2）
箇所: docs/review/state-transitions.md:454-463
確定事実: TickSplitWeight は入り・抜きともに 0.60 秒の SmoothStep 補間を行う設計であるにもかかわらず、脱出・解放時の ClearSplit が位相を無視して SplitWeight = 0f を即時代入している。
推測（仮説）: この即時リセットにより、姿勢（PinHoldPose）とカメラ（WallSplitCamera）が1フレームで急変し、カメラの飛び（docs/motion-audit.md:57-65 と同質の視覚的破綻）を引き起こす可能性が高いです。
修正提案: ClearSplit で即座に 0f にせず、退出フェーズへ遷移させて 0.60f かけて減衰させるか、意図的なカットであれば設計書に明記してください。 
引き渡し（shove: false）経路での所有権・AI状態の不整合（A-4）
箇所: docs/review/state-transitions.md:202, docs/review/state-transitions.md:476-484
確定事実: PlayerGrapple.HandOff で shove: false を指定した分岐は _session.Captor = next のみを書き換え、next.TakeOverHold(...) を呼ばない。
推測（仮説）: 現状は未使用経路ですが、テストや新規演出からデフォルト引数（shove = false）で呼ばれると、新 Captor が Chase 状態のまま通常攻撃を開始し、二重拘束やモーション破綻を起こします。
修正提案: shove: false 分岐内でも必ず next.TakeOverHold(...) を呼び出し、状態遷移を shove: true 経路と対称にしてください。 
Medium（バグの温床・状態リセット漏れ・システム間の矛盾）
ダウン・死亡時の状態リセット漏れ（A-3, A-6）
箇所: docs/review/state-transitions.md:156-158, docs/review/state-transitions.md:188, docs/review/state-transitions.md:465-474, docs/review/state-transitions.md:498-505
確定事実:
DoPinKnee のみに Player.IsDead の早期リターンが欠落しており、HP 0 到達フレームで被弾演出・SFX・ヒットイベントが二重発火する（A-3）。
Die() および Revive() で HoldsBellyPunchGuard フラグがリセットされない（A-6）。
修正提案: DoPinKnee の冒頭に if (Player.IsDead) return; を追加し、Die() / Revive() 内のクリーンアップ対象に HoldsBellyPunchGuard = false を含めてください。 
同一フレーム内の二重適用によるパラメータ過剰ブレンド（A-1）
箇所: docs/review/state-transitions.md:445-452
確定事実: CharacterAnimator.Pose.cs:93-101 において、同一条件の ApplyHaulDaze(ref pose) が2回連続で呼ばれており、腕の重みが設計値 0.85 ではなく 0.9775 に増幅されている。
修正提案: 重複している2つ目の if ブロックを削除してください。 
実行順（DefaultExecutionOrder）の暗黙の競合
箇所: docs/review/state-transitions.md:177, docs/review/state-transitions.md:423-425
確定事実:
PlayerGrapple に DefaultExecutionOrder が設定されていないため、Captor 死亡から解放までの1フレーム間の処理順が不定である。
CharacterAnimator.LateUpdate（順 20000）で加算した引き渡し押し出し姿勢（PushPulse）の腕回転が、後続の PinHoldPose（順 21000）の IK 解決によって上書きされる。
修正提案: PlayerGrapple に明示的な DefaultExecutionOrder（EnemyBrain と CharacterAnimator の間）を付与し、PinHoldPose 側で PushPulse > 0 の間は腕の IK 上書きをスキップまたはブレンドしてください。 
押さえつけ（Pin）からタッグ腹パンへの派生仕様に関するドキュメント間の矛盾
箇所: docs/GRAPPLE_NOTES.md:134-135, docs/plans/2026-11-06-grapple-handoff.md:14-17
確定事実: GRAPPLE_NOTES.md:134 では「押さえつけ（Pin）は時間制限つきの短い技なので対象外」と記載されている一方、2026-11-06-grapple-handoff.md:14 では「1人目が拘束（後ろ / 押さえつけ）└ タッグ腹パンのコンボが終わる」と記載されており、仕様記述が食い違っています。
修正提案: 現行コードの実装（GrappleHandOff が Pin を弾いているか）に合わせ、古い設計メモ側に注記を入れて統一してください。 
起き上がり無敵中の腹ダメージ未加算と Escape 後の残留仕様（未確認項目 1, 2）
箇所: docs/review/state-transitions.md:262-265, docs/review/state-transitions.md:550-551
確定事実:
Escape（自力脱出）では _heldBellyDamage がクリアされず、次回拘束の Fail まで持ち越される。
起き上がり直後の無敵時間（_downImmuneUntil）中に掴まれると、CanTakeFlinch が false のため腹パンを受けても腹ダメージが一切蓄積しない。
推測（仮説）: 無敵中に掴まれた場合、プレイヤー視点では「腹パン5発を食らったのにダウンしない（または回数が増えて理不尽）」という不整合な挙動になる恐れがあります。
修正提案: 拘束成立（BeginHold）時に _downImmuneUntil をリセットするか、腹ダメージ加算条件から無敵判定を除外すべきか仕様を確定させてください。 
Low（テスト不足・環境依存・保守性）
回帰テスト・実行時検証の未実施項目（8件）の放置
箇所: docs/review/state-transitions.md:544-573
確定事実: 静的監査で特定された A-1〜A-7 の不具合および 8 件の挙動未確認項目について、PlayMode テストや motion_audit.py による数値検証が未実施のまま残っています。
修正提案: docs/review/state-transitions.md:565-572 に記載された手順でキャプチャ監査を行い、特に A-3（死亡時二重ヒット）と A-4（HandOff 不整合）は PlayMode テストの回帰ケースとして追加してください。 
外部 CDN 依存とハードコードされたローカルパス
箇所: viewer/README.md:11, viewer/README.md:57, viewer/README.md:68
確定事実: ローカル起動コマンド例に開発者固有の絶対パス（C:\gemini-desktop\ryona-action\viewer）が記載されており、three.js / @pixiv/three-vrm を外部 CDN から取得するためオフライン環境で動作しません。
修正提案: 起動コマンドは相対パス（--directory viewer）に修正し、オフライン利用を想定する場合はライブラリをローカル同梱してください。 
誤記（セクション番号の逆転）
箇所: docs/GRAPPLE_NOTES.md:112, docs/GRAPPLE_NOTES.md:130
確定事実: 見出しの順番が 7.3 → 7.5 → 7.4 となっており、番号が逆転しています。
修正提案: セクション番号を 7.3 → 7.4 → 7.5 の順に修正してください。 
次はどの作業を進めますか？以下から選んでいただければ、具体的な実装案やテストコードを作成します。
A-1〜A-6 のバグ修正用 C# コード案の作成（該当クラスのソースをご提示いただければ直接パッチを書きます）
未検証項目（8件）を網羅する PlayMode テストケースの設計・実装
カメラ競合（HoldFocus と PushDistance）の統合設計の詳細化
あなたがアップロードしたデータ
·
このカードは自分だけが閲覧できます
chunk-26.txt
