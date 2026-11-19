using System;
using System.Collections;
using UnityEngine;

namespace Ryona.Game
{
    public sealed partial class PlayerGrapple : MonoBehaviour
    {
        /// <summary>
        /// 壁へ押し出しながら押さえつける。掴み方は <see cref="GrappleKind.Pin"/> と同じで、
        /// 立つ位置だけ壁の前に差し替える（もがき・膝蹴り・制限時間はそのまま）。
        /// </summary>
        internal void BeginWallPin(EnemyBrain captor, Vector3 spot, Quaternion rotation,
            Vector3 wallNormal, float faceZ, WallPinSite site)
        {
            if (IsGrappled || Stats == null || Stats.IsDead)
                return;

            _wallSite = site;
            _wallStartPosition = transform.position;
            _wallStartRotation = transform.rotation;
            _wallPinSpot = spot;
            _wallPinRotation = rotation;
            WallFaceZ = faceZ;
            _wallPinNormal = wallNormal.sqrMagnitude > 0.001f
                ? wallNormal.normalized
                : Vector3.back;
            _session.IsWall = true;
            WallPinWeight = 0f;
            _wallExitRemaining = 0f;

            // 押し出しの初速は今のレーン。接触のブレンドに合わせて少しずつ壁へ寄せる。
            var motor = Motor();
            if (motor != null)
                motor.SetLaneZOverride(transform.position.z);

            BeginHold(captor, GrappleKind.Pin, blendContact: true);
            _grabContactRemaining = 0f;
            _wallSite.BeginRise();
            SetWallStage(WallStage.Grab);

            // 壁拘束の間、二体目は「退避」ではなく横で待たせる（二人がかりの準備）。
            PickWallAssistCandidate();
        }

        /// <summary>壁拘束に加わる二体目を、拘束が始まった時点で選ぶ。</summary>
        void PickWallAssistCandidate()
        {
            WallAssistCandidate = null;
            float bestDistance = 5f;
            foreach (var enemy in FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None))
            {
                if (enemy == null || enemy == Captor || !enemy.isActiveAndEnabled || !enemy.IsAlive ||
                    enemy.IsRising || enemy.IsHolding || enemy.IsPinning || enemy.MoveSpeed <= 0f)
                    continue;

                float distance = Vector3.Distance(enemy.transform.position, transform.position);
                if (distance >= bestDistance)
                    continue;

                bestDistance = distance;
                WallAssistCandidate = enemy;
            }

            if (WallAssistCandidate != null)
            {
                WallAssistCandidate.BeginWallAssist(this);
                GameLog.Log($"[WallSplit] candidate {WallAssistCandidate.name} dist={bestDistance:F2}");
            }
        }

        void SetWallStage(WallStage stage)
        {
            _session.SetStage(stage);
            GameLog.Log($"[WallSequence] {stage}");
        }

        void TickWallTransition()
        {
            float seconds = WallPhase switch
            {
                WallStage.Grab => 0.22f,
                WallStage.Rise => 0.46f,
                WallStage.Throw => 0.30f,
                WallStage.Slam => 0.13f,
                WallStage.Lean => 0.57f,
                WallStage.Regrip => 0.28f,
                _ => 0.52f
            };
            _session.AdvanceStage(Time.deltaTime, seconds);
            float progress = Mathf.SmoothStep(0f, 1f, WallPhaseProgress);
            if (_wallSite == null)
            {
                Release();
                return;
            }
            if (WallPhase == WallStage.Rise)
                _wallSite.SetRise(progress);

            const float wallContactDepth = 0.10f;
            Vector3 contactSpot = _wallPinSpot + Vector3.forward * wallContactDepth;
            if (WallPhase == WallStage.Throw)
            {
                float flight = WallPhaseProgress * (0.40f + 0.60f * WallPhaseProgress);
                transform.position = Vector3.Lerp(_wallStartPosition, contactSpot, flight)
                    + Vector3.up * (0.10f * Mathf.Sin(Mathf.PI * WallPhaseProgress));
                transform.rotation = Quaternion.Slerp(_wallStartRotation, _wallPinRotation, progress);
            }
            else if (WallPhase >= WallStage.Slam)
            {
                Vector3 spot = Vector3.Lerp(contactSpot, _wallPinSpot, WallRestraintWeight);
                transform.SetPositionAndRotation(spot, _wallPinRotation);
            }

            Motor()?.SetLaneZOverride(transform.position.z);
            if (WallPhaseProgress < 1f)
                return;

            if (WallPhase == WallStage.Restrain)
            {
                SetWallStage(WallStage.Held);
                _session.HeldSeconds = 0f;
                Captor.TakeOverHold(CombatConstants.GrappleLimitSeconds + 4f);
                StartWallSequence();
                return;
            }

            SetWallStage((WallStage)((int)WallPhase + 1));
            if (WallPhase == WallStage.Slam)
            {
                WallPinWeight = 1f;
                // 壁へ叩きつける音は腹パンと同じ組み合わせ（打撃＋のけぞり＋腹の声）。
                // 専用の WallSlam は「軽い」と言われたのでやめた。
                PlaySfx(SfxId.Impact, 0.9f);
                PlaySfx(SfxId.Stagger, 0.7f);
                PlaySfx(SfxId.HurtBelly, 0.9f);
                FindFirstObjectByType<CombatFeel>()?.OnWallSlam();
            }
        }

