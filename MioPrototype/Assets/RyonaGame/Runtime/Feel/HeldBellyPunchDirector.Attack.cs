using UnityEngine;

namespace Ryona.Game
{
    /// <summary>
    /// 投げ役と腹パン役の配置・発注。
    /// </summary>
    public sealed partial class HeldBellyPunchDirector
    {
        bool TryOrderAttack(bool openingThrow)
        {
            var captor = _grapple.Captor;
            EnemyBrain best = null;
            float bestDistance = Mathf.Max(1f, AttackerSearchRange);

            // 立たせる基準は「拳が狙う腹の座標」にする。root を基準にすると、
            // 腹とのずれのぶんだけ向きが狂い、拳が 0.3m 外れる（実測: gap 0.33）。
            Vector3 anchor = openingThrow ? _stats.transform.position : BodyAnchor();

            foreach (var enemy in FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None))
            {
                if (enemy == null || !enemy.isActiveAndEnabled || !enemy.IsAlive || enemy.IsRising ||
                    enemy.IsFlanking || enemy.IsHandOffLocked || enemy.IsHolding || enemy == captor)
                    continue;

                // 予備動作・攻撃・怯みの途中なら、位置を動かすと攻撃が壊れる。次の機会を待つ。
                if (enemy.Phase == EnemyPhase.Windup || enemy.Phase == EnemyPhase.Attack ||
                    enemy.Phase == EnemyPhase.Stagger)
                    continue;

                float distance = Vector3.Distance(enemy.transform.position, anchor);
                if (distance >= bestDistance)
                    continue;

                // 彼女の背後側から回り込むと、前へ出す移動が体を突き抜ける。
                // 正面寄りにいる敵だけを腹パン役にする。
                Vector3 fromPlayer = enemy.transform.position - _stats.transform.position;
                fromPlayer.y = 0f;
                float front = Vector3.Dot(_stats.transform.forward, fromPlayer.normalized);
                if (front < (openingThrow ? 0.55f : -0.2f))
                    continue;

                best = enemy;
                bestDistance = distance;
            }

            if (best == null)
            {
                HasAttacker = false;
                return false;
            }

            HasAttacker = true;

            Vector3 forward = _stats.transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-4f)
                forward = Vector3.right;
            forward.Normalize();

            // ベルトなので奥行き（z）は EnemyBrain.LateUpdate が毎フレーム彼女に合わせる。
            // ここで側方（z）へずらしても次のフレームで戻され、「ずらした前提の向き」だけが
            // 残って腹から 35° 外れる（実測: 拳が 0.33m 届かず空振り）。
            // 立ち位置は前後（x）だけで決め、向きは実際に立つ位置から腹へ向ける。
            // 下限はカプセルが重ならない距離（彼女 0.30 + 敵 0.40 = 0.70m）に置く。
            // 0.8 に固定していたときは AttackerDistance が効かず、拳が届く位置まで
            // 踏み込めなかった（実測の立ち位置は常に 0.8m）。
            Vector3 spot = anchor + forward * (openingThrow ? 0.82f : Mathf.Max(0.70f, AttackerDistance));
            spot.z = _stats.transform.position.z;
            spot.y = best.transform.position.y;

            Vector3 toPlayer = anchor - spot;
            toPlayer.y = 0f;
            Quaternion face = toPlayer.sqrMagnitude > 1e-4f
                ? Quaternion.LookRotation(toPlayer.normalized, Vector3.up)
                : best.transform.rotation;

            PunchZone zone = openingThrow || _hits == 0 ? PunchZone.Body : NextZone();

            // 歩き始める前に攻撃トークンを確保する。取れなければ位置も動かさず、
            // 次の機会（0.3 秒後）に選び直す。
            if (!best.TryReserveTagPunchToken())
            {
                HasAttacker = false;
                return false;
            }

            float moveDistance = Vector3.Distance(best.transform.position, spot);
            if (moveDistance <= 0.05f)
            {
                best.transform.position = spot;
                best.transform.rotation = face;
                bool started = BeginOrderedAttack(best, zone, openingThrow);
                if (started && openingThrow)
                    _thrower = best;
                return started;
            }

