using System;
using UnityEngine;

namespace Ryona.Game
{
    /// <summary>
    /// 掴まれた状態（仕様書 §4.3 の抵抗システム）。
    ///
    /// 数値の本体はエンジン非依存の <see cref="GrappleResistance"/> が持っていて、
    /// ここは「入力を受け取る・時間切れで解放する・演出に知らせる」だけを担当する。
    /// 掴まれたら J / K / L / Space の連打で必要回数ぶんもがくと振りほどける。
    ///
    /// 後ろ拘束（<see cref="GrappleKind.Back"/>）は時間切れで解けない。
    /// 連打で振りほどくか、タッグ腹パンを 5 発食らうかになる。
    /// 食らい切ったときに腹ダメージが上限ならうつ伏せにダウンし、
    /// 未満なら引き渡しを抽選する（<see cref="GrappleHandOff.TryChain"/>）。
    /// 押さえつけ（<see cref="GrappleKind.Pin"/>）は
    /// <see cref="CombatConstants.GrappleLimitSeconds"/> 秒の制限時間つきで、
    /// 時間切れになると解放される（<see cref="Fail"/>）。
    /// </summary>
    [AddComponentMenu("Ryona/Player Grapple")]
    public sealed class PlayerGrapple : MonoBehaviour
    {
        public PlayerStats Stats;

        /// <summary>掴まれた瞬間。</summary>
        public event Action<GrappleKind> Started;

        /// <summary>振りほどけた瞬間。</summary>
        public event Action Escaped;

        /// <summary>押さえつけが時間切れになった瞬間（後ろ拘束では起きない）。</summary>
        public event Action Failed;

        /// <summary>拘束が解けた瞬間（もがいて逃げた場合を除く）。姿勢の突き放しに使う。</summary>
        public event Action Released;

        GrappleResistance _resistance;
        float _remaining;
        float _heldSeconds;
        float _suppressRemaining;

        /// <summary>押さえつけ（Pin）/ ベアハッグで掴まれている間の、相手との距離（m）。</summary>
        /// <remarks>
        /// 敵カプセル 0.40 + 彼女 0.30 = 0.70 が体の重ならない下限。
        /// 0.72 にすると敵が <see cref="EnemyBrain"/> 側の下限（0.95）まで押し戻され、
        /// 伸ばした腕（肩から 0.62m）が彼女の 50cm 手前で止まって「宙に手を出す」画になる。
        /// </remarks>
        const float GrabDistance = 0.60f;

        /// <summary>
        /// ベアハッグの基準距離（離れ気味のとき）。締め付けでここから 0.16m 詰めて密着する。
        /// </summary>
        const float BearHugDistance = 0.54f;
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

        /// <summary>時間切れで解ける拘束か。後ろ拘束（Back）だけが false。</summary>
        public bool HasTimeLimit => Kind != GrappleKind.Back;

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
        /// ベアハッグの密着度 0..1。0 = 少し離れている / 1 = 体が密着している。
        /// 締め付けのたびに 0 → 1 → 0 を繰り返す。
        /// </summary>
        public float SqueezePulse { get; private set; }

        /// <summary>いま「密着して締め付けている」瞬間か（ダメージはこの瞬間に入れる）。</summary>
        public bool IsBearHugContact =>
            IsGrappled && Kind == GrappleKind.BearHug && _hugStage == HugStage.Contact;

        /// <summary>
        /// 引き寄せられている勢い 0..1。引き寄せの中盤で最大になり、密着で 0 に戻る。
        /// 彼女の上体が前へ持っていかれ、腕と足が遅れて残る表現に使う。
        /// </summary>
        public float PullPulse { get; private set; }

        /// <summary>抱え上げられている度合い 0..1。ベアハッグ中に上がり、解放で下がる。</summary>
        public float LiftPulse { get; private set; }

        /// <summary>
        /// 締め付けを始める（掴んでいる側から呼ぶ）。体を引き寄せ、密着した瞬間に
        /// <see cref="IsBearHugContact"/> が true になる。
        /// </summary>
        public void Squeeze()
        {
            if (!IsGrappled || Kind != GrappleKind.BearHug || _hugStage != HugStage.Rest)
                return;

            _hugStage = HugStage.Pull;
            _hugTime = 0f;
        }

