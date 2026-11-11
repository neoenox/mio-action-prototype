using System.Collections;
using NUnit.Framework;
using Ryona.Game;
using UnityEngine;
using UnityEngine.TestTools;

namespace Ryona.Tests
{
    /// <summary>
    /// 頭上拘束（両手首を頭上へ固定）の回帰テスト。
    ///
    /// 背後から掴むのは羽交い締めと同じだが、時間切れが無く、
    /// 両手首が頭の上へ来るのが見せ場。手首の位置は IK で決めているので、
    /// 方向ベクトルだけで組めていないかを実際の骨で確かめる。
    /// </summary>
    public sealed class OverheadRuntimeTests
    {
        PlayerStats _player;
        PlayerGrapple _grapple;
        EnemyBrain _captor;
        CharacterAnimator _animator;

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            if (_grapple != null && _grapple.IsGrappled)
                _grapple.Release();
        }

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

            var group = Object.FindFirstObjectByType<EnemyGroup>();
            if (group != null)
                group.ReleaseAllTokens();

            var encounter = Object.FindFirstObjectByType<EncounterDirector>();
            if (encounter != null)
                encounter.enabled = false;
            var gate = Object.FindFirstObjectByType<BeltScreenLock>();
            if (gate != null)
                gate.enabled = false;

            var motor = _player.GetComponent<PlayerMotor>();
            if (motor != null)
            {
                motor.ForwardLimitX = float.PositiveInfinity;
                motor.BackLimitX = float.NegativeInfinity;
            }

            // VRM の読み込み中は timeScale が 0 になり、ポーズも進まない。
            float deadline = Time.realtimeSinceStartup + 20f;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (_animator == null)
                    _animator = _player.GetComponentInChildren<CharacterAnimator>();

                if (_animator != null && _animator.Rig != null && _animator.Rig.Ready &&
                    Time.timeScale > 0f)
                    break;

                if (Time.timeScale <= 0f)
                    Time.timeScale = 1f;

