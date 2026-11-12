using System.Collections;
using NUnit.Framework;
using Ryona.Game;
using UnityEngine;
using UnityEngine.TestTools;

namespace Ryona.Tests
{
    public sealed class PlayabilityRegressionTests
    {
        [UnityTest]
        public IEnumerator NewEncounterStartsItsOwnClock()
        {
            yield return TestScene.LoadPrototype();
            yield return new WaitForSeconds(0.3f);
            var first = Object.FindFirstObjectByType<EncounterDirector>();
            Assert.Greater(first.RunSeconds, 0.2f);
            yield return TestScene.LoadPrototype();
            var second = Object.FindFirstObjectByType<EncounterDirector>();
            Assert.Less(second.RunSeconds, 0.25f, "A restart must not include the previous run");
        }

        [UnityTest]
        public IEnumerator EnemiesStayOnPlayersLaneAndCanAttack()
        {
            yield return TestScene.LoadPrototype();
            var player = Object.FindFirstObjectByType<PlayerMotor>();
            var enemies = Object.FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None);
            Assert.GreaterOrEqual(enemies.Length, 2);
            bool attacked = false;
            float end = Time.realtimeSinceStartup + 12f;
            while (Time.realtimeSinceStartup < end && !attacked)
            {
                foreach (var enemy in enemies)
                {
                    Assert.That(enemy.transform.position.z, Is.EqualTo(player.transform.position.z).Within(0.02f));
                    attacked |= enemy.Phase == EnemyPhase.Windup || enemy.Phase == EnemyPhase.Attack;
                }
                yield return null;
            }
            Assert.IsTrue(attacked, "Enemies must be able to reach and attack on the single lane");
        }

