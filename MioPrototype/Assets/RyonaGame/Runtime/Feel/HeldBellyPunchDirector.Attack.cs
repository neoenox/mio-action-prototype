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

            PunchZone zone = openingThrow ? PunchZone.Body : _comboZone;

            // 顔面パンチは拳を振り抜く距離が要る。腹パンより一歩下がって立たせ、
            // 腕の振り（弧）が見えるようにする。近すぎると腕が畳まれて「触っただけ」になる。
            float standOff = openingThrow
                ? 0.82f
                : Mathf.Max(0.70f, zone == PunchZone.Face ? FaceAttackerDistance : AttackerDistance);

            // ベルトなので奥行き（z）は EnemyBrain.LateUpdate が毎フレーム彼女に合わせる。
            // ここで側方（z）へずらしても次のフレームで戻され、「ずらした前提の向き」だけが
            // 残って腹から 35° 外れる（実測: 拳が 0.33m 届かず空振り）。
            // 立ち位置は前後（x）だけで決め、向きは実際に立つ位置から腹へ向ける。
            // 下限はカプセルが重ならない距離（彼女 0.30 + 敵 0.40 = 0.70m）に置く。
            // 0.8 に固定していたときは AttackerDistance が効かず、拳が届く位置まで
            // 踏み込めなかった（実測の立ち位置は常に 0.8m）。
            Vector3 spot = anchor + forward * standOff;
            spot.z = _stats.transform.position.z;
            spot.y = best.transform.position.y;

            Vector3 toPlayer = anchor - spot;
            toPlayer.y = 0f;
            Quaternion face = toPlayer.sqrMagnitude > 1e-4f
                ? Quaternion.LookRotation(toPlayer.normalized, Vector3.up)
                : best.transform.rotation;

            // 歩き始める前に攻撃トークンを確保する。取れなければ位置も動かさず、
            // 次の機会（0.3 秒後）に選び直す。
            if (!best.TryReserveTagPunchToken())
            {
                HasAttacker = false;
                SetDreadTarget(null);
                return false;
            }

            // 実際に攻撃が始まる（トークンを取れた）ときだけ「見る相手」にする。
            // ここより前に立てると、発注に失敗した敵を不安そうに見続けてしまう。
            SetDreadTarget(best);

            float moveDistance = Vector3.Distance(best.transform.position, spot);
            if (moveDistance <= 0.05f)
            {
                best.transform.position = spot;
                best.transform.rotation = face;
                bool started = BeginOrderedAttack(best, zone, openingThrow);
                if (!started)
                {
                    SetDreadTarget(null);
                    return false;
                }

                if (openingThrow)
                    _thrower = best;
                return true;
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
            if (openingThrow)
                return enemy.TryStartHeldThrow(positioned: true);

            bool? punchLeft = zone == PunchZone.Face ? ChooseFacePunchSide(enemy) : null;
            float power = zone == PunchZone.Face ? FacePunchPower(_hits) : 1f;
            return enemy.TryStartTagPunch(zone, allowRecover: true, holdPoseSeconds: FistHoldSeconds,
                forceLeft: punchLeft, forcePower: power);
        }

        /// <summary>
        /// 5 連撃の強さ。1〜2 発目は軽く、3 発目から強く、5 発目（決め）で一番大きく踏み込む。
        /// 同じ強さの連打だと「作業」に見えるので、段を作る。
        /// </summary>
        float FacePunchPower(int hitIndex)
        {
            if (hitIndex >= ComboHitsTotal - 1)
                return 1.25f;
            if (hitIndex >= 2)
                return 1.05f;
            return 0.92f;
        }

        /// <summary>
        /// 投げた敵がそのまま一発目を打つとき、顔面パンチなら拳を振る距離まで下がらせる。
        /// 投げの距離（0.82m）のままだと腕が畳まれ、「触っただけ」のパンチになる。
        /// </summary>
        void PlaceForOpeningPunch(EnemyBrain puncher, PunchZone zone)
        {
            if (puncher == null || _stats == null || zone != PunchZone.Face)
                return;

            Vector3 forward = _stats.transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-4f)
                forward = Vector3.right;
            forward.Normalize();

            Vector3 spot = _stats.transform.position + forward * FaceAttackerDistance;
            spot.y = puncher.transform.position.y;
            Vector3 to = _stats.transform.position - spot;
            to.y = 0f;
            puncher.transform.position = spot;
            if (to.sqrMagnitude > 1e-4f)
                puncher.transform.rotation = Quaternion.LookRotation(to.normalized, Vector3.up);
        }

        /// <summary>
        /// 顔面パンチを打つ腕。1 発目はカメラから遠い腕にして彼女の顔を隠さない。
        /// 2 発目からは左右交互にして、連打が「同じ腕の連発」に見えないようにする。
        /// カメラはコンボ中に寄るので、コンボ開始時の構図で「遠い側」を決めておく。
        /// </summary>
        bool ChooseFacePunchSide(EnemyBrain enemy)
        {
            // 彼は彼女と向かい合う。カメラが彼女の右側なら、カメラ側の腕は彼の左腕。
            // 遠い腕（彼の右腕）から打ち始める。
            bool farSideLeft = !_cameraOnHerRight;
            return farSideLeft ^ (_hits % 2 == 1);
        }

        /// <summary>コンボ開始時のカメラが、彼女の右側にあるか。</summary>
        bool ComputeCameraOnHerRight()
        {
            Camera cam = Camera.main;
            if (cam == null || _stats == null)
                return false;

            Vector3 toCam = cam.transform.position - _stats.transform.position;
            toCam.y = 0f;
            return toCam.sqrMagnitude > 1e-4f &&
                Vector3.Dot(toCam.normalized, _stats.transform.right) > 0f;
        }

        bool CanContinuePlacement(EnemyBrain enemy, EnemyBrain captor, bool openingThrow)
        {
            return enemy != null && enemy.isActiveAndEnabled && enemy.IsAlive &&
                _grapple != null && _grapple.IsGrappled && !_grapple.IsHandingOff &&
                _grapple.Kind == GrappleKind.Back && _grapple.Captor == captor &&
                (openingThrow ? _thrower == enemy : _phase == Phase.Combo);
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

        /// <summary>連撃の狙いを開始時に一度だけ決める。</summary>
        PunchZone ChooseComboZone()
        {
            return FacePunchChance >= 1f || (FacePunchChance > 0f && UnityEngine.Random.value < FacePunchChance)
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
