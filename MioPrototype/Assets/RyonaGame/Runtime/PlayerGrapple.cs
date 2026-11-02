using System;
using UnityEngine;

namespace Ryona.Game
{
    /// <summary>
    /// 掴まれた状態（仕様書 §4.3 の抵抗システム）。
    ///
    /// 数値の本体はエンジン非依存の <see cref="GrappleResistance"/> が持っていて、
    /// ここは「入力を受け取る・時間で失敗させる・演出に知らせる」だけを担当する。
    /// 掴まれたら J / K / L / Space の連打で必要回数ぶんもがくと振りほどける。
    /// 時間切れになると腕にダメージ＋スタミナを失って大きく怯む。
    /// </summary>
    [AddComponentMenu("Ryona/Player Grapple")]
    public sealed class PlayerGrapple : MonoBehaviour
    {
        public PlayerStats Stats;

        /// <summary>掴まれた瞬間。</summary>
        public event Action<GrappleKind> Started;

        /// <summary>振りほどけた瞬間。</summary>
        public event Action Escaped;

        /// <summary>振りほどけずに投げられた瞬間。</summary>
        public event Action Failed;

        GrappleResistance _resistance;
        float _remaining;

        /// <summary>掴まれている間の、相手との距離（m）。</summary>
        /// <remarks>
        /// 敵カプセル 0.40 + 彼女 0.30 = 0.70 が体の重ならない下限。
        /// 0.72 にすると敵が <see cref="EnemyBrain"/> 側の下限（0.95）まで押し戻され、
        /// 伸ばした腕（肩から 0.62m）が彼女の 50cm 手前で止まって「宙に手を出す」画になる。
        /// </remarks>
        const float GrabDistance = 0.60f;
        // 羽交い締めは胸を相手の背中に付けて組む。離れていると腕が伸びきって
        // 「首へ手を伸ばしている」画になる（0.85m だった）。肘を畳んで脇の下へ入れる距離にする。
        const float BackGrabDistance = 0.34f;

        /// <summary>掴まれているか。</summary>
        public bool IsGrappled { get; private set; }

        /// <summary>掴まれている直前の残り時間。</summary>
        public float Remaining => Mathf.Max(0f, _remaining);

        /// <summary>掴んだ相手。倒されたら掴みは解ける。</summary>
        public EnemyBrain Captor { get; private set; }

        /// <summary>もがきの進み具合（0..1）。HUD のバーに出す。</summary>
        public float Progress => _resistance != null ? _resistance.Progress : 0f;

        /// <summary>もがいた回数。</summary>
        public int InputCount => _resistance != null ? _resistance.InputCount : 0;

        /// <summary>必要な入力回数（腕の損傷で増える）。</summary>
        public float RequiredInputs { get; private set; }

        public GrappleKind Kind => _resistance != null ? _resistance.Kind : GrappleKind.Front;

        /// <summary>もがいた瞬間の 0..1。見た目の震えに使う。</summary>
        public float StrugglePulse { get; private set; }

        void Awake()
        {
            if (Stats == null)
                Stats = GetComponent<PlayerStats>();
        }

        /// <summary>
        /// 既存のシーンにも自動で付ける（シーンを作り直さなくても遊べるように）。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var stats = FindFirstObjectByType<PlayerStats>();
            if (stats == null)
                return;

            if (stats.GetComponent<PlayerGrapple>() == null)
                stats.gameObject.AddComponent<PlayerGrapple>();
        }

        void Update()
        {
            if (StrugglePulse > 0f)
                StrugglePulse = Mathf.Max(0f, StrugglePulse - Time.deltaTime * 4f);

            if (!IsGrappled)
                return;

            // 掴んだ相手が倒れた／消えたら、そのまま解放する（罰は与えない）
            if (Captor == null || !Captor.IsAlive)
            {
                Release();
                Debug.Log("[Grapple] 掴んでいた相手が倒れたので解放");
                return;
            }

            if (Stats == null || Stats.IsDead)
            {
                Release();
                return;
            }

            _remaining -= Time.deltaTime;

            if (InputReader.StrugglePressed())
            {
                StrugglePulse = 1f;

                if (_resistance.RegisterInput())
                {
                    Escape();
                    return;
                }
            }

            if (_remaining <= 0f)
                Fail();
        }

        /// <summary>掴まれる。外部（敵の掴み攻撃）から呼ぶ。</summary>
        public void Begin(EnemyBrain captor, GrappleKind kind = GrappleKind.Front)
        {
            if (IsGrappled || Stats == null || Stats.IsDead)
                return;

            Captor = captor;

            // 掴まれた瞬間に相手の正面へ引き寄せられる。
            // 間合い（パイプ用に 1.4m 前後）のままだと手が届いて見えないので、
            // 掴みの間だけ 0.72m まで詰めて「掴まれている」形にする。
            SnapToCaptor(captor, kind);

            // 腕が壊れているほど逃げにくい（仕様書 §4.3）
            float armInjury = Stats.State.Body[BodyPart.Arms];
            RequiredInputs = ResistanceMath.RequiredInputs(kind, 1f, armInjury);

            _resistance = new GrappleResistance(kind, RequiredInputs);
            _remaining = CombatConstants.GrappleLimitSeconds;
            IsGrappled = true;
            StrugglePulse = 0f;

            // 掴まれている間は移動もガードもできない
            Stats.IsMoving = false;
            Stats.IsBlocking = false;

            PlaySfx(SfxId.Impact, 0.9f);

            Debug.Log($"[Grapple] begin kind={kind} need={RequiredInputs:F1} " +
                      $"limit={_remaining:F2}s arm={armInjury:F0} captor={(captor != null ? captor.name : "none")}");

            Started?.Invoke(kind);
        }