        [UnityTest]
        public IEnumerator EnemyBehindAnotherCannotTakeAttackTurn()
        {
            yield return TestScene.LoadPrototype();
            var player = Object.FindFirstObjectByType<PlayerMotor>();
            var group = Object.FindFirstObjectByType<EnemyGroup>();
            var enemies = Object.FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None);
            group.ReleaseAllTokens();
            enemies[0].transform.position = player.transform.position + Vector3.right * 2f;
            enemies[1].transform.position = player.transform.position + Vector3.right * 4f;
            Assert.IsFalse(group.TryAcquireAttackToken(enemies[1]));
            Assert.IsTrue(group.TryAcquireAttackToken(enemies[0]));
        }

        [UnityTest]
        public IEnumerator EnemyElbowsBendForwardInGuardPose()
        {
            var go = new GameObject("enemy pose regression");
            var figure = go.AddComponent<EnemyFigure>();
            figure.Preview(EnemyPhase.Chase, 0f, EnemyBrain.AttackId.Jab, 0f);
            foreach (bool left in new[] { true, false })
            {
                var shoulder = figure.ShoulderBone(left);
                var elbow = figure.ElbowBone(left);
                var wrist = figure.WristBone(left);
                var localForearm = shoulder.InverseTransformDirection(wrist.position - elbow.position);
                Assert.Greater(localForearm.z, 0f, "Elbow bends behind the upper arm in the guard pose");
            }
            Object.Destroy(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator GrabPoseReachesOpponentShoulderHeight()
        {
            var go = new GameObject("grab pose regression");
            var figure = go.AddComponent<EnemyFigure>();
            figure.Preview(EnemyPhase.Recover, 0f, EnemyBrain.AttackId.Grab, 0f);
            foreach (bool left in new[] { true, false })
            {
                Vector3 wrist = go.transform.InverseTransformPoint(figure.WristBone(left).position);
                Vector3 shoulderTarget = new Vector3(left ? -0.1f : 0.1f, 1.16f, 0.65f);
                Assert.Less(Vector3.Distance(wrist, shoulderTarget), 0.16f,
                    "Grab hand must reach the opponent at the 0.6m grab distance");
            }
            Object.Destroy(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator FrontGrabWhiffsAndOnlyBackGrabConnects()
        {
            yield return TestScene.LoadPrototype();
            var player = Object.FindFirstObjectByType<PlayerStats>();
            var grapple = player.GetComponent<PlayerGrapple>();
            if (grapple.IsGrappled)
                grapple.Release();

            var captor = Object.FindFirstObjectByType<EnemyBrain>();
            player.transform.rotation = Quaternion.LookRotation(Vector3.right);

            // 正面（+X）からは掴めない。掴みは羽交い締め（背後）だけ。
            captor.transform.position = player.transform.position + Vector3.right;
            captor.transform.rotation = Quaternion.LookRotation(Vector3.left);
            Assert.IsFalse(captor.TryGrab(), "A front grab must whiff");
            Assert.IsFalse(grapple.IsGrappled, "The player must not be held from the front");

            // 背後（-X）なら掴める。
            captor.transform.position = player.transform.position + Vector3.left * 0.6f;
            captor.transform.rotation = Quaternion.LookRotation(Vector3.right);
            Assert.IsTrue(captor.TryGrab(), "A back grab must connect");
            Assert.IsTrue(grapple.IsGrappled);
            Assert.AreEqual(GrappleKind.Back, grapple.Kind);

            grapple.Release();
            yield return null;
        }

        [UnityTest]
        public IEnumerator EnemyCannotGrabWhenPlayerGoesDownBeforeImpact()
        {
            yield return TestScene.LoadPrototype();
            var player = Object.FindFirstObjectByType<PlayerStats>();
            var grapple = player.GetComponent<PlayerGrapple>();
            var enemy = Object.FindFirstObjectByType<EnemyBrain>();
            enemy.FeintChance = 0f;
            player.transform.rotation = Quaternion.LookRotation(Vector3.right);
            enemy.transform.position = player.transform.position + Vector3.left * 0.6f;
            enemy.transform.rotation = Quaternion.LookRotation(Vector3.right);

            enemy.BeginWindup(EnemyBrain.AttackId.Grab);
            enemy.TickWindup(10f);
            Assert.AreEqual(EnemyPhase.Attack, enemy.Phase);

            player.ForceDown(1f, "test", forward: true);
            enemy.Strike(0.6f);

            Assert.IsFalse(grapple.IsGrappled, "A grab must not connect after the player goes down.");
        }

        [UnityTest]
        public IEnumerator BackGrabKeepsPlayerFacingAwayFromCaptor()
        {
            yield return TestScene.LoadPrototype();
            var player = Object.FindFirstObjectByType<PlayerStats>();
            var grapple = player.GetComponent<PlayerGrapple>();
            var captor = Object.FindFirstObjectByType<EnemyBrain>();
            player.transform.rotation = Quaternion.LookRotation(Vector3.right);
            captor.transform.position = player.transform.position + Vector3.left;
            captor.transform.rotation = Quaternion.LookRotation(Vector3.right);
            grapple.Begin(captor, GrappleKind.Back);
            Vector3 toCaptor = captor.transform.position - player.transform.position;
            Assert.Less(Vector3.Dot(player.transform.forward, toCaptor.normalized), -0.8f);
        }

        [UnityTest]
        public IEnumerator EnemiesMoveBehindPlayerWhileAnEnemyEngagesFromFront()
        {
            yield return TestScene.LoadPrototype();
            var player = Object.FindFirstObjectByType<PlayerStats>();
            player.transform.rotation = Quaternion.LookRotation(Vector3.right);
            var enemies = Object.FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None);
            var group = Object.FindFirstObjectByType<EnemyGroup>();
            for (int i = 0; i < enemies.Length; i++)
            {
                enemies[i].transform.position = player.transform.position + Vector3.right * (1.3f + i * 1.4f);
                enemies[i].FeintChance = 0f;
            }

            // 同時に攻撃に入れるのは MaxAttackers 体まで（SPEC §5 のフェアネス規定）。
            // 残りが「待ち」として背後へ回るので、期待値は上限から決まる。
            int attackers = group != null ? Mathf.Max(1, group.MaxAttackers) : 1;
            int waiting = Mathf.Max(1, enemies.Length - attackers);

            float deadline = Time.realtimeSinceStartup + 8f;
            int behind = 0;
            while (Time.realtimeSinceStartup < deadline && behind < waiting && !player.IsDead)
            {
                behind = 0;
                foreach (var enemy in enemies)
                    if (enemy.transform.position.x < player.transform.position.x - 0.8f) behind++;
                yield return null;
            }
            Assert.GreaterOrEqual(behind, waiting,
                $"The waiting enemies should move to the back on the same lane (MaxAttackers={attackers})");
        }

        [UnityTest]
        public IEnumerator OtherEnemyCanDamagePlayerDuringBackGrab()
        {
            yield return TestScene.LoadPrototype();
            var player = Object.FindFirstObjectByType<PlayerStats>();
            var grapple = player.GetComponent<PlayerGrapple>();
            var enemies = Object.FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None);
            var group = Object.FindFirstObjectByType<EnemyGroup>();
            group.ReleaseAllTokens();
            var holder = enemies[0];
            var attacker = enemies[1];
            for (int i = 2; i < enemies.Length; i++) enemies[i].enabled = false;
            player.transform.rotation = Quaternion.LookRotation(Vector3.right);
            holder.transform.position = player.transform.position + Vector3.left * 1f;
            holder.transform.rotation = Quaternion.LookRotation(Vector3.right);
            attacker.transform.position = player.transform.position + Vector3.right * 1.3f;
            holder.FeintChance = attacker.FeintChance = 0f;
            // 腹パンだと新仕様（演出後に拘束解放）でこのテストの前提が崩れるため、
            // ここは顔ヒットで「拘束中に別の敵がダメージを与えられる」ことだけを見る。
            attacker.ForceZone = PunchZone.Face;
            attacker.HoldFire(0.8f);
            Assert.IsTrue(group.TryAcquireAttackToken(holder));
            holder.BeginWindup(EnemyBrain.AttackId.Grab);
            float end = Time.realtimeSinceStartup + 5f;
            while (!grapple.IsGrappled && Time.realtimeSinceStartup < end) yield return null;
            Assert.IsTrue(grapple.IsGrappled, "Back grab should connect");
            Assert.AreEqual(GrappleKind.Back, grapple.Kind);
            Assert.IsTrue(holder.IsHolding);
            Assert.IsFalse(holder.HasAttackToken, "The holder must free the attack slot");
            float hp = player.State.Hp;
            yield return new WaitForSeconds(0.22f);
            float holdGap = Vector3.Distance(holder.transform.position, player.transform.position);
            while (grapple.IsGrappled && player.State.Hp >= hp && Time.realtimeSinceStartup < end)
                yield return null;
            Assert.Less(player.State.Hp, hp, "The second enemy must hit before the hold ends");
            Assert.That(Vector3.Distance(holder.transform.position, player.transform.position),
                Is.EqualTo(holdGap).Within(0.08f), "The holder should not be pushed into the player");
            Assert.IsTrue(grapple.IsGrappled, "A normal hit should not cancel the hold");
            holder.EnterStagger(StaggerTier.Medium, BodyPart.Torso);
            Assert.IsFalse(grapple.IsGrappled, "A staggered holder must release the player");
            Assert.IsFalse(holder.IsHolding);
        }

        [UnityTest]
        public IEnumerator RearHoldKeepsFacingWhenAnotherEnemyHits() =>
            CheckHoldFacingDuringOtherHits(GrappleKind.Back);

        [UnityTest]
        public IEnumerator FrontHoldKeepsFacingWhenAnotherEnemyHits() =>
            CheckHoldFacingDuringOtherHits(GrappleKind.Pin);

        [UnityTest]
        public IEnumerator BearHugKeepsFacingWhenAnotherEnemyHits() =>
            CheckHoldFacingDuringOtherHits(GrappleKind.BearHug);

        IEnumerator CheckHoldFacingDuringOtherHits(GrappleKind kind)
        {
            yield return TestScene.LoadPrototype();
            var player = Object.FindFirstObjectByType<PlayerStats>();
            var grapple = player.GetComponent<PlayerGrapple>();
            var enemies = Object.FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None);
            foreach (var enemy in enemies)
                enemy.enabled = false;

            var slot = Object.FindFirstObjectByType<VrmCharacterSlot>();
            float deadline = Time.realtimeSinceStartup + 8f;
            while (!slot.Loaded && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.IsTrue(slot.Loaded);
            yield return null;

            var animator = slot.Instance.GetComponent<CharacterAnimator>();
            var captor = enemies[0];
            var attacker = enemies[1];
            Vector3 origin = player.transform.position;
            player.transform.rotation = Quaternion.LookRotation(Vector3.right);
            captor.transform.position = origin + Vector3.right * (kind == GrappleKind.Back ? -1f : 1f);
            attacker.transform.position = origin + Vector3.left * 1.8f;

            player.LastHitZone = PunchZone.Body;
            player.ReceiveHit(attacker.State, AttackLibrary.Heavy(), attacker.transform);
            Assert.AreEqual(CharacterAnimator.State.Stagger, animator.Current);
            grapple.Begin(captor, kind);
            float hp = player.State.Hp;
            player.ReceiveHit(attacker.State, AttackLibrary.Light(), attacker.transform);
            Assert.Less(player.State.Hp, hp);

            yield return new WaitForSeconds(0.16f);
            Assert.AreEqual(CharacterAnimator.State.Grabbed, animator.Current);
            Assert.AreSame(captor, grapple.Captor);
            Assert.AreEqual(kind, grapple.Kind);
            Assert.Greater(Vector3.Dot(Vector3.right, player.transform.forward), 0.98f,
                "An outside hit must not turn the player's body away from the hold.");
        }

        [UnityTest]
        public IEnumerator BackHoldRaisesArmsAndPlacesHandsBesideNeck()
        {
            yield return TestScene.LoadPrototype();
            var player = Object.FindFirstObjectByType<PlayerStats>();
            var grapple = player.GetComponent<PlayerGrapple>();
            var enemies = Object.FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None);
            foreach (var e in enemies) e.enabled = false;
            var slot = Object.FindFirstObjectByType<VrmCharacterSlot>();
            float end = Time.realtimeSinceStartup + 8f;
            while ((slot == null || !slot.Loaded || slot.Instance == null) && Time.realtimeSinceStartup < end)
                yield return null;
            Assert.IsTrue(slot != null && slot.Loaded);
            var rig = slot.Instance.GetComponent<ProceduralRig>();
            Assert.IsNotNull(rig.Neck);
            var holder = enemies[0];
            holder.enabled = true;
            holder.FeintChance = 0f;
            player.transform.rotation = Quaternion.LookRotation(Vector3.right);
            holder.transform.position = player.transform.position + Vector3.left;
            holder.transform.rotation = Quaternion.LookRotation(Vector3.right);
            holder.BeginWindup(EnemyBrain.AttackId.Grab);
            end = Time.realtimeSinceStartup + 5f;
            while (!grapple.IsGrappled && Time.realtimeSinceStartup < end) yield return null;
            Assert.IsTrue(grapple.IsGrappled);
            Assert.AreEqual(GrappleKind.Back, grapple.Kind);
            yield return new WaitForSeconds(0.65f);
            var figure = holder.GetComponent<EnemyFigure>();
            // 羽交い締め: 両手は首の後ろ（うなじ）で組まれる
            float rightGap = Vector3.Distance(figure.Fist(false).position, rig.Neck.position);
            float leftGap = Vector3.Distance(figure.Fist(true).position, rig.Neck.position);
            float rightElbow = rig.RLowerArm.position.y - rig.RUpperArm.position.y;
            float leftElbow = rig.LLowerArm.position.y - rig.LUpperArm.position.y;
            Assert.Less(rightGap, 0.24f, $"right hand too far from neck: {rightGap:F2}");
            Assert.Less(leftGap, 0.24f, $"left hand too far from neck: {leftGap:F2}");
            Assert.GreaterOrEqual(figure.AnkleBone(false).position.y - holder.transform.position.y, -0.02f,
                "Holder's right foot must not sink into the floor");
            Assert.GreaterOrEqual(figure.AnkleBone(true).position.y - holder.transform.position.y, -0.02f,
                "Holder's left foot must not sink into the floor");
            Assert.Greater(rightElbow, -0.08f, $"right elbow not raised: {rightElbow:F2}");
            Assert.Greater(leftElbow, -0.08f, $"left elbow not raised: {leftElbow:F2}");
            Assert.Less(figure.ElbowBone(false).position.y, rig.RUpperArm.position.y - 0.10f,
                "Holder's right arm should pass beneath the raised arm");
            Assert.Less(figure.ElbowBone(true).position.y, rig.LUpperArm.position.y - 0.10f,
                "Holder's left arm should pass beneath the raised arm");
            Assert.Greater(Vector3.Dot(figure.ElbowBone(false).position - rig.RUpperArm.position,
                player.transform.right), -0.12f, "Holder's right elbow should stay at her flank");
            Assert.Less(Vector3.Dot(figure.ElbowBone(true).position - rig.LUpperArm.position,
                player.transform.right), 0.12f, "Holder's left elbow should stay at her flank");
        }

        [UnityTest]
        public IEnumerator EnemyHeavyStrikeIsUnarmedAndReachesForward()
        {
            var go = new GameObject("heavy pose regression");
            var figure = go.AddComponent<EnemyFigure>();
            figure.Preview(EnemyPhase.Attack, 0f, EnemyBrain.AttackId.Heavy, 0f);
            Assert.IsNull(figure.WeaponPivot, "Enemies should be unarmed");
            Vector3 shoulder = go.transform.InverseTransformPoint(figure.ShoulderBone(false).position);
            Vector3 fist = go.transform.InverseTransformPoint(figure.Fist(false).position);
            Assert.Greater(fist.z - shoulder.z, 0.35f, "Heavy punch should extend toward the opponent");
            Object.Destroy(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator EnemyPunchDoesNotDamageWhenFistIsOutOfReach()
        {
            yield return TestScene.LoadPrototype();
            var player = Object.FindFirstObjectByType<PlayerStats>();
            var enemy = Object.FindFirstObjectByType<EnemyBrain>();
            enemy.enabled = false;
            enemy.transform.position = player.transform.position + Vector3.right * 1.8f;
            enemy.transform.rotation = Quaternion.LookRotation(Vector3.left);
            enemy.BeginWindup(EnemyBrain.AttackId.Heavy);
            var figure = enemy.GetComponent<EnemyFigure>();
            figure.Preview(EnemyPhase.Attack, 0f, EnemyBrain.AttackId.Heavy, 0f);
            float hp = player.State.Hp;
            enemy.Strike(1.8f);
            Assert.AreEqual(hp, player.State.Hp, "A punch must not hit when the fist is visibly out of reach");
        }

        [UnityTest]
        public IEnumerator BackGrabKeepsBodiesSeparated()
        {
            yield return TestScene.LoadPrototype();
            var player = Object.FindFirstObjectByType<PlayerStats>();
            var captor = Object.FindFirstObjectByType<EnemyBrain>();
            player.transform.rotation = Quaternion.LookRotation(Vector3.right);
            captor.transform.position = player.transform.position + Vector3.left;
            player.GetComponent<PlayerGrapple>().Begin(captor, GrappleKind.Back);
            float distance = Vector3.Distance(player.transform.position, captor.transform.position);
            // 羽交い締めは胸を相手の背中に付けて組む（離れると腕が伸びきって首へ手を伸ばす画になる）。
            // ただし体がめり込まない距離は守る。
            Assert.GreaterOrEqual(distance, 0.28f, "The back hold must not make the bodies overlap");
            Assert.LessOrEqual(distance, 0.45f, "The back hold must keep the chest against her back");
        }

        [UnityTest]
        public IEnumerator PlayerAttackStaysOnTheHorizontalLaneWhenEnemyIsBehind()
        {
            yield return TestScene.LoadPrototype();
            var player = Object.FindFirstObjectByType<PlayerStats>();
            var combat = player.GetComponent<PlayerCombat>();
            var enemies = Object.FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None);
            foreach (var e in enemies) e.enabled = false;
            var target = enemies[0];
            target.transform.position = player.transform.position + Vector3.left * 1.1f;
            for (int i = 1; i < enemies.Length; i++)
                enemies[i].transform.position = player.transform.position + Vector3.right * 10f;
            var slot = Object.FindFirstObjectByType<VrmCharacterSlot>();
            float waitUntil = Time.realtimeSinceStartup + 8f;
            while ((slot == null || !slot.Loaded || slot.Instance == null) && Time.realtimeSinceStartup < waitUntil)
                yield return null;
            Assert.IsTrue(slot != null && slot.Loaded);
            player.transform.rotation = Quaternion.LookRotation(Vector3.right);
            combat.Begin(AttackLibrary.Light(), 0);
            Assert.IsTrue(combat.IsAttacking);
            while (combat.IsAttacking)
            {
                yield return null;
                Assert.Less(Mathf.Abs(player.transform.forward.z), 0.05f,
                    "Attack turned away from the single horizontal lane");
            }
        }

        [UnityTest]
        public IEnumerator PlayerAttackTargetsFrontEnemyInsteadOfCloserEnemyBehind()
        {
            yield return TestScene.LoadPrototype();
            var player = Object.FindFirstObjectByType<PlayerStats>();
            var combat = player.GetComponent<PlayerCombat>();
            var enemies = Object.FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None);
            foreach (var e in enemies) e.enabled = false;
            enemies[0].transform.position = player.transform.position + Vector3.left * 0.8f;
            enemies[1].transform.position = player.transform.position + Vector3.right * 1.5f;
            for (int i = 2; i < enemies.Length; i++)
                enemies[i].transform.position = player.transform.position + Vector3.right * 10f;
            var slot = Object.FindFirstObjectByType<VrmCharacterSlot>();
            float waitUntil = Time.realtimeSinceStartup + 8f;
            while ((slot == null || !slot.Loaded || slot.Instance == null) && Time.realtimeSinceStartup < waitUntil)
                yield return null;
            Assert.IsTrue(slot != null && slot.Loaded);
            player.transform.rotation = Quaternion.LookRotation(Vector3.right);
            combat.Begin(AttackLibrary.Light(), 0);
            Assert.That(combat.AimDistance, Is.EqualTo(1.5f).Within(0.05f));
            while (combat.IsAttacking)
            {
                yield return null;
                Assert.Less(Mathf.Abs(player.transform.forward.z), 0.05f);
                Assert.Greater(player.transform.forward.x, 0.99f);
            }
        }

        [UnityTest]
        public IEnumerator HiddenNonFadeableRendererBecomesVisibleAgain()
        {
            var go = new GameObject("occlusion regression");
            var renderer = go.AddComponent<MeshRenderer>();
            var fade = go.AddComponent<OcclusionFade>();
            fade.ConfigureForTest(
                new Renderer[] { renderer },
                new Material[][] { new Material[0] },
                new bool[] { false });
            fade.Apply(0.25f);
            Assert.IsFalse(renderer.enabled);
            fade.Apply(1f);
            Assert.IsTrue(renderer.enabled);
            Object.Destroy(go);
            yield return null;
        }
    }
}
