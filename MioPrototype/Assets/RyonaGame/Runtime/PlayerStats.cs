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

        /// <summary>移動入力があるか。歩き / 待機の姿勢切替に使う。</summary>
        public bool IsMoving { get; set; }

        /// <summary>ガード入力中か。被弾解決に効く。</summary>
        public bool IsBlocking { get; set; }

        public float StunRemaining { get; private set; }

        /// <summary>ゴッドモード中は HP が減り切らず、毎秒自動で回復する（G で切り替え）。</summary>
        public bool GodMode { get; private set; }

        /// <summary>ゴッドモードの回復速度（HP/秒）。</summary>
        const float GodHealPerSecond = 40f;

        /// <summary>ゴッドモードを切り替える。死亡中に有効化したら、その場で復帰する。</summary>
        public void SetGodMode(bool enabled)
        {
            if (GodMode == enabled)
                return;

            GodMode = enabled;

            if (enabled && State != null)
            {
                bool wasDead = IsDead;
                State.EnsureHpAtLeast(1f);
                if (wasDead)
                    RestoreAll();
            }

            Debug.Log($"[God] {(enabled ? "ON（体力自動回復）" : "OFF")}");
        }

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

        /// <summary>HP 0 から復帰したときに発火。</summary>
        public event Action Revived;

        void Awake()
        {
            State = new CombatState();
        }

        void Update()
        {
            if (State == null)
                return;

            if (!PauseMenu.IsPaused && InputReader.GodPressed())
                SetGodMode(!GodMode);

            if (GodMode)
            {
                State.EnsureHpAtLeast(1f);
                State.HealHp(GodHealPerSecond * Time.deltaTime);
            }

            if (StunRemaining > 0f)
                StunRemaining = Mathf.Max(0f, StunRemaining - Time.deltaTime);

            if (_downRemaining > 0f)
            {
                _downRemaining = Mathf.Max(0f, _downRemaining - Time.deltaTime);
                State.IsDown = _downRemaining > 0f;
            }

            State.Tick(Time.deltaTime);

            // 難易度調整用の状態ログ（1 秒ごと）。-holdattack の通しで
            // 「怯みで何もできない時間」を数字で見るために出している。
            _statTimer -= Time.deltaTime;
            if (_statLog && _statTimer <= 0f)
            {
                _statTimer = StatLogSeconds;

                Debug.Log($"[Stat] hp={State.Hp:F0} flinch={State.Flinch:F0} stun={StunRemaining:F2}" +
                          $" arms={State.Body[BodyPart.Arms]:F0}" +
                          $" legs={State.Body[BodyPart.Legs]:F0} down={(State.IsDown ? 1 : 0)}" +
                          $" block={(IsBlocking ? 1 : 0)}");
            }
        }

        /// <summary>最後に殴ってきた相手。被弾したときにその方向へ向き直るために使う。</summary>
        public Transform LastAttacker { get; private set; }
        public bool LastHitWasTagPunch { get; private set; }
        public bool LastHitWasHeldBelly { get; private set; }

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
        /// 今のダウンが「前へ崩れる」か。
        /// 背後からの一撃か腹ダメージの蓄積で倒れたときは前へ倒れる。
        /// </summary>
        public bool DownIsForward { get; private set; }

        float _heldBellyDamage;

        public float HeldBellyDamage => _heldBellyDamage;
        public bool HasHeldBellyDown => _heldBellyDamage >= CombatConstants.MaxHeldBellyDamage;

        // コンボを途中で切らず、自然に拘束が解けるときだけ腹ダウンを消化する。
        public void ResolveHeldBellyDown()
        {
            if (!HasHeldBellyDown || IsDead || IsGrappledNow)
                return;

            _heldBellyDamage = 0f;
            ForceDown(CombatConstants.DownDuration, "held-belly", forward: true);
        }

        /// <summary>
        /// ひるみを溜めてよいか。ダウン中と起き上がり直後は溜めない。
        /// ダウン中に溜めると「起き上がるそばからまた倒れる」になり、何もできなくなる。
        /// <para>
        /// 掴まれている最中も溜める（腹パン・膝蹴りの蓄積）。ただし**掴まれている間は倒さない**
        /// （姿勢が壊れる）。溜まったまま解け、次の一撃で崩れる。
        /// </para>
        /// </summary>
        bool CanTakeFlinch
        {
            get
            {
                if (State == null || State.IsDown || IsDead)
                    return false;
                if (Time.time < _downImmuneUntil)
                    return false;

                return true;
            }
        }

        /// <summary>
        /// 掴まれている最中か。
        /// </summary>
        bool IsGrappledNow
        {
            get
            {
                if (_grapple == null)
                    _grapple = GetComponent<PlayerGrapple>();
                return _grapple != null && _grapple.IsGrappled;
            }
        }

        /// <summary>
        /// 強制的にダウンさせる。攻撃してきた相手が分かれば、その反対側へ放物線で吹き飛ばす。
        /// <para>
        /// 攻撃者が背後なら前へ、正面なら後ろへ倒れる。腹ダウンでは前を指定する。
        /// 前へ崩れるときは飛ばさない（腹を抱えて膝から落ちる動きになる）。
        /// </para>
        /// </summary>
        public void ForceDown(float seconds, string reason, Transform attacker = null,
            float launchDistance = CombatConstants.DownLaunchDistance,
            float launchHeight = CombatConstants.DownLaunchHeight,
            bool? forward = null)
        {
            if (State == null || IsDead)
                return;

            DownIsForward = forward ?? (attacker != null &&
                Vector3.Dot(transform.forward, attacker.position - transform.position) < 0f);

            float length = seconds > 0f ? seconds : CombatConstants.DownDuration;
            _downRemaining = Mathf.Max(_downRemaining, length);
            State.IsDown = true;
            State.ResetFlinch();
            _downImmuneUntil = Time.time + length + CombatConstants.DownImmunitySeconds;

            // 前へ崩れるときは飛ばさない。腹を抱えたまま膝が抜ける動きなので、
            // 放物線で飛ぶと「腹を殴られたのに吹き飛んだ」ように見える。
            if (DownIsForward)
                launchDistance = 0f;

            // 後ろへ放物線で飛ぶ。攻撃してきた相手が分かれば、その反対側へ飛ばす。
            // 前へ崩れるときは launchDistance を 0 にしているのでここは通らない。
            launchDistance *= _launchScale;
            launchHeight *= _launchScale;

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
                      $" hp={State.Hp:F0} forward={(DownIsForward ? 1 : 0)}" +
                      $" immune={CombatConstants.DownImmunitySeconds:F1}s");
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

        /// <summary>
        /// ダウンの飛距離の倍率（<c>-downlaunch=0.7</c>）。撮り比べ用。既定は 1。
        /// </summary>
        static float _launchScale = 1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void ParseStatLogFlag()
        {
            _statLog = false;
            _launchScale = 1f;

            foreach (string a in System.Environment.GetCommandLineArgs())
            {
                if (string.Equals(a, "-statlog", System.StringComparison.OrdinalIgnoreCase))
                    _statLog = true;
                else if (a.StartsWith("-downlaunch=", System.StringComparison.OrdinalIgnoreCase)
                    && float.TryParse(a.Substring("-downlaunch=".Length),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float scale)
                    && scale > 0f)
                    _launchScale = scale;
            }
        }

        /// <summary>攻撃を受ける。プレイヤー側の被弾はすべてここを通る。</summary>
        public HitResult ReceiveHit(CombatState attacker, AttackData attack,
            Transform attackerTransform = null, float heldBellyDamage = 0f, bool isTagPunch = false)
        {
            float scale = DamageScaleAgainst(attackerTransform);

            var result = DamageResolver.Resolve(attacker, attack, State, IsBlocking, scale);

            // ゴッドモードでは致死ダメージでも HP を 1 で踏みとどめる（Update で回復する）。
            if (GodMode)
                State.EnsureHpAtLeast(1f);

            if (attackerTransform != null)
                LastAttacker = attackerTransform;
            LastHitWasTagPunch = isTagPunch;
            LastHitWasHeldBelly = heldBellyDamage > 0f;

            if (result.Stagger != StaggerTier.None)
            {
                // 怯み直後に続けて殴られても、怯み時間は上書きしない（ダメージはそのまま）。
                // 3 人に囲まれると怯みが切れ目なく続き、何もできないまま HP が溶けるため。
                if (Time.time - _lastStaggerAt >= CombatConstants.HitStunGrace)
                    StunRemaining = Mathf.Max(StunRemaining, result.StunSeconds);

                _lastStaggerAt = Time.time;
            }

            if (CanTakeFlinch)
            {
                float add = attack.Flinch * (result.Blocked ? CombatConstants.BlockedFlinchScale : 1f);
                bool grappled = IsGrappledNow;

                if (grappled && heldBellyDamage > 0f && !result.Blocked && result.Damage > 0f)
                    _heldBellyDamage = Mathf.Min(CombatConstants.MaxHeldBellyDamage,
                        _heldBellyDamage + heldBellyDamage);

                if (State.AddFlinch(add) && !grappled)
                    ForceDown(CombatConstants.DownDuration, "flinch", attackerTransform);
            }

            HitReceived?.Invoke(result);
            if (result.Stagger != StaggerTier.None)
                Staggered?.Invoke(result);

            return result;
        }

        public void RestoreAll()
        {
            bool wasDead = IsDead;
            State.RestoreAll();
            StunRemaining = 0f;
            _downRemaining = 0f;
            _downImmuneUntil = 0f;
            _heldBellyDamage = 0f;
            State.IsDown = false;

            if (wasDead)
                Revived?.Invoke();
        }

        /// <summary>
        /// 掴まれている最中は、掴んでいる本人以外からの一撃を弱める。
        /// 短時間で包囲側に袋叩きにされると 1 回の掴みで HP が半分飛ぶため。
        /// 掴んでいる本人の一撃は等倍のまま。
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