        void BeginWallToss()
        {
            // 投げる向きは「彼女が元いた向き（進行方向）」＝アリーナの内側。
            // 壁の左右へ投げると、画面外やステージ端へ飛んでいく（実測）。
            // 拘束役ののけぞりもこの向きに合わせる（逆だと投げた側と反対へ倒れて見える）。
            float interior = (_wallStartRotation * Vector3.forward).x;
            // 壁へ正面から向かっていて X 成分が無いときは向きが決められないので、
            // +X をアリーナ内側とみなす（0 にすると投げ方向がゼロベクトルになる）。
            _wallTossSide = Mathf.Abs(interior) > 0.1f ? Mathf.Sign(interior) : 1f;
            SetWallStage(WallStage.TossWindup);
        }

        /// <summary>
        /// 壁押さえつけ中に二体目が横へ入り、二人がかり（Y 字）になる。
        /// 拘束役の EnemyBrain から、最初の膝蹴りのタイミングで呼ばれる。
        /// 二体目が見つからなければ false（呼び出し側は従来の膝蹴りを続ける）。
        /// </summary>
        internal bool TryBeginWallSplit(EnemyBrain captor)
        {
            if (!IsWallPin || WallPhase != WallStage.Held || _session.SplitStarted || IsHandingOff)
                return false;
            if (Captor != captor || Stats == null || Stats.IsDown || Stats.IsDead)
                return false;

            // 壁拘束の開始時に選んでおいた二体目を優先する（すでに横で待っている）。
            EnemyBrain best = WallAssistCandidate;
            if (best != null && (!best.isActiveAndEnabled || !best.IsAlive || best.IsHolding ||
                best.IsHandOffLocked || best.IsPinning))
                best = null;

            float bestDistance = best != null
                ? Vector3.Distance(best.transform.position, transform.position) : 5f;
            if (best == null)
            {
                foreach (var enemy in FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None))
                {
                    if (enemy == null || enemy == captor || !enemy.isActiveAndEnabled || !enemy.IsAlive ||
                        enemy.IsRising || enemy.IsHolding || enemy.IsHandOffLocked || enemy.IsPinning ||
                        enemy.MoveSpeed <= 0f)
                        continue;

                    float distance = Vector3.Distance(enemy.transform.position, transform.position);
                    if (distance >= bestDistance)
                        continue;

                    bestDistance = distance;
                    best = enemy;
                }
            }
            if (best == null)
                return false;

