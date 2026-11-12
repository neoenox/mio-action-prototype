using UnityEngine;
using System.Collections.Generic;
using AttackId = Ryona.Game.EnemyBrain.AttackId;


namespace Ryona.Game
{
    /// <summary>
    /// 彼女の姿勢更新後、正面から胴を抱え込む腕を実際の体格に合わせる。
    /// 両腕を澪の上腕の外から背中へ回し、胴と腕をまとめて抱える。
    /// </summary>
    [DefaultExecutionOrder(21000)]
    public sealed class BearHugPose : MonoBehaviour
    {
        EnemyFigure _figure;
        EnemyBrain _brain;
        ProceduralRig _targetRig;
        ThirdPersonCamera _camera;

        void LateUpdate()
        {
            if (_figure == null) _figure = GetComponent<EnemyFigure>();
            if (_brain == null) _brain = GetComponent<EnemyBrain>();
            if (_figure == null || _brain == null || _brain.Player == null)
                return;
            if (!_brain.IsBearHugging)
                return;

            var hold = _brain.Player.GetComponent<PlayerGrapple>();
            if (hold == null || !hold.IsGrappled || hold.Captor != _brain
                || hold.Kind != GrappleKind.BearHug)
                return;

            if (_targetRig == null)
                _targetRig = _brain.Player.GetComponentInChildren<ProceduralRig>();
            if (_targetRig == null || !_targetRig.Ready
                || _targetRig.LUpperArm == null || _targetRig.RUpperArm == null)
                return;

            // 腕ごと抱えられるよう、手は肩より少し下の背中へ回す。
            Transform player = _brain.Player.transform;
            Vector3 center = _targetRig.Spine != null
                ? Vector3.Lerp(_targetRig.Spine.position, _targetRig.Chest != null
                    ? _targetRig.Chest.position : _targetRig.Spine.position, 0.80f)
                : player.position + player.up * 1.17f;
            center -= player.forward * 0.105f;

            // 抱え上げるほど手を上へ、締め付けるほど手を内側へ寄せる。
            // 締め付け中は細かく震わせて、力を込めているのを見せる。
            float lift = hold.LiftPulse;
            float squeeze = hold.SqueezePulse;
            float yank = hold.PullPulse;
            var chest = _figure.ChestBone;
            if (chest != null)
            {
                float waiting = hold.FrontEscapePoseWeight;
                float struggle = Mathf.Lerp(hold.StrugglePulse, hold.FrontEscapeEffort, waiting);
                float effort = Mathf.Clamp01(0.85f * squeeze + 0.55f * struggle + 0.2f * yank + 0.28f * waiting);
                BracePlayerArm(true, player, chest, effort);
                BracePlayerArm(false, player, chest, effort);
            }
            float tremor = Mathf.Sin(Time.time * 31f) * 0.008f * squeeze;

            // 引き寄せでは手を自分の胸元へ引き込み、彼女を抱き寄せる。
            Vector3 pull = player.up * (0.035f * lift + 0.028f * squeeze + tremor)
                + player.forward * (0.025f * yank);
            float spread = Mathf.Lerp(0.085f, 0.068f, squeeze);

            WrapArm(false, player, center + pull, spread);
            WrapArm(true, player, center + pull, spread);

            // 二人の顔と背中に回した腕が入る高さで寄り、締め付けで少しだけ近づける。
            if (_camera == null)
                _camera = FindFirstObjectByType<ThirdPersonCamera>();
            if (_camera != null)
            {
                float sign = player.forward.x >= 0f ? 1f : -1f;
                float close = Mathf.Clamp01(hold.SqueezePulse);
                float x = player.position.x + sign * 0.16f;
                _camera.HoldFocus(
                    Mathf.Lerp(0.25f, 0.21f, close),
                    Mathf.Lerp(0.30f, 0.36f, close),
                    x, 0.5f, 3f, -24f * sign);
            }
        }

        void BracePlayerArm(bool left, Transform player, Transform chest, float effort)
        {
            var upper = left ? _targetRig.LUpperArm : _targetRig.RUpperArm;
            var elbow = left ? _targetRig.LLowerArm : _targetRig.RLowerArm;
            var wrist = left ? _targetRig.LHand : _targetRig.RHand;
            if (upper == null || elbow == null || wrist == null)
                return;

            // 向かい合うので、澪の左手は相手の右胸へ。胴の丸みに沿って掌を当てる。
            float side = left ? 1f : -1f;
            Vector3 goal = chest.TransformPoint(_figure.Height *
                new Vector3(side * 0.078f, Mathf.Lerp(0.073f, 0.097f, effort), 0.055f));
            Vector3 pole = upper.position
                - player.right * (side * Mathf.Lerp(0.16f, 0.38f, effort))
                - player.up * Mathf.Lerp(0.34f, 0.19f, effort)
                + player.forward * 0.10f;

            ProceduralRig.SolveTwoBone(upper, elbow, wrist, goal, pole, player.right);
            EnemyFigure.SettleElbowHinge(upper, elbow, wrist);
            Vector3 inward = -chest.forward - chest.right * (0.55f * side);
            _targetRig.AlignPalm(left, inward.normalized, chest.up);
        }

        void WrapArm(bool left, Transform player, Vector3 behind, float spread)
        {
            var upper = _figure.ShoulderBone(left);
            var elbow = _figure.ElbowBone(left);
            var wrist = _figure.WristBone(left);
            if (upper == null || elbow == null || wrist == null)
                return;

            // 向かい合う相手の上腕を基準にし、もがいて肘が動いても外側から包む。
            var playerShoulder = left ? _targetRig.RUpperArm : _targetRig.LUpperArm;
            var playerElbow = left ? _targetRig.RLowerArm : _targetRig.LLowerArm;
            Vector3 arm = playerElbow != null
                ? Vector3.Lerp(playerShoulder.position, playerElbow.position, 0.55f)
                : playerShoulder.position;
            Vector3 outward = player.right * (left ? 1f : -1f);
            Vector3 pole = Vector3.Lerp(upper.position, arm, 0.45f)
                + outward * 0.80f + player.up * 0.15f;

            ProceduralRig.SolveTwoBone(upper, elbow, wrist, behind + outward * spread, pole, player.up);
            EnemyFigure.SettleElbowHinge(upper, elbow, wrist);
        }
    }
}
