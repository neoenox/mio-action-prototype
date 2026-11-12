using System;
using UnityEngine;

namespace Ryona.Game
{
    /// <summary>
    /// プレイヤーの攻撃。発生 / 判定 / 硬直を秒で管理する（仕様書 §4.5）。
    /// ダメージ計算は DamageResolver に丸投げし、ここは「いつ・誰に」だけを扱う。
    /// </summary>
    [AddComponentMenu("Ryona/Player Combat")]
    public sealed class PlayerCombat : MonoBehaviour
    {
        [Header("References")]
        public PlayerStats Stats;
        public Transform AttackOrigin;

        [Header("Hit Area")]
        /// <summary>拳の当たり半径（拳 + 少しの猶予）。</summary>
        /// <remarks>
        /// 以前は「胸の 35cm 前を中心に半径 0.55 の球を 0.85m 先へ」置いていたため、
        /// 拳の到達（0.59m）より 1m 以上先まで当たっていた。判定は拳の位置で取る。
        /// </remarks>
        [System.NonSerialized] public float FistRadius = 0.12f;

        /// <summary>相手の体の半径（見た目）。移動用カプセルの 0.40 は太すぎるので判定には使わない。</summary>
        [System.NonSerialized] public float BodyRadius = 0.24f;

        /// <summary>拳が届く間合い。ここまで踏み込んで殴る。</summary>
        [System.NonSerialized] public float HitGap = 0.90f;

        [Header("Auto Aim")]
        /// <summary>この距離までの敵には、攻撃の出始めに向きを合わせる。</summary>
        public float AutoAimRange = 2.2f;
        /// <summary>振り向く最大角度。真後ろの敵までは回らない。</summary>
        public float AutoAimMaxDegrees = 70f;

        [Header("Recovery")]
        /// <summary>
        /// 硬直が終わっても、踏み込みで出た足が体の下へ戻るまでは次の攻撃を受け付けない。
        /// 戻り切る前に次を出すと、足の戻しと新しい踏み込みが重なって足が床を滑る。
        /// </summary>
        public float SettleEpsilon = 0.001f;

        /// <summary>
        /// 硬直中に押された攻撃を覚えておく時間（先行入力）。
        /// 押した瞬間に撃てなくても、空いた最初のフレームで撃つ。0 にすると完全に無視。
        /// </summary>
        public float InputBufferSeconds = 0.15f;

        [Tooltip("3段コンボの受付時間。この秒数内に弱を押すと次の段へ進む")]
        public float ComboWindowSeconds = 0.45f;

        /// <summary>拳の軌跡を 1 フレームごとにログる（`-hitlog`）。</summary>
        [System.NonSerialized] public bool TraceFist;

        CharacterAnimator _anim;
        PlayerMotor _motor;

        AttackData _attack;
        float _elapsed;
        bool _hitDone;
        PlayerGrapple _grapple;

        // 硬直中に押された攻撃（先行入力）。_bufferedAge は押してからの経過秒。
        AttackData _buffered;
        float _bufferedAge;
        bool _settleLogged;

        // 何段目か（0=右, 1=左, 2=強）と、そのコンボを受け付ける期限。
        int _bufferedStep;
        int _comboStep;
        float _comboUntil;

        public AttackData CurrentAttack => _attack;
        public bool IsAttacking => _attack != null;

        /// <summary>硬直中（攻撃の発生〜硬直、または踏み込みの足が戻るまで）。</summary>
        public bool IsRecovering => _attack != null || FeetNotSettled;

        /// <summary>攻撃開始。SE・エフェクト・モーションはこれを見て再生する。</summary>
        public event Action<AttackData> AttackStarted;

        /// <summary>ヒット時に発火。ヒットストップとカメラワークはここに繋ぐ。</summary>
        public event Action<HitResult, IDamageable> HitLanded;

