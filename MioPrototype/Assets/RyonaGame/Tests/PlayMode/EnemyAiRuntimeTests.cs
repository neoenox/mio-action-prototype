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
                typeof(EnemyBrain).GetMethod("BeginWindup", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .Invoke(enemy, new object[] { attack });
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
            foreach (var attack in new[] { EnemyBrain.AttackId.Jab, EnemyBrain.AttackId.Heavy, EnemyBrain.AttackId.Break })
            {
                yield return LoadPrototype();
                var player = Object.FindFirstObjectByType<PlayerStats>();
                var enemies = Object.FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None);
                foreach (var e in enemies) e.enabled = false;
                var enemy = enemies[0];
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
                player.HitReceived += result => distanceAtHit = Vector3.Distance(figure.Fist(false).position, rig.Chest.position);
                typeof(EnemyBrain).GetMethod("BeginWindup", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .Invoke(enemy, new object[] { attack });
                float deadline = Time.realtimeSinceStartup + 3f;
                while (Time.realtimeSinceStartup < deadline && distanceAtHit < 0f) yield return null;
                Assert.GreaterOrEqual(distanceAtHit, 0f, $"{attack} did not hit");
                Assert.Less(distanceAtHit, 0.20f, $"{attack} fist is visibly short of the torso");
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
            var method = typeof(EnemyBrain).GetMethod("TryBeginAttack", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            bool started = (bool)method.Invoke(enemy, new object[] { 1f });
            Assert.IsFalse(started, "Enemy should turn toward the player before beginning an attack");
            foreach (var other in Object.FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None))
                if (other != enemy) other.enabled = false;
            float hp = player.State.Hp;
            float deadline = Time.realtimeSinceStartup + 10f;
            while (Time.realtimeSinceStartup < deadline && player.State.Hp >= hp)
                yield return null;
            Assert.Less(player.State.Hp, hp, "Enemy should turn around and eventually hit the player");
        }

        [UnityTest]
        public IEnumerator 掴みの投げが終わった後に敵が攻撃を再開する()
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
            float deadline = Time.realtimeSinceStartup + 16f;
            while (!grapple.IsGrappled && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsTrue(grapple.IsGrappled, "Enemy should grab before the throw");
            while (grapple.IsGrappled && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsFalse(grapple.IsGrappled, "The throw must end");
            Vector3 throwStart = player.transform.position;
            Vector3 awayFromCaptor = (throwStart - holder.transform.position).normalized;
            yield return new WaitForSeconds(0.35f);
            float away = Vector3.Dot(player.transform.position - throwStart, awayFromCaptor);
            Assert.Greater(away, 0.1f, "Throw should move the player away from the captor");
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
            Assert.IsTrue(sawPunch, $"No punch after throw: phase={holder.Phase} distance={Vector3.Distance(holder.transform.position, player.transform.position):F2} holding={holder.IsHolding} token={holder.HasAttackToken}");
            Assert.IsTrue(landedPunch, "Enemy must land a punch after the throw");
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
                    postureDamage = 50f,
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
