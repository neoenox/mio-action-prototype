using System.Collections;
using NUnit.Framework;
using Ryona.Game;
using UnityEngine;
using UnityEngine.TestTools;

namespace Ryona.Tests
{
    /// <summary>
    /// 押さえつけ（<see cref="GrappleKind.Pin"/>）の回帰テスト。
    ///
    /// 後ろ拘束（Back）は時間切れで解けないが、押さえつけは
    /// 制限時間内に 12 回もがけば逃げられ、時間切れなら解放される。
    /// 入口は正面 0.90〜1.30m・攻撃トークンあり・クールダウン外だけ。
    /// </summary>
    public sealed class PinGrappleTests
    {
        PlayerStats _player;
        PlayerGrapple _grapple;
        EnemyBrain _captor;
        EnemyBrain _other;

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            if (_grapple != null && _grapple.IsGrappled)
                _grapple.Release();
        }

        /// <summary>シーンを読み、押さえる側を正面 1.15m に置く。</summary>
        IEnumerator Load()
        {
            yield return TestScene.LoadPrototype();

            // 通常の正面拘束を検証する。壁版は WallPinRuntimeTests で検証する。
            foreach (var wall in Object.FindObjectsByType<WallPinSite>(FindObjectsSortMode.None))
                wall.enabled = false;

            _player = Object.FindFirstObjectByType<PlayerStats>();
            Assert.IsNotNull(_player, "PlayerStats がシーンに無い");

            _grapple = _player.GetComponent<PlayerGrapple>();
            Assert.IsNotNull(_grapple, "PlayerGrapple が無い");

            var enemies = Object.FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None);
            Assert.GreaterOrEqual(enemies.Length, 2, "押さえ役と追撃役で 2 体必要");

            _captor = enemies[0];
            _other = enemies[1];
            for (int i = 2; i < enemies.Length; i++)
                enemies[i].enabled = false;

            // プレイヤーは +X 向き。押さえる側は正面（+X）。
            _player.transform.rotation = Quaternion.LookRotation(Vector3.right);
            _captor.transform.position = _player.transform.position + Vector3.right * 1.15f;
            _captor.transform.rotation = Quaternion.LookRotation(Vector3.left);

            _captor.CanPin = true;
            _captor.enabled = false;
            _other.enabled = false;

