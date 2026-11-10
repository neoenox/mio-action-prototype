using System.Collections;
using NUnit.Framework;
using Ryona.Game;
using UnityEngine;
using UnityEngine.TestTools;

namespace Ryona.Tests
{
    /// <summary>
    /// ベアハッグ（<see cref="GrappleKind.BearHug"/>）の回帰テスト。
    ///
    /// 正面から胴を抱えて締める。制限時間つきで、12 回もがけば脱出できる。
    /// 押さえつけと違って膝は入らず、一定間隔で締め付けて HP を少し削る。
    /// </summary>
    public sealed class BearHugTests
    {
        PlayerStats _player;
        PlayerGrapple _grapple;
        EnemyBrain _captor;

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
            Assert.GreaterOrEqual(enemies.Length, 1);

            _captor = enemies[0];
            for (int i = 1; i < enemies.Length; i++)
                enemies[i].enabled = false;

            // プレイヤーは +X 向き。掴む側は正面（+X）。
            _player.transform.rotation = Quaternion.LookRotation(Vector3.right);
            _captor.transform.position = _player.transform.position + Vector3.right * 1.15f;
            _captor.transform.rotation = Quaternion.LookRotation(Vector3.left);
            _captor.enabled = false;

            yield return null;
        }

        [UnityTest]
        public IEnumerator ベアハッグは制限時間つきで始まり相手の方を向く()
        {
            yield return Load();

            _grapple.Begin(_captor, GrappleKind.BearHug);

            Assert.IsTrue(_grapple.IsGrappled, "ベアハッグが始まらない");
            Assert.AreEqual(GrappleKind.BearHug, _grapple.Kind);
            Assert.IsTrue(_grapple.HasTimeLimit, "ベアハッグは時間切れがある");
            Assert.AreEqual(CombatConstants.BearHugLimitSeconds, _grapple.Remaining, 0.05f);
            Assert.AreEqual(CombatConstants.MashBaseBearHug, _grapple.RequiredInputs, 0.01f,
                "必要入力は 12 回");

            Vector3 toCaptor = _captor.transform.position - _player.transform.position;
            toCaptor.y = 0f;
            Assert.Greater(Vector3.Dot(_player.transform.forward, toCaptor.normalized), 0.9f,
                "ベアハッグは正面からなので、彼女は相手を向く");
            Assert.AreEqual(0.54f, toCaptor.magnitude, 0.05f, "掴んだ距離が 0.54m になっていない");
        }