        // 密着 → 少し離れる → 密着 の周期。
        const float HugPullSeconds = 0.12f;
        const float HugContactSeconds = 0.10f;
        const float HugReleaseSeconds = 0.30f;

        /// <summary>ベアハッグの密着サイクル。</summary>
        enum HugStage
        {
            Rest,
            Pull,
            Contact,
            Release
        }

        HugStage _hugStage;
        float _hugTime;
        float _hugPhase;

        /// <summary>密着しているときの距離の詰め（m）。胸が触れる距離まで寄せる。</summary>
        const float HugTightInset = 0.16f;

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

            // 膝蹴りの見た目は掴まれていなくても抜けきるまで減衰させる（解放された直後に残らない）
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

            // ベアハッグ以外では密着度・引き寄せを抜く（演出の取りこぼし防止）。
            if (!IsGrappled || Kind != GrappleKind.BearHug)
            {
                SqueezePulse = Mathf.MoveTowards(SqueezePulse, 0f, Time.deltaTime * 4f);
                PullPulse = Mathf.MoveTowards(PullPulse, 0f, Time.deltaTime * 10f);
            }

            // 抱え上げ。掴まれてベアハッグの間だけ上がり、解放されると下がる。
            float liftTarget = IsGrappled && Kind == GrappleKind.BearHug ? 1f : 0f;
            LiftPulse = Mathf.MoveTowards(LiftPulse, liftTarget,
                Time.deltaTime * (liftTarget > LiftPulse ? 2.5f : 4.5f));

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

            // ベアハッグ: 体を引き寄せて密着させ、少し離してからまた締める。
            if (Kind == GrappleKind.BearHug)
                TickBearHugBody(Time.deltaTime);

            _heldSeconds += Time.deltaTime;

            // 腹パンで「く」の字になっている間は、もがき入力を受け付けない。
            // 拘束は続くので、演出が終わればまた連打できる。
            if (_suppressRemaining > 0f)
            {
                _suppressRemaining = Mathf.Max(0f, _suppressRemaining - Time.deltaTime);
                StrugglePulse = Mathf.Max(0f, StrugglePulse - Time.deltaTime * 6f);
                return;
            }

            // 後ろ拘束は時間切れで解けない。
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
            _remaining = kind == GrappleKind.Back
                ? float.PositiveInfinity
                : CombatConstants.GrappleLimitSeconds;
            _heldSeconds = 0f;
            _suppressRemaining = 0f;
            IsGrappled = true;
            StrugglePulse = 0f;
            KneePulse = 0f;
            if (kind == GrappleKind.BearHug)
            {
                _hugStage = HugStage.Rest;
                _hugTime = 0f;
                _hugPhase = 0f;
                SqueezePulse = 0f;
                PullPulse = 0f;
            }

            // 掴まれている間は移動できない
            Stats.IsMoving = false;

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
                // 突き飛ばし: 相手の正面（Pin）か背面（Back）へ、掴み位置まで
                // 一続きに押し出す。以前は 0.9m 突き飛ばしてから掴み位置へ
                // SnapToCaptor で戻していたため、行き過ぎてから瞬間移動して見えた。
                StartCoroutine(Shove(next, kind));
            }
            else
            {
                SnapToCaptor(next, kind);
            }

            RequiredInputs = ResistanceMath.RequiredInputs(kind, 1f, Stats.State.Body[BodyPart.Arms]);
            _resistance = new GrappleResistance(kind, RequiredInputs);
            _remaining = kind == GrappleKind.Back
                ? float.PositiveInfinity
                : CombatConstants.GrappleLimitSeconds;
            _suppressRemaining = 0f;

