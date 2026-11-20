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
            var mount = _stats.GetComponent<GroundMountAttack>();
            Vector3 approachStart = _stats.transform.position + Vector3.right * 1.9f;
            float deadline = Time.realtimeSinceStartup + 8f;
            while (mount.Stage.ToString() != "Approach" && Time.realtimeSinceStartup < deadline)
            {
                // 通常 AI に先に接近されると、移動なしで跨る配置になる。
                attacker.transform.position = approachStart;
                yield return null;
            }
            Assert.AreEqual("Approach", mount.Stage.ToString());

            float peakSpeed = 0f;
            Vector3 start = attacker.transform.position;
            Vector3 direction = Vector3.ProjectOnPlane(_anim.Rig.Hips.position - start, Vector3.up).normalized;
            Vector3 previous = start;
            while (mount.Stage.ToString() == "Approach" && Time.realtimeSinceStartup < deadline)
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
            Assert.AreEqual("Mount", mount.Stage.ToString());
            Assert.AreEqual(expectedSpeed, peakSpeed, 0.12f, "接近を通常の移動速度に合わせる");
        }

        [UnityTest]
        public IEnumerator PronePlayerIsMountedThenRolledBeforeFiveAlternatingPunches()
        {
            yield return LoadPlayer(holdEnemies: false);
            var attacker = PrepareMount(forward: true);
            var mount = _stats.GetComponent<GroundMountAttack>();
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
                Assert.AreEqual(hits % 2 == 1, mount.LeftPunch, "Hooks alternate right and left.");
                headBeforeImpact = _anim.Rig.Head.rotation;
                hits++;
            };

            float deadline = Time.realtimeSinceStartup + 9f;
            float reaction = 0f;
            while (hits < 5 && Time.realtimeSinceStartup < deadline)
            {
                string beat = mount.Stage.ToString();
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
        /// うつ伏せダウン → 跨る → 仰向けに返す → 5 発、の各パンチが**彼女の頭へ向く**こと。
        ///
        /// 寝返りで体の向きが 180° 変わる（仰向け姿が逆端向きに作られているため、ロール明けに
        /// リグが反転される）。跨ったままの向きのままだと頭と逆を殴って見えるので、
        /// 敵の forward と頭方向の dot、拳と頭の距離、跨る位置（頭側か）を見張る。
        /// </summary>
        [UnityTest]
        public IEnumerator MountedPunchesAfterProneRollFaceHerHead([Values(false, true)] bool attackerFromFeetSide)
        {
            yield return LoadPlayer(holdEnemies: false);
            var attacker = PrepareMount(forward: true);
            if (attackerFromFeetSide)
                attacker.transform.position = _stats.transform.position - Vector3.right * 1.6f;

            var mount = _stats.GetComponent<GroundMountAttack>();
            var rig = _anim.Rig;
            var figure = attacker.GetComponent<EnemyFigure>();

            int hits = 0;
            bool straddlesHeadSide = false;
            float worstFacing = 1f;
            float worstFist = 0f;
            _stats.HitReceived += result =>
            {
                if (_stats.LastAttacker != attacker.transform || result.Damage <= 0f)
                    return;

                hits++;
                Vector3 axis = Vector3.ProjectOnPlane(rig.Head.position - rig.Hips.position, Vector3.up).normalized;
                bool left = mount.LeftPunch;
                worstFacing = Mathf.Min(worstFacing, Vector3.Dot(attacker.transform.forward, axis));
                worstFist = Mathf.Max(worstFist,
                    Vector3.Distance(figure.Fist(left).position, rig.Head.position));
                if (hits == 1)
                    straddlesHeadSide =
                        Vector3.Dot(attacker.transform.position - rig.Hips.position, axis) > 0f;
            };

            float deadline = Time.realtimeSinceStartup + 10f;
            while (hits < 5 && Time.realtimeSinceStartup < deadline)
                yield return null;
            yield return null;

            Assert.AreEqual(5, hits, "5 発そろわない");
            Assert.IsTrue(straddlesHeadSide, "跨る位置が頭側でない（足側から殴っている）");
            Assert.Greater(worstFacing, 0.5f,
                $"ロール後に敵が頭と逆を向いて殴った（最小 dot={worstFacing:F2}）");
            Assert.Less(worstFist, 0.25f, $"拳が頭から離れている（最大 {worstFist:F2}m）");
        }

        /// <summary>
        /// ロール明けの「跨り直し」が実際に効いていることの検証。
        ///
        /// 実際の不具合（敵が頭と逆を向く）は現行ビルドでは再現しないため、上のテストだけだと
        /// 保険を外しても通ってしまう。ここでは Windup に入った瞬間に敵の向きを 180° 反転させ、
        /// 0.30 秒以内に頭方向へ戻る（dot &gt; 0.8）ことを見る。跨り直しが無ければ反転したままになる。
        /// </summary>
        [UnityTest]
        public IEnumerator MountResettleTurnsAttackerBackToHerHead()
        {
            yield return LoadPlayer(holdEnemies: false);
            var attacker = PrepareMount(forward: true);

            var mount = _stats.GetComponent<GroundMountAttack>();
            var rig = _anim.Rig;

            float DotToHead()
            {
                Vector3 axis = Vector3.ProjectOnPlane(rig.Head.position - rig.Hips.position, Vector3.up).normalized;
                return Vector3.Dot(attacker.transform.forward, axis);
            }

            // ロールが終わって Windup に入るまで待つ（跨り直しはこの瞬間から 0.18 秒）。
            float deadline = Time.realtimeSinceStartup + 10f;
            while (mount.Stage.ToString() != "Windup" && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.AreEqual("Windup", mount.Stage.ToString(), "ロール明けの Windup に入らない");

            // 敵の向きを 180° 反転させる（頭と逆を向いた状態を作る）。
            attacker.transform.rotation = Quaternion.AngleAxis(180f, Vector3.up) * attacker.transform.rotation;
            yield return null;

            float recoveryDeadline = Time.realtimeSinceStartup + 0.30f;
            while (Time.realtimeSinceStartup < recoveryDeadline)
                yield return null;

            Assert.Greater(DotToHead(), 0.8f,
                $"跨り直しが効かず、反転した向きのまま（dot={DotToHead():F2}）");
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

            var mount = _stats.GetComponent<GroundMountAttack>();
            Assert.IsNotNull(mount, "GroundMountAttack が無い");

            float deadline = Time.realtimeSinceStartup + 8f;
            while (mount.Stage.ToString() != "Roll" && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.AreEqual("Roll", mount.Stage.ToString(), "ロールが始まらない");

            var rig = _anim.Rig;
            Assert.IsNotNull(rig, "リグが無い");

            float tightest = 180f;
            float minReach = 10f;
            float lowestHand = 10f;
            int samples = 0;

            while (mount.Stage.ToString() == "Roll" && Time.realtimeSinceStartup < deadline)
            {
                float roll = mount.Progress;
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
            var mount = _stats.GetComponent<GroundMountAttack>();
            Assert.IsNotNull(mount);
            float deadline = Time.realtimeSinceStartup + 5f;
            while (mount.Stage.ToString() != "Windup" && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.AreEqual("Windup", mount.Stage.ToString());
            float hp = _stats.State.Hp;
            for (int i = 0; i < 6; i++)
                mount.Struggle();

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
        public IEnumerator ダウン中も呼吸で体がわずかに動く()
        {
            yield return LoadPlayer();

            // ダウン姿勢を時刻を変えて作り、腰の高さの振れ幅を見る。
            // 以前は呼吸が 3mm で、合成シートの時間方向の差分が平均の約 2% しかなく
            // 「止め絵」に見えていた（docs/motion-audit.md）。その退行を止める。
            var rig = _anim.Rig;
            float min = float.MaxValue;
            float max = float.MinValue;

            for (float t = 1.0f; t <= 1.99f; t += 0.05f)
            {
                _anim.ForceState(CharacterAnimator.State.Down, t, 2f, StaggerTier.None, log: false);
                yield return null;
                float y = rig.Hips.position.y;
                if (y < min) min = y;
                if (y > max) max = y;
            }

            Assert.Greater(max - min, 0.008f,
                $"ダウン中に体がほとんど動かない（振れ幅 {max - min:F3}m）。呼吸が効いていない");
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

        [UnityTest]
        public IEnumerator MountedEscapeShovesEnemyIntoStagger()
        {
            yield return LoadPlayer(holdEnemies: false);
            var attacker = PrepareMount(forward: false);
            var mount = _stats.GetComponent<GroundMountAttack>();

            float deadline = Time.realtimeSinceStartup + 6f;
            while (mount.Stage != GroundMountAttack.Beat.Windup && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.AreEqual(GroundMountAttack.Beat.Windup, mount.Stage, "マウントに入らない");

            for (int i = 0; i < 6; i++)
                mount.Struggle();
            Assert.IsTrue(mount.Escaped, "連打で脱出状態に入らない");
            Assert.AreEqual(GroundMountAttack.Beat.Dismount, mount.Stage);

            deadline = Time.realtimeSinceStartup + 2f;
            while (mount.Stage != GroundMountAttack.Beat.Idle && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.AreEqual(GroundMountAttack.Beat.Idle, mount.Stage, "Dismount が完了しない");
            Assert.AreEqual(EnemyPhase.Stagger, attacker.Phase, "跳ね除けられた敵がよろけていない");
            yield return null;
            Assert.IsTrue(_stats.GetUpRequested || _stats.IsGettingUp, "脱出後に起き上がり要求/開始に入っていない");

            yield return new WaitForSeconds(_anim.GetUpSeconds + 0.3f);
            Assert.IsFalse(_stats.IsDown, "脱出後に立ち上がれていない");
        }
        [UnityTest]
        public IEnumerator MountKeepsFaceVisibleWhileAttackerCovers()
        {
            yield return LoadPlayer(holdEnemies: false);
            PrepareMount(forward: false);
            var mount = _stats.GetComponent<GroundMountAttack>();

            float deadline = Time.realtimeSinceStartup + 6f;
            while (mount.Stage < GroundMountAttack.Beat.Mount && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.GreaterOrEqual(mount.Stage, GroundMountAttack.Beat.Mount, "マウントに入らない");

            // 跨った直後は通常のベルトカメラ（引きの全体）で見せる設計なので、
            // マウントのカメラ（FrameMount）が担当するパンチの区間から測る。
            deadline = Time.realtimeSinceStartup + 6f;
            while (mount.Stage < GroundMountAttack.Beat.Windup && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.GreaterOrEqual(mount.Stage, GroundMountAttack.Beat.Windup, "パンチが始まらない");

            var figures = Object.FindObjectsByType<EnemyFigure>(FindObjectsSortMode.None);
            int frames = 0;
            float sum = 0f;
            int badLensStreak = 0, worstLensStreak = 0;
            float minUpDot = 1f;
            float minDistance = float.MaxValue, maxDistance = 0f, maxElevation = 0f;
            int smallFaceFrames = 0;
            int sideSwitches = 0, lastSide = 0;
            float sideStart = Time.realtimeSinceStartup;
            deadline = Time.realtimeSinceStartup + 2f;
            while (mount.Stage >= GroundMountAttack.Beat.Mount &&
                   mount.Stage < GroundMountAttack.Beat.Dismount &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return null;
                sum += FaceVisibility.Measure(Camera.main, _anim.Rig.Head, figures).Visible;
                float lens = FaceVisibility.LensClearance(Camera.main.transform.position, figures);
                badLensStreak = lens < 0.5f ? badLensStreak + 1 : 0;
                worstLensStreak = Mathf.Max(worstLensStreak, badLensStreak);
                minUpDot = Mathf.Min(minUpDot, Vector3.Dot(Camera.main.transform.up, Vector3.up));
                // 遠すぎる画（上空からの豆粒）を止める。可視率だけでは豆粒でも満点になる。
                Vector3 toCamera = Camera.main.transform.position - _anim.Rig.Head.position;
                float distance = toCamera.magnitude;
                minDistance = Mathf.Min(minDistance, distance);
                maxDistance = Mathf.Max(maxDistance, distance);
                float flat = new Vector3(toCamera.x, 0f, toCamera.z).magnitude;
                maxElevation = Mathf.Max(maxElevation, Mathf.Atan2(toCamera.y, Mathf.Max(0.001f, flat)) * Mathf.Rad2Deg);
                if (FaceVisibility.ScreenFaceSize(Camera.main, _anim.Rig.Head) < GroundMountAttack.MountMinFaceSize)
                    smallFaceFrames++;
                int side = System.Math.Sign(Vector3.Dot(
                    Camera.main.transform.position - _anim.Rig.Head.position, _stats.transform.right));
                if (lastSide != 0 && side != 0 && side != lastSide)
                    sideSwitches++;
                lastSide = side;
                frames++;
            }

            float avg = frames > 0 ? sum / frames : 0f;
            Debug.Log($"[MountFaceVisible] frames={frames} avgVisible={avg:F2} " +
                      $"dist={minDistance:F2}..{maxDistance:F2} elev<={maxElevation:F0} smallFace={smallFaceFrames}");
            // 寄りすぎ（敵の体が画面を覆う）と引きすぎ（上空からの豆粒）の両方を止める。
            Assert.GreaterOrEqual(minDistance, 1.2f, $"カメラが近すぎる ({minDistance:F2}m)");
            // 頭からの距離（候補は頭と腰の間の注視点から水平 1.4〜2.6m・高さ 0.9〜1.5m）。
            // 読める大きさかは下の顔の大きさで見る。上空からの豆粒（実測 6.1m）はここで止まる。
            Assert.LessOrEqual(maxDistance, 3.2f, $"カメラが遠すぎる ({maxDistance:F2}m)");
            Assert.LessOrEqual(maxElevation, GroundMountAttack.MountMaxElevation + 15f,
                $"真上から見下ろしすぎている ({maxElevation:F0}°)");
            Assert.LessOrEqual(smallFaceFrames, frames / 10, $"顔が小さすぎるコマが多い ({smallFaceFrames}/{frames})");
            Assert.Greater(frames, 10);
            Assert.GreaterOrEqual(avg, 0.60f, $"マウント中に顔が隠れすぎている (avg={avg:F2})");
            Assert.Less(worstLensStreak, 3, $"レンズ前に敵が長く留まる ({worstLensStreak} コマ)");
            Assert.Greater(minUpDot, 0.3f, $"カメラが逆さまになる (up·up={minUpDot:F2})");
            float seconds = Mathf.Max(0.1f, Time.realtimeSinceStartup - sideStart);
            Assert.Less(sideSwitches / seconds, 1f, $"側の切り替えが多すぎる ({sideSwitches} 回 / {seconds:F1}s)");
        }
    }
}