        [UnityTest]
        public IEnumerator 締め付けてHPを少し削りもがけば脱出できる()
        {
            yield return Load();

            float rigDeadline = Time.realtimeSinceStartup + 8f;
            var rig = _player.GetComponentInChildren<ProceduralRig>();
            while (rig == null && Time.realtimeSinceStartup < rigDeadline)
            {
                yield return null;
                rig = _player.GetComponentInChildren<ProceduralRig>();
            }
            Assert.IsNotNull(rig);
            var figure = _captor.GetComponent<EnemyFigure>();

            _captor.enabled = true;
            _captor.QueueBearHug();

            float deadline = Time.realtimeSinceStartup + 6f;
            while ((_grapple.Kind != GrappleKind.BearHug || !_grapple.IsGrappled)
                && Time.realtimeSinceStartup < deadline)
                yield return null;

            Assert.IsTrue(_grapple.IsGrappled && _grapple.Kind == GrappleKind.BearHug,
                "正面からベアハッグに入らない");
            Assert.IsTrue(_captor.IsBearHugging, "締め付けの状態になっていない");
            var camera = Object.FindFirstObjectByType<ThirdPersonCamera>();
            Assert.IsNotNull(camera);

            float hpBefore = _player.State.Hp;
            yield return new WaitForSeconds(0.55f);
            Assert.Greater(_grapple.FrontEscapeRemaining, 0f);
            Assert.Less(camera.EffectDistanceScale, 0.30f, "締め付けの前から顔と腕が見える距離まで寄る");
            float squeezeDeadline = Time.realtimeSinceStartup + CombatConstants.FrontGrappleEscapeSeconds + 1.5f;
            while (_grapple.SqueezePulse <= 0f && Time.realtimeSinceStartup < squeezeDeadline)
                yield return null;
            Assert.Greater(_grapple.SqueezePulse, 0f, "締め付けのパルスが来ない");

            // 密着 → 少し離れる → 密着 を繰り返す。距離の振れ幅と、密着時のダメージを見る。
            float minDistance = float.MaxValue;
            float maxDistance = 0f;
            float closestCameraScale = 1f;
            float worstWristGap = 0f;
            float leastPalmContact = 1f;
            float restElbowSpread = float.PositiveInfinity;
            float squeezeElbowSpread = 0f;
            float leastWrapOutside = float.PositiveInfinity;
            float lowestWrap = float.PositiveInfinity;
            float mostForwardWrapWrist = float.NegativeInfinity;
            bool sawContact = false;
            bool sawPull = false;
            float observeUntil = Time.realtimeSinceStartup + 2.4f;
            while (Time.realtimeSinceStartup < observeUntil)
            {
                yield return null;
                float distance = Vector3.Distance(
                    _player.transform.position, _captor.transform.position);
                minDistance = Mathf.Min(minDistance, distance);
                maxDistance = Mathf.Max(maxDistance, distance);
                closestCameraScale = Mathf.Min(closestCameraScale, camera.EffectDistanceScale);
                var chest = figure.ChestBone;
                Vector3 leftWrist = chest.InverseTransformPoint(rig.LHand.position) / figure.Height;
                Vector3 rightWrist = chest.InverseTransformPoint(rig.RHand.position) / figure.Height;
                worstWristGap = Mathf.Max(worstWristGap,
                    Mathf.Max(TorsoGap(leftWrist, 1f), TorsoGap(rightWrist, -1f)));
                leastPalmContact = Mathf.Min(leastPalmContact,
                    Mathf.Min(Vector3.Dot(rig.PalmNormalWorld(true), -chest.forward),
                        Vector3.Dot(rig.PalmNormalWorld(false), -chest.forward)));
                float elbowSpread = Mathf.Abs(Vector3.Dot(
                    rig.RLowerArm.position - rig.LLowerArm.position, _player.transform.right));
                if (_grapple.SqueezePulse < 0.1f)
                    restElbowSpread = Mathf.Min(restElbowSpread, elbowSpread);
                if (_grapple.IsBearHugContact)
                {
                    sawContact = true;
                    squeezeElbowSpread = Mathf.Max(squeezeElbowSpread, elbowSpread);
                    var leftArm = Vector3.Lerp(rig.LUpperArm.position, rig.LLowerArm.position, 0.55f);
                    var rightArm = Vector3.Lerp(rig.RUpperArm.position, rig.RLowerArm.position, 0.55f);
                    Vector3 rightWrap = figure.ElbowBone(false).position - leftArm;
                    Vector3 leftWrap = figure.ElbowBone(true).position - rightArm;
                    leastWrapOutside = Mathf.Min(leastWrapOutside,
                        Mathf.Min(Vector3.Dot(rightWrap, -_player.transform.right),
                            Vector3.Dot(leftWrap, _player.transform.right)));
                    lowestWrap = Mathf.Min(lowestWrap, Mathf.Min(rightWrap.y, leftWrap.y));
                    mostForwardWrapWrist = Mathf.Max(mostForwardWrapWrist,
                        Mathf.Max(Vector3.Dot(figure.WristBone(false).position - rig.Spine.position, _player.transform.forward),
                            Vector3.Dot(figure.WristBone(true).position - rig.Spine.position, _player.transform.forward)));
                }
                if (_grapple.PullPulse > 0.3f)
                    sawPull = true;
            }

            Assert.IsTrue(sawContact, "密着した瞬間がない");
            Assert.IsTrue(sawPull, "引き寄せられている感じ（PullPulse）がない");
            Assert.Less(minDistance, 0.42f, "密着しきっていない");
            Assert.Greater(maxDistance - minDistance, 0.06f,
                $"体が密着と離れを繰り返していない（{minDistance:F2}〜{maxDistance:F2}m）");
            Assert.Less(_player.State.Hp, hpBefore - 2f, "密着時のダメージが入っていない");
            Assert.Greater(_grapple.LiftPulse, 0.5f, "抱え上げられていない");
            Assert.IsFalse(_player.IsDead, "ベアハッグだけで死亡している");
            Assert.Less(closestCameraScale, 0.25f, "締め付け時は顔と腕にさらに寄る");
            Assert.Less(worstWristGap, 0.055f, "両手が敵の胴の前面から離れない");
            Assert.Greater(leastPalmContact, 0.8f, "掌を敵の胴へ向けて支える");
            Assert.Greater(squeezeElbowSpread, restElbowSpread + 0.02f, "締め付け時に肘を張って押し返す");
            // 肘の外側への張り出しは、締め付けの位相とフレームレートで 2〜3.5cm の幅で揺れる
            // （実測 0.021〜0.035。旧しきい値 0.035 は環境差 1mm で落ちる）。
            // ここで見たいのは「肘が彼女の上腕より外側にある」ことなので、揺れの下限で判定する。
            Assert.Greater(leastWrapOutside, 0.015f, "敵の肘は澪の上腕より外側にある");
            Assert.Greater(lowestWrap, -0.05f, "敵の腕は澪の脇の下へ落ちず、上腕を抱え込む");
            Assert.Less(mostForwardWrapWrist, -0.04f, "敵の両手が澪の背中まで回る");

            // 12 回もがけば脱出できる。
            int need = Mathf.CeilToInt(_grapple.RequiredInputs);
            for (int i = 0; i < need; i++)
                _grapple.Struggle();
            yield return null;

            Assert.IsFalse(_grapple.IsGrappled, "12 回もがけば振りほどける");
            Assert.AreEqual(EnemyPhase.Stagger, _captor.Phase, "振りほどかれた側はよろける");

            // 脱出後に締め付けが続かない。
            float hpAfter = _player.State.Hp;
            yield return new WaitForSeconds(1.2f);
            Assert.AreEqual(hpAfter, _player.State.Hp, 0.01f, "脱出後も締め付けが続いている");
            Assert.Less(_grapple.LiftPulse, 0.1f, "脱出後も抱え上げられたまま");
            Assert.AreEqual(1f, camera.EffectDistanceScale, 0.01f, "脱出後は通常のカメラ距離に戻る");
        }

        static float TorsoGap(Vector3 wrist, float side)
        {
            // 胸ボーンの前にある胴のうち、左右の手で支える範囲までの距離。
            var contact = new Vector3(side * 0.06f, Mathf.Clamp(wrist.y, 0.05f, 0.10f), 0.06f);
            return Vector3.Distance(wrist, contact);
        }
    }
}