            yield return null;
        }

        [UnityTest]
        public IEnumerator 押さえつけは制限時間つきで始まり相手の方を向く()
        {
            yield return Load();

            _grapple.Begin(_captor, GrappleKind.Pin);

            Assert.IsTrue(_grapple.IsGrappled, "押さえつけが始まらない");
            Assert.AreEqual(GrappleKind.Pin, _grapple.Kind);
            Assert.IsTrue(_grapple.HasTimeLimit, "押さえつけは時間切れがある");
            Assert.AreEqual(CombatConstants.GrappleLimitSeconds, _grapple.Remaining, 0.05f,
                $"制限時間は {CombatConstants.GrappleLimitSeconds:F1} 秒");
            Assert.AreEqual(CombatConstants.MashBasePin, _grapple.RequiredInputs, 0.01f,
                "必要入力は 12 回");

            // 正面から掴まれるので、相手の方を向く
            Vector3 toCaptor = _captor.transform.position - _player.transform.position;
            toCaptor.y = 0f;
            Assert.Greater(Vector3.Dot(_player.transform.forward, toCaptor.normalized), 0.9f,
                "押さえつけは正面からなので、彼女は相手を向く");

            // 掴んだ距離は腕を伸ばす余地を残す 0.60m
            Assert.AreEqual(0.60f, toCaptor.magnitude, 0.05f, "掴んだ距離が 0.60m になっていない");
        }

        [UnityTest]
        public IEnumerator もがき12回で振りほどけ押さえた側がよろける()
        {
            yield return Load();

            _grapple.Begin(_captor, GrappleKind.Pin);
            int need = Mathf.CeilToInt(_grapple.RequiredInputs);

            for (int i = 0; i < need - 1; i++)
            {
                _grapple.Struggle();
                Assert.IsTrue(_grapple.IsGrappled, $"{i + 1} 回目でもがききっていない");
            }

            _grapple.Struggle();
            yield return null;

            Assert.IsFalse(_grapple.IsGrappled, "12 回もがけば振りほどける");
            Assert.AreEqual(EnemyPhase.Stagger, _captor.Phase, "振りほどかれた側はよろける（反撃の隙）");
        }

        [UnityTest]
        public IEnumerator 時間切れで追加ダメージやダウンなしに解放される()
        {
            yield return Load();

            bool failed = false;
            _grapple.Failed += () => failed = true;

            float armDamageBefore = _player.State.Body[BodyPart.Arms];
            float hpBefore = _player.State.Hp;

            _grapple.Begin(_captor, GrappleKind.Pin);
            yield return new WaitForSeconds(CombatConstants.GrappleLimitSeconds + 0.4f);

            Assert.IsTrue(failed, "時間切れで Failed が飛んでいない");
            Assert.IsFalse(_grapple.IsGrappled);
            Assert.IsFalse(_player.IsDown);
            Assert.AreEqual(hpBefore, _player.State.Hp);
            Assert.AreEqual(armDamageBefore, _player.State.Body[BodyPart.Arms]);
        }

        [UnityTest]
        public IEnumerator 膝蹴りが蓄積すると次の解放でうつ伏せに倒れる()
        {
            yield return Load();
            _captor.enabled = true;

            for (int round = 0; round < 2; round++)
            {
                _grapple.Begin(_captor, GrappleKind.Pin);
                _captor.TakeOverHold(CombatConstants.GrappleLimitSeconds + 1f);

                float deadline = Time.realtimeSinceStartup + 8f;
                while (_grapple.IsGrappled && Time.realtimeSinceStartup < deadline)
                    yield return null;
                Assert.IsFalse(_grapple.IsGrappled);
                Assert.AreEqual(round == 1, _player.IsDown);
            }

            Assert.IsTrue(_player.DownIsForward);
            Assert.IsFalse(_player.HasHeldBellyDown, "腹ダウンで蓄積を消化する");
        }

        [UnityTest]
        public IEnumerator 背後や間合い外では押さえつけを始めない()
        {
            yield return Load();

            // 正面 1.15m・トークン不要の直接指示なら始められる
            Assert.IsTrue(_captor.CanStartPin(1.15f), "正面 1.15m では始められること");

            // 間合いの外
            Assert.IsFalse(_captor.CanStartPin(0.60f), "近すぎる（0.60m）と始めない");
            Assert.IsFalse(_captor.CanStartPin(1.60f), "遠すぎる（1.60m）と始めない");

            // 背後からは羽交い締め（Back）なので、押さえつけは出さない
            _captor.transform.position = _player.transform.position - Vector3.right * 1.15f;
            yield return null;
            Assert.IsFalse(_captor.CanStartPin(1.15f), "背後からは押さえつけない");

            // CanPin が false なら、正面でも始めない
            _captor.transform.position = _player.transform.position + Vector3.right * 1.15f;
            _captor.CanPin = false;
            yield return null;
            Assert.IsFalse(_captor.CanStartPin(1.15f), "CanPin が false なら始めない");
        }

        [UnityTest]
        public IEnumerator FrontGrabClosesTheGapBeforeItConnects() =>
            CheckGrabClosesTheGap(GrappleKind.Pin);

        [UnityTest]
        public IEnumerator RearGrabClosesTheGapBeforeItConnects() =>
            CheckGrabClosesTheGap(GrappleKind.Back);

        IEnumerator CheckGrabClosesTheGap(GrappleKind kind)
        {
            yield return Load();
            if (kind == GrappleKind.Pin)
                _captor.QueuePin();
            else
            {
                _captor.CanPin = false;
                _captor.transform.SetPositionAndRotation(
                    _player.transform.position + Vector3.left * 1.10f, Quaternion.LookRotation(Vector3.right));
                _captor.QueueGrab();
            }
            _captor.FeintChance = 1f;
            _captor.MinPlayerGap = 0.95f;
            _captor.enabled = true;

            float deadline = Time.realtimeSinceStartup + 4f;
            while (_captor.Phase != EnemyPhase.Attack && Time.realtimeSinceStartup < deadline)
                yield return null;

            Assert.AreEqual(EnemyPhase.Attack, _captor.Phase);
            Assert.Less(Vector3.Distance(_captor.transform.position, _player.transform.position), 0.88f);
            Vector3 atReach = _player.transform.position;

            while (!_grapple.IsGrappled && Time.realtimeSinceStartup < deadline)
                yield return null;

            Assert.IsTrue(_grapple.IsGrappled);
            Assert.AreEqual(kind, _grapple.Kind);
            Assert.Less(Vector3.Distance(atReach, _player.transform.position), 0.09f,
                "The player stays near the contact point on the first frame of the grab.");

            yield return new WaitForSeconds(0.22f);
            Assert.AreEqual(kind == GrappleKind.Back ? 0.34f : 0.60f,
                Vector3.Distance(_captor.transform.position, _player.transform.position), 0.035f);
        }

        [UnityTest]
        public IEnumerator PinCanBeEscapedBeforeTheFirstKnee() => EscapeBeforeFrontAttack(GrappleKind.Pin);

        [UnityTest]
        public IEnumerator BearHugCanBeEscapedBeforeTheFirstSqueeze() => EscapeBeforeFrontAttack(GrappleKind.BearHug);

        IEnumerator EscapeBeforeFrontAttack(GrappleKind kind)
        {
            yield return Load();
            _player.State.Body.Apply(BodyPart.Arms, CombatConstants.PartMax);
            _captor.enabled = true;
            if (kind == GrappleKind.Pin)
                _captor.QueuePin();
            else
                _captor.QueueBearHug();

            float deadline = Time.realtimeSinceStartup + 5f;
            while (!_grapple.IsGrappled && Time.realtimeSinceStartup < deadline)
                yield return null;

            Assert.IsTrue(_grapple.IsGrappled);
            Assert.AreEqual(kind, _grapple.Kind);
            Assert.AreEqual(15, Mathf.CeilToInt(_grapple.RequiredInputs));

            float hp = _player.State.Hp;
            float first = Time.time;
            for (int i = 1; i <= 15; i++)
            {
                while (Time.time < first + i / 6f && Time.realtimeSinceStartup < deadline + 4f)
                    yield return null;

                Assert.Greater(_grapple.FrontEscapeRemaining, 0f);
                Assert.AreEqual(0f, _grapple.KneePulse);
                Assert.AreEqual(0f, _grapple.SqueezePulse);
                Assert.AreEqual(hp, _player.State.Hp);
                _grapple.Struggle();
            }

            Assert.IsFalse(_grapple.IsGrappled);
            Assert.AreEqual(EnemyPhase.Stagger, _captor.Phase);
        }

        [UnityTest]
        public IEnumerator 押さえつけ中に膝蹴りが5回入る()
        {
            yield return Load();

            // 実際の入口（予備動作 → 当たり → 押さえつけ）から始める。膝蹴りは Recover 中に入る。
            _captor.enabled = true;
            _captor.QueuePin();

            float deadline = Time.realtimeSinceStartup + 4f;
            while (!_grapple.IsGrappled && Time.realtimeSinceStartup < deadline)
                yield return null;

            Assert.IsTrue(_grapple.IsGrappled, "押さえつけが始まらない");
            Assert.AreEqual(GrappleKind.Pin, _grapple.Kind);

            int hits = 0;
            float hpBefore = _player.State.Hp;
            _player.HitReceived += _ => hits++;

            Assert.AreEqual(0, hits, "掴んだ瞬間はまだ膝蹴りは入らない");

            float lastKnee = CombatConstants.FrontGrappleEscapeSeconds +
                (CombatConstants.PinKneeCount - 1) * CombatConstants.PinKneeInterval;
            yield return new WaitForSeconds(lastKnee + 0.25f);

            Assert.AreEqual(CombatConstants.PinKneeCount, hits, "押さえつけ中に膝蹴りが 5 発入る");
            Assert.Less(_player.State.Hp, hpBefore - 10f, "膝蹴り 5 発ぶん HP が減る");

            // 時間切れより前に蹴り終わっている
            yield return new WaitForSeconds(0.1f);
            Assert.AreEqual(CombatConstants.PinKneeCount, hits, "制限時間内に 5 発で収まる");
        }

        [UnityTest]
        public IEnumerator 最後の膝蹴りは背後の敵にだけ拘束を引き継ぐ(
            [Values(false, true)] bool receiverBehind)
        {
            yield return Load();
            _captor.enabled = true;
            _captor.QueuePin();
            float deadline = Time.realtimeSinceStartup + 4f;
            while (!_grapple.IsGrappled && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.AreEqual(GrappleKind.Pin, _grapple.Kind);
            Assert.AreSame(_captor, _grapple.Captor);

            int knees = 0;
            _player.HitReceived += _ =>
            {
                knees++;
                if (knees != CombatConstants.PinKneeCount)
                    return;
                float offset = receiverBehind ? -0.82f : 0.82f;
                _other.transform.position = _player.transform.position + _player.transform.forward * offset;
                _other.HoldFire(10f);
                _other.enabled = true;
            };

            int releases = 0;
            _grapple.Released += () => releases++;
            deadline = Time.realtimeSinceStartup + 10f;
            bool sawLastKneeWithSender = false;
            while (_grapple.IsGrappled && _grapple.Captor == _captor &&
                   Time.realtimeSinceStartup < deadline)
            {
                if (knees == CombatConstants.PinKneeCount)
                {
                    sawLastKneeWithSender = true;
                    Assert.IsTrue(_captor.IsPinning, "受け手が近づく間は正面拘束を維持する");
                }
                yield return null;
            }

            Assert.AreEqual(CombatConstants.PinKneeCount, knees);
            if (!receiverBehind)
            {
                Assert.IsFalse(_grapple.IsGrappled, "正面の敵だけなら従来どおり時間切れで解放する");
                Assert.AreEqual(1, releases);
                yield break;
            }

            Assert.IsTrue(sawLastKneeWithSender);
            Assert.IsTrue(_grapple.IsGrappled);
            Assert.AreSame(_other, _grapple.Captor);
            Assert.AreEqual(GrappleKind.Back, _grapple.Kind);
            Assert.AreEqual(0, releases, "引き渡しで拘束は途切れない");
            Assert.IsTrue(_other.IsHolding);
            Assert.IsFalse(_captor.IsHolding);
            Assert.IsFalse(_grapple.IsStruggleSuppressed);
            Assert.Greater(Object.FindFirstObjectByType<HeldBellyPunchDirector>().NextThrowRemaining, 0f,
                "羽交い締めから通常の連携に入れる");
            int inputs = Mathf.CeilToInt(_grapple.RequiredInputs);
            for (int i = 0; i < inputs; i++)
                _grapple.Struggle();
            Assert.IsFalse(_grapple.IsGrappled, "引き渡し直後も連打で抜けられる");
        }

        [UnityTest]
        public IEnumerator 蹴り上げ中の膝が跳ねた腹へ届く()
        {
            yield return Load();
            _captor.enabled = true;
            _captor.QueuePin();
            float deadline = Time.realtimeSinceStartup + 6f;
            while (!_grapple.IsGrappled && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.AreEqual(GrappleKind.Pin, _grapple.Kind);
            yield return new WaitForSeconds(0.3f);
            var figure = _captor.GetComponent<EnemyFigure>();
            Assert.IsNotNull(figure);
            float headDepth = _captor.transform.InverseTransformPoint(figure.HeadBone.position).z;
            while (_grapple.KneePulse < 0.99f && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.GreaterOrEqual(_grapple.KneePulse, 0.99f, "膝蹴りが始まらない");
            var rig = _player.GetComponentInChildren<ProceduralRig>();
            Assert.IsNotNull(rig);
            int samples = 0;
            float kickHeadDepth = float.PositiveInfinity;
            while (_grapple.KneePulse >= 0.99f && samples < 6)
            {
                // yield null の再開時には直前フレームの LateUpdate の骨位置が読める。
                yield return null;
                Vector3 spine = rig.Spine.position;
                Vector3 chest = rig.Chest.position;
                Vector3 front = Vector3.ProjectOnPlane(_player.transform.forward, chest - spine).normalized;
                Vector3 belly = Vector3.Lerp(spine, chest, 0.60f) + front * 0.085f;
                bool left = (figure.KneeBone(true).position - belly).sqrMagnitude
                    < (figure.KneeBone(false).position - belly).sqrMagnitude;
                Vector3 knee = figure.KneeBone(left).position;
                Vector3 kneeSurface = knee - front * (figure.Height * 0.039f);
                Assert.Less(Vector3.Distance(kneeSurface, belly), 0.055f, "膝頭が前傾した腹の表面から離れている");
                Vector3 shin = figure.AnkleBone(left).position - knee;
                Assert.Less(Vector3.Dot(shin, _captor.transform.forward), -0.05f,
                    "当たる膝より奥へ脛が入り込まない");
                Assert.Less(Vector3.Angle(figure.ChestBone.up, _captor.transform.up), 52f,
                    "ヒットストップ中も上体の反りが積み重ならない");
                kickHeadDepth = Mathf.Min(kickHeadDepth,
                    _captor.transform.InverseTransformPoint(figure.HeadBone.position).z);
                samples++;
            }
            Assert.Greater(samples, 0);
            Assert.Less(kickHeadDepth, headDepth - 0.03f,
                "膝を入れながら上体を引き、澪の顔が跳ね上がる空間を空ける");
        }

        [UnityTest]
        public IEnumerator 拘束は別の敵へ引き渡せる()
        {
            yield return Load();
            _grapple.Begin(_captor, GrappleKind.Back);

            int started = 0;
            _grapple.Started += _ => started++;

            float before = _grapple.RequiredInputs;
            _grapple.HandOff(_other, GrappleKind.Back);
            yield return null;

            Assert.IsTrue(_grapple.IsGrappled, "引き渡しても掴まれたまま");
            Assert.AreEqual(_other, _grapple.Captor, "掴んでいる相手が入れ替わる");
            Assert.AreEqual(1, started, "掴み直しとして Started が飛ぶ");
            Assert.AreEqual(before, _grapple.RequiredInputs, 0.01f, "必要入力は同じ");
        }

        [UnityTest]
        public IEnumerator HandOffCatchesBeforeStartingThePin() =>
            CheckHandOffStages(GrappleKind.Pin);

        [UnityTest]
        public IEnumerator HandOffCatchesBeforeStartingTheBackHold() =>
            CheckHandOffStages(GrappleKind.Back);

        IEnumerator CheckHandOffStages(GrappleKind kind)
        {
            yield return Load();
            _player.GetComponent<PlayerMotor>().enabled = false;
            _captor.transform.SetPositionAndRotation(_player.transform.position + Vector3.left,
                Quaternion.LookRotation(Vector3.right));
            _grapple.Begin(_captor, GrappleKind.Back);
            _other.transform.position = _player.transform.position + Vector3.right * 2f;
            _other.transform.rotation = Quaternion.LookRotation(Vector3.left);

            int started = 0;
            _grapple.Started += _ => started++;
            Vector3 beforePush = _player.transform.position;
            Vector3 receiverStart = _other.transform.position;
            _grapple.HandOff(_other, kind, shove: true);

            Assert.IsFalse(_other.IsCatching || _other.IsHolding, "The shove comes first.");
            Assert.AreEqual(0, started, "The new hold must not start during the shove.");
            Assert.IsTrue(_captor.IsPushing);
            Assert.AreEqual(0f, _grapple.PushPulse, "The sender reaches before the player reacts.");
            Assert.AreEqual(beforePush, _player.transform.position, "The player stays planted during the reach.");

            float deadline = Time.realtimeSinceStartup + 3f;
            while (_grapple.PushPulse == 0f && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.Greater(_grapple.PushPulse, 0f, "The shove starts on contact.");
            Assert.IsTrue(_captor.IsPushing, "The sender follows through as the player moves.");
            while (_player.transform.position == beforePush && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.Greater(_player.transform.position.x, beforePush.x);

            while (_grapple.PushPulse > 0f && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.IsFalse(_other.IsCatching, "The player takes recovery steps before reaching the receiver.");
            Vector3 beforeSteps = _player.transform.position;

            while (!_other.IsCatching && Time.realtimeSinceStartup < deadline)
                yield return null;

            Assert.IsTrue(_other.IsCatching);
            float contactDistance = Vector3.Distance(_player.transform.position, _other.transform.position);
            Assert.Less(contactDistance, kind == GrappleKind.Back ? 0.36f : 0.46f,
                "The receiver catches after the player reaches body contact.");
            Assert.Greater(Vector3.Dot(_player.transform.forward, Vector3.right), 0.9f,
                "The player collides facing forward before turning into the new hold.");
            Quaternion receiverAtContact = _other.transform.rotation;
            Assert.Greater(_player.transform.position.x - beforeSteps.x, 0.25f,
                "The player still travels after the shove reaction ends.");
            Assert.IsFalse(_other.IsHolding || _other.IsPinning, "The receiver braces before pinning.");
            Assert.AreEqual(0, started);

            while (!_other.IsHolding && Time.realtimeSinceStartup < deadline)
                yield return null;

            Assert.IsTrue(_other.IsHolding);
            Assert.Greater(_other.transform.position.x - receiverStart.x, 0.10f,
                "The impact pushes the receiver back before the hold starts.");
            if (kind == GrappleKind.Back)
            {
                Assert.Greater(Quaternion.Angle(receiverAtContact, _other.transform.rotation), 10f,
                    "The receiver also turns while guiding the player into the back hold.");
                Assert.Greater(Vector3.Dot(_player.transform.forward, _other.transform.forward), 0.98f);
            }
            Assert.AreEqual(kind == GrappleKind.Pin, _other.IsPinning);
            Assert.IsFalse(_other.IsCatching);
            Assert.AreEqual(kind, _grapple.Kind);
            Assert.AreEqual(1, started);
            if (kind == GrappleKind.Pin)
                Assert.Greater(_grapple.Remaining, CombatConstants.GrappleLimitSeconds - 0.2f);
        }

        [UnityTest]
        public IEnumerator InterruptedHandOffStopsMovingThePlayer()
        {
            yield return Load();
            _player.GetComponent<PlayerMotor>().enabled = false;
            _captor.transform.SetPositionAndRotation(_player.transform.position + Vector3.left,
                Quaternion.LookRotation(Vector3.right));
            _grapple.Begin(_captor, GrappleKind.Back);
            _other.transform.position = _player.transform.position + Vector3.right * 2.5f;
            _other.transform.rotation = Quaternion.LookRotation(Vector3.left);
            _grapple.HandOff(_other, GrappleKind.Pin, shove: true);
            float deadline = Time.realtimeSinceStartup + 2f;
            while (_grapple.PushPulse == 0f && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.Greater(_grapple.PushPulse, 0f);
            yield return null;

            _other.TakeHit(new CombatState(), new AttackData { damage = _other.State.Hp });
            Assert.IsFalse(_other.IsAlive);
            yield return null;
            Assert.IsFalse(_grapple.IsGrappled);

            Vector3 releasedPosition = _player.transform.position;
            Quaternion releasedRotation = _player.transform.rotation;
            yield return null;
            yield return null;

            Assert.AreEqual(releasedPosition, _player.transform.position);
            Assert.AreEqual(releasedRotation, _player.transform.rotation);
        }

        [UnityTest]
        public IEnumerator 膝蹴りの連撃中に別の敵の通常パンチも当たる()
        {
            yield return Load();

            _grapple.Begin(_captor, GrappleKind.Pin);
            _captor.enabled = true;
            _captor.TakeOverHold(CombatConstants.GrappleLimitSeconds + 1f);

            float deadline = Time.realtimeSinceStartup + CombatConstants.FrontGrappleEscapeSeconds + 1.5f;
            while (_grapple.KneePulse <= 0f && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.Greater(_grapple.KneePulse, 0f);

            // 押さえ役と反対側から、別の敵が通常の AI で攻撃する。
            _other.transform.position = _player.transform.position - _player.transform.forward * 0.74f;
            _other.transform.rotation = Quaternion.LookRotation(_player.transform.forward);
            _other.FeintChance = 0f;
            bool punchLanded = false;
            _player.HitReceived += _ =>
            {
                if (_grapple.IsGrappled && _player.LastAttacker == _other.transform)
                    punchLanded = true;
            };
            _other.enabled = true;
            deadline = Time.realtimeSinceStartup + 4f;
            while (_grapple.IsGrappled && !punchLanded && Time.realtimeSinceStartup < deadline)
                yield return null;

            Assert.IsTrue(punchLanded, "押さえつけの膝蹴り中に別の敵のパンチが当たる");
            Assert.AreSame(_captor, _grapple.Captor);
        }
    }
}
