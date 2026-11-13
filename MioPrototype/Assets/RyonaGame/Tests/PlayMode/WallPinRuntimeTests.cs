using System.Collections;
using NUnit.Framework;
using Ryona.Game;
using UnityEngine;
using UnityEngine.TestTools;

namespace Ryona.Tests
{
    /// <summary>
    /// 壁押さえつけの回帰テスト。
    ///
    /// レーンの外（+Z）に置いた壁の前で正面の押さえつけが成立すると、
    /// 通常の押さえつけではなく壁へ押し出す形に切り替わる。
    /// もがき・制限時間・膝蹴りは通常の押さえつけと同じ。
    /// </summary>
    public sealed class WallPinRuntimeTests
    {
        PlayerStats _player;
        PlayerGrapple _grapple;
        EnemyBrain _captor;
        EnemyBrain _assist;
        EnemyBrain _third;

        /// <summary>テストで置いた壁の中心 X。押さえ役を毎回ここ基準で置き直す。</summary>
        float _wallCenterX;

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            PlayerGrapple.ForcedWallTossForward = null;
            if (_grapple != null && _grapple.IsGrappled)
                _grapple.Release();
        }

        [UnityTest]
        public IEnumerator 開始地点の壁は掴まれる前は地面の下にある()
        {
            yield return TestScene.LoadPrototype();
            var player = Object.FindFirstObjectByType<PlayerStats>();
            var wall = WallPinSites.Find(player.transform.position);
            Assert.IsNotNull(wall);
            Assert.AreEqual(0f, wall.RiseProgress);
            Assert.Less(wall.transform.Find("Visuals/Panel").GetComponent<Renderer>().bounds.max.y, 0f);
        }

