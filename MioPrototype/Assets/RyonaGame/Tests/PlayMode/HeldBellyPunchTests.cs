using System.Collections;
using System.Reflection;
using NUnit.Framework;
using Ryona.Game;
using UnityEngine;
using UnityEngine.TestTools;

namespace Ryona.Tests
{
    /// <summary>
    /// 後ろ拘束中の腹パン演出の回帰テスト。
    ///
    /// 後ろ拘束は時間切れで解けない。別の敵の腹パンチが当たると
    /// 「く」の字＋カメラ寄せが始まり、演出後に解放される。
    /// 顔パンチや拘束者自身の攻撃では発動しない。
    /// </summary>
    public sealed class HeldBellyPunchTests
    {
        PlayerStats _player;
        PlayerGrapple _grapple;
        EnemyBrain _captor;
        EnemyBrain _attacker;
        EnemyBrain[] _enemies;
        HeldBellyPunchDirector _director;
        CharacterAnimator _anim;
        ThirdPersonCamera _camera;

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            if (_grapple != null)
                _grapple.Release();
        }

        /// <summary>シーンを読み、後ろ拘束を1つ作る。攻撃役は正面に置く。</summary>
        IEnumerator LoadAndGrab(bool waitForAnimator = false, bool directorEnabled = true)
        {
            yield return TestScene.LoadPrototype();

            _player = Object.FindFirstObjectByType<PlayerStats>();
            Assert.IsNotNull(_player, "PlayerStats がシーンに無い");

            _grapple = _player.GetComponent<PlayerGrapple>();
            Assert.IsNotNull(_grapple, "PlayerGrapple が無い");

            _director = Object.FindFirstObjectByType<HeldBellyPunchDirector>();
            Assert.IsNotNull(_director, "HeldBellyPunchDirector が自動で付いていない");
            _director.enabled = directorEnabled;

            _camera = Object.FindFirstObjectByType<ThirdPersonCamera>();
            Assert.IsNotNull(_camera, "ThirdPersonCamera が無い");

            _enemies = Object.FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None);
            Assert.GreaterOrEqual(_enemies.Length, 2, "拘束役と腹パン役の2体が必要");
            _captor = _enemies[0];
            _attacker = _enemies[1];

            for (int i = 2; i < _enemies.Length; i++)
                _enemies[i].enabled = false;

            // プレイヤーは +X 向き。拘束者は背後（-X）、腹パン役は正面（+X）。
            _player.transform.rotation = Quaternion.LookRotation(Vector3.right);
            _captor.transform.position = _player.transform.position + Vector3.left;
            _captor.transform.rotation = Quaternion.LookRotation(Vector3.right);
            _attacker.enabled = false;

            _grapple.Begin(_captor, GrappleKind.Back);
            yield return null;

            Assert.IsTrue(_grapple.IsGrappled, "Back grab should connect");

            if (!waitForAnimator)
                yield break;

            float deadline = Time.realtimeSinceStartup + 8f;
            while (Time.realtimeSinceStartup < deadline)
            {
                _anim = _player.GetComponentInChildren<CharacterAnimator>();
                if (_anim != null && _anim.Rig != null && _anim.Rig.Ready)
                    break;
                yield return null;
            }

