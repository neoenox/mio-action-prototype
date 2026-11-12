using System;
using UnityEngine;

namespace Ryona.Game
{
    public sealed partial class EnemyBrain : MonoBehaviour, IDamageable
    {
        /// <summary>
        /// 距離と乱数で攻撃を選ぶ。直前と同じ攻撃は避け、同じ攻撃の 3 連続は禁止する。
        /// </summary>
        bool TrySelectAttack(float distance, out AttackId id)
        {
            id = AttackId.Jab;

            // 1 回目: 直前と同じ攻撃を除いた候補
            int count = CollectCandidates(distance, excludeLast: true);
            if (count == 0)
            {
                // 2 回目: 2 連続までは許す（3 連続は絶対に作らない）
                if (_hasLast && _repeatCount >= 2)
                {
                    return false;   // 間合い外か、3 連続になる。攻めずに回り込む
                }

                count = CollectCandidates(distance, excludeLast: false);
                if (count == 0)
                    return false;
            }

            float total = 0f;
            for (int i = 0; i < count; i++)
                total += _candidateWeights[i];

            if (total <= 0f)
                return false;

            float roll = UnityEngine.Random.value * total;
            int pick = count - 1;
            for (int i = 0; i < count; i++)
            {
                roll -= _candidateWeights[i];
                if (roll <= 0f)
                {
                    pick = i;
                    break;
                }
            }

            id = _candidateIds[pick];
            return true;
        }

        /// <summary>
        /// 押さえつけの準備ができているとき、彼女の正面へ回り込む。
        /// 背後にいると <see cref="CanStartPin"/> が通らず、羽交い締め（Back）ばかりになって
        /// 「正面から掴まれない」状態になる（実測）。正面 1.0m まで歩いてから攻撃抽選に戻す。
        /// </summary>
        bool TryApproachForPin()
        {
            if (!CanPin && !_pinOrder)
                return false;

            if (IsPinning || Player == null || Player.IsDead)
                return false;

            if (_grapple == null)
                _grapple = Player.GetComponent<PlayerGrapple>();
            if (_grapple == null || _grapple.IsGrappled)
                return false;

            // クールダウン中は回り込まない（ふだんどおり戦う）
            if (!_pinOrder && Time.time < _lastPinAt + CombatConstants.PinCooldown)
                return false;

            // すでに正面にいれば、通常の攻撃抽選（CanStartPin）に任せる
            Vector3 toEnemy = transform.position - Player.transform.position;
            toEnemy.y = 0f;
            Vector3 playerForward = Player.transform.forward;
            playerForward.y = 0f;
            if (toEnemy.sqrMagnitude > 1e-4f && playerForward.sqrMagnitude > 1e-4f &&
                Vector3.Dot(playerForward.normalized, toEnemy.normalized) > 0.9f)
                return false;

            Vector3 front = playerForward.sqrMagnitude > 1e-4f ? playerForward.normalized : Vector3.right;
            Vector3 target = Player.transform.position + front * 1.0f;
            Vector3 dir = target - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.04f)
                return false;

            Move(dir.normalized, MoveSpeed * 0.9f);
            return true;
        }

        /// <summary>この敵が、プレイヤーの背中側にいるか。</summary>
        bool IsBehindPlayer()
        {
            if (Player == null)
                return false;

            return Vector3.Dot(Player.transform.forward,
                transform.position - Player.transform.position) < -0.25f;
        }

