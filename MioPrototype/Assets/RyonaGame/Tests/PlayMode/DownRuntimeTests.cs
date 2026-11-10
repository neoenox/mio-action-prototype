using System.Collections;
using NUnit.Framework;
using Ryona.Game;
using UnityEngine;
using UnityEngine.TestTools;

namespace Ryona.Tests
{
    /// <summary>
    /// ひるみ（ダウンゲージ）が溜まったときのダウン。
    ///
    /// ・軽い攻撃を当て続けるとダウンする
    /// ・ダウンは後ろへ放物線で飛ぶ（その場で崩れるだけにしない）
    /// ・ダウン中はひるみが溜まらない（起き上がるそばから倒れ続けない）
    /// </summary>
    public sealed class DownRuntimeTests
    {
        PlayerStats _stats;
        PlayerMotor _motor;
        CharacterAnimator _anim;
        GameObject _attacker;

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            if (_attacker != null)
                Object.Destroy(_attacker);
            _attacker = null;
        }

        IEnumerator LoadPlayer(bool holdEnemies = true)
        {
            yield return TestScene.LoadPrototype();

            _stats = Object.FindFirstObjectByType<PlayerStats>();
            Assert.IsNotNull(_stats, "PlayerStats がシーンに無い");
            _motor = _stats.GetComponent<PlayerMotor>();
            Assert.IsNotNull(_motor, "PlayerMotor が PlayerStats と同じオブジェクトに無い");

            float deadline = Time.realtimeSinceStartup + 8f;
            while (Time.realtimeSinceStartup < deadline)
            {
                _anim = _stats.GetComponentInChildren<CharacterAnimator>();
                if (_anim != null && _anim.Rig != null && _anim.Rig.Ready)
                    break;
                yield return null;
            }

            Assert.IsTrue(_anim != null && _anim.Rig != null && _anim.Rig.Ready,
                "キャラクターのリグが組み上がらない");

            // テスト中は AI に殴らせない（当てるのはこのテストだけ）
            foreach (var enemy in Object.FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None))
            {
                if (holdEnemies && enemy != null)
                    enemy.HoldFire(9999f);
            }