        [UnityTest]
        public IEnumerator 壁拘束は掴みから壁出現と投げと接触を経て始まる()
        {
            yield return Load();
            _captor.QueuePin();
            var stages = new System.Collections.Generic.List<WallStage>();
            float hp = _player.State.Hp;
            float deadline = Time.realtimeSinceStartup + 7f;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (_grapple.IsWallPin)
                {
                    var stage = _grapple.WallPhase;
                    if (stages.Count == 0 || stages[stages.Count - 1] != stage)
                        stages.Add(stage);
                    if (stage == WallStage.Rise)
                        Assert.Less(Mathf.Abs(_player.transform.position.z), 0.1f,
                            "壁が出る間はまだ投げられない");
                    if (stage == WallStage.Throw)
                        Assert.AreEqual(1f, WallPinSites.Nearest(_player.transform.position).RiseProgress);
                    if (stage == WallStage.Slam)
                    {
                        var shake = Object.FindFirstObjectByType<CameraShake>();
                        Assert.IsTrue(shake != null && shake.IsShaking, "壁に当たった瞬間にカメラが揺れる");
                    }
                    if (stage == WallStage.Held)
                        break;
                }
                yield return null;
            }
            CollectionAssert.AreEqual(new[] {
                WallStage.Grab, WallStage.Rise,
                WallStage.Throw, WallStage.Slam,
                WallStage.Lean, WallStage.Regrip,
                WallStage.Restrain, WallStage.Held
            }, stages);
            Assert.AreEqual(hp, _player.State.Hp, "拘束技は壁への接触と手首の固定が済んでから始まる");
            Assert.Greater(_grapple.FrontEscapeRemaining, 2.5f);
        }

        [UnityTest]
        public IEnumerator 壁拘束の膝蹴りは腹にめり込まない()
        {
            yield return Load();

            _captor.QueuePin();
            yield return WaitForGrapple();
            Assert.IsTrue(_grapple.IsWallPin, "壁押さえつけが始まらない");

            var anim = _player.GetComponentInChildren<CharacterAnimator>();
            var figure = _captor.GetComponent<EnemyFigure>();
            Assert.IsNotNull(anim, "CharacterAnimator が無い");
            Assert.IsNotNull(figure, "EnemyFigure が無い");

            // 膝頭の球の半径と、彼女の腹の表面（実装と同じ式）。
            float surface = 0.085f + figure.Height * 0.039f;
            float kneeRadius = figure.Height * 0.039f;

            // 立ち姿勢の腰の高さ。膝蹴りでこれより上へ跳ね上がったら「飛んでいる」。
            float restHipsY = figure.Height * 0.515f;

            float worst = float.NegativeInfinity;
            float highestHips = float.NegativeInfinity;
            float hitY = float.NegativeInfinity;
            float chestY = 0f;
            float hipsBoneY = 0f;
            float deadline = Time.realtimeSinceStartup + 8f;
            while (Time.realtimeSinceStartup < deadline && _grapple.IsGrappled)
            {
                float hipsY = figure.HipsBone.position.y - _captor.transform.position.y;
                highestHips = Mathf.Max(highestHips, hipsY);

                if (_grapple.KneePulse > 0.80f && anim.Rig != null && anim.Rig.Ready &&
                    anim.Rig.Spine != null && anim.Rig.Chest != null)
                {
                    Vector3 spine = anim.Rig.Spine.position;
                    Vector3 chest = anim.Rig.Chest.position;
                    // 実装と同じく水平の正面で測る（胴の線へ射影すると前傾で下がる）。
                    Vector3 front = Vector3.ProjectOnPlane(_player.transform.forward, Vector3.up).normalized;
                    Vector3 axis = Vector3.Lerp(spine, chest, 0.60f);
                    chestY = chest.y;
                    if (anim.Rig.Hips != null)
                        hipsBoneY = anim.Rig.Hips.position.y;

                    // 実装と同じく、蹴る脚（カメラ側）だけを見る。軸足の膝は
                    // 彼女の脚の近くに来るので、両方見ると誤検知する（実測）。
                    var cam = Camera.main;
                    bool left = cam != null &&
                        Vector3.Dot(cam.transform.position - _captor.transform.position,
                            _captor.transform.right) < 0f;
                    var knee = figure.KneeBone(left);
                    if (knee != null)
                    {
                        float depth = Vector3.Dot(knee.position - axis, front);
                        float penetration = surface + kneeRadius - depth;
                        worst = Mathf.Max(worst, penetration);
                        // 振り上げの初速では脚がまだ下にある。当たる瞬間の高さ（最大）を見る。
                        hitY = Mathf.Max(hitY, knee.position.y);
                    }
                }

                yield return null;
            }

            Assert.Greater(worst, float.NegativeInfinity, "膝蹴りが一度も起きていない");
            Assert.Less(worst, 0.05f, $"膝頭が腹にめり込んでいる（{worst:F3}m）");
            Assert.Less(highestHips, restHipsY + 0.05f,
                $"膝蹴りのあと押さえ役が浮き上がっている（腰 {highestHips:F2}m / 立ち {restHipsY:F2}m）");

            // 当たる高さが腹（股間より上、胸より下）であること。
            // 彼女の Hips は骨盤の中心（実測 0.95m）。股間はその下なので、少し上を腹とみなす。
            Assert.Greater(hitY, hipsBoneY + 0.02f,
                $"膝が股間の高さに当たっている（膝 {hitY:F2}m / 彼女の腰 {hipsBoneY:F2}m）");
            Assert.Less(hitY, chestY + 0.06f,
                $"膝が胸より上に当たっている（膝 {hitY:F2}m / 胸 {chestY:F2}m）");
        }

        [UnityTest]
        public IEnumerator 壁押さえつけの両手首は壁面へ貼り付く()
        {
            yield return Load();

            var site = WallPinSites.Nearest(_player.transform.position);
            Assert.IsNotNull(site, "壁が無い");
            float face = site.FaceZ;

            _captor.QueuePin();
            yield return WaitForGrapple();
            Assert.IsTrue(_grapple.IsWallPin, "壁押さえつけが始まらない");

            var anim = _player.GetComponentInChildren<CharacterAnimator>();
            Assert.IsNotNull(anim, "CharacterAnimator が無い");
            Assert.IsTrue(anim.Rig != null && anim.Rig.Ready, "リグが完成していない");

            // 貼り付き（0.40 秒）と姿勢のブレンドが終わるまで待つ。
            yield return WaitGameSeconds(0.7f);

            Vector3 head = anim.Rig.Head.position;
            float gapL = face - anim.Rig.LHand.position.z;
            float gapR = face - anim.Rig.RHand.position.z;

            Assert.Less(gapL, 0.12f, $"左手首が壁から離れている（{gapL:F2}m）");
            Assert.Less(gapR, 0.12f, $"右手首が壁から離れている（{gapR:F2}m）");
            Assert.Greater(anim.Rig.LHand.position.y, head.y, "左手首が頭の上に無い");
            Assert.Greater(anim.Rig.RHand.position.y, head.y, "右手首が頭の上に無い");
            Assert.Greater(anim.Rig.LHand.position.z, anim.Rig.Chest.position.z,
                "左手首が壁側（胸より奥）に無い");
            Assert.Greater(anim.Rig.RHand.position.z, anim.Rig.Chest.position.z,
                "右手首が壁側（胸より奥）に無い");

            // もがいても、壁に貼り付いた手首はその場に残る。
            for (int i = 0; i < 4; i++)
            {
                _grapple.Struggle();
                yield return WaitGameSeconds(0.05f);
                Assert.IsTrue(_grapple.IsGrappled, "まだ抜けてはいけない");
                Assert.Less(face - anim.Rig.LHand.position.z, 0.15f,
                    $"もがき中に左手首が壁から浮いた（{face - anim.Rig.LHand.position.z:F2}m）");
                Assert.Less(face - anim.Rig.RHand.position.z, 0.15f,
                    $"もがき中に右手首が壁から浮いた（{face - anim.Rig.RHand.position.z:F2}m）");
            }
        }

        [UnityTest]
        public IEnumerator 最後の膝蹴りのあと投げ捨てられてダウンする()
        {
            PlayerGrapple.ForcedWallTossForward = false;   // 仰向け
            yield return Load();
            yield return WaitForGrapple();

            Vector3 start = _player.transform.position;
            float peak = start.y;
            bool airborne = false;
            var stages = new System.Collections.Generic.List<WallStage>();
            float deadline = Time.realtimeSinceStartup + 10f;
            while (_grapple.IsGrappled && Time.realtimeSinceStartup < deadline)
            {
                var stage = _grapple.WallPhase;
                if (stages.Count == 0 || stages[stages.Count - 1] != stage)
                    stages.Add(stage);
                yield return null;
            }

            // 投げられたあとは放物線で飛ぶ。着地まで見る。
            var motor = _player.GetComponent<PlayerMotor>();
            float flightDeadline = Time.realtimeSinceStartup + 2f;
            while (Time.realtimeSinceStartup < flightDeadline)
            {
                peak = Mathf.Max(peak, _player.transform.position.y);
                if (motor != null && motor.IsAirborne)
                    airborne = true;
                if (motor != null && !motor.IsAirborne && _player.transform.position.y < 0.3f)
                    break;
                yield return null;
            }

            yield return null;

            CollectionAssert.AreEqual(new[] { WallStage.Held,
                WallStage.TossWindup }, stages);
            Assert.IsTrue(airborne, "投げ捨てなのに浮いていない（その場で倒れただけ）");
            Assert.Greater(peak, start.y + 0.30f, "投げ上げが低すぎる");
            Assert.AreEqual(80f, _player.State.Hp, 0.1f, "膝蹴り5発のあとに追加ダメージはない");
            Assert.IsFalse(_grapple.IsGrappled);
            Assert.IsTrue(_player.IsDown, "投げ捨てられたらダウンする（レーンで立ち直らない）");
            Assert.IsFalse(_player.DownIsForward, "仰向けに倒れる");
            Assert.Greater(_player.DownRemaining, 0.5f, "すぐ起き上がってはいけない");
            Assert.AreEqual(0f, _player.transform.position.z, 0.06f, "レーンへ戻っている");
        }

        [UnityTest]
        public IEnumerator 投げ捨てのダウンはうつ伏せにもなる()
        {
            PlayerGrapple.ForcedWallTossForward = true;
            yield return Load();
            yield return WaitForGrapple();

            Vector3 start = _player.transform.position;
            float peak = start.y;
            bool airborne = false;

            float deadline = Time.realtimeSinceStartup + 10f;
            while (_grapple.IsGrappled && Time.realtimeSinceStartup < deadline)
            {
                peak = Mathf.Max(peak, _player.transform.position.y);
                yield return null;
            }

            // うつ伏せでも投げ捨ては放物線で飛ぶ（壁際で崩れ落ちるだけにしない）。
            var motor = _player.GetComponent<PlayerMotor>();
            float flightDeadline = Time.realtimeSinceStartup + 2f;
            while (Time.realtimeSinceStartup < flightDeadline)
            {
                peak = Mathf.Max(peak, _player.transform.position.y);
                if (motor != null && motor.IsAirborne)
                    airborne = true;
                if (motor != null && !motor.IsAirborne && _player.transform.position.y < 0.3f)
                    break;
                yield return null;
            }

            yield return null;
            yield return null;

            Assert.IsFalse(_grapple.IsGrappled);
            Assert.IsTrue(airborne, "うつ伏せの投げ捨てなのに浮いていない（壁際で崩れただけ）");
            Assert.Greater(peak, start.y + 0.30f, "うつ伏せの投げ上げが低すぎる");
            Assert.IsTrue(_player.IsDown, "投げ捨てられたらダウンする");
            Assert.IsTrue(_player.DownIsForward, "うつ伏せに倒れる");
            Assert.AreEqual(0f, _player.transform.position.z, 0.06f, "レーンへ戻っている");
        }

        [UnityTest]
        public IEnumerator 壁拘束中は待機役が手前を横切らず左右に下がる()
        {
            yield return Load();
            yield return WaitForGrapple();

            var enemies = Object.FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None);
            var bystanders = System.Array.FindAll(enemies, enemy => enemy != _captor);
            Assert.GreaterOrEqual(bystanders.Length, 2);
            Vector3 center = _player.transform.position;
            center.z = 0f;
            for (int i = 0; i < 2; i++)
            {
                bystanders[i].transform.position = center + Vector3.right * (i == 0 ? -1f : 1f);
                bystanders[i].enabled = true;
            }

            yield return new WaitForSeconds(1.8f);
            Assert.IsTrue(_grapple.IsWallPin);
            Assert.LessOrEqual(bystanders[0].transform.position.x, center.x - 3.9f);
            Assert.GreaterOrEqual(bystanders[1].transform.position.x, center.x + 1.8f);
            for (int i = 0; i < 2; i++)
            {
                Assert.AreEqual(0f, bystanders[i].transform.position.z, 0.01f);
                Assert.AreNotEqual(EnemyPhase.Windup, bystanders[i].Phase);
                Assert.AreNotEqual(EnemyPhase.Attack, bystanders[i].Phase);
            }
        }

        [UnityTest]
        public IEnumerator 壁が出る途中で振りほどくと投げを中止して壁を戻す()
        {
            yield return Load();
            _captor.QueuePin();
            float deadline = Time.realtimeSinceStartup + 5f;
            while (_grapple.WallPhase != WallStage.Rise &&
                   Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.AreEqual(WallStage.Rise, _grapple.WallPhase);
            var wall = WallPinSites.Nearest(_player.transform.position);
            int inputs = Mathf.CeilToInt(_grapple.RequiredInputs);
            for (int i = 0; i < inputs; i++)
                _grapple.Struggle();
            Assert.IsFalse(_grapple.IsGrappled);
            _captor.enabled = false;
            yield return WaitGameSeconds(0.9f);
            Assert.Less(Mathf.Abs(_player.transform.position.z), 0.1f);
            Assert.AreEqual(0f, wall.RiseProgress);
        }

        /// <summary>壁の前（レーン上）に彼女と押さえ役を置く。</summary>
        IEnumerator Load(bool withAssist = false, bool withThird = false)
        {
            yield return TestScene.LoadPrototype();

            _player = Object.FindFirstObjectByType<PlayerStats>();
            Assert.IsNotNull(_player, "PlayerStats がシーンに無い");

            _grapple = _player.GetComponent<PlayerGrapple>();
            Assert.IsNotNull(_grapple, "PlayerGrapple が無い");

            var enemies = Object.FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None);
            Assert.GreaterOrEqual(enemies.Length, 1, "押さえ役が必要");

            _captor = enemies[0];
            _assist = null;
            _third = null;
            for (int i = 1; i < enemies.Length; i++)
            {
                // 3 人がかりのテストでは、3 体目も残す。
                if (withThird && i == 2)
                {
                    _third = enemies[i];
                    enemies[i].enabled = true;
                    continue;
                }

                // 二体がかりのテストでは、2 体目だけ残して横に立たせる。
                if (withAssist && i == 1)
                {
                    _assist = enemies[i];
                    enemies[i].enabled = true;
                    continue;
                }
                enemies[i].enabled = false;
            }

            // 無効化した敵が攻撃トークンを持ったままだと、押さえ役が予備動作に入れない。
            var group = Object.FindFirstObjectByType<EnemyGroup>();
            if (group != null)
                group.ReleaseAllTokens();

            // 波の進行と画面ロックを止める（位置を直接置くため）。
            var encounter = Object.FindFirstObjectByType<EncounterDirector>();
            if (encounter != null)
                encounter.enabled = false;
            var gate = Object.FindFirstObjectByType<BeltScreenLock>();
            if (gate != null)
                gate.enabled = false;

            // 関所が最後に決めた X の制限を外す。残っていると、壁の前へ移した彼女が
            // PlayerMotor.Update で元の位置へ戻される。
            var motor = _player.GetComponent<PlayerMotor>();
            if (motor != null)
            {
                motor.ForwardLimitX = float.PositiveInfinity;
                motor.BackLimitX = float.NegativeInfinity;
            }

            // テスト専用の壁を必ず 1 つ置く。実行時の壁はシーン再読込のタイミングで
            // 生成が間に合わないことがあり、その場合の立ち位置が安定しない（実測）。
            // 既存のテスト壁は作り直して、場所を毎回このテストで決める。
            var existing = WallPinSites.Nearest(_player.transform.position);
            if (existing != null && existing.name == "Test Wall")
                Object.Destroy(existing.gameObject);

            float wallX = _player.transform.position.x;
            var go = new GameObject("Test Wall");
            go.transform.position = new Vector3(wallX, 0f, 1.78f);
            var site = go.AddComponent<WallPinSite>();
            site.FaceZ = 1.78f;
            site.HalfWidth = 3f;
            _wallCenterX = wallX;

            Vector3 lane = _player.transform.position;
            lane.z = 0f;
            lane.x = wallX;
            _player.transform.position = lane;
            _player.transform.rotation = Quaternion.LookRotation(Vector3.right);

            // 押さえ役は正面 1.15m。
            _captor.transform.position = lane + Vector3.right * 1.15f;
            _captor.transform.rotation = Quaternion.LookRotation(Vector3.left);
            _captor.CanPin = true;
            _captor.enabled = true;

            if (_assist != null)
            {
                // 二体目は彼女の反対側（-X）に立たせる。攻撃は止めて、持ち場待ちにする。
                _assist.transform.position = lane + Vector3.left * 1.6f;
                _assist.transform.rotation = Quaternion.LookRotation(Vector3.right);
                _assist.CanPin = false;
                _assist.HoldFire(60f);
            }

            // VRM の読み込みが終わるまで timeScale が 0 のことがある。
            // その間は AI が進まないので、動く状態になってから返す。
            float waitUntil = Time.realtimeSinceStartup + 8f;
            while (Time.realtimeSinceStartup < waitUntil)
            {
                if (Time.timeScale > 0f)
                    break;

                Time.timeScale = 1f;
                yield return null;
            }

            yield return null;
            yield return null;
        }

        /// <summary>
        /// 壁押さえつけが始まるまで待つ。VRM の読み込み中は <c>Time.timeScale = 0</c> になるので、
        /// 実時間で待つだけでは AI が 1 フレームも進まない（実測: 3 件とも掴まれなかった）。
        /// さらに、押さえ役が別の行動（移動・通常攻撃・フェイント）に移ると指示が消えるので、
        /// 掴まれるまで指示を出し直す。
        /// </summary>
        IEnumerator WaitForGrapple()
        {
            // 押さえ役は前のテスト／前の行動で位置が動いている。壁の正面 1.15m に 1 度だけ置き直す。
            // 毎フレーム書き換えると AI の踏み込みと競合して掴まれなくなる（実測: 3 件とも失敗）。
            // 彼女の X も壁の中心へ戻す（前のテストで壁の外へ歩かれていることがある）。
            Vector3 lane = _player.transform.position;
            lane.z = 0f;
            lane.x = _wallCenterX;
            _player.transform.position = lane;
            _player.transform.rotation = Quaternion.LookRotation(Vector3.right);

            _captor.enabled = true;
            _captor.CanPin = true;
            _captor.transform.position = lane + Vector3.right * 1.15f;
            _captor.transform.rotation = Quaternion.LookRotation(Vector3.left);
            _captor.QueuePin();

            float deadline = Time.realtimeSinceStartup + 15f;
            float nextOrder = 0f;
            while ((!_grapple.IsWallPin || _grapple.IsWallTransition) && Time.realtimeSinceStartup < deadline)
            {
                if (Time.timeScale <= 0f)
                    Time.timeScale = 1f;


                // 指示は一度で消える（フェイントや通常攻撃に切り替わる）。0.5 秒ごとに出し直す。
                if (!_grapple.IsGrappled && Time.time >= nextOrder)
                {
                    nextOrder = Time.time + 0.5f;
                    _captor.QueuePin();
                }

                yield return null;
            }
        }

        /// <summary>指定秒だけゲーム内時間を進める（VRM 読込中の timeScale 0 に耐える）。</summary>
        IEnumerator WaitGameSeconds(float seconds)
        {
            float end = Time.time + seconds;
            float deadline = Time.realtimeSinceStartup + seconds * 10f + 5f;
            while (Time.time < end && Time.realtimeSinceStartup < deadline)
            {
                if (Time.timeScale <= 0f)
                    Time.timeScale = 1f;

                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator 壁の前で押さえつけると壁へ押し出される()
        {
            yield return Load();

            _captor.QueuePin();
            yield return WaitForGrapple();

            Assert.IsTrue(_grapple.IsGrappled, "押さえつけが始まらない");
            Assert.IsTrue(_grapple.IsWallPin, "壁の前なのに壁押さえつけにならない");

            yield return WaitGameSeconds(0.4f);

            Assert.Greater(_player.transform.position.z, 1.0f,
                "彼女が壁側へ押し出されていない");
            Assert.Less(_player.transform.position.z, 1.79f,
                "壁を突き抜けている");
            Assert.Less(_captor.transform.position.z, 0.95f,
                "押さえ役が壁との間に立っていない");
        }

        [UnityTest]
        public IEnumerator 壁押さえつけ中に体と髪が壁を貫通しない()
        {
            yield return Load();

            var site = WallPinSites.Nearest(_player.transform.position);
            Assert.IsNotNull(site, "壁が無い");
            float face = site.FaceZ;

            _captor.QueuePin();
            yield return WaitForGrapple();
            Assert.IsTrue(_grapple.IsWallPin, "壁押さえつけが始まらない");

            // 押し出し（0.18 秒）と姿勢のブレンドが終わるまで待つ。
            yield return WaitGameSeconds(0.5f);

            var anim = _player.GetComponentInChildren<CharacterAnimator>();
            Assert.IsNotNull(anim, "CharacterAnimator が無い");
            Assert.IsTrue(anim.Rig != null && anim.Rig.Ready, "リグが完成していない");

            // 体の主要な点が壁の表面より室内側にあること。
            Assert.LessOrEqual(anim.Rig.Hips.position.z, face + 0.001f, "腰が壁を越えている");
            Assert.LessOrEqual(anim.Rig.Chest.position.z, face + 0.001f, "胸が壁を越えている");
            Assert.LessOrEqual(anim.Rig.Head.position.z, face + 0.001f, "頭が壁を越えている");

            // 髪（VRM のスプリングボーン）も壁の手前で止まっていること。
            var joints = _player.GetComponentsInChildren<UniVRM10.VRM10SpringBoneJoint>(true);
            int over = 0;
            float deepest = 0f;
            for (int i = 0; i < joints.Length; i++)
            {
                if (joints[i] == null)
                    continue;

                float d = joints[i].transform.position.z - face;
                if (d <= 0f)
                    continue;

                over++;
                if (d > deepest) deepest = d;
            }

            Assert.AreEqual(0, over, $"髪が壁の内側へ出ている（{over}/{joints.Length}、最深 {deepest:F3}m）");

            // 骨ではなく、実際に描かれるメッシュで判定する。
            // 骨が手前でも、頭の表面や毛束の面は壁へ入ることがある
            // （実測: 骨は全部手前なのに Hair メッシュが +0.004m 越えていた）。
            var mesh = new Mesh();
            try
            {
                foreach (var smr in _player.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (smr == null || !smr.enabled || !smr.gameObject.activeInHierarchy ||
                        smr.sharedMesh == null)
                        continue;

                    smr.BakeMesh(mesh, true);
                    Vector3[] verts = mesh.vertices;
                    Matrix4x4 m = smr.transform.localToWorldMatrix;

                    float maxZ = float.NegativeInfinity;
                    for (int i = 0; i < verts.Length; i++)
                    {
                        float z = m.MultiplyPoint3x4(verts[i]).z;
                        if (z > maxZ) maxZ = z;
                    }

                    Assert.LessOrEqual(maxZ, face,
                        $"{smr.name} の描画メッシュが壁を越えている（{maxZ - face:F3}m）");
                }
            }
            finally
            {
                Object.Destroy(mesh);
            }
        }

        [UnityTest]
        public IEnumerator 壁押さえつけからもがいて抜けるとレーンへ戻る()
        {
            yield return Load();

            _captor.QueuePin();
            yield return WaitForGrapple();
            Assert.IsTrue(_grapple.IsWallPin, "壁押さえつけが始まらない");

            int need = Mathf.CeilToInt(_grapple.RequiredInputs);
            for (int i = 0; i < need; i++)
                _grapple.Struggle();

            yield return null;

            Assert.IsFalse(_grapple.IsGrappled, "もがいても抜けられない");
            Assert.IsFalse(_grapple.IsWallPin, "壁押さえつけの状態が残っている");

            yield return WaitGameSeconds(0.4f);

            Assert.AreEqual(0f, _player.transform.position.z, 0.08f,
                "レーン（z=0）へ戻っていない");
        }

        [UnityTest]
        public IEnumerator 壁から戻るあいだ姿勢が立ち姿へ先に戻らない()
        {
            yield return Load();

            _captor.QueuePin();
            yield return WaitForGrapple();
            Assert.IsTrue(_grapple.IsWallPin, "壁押さえつけが始まらない");

            var anim = _player.GetComponentInChildren<CharacterAnimator>();
            Assert.IsNotNull(anim, "CharacterAnimator が無い");
            Assert.IsTrue(anim.Rig != null && anim.Rig.Ready, "リグが完成していない");

            // 壁に押し付けられている間の腰の高さ（姿勢の基準）。
            yield return WaitGameSeconds(0.5f);
            float pinnedHips = anim.Rig.Hips.position.y - _player.transform.position.y;

            int need = Mathf.CeilToInt(_grapple.RequiredInputs);
            for (int i = 0; i < need; i++)
                _grapple.Struggle();
            yield return null;
            Assert.IsFalse(_grapple.IsWallPin, "壁押さえつけの状態が残っている");

            // レーンへ戻る途中（まだ壁際）。姿勢は壁の反りが残っていること。
            yield return WaitGameSeconds(0.10f);
            float midHips = anim.Rig.Hips.position.y - _player.transform.position.y;

            // 戻りきったあとの立ち姿勢（比較用の基準）。
            yield return WaitGameSeconds(0.5f);
            float idleHips = anim.Rig.Hips.position.y - _player.transform.position.y;

            Assert.Greater(idleHips - pinnedHips, 0.01f,
                $"壁姿勢と立ち姿勢で腰の高さが変わっていない（測れていない）"
                + $" pinned={pinnedHips:F3} mid={midHips:F3} idle={idleHips:F3}");
            Assert.Less(midHips, idleHips - 0.008f,
                $"戻っている途中なのに立ち姿勢へ戻っている（腰が壁姿勢の高さに残っていない）"
                + $" pinned={pinnedHips:F3} mid={midHips:F3} idle={idleHips:F3}");
        }

        // --- 壁・二体がかり（Y 字） ---------------------------------

        [UnityTest]
        public IEnumerator 壁拘束は膝蹴りのあとに猶予を挟んで二人がかりになる()
        {
            yield return Load(withAssist: true);
            _captor.QueuePin();
            yield return WaitForGrapple();
            Assert.IsTrue(_grapple.IsWallPin, "壁押さえつけが始まらない");

            // 膝（拘束役 1 人で 5 発）が先に入り、そのあとに猶予（連打で抜けられる）が挟まる。
            float hp = _player.State.Hp;
            float deadline = Time.realtimeSinceStartup + 14f;
            while (_grapple.WallPauseRemaining <= 0f && !_grapple.IsWallSplitActive &&
                   Time.realtimeSinceStartup < deadline)
                yield return null;

            Assert.Greater(_grapple.WallPauseRemaining, 0f, "膝のあとに猶予が来ない");
            Assert.AreEqual(CombatConstants.WallPauseInputsAfterKnees, _grapple.RequiredInputs, 0.1f,
                "猶予の連打必要数になっていない");
            Assert.IsFalse(_grapple.IsWallSplitActive, "猶予の前に二人がかりが始まっている");
            Assert.Less(_player.State.Hp, hp - CombatConstants.PinKneeDamage * 3f,
                "二人がかりの前に、膝蹴りが入っていない");

            // 猶予が明けると、二人がかりが始まる。
            deadline = Time.realtimeSinceStartup + 14f;
            while (!_grapple.IsWallSplitActive && Time.realtimeSinceStartup < deadline)
                yield return null;

            Assert.IsTrue(_grapple.IsWallSplitActive, "二人がかり（Y 字）が始まらない");
            Assert.AreEqual(_assist, _grapple.WallAssist, "アシストが二体目になっていない");
            Assert.AreEqual(CombatConstants.WallSplitEscapeInputs, _grapple.RequiredInputs, 0.1f,
                "二人がかりの連打必要数になっていない");

            // 手首が左右へ開いている（Y 字）。
            var anim = _player.GetComponentInChildren<CharacterAnimator>();
            Assert.IsNotNull(anim, "CharacterAnimator が無い");
            Assert.IsTrue(anim.Rig != null && anim.Rig.Ready, "リグが完成していない");
            yield return WaitGameSeconds(0.35f);
            float spread = Mathf.Abs(anim.Rig.LHand.position.x - anim.Rig.RHand.position.x);
            Assert.Greater(spread, 0.5f, $"手首が Y 字に開いていない（{spread:F2}m）");
        }

        [UnityTest]
        public IEnumerator 二人がかりの腹パンは交互に6発入る()
        {
            yield return Load(withAssist: true);
            _captor.QueuePin();
            yield return WaitForGrapple();

            var attackers = new System.Collections.Generic.List<Transform>();
            _player.HitReceived += result =>
            {
                if (result.Damage <= 0f)
                    return;

                Transform attacker = _player.LastAttacker;
                if (attacker == _captor.transform || attacker == _assist.transform)
                    attackers.Add(attacker);
            };

            float deadline = Time.realtimeSinceStartup + 12f;
            while (attackers.Count < CombatConstants.WallSplitPunchCount &&
                   Time.realtimeSinceStartup < deadline)
                yield return null;

            Assert.AreEqual(CombatConstants.WallSplitPunchCount, attackers.Count,
                $"二人がかりの腹パンが {attackers.Count} 発しか入っていない");
            for (int i = 0; i < attackers.Count; i++)
            {
                Transform expected = i % 2 == 0 ? _assist.transform : _captor.transform;
                Assert.AreEqual(expected, attackers[i], $"{i + 1} 発目が交互になっていない");
            }

            // 6 発のあとは既存の投げ捨てでダウンする。
            deadline = Time.realtimeSinceStartup + 6f;
            while (!_player.IsDown && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.IsTrue(_player.IsDown, "二人がかりのあと投げ捨てでダウンしない");
        }

        [UnityTest]
        public IEnumerator 二人がかりは連打18回で脱出できる()
        {
            yield return Load(withAssist: true);
            _captor.QueuePin();
            yield return WaitForGrapple();

            float deadline = Time.realtimeSinceStartup + 14f;
            while (!_grapple.IsWallSplitActive && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.IsTrue(_grapple.IsWallSplitActive, "二人がかりが始まらない");

            int need = Mathf.CeilToInt(_grapple.RequiredInputs);
            Assert.AreEqual(Mathf.CeilToInt(CombatConstants.WallSplitEscapeInputs), need,
                "二人がかりの連打必要数が 18 になっていない");
            for (int i = 0; i < need; i++)
                _grapple.Struggle();
            yield return null;

            Assert.IsFalse(_grapple.IsGrappled, "連打しても二人がかりから抜けられない");
            Assert.IsFalse(_grapple.IsWallSplitActive, "二人がかりの状態が残っている");
            Assert.IsFalse(_assist.IsWallAssisting, "アシストが持ち場に残っている");
        }

        [UnityTest]
        public IEnumerator 膝のあとの猶予は連打で脱出できる()
        {
            yield return Load(withAssist: true);
            _captor.QueuePin();
            yield return WaitForGrapple();

            float deadline = Time.realtimeSinceStartup + 14f;
            while (_grapple.WallPauseRemaining <= 0f && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.Greater(_grapple.WallPauseRemaining, 0f, "膝のあとに猶予が来ない");

            int need = Mathf.CeilToInt(_grapple.RequiredInputs);
            Assert.AreEqual(CombatConstants.WallPauseInputsAfterKnees, need);
            for (int i = 0; i < need; i++)
                _grapple.Struggle();
            yield return null;

            Assert.IsFalse(_grapple.IsGrappled, "猶予の間に連打すれば抜けられる");
            Assert.IsFalse(_grapple.IsWallSplitActive, "抜けたのに二人がかりが始まっている");
        }

        [UnityTest]
        public IEnumerator 二人がかりのあとにも猶予が挟まってから解放される()
        {
            yield return Load(withAssist: true);
            _captor.QueuePin();
            yield return WaitForGrapple();

            // 二人がかりが始まって、終わるまで待つ。
            float deadline = Time.realtimeSinceStartup + 14f;
            while (!_grapple.IsWallSplitActive && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.IsTrue(_grapple.IsWallSplitActive, "二人がかりが始まらない");

            deadline = Time.realtimeSinceStartup + 10f;
            while (_grapple.WallSplitHits < CombatConstants.WallSplitPunchCount &&
                   Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.AreEqual(CombatConstants.WallSplitPunchCount, _grapple.WallSplitHits,
                "二人がかりの腹パンが打ち終わらない");

            // すぐには解放されず、猶予（連打 14 回）が挟まる。Y 字（二人が腕を持つ形）は猶予のあいだも続く。
            deadline = Time.realtimeSinceStartup + 3f;
            while (_grapple.WallPauseRemaining <= 0f && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.Greater(_grapple.WallPauseRemaining, 0f, "二人がかりのあとに猶予が来ない");
            Assert.AreEqual(CombatConstants.WallPauseInputsAfterSplit, _grapple.RequiredInputs, 0.1f,
                "猶予の連打必要数になっていない");
            Assert.IsTrue(_grapple.IsGrappled, "猶予の間は拘束が続く");
            Assert.IsTrue(_grapple.IsWallSplitActive, "猶予の間も二人が腕を持ち続ける（Y 字のまま）");

            // 猶予が明けると解放（投げ捨て）。
            deadline = Time.realtimeSinceStartup + 6f;
            while (_grapple.IsGrappled && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.IsFalse(_grapple.IsGrappled, "猶予が明けても解放されない");
        }

        [UnityTest]
        public IEnumerator 三人目が参戦し三人が順番に打つ()
        {
            yield return Load(withAssist: true, withThird: true);
            Assert.IsNotNull(_third, "3 体目の敵が用意できない");
            _captor.QueuePin();
            yield return WaitForGrapple();

            var hits = new System.Collections.Generic.Dictionary<Transform, int>();
            var spots = new System.Collections.Generic.Dictionary<Transform, Vector3>();
            var spotLog = new System.Text.StringBuilder();
            _player.HitReceived += result =>
            {
                if (result.Damage <= 0f || !_grapple.IsWallGangActive)
                    return;
                Transform attacker = _player.LastAttacker;
                if (attacker == null)
                    return;
                hits.TryGetValue(attacker, out int n);
                hits[attacker] = n + 1;

                // 打った瞬間の位置（彼女から見た横・前・距離）。3 人目が画面の外に立っていないかを見る。
                Vector3 offset = attacker.position - _player.transform.position;
                Vector3 front = Vector3.ProjectOnPlane(_player.transform.forward, Vector3.up).normalized;
                Vector3 right = Vector3.Cross(Vector3.up, front);
                spots[attacker] = new Vector3(Vector3.Dot(offset, right), Vector3.Dot(offset, front),
                    new Vector2(offset.x, offset.z).magnitude);
                spotLog.Append($"{attacker.name}:({Vector3.Dot(offset, right):F2},{Vector3.Dot(offset, front):F2}) ");
            };

            // 3 人がかりが始まるまで（膝 → 猶予 → 二人がかり → 猶予）。
            float deadline = Time.realtimeSinceStartup + 40f;
            while (!_grapple.IsWallGangActive && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.IsTrue(_grapple.IsWallGangActive, "3 人がかりが始まらない");

            // 拘束役に近い敵がアシストになる。どちらがどの役かは実際の割り当てを見る。
            EnemyBrain holder = _grapple.WallAssist;
            EnemyBrain third = _grapple.WallThird;
            Assert.IsNotNull(holder, "アシストが持ち続けていない");
            Assert.IsNotNull(third, "3 人目が参戦していない");
            Assert.AreNotEqual(holder, third, "アシストと 3 人目が同じ敵");
            Assert.IsTrue((holder == _assist && third == _third) || (holder == _third && third == _assist),
                "アシストと 3 人目が、用意した 2 体になっていない");

            // 3 人がかりが終わるまで待つ。3 人目の蹴りの突き上げの瞬間は、足と腹の距離を測る。
            float bestFoot = float.MaxValue;
            var hitAnim = _player.GetComponentInChildren<CharacterAnimator>();
            float bestFace = float.MaxValue;
            float torsoMin = float.MaxValue;
            float torsoMax = float.MinValue;
            deadline = Time.realtimeSinceStartup + 40f;
            while (_grapple.IsWallGangActive && Time.realtimeSinceStartup < deadline)
            {
                // 殴られるたびに上体が動いているか（グロッキーの角度が折れの動きを上書きすると動かない）。
                if (hitAnim != null)
                {
                    torsoMin = Mathf.Min(torsoMin, hitAnim.DebugTorsoPitch);
                    torsoMax = Mathf.Max(torsoMax, hitAnim.DebugTorsoPitch);
                }

                // 顔パンの拳が、彼女の顔（頭の実際の位置）に届いているか。
                EnemyBrain facePuncher = _grapple.WallSplitPuncher;
                if (facePuncher != null && _grapple.WallSplitFace && hitAnim != null && hitAnim.Rig != null &&
                    hitAnim.Rig.Head != null && _grapple.WallSplitPunchAge >= CombatConstants.WallGangWindup &&
                    _grapple.WallSplitPunchAge < CombatConstants.WallGangWindup + 0.10f)
                {
                    var faceFigure = facePuncher.GetComponent<EnemyFigure>();
                    Transform fist = faceFigure != null ? faceFigure.Fist(_grapple.WallSplitPunchLeft) : null;
                    if (fist != null)
                        bestFace = Mathf.Min(bestFace, Vector3.Distance(fist.position, hitAnim.Rig.Head.position));
                }
                EnemyBrain kicker = _grapple.WallSplitPuncher;
                if (kicker != null && _grapple.WallSplitKick && _grapple.WallSplitKneeThrust(kicker) > 0.8f)
                {
                    var kickFigure = kicker.GetComponent<EnemyFigure>();
                    Vector3 herPos = _player.transform.position;
                    Vector3 herFront = Vector3.ProjectOnPlane(_player.transform.forward, Vector3.up).normalized;
                    Vector3 bellyPoint = herPos + Vector3.up * 0.86f + herFront * 0.14f;
                    foreach (bool left in new[] { true, false })
                    {
                        Transform ankle = kickFigure.AnkleBone(left);
                        if (ankle != null)
                            bestFoot = Mathf.Min(bestFoot, Vector3.Distance(ankle.position, bellyPoint));
                    }
                }
                yield return null;
            }
            Assert.IsFalse(_grapple.IsWallGangActive, "3 人がかりが終わらない");
            UnityEngine.Debug.Log($"[GangCheck] kickFoot={bestFoot:F2}m torsoRange={torsoMax - torsoMin:F0}deg faceFist={bestFace:F2}m");
            Assert.Less(bestFace, 0.17f, $"顔パンの拳が彼女の顔に届いていない（最接近 {bestFace:F2}m）");
            Assert.Greater(torsoMax - torsoMin, 25f,
                $"殴られても上体が動かない（3 人がかりの間の上体の角度の振れ幅 {torsoMax - torsoMin:F0}°）");
            Assert.Less(bestFoot, 0.40f, $"3 人目の蹴りの足が腹に届いていない（最接近 {bestFoot:F2}m）");

            hits.TryGetValue(_captor.transform, out int captorHits);
            hits.TryGetValue(third.transform, out int thirdHits);
            hits.TryGetValue(holder.transform, out int assistHits);
            UnityEngine.Debug.Log($"[GangCheck] captor={captorHits} third={thirdHits} assist={assistHits}");
            UnityEngine.Debug.Log($"[GangSpots] {spotLog}");
            foreach (var pair in spots)
                UnityEngine.Debug.Log($"[GangSpot] {pair.Key.name} lateral={pair.Value.x:F2} depth={pair.Value.y:F2} dist={pair.Value.z:F2}" +
                                      (pair.Key == third.transform ? " (3人目)" : pair.Key == holder.transform ? " (アシスト)" : " (拘束役)"));

            // 3 人が順番に同じ数ずつ打つ（誰も彼女を持たない）。
            int each = CombatConstants.WallGangStrikes / 3;
            Assert.AreEqual(each, captorHits, "拘束役の打撃の数");
            Assert.AreEqual(each, thirdHits, "3 人目の打撃の数");
            Assert.AreEqual(each, assistHits, "アシストの打撃の数");
        }

        [UnityTest]
        public IEnumerator 三人がかりでは誰も持たず彼女の両腕が下がる()
        {
            yield return Load(withAssist: true, withThird: true);
            Assert.IsNotNull(_third, "3 体目の敵が用意できない");
            _captor.QueuePin();
            yield return WaitForGrapple();

            float deadline = Time.realtimeSinceStartup + 40f;
            while (!_grapple.IsWallGangActive && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.IsTrue(_grapple.IsWallGangActive, "3 人がかりが始まらない");

            var anim = _player.GetComponentInChildren<CharacterAnimator>();
            Assert.IsNotNull(anim, "CharacterAnimator が無い");
            Assert.IsTrue(anim.Rig != null && anim.Rig.Ready, "リグが完成していない");

            // 腕が下がりきるまで待つ（二人が手を離す重みが 1 になるまで）。
            deadline = Time.realtimeSinceStartup + 4f;
            while (_grapple.WallGangWeight < 0.99f && Time.realtimeSinceStartup < deadline)
                yield return null;
            yield return WaitGameSeconds(0.3f);

            float rootY = _player.transform.position.y;
            float leftHandY = anim.Rig.LHand.position.y - rootY;
            float rightHandY = anim.Rig.RHand.position.y - rootY;
            float headY = anim.Rig.Head.position.y - rootY;
            UnityEngine.Debug.Log($"[GangCheck] leftHand={leftHandY:F2} rightHand={rightHandY:F2} head={headY:F2}");

            Assert.Less(leftHandY, 1.05f, $"離された左腕が下がっていない（手の高さ {leftHandY:F2}m）");
            Assert.Less(rightHandY, 1.05f, $"離された右腕も下がっていない（手の高さ {rightHandY:F2}m）");
        }

        // --- 壁拘束の被弾と手首のクリアランス -----------------------

        [UnityTest]
        public IEnumerator 二人がかりの拳は腹に当たる()
        {
            yield return Load(withAssist: true);
            _captor.QueuePin();
            yield return WaitForGrapple();

            float deadline = Time.realtimeSinceStartup + 14f;
            while (!_grapple.IsWallSplitActive && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.IsTrue(_grapple.IsWallSplitActive, "二人がかりが始まらない");

            // 打つ瞬間、拳が腹の正面へ来ているか（胸や顔で止まっていないか）。
            Vector3 her = _player.transform.position;
            Vector3 front = Vector3.ProjectOnPlane(_player.transform.forward, Vector3.up).normalized;
            Vector3 belly = her + Vector3.up * 0.88f + front * 0.12f;

            float best = float.MaxValue;
            deadline = Time.realtimeSinceStartup + 4f;
            while (Time.realtimeSinceStartup < deadline)
            {
                EnemyBrain puncher = _grapple.WallSplitPuncher;
                if (puncher != null)
                {
                    var figure = puncher.GetComponent<EnemyFigure>();
                    Transform fist = figure != null ? figure.Fist(_grapple.WallSplitPunchLeft) : null;
                    if (fist != null)
                        best = Mathf.Min(best, Vector3.Distance(fist.position, belly));
                }
                yield return null;
            }

            Assert.Less(best, 0.16f, $"拳が腹に届いていない（最接近 {best:F2}m）");
        }

        [UnityTest]
        public IEnumerator 二人がかりの締めの膝は腹に届き空き手は腰を掴んで肘が折れすぎない()
        {
            yield return Load(withAssist: true);
            _captor.QueuePin();
            yield return WaitForGrapple();

            float deadline = Time.realtimeSinceStartup + 14f;
            while (!_grapple.IsWallSplitActive && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.IsTrue(_grapple.IsWallSplitActive, "二人がかりが始まらない");

            // 締めの突き上げの瞬間（膝がほぼ最大に上がっている間）だけ測る。
            float bestKnee = float.MaxValue;
            float bestHand = float.MaxValue;
            float minElbow = 180f;
            int samples = 0;
            string kneeLog = string.Empty;

            deadline = Time.realtimeSinceStartup + 9f;
            while (Time.realtimeSinceStartup < deadline && _grapple.IsWallSplitActive)
            {
                EnemyBrain puncher = _grapple.WallSplitPuncher;
                if (puncher != null && _grapple.WallSplitKneeStrike &&
                    _grapple.WallSplitKneeThrust(puncher) > 0.8f)
                {
                    var figure = puncher.GetComponent<EnemyFigure>();
                    Vector3 her = _player.transform.position;
                    Vector3 front = Vector3.ProjectOnPlane(_player.transform.forward, Vector3.up).normalized;
                    Vector3 belly = her + Vector3.up * 0.88f + front * 0.12f;
                    float side = puncher.transform.position.x >= her.x ? 1f : -1f;
                    Vector3 waist = her + Vector3.up * 0.95f + front * 0.08f + Vector3.right * (side * 0.17f);

                    foreach (bool left in new[] { true, false })
                    {
                        Transform knee = figure.KneeBone(left);
                        if (knee != null)
                        {
                            float d = Vector3.Distance(knee.position, belly);
                            if (d < bestKnee)
                            {
                                bestKnee = d;
                                Vector3 flat = puncher.transform.position - her;
                                flat.y = 0f;
                                kneeLog = $"left={left} kneeY={knee.position.y:F2} bellyY={belly.y:F2} " +
                                          $"standoff={flat.magnitude:F2} rootY={puncher.transform.position.y:F2}";
                            }
                        }
                    }

                    bool freeLeft = _grapple.WallSplitPunchLeft;
                    Transform shoulder = figure.ShoulderBone(freeLeft);
                    Transform elbow = figure.ElbowBone(freeLeft);
                    Transform wrist = figure.WristBone(freeLeft);
                    if (shoulder != null && elbow != null && wrist != null)
                    {
                        bestHand = Mathf.Min(bestHand, Vector3.Distance(wrist.position, waist));
                        minElbow = Mathf.Min(minElbow,
                            Vector3.Angle(shoulder.position - elbow.position, wrist.position - elbow.position));
                    }

                    samples++;
                }

                yield return null;
            }

            Debug.Log($"[KneeCheck] samples={samples} knee={bestKnee:F2}m hand={bestHand:F2}m elbow={minElbow:F0}deg {kneeLog}");
            Assert.Greater(samples, 0, "締めの突き上げを一度も測れていない");
            // 膝の関節の中心から腹の点までの距離。膝の丸み（約 8cm）があるので、0.15m 以内なら
            // 膝が腹の表面に当たっている（関節の角度だけで振り上げると 0.25m 前後で腹の横を通る）。
            Assert.Less(bestKnee, 0.15f, $"締めの膝が腹に届いていない（最接近 {bestKnee:F2}m）");
            Assert.Less(bestHand, 0.25f, $"空き手が彼女の腰を掴めていない（最接近 {bestHand:F2}m）");
            Assert.Greater(minElbow, 70f, $"空き手の肘が折れすぎている（最小 {minElbow:F0}°）");
        }

        [UnityTest]
        public IEnumerator 二人がかりの腹パンで壁に固定されたまま上体が折れる()
        {
            yield return Load(withAssist: true);
            _captor.QueuePin();
            yield return WaitForGrapple();

            float deadline = Time.realtimeSinceStartup + 14f;
            while (!_grapple.IsWallSplitActive && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.IsTrue(_grapple.IsWallSplitActive, "二人がかりが始まらない");

            var anim = _player.GetComponentInChildren<CharacterAnimator>();
            Assert.IsNotNull(anim, "CharacterAnimator が無い");
            Assert.IsTrue(anim.Rig != null && anim.Rig.Ready, "リグが完成していない");

            // Y 字へ開ききり、1 発目が当たる前（振りかぶり中）の姿勢を基準にする。
            // （二人がかりでは手首は壁ではなく正面へ出るので、壁のクリアランスは 1 体のときのテストで見る）
            yield return WaitGameSeconds(0.30f);

            float baseChestZ = anim.Rig.Chest.position.z;
            float baseHipsY = anim.Rig.Hips.position.y;
            float baseHeadZ = anim.Rig.Head.position.z;

            // 1 発目が当たって折れきるまで待つ。
            deadline = Time.realtimeSinceStartup + 3f;
            while (anim.HeldBellyPunchFold < 0.6f && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.Greater(anim.HeldBellyPunchFold, 0.6f, "腹パンの被弾姿勢が始まらない");

            // フレームの位相に依存しないよう、当たった直後 0.40 秒の最大の動きで見る
            // （次の一発は 0.55 秒後なので、その前の戻りまでを測れる）。
            float minChestZ = baseChestZ;
            float minHipsY = baseHipsY;
            float minHeadZ = baseHeadZ;
            float sampleUntil = Time.realtimeSinceStartup + 0.40f;
            while (Time.realtimeSinceStartup < sampleUntil)
            {
                minChestZ = Mathf.Min(minChestZ, anim.Rig.Chest.position.z);
                minHipsY = Mathf.Min(minHipsY, anim.Rig.Hips.position.y);
                minHeadZ = Mathf.Min(minHeadZ, anim.Rig.Head.position.z);
                yield return null;
            }

            Assert.Greater(baseChestZ - minChestZ, 0.03f,
                $"上体が前へ折れていない（胸 z {baseChestZ:F2}→{minChestZ:F2}）");
            Assert.Less(minHipsY - baseHipsY, -0.02f,
                $"膝が抜けて腰が沈んでいない（腰 y {baseHipsY:F2}→{minHipsY:F2}）");
            Assert.Greater(baseHeadZ - minHeadZ, 0.10f,
                $"頭が前へ落ちていない（頭 z {baseHeadZ:F2}→{minHeadZ:F2}）");

            // 殴られても腕は振りほどかれず、頭上に残る（Y 字が崩れない）。
            Assert.Greater(anim.Rig.LHand.position.y, anim.Rig.Head.position.y,
                "左手首が頭より下へ落ちた");
            Assert.Greater(anim.Rig.RHand.position.y, anim.Rig.Head.position.y,
                "右手首が頭より下へ落ちた");

            // 次の一発（0.55 秒間隔）の前に戻る。戻らないと連打で固まって見える。
            Assert.Less(anim.HeldBellyPunchFold, 0.5f,
                $"折れた姿勢から戻らない（fold={anim.HeldBellyPunchFold:F2}）");
            Assert.Less(Mathf.Abs(anim.Rig.Chest.position.z - baseChestZ), 0.05f,
                "上体が折れたまま戻らない");
        }

        [UnityTest]
        public IEnumerator 壁に貼り付いた手首と手のひらが壁へ埋まらない()
        {
            yield return Load();

            var site = WallPinSites.Nearest(_player.transform.position);
            Assert.IsNotNull(site, "壁が無い");
            float face = site.FaceZ;

            _captor.QueuePin();
            yield return WaitForGrapple();
            Assert.IsTrue(_grapple.IsWallPin, "壁押さえつけが始まらない");

            var anim = _player.GetComponentInChildren<CharacterAnimator>();
            Assert.IsNotNull(anim, "CharacterAnimator が無い");
            Assert.IsTrue(anim.Rig != null && anim.Rig.Ready, "リグが完成していない");

            yield return WaitGameSeconds(0.7f);
            AssertHandClearance(anim, face);
        }

        [UnityTest]
        public IEnumerator 二人がかりのカメラは正面寄りで三人が画に入る()
        {
            yield return Load(withAssist: true);
            _captor.QueuePin();
            yield return WaitForGrapple();

            float deadline = Time.realtimeSinceStartup + 14f;
            while (!_grapple.IsWallSplitActive && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.IsTrue(_grapple.IsWallSplitActive, "二人がかりが始まらない");
            yield return WaitGameSeconds(0.35f);

            Vector3 her = _player.transform.position;
            WallSplitCamera.Build(her, _player.transform.forward,
                _captor.transform.position, _assist.transform.position,
                out Vector3 position, out Vector3 target);

            // 壁の奥へ回り込まない（壁は +Z。彼女は部屋側を向く）。
            var site = WallPinSites.Nearest(her);
            Assert.IsNotNull(site, "壁が無い");
            Assert.Less(position.z, site.FaceZ - 0.5f, "カメラが壁の奥へ回り込んでいる");

            // 斜め（既定 18°）。正面ぴったりではない。
            Vector3 front = Vector3.ProjectOnPlane(_player.transform.forward, Vector3.up).normalized;
            Vector3 toCamera = Vector3.ProjectOnPlane(position - target, Vector3.up).normalized;
            float yaw = Vector3.Angle(front, toCamera);
            if (WallSplitCamera.Mode == WallSplitCamera.Shot.Angle)
                Assert.That(yaw, Is.InRange(0f, 20f),
                    $"二体がかりのカメラが正面寄りになっていない（{yaw:F0}°）");

            // 三人とも画に入る。実カメラは動かさず、同じ画角の探りカメラで投影する。
            var main = Camera.main;
            var probeGo = new GameObject("WallSplit Camera Probe");
            var probe = probeGo.AddComponent<Camera>();
            probe.fieldOfView = main != null ? main.fieldOfView : 50f;
            probe.aspect = main != null ? main.aspect : 1.57f;
            probe.transform.position = position;
            probe.transform.rotation = Quaternion.LookRotation(target - position, Vector3.up);

            Vector3[] subjects =
            {
                her + Vector3.up * 1.05f,
                _captor.transform.position + Vector3.up * 1.02f,
                _assist.transform.position + Vector3.up * 1.02f,
            };
            foreach (Vector3 p in subjects)
            {
                Vector3 view = probe.WorldToViewportPoint(p);
                Assert.Greater(view.z, 0f, "三人の誰かがカメラの後ろにある");
                Assert.That(view.x, Is.InRange(0.02f, 0.98f),
                    $"三人の誰かが画面の外（x={view.x:F2}）");
                Assert.That(view.y, Is.InRange(0.02f, 0.98f),
                    $"三人の誰かが画面の外（y={view.y:F2}）");
            }

            Object.Destroy(probeGo);
        }

        /// <summary>
        /// 手首と前腕が壁へ埋まり、指先は壁に触れていることを測る。
        /// 手首の位置だけでなく、前腕（手首寄り）と指の付け根も見る。
        /// </summary>
        static void AssertHandClearance(CharacterAnimator anim, float face)
        {
            var rig = anim.Rig;
            for (int i = 0; i < 2; i++)
            {
                bool left = i == 0;
                string name = left ? "左" : "右";
                Transform hand = left ? rig.LHand : rig.RHand;
                Transform lower = left ? rig.LLowerArm : rig.RLowerArm;

                float wristGap = face - hand.position.z;
                Assert.Greater(wristGap, 0.06f,
                    $"{name}手首が壁へ埋まっている（壁から {wristGap:F3}m）");

                Vector3 forearm = Vector3.Lerp(lower.position, hand.position, 0.85f);
                float forearmGap = face - forearm.z;
                Assert.Greater(forearmGap, 0.045f,
                    $"{name}前腕が壁へ埋まっている（壁から {forearmGap:F3}m）");

                // 手首は浮かせたまま、掌を壁へ向けて傾けて指先だけ触れさせる。
                // （指の骨は VRM の制御階層側にあり位置が取れないので、掌の向きで測る。）
                Vector3 palm = rig.PalmNormalWorld(left);
                Assert.Greater(palm.sqrMagnitude, 0.5f, $"{name}の掌の向きが取れない");
                float tilt = Vector3.Angle(palm, Vector3.forward);   // 壁の法線（部屋側から壁へ）
                Assert.That(tilt, Is.InRange(4f, 22f),
                    $"{name}の掌が壁へ傾いていない（法線から {tilt:F0}°）");
            }
        }
    }
}