        /// <summary>罰なしで解放する（相手が倒れた・プレイヤーが倒れた）。</summary>
        public void Release()
        {
            IsGrappled = false;
            _remaining = 0f;
            Captor = null;
        }

        /// <summary>
        /// 掴まれた瞬間の引き寄せ。相手の正面 GrabDistance まで詰め、向かい合う。
        /// これをしないと、殴り合い用の間合い（1.4m）のまま腕だけ伸びて手が届かない。
        /// </summary>
        void SnapToCaptor(EnemyBrain captor, GrappleKind kind)
        {
            if (captor == null)
                return;

            Vector3 dir = transform.position - captor.transform.position;
            dir.y = 0f;

            if (dir.sqrMagnitude < 1e-4f)
            {
                dir = -captor.transform.forward;
                dir.y = 0f;
            }

            if (dir.sqrMagnitude < 1e-4f)
                return;

            dir.Normalize();

            Vector3 spot = captor.transform.position + dir * (kind == GrappleKind.Back ? BackGrabDistance : GrabDistance);
            spot.y = transform.position.y;
            transform.position = spot;

            // 背中を向けたまま掴まれていると、何に掴まれているのか分からない。
            // 向かい合って抵抗している形にする（カメラは遅れて追いつく）。
            if (kind == GrappleKind.Front)
                transform.rotation = Quaternion.LookRotation(-dir, Vector3.up);
        }

        void Escape()
        {
            var captor = Captor;
            int inputs = InputCount;
            float spent = CombatConstants.GrappleLimitSeconds - Remaining;

            IsGrappled = false;
            _remaining = 0f;
            Captor = null;

            PlaySfx(SfxId.Guard, 0.9f);

            // 振りほどいた側に反撃の隙を作る（掴んでいた側がよろける）
            if (captor != null)
                captor.ForceStagger(CombatConstants.GrappleEscapeStun);

            Debug.Log($"[Grapple] escape inputs={inputs} need={RequiredInputs:F1} t={spent:F2}s");
            Escaped?.Invoke();
        }

        void Fail()
        {
            var captor = Captor;

            int inputs = InputCount;
            float stamina = _resistance.FailureStaminaCost;
            float part = _resistance.FailurePartDamage;

            IsGrappled = false;
            _remaining = 0f;
            Captor = null;

            Stats.State.ConsumeStamina(stamina);

            // 体勢を必ず折って、大きく怯ませる（腕にも傷害が残る）
            var throwAttack = new AttackData
            {
                id = "grapple_throw",
                kind = AttackKind.Grapple,
                damage = 6f,
                postureDamage = CombatConstants.MaxPosture,
                partDamage = part,
                targetPart = BodyPart.Arms,
                startupSeconds = 0f,
                activeSeconds = 0f,
                recoverySeconds = 0f
            };

            // 投げは「殴られた場所」ではない。直前のパンチの顔 / 腹が残っていると、
            // 投げなのに腹打ちの折れ方（上体 57〜71°）をするので、ここで消す。
            Stats.LastHitZone = null;

            var result = Stats.ReceiveHit(captor != null ? captor.State : null, throwAttack,
                captor != null ? captor.transform : null);

            PlaySfx(SfxId.Break, 1f);

            // 突き飛ばされて囲みの外へ。その場に残すと、
            // 怯んでいる 0.7 秒のあいだに包囲側から袋叩きにされる。
            var motor = GetComponent<PlayerMotor>();
            if (motor != null)
            {
                // 背後から投げられた場合も、掴んだ相手から離れる方向へ飛ばす。
                Vector3 away = captor != null ? transform.position - captor.transform.position : -transform.forward;
                motor.Knockback(CombatConstants.GrappleThrowKnockback,
                    CombatConstants.GrappleThrowKnockbackSeconds, away);
            }

            // 投げた直後、他の敵は一拍だけ様子を見る。
            // 怯んでいる 0.7 秒のあいだに殺到されると、1 回の失敗で HP が 3 割飛ぶ。
            int held = 0;
            foreach (var enemy in FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None))
            {
                if (enemy == null || enemy == captor || !enemy.IsAlive)
                    continue;

                if (Vector3.Distance(enemy.transform.position, transform.position) > 6f)
                    continue;

                enemy.HoldFire(CombatConstants.GrappleThrowGraceSeconds);
                held++;
            }

            if (held > 0)
                Debug.Log($"[Grapple] others hold fire {CombatConstants.GrappleThrowGraceSeconds:F2}s x{held}");

            Debug.Log($"[Grapple] fail inputs={inputs} need={RequiredInputs:F1} " +
                      $"staminaCost={stamina:F0} partDamage={part:F0} " +
                      $"hp={Stats.State.Hp:F0} stagger={result.Stagger}");

            Failed?.Invoke();
        }

        static void PlaySfx(SfxId id, float volume)
        {
            var sfx = ProceduralSfx.Instance;
            if (sfx != null)
                sfx.Play(id, volume);
        }
    }
}
