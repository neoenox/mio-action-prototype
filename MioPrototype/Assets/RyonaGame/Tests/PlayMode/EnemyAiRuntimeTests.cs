using System.Collections;
using NUnit.Framework;
using Ryona.Game;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Ryona.Tests
{
    /// <summary>
    /// 敵 AI の「フェアネス」と「複数敵の協調」の回帰テスト。
    /// 仕様書 §5 の「攻撃前に必ず予備動作を置く」と、
    /// 複数敵が一斉に殴ってこないことを機械的に確かめる。
    /// </summary>
    public sealed class EnemyAiRuntimeTests
    {
        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
        }

        static IEnumerator LoadPrototype()
        {
            yield return TestScene.LoadPrototype();
        }

        [UnityTest]
        public IEnumerator 敵が予備動作を挟んでから攻撃してくる()
        {
            yield return LoadPrototype();

            var brains = Object.FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None);
            Assert.Greater(brains.Length, 1, "敵が複数配置されていない");

            bool sawWindup = false;
            bool sawAttack = false;

            // ヒットストップで timeScale が 0 になるので、実時間で測る
            float elapsed = 0f;
            int frames = 0;
            while (elapsed < 10f && frames < 3000 && !(sawWindup && sawAttack))
            {
                foreach (var b in brains)
                {
                    if (b == null)
                        continue;
                    if (b.Phase == EnemyPhase.Windup)
                        sawWindup = true;
                    if (b.Phase == EnemyPhase.Attack)
                        sawAttack = true;
                }

                elapsed += Time.unscaledDeltaTime;
                frames++;
                yield return null;
            }

            Assert.IsTrue(sawWindup, "予備動作を出さずに攻撃している（フェアネス違反）");
            Assert.IsTrue(sawAttack, "予備動作のあとに攻撃してこない");
        }

        [UnityTest]
        public IEnumerator 通常戦闘で敵が近づいてプレイヤーに命中する()
        {
            yield return LoadPrototype();
            var player = Object.FindFirstObjectByType<PlayerStats>();
            var enemies = Object.FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None);
            Assert.GreaterOrEqual(enemies.Length, 2);
            for (int i = 1; i < enemies.Length; i++) enemies[i].enabled = false;
            var enemy = enemies[0];
            enemy.FeintChance = 0f;
            enemy.transform.position = player.transform.position + Vector3.right * 2.2f;
            enemy.transform.rotation = Quaternion.LookRotation(Vector3.left);
            float hp = player.State.Hp;
            bool sawAttack = false;
            float deadline = Time.realtimeSinceStartup + 10f;
            while (Time.realtimeSinceStartup < deadline && player.State.Hp >= hp)
            {
                sawAttack |= enemy.Phase == EnemyPhase.Attack;
                yield return null;
            }
            Assert.IsTrue(sawAttack, $"Enemy did not strike: phase={enemy.Phase} distance={Vector3.Distance(enemy.transform.position, player.transform.position):F2}");
            Assert.Less(player.State.Hp, hp, "The natural attack sequence must damage a stationary player");
        }

        [UnityTest]
        public IEnumerator 各パンチが通常の間合いで命中する()
        {
            foreach (var attack in new[] { EnemyBrain.AttackId.Jab, EnemyBrain.AttackId.Heavy, EnemyBrain.AttackId.Break })
            {
                yield return LoadPrototype();
                var player = Object.FindFirstObjectByType<PlayerStats>();
                var enemies = Object.FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None);
                for (int i = 1; i < enemies.Length; i++) enemies[i].enabled = false;
                var enemy = enemies[0];
                enemy.FeintChance = 0f;
                enemy.transform.position = player.transform.position + Vector3.right * 1.03f;
                enemy.transform.rotation = Quaternion.LookRotation(Vector3.left);
                enemy.BeginWindup(attack);
                float hp = player.State.Hp;
                float deadline = Time.realtimeSinceStartup + 2.5f;
                while (Time.realtimeSinceStartup < deadline && player.State.Hp >= hp && enemy.Phase != EnemyPhase.Recover)
                    yield return null;
                Assert.Less(player.State.Hp, hp, $"{attack} missed a stationary player at normal range");
            }
        }

        [UnityTest]
        public IEnumerator パンチ命中時に拳が体の表面まで届く()
        {
            // 顔と腹のどちらを狙っても、拳はその座標へ届く（ホーミング）
            foreach (var zone in new[] { PunchZone.Face, PunchZone.Body })
            foreach (var attack in new[] { EnemyBrain.AttackId.Jab, EnemyBrain.AttackId.Heavy, EnemyBrain.AttackId.Break })
            {
                yield return LoadPrototype();
                var player = Object.FindFirstObjectByType<PlayerStats>();
                var enemies = Object.FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None);
                foreach (var e in enemies) e.enabled = false;
                var enemy = enemies[0];
                enemy.ForceZone = zone;
                var slot = player.GetComponentInChildren<VrmCharacterSlot>();
                if (slot == null) slot = Object.FindFirstObjectByType<VrmCharacterSlot>();
                float loadDeadline = Time.realtimeSinceStartup + 8f;
                while ((slot == null || !slot.Loaded || slot.Instance == null) && Time.realtimeSinceStartup < loadDeadline)
                    yield return null;
                Assert.IsTrue(slot != null && slot.Loaded, "Player VRM must be loaded for the contact measurement");
                var rig = slot.Instance.GetComponent<ProceduralRig>();
                Assert.IsNotNull(rig);
                Assert.IsNotNull(rig.Chest);
                enemy.enabled = true;
                enemy.FeintChance = 0f;
                enemy.transform.position = player.transform.position + Vector3.right * 1.03f;
                enemy.transform.rotation = Quaternion.LookRotation(Vector3.left);
                var figure = enemy.GetComponent<EnemyFigure>();
                float distanceAtHit = -1f;
                player.HitReceived += result =>
                    distanceAtHit = Vector3.Distance(figure.Fist(false).position, EnemyBrain.PunchTarget(player, zone));
                enemy.BeginWindup(attack);
                float deadline = Time.realtimeSinceStartup + 3f;
                while (Time.realtimeSinceStartup < deadline && distanceAtHit < 0f) yield return null;
                Assert.GreaterOrEqual(distanceAtHit, 0f, $"{attack} to the {zone} did not hit");
                Assert.Less(distanceAtHit, 0.20f, $"{attack} fist is visibly short of the {zone}");
                Assert.AreEqual(zone, player.LastHitZone, "the hit must record where it landed");
            }
        }

        [UnityTest]
        public IEnumerator 敵は背を向けたまま攻撃を始めない()
        {
            yield return LoadPrototype();
            var player = Object.FindFirstObjectByType<PlayerStats>();
            var enemy = Object.FindFirstObjectByType<EnemyBrain>();
            enemy.transform.position = player.transform.position + Vector3.right * 1.0f;
            enemy.transform.rotation = Quaternion.LookRotation(Vector3.right);
            enemy.FeintChance = 0f;
            bool started = enemy.TryBeginAttack(1f);
            Assert.IsFalse(started, "Enemy should turn toward the player before beginning an attack");
            foreach (var other in Object.FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None))
                if (other != enemy) other.enabled = false;
            float hp = player.State.Hp;
            float deadline = Time.realtimeSinceStartup + 10f;
            while (Time.realtimeSinceStartup < deadline && player.State.Hp >= hp)
                yield return null;
            Assert.Less(player.State.Hp, hp, "Enemy should turn around and eventually hit the player");
        }

        /// <summary>
        /// 攻撃はベルトの左右（画面の左右）へ出す。彼女が目の前を通り過ぎた直後など、
        /// 旋回の途中（26° 以内）で殴り始めても、斜めを向いたまま振り抜かない。
        /// </summary>
        [UnityTest]
        public IEnumerator 攻撃の向きはベルトの左右へ揃う()
        {
            yield return LoadPrototype();
            var player = Object.FindFirstObjectByType<PlayerStats>();
            var brains = Object.FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None);
            for (int i = 1; i < brains.Length; i++) brains[i].enabled = false;
            var enemy = brains[0];
            enemy.FeintChance = 0f;
            enemy.transform.position = player.transform.position + Vector3.right * 1.0f;

            // 旋回の途中で予備動作に入る状況（レーンから 20° ずれて向いている）。
            Vector3 toPlayer = player.transform.position - enemy.transform.position;
            toPlayer.y = 0f;
            enemy.transform.rotation =
                Quaternion.LookRotation(toPlayer.normalized, Vector3.up) * Quaternion.Euler(0f, 20f, 0f);
            Assert.Greater(FlatAngle(enemy.transform.forward, toPlayer), 10f, "前提: 斜めを向いている");

            enemy.BeginWindup(EnemyBrain.AttackId.Jab);

            Vector3 to = player.transform.position - enemy.transform.position;
            Assert.Less(FlatAngle(enemy.transform.forward, to), 2f,
                "予備動作に入った瞬間、向きが左右へ揃う");

            float strikeErr = -1f;
            float deadline = Time.realtimeSinceStartup + 3f;
            while (Time.realtimeSinceStartup < deadline && enemy.Phase != EnemyPhase.Recover)
            {
                if (enemy.Phase == EnemyPhase.Attack)
                {
                    to = player.transform.position - enemy.transform.position;
                    strikeErr = FlatAngle(enemy.transform.forward, to);
                }
                yield return null;
            }

            Assert.GreaterOrEqual(strikeErr, 0f, "攻撃の判定フレームを見られなかった");
            Assert.Less(strikeErr, 2f, "振り抜きも左右を向く");
        }

        static float FlatAngle(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            if (a.sqrMagnitude < 1e-6f || b.sqrMagnitude < 1e-6f)
                return 0f;
            return Vector3.Angle(a.normalized, b.normalized);
        }

        [UnityTest]
        public IEnumerator 後ろ拘束は時間切れで解けず振りほどくと敵が攻撃を再開する()
        {
            yield return LoadPrototype();
            var player = Object.FindFirstObjectByType<PlayerStats>();
            var grapple = player.GetComponent<PlayerGrapple>();
            var enemies = Object.FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None);
            for (int i = 1; i < enemies.Length; i++) enemies[i].enabled = false;
            var holder = enemies[0];
            holder.FeintChance = 0f;
            player.transform.rotation = Quaternion.LookRotation(Vector3.right);
            holder.transform.position = player.transform.position + Vector3.left;
            holder.transform.rotation = Quaternion.LookRotation(Vector3.right);
            holder.QueueGrab();
            float deadline = Time.realtimeSinceStartup + 20f;
            while (!grapple.IsGrappled && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsTrue(grapple.IsGrappled, "Enemy should grab");

            // 後ろ拘束は時間切れで解けない（投げも無い）
            yield return new WaitForSeconds(3.4f);
            Assert.IsTrue(grapple.IsGrappled, "Back hold must not time out");
            Assert.IsFalse(grapple.HasTimeLimit);

            // 振りほどくと拘束者がよろけ、その後で攻撃を再開する
            grapple.Escape();
            yield return null;
            Assert.IsFalse(grapple.IsGrappled, "Escape must end the hold");

            deadline = Time.realtimeSinceStartup + 12f;
            bool sawPunch = false;
            bool landedPunch = false;
            player.HitReceived += result =>
            {
                if (player.LastAttacker != null)
                {
                    var attacker = player.LastAttacker.GetComponent<EnemyBrain>();
                    if (attacker != null && attacker.CurrentAttack != EnemyBrain.AttackId.Grab && result.Damage > 0f)
                        landedPunch = true;
                }
            };
            while (Time.realtimeSinceStartup < deadline && !landedPunch && !player.IsDead)
            {
                sawPunch |= holder.Phase == EnemyPhase.Attack && holder.CurrentAttack != EnemyBrain.AttackId.Grab;
                yield return null;
            }
            Assert.IsTrue(sawPunch, $"No punch after escape: phase={holder.Phase} distance={Vector3.Distance(holder.transform.position, player.transform.position):F2} holding={holder.IsHolding} token={holder.HasAttackToken}");
            Assert.IsTrue(landedPunch, "Enemy must land a punch after the escape");
        }

        [UnityTest]
        public IEnumerator 奥の敵は手前の敵の休み中に前へ出て攻撃する()
        {
            yield return LoadPrototype();
            var player = Object.FindFirstObjectByType<PlayerStats>();
            var enemies = Object.FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None);
            Assert.GreaterOrEqual(enemies.Length, 3);

            var lead = enemies[0];
            var waiting = enemies[1];
            var opposite = enemies[2];
            player.transform.rotation = Quaternion.LookRotation(Vector3.right);
            lead.transform.position = player.transform.position + Vector3.left * 1.25f;
            waiting.transform.position = player.transform.position + Vector3.left * 2.1f;
            opposite.transform.position = player.transform.position + Vector3.right * 1.25f;
            lead.HoldFire(6f);
            opposite.HoldFire(6f);
            // 反対側が空いた場合の回り込みと区別するため、味方の配置を保つ。
            opposite.MoveSpeed = 0f;
            waiting.FeintChance = 0f;
            waiting.CanPin = false;

            yield return new WaitForSeconds(0.65f);
            Assert.IsFalse(waiting.IsFlanking, "味方が休むたびに入れ替わらない");

            float deadline = Time.realtimeSinceStartup + 3f;
            while (waiting.Phase != EnemyPhase.Windup && Time.realtimeSinceStartup < deadline)
                yield return null;

            Assert.AreEqual(EnemyPhase.Windup, waiting.Phase, "手前の味方を待ち続けず攻撃する");
            Assert.Greater(waiting.transform.position.x, lead.transform.position.x);
            Assert.Less(waiting.transform.position.x, player.transform.position.x);
        }

        [UnityTest]
        public IEnumerator 両側の敵はそれぞれ間合いに入れば同時に攻撃を始める()
        {
            yield return LoadPrototype();
            var player = Object.FindFirstObjectByType<PlayerStats>();
            var enemies = Object.FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None);
            Assert.GreaterOrEqual(enemies.Length, 2);

            player.transform.rotation = Quaternion.LookRotation(Vector3.right);
            for (int i = 0; i < 2; i++)
            {
                Vector3 side = i == 0 ? Vector3.right : Vector3.left;
                enemies[i].transform.position = player.transform.position + side;
                enemies[i].transform.rotation = Quaternion.LookRotation(-side);
                enemies[i].FeintChance = 0f;
                enemies[i].CanPin = false;
            }
            for (int i = 2; i < enemies.Length; i++)
                enemies[i].enabled = false;

            float deadline = Time.realtimeSinceStartup + 3f;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (enemies[0].Phase == EnemyPhase.Windup && enemies[1].Phase == EnemyPhase.Windup)
                    yield break;
                yield return null;
            }

            Assert.Fail("両側の敵が同時に予備動作に入れない");
        }

        [UnityTest]
        public IEnumerator 同時に攻撃してくる敵は上限まで()
        {
            yield return LoadPrototype();

            var group = Object.FindFirstObjectByType<EnemyGroup>();
            Assert.IsNotNull(group, "EnemyGroup がシーンに無い");

            var brains = Object.FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None);
            Assert.Greater(brains.Length, 1);

            int maxConcurrent = 0;

            float elapsed = 0f;
            int frames = 0;
            while (elapsed < 8f && frames < 2400)
            {
                int concurrent = 0;
                foreach (var b in brains)
                {
                    if (b == null)
                        continue;
                    if (b.Phase == EnemyPhase.Windup || b.Phase == EnemyPhase.Attack)
                        concurrent++;
                }

                maxConcurrent = Mathf.Max(maxConcurrent, concurrent);

                elapsed += Time.unscaledDeltaTime;
                frames++;
                yield return null;
            }

            Assert.LessOrEqual(maxConcurrent, group.MaxAttackers,
                $"攻撃に踏み切った敵が多すぎる: {maxConcurrent} > {group.MaxAttackers}");
            Assert.Greater(maxConcurrent, 0, "誰も攻撃してこない");
        }

        [UnityTest]
        public IEnumerator 敵は死んだら行動を止める()
        {
            yield return LoadPrototype();

            var brains = Object.FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None);
            Assert.IsNotEmpty(brains);

            var target = brains[0];
            var attacker = new CombatState();

            for (int i = 0; i < 200 && target.IsAlive; i++)
            {
                target.TakeHit(attacker, new AttackData
                {
                    kind = AttackKind.Heavy,
                    damage = 30f,
                    targetPart = BodyPart.Torso
                });
            }

            Assert.IsFalse(target.IsAlive, "ダメージを与えても死なない");

            Vector3 before = target.transform.position;
            float elapsed = 0f;
            while (elapsed < 1.2f)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            Assert.AreEqual(EnemyPhase.Dead, target.Phase, "死亡しても Dead 状態にならない");
            Assert.Less((target.transform.position - before).magnitude, 0.5f,
                "死亡後も動き続けている");
        }
    }
}
