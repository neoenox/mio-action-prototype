using System;
using UnityEngine;

namespace Ryona.Game
{
    /// <summary>
    /// プレイヤーの戦闘状態。数値の本体はエンジン非依存の <see cref="CombatState"/>。
    /// </summary>
    [AddComponentMenu("Ryona/Player Stats")]
    public sealed class PlayerStats : MonoBehaviour
    {
        public CombatState State { get; private set; }

        /// <summary>移動入力があるか。スタミナ回復速度に効く。</summary>
        public bool IsMoving { get; set; }

        /// <summary>ガード入力中か。被弾解決に効く。</summary>
        public bool IsBlocking { get; set; }

        public float StunRemaining { get; private set; }

        /// <summary>怯み時間を延長する。起き上がり中は動けないようにするために使う。</summary>
        public void AddStun(float seconds)
        {
            if (seconds > 0f)
                StunRemaining = Mathf.Max(StunRemaining, seconds);
        }

        public bool IsStunned => StunRemaining > 0f;

        public bool IsDead => State != null && !State.IsAlive;

        /// <summary>被弾したときに発火。演出はこれを見て反応モーションを選ぶ。</summary>
        public event Action<HitResult> HitReceived;

        /// <summary>怯みが発生したときに発火。</summary>
        public event Action<HitResult> Staggered;

        void Awake()
        {
            State = new CombatState();
        }

        void Update()
        {
            if (State == null)
                return;

            if (StunRemaining > 0f)
                StunRemaining = Mathf.Max(0f, StunRemaining - Time.deltaTime);

            if (_downRemaining > 0f)
            {
                _downRemaining = Mathf.Max(0f, _downRemaining - Time.deltaTime);
                State.IsDown = _downRemaining > 0f;
            }

            State.Tick(Time.deltaTime, moving: IsMoving && !IsBlocking, blocking: IsBlocking);

            // 難易度調整用の状態ログ（1 秒ごと）。-holdattack の通しで
            // 「怯みで何もできない時間」を数字で見るために出している。
            _statTimer -= Time.deltaTime;
            if (_statLog && _statTimer <= 0f)
            {
                _statTimer = StatLogSeconds;

                Debug.Log($"[Stat] hp={State.Hp:F0} stamina={State.Stamina:F0} posture={State.Posture:F0}" +
                          $" stun={StunRemaining:F2} arms={State.Body[BodyPart.Arms]:F0}" +
                          $" legs={State.Body[BodyPart.Legs]:F0} down={(State.IsDown ? 1 : 0)}" +
                          $" block={(IsBlocking ? 1 : 0)}");
            }
        }

        /// <summary>最後に殴ってきた相手。被弾したときにその方向へ向き直るために使う。</summary>
        public Transform LastAttacker { get; private set; }

        /// <summary>最後に殴られた場所（顔 / 腹）。被弾の見た目が使う。</summary>
        public PunchZone LastHitZone { get; set; } = PunchZone.Face;

        /// <summary>ダウン（倒れている）残り時間。コア状態の IsDown をここで立てる。</summary>
        float _downRemaining;

        /// <summary>掴まれ判定を見るためだけに持つ（無ければ 1 回だけ探す）。</summary>
        PlayerGrapple _grapple;

        /// <summary>最後に怯んだ時刻（連続怯みの抑制に使う）。</summary>
        float _lastStaggerAt = -10f;

        /// <summary>デバッグ用の状態ログ（[Stat]）の間隔。</summary>
        const float StatLogSeconds = 1f;
        float _statTimer;

        /// <summary>状態ログを出すか（<c>-statlog</c>）。既定は出さない（ログが埋まるため）。</summary>
        static bool _statLog;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void ParseStatLogFlag()
        {
            _statLog = false;

            foreach (string a in System.Environment.GetCommandLineArgs())
            {
                if (string.Equals(a, "-statlog", System.StringComparison.OrdinalIgnoreCase))
                    _statLog = true;
            }
        }

        /// <summary>攻撃を受ける。プレイヤー側の被弾はすべてここを通る。</summary>
        public HitResult ReceiveHit(CombatState attacker, AttackData attack, Transform attackerTransform = null)
        {
            float scale = DamageScaleAgainst(attackerTransform);

            var result = DamageResolver.Resolve(attacker, attack, State, IsBlocking, scale);

            if (attackerTransform != null)
                LastAttacker = attackerTransform;

            if (result.Stagger != StaggerTier.None)
            {
                // 怯み直後に続けて殴られても、怯み時間は上書きしない（ダメージはそのまま）。
                // 3 人に囲まれると怯みが切れ目なく続き、何もできないまま HP が溶けるため。
                if (Time.time - _lastStaggerAt >= CombatConstants.HitStunGrace)
                    StunRemaining = Mathf.Max(StunRemaining, result.StunSeconds);

                _lastStaggerAt = Time.time;
            }

            // ガードブレイク = ダウン。演出は「倒れ 1.2s + 起き上がり 0.8s」で、
            // 合計は DownDuration（2.0s）と揃えてある。コア側でも同じ長さだけ
            // 倒れている扱いにしないと、倒れている間も立ち扱いになる
            // （足音が鳴る、倒れる音が出ない、など）。
            if (result.Stagger == StaggerTier.GuardBreak)
                _downRemaining = CombatConstants.DownDuration;

            HitReceived?.Invoke(result);
            if (result.Stagger != StaggerTier.None)
                Staggered?.Invoke(result);

            return result;
        }

        public void RestoreAll()
        {
            State.RestoreAll();
            StunRemaining = 0f;
            _downRemaining = 0f;
            State.IsDown = false;
        }

        /// <summary>
        /// 掴まれている最中は、掴んでいる本人以外からの一撃を弱める。
        /// 3 秒間で包囲側に袋叩きにされると 1 回の掴みで HP が半分飛ぶため。
        /// 掴んでいる本人の「投げ」は等倍のまま。
        /// </summary>
        float DamageScaleAgainst(Transform attacker)
        {
            if (_grapple == null)
                _grapple = GetComponent<PlayerGrapple>();

            if (_grapple == null || !_grapple.IsGrappled)
                return 1f;

            Transform captor = _grapple.Captor != null ? _grapple.Captor.transform : null;
            if (captor != null && attacker == captor)
                return 1f;

            Debug.Log($"[Grapple] damped hit from {(attacker != null ? attacker.name : "unknown")}" +
                      $" scale={CombatConstants.GrabbedDamageFromOthers:F2}");

            return CombatConstants.GrabbedDamageFromOthers;
        }
    }
}