                yield return null;
            }

            Assert.IsNotNull(_animator, "CharacterAnimator が無い");
            Assert.IsTrue(_animator.Rig != null && _animator.Rig.Ready, "リグが完成していない");

            // 彼女は +X 向き。押さえ役は背後（-X）に立たせる。
            Vector3 lane = _player.transform.position;
            lane.z = 0f;
            _player.transform.position = lane;
            _player.transform.rotation = Quaternion.LookRotation(Vector3.right);

            _captor.transform.position = lane + Vector3.left * 1.0f;
            _captor.transform.rotation = Quaternion.LookRotation(Vector3.right);
            _captor.enabled = false;

            yield return null;
            yield return null;
        }

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

        IEnumerator BeginWallHold()
        {
            Vector3 lane = _player.transform.position;
            var wall = new GameObject("Overhead Test Wall").AddComponent<WallPinSite>();
            wall.transform.position = new Vector3(lane.x, 0f, 1.78f);
            wall.FaceZ = 1.78f;
            wall.HalfWidth = 3f;
            _captor.transform.SetPositionAndRotation(lane + Vector3.right * 1.15f,
                Quaternion.LookRotation(Vector3.left));
            _captor.CanPin = true;
            _captor.enabled = true;
            _captor.QueuePin();
            float deadline = Time.realtimeSinceStartup + 5f;
            while ((!_grapple.IsGrappled || _grapple.IsWallTransition) && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.IsTrue(_grapple.IsWallPin);
            Assert.AreEqual(GrappleKind.Pin, _grapple.Kind);
            Assert.IsTrue(_grapple.HasTimeLimit);
        }

        [UnityTest]
        public IEnumerator 通常の背後からの掴みは羽交い締めになる()
        {
            yield return Load();
            _captor.enabled = true;
            _captor.QueueGrab();
            float deadline = Time.realtimeSinceStartup + 5f;
            while (!_grapple.IsGrappled && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.IsTrue(_grapple.IsGrappled);
            Assert.AreEqual(GrappleKind.Back, _grapple.Kind);
            Assert.IsFalse(_grapple.IsWallPin);
            yield return WaitGameSeconds(0.4f);
            Assert.Less(_animator.Rig.LHand.position.y, _animator.Rig.Head.position.y);
            Assert.Less(_animator.Rig.RHand.position.y, _animator.Rig.Head.position.y);
        }

        [UnityTest]
        public IEnumerator 両手首が頭の上へ固定される()
        {
            yield return Load();

            yield return BeginWallHold();
            Assert.IsTrue(_grapple.IsGrappled);

            // 貼り付き（0.40 秒）が終わるまで待つ。
            yield return WaitGameSeconds(0.7f);

            Vector3 head = _animator.Rig.Head.position;
            Vector3 handL = _animator.Rig.LHand.position;
            Vector3 handR = _animator.Rig.RHand.position;

            Assert.Greater(handL.y, head.y + 0.10f,
                $"左手首が頭の上に無い（head={head.y:F2} handL={handL.y:F2}）");
            Assert.Greater(handR.y, head.y + 0.10f,
                $"右手首が頭の上に無い（head={head.y:F2} handR={handR.y:F2}）");

            // 手首は壁の表面へ貼り付く。
            float face = _grapple.WallFaceZ;
            Assert.Less(face - handL.z, 0.12f, $"左手首が壁から離れている（{face - handL.z:F2}m）");
            Assert.Less(face - handR.z, 0.12f, $"右手首が壁から離れている（{face - handR.z:F2}m）");

            // 押さえ役は彼女の腕（前腕）を掴む。腹や胸ではなく腕に手があること。
            var figure = _captor.GetComponent<EnemyFigure>();
            Vector3 chest = _animator.Rig.Chest.position;
            Vector3 foreL = Vector3.Lerp(_animator.Rig.LLowerArm.position, handL, 0.22f);
            Vector3 foreR = Vector3.Lerp(_animator.Rig.RLowerArm.position, handR, 0.22f);
            float holdL = Vector3.Distance(figure.WristBone(false).position, foreL);
            float holdR = Vector3.Distance(figure.WristBone(true).position, foreR);
            Assert.Less(holdL, 0.22f, $"押さえ役の右手が彼女の左前腕に無い（{holdL:F2}m）");
            Assert.Less(holdR, 0.22f, $"押さえ役の左手が彼女の右前腕に無い（{holdR:F2}m）");
            Assert.Greater(foreL.y, chest.y, "掴む位置が腹ではなく腕（胸より上）に無い");
            Assert.Greater(foreR.y, chest.y, "掴む位置が腹ではなく腕（胸より上）に無い");
            Assert.Greater(handL.z, _animator.Rig.Chest.position.z,
                "左手首が壁側（胸より奥）に無い");
            Assert.Greater(handR.z, _animator.Rig.Chest.position.z,
                "右手首が壁側（胸より奥）に無い");

            // 両手が揃っている（頭上で一箇所にまとめられている）。
            Assert.Less(Mathf.Abs(handL.x - handR.x), 0.35f,
                "両手首が離れすぎている");
            Assert.Less(Mathf.Abs(handL.z - handR.z), 0.35f,
                "両手首が前後に行き違っている");

            // 腕が伸びきって「吊られている」だけになっていないこと。
            float reachL = Vector3.Distance(_animator.Rig.LUpperArm.position, handL);
            float armL = Vector3.Distance(_animator.Rig.LUpperArm.position, _animator.Rig.LLowerArm.position)
                + Vector3.Distance(_animator.Rig.LLowerArm.position, handL);
            Assert.Less(reachL, armL - 0.01f, "左腕が伸びきっている");

            float reachR = Vector3.Distance(_animator.Rig.RUpperArm.position, handR);
            float armR = Vector3.Distance(_animator.Rig.RUpperArm.position, _animator.Rig.RLowerArm.position)
                + Vector3.Distance(_animator.Rig.RLowerArm.position, handR);
            Assert.Less(reachR, armR - 0.01f, "右腕が伸びきっている");
        }

        [UnityTest]
        public IEnumerator もがき12回で振りほどけ押さえた側がよろける()
        {
            yield return Load();

            yield return BeginWallHold();
            _captor.enabled = true;

            int need = Mathf.CeilToInt(_grapple.RequiredInputs);
            for (int i = 0; i < need - 1; i++)
            {
                _grapple.Struggle();
                Assert.IsTrue(_grapple.IsGrappled, $"{i + 1} 回目でもがききっていない");
            }

            _grapple.Struggle();
            yield return null;

            Assert.IsFalse(_grapple.IsGrappled, "12 回もがけば振りほどける");
            Assert.AreEqual(EnemyPhase.Stagger, _captor.Phase,
                "振りほどかれた側はよろける（反撃の隙）");
        }
    }
}
