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
    ///
    /// 後ろ拘束（<see cref="GrappleKind.Back"/>）は時間切れで解けない。
    /// 連打で振りほどくか、別の敵の腹パンを受けて解放されるかの二択にする。
    /// 押さえつけ（<see cref="GrappleKind.Pin"/>）は 3 秒の制限時間つきで、
    /// 時間切れになると投げられる（<see cref="Fail"/>）。
    /// </summary>
    [AddComponentMenu("Ryona/Player Grapple")]
    public sealed class PlayerGrapple : MonoBehaviour
    {
        public PlayerStats Stats;

        /// <summary>掴まれた瞬間。</summary>
        public event Action<GrappleKind> Started;

        /// <summary>振りほどけた瞬間。</summary>
        public event Action Escaped;

        /// <summary>押さえつけの時間切れで投げられた瞬間（後ろ拘束では起きない）。</summary>
        public event Action Failed;

        GrappleResistance _resistance;
        float _remaining;
        float _heldSeconds;
        float _suppressRemaining;

        /// <summary>押さえつけ（Pin）で掴まれている間の、相手との距離（m）。</summary>
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

        /// <summary>
        /// 掴まれている残り時間。後ろ拘束は時間切れが無いので 0 を返す
        /// （表示するときは <see cref="HasTimeLimit"/> で分岐する）。
        /// </summary>
        public float Remaining =>
            HasTimeLimit ? Mathf.Max(0f, _remaining) : 0f;

        /// <summary>時間切れで投げられる拘束か。後ろ拘束は false（連打か腹パンでだけ終わる）。</summary>
        public bool HasTimeLimit => Kind == GrappleKind.Pin;

        /// <summary>掴まれてからの経過秒。ログとテスト用。</summary>
        public float HeldSeconds => _heldSeconds;

        /// <summary>腹パン演出などで、もがき入力を一時的に止めているか。</summary>
        public bool IsStruggleSuppressed => _suppressRemaining > 0f;

        /// <summary>もがき入力を止めている残り秒。</summary>
        public float SuppressRemaining => Mathf.Max(0f, _suppressRemaining);

        /// <summary>
        /// 指定秒だけ、もがき入力を受け付けない（腹パンで「く」の字になっている間）。
        /// 拘束そのものは続くので、演出が終わればまた連打できる。
        /// </summary>
        public void SuppressStruggle(float seconds)
        {
            if (seconds > 0f)
                _suppressRemaining = Mathf.Max(_suppressRemaining, seconds);
        }

        /// <summary>
        /// もがきの抑制をすぐ解除する。腹パンを撃ち終えて「拘束だけが残る」ときに使う
        /// （抑制が残ったままだと、5 発のあと数秒間は連打しても抜けられない）。
        /// </summary>
        public void ClearStruggleSuppression()
        {
            _suppressRemaining = 0f;
        }

        /// <summary>掴んだ相手。倒されたら掴みは解ける。</summary>
        public EnemyBrain Captor { get; private set; }

        /// <summary>もがきの進み具合（0..1）。HUD のバーに出す。</summary>
        public float Progress => _resistance != null ? _resistance.Progress : 0f;

        /// <summary>もがいた回数。</summary>
        public int InputCount => _resistance != null ? _resistance.InputCount : 0;

        /// <summary>必要な入力回数（腕の損傷で増える）。</summary>
        public float RequiredInputs { get; private set; }

        public GrappleKind Kind => _resistance != null ? _resistance.Kind : GrappleKind.Back;

        /// <summary>もがいた瞬間の 0..1。見た目の震えに使う。</summary>
        public float StrugglePulse { get; private set; }

        /// <summary>
        /// 押さえつけ中に入る膝蹴りの 0..1。1 から 0 へ減衰する。
        /// 押さえる側（膝）と彼女（崩れ）の両方が見る。
        /// </summary>
        public float KneePulse { get; private set; }

        /// <summary>押さえつけ中に膝蹴りの見た目を立てる（押さえる側から呼ぶ）。</summary>
        public void KickKnee()
        {
            _kneeHold = KneeHoldSeconds;
            KneePulse = 1f;
        }

        /// <summary>膝蹴りの形を保つ秒。姿勢の補間（約 0.1 秒）が追いついてから減衰させる。</summary>
        const float KneeHoldSeconds = 0.18f;
        float _kneeHold;

        /// <summary>
        /// 突き飛ばされた瞬間の 0..1。押されて上体が前へ流れ、腕が後ろへ残る。
        /// 引き渡し（<see cref="HandOff"/>）で立てる。
        /// </summary>
        public float PushPulse { get; private set; }

        /// <summary>突き飛ばしの見た目を立てる。</summary>
        public void PushOff()
        {
            PushPulse = 1f;
        }

        /// <summary>突き飛ばしの形を保つ秒。</summary>
        const float PushHoldSeconds = 0.12f;
        float _pushHold;

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

            // 膝蹴りの見た目は掴まれていなくても抜けきるまで減衰させる（投げられた直後に残らない）
            if (_kneeHold > 0f)
            {
                // 姿勢の補間が追いつくまで形を保つ。すぐ減衰させると腿が上がりきらない。
                _kneeHold -= Time.deltaTime;
                KneePulse = 1f;
            }
            else if (KneePulse > 0f)
                KneePulse = Mathf.Max(0f, KneePulse - Time.deltaTime * 4f);

            // 突き飛ばしの見た目も同じ作り（保ってから減衰）
            if (_pushHold > 0f)
            {
                _pushHold -= Time.deltaTime;
                PushPulse = 1f;
            }
            else if (PushPulse > 0f)
                PushPulse = Mathf.Max(0f, PushPulse - Time.deltaTime * 4f);

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

            _heldSeconds += Time.deltaTime;

            // 腹パンで「く」の字になっている間は、もがき入力を受け付けない。
            // 拘束は続くので、演出が終わればまた連打できる。
            if (_suppressRemaining > 0f)
            {
                _suppressRemaining = Mathf.Max(0f, _suppressRemaining - Time.deltaTime);
                StrugglePulse = Mathf.Max(0f, StrugglePulse - Time.deltaTime * 6f);
                return;
            }

            // 後ろ拘束は時間切れが無いので、限界まで殴られていても手を離すのは
            // もがきで逃げたときだけ（`PlayerStats` が離れた瞬間にうつ伏せで倒す）。
            // 腹パンの途中で勝手に離すと、5 発のコンボが途切れて「殴り終える前に崩れた」画になる。

            // 後ろ拘束は時間切れで解けない。押さえつけ（Pin）だけが時間切れで投げる。
            if (HasTimeLimit)
            {
                _remaining -= Time.deltaTime;
                if (_remaining <= 0f)
                {
                    Fail();
                    return;
                }
            }

            if (InputReader.StrugglePressed())
                Struggle();
        }

        /// <summary>
        /// もがく。入力を待たずに呼べるようにしておく（テストと自動操作のため）。
        /// 必要回数に届いたらその場で振りほどく。
        /// </summary>
        public void Struggle()
        {
            if (!IsGrappled || IsStruggleSuppressed)
                return;

            StrugglePulse = 1f;

            if (_resistance.RegisterInput())
                Escape();
        }

        /// <summary>掴まれる。外部（敵の掴み攻撃）から呼ぶ。掴みは背後からだけ。</summary>
        public void Begin(EnemyBrain captor, GrappleKind kind = GrappleKind.Back)
        {
            if (IsGrappled || Stats == null || Stats.IsDead)
                return;

            Captor = captor;

            // 掴まれた瞬間に相手の正面へ引き寄せられる。
            // 間合い（パイプ用に 1.4m 前後）のままだと手が届いて見えないので、
            // 掴みの間だけ詰めて「掴まれている」形にする。
            SnapToCaptor(captor, kind);

            // 腕が壊れているほど逃げにくい（仕様書 §4.3）
            float armInjury = Stats.State.Body[BodyPart.Arms];
            RequiredInputs = ResistanceMath.RequiredInputs(kind, 1f, armInjury);

            _resistance = new GrappleResistance(kind, RequiredInputs);
            _remaining = kind == GrappleKind.Pin
                ? CombatConstants.GrappleLimitSeconds
                : float.PositiveInfinity;
            _heldSeconds = 0f;
            _suppressRemaining = 0f;
            IsGrappled = true;
            StrugglePulse = 0f;
            KneePulse = 0f;

            // 掴まれている間は移動もガードもできない
            Stats.IsMoving = false;
            Stats.IsBlocking = false;

            PlaySfx(SfxId.Impact, 0.9f);

            string limit = HasTimeLimit ? $"{_remaining:F2}s" : "none";
            Debug.Log($"[Grapple] begin kind={kind} need={RequiredInputs:F1} " +
                      $"limit={limit} arm={armInjury:F0} captor={(captor != null ? captor.name : "none")}");

            Started?.Invoke(kind);
        }

        /// <summary>
        /// 拘束を別の敵へ引き渡す（たらい回し）。掴まれたまま相手だけが入れ替わる。
        /// もがきの進捗と制限時間はやり直し（抜けても次の敵に捕まる）。
        /// </summary>
        public void HandOff(EnemyBrain next, GrappleKind kind = GrappleKind.Back, bool shove = false)
        {
            if (!IsGrappled || next == null || next == Captor || Stats == null || Stats.IsDead)
                return;

            Captor = next;

            if (shove)
            {
                // 突き飛ばし: 相手の正面（Pin）か背面（Back）へ押し出し、
                // 背面向きのときは 180° 回す。押し終わりに必ず掴み位置へ合わせる
                // （合わせないと「2 人目が遠い」距離で拘束が始まる）。
                Vector3 toNext = next.transform.position - transform.position;
                toNext.y = 0f;
                Vector3 dirTo = toNext.sqrMagnitude > 1e-4f ? toNext.normalized : transform.forward;

                bool faceTarget = kind == GrappleKind.Pin;
                Vector3 facing = faceTarget ? dirTo : -dirTo;
                Vector3 pushDir = dirTo;

                StartCoroutine(Shove(next, kind, pushDir, facing, faceTarget));
            }
            else
            {
                SnapToCaptor(next, kind);
            }

            RequiredInputs = ResistanceMath.RequiredInputs(kind, 1f, Stats.State.Body[BodyPart.Arms]);
            _resistance = new GrappleResistance(kind, RequiredInputs);
            _remaining = kind == GrappleKind.Pin
                ? CombatConstants.GrappleLimitSeconds
                : float.PositiveInfinity;
            _suppressRemaining = 0f;

            Debug.Log($"[HandOff] {next.name} が拘束を引き継ぐ kind={kind} need={RequiredInputs:F1}");
            Started?.Invoke(kind);
        }

        /// <summary>
        /// 突き飛ばし → 向き直し → 掴み位置へ合わせる。
        /// 押し出しただけで終わると、相手との距離が 1m 以上開いて「遠い拘束」になる（実測）。
        /// </summary>
        System.Collections.IEnumerator Shove(EnemyBrain next, GrappleKind kind,
            Vector3 pushDir, Vector3 facing, bool faceTarget)
        {
            var motor = GetComponent<PlayerMotor>();
            if (motor != null)
                motor.Knockback(0.9f, 0.30f, pushDir);

            // 押された瞬間の姿勢（上体が前へ、腕が後ろへ残る）
            _pushHold = PushHoldSeconds;
            PushPulse = 1f;

            Quaternion target = Quaternion.LookRotation(facing, Vector3.up);
            Quaternion from = transform.rotation;
            float t = 0f;
            const float turnSeconds = 0.25f;
            while (t < turnSeconds)
            {
                t += Time.deltaTime;
                transform.rotation = Quaternion.Slerp(from, target, Mathf.Clamp01(t / turnSeconds));
                yield return null;
            }
            transform.rotation = target;

            // 押し出しの残りが抜けてから、掴み位置ぴったりに寄せる
            yield return new WaitForSeconds(0.05f);
            if (IsGrappled && Captor == next)
            {
                SnapToCaptor(next, kind);
                Vector3 gap = next.transform.position - transform.position;
                gap.y = 0f;
                Debug.Log($"[HandOff] snap kind={kind} gap={gap.magnitude:F2}");
            }
        }

        /// <summary>
        /// 罰なしで解放する（相手が倒れた・プレイヤーが倒れた・腹パン演出が終わった）。
        /// </summary>
        public void Release()
        {
            IsGrappled = false;
            _remaining = 0f;
            _suppressRemaining = 0f;
            Captor = null;
        }

        /// <summary>
        /// 掴まれた瞬間の引き寄せ。相手の体の前に、掴み方に応じた距離まで詰める。
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

            // 羽交い締めは密着させる。押さえつけは腕を伸ばす余地を残す。
            Vector3 spot = captor.transform.position + dir * (kind == GrappleKind.Back ? BackGrabDistance : GrabDistance);
            spot.y = transform.position.y;
            transform.position = spot;

            // 羽交い締めは背中を向けたまま掴まれる（向きは変えない）。
            // 押さえつけは正面から掴まれるので、相手の方を向かせる。
            if (kind == GrappleKind.Pin)
            {
                Vector3 look = captor.transform.position - spot;
                look.y = 0f;
                if (look.sqrMagnitude > 1e-4f)
                    transform.rotation = Quaternion.LookRotation(look.normalized, Vector3.up);
            }
        }

        void Escape()
        {
            var captor = Captor;
            int inputs = InputCount;
            float spent = _heldSeconds;

            IsGrappled = false;
            _remaining = 0f;
            _suppressRemaining = 0f;
            Captor = null;

            PlaySfx(SfxId.Guard, 0.9f);

            // 振りほどいた側に反撃の隙を作る（掴んでいた側がよろける）
            if (captor != null)
                captor.ForceStagger(CombatConstants.GrappleEscapeStun);

            Debug.Log($"[Grapple] escape inputs={inputs} need={RequiredInputs:F1} t={spent:F2}s");
            Escaped?.Invoke();
        }

        /// <summary>押さえつけ（Pin）の時間切れ。後ろ拘束では呼ばない。</summary>
        void Fail()
        {
            var captor = Captor;

            int inputs = InputCount;
            float stamina = _resistance.FailureStaminaCost;
            float part = _resistance.FailurePartDamage;

            IsGrappled = false;
            _remaining = 0f;
            _suppressRemaining = 0f;
            Captor = null;

            Stats.State.ConsumeStamina(stamina);

            // 押さえつけを逃げられなかったら必ず投げる（後ろへ飛んで仰向けになる）。
            // 以前は「ひるみが満タンなら投げずに崩す」にしていたが、膝 5 発で毎回満タンになり
            // 投げ（仰向け）が消えた。うつ伏せになるのは、もがいて抜けたときに限界だった場合だけ。

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

            // 投げられたらダウン（後ろへ放物線で飛んで倒れる）。
            // 怯みだけで済ませていた頃は「投げられたのに立っている」ように見えた。
            // ダウンを先に立ててからダメージを渡す（受け手の姿勢が「倒れる」で始まるように）。
            Stats.ForceDown(CombatConstants.DownDuration, "throw", captor != null ? captor.transform : null,
                CombatConstants.ThrowLaunchDistance, CombatConstants.ThrowLaunchHeight);

            var result = Stats.ReceiveHit(captor != null ? captor.State : null, throwAttack,
                captor != null ? captor.transform : null);

            PlaySfx(SfxId.Break, 1f);

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