        void Awake()
        {
            if (Stats == null)
                Stats = GetComponent<PlayerStats>();

            _motor = GetComponent<PlayerMotor>();

            TraceFist = LaunchOptions.Has("-hitlog");

            // AttackOrigin は「攻撃の出る位置」を取るためだけの目印。
            // 球が赤く浮いているように見えるので、見た目は必ず消す。
            if (AttackOrigin != null)
            {
                var originRenderer = AttackOrigin.GetComponent<Renderer>();
                if (originRenderer != null)
                    originRenderer.enabled = false;
            }
        }

        void Update()
        {
            if (Stats == null)
                return;

            if (Stats.IsDead)
            {
                _buffered = null;
                return;
            }

            // 倒れている間は何もできない（起き上がりは状態機械が戻す）。
            // スタンだけを見ていると、短い怯みのダウン中に入力が通ってしまう。
            if (Stats.State != null && Stats.State.IsDown)
            {
                CancelAttack();
                return;
            }

            // 掴まれている間は攻撃できない。入力はレバガチャに回る（PlayerGrapple 側）
            if (_grapple == null)
                _grapple = GetComponent<PlayerGrapple>();
            if (_grapple != null && _grapple.IsGrappled)
            {
                // 殴っている最中に掴まれたら、拳を突き出したまま固まるので打ち切る。
                CancelAttack();
                return;
            }

            // 硬直中は次を受け付けない。押された分は先行入力として保持だけしておく。
            if (_attack != null)
            {
                TickAttack();

                if (_attack != null)
                    BufferAttackInput();
            }

            if (_attack != null)
                return;

            // 攻撃は終わったが踏み込みの足が戻り切っていない間も、次は撃たせない。
            if (FeetNotSettled)
            {

                if (!_settleLogged)
                {
                    _settleLogged = true;
                    Debug.Log($"[Combat] settle travel={_motor.LungeTravel:F3}");
                }

                BufferAttackInput();
                return;
            }

            _settleLogged = false;

            if (Stats.IsStunned)
            {
                _buffered = null;
                return;
            }

            // 硬直中に押されていた分を、空いたこのフレームで撃つ。
            if (TryConsumeBuffered())
                return;

            if (InputReader.LightPressed())
            {
                AttackData light = ResolveLight(out int step);
                Begin(light, step);
            }
            else if (InputReader.HeavyPressed())
                Begin(AttackLibrary.Heavy());
            else if (InputReader.BreakPressed())
                Begin(AttackLibrary.BreakAttack());
        }

        /// <summary>
        /// 弱の段数を進めて、撃つ攻撃と段数を返す。
        /// 2 段目は左腕、3 段目は強（フィニッシュ）。時間が空いたら 1 段目に戻る。
        /// </summary>
        AttackData ResolveLight(out int step)
        {
            step = Time.time <= _comboUntil ? (_comboStep + 1) % 3 : 0;

            // 検証用: 常に左で打つ（-leftpunch）
            if (InputReader.ForceLeftPunch)
                step = 1;

            return step == 2 ? AttackLibrary.Heavy() : AttackLibrary.Light();
        }

        /// <summary>踏み込みで出た足がまだ体の下へ戻り切っていない。</summary>
        bool FeetNotSettled => _motor != null && _motor.LungeTravel > SettleEpsilon;

        /// <summary>
        /// 攻撃中／足の戻り待ちに押された攻撃を 1 つだけ覚えておく。
        /// 古くなったものは捨てる（押しっぱなしで勝手に撃ち続けない）。
        /// </summary>
        void BufferAttackInput()
        {
            if (_buffered != null)
            {
                _bufferedAge += Time.deltaTime;
                if (_bufferedAge > InputBufferSeconds)
                {
                    // 段数も一緒に捨てる。残すと、次に強 / 崩しを出したときに
                    // 古い段数（＝左腕）が当たり判定と姿勢へ漏れる。
                    _buffered = null;
                    _bufferedStep = 0;
                }
            }

            AttackData pressed = null;
            int step = 0;

            if (InputReader.LightPressed())
            {
                // 連続で弱を押すと 右 → 左 → 強 の3段になる。
                pressed = ResolveLight(out step);
            }
            else if (InputReader.HeavyPressed())
            {
                pressed = AttackLibrary.Heavy();
            }
            else if (InputReader.BreakPressed())
            {
                pressed = AttackLibrary.BreakAttack();
            }

            if (pressed == null)
                return;

            _buffered = pressed;
            _bufferedStep = step;
            _bufferedAge = 0f;

            Log($"[Combat] buffer {pressed.id} step={step} " +
                $"wait={(IsAttacking ? _attack.TotalSeconds - _elapsed : 0f):F2}");
        }

