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
    /// 上体の反動（腹）または首振り（顔）＋カメラ寄せが始まる。
    /// 5 発食らい切ると、腹の蓄積でダウンするか、引き渡しまたは拘束継続になる。
    /// 途中で諦めた場合だけ拘束が続く。
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
        IEnumerator LoadAndGrab(bool waitForAnimator = false, bool directorEnabled = true, float? throwDelay = null)
        {
            yield return TestScene.LoadPrototype();

            _player = Object.FindFirstObjectByType<PlayerStats>();
            Assert.IsNotNull(_player, "PlayerStats がシーンに無い");

            _grapple = _player.GetComponent<PlayerGrapple>();
            Assert.IsNotNull(_grapple, "PlayerGrapple が無い");

            _director = Object.FindFirstObjectByType<HeldBellyPunchDirector>();
            Assert.IsNotNull(_director, "HeldBellyPunchDirector が自動で付いていない");
            _director.enabled = directorEnabled;
            _director.FacePunchChance = 0f;
            if (throwDelay.HasValue)
                _director.ThrowDelaySeconds = throwDelay.Value;

            // 個別のテストで指定する場合を除き、受け渡しの抽選を止める。
            var handOff = Object.FindFirstObjectByType<GrappleHandOff>();
            if (handOff != null)
                handOff.Chance = 0f;

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

        [UnityTest]
        public IEnumerator RearCaptorFollowsBellyRecoilButStaysBracedForFacePunch()
        {
            yield return LoadAndGrab(waitForAnimator: true, directorEnabled: false);
            Vector3 center = _player.transform.position;
            center.x = -3f;
            Quaternion facing = Quaternion.LookRotation(Vector3.right);
            _player.transform.SetPositionAndRotation(center, facing);
            _captor.transform.SetPositionAndRotation(center + Vector3.left * 0.34f, facing);
            _captor.TakeOverHold(10f);
            yield return new WaitForSeconds(0.25f);

            var figure = _captor.GetComponent<EnemyFigure>();
            float headDepth = _captor.transform.InverseTransformPoint(figure.HeadBone.position).z;
            Vector3 hipsBefore = _anim.Rig.Hips.position;

            _anim.PlayHeldBellyPunch(0.55f);
            yield return new WaitForSeconds(0.14f);
            float bellyDepth = _captor.transform.InverseTransformPoint(figure.HeadBone.position).z;
            Assert.Greater(_anim.DebugTorsoPitch, 27f, "The upper body folds over the abdomen.");
            Assert.Less(Vector3.Dot(_anim.Rig.Hips.position - hipsBefore, _player.transform.forward), -0.075f,
                "The waist travels in the direction of the impact.");
            Assert.Greater(bellyDepth, headDepth + 0.04f, "The captor bends with the player's folded upper body.");

            yield return new WaitForSeconds(0.5f);
            _anim.PlayHeldFacePunch(0.55f);
            yield return new WaitForSeconds(0.10f);
            float faceDepth = _captor.transform.InverseTransformPoint(figure.HeadBone.position).z;
            Assert.AreEqual(headDepth, faceDepth, 0.04f, "The captor stays braced during the face punch.");
            Assert.AreSame(_captor, _grapple.Captor);
        }

        void PlaceAttackerInFront(float distance = 1.15f)
        {
            Vector3 forward = _player.transform.forward;
            forward.y = 0f;
            forward = forward.sqrMagnitude > 1e-4f ? forward.normalized : Vector3.right;

            _attacker.transform.position = _player.transform.position + forward * distance;
            _attacker.transform.rotation = Quaternion.LookRotation(-forward, Vector3.up);
        }

        HitResult Hit(EnemyBrain source, PunchZone zone, bool isTagPunch = true)
        {
            var attack = new AttackData
            {
                id = "test_body",
                kind = AttackKind.Light,
                damage = 1f,
                partDamage = 1f,
                targetPart = BodyPart.Torso,
                startupSeconds = 0f,
                activeSeconds = 0f,
                recoverySeconds = 0f
            };

            // 実機では EnemyBrain.Strike が LastHitZone を立ててから ReceiveHit を呼ぶ。
            _player.LastHitZone = zone;
            return _player.ReceiveHit(source.State, attack, source.transform, isTagPunch: isTagPunch);
        }

        void LandOpeningThrow()
        {
            PlaceAttackerInFront(0.82f);
            _attacker.enabled = true;
            Assert.IsTrue(_attacker.TryStartHeldThrow());
            Assert.IsFalse(_director.IsPerforming, "The windup is not a landed throw.");
            const BindingFlags privateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(EnemyBrain).GetMethod("TickWindup", privateInstance)
                .Invoke(_attacker, new object[] { 10f });
            Assert.AreEqual(EnemyPhase.Attack, _attacker.Phase);
            typeof(EnemyBrain).GetMethod("Strike", privateInstance)
                .Invoke(_attacker, new object[] { Vector3.Distance(_attacker.transform.position, _player.transform.position) });
            Assert.IsTrue(_director.IsPerforming, "The landed throw opens the combo.");
            Assert.AreEqual(0, _director.ComboHitsLanded, "A throw does not count as a punch.");
        }

        [UnityTest]
        public IEnumerator 演出の段階が投げ待ちからコンボへ進む()
        {
            // 投げの発注は 5 秒先にしておき、段階の遷移だけを決定的に見る。
            yield return LoadAndGrab(waitForAnimator: true, throwDelay: 5f);

            Assert.AreEqual(HeldBellyPunchDirector.Phase.WaitingThrow, _director.CurrentPhase);
            Assert.Greater(_director.NextThrowRemaining, 0f, "投げ待ちの残り秒が出る");
            Assert.IsFalse(_director.IsPerforming);

            PlaceAttackerInFront();
            LandOpeningThrow();

            Assert.AreEqual(HeldBellyPunchDirector.Phase.Combo, _director.CurrentPhase,
                "投げが決まったらコンボの段階へ移る");
            Assert.IsTrue(_director.IsPerforming);

            // 5 発と余韻が終わると、コンボの段階からは抜ける（引き渡し待ち / 次の投げ待ち）。
            _attacker.HoldFire(10f);
            _attacker.enabled = true;
            float deadline = Time.realtimeSinceStartup + 12f;
            while (_director.IsPerforming && Time.realtimeSinceStartup < deadline)
                yield return null;

            Assert.IsFalse(_director.IsPerforming, "コンボが終われば段階が戻る");
            Assert.AreNotEqual(HeldBellyPunchDirector.Phase.Combo, _director.CurrentPhase);
            Assert.IsTrue(
                _director.CurrentPhase == HeldBellyPunchDirector.Phase.HandOffWait ||
                _director.CurrentPhase == HeldBellyPunchDirector.Phase.WaitingThrow,
                $"コンボ後は引き渡し待ちか再投げ待ち（実際: {_director.CurrentPhase}）");
        }

        [UnityTest]
        public IEnumerator 通常パンチはタッグ連撃を開始せず途中の打数にも入らない()
        {
            yield return LoadAndGrab();
            PlaceAttackerInFront();

            float hp = _player.State.Hp;
            Hit(_attacker, PunchZone.Body, isTagPunch: false);
            Assert.IsFalse(_director.IsPerforming);
            Assert.Less(_player.State.Hp, hp, "An ordinary punch still deals damage.");
            Assert.AreEqual(0f, _player.HeldBellyDamage);

            Hit(_attacker, PunchZone.Body);
            Assert.IsFalse(_director.IsPerforming, "Even a tagged punch cannot open the combo.");
            LandOpeningThrow();
            Hit(_attacker, PunchZone.Body);
            Hit(_attacker, PunchZone.Face, isTagPunch: false);
            Assert.AreEqual(1, _director.ComboHitsLanded);
        }

        [UnityTest]
        public IEnumerator OpeningThrowStepsIntoReachBeforeContact()
        {
            yield return LoadAndGrab(throwDelay: 10f);
            _captor.TakeOverHold(10f);
            _attacker.MinPlayerGap = 0.95f;
            _attacker.HoldFire(10f);
            PlaceAttackerInFront(0.82f);
            _attacker.enabled = true;
            Assert.IsTrue(_attacker.TryStartHeldThrow());

            float deadline = Time.realtimeSinceStartup + 4f;
            while (!_director.IsPerforming && Time.realtimeSinceStartup < deadline)
                yield return null;

            Assert.IsTrue(_director.IsPerforming);
            Assert.Less(Vector3.Distance(_attacker.transform.position, _player.transform.position), 0.75f,
                "The thrower must get close enough to reach the player's shoulders.");
            Assert.AreEqual(0, _director.ComboHitsLanded);
        }

        [UnityTest]
        public IEnumerator BellyPuncherKeepsLowGuardAfterRetractingFist()
        {
            yield return LoadAndGrab(throwDelay: 10f);
            _captor.TakeOverHold(10f);
            _attacker.HoldFire(10f);
            float rigDeadline = Time.realtimeSinceStartup + 8f;
            while (_player.GetComponentInChildren<CharacterAnimator>() == null &&
                Time.realtimeSinceStartup < rigDeadline)
                yield return null;
            Assert.IsNotNull(_player.GetComponentInChildren<CharacterAnimator>());
            Object.FindFirstObjectByType<EnemyGroup>().ReleaseAllTokens();
            _director.FacePunchChance = 0f;
            _director.ComboIntervalSeconds = 0.65f;
            LandOpeningThrow();

            float deadline = Time.realtimeSinceStartup + 4f;
            while (_director.ComboHitsLanded == 0 && Time.realtimeSinceStartup < deadline)
                yield return null;

            Assert.AreEqual(1, _director.ComboHitsLanded);
            var chest = _attacker.GetComponent<EnemyFigure>().ChestBone;
            yield return new WaitForSeconds(0.18f);
            float contactHeight = chest.position.y;
            yield return new WaitForSeconds(0.25f);

            Assert.AreEqual(1, _director.ComboHitsLanded);
            Assert.IsFalse(_attacker.IsBellyPunch, "The fist should already be retracting.");
            Assert.Less(chest.position.y - contactHeight, 0.11f,
                "The puncher should keep a low guard between punches.");
        }

        [UnityTest]
        public IEnumerator OrdinaryPunchFollowsThroughDuringRearHold()
        {
            yield return LoadAndGrab(directorEnabled: false);
            _captor.TakeOverHold(10f);
            PlaceAttackerInFront(0.82f);
            _attacker.enabled = true;
            _attacker.FeintChance = 1f;

            const BindingFlags privateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
            var begin = typeof(EnemyBrain).GetMethod("TryBeginAttack", privateInstance);
            Assert.IsTrue((bool)begin.Invoke(_attacker, new object[] { 0.82f }));
            Assert.AreEqual(EnemyPhase.Windup, _attacker.Phase);
            Assert.IsFalse(_attacker.IsTagPunch);

            typeof(EnemyBrain).GetMethod("TickWindup", privateInstance)
                .Invoke(_attacker, new object[] { 10f });

            Assert.AreEqual(EnemyPhase.Attack, _attacker.Phase,
                "A regular punch should follow through while another enemy holds the player.");
            Assert.IsFalse(_director.IsPerforming);
        }

        [UnityTest]
        public IEnumerator FrontEnemyAutomaticallyThrowsBeforeStartingTheCombo()
        {
            yield return LoadAndGrab(throwDelay: 0.3f);
            _captor.TakeOverHold(10f);
            _attacker.enabled = true;
            _attacker.HoldFire(10f);
            _attacker.transform.position = _player.transform.position - _player.transform.forward * 0.9f;
            Assert.IsFalse(_attacker.TryStartHeldThrow(), "An enemy behind the player cannot open the combo.");
            PlaceAttackerInFront(1.02f);

            float deadline = Time.realtimeSinceStartup + 6f;
            while (!_director.IsThrowIncoming && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.IsTrue(_director.IsThrowIncoming, "A front enemy should start the throw windup.");
            Assert.IsFalse(_director.IsPerforming);

            float hp = _player.State.Hp;
            while (!_director.IsPerforming && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.IsTrue(_director.IsPerforming, "Landing the throw starts the combo.");
            Assert.AreEqual(hp, _player.State.Hp, 0.01f, "The throw only changes her stance, it does not deal damage.");
            Assert.IsFalse(_player.IsStunned, "The throw must not stun.");
            Assert.AreEqual(0, _director.ComboHitsLanded);
            Assert.AreSame(_captor, _grapple.Captor);

            float firstPunchDeadline = Time.time + 0.30f;
            while (_director.ComboHitsLanded == 0 && Time.time < firstPunchDeadline &&
                Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.AreEqual(1, _director.ComboHitsLanded, "The first punch follows the reach immediately.");
            Assert.AreEqual(PunchZone.Body, _director.LastLandedZone);
            Assert.AreSame(_attacker.transform, _player.LastAttacker, "The thrower follows through with the punch.");
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
        public IEnumerator 腹パンで被弾姿勢とカメラ寄せが始まり抽選に外れると拘束が続く()
        {
            yield return LoadAndGrab(waitForAnimator: true);

            // 被弾姿勢と拘束の継続を見るため、狙いを腹に固定する。
            _director.FacePunchChance = 0f;
            PlaceAttackerInFront();

            LandOpeningThrow();
            // 5発は敵自身に振らせる。
            // ただし AI が勝手に別の攻撃を始めないよう、攻撃予約だけ止めておく。
            _attacker.HoldFire(10f);
            _attacker.enabled = true;
            yield return null;

            Assert.IsTrue(_director.IsPerforming, "Belly punch combo must start");
            Assert.AreEqual(0, _director.ComboHitsLanded, "The throw does not count as a punch");
            Assert.IsTrue(_grapple.IsStruggleSuppressed, "Struggle must stop during the combo");
            Assert.IsTrue(_anim.IsHeldThrowLead, "The throw bends her into the belly-punch stance");
            Assert.IsFalse(_anim.IsHeldBellyPunch, "The opening throw is a transition, not a punch reaction");

            // 投げから一発目まで構図と拘束をつなぐ。
            float focusDeadline = Time.realtimeSinceStartup + 3f;
            while (_director.ComboHitsLanded == 0 && Time.realtimeSinceStartup < focusDeadline)
                yield return null;
            Assert.AreEqual(1, _director.ComboHitsLanded);
            Assert.IsTrue(_grapple.IsGrappled, "The hold must remain during the combo");
            _director.FacePunchChance = 1f;
            Assert.Greater(_anim.HeldThrowLead, 0.6f, "The throw leaves her in the belly stance");
            Assert.IsTrue(_anim.IsHeldBellyPunch, "The first punch starts her recoil");
            Assert.IsTrue(_camera.IsFocusing, "Camera must center on the player for the punches");

            // 5 発を食らっても拘束は続く
            float deadline = Time.realtimeSinceStartup + 10f;
            while ((_director.IsPerforming || _director.NextThrowRemaining <= 0f)
                && Time.realtimeSinceStartup < deadline)
                yield return null;

            Assert.IsFalse(_director.IsPerforming, "The combo must end");
            Assert.AreEqual(5, _director.ComboHitsLanded, "The player must take 5 belly punches");
            Assert.AreEqual(PunchZone.Body, _director.LastLandedZone);
            Assert.IsTrue(_grapple.IsGrappled, "The original hold must continue");
            Assert.AreSame(_captor, _grapple.Captor);
            Assert.Greater(_director.NextThrowRemaining, 0f, "Schedule the next opening throw");
            Assert.IsFalse(_player.IsDown, "One combo is below the belly threshold");
        }

        [UnityTest]
        public IEnumerator カメラが元に戻ってから間を置いて拘束を引き渡す()
        {
            yield return LoadAndGrab(waitForAnimator: true);

            var handOff = Object.FindFirstObjectByType<GrappleHandOff>();
            Assert.IsNotNull(handOff);
            handOff.Chance = 1f;
            _director.FacePunchChance = 0f;
            PlaceAttackerInFront();
            _attacker.HoldFire(10f);
            LandOpeningThrow();

            float deadline = Time.realtimeSinceStartup + 10f;
            while (_director.ComboHitsLanded < _director.ComboHitsTotal
                && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.AreEqual(_director.ComboHitsTotal, _director.ComboHitsLanded);

            // 引き渡し先が攻撃中・硬直中だと抽選に落ちる（仕様）。
            // テストでは抽選を決定的にするため、他の敵を構えに戻してから待つ。
            foreach (var enemy in _enemies)
                if (enemy != _captor)
                    enemy.HoldFire(30f);

            float quiet = Time.realtimeSinceStartup + 5f;
            while (Time.realtimeSinceStartup < quiet)
            {
                bool busy = false;
                foreach (var enemy in _enemies)
                {
                    if (enemy == _captor)
                        continue;
                    if (enemy.Phase == EnemyPhase.Windup || enemy.Phase == EnemyPhase.Attack ||
                        enemy.Phase == EnemyPhase.Recover)
                        busy = true;
                }

                if (!busy)
                    break;
                yield return null;
            }

            // EndRecover が _attackReadyAt を上書きするので、構えに戻ってからもう一度止める。
            foreach (var enemy in _enemies)
                if (enemy != _captor)
                    enemy.HoldFire(30f);

            deadline = Time.realtimeSinceStartup + 5f;
            while ((_camera.IsFocusing || !Mathf.Approximately(_camera.EffectDistanceScale, 1f)
                || !Mathf.Approximately(_camera.EffectPivotOffset, 0f))
                && Time.realtimeSinceStartup < deadline)
            {
                Assert.AreSame(_captor, _grapple.Captor, "カメラの演出中は元の拘束を保つ");
                yield return null;
            }
            Assert.Less(Time.realtimeSinceStartup, deadline, "カメラが通常の構図に戻る");
            yield return new WaitForSeconds(0.7f);
            Assert.AreSame(_captor, _grapple.Captor, "構図が戻った直後も元の拘束を保つ");
            Assert.IsFalse(_grapple.IsStruggleSuppressed, "受け渡しを待つ間ももがける");

            deadline = Time.realtimeSinceStartup + 3f;
            while (_grapple.Captor == _captor && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.IsTrue(_grapple.IsGrappled, "待った後は次の敵へ引き渡す");
            Assert.AreNotSame(_captor, _grapple.Captor);
            Assert.IsTrue(_captor.IsHandOffLocked, "渡す側が引き渡し中にロックされていない");
            Assert.IsTrue(_grapple.IsHandingOff, "最初は突き飛ばしの途中");
            Assert.IsFalse(_grapple.Captor.IsHolding, "突き飛ばしと拘束を同時に始めない");

            // 以前は 0.9m 突き飛ばしてから掴み位置へ瞬間移動していた。
            // 通常フレームで 1 フレームの移動が大きすぎないことを見る（ワープ防止）。
            Vector3 prev = _player.transform.position;
            float maxStep = 0f;
            float observeUntil = Time.realtimeSinceStartup + 2f;
            bool sawCatch = false;
            while (_grapple.IsHandingOff && Time.realtimeSinceStartup < observeUntil)
            {
                yield return null;
                Vector3 now = _player.transform.position;
                if (Time.deltaTime < 0.05f)
                    maxStep = Mathf.Max(maxStep, Vector3.Distance(prev, now));
                prev = now;
                sawCatch |= _grapple.Captor != null && _grapple.Captor.IsCatching && !_grapple.Captor.IsHolding;
            }

            Assert.IsTrue(sawCatch, "掴む前に受け止めの姿勢を取る");
            Assert.IsFalse(_grapple.IsHandingOff);
            Assert.IsTrue(_grapple.Captor != null && _grapple.Captor.IsHolding, "受け止めた後で拘束する");
            Assert.Less(maxStep, 0.25f, $"受け渡しで瞬間移動している（1 フレーム {maxStep:F2}m）");
            float gap = Vector3.Distance(_player.transform.position, _grapple.Captor.transform.position);
            Assert.Less(gap, 0.8f, $"受け渡し後の距離が離れすぎている（{gap:F2}m）");
        }

        [UnityTest]
        public IEnumerator 投げから派生した連撃の顔面パンチで首が手前へ振れる()
        {
            yield return LoadAndGrab(waitForAnimator: true);
            _director.FacePunchChance = 1f;
            PlaceAttackerInFront();

            LandOpeningThrow();
            _director.FacePunchChance = 0f;
            _attacker.HoldFire(10f);
            float deadline = Time.realtimeSinceStartup + 5f;
            while (_director.ComboHitsLanded < 1 && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.AreEqual(1, _director.ComboHitsLanded);

            Assert.IsTrue(_director.IsPerforming, "The combo continues after a face punch");
            Assert.AreEqual(PunchZone.Face, _director.LastLandedZone, "The landed zone must be the face");
            Assert.IsTrue(_anim.IsHeldFacePunch, "The face-punch reaction must play");
            Assert.IsFalse(_anim.IsHeldBellyPunch, "A face punch must not fold the body");

            yield return new WaitForSeconds(0.12f);
            Assert.Greater(_anim.HeldFacePunchFlick, 0.5f, "The head must snap away toward the camera");
            Assert.IsTrue(_grapple.IsGrappled, "The hold must remain during the combo");

            deadline = Time.realtimeSinceStartup + 10f;
            while (_director.ComboHitsLanded < 5 && Time.realtimeSinceStartup < deadline)
            {
                Assert.AreEqual(PunchZone.Face, _director.LastLandedZone);
                yield return null;
            }
            Assert.AreEqual(5, _director.ComboHitsLanded);
            Assert.AreEqual(PunchZone.Face, _director.LastLandedZone);
        }

        [UnityTest]
        public IEnumerator 投げは食らいにならず腹パン体勢へ移行する()
        {
            // リグの読み込み待ちの間に拘束役が殴ると、投げと関係ないスタンが残る。
            // 拘束の状態は掴んだときに決まっているので、待つ前に AI を止める。
            yield return LoadAndGrab();
            _captor.enabled = false;
            float rigDeadline = Time.realtimeSinceStartup + 8f;
            while (_anim == null && Time.realtimeSinceStartup < rigDeadline)
            {
                _anim = _player.GetComponentInChildren<CharacterAnimator>();
                yield return null;
            }
            Assert.IsNotNull(_anim);

            PlaceAttackerInFront(1.02f);
            _attacker.enabled = true;
            _attacker.HoldFire(10f);

            int hits = 0;
            System.Action<HitResult> countHit = _ => hits++;
            _player.HitReceived += countHit;
            float hp = _player.State.Hp;
            LandOpeningThrow();
            _player.HitReceived -= countHit;

            Assert.AreEqual(0, hits, "The throw must not go through the damage path");
            Assert.AreEqual(hp, _player.State.Hp, 0.01f, "The throw does not deal damage");
            Assert.IsFalse(_player.IsStunned, "The throw does not stun");
            Assert.IsTrue(_anim.IsHeldThrowLead, "She is pulled into the belly-punch stance");
            Assert.IsFalse(_anim.IsHeldBellyPunch, "The transition is not a punch reaction");
            Assert.Less(_anim.HeldBellyPunchFold, 0.05f, "The throw must not double her over");

            // 最初の一発で崩れを引き継ぎ、当てられた反応に切り替わる。
            float deadline = Time.realtimeSinceStartup + 3f;
            while (!_anim.IsHeldBellyPunch && Time.realtimeSinceStartup < deadline)
                yield return null;

            Assert.IsTrue(_anim.IsHeldBellyPunch, "The first punch takes over from the throw");
            Assert.AreEqual(1, _director.ComboHitsLanded);
            Assert.AreEqual(CharacterAnimator.State.Grabbed, _anim.Current, "She stays held");
        }

        [UnityTest]
        public IEnumerator 拘束者自身の攻撃では腹パン演出が始まらない()
        {
            yield return LoadAndGrab();

            _captor.enabled = true;
            Assert.IsFalse(_captor.TryStartHeldThrow(), "The rear captor cannot perform the opening throw.");
            Hit(_captor, PunchZone.Body);
            yield return null;

            Assert.IsFalse(_director.IsPerforming, "The captor must not trigger the tag-team belly punch");
            Assert.IsTrue(_grapple.IsGrappled, "The hold must remain");
        }

        [UnityTest]
        public IEnumerator 連打で先に逃げると腹パン演出は始まらない()
        {
            yield return LoadAndGrab();
            PlaceAttackerInFront(1.02f);
            _attacker.enabled = true;
            Assert.IsTrue(_attacker.TryStartHeldThrow());

            var escape = typeof(PlayerGrapple).GetMethod("Escape",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(escape, "Escape not found");
            escape.Invoke(_grapple, null);

            yield return null;
            yield return null;

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
            LandOpeningThrow();
            float deadline = Time.realtimeSinceStartup + 3f;
            while (!_camera.IsFocusing && Time.realtimeSinceStartup < deadline)
                yield return null;
            yield return null;

            Assert.IsTrue(_camera.IsFocusing, "Camera must focus during the combo");

            Vector3 toCamera = _camera.transform.position - _player.transform.position;
            toCamera.y = 0f;
            // 腹パンの構図は正面斜め前（彼女の顔が見える）。左を向いたときも
            // 同じ側（正面）へ回り込むこと。
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
        /// <summary>
        /// 殴られている最中でも、合間を縫って抜けられる。
        /// 「5 発食らい終わるまで何もできない」拘束にしない（リョナの核は抵抗）。
        /// </summary>
        [UnityTest]
        public IEnumerator 腹パンを受けている最中でももがけば抜けられる()
        {
            yield return LoadAndGrab(waitForAnimator: true);
            _director.FacePunchChance = 0f;
            PlaceAttackerInFront();

            LandOpeningThrow();
            _attacker.HoldFire(10f);
            _attacker.enabled = true;
            yield return null;

            Assert.IsTrue(_director.IsPerforming, "コンボが始まる");
            Assert.IsTrue(_grapple.IsGrappled, "掴まれている");

            // 演出中にもがき続ける（当たりの瞬間の 0.12 秒だけ止まる）
            float t = 0f;
            while (_grapple.IsGrappled && t < 3f)
            {
                _grapple.Struggle();
                yield return new WaitForSeconds(0.12f);
                t += 0.12f;
            }

            Assert.IsFalse(_grapple.IsGrappled, "腹パン中でももがけば抜けられる");

            // 抜けたら演出も止まる
            yield return new WaitForSeconds(0.2f);
            Assert.IsFalse(_director.IsPerforming, "抜けたらコンボは終わる");
        }

        /// <summary>
        /// 連撃ごとに腹か顔を選び、外れ（retry）が出ないこと。
        /// 敵 AI は実機どおり有効（フェイントも抽選される）。
        /// </summary>
        [UnityTest]
        public IEnumerator 拘束中の腹パンと顔面パンチは外れずに5発当たる()
        {
            yield return LoadAndGrab(waitForAnimator: true, throwDelay: 0.3f);

            _director.FacePunchChance = 0.5f;
            _director.RestartCooldownSeconds = 0.5f;

            // 4 コンボ分の被弾で HP が尽きると、拘束が解けてコンボが終わらなくなる。
            // このテストが見るのは「当たるか」なので、死亡しないようにする。
            _player.SetGodMode(true);

            foreach (var e in _enemies)
            {
                if (e != _captor)
                    e.enabled = true;
            }

            int retries = 0;
            int combos = 0;
            var misses = new System.Collections.Generic.List<string>();
            Application.LogCallback cb = (msg, stack, type) =>
            {
                // retry = 演出が「当たらなかった」と判断した回数。
                // 通常攻撃の空振りとタッグ技の失敗を区別する。
                if (msg.StartsWith("[HeldPunch] retry")
                    || msg.StartsWith("[StrikeMiss]") && msg.Contains("tag=True"))
                {
                    retries++;
                    misses.Add(msg);
                }
                else if (msg.StartsWith("[HeldPunch] combo end"))
                    combos++;
            };
            Application.logMessageReceived += cb;

            try
            {
                // コンボが終わったら掴み直し、4 回繰り返す。
                float deadline = Time.realtimeSinceStartup + 180f;
                int rounds = 0;
                while (rounds < 4 && Time.realtimeSinceStartup < deadline)
                {
                    int before = combos;
                    while (combos == before && Time.realtimeSinceStartup < deadline)
                        yield return null;

                    rounds++;
                    // 倒れて、自然に起き上がるのを待つ（強制復帰すると敵の状態とずれる）
                    if (rounds == 4)
                        break;

                    // 試行ごとに掴み直す。腹ダウンが起きていなければ拘束は続いている。
                    _grapple.Release();
                    // 掴み直し用の待機中に、通常攻撃で新たに倒されないようにする。
                    foreach (var enemy in _enemies)
                        enemy.HoldFire(0.75f);
                    yield return new WaitForSeconds(0.5f);
                    float up = Time.realtimeSinceStartup + 8f;
                    while ((_player.IsDown || _anim.IsDowned) && Time.realtimeSinceStartup < up)
                        yield return null;
                    Assert.IsFalse(_player.IsDown || _anim.IsDowned, "掴み直す前に起き上がる");
                    _captor.transform.position = _player.transform.position + Vector3.left;
                    _grapple.Begin(_captor, GrappleKind.Back);
                    yield return null;
                }
            }
            finally
            {
                // アサートで抜けても、ログ購読を後続テストへ残さない
                Application.logMessageReceived -= cb;
                if (_player != null)
                    _player.SetGodMode(false);
            }

            Assert.GreaterOrEqual(combos, 4, $"コンボが終わらない combos={combos}");
            Assert.AreEqual(0, retries, "外れた回数: " + string.Join(" / ", misses));
        }

        /// <summary>
        /// 敵の最初の位置に顔を向け、移動には追従せず、振りかぶりで視線を外す。
        /// </summary>
        [UnityTest]
        public IEnumerator 腹パンが始まるとき殴りに来る敵の顔を見る()
        {
            yield return LoadAndGrab(waitForAnimator: true, throwDelay: 10f);
            _captor.TakeOverHold(10f);
            PlaceAttackerInFront(1.15f);
            _attacker.transform.position += _player.transform.right * 0.45f;
            typeof(HeldBellyPunchDirector).GetMethod("SetDreadTarget", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(_director, new object[] { _attacker });

            yield return new WaitForSeconds(0.25f);
            Assert.GreaterOrEqual(_anim.DreadLook, 0.95f, "最初に敵へ顔を向ける");
            float firstYaw = _anim.DebugHeadYaw;
            float firstPitch = _anim.DebugHeadPitch + _anim.DebugTorsoPitch;
            Assert.Greater(firstYaw, 10f, "最初の敵の横位置が顔の角度に反映される");

            PlaceAttackerInFront(0.72f);
            yield return new WaitForSeconds(0.14f);
            Assert.AreEqual(firstYaw, _anim.DebugHeadYaw, 3f, "敵の移動に顔が追従する");
            Assert.AreEqual(firstPitch, _anim.DebugHeadPitch + _anim.DebugTorsoPitch, 5f);

            const BindingFlags privateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
            _attacker.JabSeconds = 0.35f;
            typeof(EnemyBrain).GetMethod("BeginWindup", privateInstance)
                .Invoke(_attacker, new object[] { EnemyBrain.AttackId.Jab });
            typeof(EnemyBrain).GetField("_tagPunchSwing", privateInstance).SetValue(_attacker, true);
            typeof(EnemyBrain).GetMethod("TickWindup", privateInstance)
                .Invoke(_attacker, new object[] { 0.24f });
            yield return new WaitForSeconds(0.14f);
            Assert.AreEqual(EnemyPhase.Windup, _attacker.Phase);
            Assert.Less(_anim.DreadLook, 0.05f, "殴られる前に敵から視線を外す");

            _attacker.LockForHandOff(1f);
            _anim.PlayHeldBellyPunch(0.55f);
            yield return new WaitForSeconds(0.62f);
            Assert.GreaterOrEqual(_anim.DreadLook, 0.95f, "腹パンの反応が終わったら顔を上げる");
            Assert.AreEqual(firstYaw, _anim.DebugHeadYaw, 3f, "被弾後も最初の敵の位置を基準にする");
            Assert.AreEqual(firstPitch, _anim.DebugHeadPitch + _anim.DebugTorsoPitch, 5f);
        }

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