        int CollectCandidates(float distance, bool excludeLast)
        {
            int count = 0;

            // すでに誰かが掴んでいるなら、掴みは選ばない（二重に掴めない）
            var grapple = Player != null ? Player.GetComponent<PlayerGrapple>() : null;
            bool alreadyGrappled = grapple != null && grapple.IsGrappled;

            for (int i = 0; i < AttackCount; i++)
            {
                var id = (AttackId)i;

                if (distance > RangeOf(id))
                    continue;

                if (id == AttackId.Grab && (alreadyGrappled || Time.time < _grabReadyAt))
                    continue;

                // 掴みは背後からだけ（羽交い締め）。正面からの掴みは無い。
                if (id == AttackId.Grab && !IsBehindPlayer())
                    continue;

                if (excludeLast && _hasLast && _lastAttack == id)
                    continue;

                _candidateIds[count] = id;
                _candidateWeights[count] = WeightOf(id);
                count++;
            }

            return count;
        }

        /// <summary>重い攻撃ほど出しにくくして、軽い刻みが主体になるようにする。</summary>
        static float WeightOf(AttackId id)
        {
            switch (id)
            {
                case AttackId.Jab: return 1.10f;
                case AttackId.Heavy: return 0.85f;
                case AttackId.Break: return 0.70f;
                case AttackId.Grab: return 0.75f;
                default: return 0f;
            }
        }

        internal void BeginWindup(AttackId id) => BeginAttackWindup(id, fromThrow: false);

        void BeginAttackWindup(AttackId id, bool fromThrow)
        {
            _currentAttack = id;
            ClearTransientAttackState();
            GrabAdvance = 0f;

            AttackData data = DataOf(id);
            if (data != null)
                data.damage = Mathf.Max(0f, DamageOf(id));   // インスペクタの値をそのまま反映する

            _run = new EnemyAttackRunner
            {
                Id = id,
                Data = data,
                Range = RangeOf(id),
                Windup = fromThrow ? 0.12f : Mathf.Max(0.05f, WindupOf(id)),
                FromHeldThrow = fromThrow
            };

            _timer = _run.Windup;
            WindupProgress = 0f;

            // 狙いを決める。崩しは腹、素早い一撃は顔寄り、重い一撃は腹寄り。
            Zone = ForceZone ?? id switch
            {
                AttackId.Jab => UnityEngine.Random.value < JabFaceChance ? PunchZone.Face : PunchZone.Body,
                AttackId.Heavy => UnityEngine.Random.value < HeavyFaceChance ? PunchZone.Face : PunchZone.Body,
                AttackId.Break => PunchZone.Body,
                _ => PunchZone.Face
            };

            if (_hasLast && _lastAttack == id)
            {
                _repeatCount++;
            }
            else
            {
                _lastAttack = id;
                _repeatCount = 1;
                _hasLast = true;
            }

            Phase = EnemyPhase.Windup;

            // 攻撃は画面の左右（ベルト軸）へ出す。旋回の途中で殴り始めても、
            // 予備動作から振り抜きまで斜めを向いたまま固まらないよう、ここで向きを揃える。
            SnapToLaneFacing();

            // 投げから続けて打つ一発目は、伸ばした手からすぐ拳へつなぐ。
            if (fromThrow)
                return;

            ShowTelegraph();
            ApplyTelegraphVisual(0f);

            // 予備動作の合図。背中側から来ても音で分かるようにする
            PlaySfx(id == AttackId.Heavy ? SfxId.TellHeavy : SfxId.Tell, id == AttackId.Heavy ? 0.75f : 0.55f);
        }

        /// <summary>
        /// この敵の効果音。位置を持たせて「どこから鳴ったか」が分かるようにする。
        /// 2D の共通音源だと、後ろから来たのか前から来たのか耳で区別できない。
        /// </summary>
        void PlaySfx(SfxId id, float volume)
        {
            var sfx = ProceduralSfx.Instance;
            if (sfx == null)
                return;

            if (_voice == null)
            {
                // 胸の高さに置く。足元だと低く聞こえて方向が掴みにくい
                var voiceGo = new GameObject("Voice");
                voiceGo.transform.SetParent(transform, false);
                voiceGo.transform.localPosition = new Vector3(0f, 1.15f, 0f);

                _voice = voiceGo.AddComponent<AudioSource>();
                _voice.playOnAwake = false;
                _voice.spatialBlend = 1f;                  // 3D。左右と距離が付く
                _voice.dopplerLevel = 0f;                  // 動き回るので音程が揺れないようにする
                _voice.rolloffMode = AudioRolloffMode.Linear;
                _voice.minDistance = 1.6f;
                _voice.maxDistance = 22f;
                _voice.volume = 1f;

                Debug.Log($"[Sfx] 3D音源を作成 min={_voice.minDistance} max={_voice.maxDistance} " +
                          $"listener={(UnityEngine.Object.FindFirstObjectByType<AudioListener>() != null)}");
            }

            sfx.PlayOn(_voice, id, volume);
        }