        bool TryConsumeBuffered()
        {
            if (_buffered == null)
                return false;

            AttackData attack = _buffered;
            _buffered = null;

            int step = _bufferedStep;
            _bufferedStep = 0;

            Log($"[Combat] buffer fire {attack.id} step={step}");
            Begin(attack, step);
            return true;
        }

        /// <param name="step">
        /// 弱の段数（0=右, 1=左, 2=強）。強 / 崩しは必ず 0。
        /// 先行入力の段数をここで受け取らないと、時間切れした段数が残って
        /// 強攻撃の当たり判定が左拳の位置で取られる（実測）。
        /// </param>
        internal void Begin(AttackData attack, int step = 0)
        {
            _attack = attack;
            _elapsed = 0f;
            _hitDone = false;

            // 何段目かは姿勢の左右反転に使う。攻撃の種類が違えば必ず 1 段目扱い。
            _comboStep = Mathf.Clamp(step, 0, 2);
            _bufferedStep = 0;
            _comboUntil = Time.time + attack.TotalSeconds + ComboWindowSeconds;

            if (_anim == null)
                _anim = GetComponentInChildren<CharacterAnimator>();
            if (_anim != null)
                _anim.ComboStep = _comboStep;


            if (_motor != null && _motor.BeltFacing)
            {
                _motor.SnapToLaneFacing();
                if (_anim != null) _anim.CancelAim();
            }
            AimAtNearestEnemy();

            // 踏み込み。拳が届く間合い（HitGap）までだけ詰める。
            // 距離を見ずに固定量だと、近いときに拳が体へ入り、遠いときに届かない。
            if (_motor != null)
            {
                float lunge = attack.lungeDistance;
                if (AimDistance > 0.01f)
                    lunge = Mathf.Clamp(AimDistance - HitGap, 0f, attack.lungeDistance);

                _motor.Lunge(lunge, attack.startupSeconds, attack.id);
            }

            Log($"[Combat] attack {attack.id} step={_comboStep}");

            AttackStarted?.Invoke(attack);
        }

        /// <summary>
        /// 攻撃の出始めに相手へ向く（ソフトオートエイム）。
        /// 止まったまま殴ると「前を向いたまま横の敵を殴って空振り」になるため、
        /// 一番近い相手の方向へ体を向ける（回る角度には上限を置く）。
        /// </summary>
        void AimAtNearestEnemy()
        {
            if (_anim == null)
                _anim = GetComponentInChildren<CharacterAnimator>();
            if (_anim == null)
                return;

            EnemyBrain best = null;
            float bestDist = AutoAimRange;

            foreach (var enemy in FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None))
            {
                if (enemy == null || !enemy.IsAlive)
                    continue;

                Vector3 d = enemy.transform.position - transform.position;
                d.y = 0f;

                // 一列の戦闘では、背後の敵へ半端に振り向かない。
                if (_motor != null && _motor.BeltFacing && d.x * _motor.LaneFacingSign <= 0f)
                    continue;

                float dist = d.magnitude;
                if (dist >= bestDist)
                    continue;

                best = enemy;
                bestDist = dist;
            }

            if (best != null)
            {
                if (_motor == null || !_motor.BeltFacing)
                    _anim.AimAt(best.transform.position, AutoAimMaxDegrees);
                AimDistance = bestDist;
            }
            else
            {
                AimDistance = 0f;
            }
        }

