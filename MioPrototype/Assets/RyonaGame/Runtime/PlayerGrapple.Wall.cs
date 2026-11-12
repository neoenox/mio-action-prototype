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
                Debug.Log($"[WallSplit] candidate {WallAssistCandidate.name} dist={bestDistance:F2}");
            }
        }

        void SetWallStage(WallStage stage)
        {
            _session.SetStage(stage);
            Debug.Log($"[WallSequence] {stage}");
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
            Debug.Log($"[WallSplit] begin captor={captor.name} assist={best.name} " +
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
                Debug.Log("[WallSplit] assist could not arrive - fallback to knees");
                var captor = Captor;
                EndWallSplit();
                captor?.ResumePinKnees();
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

                EnemyBrain puncher = i % 2 == 0 ? Captor : WallAssist;
                _session.SplitPuncher = puncher;
                // 拘束役は右で彼女の左腕を持つので空き手は左。アシストは左で右腕を持つので空き手は右。
                _session.SplitPunchLeft = puncher == Captor;
                _session.SplitPunchAge = 0f;

                // 振りかぶり（0.22 秒）
                float t = 0f;
                while (t < 0.22f)
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

                puncher?.DoWallSplitPunch();

                // 壁に両腕を固定されたまま、腹で折れる被弾姿勢を重ねる。
                // 疲労（打った数）を渡すと、後半ほど戻りが遅く、折れが深くなる。
                float fatigue = (float)WallSplitHits / CombatConstants.WallSplitPunchCount;
                Animator()?.PlayHeldBellyPunch(CombatConstants.WallSplitPunchInterval, fatigue);
                _session.SplitHits++;

                // 戻り（合計 0.55 秒間隔）
                while (t < CombatConstants.WallSplitPunchInterval)
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

            // 6 発を打ち終えたらアシストを離し、既存の投げ捨てへ。
            EndWallSplit();
            if (IsWallPin && WallPhase == WallStage.Held && Captor != null &&
                Stats != null && !Stats.IsDown)
                BeginWallToss();
        }

        /// <summary>二人がかりを終える。アシストには退避を指示する。</summary>
        void EndWallSplit()
        {
            var assist = WallAssist;
            var candidate = WallAssistCandidate;
            _session.ClearSplit();
            if (assist != null)
                assist.ReleaseWallAssist();
            if (candidate != null && candidate != assist)
                candidate.ReleaseWallAssist();

            // もがき必要数を通常へ戻す（腕の負傷の扱いは BeginHold と同じ）。
            if (IsWallPin && Stats != null)
                RequiredInputs = ResistanceMath.RequiredInputs(Kind, 1f,
                    Stats.State.Body[BodyPart.Arms]);
        }

        void TickWallSplitWeight(float dt) => _session.TickSplitWeight(dt);

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
            Fail();

            // うつ伏せ着地でも投げ飛ばす（壁の投げ捨ては「崩れ落ち」ではない）。
            bool forward = ForcedWallTossForward ?? (UnityEngine.Random.value < 0.5f);
            Stats?.ForceDown(CombatConstants.DownDuration, "wall-toss", attacker,
                1.7f, 0.95f, forward, throwDirection, launchWhenForward: true);
            StartCoroutine(PlayBodyDropAfter(ThrowFlightSeconds));
        }

        /// <summary>投げ捨ての滞空時間（秒）。着地音の待ちに使う。</summary>
        const float ThrowFlightSeconds = 0.62f;

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