            Assert.IsTrue(_anim != null && _anim.Rig != null && _anim.Rig.Ready,
                "キャラクターのリグが組み上がらない（VRM の読み込み待ちで時間切れ）");
        }

        void PlaceAttackerInFront(float distance = 1.15f)
        {
            Vector3 forward = _player.transform.forward;
            forward.y = 0f;
            forward = forward.sqrMagnitude > 1e-4f ? forward.normalized : Vector3.right;

            _attacker.transform.position = _player.transform.position + forward * distance;
            _attacker.transform.rotation = Quaternion.LookRotation(-forward, Vector3.up);
        }

        HitResult Hit(EnemyBrain source, PunchZone zone)
        {
            var attack = new AttackData
            {
                id = "test_body",
                kind = AttackKind.Light,
                damage = 1f,
                postureDamage = 0f,
                partDamage = 1f,
                targetPart = BodyPart.Torso,
                startupSeconds = 0f,
                activeSeconds = 0f,
                recoverySeconds = 0f
            };

            // 実機では EnemyBrain.Strike が LastHitZone を立ててから ReceiveHit を呼ぶ。
            _player.LastHitZone = zone;
            return _player.ReceiveHit(source.State, attack, source.transform);
        }

        [UnityTest]
        public IEnumerator 後ろ拘束は時間切れでは解けない()
        {
            yield return LoadAndGrab(directorEnabled: false);

            Assert.IsFalse(_grapple.HasTimeLimit, "Back hold must have no time limit");

            yield return new WaitForSeconds(3.4f);

            Assert.IsTrue(_grapple.IsGrappled, "Back hold must not release on a timer");
            Assert.Greater(_grapple.HeldSeconds, 3f);
        }

        [UnityTest]
        public IEnumerator 腹パンでくの字とカメラ寄せが始まり5発後に解放される()
        {
            yield return LoadAndGrab(waitForAnimator: true);
            PlaceAttackerInFront();

            Hit(_attacker, PunchZone.Body);
            // コンボの残り4発は敵自身に振らせる必要があるので有効化する。
            // ただし AI が勝手に別の攻撃を始めないよう、攻撃予約だけ止めておく。
            _attacker.HoldFire(10f);
            _attacker.enabled = true;
            yield return null;

            Assert.IsTrue(_director.IsPerforming, "Belly punch combo must start");
            Assert.AreEqual(1, _director.ComboHitsLanded, "The first hit must be counted");
            Assert.IsTrue(_grapple.IsStruggleSuppressed, "Struggle must stop during the combo");
            Assert.IsTrue(_anim.IsHeldBellyPunch, "Held belly punch pose must play");
            Assert.Less(_camera.EffectDistanceScale, 0.6f, "Camera must push in");
            Assert.Greater(_camera.EffectPivotOffset, 0.2f, "Camera pivot must rise to the belly/face");
            Assert.IsTrue(_camera.IsFocusing, "Camera must center on the player");

            // 0.3 秒後: まだ拘束中で、くの字のピーク付近
            yield return new WaitForSeconds(0.30f);
            Assert.IsTrue(_grapple.IsGrappled, "The hold must remain during the combo");
            Assert.Greater(_anim.HeldBellyPunchFold, 0.5f, "The body must fold");

            // 5 発を食らってから解放される
            float deadline = Time.realtimeSinceStartup + 6f;
            while (_director.IsPerforming && Time.realtimeSinceStartup < deadline)
                yield return null;

            Assert.IsFalse(_director.IsPerforming, "The combo must end");
            Assert.AreEqual(5, _director.ComboHitsLanded, "The player must take 5 belly punches");
            Assert.IsFalse(_grapple.IsGrappled, "The hold must be released after the combo");
        }

        [UnityTest]
        public IEnumerator 顔パンチでは腹パン演出が始まらない()
        {
            yield return LoadAndGrab();
            PlaceAttackerInFront();

            Hit(_attacker, PunchZone.Face);
            yield return null;

            Assert.IsFalse(_director.IsPerforming, "A face punch must not start the belly punch");
            Assert.IsTrue(_grapple.IsGrappled, "The hold must remain after a face punch");
        }

        [UnityTest]
        public IEnumerator 拘束者自身の攻撃では腹パン演出が始まらない()
        {
            yield return LoadAndGrab();

            Hit(_captor, PunchZone.Body);
            yield return null;

            Assert.IsFalse(_director.IsPerforming, "The captor must not trigger the tag-team belly punch");
            Assert.IsTrue(_grapple.IsGrappled, "The hold must remain");
        }

        [UnityTest]
        public IEnumerator 連打で先に逃げると腹パン演出は始まらない()
        {
            yield return LoadAndGrab();

            var escape = typeof(PlayerGrapple).GetMethod("Escape",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(escape, "Escape not found");
            escape.Invoke(_grapple, null);

            yield return new WaitForSeconds(1.8f);

            Assert.IsFalse(_grapple.IsGrappled, "The hold must end after escaping");
            Assert.IsFalse(_director.IsPerforming, "No belly punch after escaping");
        }

        [UnityTest]
        public IEnumerator 拘束中の敵に腹パンを指示できる()
        {
            yield return LoadAndGrab();
            PlaceAttackerInFront();

            Assert.IsTrue(_attacker.TryStartBellyPunch(), "The tag-team belly punch must start");
            Assert.AreEqual(PunchZone.Body, _attacker.Zone, "The punch must aim at the belly");
            Assert.AreEqual(EnemyPhase.Windup, _attacker.Phase, "The enemy must enter windup");
        }
    }
}