        /// <summary>
        /// 狙っている相手までの距離（0 = 誰もいない）。
        /// 拳が相手の体にめり込まないよう、腕の伸びを止めるのに使う。
        /// </summary>
        public float AimDistance { get; private set; }

        /// <summary>
        /// 掴まれた等で攻撃を続けられないとき、構えを打ち切りにする。
        /// 放置すると「拳を突き出したまま固まる」画になる。
        /// </summary>
        public void CancelAttack()
        {
            _attack = null;
            _elapsed = 0f;
            _hitDone = false;
            _buffered = null;
            _bufferedStep = 0;
            _bufferedAge = 0f;
        }

        void TickAttack()
        {

            // 殴られてひるんだら振りは中断する。中断しないと、姿勢は「ひるみ」のまま
            // 判定だけが発生の時刻に来て、拳が伸びていないのに当たる（＝見た目とズレる）。
            if (Stats.IsStunned || Stats.IsDead)
            {
                CancelAttack();
                return;
            }

            _elapsed += Time.deltaTime;

            if (TraceFist)
                LogFist();

            if (!_hitDone && _elapsed >= _attack.startupSeconds)
            {
                _hitDone = true;
                ResolveHit(_attack);
            }

            if (_elapsed >= _attack.TotalSeconds)
            {
                _attack = null;
                _elapsed = 0f;
                _hitDone = false;
            }
        }

        void ResolveHit(AttackData attack)
        {
            Vector3 fist = FistPoint();

            EnemyBrain hitEnemy = null;

            foreach (var enemy in FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None))
            {
                if (enemy == null || !enemy.IsAlive)
                    continue;

                if (!InFistReach(fist, enemy))
                    continue;

                var target = enemy.GetComponent<IDamageable>();
                if (target == null || ReferenceEquals(target, this))
                    continue;

                var result = target.TakeHit(Stats.State, attack);

                Log($"[Combat] hit {attack.id} dmg={result.Damage:F0} " +
                    $"stagger={result.Stagger}");

                hitEnemy = enemy;
                HitLanded?.Invoke(result, target);
            }