        AttackData DataOf(AttackId id)
        {
            switch (id)
            {
                case AttackId.Jab: return _jab;
                case AttackId.Heavy: return _heavy;
                case AttackId.Break: return _break;
                case AttackId.Grab: return _grab;
                default: return _jab;
            }
        }

        float RangeOf(AttackId id)
        {
            switch (id)
            {
                // 見た目の拳が届く距離に合わせる。シーンに保存された値（1.45 / 1.70 / 1.55）を
                // そのまま使うと、届かない距離から振って空振りする（実測: Jab は 1.25m まで）。
                case AttackId.Jab: return Mathf.Min(
                    JabRange > 0f ? JabRange : AttackRange, MaxJabReach);
                case AttackId.Heavy: return Mathf.Min(
                    HeavyRange > 0f ? HeavyRange : AttackRange, MaxHeavyReach);
                case AttackId.Break: return Mathf.Min(
                    BreakRange > 0f ? BreakRange : AttackRange, MaxBreakReach);
                case AttackId.Grab:
                    // 掴みは「攻撃を始める間合い（EngageDistance）」で必ず選べるようにする。
                    // GrabRange が EngageDistance より狭いと、敵が間合いに入った瞬間に
                    // 掴みが候補から外れて出番がなくなる。
                    return Mathf.Max(GrabRange > 0f ? GrabRange : AttackRange, EngageDistance);
                default: return AttackRange;
            }
        }

        float WindupOf(AttackId id)
        {
            switch (id)
            {
                case AttackId.Jab: return JabSeconds > 0f ? JabSeconds : WindupSeconds;
                case AttackId.Heavy: return HeavySeconds > 0f ? HeavySeconds : WindupSeconds;
                case AttackId.Break: return BreakSeconds > 0f ? BreakSeconds : WindupSeconds;
                case AttackId.Grab: return GrabSeconds > 0f ? GrabSeconds : WindupSeconds;
                default: return WindupSeconds;
            }
        }

        float DamageOf(AttackId id)
        {
            switch (id)
            {
                case AttackId.Jab: return JabDamage;
                case AttackId.Heavy: return HeavyDamage;
                case AttackId.Break: return BreakDamage;
                case AttackId.Grab: return GrabDamage;
                default: return 0f;
            }
        }

        /// <summary>AttackLibrary の定義を元に、ダメージだけ持ち替えた 3 種を作る。</summary>
        void BuildAttacks()
        {
            _jab = AttackLibrary.Light();
            _jab.id = "enemy_jab";
            _jab.damage = Mathf.Max(0f, JabDamage);

            _heavy = AttackLibrary.Heavy();
            _heavy.id = "enemy_heavy";
            _heavy.damage = Mathf.Max(0f, HeavyDamage);

            _break = AttackLibrary.BreakAttack();
            _break.id = "enemy_break";
            _break.damage = Mathf.Max(0f, BreakDamage);

            // 掴み。ダメージは低く、当たったらレバガチャ（PlayerGrapple）に移る
            _grab = AttackLibrary.GrappleAttack();
            _grab.id = "enemy_grab";
            _grab.damage = Mathf.Max(0f, GrabDamage);
        }

        // --- 被弾・死亡 ----------------------------------------------------------

    }
}