            WallAssistCandidate = best;
            _session.Assist = best;
            _session.SplitStarted = true;
            _session.SplitReady = best.WallAssistReady;
            _session.SplitHits = 0;
            _session.SplitAssistAge = 0f;
            _session.ResetSplitWeight();
            _session.SplitPunchAge = -1f;
            // 二人がかりの間は拘束時間を延ばす（膝 5 発ぶんの時間を使うため）。
            _session.LimitRemaining = Mathf.Max(_session.LimitRemaining, 4.6f);
            RequiredInputs = CombatConstants.WallSplitEscapeInputs;
            if (!best.IsWallAssisting)
                best.BeginWallAssist(this);
            StartCoroutine(WallSplitRoutine());
            GameLog.Log($"[WallSplit] begin captor={captor.name} assist={best.name} " +
                      $"dist={bestDistance:F2} ready={_session.SplitReady} need={RequiredInputs:F0}");
            return true;
        }

        System.Collections.IEnumerator WallSplitRoutine()
        {
            // アシストが横へ着くまで待つ（来なければ通常の膝蹴りへ戻す）。
            // 二人は彼女の正面側まで回り込むので、横に並ぶより距離がある（実測 1.7〜2.2m）。
            while (_session.SplitAssistAge < 2.4f && !_session.SplitReady &&
                   WallAssist != null && WallAssist.IsAlive && WallAssist.isActiveAndEnabled)
            {
                _session.SplitAssistAge += Time.deltaTime;
                if (WallAssist.WallAssistReady)
                {
                    _session.SplitReady = true;
                    break;
                }
                yield return null;
            }
            if (!_session.SplitReady)
            {
                // 膝はこの前に済んでいるので、膝へは戻らない。二人がかりを飛ばして次へ進む。
                GameLog.Log("[WallSplit] assist could not arrive - skip");
                EndWallSplit();
                yield break;
            }

            // 手首を Y 字へ広げ、二人の持ち方へ移る（姿勢側は WallSplitWeight を見る）。
            while (WallSplitWeight < 0.99f && IsWallSplitActive && Stats != null && !Stats.IsDown)
                yield return null;
            if (!IsWallSplitActive || Stats == null || Stats.IsDown)
            {
                EndWallSplit();
                yield break;
            }

            for (int i = 0; i < CombatConstants.WallSplitPunchCount; i++)
            {
                if (!IsWallSplitActive || WallAssist == null || Stats == null || Stats.IsDown)
                {
                    EndWallSplit();
                    yield break;
                }

                // アシストから始めて交互に打つ。6 発目（締めの膝入れ）は拘束役。
                // カメラは既定でアシスト側にあるので、アシストが膝を入れると、手前の体が
                // 彼女の腹を隠す。カメラから遠い側の拘束役が入れれば、腹へ入る膝が見える。
                EnemyBrain puncher = i % 2 == 0 ? WallAssist : Captor;
                _session.SplitPuncher = puncher;
                // 拘束役は右で彼女の左腕を持つので空き手は左。アシストは左で右腕を持つので空き手は右。
                _session.SplitPunchLeft = puncher == Captor;
                _session.SplitFinisher = CombatConstants.IsWallSplitFinisher(i);
                _session.SplitPunchAge = 0f;

                // 段階（序盤→中盤→締め）ごとに振りかぶり・間隔・重さが変わる。
                float windup = CombatConstants.WallSplitHitWindup(i);
                float interval = CombatConstants.WallSplitHitInterval(i);
                _session.SplitWindup = windup;

                float t = 0f;
                while (t < windup)
                {
                    if (!IsWallSplitActive)
                    {
                        EndWallSplit();
                        yield break;
                    }
                    t += Time.deltaTime;
                    _session.SplitPunchAge = t;
                    yield return null;
                }

                puncher?.DoWallSplitPunch(CombatConstants.WallSplitHitScale(i));

                // 壁に両腕を固定されたまま、腹で折れる被弾姿勢を重ねる。
                // 疲労（打った数）を渡すと、後半ほど戻りが遅く、折れが深くなる。
                float fatigue = (float)WallSplitHits / CombatConstants.WallSplitPunchCount;
                Animator()?.PlayHeldBellyPunch(interval, fatigue, CombatConstants.WallSplitHitImpact(i));
                if (CombatConstants.IsWallSplitFinisher(i))
                    FindFirstObjectByType<CombatFeel>()?.OnWallSplitFinisher();
                _session.SplitHits++;

                // 戻り（段階ごとの間隔）
                while (t < interval)
                {
                    if (!IsWallSplitActive)
                    {
                        EndWallSplit();
                        yield break;
                    }
                    t += Time.deltaTime;
                    _session.SplitPunchAge = t;
                    yield return null;
                }
                _session.SplitPunchAge = -1f;
            }

            // 6 発を打ち終えた。Y 字（二人で腕を持つ形）は次の猶予のあいだも続けるので、まだ解かない。
            // 解くのは 3 人がかりが終わったあと（WallSequenceRoutine）。
            _session.SplitPuncher = null;
            _session.SplitPunchAge = -1f;
            _session.SplitFinisher = false;
            _session.SplitPunchesDone = true;
        }

        // --- 壁拘束の段階進行 ----------------------------------------------------------
        // 壁拘束 → 膝蹴り（拘束役 1 人） → 猶予 → 二人がかりの腹パン → 猶予 → 解放（投げ捨て）。
        // 猶予では連打で抜けられる。抜けられなければ次の段階へ進む。

        bool _wallKneesDone;
        Coroutine _wallSeq;

        /// <summary>拘束役の膝蹴りが全部終わった。拘束役の EnemyBrain から呼ばれる。</summary>
        internal void OnWallKneesFinished() => _wallKneesDone = true;

        /// <summary>壁拘束の進行を続けられる状態か（拘束が解けた・倒れた・引き渡し中は止める）。</summary>
        bool WallSequenceAlive =>
            IsGrappled && IsWallPin && WallPhase == WallStage.Held && Captor != null &&
            Stats != null && !Stats.IsDown && !Stats.IsDead && !IsHandingOff;

        void StartWallSequence()
        {
            // 前回の進行が残っていれば先に片付ける（フラグが立ったままだと時間切れが止まる）。
            StopWallSequence();

            _wallKneesDone = false;
            _session.WallSequenceActive = true;
            _session.WallPauseActive = false;
            _wallSeq = StartCoroutine(WallSequenceRoutine());
        }

        void StopWallSequence()
        {
            if (_wallSeq != null)
            {
                StopCoroutine(_wallSeq);
                _wallSeq = null;
            }

            FinishWallSequence();
        }

        /// <summary>
        /// 段階進行の後始末。正常終了・途中で抜けた（ダウン／死亡／引き渡し／相方が来ない等）の
        /// どちらでも通るので、何度呼んでも安全。
        /// <c>WallSequenceActive</c> を落とさないと、時間切れの解放（<see cref="PlayerGrapple"/> の
        /// Update にある判定）が止まったままになり、壁拘束が解放されずに残る。
        /// 3 人がかりの途中なら、3 人目を離して彼女の腕も戻す。
        /// </summary>
        void FinishWallSequence()
        {
            _wallSeq = null;
            _session.WallSequenceActive = false;
            _session.WallPauseActive = false;

            if (_session.GangActive || _session.Third != null)
                EndWallGang();
        }

        IEnumerator WallSequenceRoutine()
        {
            // 途中で抜けたときも後始末を必ず通す（yield break が何本あっても漏れないように try/finally）。
            try
            {
                yield return WallSequenceSteps();
            }
            finally
            {
                FinishWallSequence();
            }
        }

        /// <summary>段階進行の本体。終了時の後始末は <see cref="WallSequenceRoutine"/> が行う。</summary>
        IEnumerator WallSequenceSteps()
        {
            // 1〜2. 猶予（連打で逃げる）→ 膝蹴り。膝は拘束役の既存の処理が入れる。終わるまで待つ。
            float timeout = CombatConstants.FrontGrappleEscapeSeconds +
                            CombatConstants.PinKneeCount * CombatConstants.PinKneeInterval + 4f;
            float waited = 0f;
            while (WallSequenceAlive && !_wallKneesDone && waited < timeout)
            {
                waited += Time.deltaTime;
                yield return null;
            }
            if (!WallSequenceAlive)
                yield break;

            // 3. 拘束が続く。
            yield return WallPause(CombatConstants.WallPauseInputsAfterKnees);
            if (!WallSequenceAlive)
                yield break;

            // 4〜6. 二人がかりの腹パン → 拘束が続く → 3 人目が参戦。二体目が来なければ飛ばす。
            if (TryBeginWallSplit(Captor))
            {
                while (_session.SplitStarted && !_session.SplitPunchesDone && WallSequenceAlive)
                    yield return null;
                if (!WallSequenceAlive)
                    yield break;

                // 二体目が来られなかったときは SplitStarted が落ちている（次へ進まず解放する）。
                if (_session.SplitStarted)
                {
                    // 猶予のあいだに 3 人目を選び、待ち位置へ向かわせる（到着が間に合うように）。
                    PrepareWallThird();
                    yield return WallPause(CombatConstants.WallPauseInputsAfterSplit);
                    if (!WallSequenceAlive)
                        yield break;

                    yield return WallGang();
                    if (!WallSequenceAlive)
                        yield break;
                }

                EndWallSplit();
            }

            // 7. 解放（投げ捨て）。
            BeginWallToss();
        }

        enum WallStrikeKind
        {
            Belly,
            Knee,
            Face,
            Kick
        }

        /// <summary>3 人目に選ぶ敵（拘束役・アシスト以外で、いちばん近い敵）。</summary>
        EnemyBrain PickWallThird()
        {
            EnemyBrain best = null;
            // 画面内の敵なら歩いて来られる距離（歩きで約 4 秒）。
            float bestDistance = 10f;
            foreach (var enemy in FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None))
            {
                if (enemy == null || enemy == Captor || enemy == _session.Assist)
                    continue;

                if (!enemy.isActiveAndEnabled || !enemy.IsAlive || enemy.IsRising ||
                    enemy.IsHolding || enemy.IsPinning || enemy.IsWallAssisting ||
                    enemy.IsHandOffLocked || enemy.MoveSpeed <= 0f)
                {
                    GameLog.Log($"[WallGang] skip {enemy.name}: enabled={enemy.isActiveAndEnabled} alive={enemy.IsAlive} " +
                              $"rising={enemy.IsRising} holding={enemy.IsHolding} pinning={enemy.IsPinning} " +
                              $"assisting={enemy.IsWallAssisting} handoff={enemy.IsHandOffLocked} speed={enemy.MoveSpeed:F1}");
                    continue;
                }

                float distance = Vector3.Distance(enemy.transform.position, transform.position);
                if (distance >= bestDistance)
                {
                    GameLog.Log($"[WallGang] skip {enemy.name}: too far {distance:F1}m");
                    continue;
                }

                bestDistance = distance;
                best = enemy;
            }
            return best;
        }

        /// <summary>3 人目を選び、待ち位置へ向かわせる。居なければ何もしない（3 人がかりは飛ばされる）。</summary>
        void PrepareWallThird()
        {
            EnemyBrain third = PickWallThird();
            if (third == null)
                return;

            _session.Third = third;
            third.BeginWallAssist(this);
            GameLog.Log($"[WallGang] third {third.name} heading to wait spot");
        }

        /// <summary>
        /// 3 人がかり（最後の段階）。画面左のアシストは片手首を持ち続け、拘束役は手を離す
        /// （彼女のその腕はだらんと下がる）。拘束役と 3 人目が入れ替わり立ち代わりで殴る蹴る。
        /// アシストは合間に、空いた手で腹パンを入れる。3 人目がいなければ飛ばす。
        /// </summary>
        IEnumerator WallGang()
        {
            EnemyBrain third = _session.Third;
            if (third == null || !third.IsAlive || !third.isActiveAndEnabled)
            {
                GameLog.Log("[WallGang] no third enemy - skip");
                yield break;
            }

            _session.GangTotal = CombatConstants.WallGangStrikes;
            _session.GangHits = 0;
            _session.GangStriker = third;
            _session.GangActive = true;

            // 彼女はグロッキーで、抵抗できない（連打は受け付けない）。ここへ来る前の猶予が最後の脱出の機会。
            // 制限時間は止めない。0 になっても、進行中は WallSequenceActive が時間切れの解放を止める。
            // ここで 999 のような値を入れると、進行が途中で抜けたときに時間切れが二度と来なくなる。
            _session.SuppressRemaining = 999f;
            GameLog.Log($"[WallGang] begin third={third.name}");

            // 3 人目が持ち場へ着くまで待つ（来なければ飛ばす）。
            float arrive = 0f;
            while (WallSequenceAlive && !third.WallAssistReady && arrive < 4f)
            {
                arrive += Time.deltaTime;
                yield return null;
            }
            if (!WallSequenceAlive || !third.WallAssistReady)
            {
                EndWallGang();
                yield break;
            }

            // アシストと拘束役は腹パン・顔パン・膝を混ぜる。遠くに立つ 3 人目は蹴りだけ（拳は届かない）。
            // 1 巡 = アシスト・拘束役・3 人目。巡によってはアシストと拘束役が「同時に」入る。
            // 3 人全員が 1 人ずつ順番に打つだけだと、打っていない 2 人が棒立ちで順番待ちに見える。
            var assistKinds = new[]
            {
                WallStrikeKind.Belly, WallStrikeKind.Face, WallStrikeKind.Belly,
                WallStrikeKind.Knee, WallStrikeKind.Face
            };
            var captorKinds = new[]
            {
                WallStrikeKind.Face, WallStrikeKind.Belly, WallStrikeKind.Face,
                WallStrikeKind.Belly, WallStrikeKind.Knee
            };
            var thirdKinds = new[]
            {
                WallStrikeKind.Kick, WallStrikeKind.Kick, WallStrikeKind.Kick,
                WallStrikeKind.Kick, WallStrikeKind.Kick
            };

            // 2 巡目と 3 巡目で、アシストと拘束役が腹と顔へ同時に入る。
            var simultaneous = new[] { false, true, true, false, false };

            EnemyBrain assist = WallAssist;
            EnemyBrain holder = Captor;
            int cycles = Mathf.Min(simultaneous.Length,
                Mathf.Min(assistKinds.Length, Mathf.Min(captorKinds.Length, thirdKinds.Length)));

            for (int cycle = 0; cycle < cycles && WallSequenceAlive; cycle++)
            {
                int baseIndex = cycle * 3;

                if (simultaneous[cycle] && assist != null && holder != null)
                {
                    // 2 人同時。片方をコルーチンで走らせたまま、もう片方を待つ。
                    _session.GangStriker = assist;
                    Coroutine other = StartCoroutine(
                        WallGangStrike(holder, captorKinds[cycle], false, baseIndex + 1));
                    yield return WallGangStrike(assist, assistKinds[cycle], false, baseIndex);

                    // 枠が空くまで待ってから次へ（打撃が重なり続けないように）。
                    while (other != null && WallSequenceAlive && _session.StrikeOf(holder) != null)
                        yield return null;
                }
                else
                {
                    _session.GangStriker = assist;
                    yield return WallGangStrike(assist, assistKinds[cycle], false, baseIndex);
                    if (!WallSequenceAlive)
                        break;

                    _session.GangStriker = holder;
                    yield return WallGangStrike(holder, captorKinds[cycle], false, baseIndex + 1);
                }

                if (!WallSequenceAlive)
                    break;

                _session.GangStriker = third;
                yield return WallGangStrike(third, thirdKinds[cycle], cycle == cycles - 1, baseIndex + 2);
            }

            EndWallGang();
        }

        /// <summary>3 人がかりの 1 発。振りかぶり → 当たる → 間隔。</summary>
        IEnumerator WallGangStrike(EnemyBrain puncher, WallStrikeKind kind, bool finisher, int index)
        {
            if (puncher == null)
                yield break;

            // 膝と蹴りは同じ枠（溜め → 突き上げ）で動く。
            bool legStrike = kind == WallStrikeKind.Knee || kind == WallStrikeKind.Kick;

            // 段階（序盤→中盤→締め）で振りかぶりと間隔が伸びる。
            // 序盤は詰めて畳みかけ、締めは溜めてから重く入れる。
            float windup = legStrike
                ? CombatConstants.WallGangLegWindup(index)
                : CombatConstants.WallGangWindup(index);
            float interval = legStrike
                ? CombatConstants.WallGangLegInterval(index)
                : CombatConstants.WallGangInterval(index);
            float scale = kind switch
            {
                WallStrikeKind.Knee => finisher ? CombatConstants.WallGangFinisherScale : CombatConstants.WallGangKneeScale,
                WallStrikeKind.Kick => finisher ? CombatConstants.WallGangFinisherScale : CombatConstants.WallGangKickScale,
                WallStrikeKind.Face => CombatConstants.WallGangFaceScale,
                _ => CombatConstants.WallGangBellyScale
            };
            // 同時に打てるのは 2 人まで。空きが無ければ少しだけ待つ（他の打撃の枠は奪わない）。
            SplitStrike strike = null;
            float slotWait = 0f;
            while (strike == null && WallSequenceAlive && slotWait < StrikeSlotWaitSeconds)
            {
                strike = _session.ClaimStrike(puncher);
                if (strike == null)
                {
                    slotWait += Time.deltaTime;
                    yield return null;
                }
            }
            if (strike == null)
            {
                GameLog.Log($"[WallGang] no free strike slot - skip {puncher.name}");
                yield break;
            }

            // 膝と蹴りは Finisher、蹴りは Kick、顔パンは Face を立てる（姿勢側がこの枠を見る）。
            strike.Left = puncher == Captor;
            strike.Finisher = legStrike;
            strike.Kick = kind == WallStrikeKind.Kick;
            strike.Face = kind == WallStrikeKind.Face;
            strike.Windup = windup;

            // 途中で抜けても枠を残さない（残すと次の打撃が「打ちっぱなし」になる）。
            try
            {
                float t = 0f;
                while (t < windup)
                {
                    if (!WallSequenceAlive)
                        yield break;
                    t += Time.deltaTime;
                    strike.Age = t;
                    yield return null;
                }

                puncher.DoWallSplitPunch(scale, kind == WallStrikeKind.Face ? PunchZone.Face : PunchZone.Body);

                // 1 発ごとに画面が応える（揺れ・ヒットストップ・フラッシュ）。当たった感じを出す。
                // 段階が進むほど大きく応える（序盤は軽く、締めへ向けて重く）。
                float stageImpact = CombatConstants.WallGangImpactScale(index);
                if (!finisher)
                    FindFirstObjectByType<CombatFeel>()?.OnWallGangHit(
                        (kind == WallStrikeKind.Belly ? 0.55f : kind == WallStrikeKind.Face ? 0.75f : 1.0f)
                        * stageImpact);

                var anim = Animator();
                float progress = _session.GangTotal > 0 ? (float)_session.GangHits / _session.GangTotal : 0f;
                if (kind == WallStrikeKind.Face)
                {
                    Vector3 push = transform.position - puncher.transform.position;
                    push.y = 0f;
                    anim?.PlayHeldFacePunch(0.45f, push, 1f);
                }
                else
                {
                    // 重さ: 腹パンでも深く折れる（実測: 0.9 だと「当たっている感」が無かった）。
                    // 段階倍率を掛けるが、1.0 を下回らせない（浅いと当たって見えない）。
                    float impact = (legStrike ? (finisher ? 1.7f : 1.6f) : 1.15f) * stageImpact;
                    impact = Mathf.Max(1.0f, impact);
                    anim?.PlayHeldBellyPunch(interval, Mathf.Clamp01(0.5f + 0.5f * progress), impact);
                    if (legStrike && finisher)
                        FindFirstObjectByType<CombatFeel>()?.OnWallSplitFinisher();
                }
                _session.GangHits++;

                while (t < interval)
                {
                    if (!WallSequenceAlive)
                        yield break;
                    t += Time.deltaTime;
                    strike.Age = t;
                    yield return null;
                }
            }
            finally
            {
                strike.Reset();
            }
        }

        /// <summary>3 人がかりを終える。3 人目を持ち場から離し、拘束役の手を戻す。</summary>
        void EndWallGang()
        {
            var third = _session.Third;
            _session.ClearStrikes();
            _session.GangActive = false;
            _session.GangStriker = null;
            _session.SuppressRemaining = 0f;
            if (third != null)
                third.ReleaseWallAssist();
            _session.Third = null;
        }

        /// <summary>段階の間の猶予。脱出に必要な連打数を入れ直し、残り秒を数える。</summary>
        IEnumerator WallPause(int inputs)
        {
            RequiredInputs = inputs;
            _resistance = new GrappleResistance(Kind, RequiredInputs);
            _session.LimitRemaining = CombatConstants.WallPauseSeconds;
            _session.WallPauseActive = true;
            GameLog.Log($"[WallSequence] pause {CombatConstants.WallPauseSeconds:F1}s need={inputs}");

            while (WallSequenceAlive && _session.LimitRemaining > 0f)
                yield return null;

            _session.WallPauseActive = false;
        }

        /// <summary>二人がかりを終える。アシストには退避を指示する。</summary>
        void EndWallSplit()
        {
            var assist = WallAssist;
            var candidate = WallAssistCandidate;
            var third = _session.Third;
            _session.ClearSplit();
            if (third != null)
                third.ReleaseWallAssist();
            if (assist != null)
                assist.ReleaseWallAssist();
            if (candidate != null && candidate != assist)
                candidate.ReleaseWallAssist();

            // もがき必要数を通常へ戻す（腕の負傷の扱いは BeginHold と同じ）。
            if (IsWallPin && Stats != null)
                RequiredInputs = ResistanceMath.RequiredInputs(Kind, 1f,
                    Stats.State.Body[BodyPart.Arms]);
        }

        void TickWallSplitWeight(float dt)
        {
            _session.TickSplitWeight(dt);
            _session.TickGangWeight(dt);
        }

        void TickWallToss()
        {
            const float seconds = 0.26f;
            _session.AdvanceStage(Time.deltaTime, seconds);

            // 壁から 0.10m 手前まで引き上げる。ここから放物線で投げ捨てる。
            Vector3 launch = _wallPinSpot + (_wallPinRotation * Vector3.forward) * 0.10f;
            transform.position = Vector3.Lerp(_wallPinSpot, launch, WallTossLower);
            Motor()?.SetLaneZOverride(transform.position.z);

            if (WallPhaseProgress < 1f)
                return;

            // 引き上げてから投げ捨てる。空中の弧・着地・倒れはダウンの仕組み
            // （PlayerStats.ForceDown → PlayerMotor.LaunchBack）に任せる。
            // ここで台本どおりに運ぶと、レーンへ戻ってから急に倒れるように見えた（実測）。
            var attacker = Captor != null ? Captor.transform : null;
            Vector3 throwDirection = Vector3.right * _wallTossSide;
            PlaySfx(SfxId.Swing, 0.95f);

            // 飛んでいる間に z をレーンへ戻す。壁の真上を飛ぶと壁の見た目を
            // すり抜けるので、離脱は速め（着地より先にレーンへ出る）。
            _wallThrowExitSeconds = ThrowExitSeconds;

            // 腹ダメージの消化はここでは行わない。直後の ForceDown が投げ捨てのダウンを作るので、
            // 両方走らせると 1 フレームに 2 回ダウンして `[Down] held-belly` と `[Down] wall-toss` が並び、
            // DownIsForward も 2 回上書きされる。
            Fail(resolveBellyDown: false);

            // うつ伏せ着地でも投げ飛ばす（壁の投げ捨ては「崩れ落ち」ではない）。
            bool forward = ForcedWallTossForward ?? (UnityEngine.Random.value < 0.5f);

            // 拘束が終わるので、溜まった腹ダメージはここで消化する（ダウン自体は投げ捨てが起こす）。
            // HeldBellyPunchDirector のトドメも同じ順序（Clear → ForceDown）で処理している。
            Stats?.ClearHeldBellyDamage();
            Stats?.ForceDown(CombatConstants.DownDuration, "wall-toss", attacker,
                1.7f, 0.95f, forward, throwDirection, launchWhenForward: true);
            StartCoroutine(PlayBodyDropAfter(ThrowFlightSeconds));
        }

        /// <summary>投げ捨ての滞空時間（秒）。着地音の待ちに使う。</summary>
        const float ThrowFlightSeconds = 0.62f;

        /// <summary>3 人がかりで打撃枠（2 枠）が空くのを待つ最大秒。</summary>
        const float StrikeSlotWaitSeconds = 0.35f;

        /// <summary>投げ捨てで壁からレーンへ抜けるまでの秒数（滞空より短くする）。</summary>
        const float ThrowExitSeconds = 0.34f;

        IEnumerator PlayBodyDropAfter(float seconds)
        {
            yield return new WaitForSeconds(seconds);

            // 待つ間に別の敵へ掴み直されたら、拘束中の効果音になるので鳴らさない。
            // ダウンが解けている（着地ではない）ときも鳴らさない。
            if (IsGrappled || Stats == null || !Stats.IsDown)
                yield break;

            PlaySfx(SfxId.BodyDrop, 0.8f);
        }

    }
}