            if (TraceFist)
                LogGeometry(attack, fist, hitEnemy);
        }

        /// <summary>
        /// 拳が相手の体に届いているか。相手を縦のカプセル（半径 <see cref="BodyRadius"/>）とみなし、
        /// 拳との水平距離で判定する。高さは膝より上・頭より下に入っていること。
        /// </summary>
        bool InFistReach(Vector3 fist, EnemyBrain enemy)
        {
            Vector3 root = enemy.transform.position;

            if (fist.y < root.y + 0.25f || fist.y > root.y + 1.60f)
                return false;

            Vector3 d = fist - root;
            d.y = 0f;

            return d.magnitude <= FistRadius + BodyRadius;
        }

        /// <summary>
        /// 拳の実際の位置。当たり判定を「見えている拳」に寄せるための基準。
        /// VRM の読み込み前（仮カプセルの間）は AttackOrigin で代用する。
        /// </summary>
        Vector3 FistPoint()
        {
            if (_anim == null)
                _anim = GetComponentInChildren<CharacterAnimator>();

            var rig = _anim != null ? _anim.Rig : null;
            if (rig != null && rig.Ready)
            {
                var hand = _comboStep == 1 ? rig.LHand : rig.RHand;
                if (hand != null)
                    return hand.position;
            }

            return AttackOrigin != null ? AttackOrigin.position : transform.position;
        }

        /// <summary>拳の軌跡（`-hitlog`）。発生の何秒後にどこまで出ているかを見る。</summary>
        void LogFist()
        {
            if (_attack == null)
                return;

            Vector3 f = transform.InverseTransformPoint(FistPoint());
            float animT = _anim != null ? _anim.StateTime : -1f;

            var rig = _anim != null ? _anim.Rig : null;
            string arm = "no-rig";
            string other = "";
            if (rig != null && rig.Ready)
            {
                Vector3 e = transform.InverseTransformPoint(rig.RLowerArm.position);
                Vector3 s = transform.InverseTransformPoint(rig.RUpperArm.position);
                arm = $"sh={s.z:F2} el={e.z:F2} elY={e.y:F2}";

                // 打っていない側の手が前へ出ていないか（両腕が前に出る不具合の検出）
                Transform otherHand = _comboStep == 1 ? rig.RHand : rig.LHand;
                if (otherHand != null)
                {
                    Vector3 o = transform.InverseTransformPoint(otherHand.position);
                    other = $" otherX={o.x:F2} otherZ={o.z:F2} otherY={o.y:F2}";
                }
            }

            Debug.Log($"[Fist] {_attack.id} t={_elapsed:F3} animT={animT:F3} " +
                $"z={f.z:F2} y={f.y:F2} aim={AimDistance:F2} " +
                $"state={(_anim != null ? _anim.Current.ToString() : "-")} {arm}{other}");
        }

        /// <summary>
        /// 「拳がどこまで出て、相手の体まで何 cm 余っているか」を残す。
        /// gapBody が正 = 拳が相手の体に届いていない（見えて当たらない）、
        /// 負 = 拳が体に入っている（当たって見えない・めり込む）。
        /// </summary>
        void LogGeometry(AttackData attack, Vector3 fist, EnemyBrain hitEnemy)
        {
            Vector3 fistL = transform.InverseTransformPoint(fist);
            string outcome = hitEnemy != null ? "hit" : "miss";

            var enemy = hitEnemy;
            Vector3 enemyCenter;
            float dist;

            if (enemy != null)
            {
                enemyCenter = enemy.transform.position;
                Vector3 dd = enemyCenter - transform.position;
                dd.y = 0f;
                dist = dd.magnitude;
            }
            else
            {
                enemy = FindNearestEnemy(out enemyCenter, out dist);
            }

            // 拳と相手の体（半径 BodyRadius）の隙間。正 = 届いていない、負 = 体に入っている。
            float gap = float.NaN;
            float gapToAxis = float.NaN;
            if (enemy != null)
            {
                Vector3 d = fist - enemyCenter;
                d.y = 0f;
                gapToAxis = d.magnitude;
                gap = gapToAxis - BodyRadius - FistRadius;
            }

            Debug.Log($"[HitGeom] {attack.id} step={_comboStep} {outcome} " +
                $"fistZ={fistL.z:F2} fistY={fistL.y:F2} " +
                $"enemyDist={(enemy != null ? dist : 0f):F2} fistToAxis={gapToAxis:F2} gapBody={gap:F2}");

            // 空振りの内訳（間合いの外 / 向きが違う）
            if (outcome != "hit" && enemy != null)
            {
                Vector3 to = enemy.transform.position - transform.position;
                to.y = 0f;
                Debug.Log($"[Combat] miss {attack.id} dist={dist:F2} " +
                    $"angle={Vector3.Angle(transform.forward, to):F0} " +
                    $"phase={enemy.Phase} reach={FistRadius + BodyRadius:F2}");
            }
        }

        /// <summary>
        /// 戦闘ログ。毎攻撃・毎ヒットで文字列を作ると GC とログ I/O が無視できないので、
        /// リリースビルドでは <c>-hitlog</c>（<see cref="TraceFist"/>）のときだけ出す。
        /// Editor / Development Build では従来どおり常に出す。
        /// </summary>
        void Log(string message)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log(message);
#else
            if (TraceFist)
                Debug.Log(message);
#endif
        }

        EnemyBrain FindNearestEnemy(out Vector3 center, out float distance)
        {
            EnemyBrain best = null;
            float bestDist = float.MaxValue;
            center = Vector3.zero;

            foreach (var enemy in FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None))
            {
                if (enemy == null || !enemy.IsAlive)
                    continue;

                Vector3 d = enemy.transform.position - transform.position;
                d.y = 0f;
                float dist = d.magnitude;
                if (dist >= bestDist)
                    continue;

                best = enemy;
                bestDist = dist;
                center = enemy.transform.position;
            }

            distance = best != null ? bestDist : 0f;
            return best;
        }
    }
}