            // 遠くから瞬間移動させず、短く歩かせてから打たせる。
            // 位置合わせの間は AI を止め、体が勝手に動いて拳の位置がずれないようにする。
            best.LockForHandOff(openingThrow ? 0.9f : 0.8f, releaseToken: false);
            if (openingThrow)
                _thrower = best;
            StartCoroutine(PlaceThenAttack(best, captor, spot, face, zone, moveDistance, openingThrow));
            return true;
        }

        bool BeginOrderedAttack(EnemyBrain enemy, PunchZone zone, bool openingThrow)
        {
            return openingThrow ? enemy.TryStartHeldThrow(positioned: true)
                : enemy.TryStartTagPunch(zone, allowRecover: true, holdPoseSeconds: FistHoldSeconds);
        }

        bool CanContinuePlacement(EnemyBrain enemy, EnemyBrain captor, bool openingThrow)
        {
            return enemy != null && enemy.isActiveAndEnabled && enemy.IsAlive &&
                _grapple != null && _grapple.IsGrappled && !_grapple.IsHandingOff &&
                _grapple.Kind == GrappleKind.Back && _grapple.Captor == captor &&
                (openingThrow ? _thrower == enemy : _comboActive);
        }

        /// <summary>攻撃役を短く歩かせてから、投げまたはパンチを指示する。</summary>
        System.Collections.IEnumerator PlaceThenAttack(EnemyBrain enemy, EnemyBrain captor, Vector3 spot,
            Quaternion face, PunchZone zone, float distance, bool openingThrow)
        {
            Vector3 from = enemy.transform.position;
            Quaternion fromRot = enemy.transform.rotation;
            float seconds = Mathf.Clamp(0.08f + distance * 0.10f, 0.10f, 0.30f);
            float t = 0f;

            while (t < seconds)
            {
                t += Time.deltaTime;
                if (!CanContinuePlacement(enemy, captor, openingThrow))
                    yield break;

                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / seconds));
                enemy.transform.position = Vector3.Lerp(from, spot, k);
                enemy.transform.rotation = Quaternion.Slerp(fromRot, face, k);
                yield return null;
            }

            if (!CanContinuePlacement(enemy, captor, openingThrow))
                yield break;

            enemy.transform.position = spot;
            enemy.transform.rotation = face;

            // 位置に着いても、攻撃トークンが埋まっていて発注に失敗することがある。
            // すぐ諦めると「1 発目が空振り」になるので、少しだけ順番待ちする。
            float orderUntil = Time.time + 0.5f;
            bool started = false;
            while (!started && Time.time < orderUntil)
            {
                if (!CanContinuePlacement(enemy, captor, openingThrow))
                    yield break;

                started = BeginOrderedAttack(enemy, zone, openingThrow);
                if (!started)
                    yield return null;
            }

            if (started)
            {
                // 位置合わせに使った時間をヒット待ちの残りから差し引かない。
                if (!openingThrow)
                    _hitTimeout = Mathf.Max(_hitTimeout, 1.5f);
                Debug.Log($"[HeldPunch] placed {enemy.name} dist={distance:F2}m t={seconds:F2}s");
            }
            else
            {
                Debug.Log($"[HeldPunch] place order failed {enemy.name} phase={enemy.Phase} " +
                          $"token={enemy.HasAttackToken} dist={distance:F2}m");
            }
        }

        /// <summary>次の一発の狙い。腹パンと顔面パンチを毎回ランダムに選ぶ。</summary>
        PunchZone NextZone()
        {
            return UnityEngine.Random.value < Mathf.Clamp01(FacePunchChance)
                ? PunchZone.Face
                : PunchZone.Body;
        }

        /// <summary>
        /// 立ち位置と向きの基準。拳が狙うのと同じ「腹の座標」を使う。
        /// root を基準にすると、腹とのずれのぶんだけ腹パン役の向きが狂う。
        /// </summary>
        Vector3 BodyAnchor()
        {
            if (_stats == null)
                return Vector3.zero;

            Vector3 belly = EnemyBrain.PunchTarget(_stats, PunchZone.Body, 0.22f);
            return new Vector3(belly.x, _stats.transform.position.y, belly.z);
        }

    }
}
