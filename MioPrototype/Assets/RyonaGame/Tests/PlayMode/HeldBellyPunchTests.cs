using System.Collections;
using System.Reflection;
using NUnit.Framework;
using Ryona.Game;
using UnityEngine;
using UnityEngine.TestTools;

namespace Ryona.Tests
{
    /// <summary>
    /// 後ろ拘束中のタッグ技（腹パン / 顔面パンチ）の回帰テスト。
    ///
    /// 後ろ拘束は時間切れで解けない。別の敵のパンチが当たると
    /// 「く」の字（腹）または首振り（顔）＋カメラ寄せが始まる。
    /// 5 発を撃ち終えても拘束は続き、抜けるには連打（もがき）が必要。
    /// 拘束者自身の攻撃では発動しない。
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
        public IEnumerator 腹パンでくの字とカメラ寄せが始まり5発後も拘束が続く()
        {
            yield return LoadAndGrab(waitForAnimator: true);

            // このテストは「く」の字と拘束の継続だけを見る。狙いを腹に固定する。
            _director.FacePunchChance = 0f;
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
            Assert.IsTrue(_camera.IsFocusing, "Camera must center on the player");

            // 0.3 秒後: まだ拘束中で、くの字のピーク付近
            yield return new WaitForSeconds(0.30f);
            Assert.IsTrue(_grapple.IsGrappled, "The hold must remain during the combo");
            Assert.Greater(_anim.HeldBellyPunchFold, 0.5f, "The body must fold");

            // 5 発を食らっても拘束は続く
            float deadline = Time.realtimeSinceStartup + 10f;
            while (_director.IsPerforming && Time.realtimeSinceStartup < deadline)
                yield return null;

            Assert.IsFalse(_director.IsPerforming, "The combo must end");
            Assert.AreEqual(5, _director.ComboHitsLanded, "The player must take 5 belly punches");
            Assert.IsTrue(_grapple.IsGrappled, "The hold must continue after the combo");
            Assert.IsFalse(_grapple.IsStruggleSuppressed, "Mashing must work right after the combo");

            // 数秒のクールタイム後に、また 5 発が始まる（拘束は続いたまま）
            float secondDeadline = Time.realtimeSinceStartup + 6f;
            while (!_director.IsPerforming && Time.realtimeSinceStartup < secondDeadline)
                yield return null;

            Assert.IsTrue(_director.IsPerforming, "A second combo must start after the cooldown");
            Assert.IsTrue(_grapple.IsGrappled, "The hold must stay during the second combo");

            // 連打で抜けられる（抜けたら拘束終了）
            var escape = typeof(PlayerGrapple).GetMethod("Escape",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(escape, "Escape not found");
            escape.Invoke(_grapple, null);
            yield return null;

            Assert.IsFalse(_grapple.IsGrappled, "Mashing must free the player after the combo");
        }

        [UnityTest]
        public IEnumerator 顔面パンチでもコンボが始まり首が手前へ振れる()
        {
            yield return LoadAndGrab(waitForAnimator: true);
            _director.FacePunchChance = 1f;   // 顔面に固定
            PlaceAttackerInFront();

            Hit(_attacker, PunchZone.Face);
            yield return null;

            Assert.IsTrue(_director.IsPerforming, "A face punch must start the combo too");
            Assert.AreEqual(PunchZone.Face, _director.LastLandedZone, "The landed zone must be the face");
            Assert.IsTrue(_anim.IsHeldFacePunch, "The face-punch reaction must play");
            Assert.IsFalse(_anim.IsHeldBellyPunch, "A face punch must not fold the body");

            yield return new WaitForSeconds(0.12f);
            Assert.Greater(_anim.HeldFacePunchFlick, 0.5f, "The head must snap away toward the camera");
            Assert.IsTrue(_grapple.IsGrappled, "The hold must remain during the combo");
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
        public IEnumerator 左向きでもカメラは正面側に来る()
        {
            yield return LoadAndGrab();

            // 左向きで組み直す（拘束者は +X 側＝背中側）
            _grapple.Release();
            _player.transform.rotation = Quaternion.LookRotation(Vector3.left);
            _captor.transform.position = _player.transform.position + Vector3.right;
            _captor.transform.rotation = Quaternion.LookRotation(Vector3.left);
            _grapple.Begin(_captor, GrappleKind.Back);
            yield return null;

            PlaceAttackerInFront();
            Hit(_attacker, PunchZone.Body);
            yield return null;

            Assert.IsTrue(_camera.IsFocusing, "Camera must focus during the combo");

            Vector3 toCamera = _camera.transform.position - _player.transform.position;
            toCamera.y = 0f;
            Assert.Greater(Vector3.Dot(_player.transform.forward, toCamera.normalized), 0.3f,
                "Camera must stay on the player's front side when facing left");
        }

        [UnityTest]
        public IEnumerator 拘束中の敵に腹パンを指示できる()
        {
            yield return LoadAndGrab();
            PlaceAttackerInFront();

            Assert.IsTrue(_attacker.TryStartTagPunch(PunchZone.Body), "The tag-team belly punch must start");
            Assert.AreEqual(PunchZone.Body, _attacker.Zone, "The punch must aim at the belly");
            Assert.AreEqual(EnemyPhase.Windup, _attacker.Phase, "The enemy must enter windup");
        }

        [UnityTest]
        public IEnumerator 拘束中の敵に顔面パンチも指示できる()
        {
            yield return LoadAndGrab();
            PlaceAttackerInFront();

            Assert.IsTrue(_attacker.TryStartTagPunch(PunchZone.Face), "The tag-team face punch must start");
            Assert.AreEqual(PunchZone.Face, _attacker.Zone, "The punch must aim at the face");
            Assert.AreEqual(EnemyPhase.Windup, _attacker.Phase, "The enemy must enter windup");
        }

        /// <summary>
        /// タッグ技（腹 / 顔）は片手で打つ。左腕を胸の前へ構えたままだと、上体を前へ倒す
        /// 腹パンの姿勢と重なって「両手で押している」画になる（実測でそう見えていた）。
        /// </summary>
        [UnityTest]
        public IEnumerator 腹パンは片手で打ち反対の手は狙いから離れている()
        {
            yield return AssertOneHandedTagPunch(PunchZone.Body);
        }

        [UnityTest]
        public IEnumerator 顔面タッグパンチも片手で打つ()
        {
            yield return AssertOneHandedTagPunch(PunchZone.Face);
        }

        /// <summary>
        /// 打たない腕は腰の横へ下ろさず、脇を締めて拳を胸の前に構える。
        /// 下ろすと上体を前へ倒す腹パンの姿勢と重なって「腕がだらりと下がった」画になる。
        /// </summary>
        [UnityTest]
        public IEnumerator 打たない腕は脇を締めて胸の前に構える()
        {
            // 演出は拘束 1.6 秒後に自分でパンチを振るため、先に始まると Phase が
            // Windup のままになり、こちらから指示できない。手で 1 発振らせるので切る。
            yield return LoadAndGrab(waitForAnimator: true, directorEnabled: false);
            PlaceAttackerInFront();
            _attacker.HoldFire(10f);
            Assert.IsTrue(_attacker.TryStartTagPunch(PunchZone.Body, holdPoseSeconds: 0.3f),
                "タッグ技が始まらない");
            _attacker.enabled = true;

            var figure = _attacker.GetComponent<EnemyFigure>();
            Vector3 target = EnemyBrain.PunchTarget(_player, PunchZone.Body, _attacker.BodyTargetBias);
            var fistR = figure.Fist(false);
            var fistL = figure.Fist(true);

            // 右の拳が一番伸びた瞬間の、左の拳の位置を測る
            float bestRight = float.MaxValue;
            float leftHeight = 0f;
            float leftForward = 0f;
            float deadline = Time.realtimeSinceStartup + 3f;
            while (Time.realtimeSinceStartup < deadline)
            {
                float r = Vector3.Distance(fistR.position, target);
                if (r < bestRight)
                {
                    bestRight = r;
                    Vector3 local = _attacker.transform.InverseTransformPoint(fistL.position);
                    leftHeight = local.y;
                    leftForward = local.z;
                }

                if (bestRight < 0.30f)
                    break;

                yield return null;
            }

            Vector3 hip = _attacker.transform.InverseTransformPoint(figure.HipBone(false).position);

            Assert.Greater(leftForward, 0.20f, "打たない腕は胴の前（拳を構える位置）へ出すこと");
            Assert.Greater(leftHeight - hip.y, 0.25f, "打たない腕は腰まで下ろさず、胸の高さに構えること");
            Assert.Greater(Vector3.Distance(fistL.position, target), 0.45f,
                "打たない腕は狙いへ届かせない（両手で押している画になる）");
        }

        /// <summary>
        /// 打つ手（右）が狙いに届き、反対の手（左）は腰の横へ引いてあることを確かめる。
        /// 一番伸びた瞬間（右の拳が狙いに最も近づいたフレーム）で左右を比べる。
        /// </summary>
        IEnumerator AssertOneHandedTagPunch(PunchZone zone)
        {
            // 演出（HeldBellyPunchDirector）は拘束 1.6 秒後に自分でパンチを振る。
            // リグの読み込み待ちの間に始まって Phase が Windup のままだと、こちらから
            // 指示できない。このテストは手で 1 発振らせるので演出は切っておく。
            yield return LoadAndGrab(waitForAnimator: true, directorEnabled: false);
            PlaceAttackerInFront();

            _attacker.HoldFire(10f);
            Assert.IsTrue(_attacker.TryStartTagPunch(zone, holdPoseSeconds: 0.3f),
                $"タッグ技（{zone}）が始まらない");
            _attacker.enabled = true;

            var figure = _attacker.GetComponent<EnemyFigure>();
            Assert.IsNotNull(figure, "EnemyFigure が無い");

            Vector3 target = EnemyBrain.PunchTarget(_player, zone, _attacker.BodyTargetBias);
            var fistR = figure.Fist(false);
            var fistL = figure.Fist(true);
            Assert.IsNotNull(fistR, "右の拳が無い");
            Assert.IsNotNull(fistL, "左の拳が無い");

            float bestRight = float.MaxValue;
            float leftAtBest = float.MaxValue;
            float deadline = Time.realtimeSinceStartup + 3f;
            while (Time.realtimeSinceStartup < deadline)
            {
                float r = Vector3.Distance(fistR.position, target);
                if (r < bestRight)
                {
                    bestRight = r;
                    leftAtBest = Vector3.Distance(fistL.position, target);
                }

                if (bestRight < 0.30f)
                    break;

                yield return null;
            }

            Assert.Less(bestRight, 0.35f, "打つ手（右）は狙いへ届いていること");
            Assert.Greater(leftAtBest, 0.45f, "反対の手（左）は腰の横へ引いてあること");
            Assert.Greater(leftAtBest, bestRight + 0.20f, "打つ手だけが前へ出ていること");
        }
    }
}
