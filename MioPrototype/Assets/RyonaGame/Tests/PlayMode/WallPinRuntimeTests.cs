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

        /// <summary>テストで置いた壁の中心 X。押さえ役を毎回ここ基準で置き直す。</summary>
        float _wallCenterX;

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
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
            var stages = new System.Collections.Generic.List<PlayerGrapple.WallStage>();
            float hp = _player.State.Hp;
            float deadline = Time.realtimeSinceStartup + 7f;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (_grapple.IsWallPin)
                {
                    var stage = _grapple.WallPhase;
                    if (stages.Count == 0 || stages[stages.Count - 1] != stage)
                        stages.Add(stage);
                    if (stage == PlayerGrapple.WallStage.Rise)
                        Assert.Less(Mathf.Abs(_player.transform.position.z), 0.1f,
                            "壁が出る間はまだ投げられない");
                    if (stage == PlayerGrapple.WallStage.Throw)
                        Assert.AreEqual(1f, WallPinSites.Nearest(_player.transform.position).RiseProgress);
                    if (stage == PlayerGrapple.WallStage.Slam)
                    {
                        var shake = Object.FindFirstObjectByType<CameraShake>();
                        Assert.IsTrue(shake != null && shake.IsShaking, "壁に当たった瞬間にカメラが揺れる");
                    }
                    if (stage == PlayerGrapple.WallStage.Held)
                        break;
                }
                yield return null;
            }
            CollectionAssert.AreEqual(new[] {
                PlayerGrapple.WallStage.Grab, PlayerGrapple.WallStage.Rise,
                PlayerGrapple.WallStage.Throw, PlayerGrapple.WallStage.Slam,
                PlayerGrapple.WallStage.Lean, PlayerGrapple.WallStage.Regrip,
                PlayerGrapple.WallStage.Restrain, PlayerGrapple.WallStage.Held
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
            float deadline = Time.realtimeSinceStartup + 8f;
            while (Time.realtimeSinceStartup < deadline && _grapple.IsGrappled)
            {
                float hipsY = figure.HipsBone.position.y - _captor.transform.position.y;
                highestHips = Mathf.Max(highestHips, hipsY);

                if (_grapple.KneePulse > 0.10f && anim.Rig != null && anim.Rig.Ready &&
                    anim.Rig.Spine != null && anim.Rig.Chest != null)
                {
                    Vector3 spine = anim.Rig.Spine.position;
                    Vector3 chest = anim.Rig.Chest.position;
                    Vector3 front = Vector3.ProjectOnPlane(_player.transform.forward, chest - spine).normalized;
                    Vector3 axis = Vector3.Lerp(spine, chest, 0.60f);

                    for (int side = 0; side < 2; side++)
                    {
                        var knee = figure.KneeBone(side == 0);
                        if (knee == null)
                            continue;

                        float depth = Vector3.Dot(knee.position - axis, front);
                        float penetration = surface + kneeRadius - depth;
                        worst = Mathf.Max(worst, penetration);
                    }
                }

                yield return null;
            }

            Assert.Greater(worst, float.NegativeInfinity, "膝蹴りが一度も起きていない");
            Assert.Less(worst, 0.05f, $"膝頭が腹にめり込んでいる（{worst:F3}m）");
            Assert.Less(highestHips, restHipsY + 0.05f,
                $"膝蹴りのあと押さえ役が浮き上がっている（腰 {highestHips:F2}m / 立ち {restHipsY:F2}m）");
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
        public IEnumerator 最後の膝蹴りのあと元のレーンへ投げられて着地する()
        {
            yield return Load();
            yield return WaitForGrapple();

            Vector3 start = _player.transform.position;
            Vector3 thrower = _captor.transform.position;
            float peak = start.y;
            var stages = new System.Collections.Generic.List<PlayerGrapple.WallStage>();
            float deadline = Time.realtimeSinceStartup + 10f;
            while (_grapple.IsGrappled && Time.realtimeSinceStartup < deadline)
            {
                var stage = _grapple.WallPhase;
                if (stages.Count == 0 || stages[stages.Count - 1] != stage)
                    stages.Add(stage);
                if (stage == PlayerGrapple.WallStage.TossAir)
                {
                    peak = Mathf.Max(peak, _player.transform.position.y);
                    Assert.Less(Vector3.Distance(thrower, _captor.transform.position), 0.05f,
                        "投げる側は飛んでいる相手について行かない");
                }
                yield return null;
            }

            CollectionAssert.AreEqual(new[] { PlayerGrapple.WallStage.Held,
                PlayerGrapple.WallStage.TossWindup, PlayerGrapple.WallStage.TossAir,
                PlayerGrapple.WallStage.TossLand }, stages);
            Assert.Greater(peak, start.y + 0.20f);
            Assert.AreEqual(80f, _player.State.Hp, 0.1f, "膝蹴り5発のあとに追加ダメージはない");
            Assert.IsFalse(_grapple.IsGrappled);
            Assert.AreEqual(0f, _player.transform.position.z, 0.02f);
            Assert.AreEqual(start.y, _player.transform.position.y, 0.04f);
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
            while (_grapple.WallPhase != PlayerGrapple.WallStage.Rise &&
                   Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.AreEqual(PlayerGrapple.WallStage.Rise, _grapple.WallPhase);
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
        IEnumerator Load()
        {
            yield return TestScene.LoadPrototype();

            _player = Object.FindFirstObjectByType<PlayerStats>();
            Assert.IsNotNull(_player, "PlayerStats がシーンに無い");

            _grapple = _player.GetComponent<PlayerGrapple>();
            Assert.IsNotNull(_grapple, "PlayerGrapple が無い");

            var enemies = Object.FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None);
            Assert.GreaterOrEqual(enemies.Length, 1, "押さえ役が必要");

            _captor = enemies[0];
            for (int i = 1; i < enemies.Length; i++)
                enemies[i].enabled = false;

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
    }
}
