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
                          $" flinch={State.Flinch:F0} stun={StunRemaining:F2} arms={State.Body[BodyPart.Arms]:F0}" +
                          $" legs={State.Body[BodyPart.Legs]:F0} down={(State.IsDown ? 1 : 0)}" +
                          $" block={(IsBlocking ? 1 : 0)}");
            }
        }

        /// <summary>最後に殴ってきた相手。被弾したときにその方向へ向き直るために使う。</summary>
        public Transform LastAttacker { get; private set; }

        /// <summary>
        /// 最後に殴られた場所（顔 / 腹）。被弾の見た目が使う。
        /// <para>
        /// null は「殴られた場所が無い」＝ 掴み失敗の投げなど、パンチ以外で怯んだ場合。
        /// 直前の値が残ると、投げなのに腹打ちの折れ方をするので、投げの経路では明示的に消す
        /// （<see cref="PlayerGrapple"/>）。既定値も null（ゲーム開始直後の怯みで
        /// 顔を殴られた扱いにならないように）。
        /// </para>
        /// </summary>
        public PunchZone? LastHitZone { get; set; }

        /// <summary>ダウン（倒れている）残り時間。コア状態の IsDown をここで立てる。</summary>
        float _downRemaining;

        /// <summary>この時刻まではダウンしない（起き上がり直後の猶予）。</summary>
        float _downImmuneUntil;

        /// <summary>ダウンが明けるまでの残り秒（演出側が「倒れ」の長さを決めるのに使う）。</summary>
        public float DownRemaining => _downRemaining;

        /// <summary>ダウン中か（コア状態と同じ）。</summary>
        public bool IsDown => State != null && State.IsDown;

        /// <summary>
        /// ひるみを溜めてよいか。ダウン中・起き上がり直後・掴まれている最中は溜めない。
        /// ダウン中に溜めると「起き上がるそばからまた倒れる」になり、何もできなくなる。
        /// </summary>
        bool CanTakeFlinch
        {
            get
            {
                if (State == null || State.IsDown || IsDead)
                    return false;
                if (Time.time < _downImmuneUntil)
                    return false;

                if (_grapple == null)
                    _grapple = GetComponent<PlayerGrapple>();
                if (_grapple != null && _grapple.IsGrappled)
                    return false;

                return true;
            }
        }

        /// <summary>
        /// 強制的にダウンさせる。攻撃してきた相手が分かれば、その反対側へ放物線で吹き飛ばす。
        /// </summary>
        public void ForceDown(float seconds, string reason, Transform attacker = null,
            float launchDistance = CombatConstants.DownLaunchDistance,
            float launchHeight = CombatConstants.DownLaunchHeight)
        {
            if (State == null || IsDead)
                return;

            float length = seconds > 0f ? seconds : CombatConstants.DownDuration;
            _downRemaining = Mathf.Max(_downRemaining, length);
            State.IsDown = true;
            State.ResetFlinch();
            _downImmuneUntil = Time.time + length + CombatConstants.DownImmunitySeconds;

            // 後ろへ放物線で飛ぶ。掴まれている最中（投げ）は掴み側が向きを決めるので、
            // ここでは相手の反対側＝飛ばされる向きを渡すだけでよい。
            if (launchDistance > 0f)
            {
                Vector3 dir = attacker != null
                    ? transform.position - attacker.position
                    : -transform.forward;
                var motor = GetComponent<PlayerMotor>();
                if (motor != null)
                    motor.LaunchBack(launchDistance, launchHeight, dir);
            }

            Debug.Log($"[Down] {reason} {length:F2}s flinch={State.Flinch:F0}" +
                      $" hp={State.Hp:F0} immune={CombatConstants.DownImmunitySeconds:F1}s");
        }

        /// <summary>掴まれ判定を見るためだけに持つ（無ければ 1 回だけ探す）。</summary>
        PlayerGrapple _grapple;

        /// <summary>最後に怯んだ時刻（連続怯みの抑制に使う）。</summary>
        float _lastStaggerAt = -10f;

        /// <summary>デバッグ用の状態ログ（[Stat]）の間隔。</summary>
        const float StatLogSeconds = 1f;
        float _statTimer;

        /// <summary>状態ログを出すか（<c>-statlog</c>）。既定は出さない（ログが埋まるため）。</summary>
        static bool _statLog;

        /// <summary>
        /// 状態ログが有効か。姿勢側（被弾の向き・顔の振れ）も同じ条件で測れるように公開する。
        /// </summary>
        public static bool StatLogging => _statLog;

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
            //
            // ひるみ（ダウンゲージ）が上限に達したときも同じダウンにする。
            // こちらは「殴られ続けた結果」なので、攻撃してきた相手の反対側へ吹き飛ぶ。
            if (result.Stagger == StaggerTier.GuardBreak)
            {
                ForceDown(CombatConstants.DownDuration, "guardbreak", attackerTransform);
            }
            else if (CanTakeFlinch)
            {
                float add = attack.Flinch * (result.Blocked ? CombatConstants.BlockedFlinchScale : 1f);
                if (State.AddFlinch(add))
                    ForceDown(CombatConstants.DownDuration, "flinch", attackerTransform);
            }

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
            _downImmuneUntil = 0f;
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