            // 殴る側は彼女の正面 0.8m に置く（吹き飛ぶ向きが後ろになる）
            _attacker = new GameObject("Down Test Attacker");
            _attacker.transform.position = _stats.transform.position + _stats.transform.forward * 0.8f;
        }

        EnemyBrain PrepareMount(bool forward)
        {
            var enemies = Object.FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None);
            Assert.Greater(enemies.Length, 0);
            var attacker = enemies[0];
            foreach (var enemy in enemies)
                enemy.enabled = enemy == attacker;
            attacker.LockForHandOff(0.1f);
            attacker.transform.position = _stats.transform.position + Vector3.right * 1.6f;
            _stats.ForceDown(2f, "mount-test", launchDistance: 0f, forward: forward);
            return attacker;
        }

        [UnityTest]
        public IEnumerator MountApproachIsDirectAtNormalEnemyMovementSpeed([Values(false, true)] bool forward)
        {
            yield return LoadPlayer(holdEnemies: false);
            var attacker = PrepareMount(forward);
            if (forward)
                attacker.MoveSpeed = 1.65f;
            float expectedSpeed = attacker.MoveSpeed * attacker.State.MoveSpeedMultiplier;
            var mount = _stats.GetComponent("GroundMountAttack");
            const System.Reflection.BindingFlags members = System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.NonPublic;
            var stage = mount.GetType().GetProperty("Stage", members);
            Vector3 approachStart = _stats.transform.position + Vector3.right * 1.9f;
            float deadline = Time.realtimeSinceStartup + 8f;
            while (stage.GetValue(mount).ToString() != "Approach" && Time.realtimeSinceStartup < deadline)
            {
                // 通常 AI に先に接近されると、移動なしで跨る配置になる。
                attacker.transform.position = approachStart;
                yield return null;
            }
            Assert.AreEqual("Approach", stage.GetValue(mount).ToString());

            float peakSpeed = 0f;
            Vector3 start = attacker.transform.position;
            Vector3 direction = Vector3.ProjectOnPlane(_anim.Rig.Hips.position - start, Vector3.up).normalized;
            Vector3 previous = start;
            while (stage.GetValue(mount).ToString() == "Approach" && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
                Vector3 step = Vector3.ProjectOnPlane(attacker.transform.position - previous, Vector3.up);
                if (Time.deltaTime > 0f)
                    peakSpeed = Mathf.Max(peakSpeed, step.magnitude / Time.deltaTime);
                float detour = Vector3.Cross(
                    Vector3.ProjectOnPlane(attacker.transform.position - start, Vector3.up), direction).magnitude;
                Assert.Less(detour, 0.06f, "敵は今いる側から澪へまっすぐ近づく");
                previous = attacker.transform.position;
            }
            Assert.AreEqual("Mount", stage.GetValue(mount).ToString());
            Assert.AreEqual(expectedSpeed, peakSpeed, 0.12f, "接近を通常の移動速度に合わせる");
        }

        [UnityTest]
        public IEnumerator PronePlayerIsMountedThenRolledBeforeFiveAlternatingPunches()
        {
            yield return LoadPlayer(holdEnemies: false);
            var attacker = PrepareMount(forward: true);
            var mount = _stats.GetComponent("GroundMountAttack");
            const System.Reflection.BindingFlags members = System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.NonPublic;
            var leftPunch = mount.GetType().GetProperty("LeftPunch", members);
            var stage = mount.GetType().GetProperty("Stage", members);
            bool mountedWhileProne = false;
            bool rolledWhileMounted = false;
            int hits = 0;
            Quaternion headBeforeImpact = Quaternion.identity;
            _stats.HitReceived += result =>
            {
                if (_stats.LastAttacker != attacker.transform || result.Damage <= 0f)
                    return;
                Assert.IsFalse(_stats.DownIsForward, "The player is on her back before being punched.");
                Assert.AreEqual(PunchZone.Face, _stats.LastHitZone);
                Assert.AreEqual(hits % 2 == 1, leftPunch.GetValue(mount), "Hooks alternate right and left.");
                headBeforeImpact = _anim.Rig.Head.rotation;
                hits++;
            };

            float deadline = Time.realtimeSinceStartup + 9f;
            float reaction = 0f;
            while (hits < 5 && Time.realtimeSinceStartup < deadline)
            {
                string beat = stage.GetValue(mount).ToString();
                if (beat == "Mount" && _stats.DownIsForward)
                    mountedWhileProne = true;
                if (beat == "Roll")
                    rolledWhileMounted = mountedWhileProne && _stats.DownIsForward;
                if (hits == 1)
                    reaction = Mathf.Max(reaction, Quaternion.Angle(headBeforeImpact, _anim.Rig.Head.rotation));
                yield return null;
            }
            Assert.IsTrue(mountedWhileProne, "The attacker straddles the player while she is still prone.");
            Assert.IsTrue(rolledWhileMounted, "The attacker rolls the player after straddling her.");
            Assert.AreEqual(5, hits);
            Assert.Greater(reaction, 27f, "A landed punch visibly snaps the head away.");
            yield return new WaitForSeconds(1f);
            Assert.AreEqual(5, hits, "The player has time to request get-up after the mount.");
            Assert.IsTrue(_stats.IsDown);
            _stats.RequestGetUp();
            yield return new WaitForSeconds(_anim.GetUpSeconds + 0.15f);
            Assert.IsFalse(_stats.IsDown);
        }

        /// <summary>
        /// うつ伏せから仰向けへ返すとき、腕が畳み切られたり飛んだりしない。
        ///
        /// 修正前の実測: ロール半ばで肘が 19° まで畳まれ、手が肩の 17cm まで寄っていた。
        /// さらにロール開始の 1 フレームで片手が 0.42m 飛んでいた（伏せダウン中のマウントは
        /// ガード姿勢を出さないため、ガードを入りの基準にすると飛ぶ）。
        /// </summary>
        [UnityTest]
        public IEnumerator RollingFromProneToSupineDoesNotFoldTheArmsUp()
        {
            yield return LoadPlayer(holdEnemies: false);
            PrepareMount(forward: true);

            var mount = _stats.GetComponent("GroundMountAttack");
            Assert.IsNotNull(mount, "GroundMountAttack が無い");
            const System.Reflection.BindingFlags members = System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.NonPublic;
            var stage = mount.GetType().GetProperty("Stage", members);
            var progress = mount.GetType().GetProperty("Progress", members);
            Assert.IsNotNull(stage, "Stage が取れない");
            Assert.IsNotNull(progress, "Progress が取れない");

            float deadline = Time.realtimeSinceStartup + 8f;
            while (stage.GetValue(mount).ToString() != "Roll" && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.AreEqual("Roll", stage.GetValue(mount).ToString(), "ロールが始まらない");

            var rig = _anim.Rig;
            Assert.IsNotNull(rig, "リグが無い");

            float tightest = 180f;
            float minReach = 10f;
            float lowestHand = 10f;
            int samples = 0;

            while (stage.GetValue(mount).ToString() == "Roll" && Time.realtimeSinceStartup < deadline)
            {
                float roll = (float)progress.GetValue(mount);
                // 入りと終わりはガード姿勢へ寄せる区間なので、判定は中盤だけにする。
                if (roll > 0.12f && roll < 0.88f)
                {
                    tightest = Mathf.Min(tightest,
                        ElbowBend(rig.LUpperArm, rig.LLowerArm, rig.LHand));
                    tightest = Mathf.Min(tightest,
                        ElbowBend(rig.RUpperArm, rig.RLowerArm, rig.RHand));
                    minReach = Mathf.Min(minReach,
                        Vector3.Distance(rig.LUpperArm.position, rig.LHand.position));
                    minReach = Mathf.Min(minReach,
                        Vector3.Distance(rig.RUpperArm.position, rig.RHand.position));
                    lowestHand = Mathf.Min(lowestHand,
                        Mathf.Min(rig.LHand.position.y, rig.RHand.position.y) - rig.transform.position.y);
                }

                samples++;
                yield return null;
            }

            Assert.Greater(samples, 5, "ロール中に姿勢をサンプルできない");
            Assert.Greater(tightest, 40f, $"ロール中に肘が畳み切られている（{tightest:F0}°）");
            Assert.Greater(minReach, 0.18f, $"手が肩に寄りすぎている（{minReach:F2}m）");
            Assert.Greater(lowestHand, -0.05f, $"手が床に潜っている（y={lowestHand:F2}m）");
        }

        static float ElbowBend(Transform upper, Transform lower, Transform hand)
        {
            Vector3 a = upper.position - lower.position;
            Vector3 b = hand.position - lower.position;
            return a.sqrMagnitude > 1e-8f && b.sqrMagnitude > 1e-8f ? Vector3.Angle(a, b) : 180f;
        }

        [UnityTest]
        public IEnumerator StayingDownAfterMountLetsTheEnemyAttackAgain()
        {
            yield return LoadPlayer(holdEnemies: false);
            var attacker = PrepareMount(forward: false);
            int hits = 0;
            float fifthHitAt = 0f;
            _stats.HitReceived += result =>
            {
                if (_stats.LastAttacker == attacker.transform && result.Damage > 0f && ++hits == 5)
                    fifthHitAt = Time.time;
            };

            float deadline = Time.realtimeSinceStartup + 12f;
            while (hits < 6 && Time.realtimeSinceStartup < deadline)
                yield return null;

            Assert.AreEqual(6, hits, "The second mount lands a punch without a new knockdown.");
            Assert.GreaterOrEqual(Time.time - fifthHitAt, 2.5f, "The player gets a recovery window.");
            Assert.IsTrue(_stats.IsDown);
        }

        [UnityTest]
        public IEnumerator EarlyGetUpAvoidsGroundMount()
        {
            yield return LoadPlayer(holdEnemies: false);
            PrepareMount(forward: true);
            float hp = _stats.State.Hp;
            yield return new WaitForSeconds(0.85f);
            _stats.RequestGetUp();
            yield return new WaitForSeconds(1.5f);
            Assert.IsFalse(_stats.IsDown);
            Assert.IsTrue(_stats.DownIsForward, "The enemy never rolled the player.");
            Assert.AreEqual(hp, _stats.State.Hp, 0.001f);
        }

        [UnityTest]
        public IEnumerator StrugglingFromSupineMountGetsThePlayerUp()
        {
            yield return LoadPlayer(holdEnemies: false);
            PrepareMount(forward: false);
            var mount = _stats.GetComponent("GroundMountAttack");
            Assert.IsNotNull(mount);
            const System.Reflection.BindingFlags members = System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.NonPublic;
            var stage = mount.GetType().GetProperty("Stage", members);
            var struggle = mount.GetType().GetMethod("Struggle", members);
            float deadline = Time.realtimeSinceStartup + 5f;
            while (stage.GetValue(mount).ToString() != "Windup" && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.AreEqual("Windup", stage.GetValue(mount).ToString());
            float hp = _stats.State.Hp;
            for (int i = 0; i < 6; i++)
                struggle.Invoke(mount, null);

            yield return new WaitForSeconds(1.5f);
            Assert.IsFalse(_stats.IsDown);
            Assert.AreEqual(hp, _stats.State.Hp, 0.001f);
        }

        [UnityTest]
        public IEnumerator HPゼロから復帰すると起き上がって操作に戻れる()
        {
            yield return LoadPlayer();

            var lethal = AttackLibrary.Light();
            lethal.damage = _stats.State.MaxHp * 10f;
            _stats.ReceiveHit(new CombatState(), lethal, _attacker.transform);
            Assert.IsTrue(_stats.IsDead, "HP が 0 になっていない");

            float deadline = Time.realtimeSinceStartup + 2f;
            while (_anim.Current != CharacterAnimator.State.Down && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.AreEqual(CharacterAnimator.State.Down, _anim.Current);

            // HP が 0 のとき、HUD が R キーで呼ぶ復帰処理。
            _stats.RestoreAll();
            yield return null;
            yield return null;

            Assert.AreEqual(_stats.State.MaxHp, _stats.State.Hp);
            Assert.AreEqual(CharacterAnimator.State.GetUp, _anim.Current);
            Assert.IsTrue(_stats.IsStunned, "起き上がり中は操作を受け付けない");

            yield return new WaitForSeconds(_anim.GetUpSeconds + 0.1f);
            Assert.IsFalse(_anim.IsDowned);
            Assert.IsFalse(_stats.IsDown);
            Assert.IsFalse(_stats.IsStunned);
        }

        [UnityTest]
        public IEnumerator ゴッドモード中は致死ダメージでも死なず体力が回復する()
        {
            yield return LoadPlayer();

            _stats.SetGodMode(true);

            var lethal = AttackLibrary.Heavy();
            lethal.damage = _stats.State.MaxHp * 10f;
            _stats.ReceiveHit(new CombatState(), lethal, _attacker.transform);

            Assert.IsFalse(_stats.IsDead, "ゴッドモード中に死んでいる");
            Assert.GreaterOrEqual(_stats.State.Hp, 1f, "HP が 0 になっている");

            float before = _stats.State.Hp;
            yield return new WaitForSeconds(0.25f);
            Assert.Greater(_stats.State.Hp, before, "体力が自動回復していない");

            _stats.SetGodMode(false);
        }

        /// <summary>軽い攻撃を、ダウンするまで当てる。当てた回数を返す。</summary>
        IEnumerator HitUntilDown(System.Action<int> onHit = null)
        {
            var attackerState = new CombatState();
            for (int i = 0; i < 30 && !_stats.State.IsDown; i++)
            {
                _stats.ReceiveHit(attackerState, AttackLibrary.Light(), _attacker.transform);
                onHit?.Invoke(i + 1);
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator 軽い攻撃を当て続けるとダウンする()
        {
            yield return LoadPlayer();

            int hits = 0;
            yield return HitUntilDown(n => hits = n);

            Assert.IsTrue(_stats.State.IsDown,
                $"{hits} 発当ててもダウンしない（ひるみ={_stats.State.Flinch:F0}）");
            Assert.AreEqual(9, hits, "軽い攻撃は 9 発でダウンする想定");
            Assert.IsFalse(_stats.DownIsForward, "正面からの一撃で仰向けに倒れる");
        }

        [UnityTest]
        public IEnumerator ダウンは後ろへ放物線で飛ぶ()
        {
            yield return LoadPlayer();

            Vector3 start = _stats.transform.position;
            yield return HitUntilDown();

            Assert.IsTrue(_stats.State.IsDown, "ダウンしていない");
            Assert.IsTrue(_motor.IsAirborne, "ダウンの瞬間に浮いていない（その場で崩れるだけになっている）");

            // 0.08 秒後には目に見えて浮いている（滞空は 0.24 秒）
            yield return new WaitForSeconds(0.08f);
            Assert.Greater(_stats.transform.position.y, start.y + 0.05f,
                $"浮いていない（y={_stats.transform.position.y:F2}）");

            // 0.6 秒後には着地して、後ろへ下がっている（姿勢側でも倒れるので 0.3m で見る）
            yield return new WaitForSeconds(0.6f);
            Assert.IsFalse(_motor.IsAirborne, "着地していない");

            Vector3 moved = _stats.transform.position - start;
            moved.y = 0f;
            Assert.Greater(moved.magnitude, 0.2f, $"吹き飛んでいない（{moved.magnitude:F2}m）");

            Assert.AreEqual(CharacterAnimator.State.Down, _anim.Current,
                "倒れた姿勢になっていない");
        }

        [UnityTest]
        public IEnumerator ダウン中はひるみが溜まらない()
        {
            yield return LoadPlayer();

            yield return HitUntilDown();
            Assert.IsTrue(_stats.State.IsDown, "ダウンしていない");
            Assert.AreEqual(0f, _stats.State.Flinch, 0.001f, "ダウン時にひるみが 0 に戻っていない");

            // 倒れたまま 3 発殴られる
            var attackerState = new CombatState();
            for (int i = 0; i < 3; i++)
            {
                _stats.ReceiveHit(attackerState, AttackLibrary.Light(), _attacker.transform);
                yield return null;
            }

            Assert.AreEqual(0f, _stats.State.Flinch, 0.001f,
                "ダウン中にひるみが溜まっている（起き上がるそばから倒れ続ける）");
        }

        [UnityTest]
        public IEnumerator 正面からの蓄積でも最後が背後からなら前へ崩れる()
        {
            yield return LoadPlayer();

            var attackerState = new CombatState();
            for (int i = 0; i < 8; i++)
                _stats.ReceiveHit(attackerState, AttackLibrary.Light(), _attacker.transform);
            Assert.IsFalse(_stats.IsDown);

            _attacker.transform.position = _stats.transform.position - _stats.transform.forward;
            _stats.ReceiveHit(attackerState, AttackLibrary.Light(), _attacker.transform);
            yield return null;
            yield return null;

            Assert.IsTrue(_stats.State.IsDown, "ダウンしていない");
            Assert.IsTrue(_stats.DownIsForward, "最後の一撃が背後なのに前へ崩れていない");
            Assert.IsFalse(_motor.IsAirborne, "前へ崩れるときに飛ばしている（その場で膝が抜ける動きにする）");
            Assert.AreEqual(CharacterAnimator.State.Down, _anim.Current, "倒れた姿勢になっていない");
        }

        [UnityTest]
        public IEnumerator うつ伏せのとき手も床についている()
        {
            yield return LoadPlayer();

            _stats.State.AddFlinch(CombatConstants.MaxFlinch - 1f);
            _attacker.transform.position = _stats.transform.position - _stats.transform.forward;
            _stats.LastHitZone = PunchZone.Face;
            _stats.ReceiveHit(new CombatState(), AttackLibrary.Light(), _attacker.transform);

            Assert.IsTrue(_stats.IsDown && _stats.DownIsForward, "背後からの一撃でうつ伏せに倒れる");

            // 倒れきるまで待つ（0.78 秒 + 余裕）
            yield return new WaitForSeconds(1.0f);

            var rig = _anim.Rig;
            Assert.IsNotNull(rig, "リグが無い");
            Assert.IsNotNull(rig.LHand, "左手が無い");
            Assert.IsNotNull(rig.RHand, "右手が無い");

            float groundY = _stats.transform.position.y;
            float lh = rig.LHand.position.y - groundY;
            float rh = rig.RHand.position.y - groundY;
            float head = rig.Head.position.y - groundY;

            // 腕が宙に浮いていない（肩の高さ 0.4m 前後に浮いていたら失敗）
            Assert.Less(lh, 0.30f, $"左手が浮いている（y={lh:F2}）");
            Assert.Less(rh, 0.30f, $"右手が浮いている（y={rh:F2}）");
            Assert.Greater(lh, -0.05f, $"左手が床に埋まっている（y={lh:F2}）");
            Assert.Greater(rh, -0.05f, $"右手が床に埋まっている（y={rh:F2}）");

            // 顔も伏せている（立ち姿勢の 1.5m ではなく、床近く）
            Assert.Less(head, 0.45f, $"顔が浮いている（y={head:F2}）");
        }

        [UnityTest]
        public IEnumerator ダウンは操作するまで続き起き上がり中も移動できない()
        {
            yield return LoadPlayer();

            yield return HitUntilDown();
            Assert.IsTrue(_stats.State.IsDown, "ダウンしていない");

            yield return new WaitForSeconds(CombatConstants.DownDuration + 0.5f);
            Assert.IsTrue(_stats.State.IsDown, "入力がない間はダウンが続く");
            Assert.AreEqual(CharacterAnimator.State.Down, _anim.Current);

            _stats.RequestGetUp();
            yield return null;
            yield return null;
            Assert.AreEqual(CharacterAnimator.State.GetUp, _anim.Current);
            Assert.IsTrue(_stats.State.IsDown, "起き上がり中は操作をロックする");

            yield return new WaitForSeconds(_anim.GetUpSeconds + 0.1f);
            Assert.IsFalse(_stats.State.IsDown, "ダウンが明けていない");
            Assert.AreEqual(CharacterAnimator.State.Idle, _anim.Current,
                $"起き上がって構えに戻っていない（{_anim.Current}）");
        }
    }
}