            Debug.Log($"[HandOff] {next.name} が拘束を引き継ぐ kind={kind} need={RequiredInputs:F1}");
            Started?.Invoke(kind);
        }

        /// <summary>
        /// 掴み位置まで一続きに移動する。突き飛ばしの距離と掴み位置を別々に扱うと、
        /// 行き過ぎた後に戻る動きが「ワープ」に見えるので、最初から終点へ運ぶ。
        /// </summary>
        System.Collections.IEnumerator Shove(EnemyBrain next, GrappleKind kind)
        {
            // 押された瞬間の姿勢（上体が前へ、腕が後ろへ残る）
            _pushHold = PushHoldSeconds;
            PushPulse = 1f;

            Vector3 fromPos = transform.position;
            Vector3 toPos = GrabSpot(next, kind);

            Vector3 toNext = next.transform.position - toPos;
            toNext.y = 0f;
            Vector3 lookDir = kind == GrappleKind.Back ? -toNext : toNext;

            Quaternion fromRot = transform.rotation;
            Quaternion targetRot = lookDir.sqrMagnitude > 1e-4f
                ? Quaternion.LookRotation(lookDir.normalized, Vector3.up)
                : fromRot;

            float distance = Vector3.Distance(fromPos, toPos);
            float moveSeconds = Mathf.Clamp(0.12f + distance * 0.18f, 0.18f, 0.65f);
            const float turnSeconds = 0.25f;

            float t = 0f;
            while (t < moveSeconds)
            {
                t += Time.deltaTime;
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / moveSeconds));
                transform.position = Vector3.Lerp(fromPos, toPos, k);
                transform.rotation = Quaternion.Slerp(fromRot, targetRot,
                    Mathf.Clamp01(t / turnSeconds));
                yield return null;
            }

            if (!IsGrappled || Captor != next)
                yield break;

            transform.position = toPos;
            transform.rotation = targetRot;
            Debug.Log($"[HandOff] shove kind={kind} dist={distance:F2} t={moveSeconds:F2}s");
        }

        /// <summary>
        /// 罰なしで解放する（相手が倒れた・プレイヤーが倒れた・腹パン演出が終わった）。
        /// </summary>
        public void Release()
        {
            bool wasGrappled = IsGrappled;

            IsGrappled = false;
            _remaining = 0f;
            _suppressRemaining = 0f;
            Captor = null;
            _hugStage = HugStage.Rest;
            _hugTime = 0f;
            _hugPhase = 0f;
            PullPulse = 0f;

            if (wasGrappled)
                Released?.Invoke();
        }

        /// <summary>
        /// 掴まれた瞬間の引き寄せ。相手の体の前に、掴み方に応じた距離まで詰める。
        /// これをしないと、殴り合い用の間合い（1.4m）のまま腕だけ伸びて手が届かない。
        /// </summary>
        void SnapToCaptor(EnemyBrain captor, GrappleKind kind)
        {
            if (captor == null)
                return;

            Vector3 spot = GrabSpot(captor, kind);
            transform.position = spot;

            // 羽交い締めは背中を向けたまま掴まれる（向きは変えない）。
            // 押さえつけ / ベアハッグは正面から掴まれるので、相手の方を向かせる。
            if (kind != GrappleKind.Back)
            {
                Vector3 look = captor.transform.position - spot;
                look.y = 0f;
                if (look.sqrMagnitude > 1e-4f)
                    transform.rotation = Quaternion.LookRotation(look.normalized, Vector3.up);
            }
        }

        /// <summary>
        /// 掴まれる位置（相手から見て、掴み方に応じた距離）。
        /// 突き飛ばしと瞬間的な寄せの両方で同じ計算を使う。
        /// </summary>
        Vector3 GrabSpot(EnemyBrain captor, GrappleKind kind)
        {
            Vector3 dir = transform.position - captor.transform.position;
            dir.y = 0f;

            if (dir.sqrMagnitude < 1e-4f)
            {
                dir = -captor.transform.forward;
                dir.y = 0f;
            }

            if (dir.sqrMagnitude < 1e-4f)
                return transform.position;

            dir.Normalize();

            // 羽交い締めは密着させる。押さえつけは腕を伸ばす余地を残し、
            // ベアハッグは胴を抱えるので少しだけ近い。
            float distance = kind switch
            {
                GrappleKind.Back => BackGrabDistance,
                GrappleKind.BearHug => BearHugDistance,
                _ => GrabDistance
            };
            Vector3 spot = captor.transform.position + dir * distance;
            spot.y = transform.position.y;
            return spot;
        }

        /// <summary>
        /// ベアハッグ中、掴み位置を「密着 → 少し離れる → 密着」と動かす。
        /// ダメージは密着した瞬間（<see cref="IsBearHugContact"/>）だけに入れる。
        /// </summary>
        void TickBearHugBody(float dt)
        {
            if (Captor == null)
                return;

            switch (_hugStage)
            {
                case HugStage.Pull:
                    _hugTime += dt;
                    {
                        float x = Mathf.Clamp01(_hugTime / HugPullSeconds);

                        // 最初の 35% はほとんど動かず（相手が体重を預けて粘る）、
                        // 残りで一気に引き込む。等速で寄せると「滑っている」ように見える。
                        _hugPhase = x < 0.35f
                            ? 0.05f * (x / 0.35f)
                            : 0.05f + 0.95f * Mathf.SmoothStep(
                                0f, 1f, (x - 0.35f) / 0.65f);

                        // 引き寄せの勢いは中盤で最大。密着の瞬間には 0 へ戻る。
                        PullPulse = Mathf.Sin(Mathf.PI * x);

                        if (_hugTime >= HugPullSeconds)
                        {
                            // 引き込みの勢いで少し行き過ぎてから戻る（ぶつかる感じ）。
                            _hugPhase = 1.08f;
                            PullPulse = 0f;
                            _hugStage = HugStage.Contact;
                            _hugTime = 0f;
                        }
                    }
                    break;

                case HugStage.Contact: // 密着（ここでダメージ）
                    _hugTime += dt;
                    _hugPhase = Mathf.MoveTowards(_hugPhase, 1f, dt * 0.8f);
                    PullPulse = Mathf.MoveTowards(PullPulse, 0f, dt * 12f);
                    if (_hugTime >= HugContactSeconds)
                    {
                        _hugStage = HugStage.Release;
                        _hugTime = 0f;
                    }
                    break;

                case HugStage.Release: // 少し離す
                    _hugTime += dt;
                    _hugPhase = 1f - Mathf.SmoothStep(0f, 1f,
                        Mathf.Clamp01(_hugTime / HugReleaseSeconds));
                    PullPulse = Mathf.MoveTowards(PullPulse, 0f, dt * 10f);
                    if (_hugTime >= HugReleaseSeconds)
                    {
                        _hugPhase = 0f;
                        _hugStage = HugStage.Rest;
                        _hugTime = 0f;
                    }
                    break;
            }

            SqueezePulse = Mathf.Clamp01(_hugPhase);

            // 掴み位置の基準（BearHugDistance）から、密着度に応じて前後させる。
            // 方向は「相手から見た彼女」の向きを使い、横へは流れないようにする。
            Vector3 dir = transform.position - Captor.transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-4f)
                dir = -Captor.transform.forward;
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-4f)
                return;
            dir.Normalize();

            float distance = BearHugDistance - HugTightInset * _hugPhase;
            Vector3 spot = Captor.transform.position + dir * distance;
            spot.y = transform.position.y;
            transform.position = spot;
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
            _hugStage = HugStage.Rest;
            _hugTime = 0f;
            _hugPhase = 0f;

            PlaySfx(SfxId.Guard, 0.9f);

            // 振りほどいた側に反撃の隙を作る（掴んでいた側がよろける）
            if (captor != null)
                captor.ForceStagger(CombatConstants.GrappleEscapeStun);

            Debug.Log($"[Grapple] escape inputs={inputs} need={RequiredInputs:F1} t={spent:F2}s");
            Escaped?.Invoke();
        }

        /// <summary>
        /// 押さえつけ（Pin）の時間切れ。後ろ拘束では呼ばない。
        /// 投げや追加ダメージは無く、腹ダメージが上限に達していればうつ伏せに倒れる。
        /// </summary>
        void Fail()
        {
            var captor = Captor;

            int inputs = InputCount;
            Release();
            Stats.ResolveHeldBellyDown();

            // 拘束を解いた直後に、別の敵から即座に追撃されないようにする。
            foreach (var enemy in FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None))
            {
                if (enemy == null || enemy == captor || !enemy.IsAlive)
                    continue;

                if (Vector3.Distance(enemy.transform.position, transform.position) > 6f)
                    continue;

                enemy.HoldFire(CombatConstants.GrappleReleaseGraceSeconds);
            }

            Debug.Log($"[Grapple] release kind={Kind} inputs={inputs} need={RequiredInputs:F1}");

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
