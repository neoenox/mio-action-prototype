using UnityEngine;
using System.Collections.Generic;
using AttackId = Ryona.Game.EnemyBrain.AttackId;

namespace Ryona.Game
{
    public sealed partial class EnemyFigure : MonoBehaviour
    {
        internal void ApplyGroundMount(GroundMountAttack mount)
        {
            var stage = mount.Stage;
            if (stage == GroundMountAttack.Beat.Approach || _hips == null)
                return;

            float t = Mathf.SmoothStep(0f, 1f, mount.Progress);
            bool mounting = stage == GroundMountAttack.Beat.Mount;
            bool rolling = stage == GroundMountAttack.Beat.Roll;
            float kneel = stage switch
            {
                GroundMountAttack.Beat.Mount => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.18f, 0.90f, mount.Progress)),
                GroundMountAttack.Beat.Dismount => 1f - t,
                _ => 1f
            };
            float seated = mounting ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.34f, 0.92f, mount.Progress)) : 1f;
            float rollGrip = rolling
                ? mount.RollPhase(0f, 0.17f) * (1f - mount.RollPhase(0.86f, 1f))
                : 0f;
            float rollPull = rolling ? Mathf.Sin(Mathf.PI * mount.ShoulderRoll) : 0f;
            float transfer = mounting ? Mathf.Sin(Mathf.PI * t) : 0f;
            _hips.localPosition = Vector3.Lerp(new Vector3(_hipsX, _hipsY, _hipsZ),
                new Vector3(mount.Side * -0.06f * transfer,
                    Mathf.Lerp(0.48f, 0.39f, seated) + 0.075f * transfer, -0.06f), kneel);

            bool left = mount.LeftPunch;
            float windup = mount.Cock;
            float followThrough = mount.Drive;
            float punchSide = left ? -1f : 1f;
            float twist = punchSide * (28f * windup - 24f * followThrough);
            _chest.localRotation = Quaternion.Slerp(
                Quaternion.Euler(-_torsoPitch, _torsoYaw * 0.55f, _torsoRoll),
                Quaternion.Euler(Mathf.Lerp(53f, 32f, seated) + 19f * rollGrip - 15f * rollPull - 14f * windup
                    + 32f * followThrough, twist + 18f * rollPull * mount.Side,
                    punchSide * (10f * windup - 12f * followThrough) - 9f * mount.Side * rollPull), kneel);
            _head.localRotation = Quaternion.Euler(12f * kneel, -0.55f * twist, 0f);

            float lead = mounting ? Mathf.Sin(Mathf.PI * Mathf.InverseLerp(0.02f, 0.56f, mount.Progress)) : 0f;
            float trail = mounting ? Mathf.Sin(Mathf.PI * Mathf.InverseLerp(0.28f, 0.86f, mount.Progress)) : 0f;
            MountLeg(false, Mathf.Max(kneel, 0.62f * lead), seated, lead);
            MountLeg(true, Mathf.Max(kneel, 0.62f * trail), seated, trail);
            SetWeaponVisible(false);

            var rig = mount.TargetRig;
            Vector3 side = transform.right * punchSide;
            Vector3 entry = rig.Hips.position + Vector3.up * 0.20f;
            Vector3 raised = MountRaisedFist(left, side);
            Vector3 outside = MountOpenHand(rig, side);
            Vector3 otherOutside = MountOpenHand(rig, -side);
            Vector3 otherHand = Vector3.Lerp(otherOutside, MountRaisedFist(!left, -side), mount.NextArmWindup);
            Vector3 contact = mount.StrikeTarget + Vector3.up * 0.095f + side * 0.055f - transform.forward * 0.025f;
            Vector3 punchHand = Vector3.Lerp(outside, raised, mount.ArmWindup);
            if (stage == GroundMountAttack.Beat.Strike)
            {
                // Drop toward cheek height outside the guard, then sweep inward.
                Vector3 outsideCheek = contact + side * 0.42f - transform.forward * 0.10f;
                punchHand = Vector3.Lerp(Vector3.Lerp(raised, outsideCheek, followThrough),
                    Vector3.Lerp(outsideCheek, contact, followThrough), followThrough);
            }
            else if (stage == GroundMountAttack.Beat.Recover)
            {
                // Clear the cheek and stay outside. The other hand starts rising before this one settles.
                punchHand = Vector3.Lerp(contact, outside, t)
                    + (side * 0.11f + Vector3.up * 0.12f) * Mathf.Sin(Mathf.PI * t);
            }
            Vector3 punchGoal = Vector3.Lerp(entry + side * 0.20f, punchHand, seated);
            Vector3 supportGoal = Vector3.Lerp(entry - side * 0.20f, otherHand, seated);
            if (rolling)
            {
                var shoulder = mount.RollLeftShoulder ? rig.LUpperArm : rig.RUpperArm;
                Vector3 grip = shoulder.position + Vector3.up * 0.066f - transform.forward * 0.035f;
                Vector3 brace = Vector3.Lerp(rig.Chest.position, rig.Hips.position, 0.25f)
                    + Vector3.up * 0.09f - transform.right * (0.10f * mount.Side);
                bool punchHandPulls = left == (mount.Side < 0f);
                punchGoal = Vector3.Lerp(punchGoal, punchHandPulls ? grip : brace, rollGrip);
                supportGoal = Vector3.Lerp(supportGoal, punchHandPulls ? brace : grip, rollGrip);
            }
            AimMountArm(!left, supportGoal, kneel);
            AimMountArm(left, punchGoal, kneel);
        }

        Vector3 MountOpenHand(ProceduralRig rig, Vector3 outside)
        {
            return rig.Hips.position + transform.forward * 0.37f + outside * 0.24f + Vector3.up * 0.20f;
        }

        Vector3 MountRaisedFist(bool left, Vector3 outside)
        {
            return ShoulderBone(left).position + outside * 0.33f + transform.forward * 0.11f + Vector3.up * 0.07f;
        }

        void MountLeg(bool left, float kneel, float seated, float swing)
        {
            Transform hip = left ? _hipL : _hipR;
            Transform knee = left ? _kneeL : _kneeR;
            Transform ankle = left ? _ankleL : _ankleR;
            float side = left ? -1f : 1f;
            // Both knees carry the weight. Open the hips and fold each shin back along the floor.
            Vector3 lateral = transform.right * side;
            Vector3 thighDirection = (lateral * 0.20f + transform.forward * 0.22f - transform.up * 0.33f).normalized;
            Vector3 shinDirection = (-transform.forward + lateral * 0.16f - transform.up * 0.06f).normalized;
            Vector3 bendDirection = Vector3.ProjectOnPlane(shinDirection, thighDirection);
            Quaternion mounted = Quaternion.Inverse(hip.parent.rotation)
                * Quaternion.LookRotation(-bendDirection, -thighDirection);
            Quaternion openHip = Quaternion.Euler((left ? -57f : -27f) - 23f * swing,
                side * (12f + 18f * swing), side * (6f + 12f * swing));
            float bend = Mathf.Lerp(left ? 83f : 111f, Vector3.Angle(thighDirection, shinDirection), seated);
            hip.localRotation = Quaternion.Slerp(
                Quaternion.Euler(left ? _legLPitch : _legRPitch, 0f, 0f),
                Quaternion.Slerp(openHip, mounted, seated), kneel);
            float flex = Mathf.Lerp(left ? _kneeLAngle : _kneeRAngle, bend + 8f * swing, kneel);
            knee.localRotation = Quaternion.Euler(Mathf.Clamp(flex, 0f, 132f), 0f, 0f);
            ankle.localRotation = Quaternion.Euler(-12f * kneel, 0f, 0f);
        }

        void AimMountArm(bool left, Vector3 goal, float weight)
        {
            Transform shoulder = ShoulderBone(left);
            Transform elbow = ElbowBone(left);
            Transform wrist = WristBone(left);
            Vector3 pole = shoulder.position + transform.right * (left ? -0.38f : 0.38f)
                - transform.forward * 0.18f - transform.up * 0.21f;
            Vector3 toGoal = Vector3.Lerp(wrist.position, goal, weight) - shoulder.position;
            float upperLength = Vector3.Distance(shoulder.position, elbow.position);
            float lowerLength = Vector3.Distance(elbow.position, wrist.position);
            const float maxFlex = 128f;
            float minReach = Mathf.Sqrt(upperLength * upperLength + lowerLength * lowerLength
                + 2f * upperLength * lowerLength * Mathf.Cos(maxFlex * Mathf.Deg2Rad));
            Vector3 target = shoulder.position + toGoal.normalized * Mathf.Max(minReach, toGoal.magnitude);
            ProceduralRig.SolveTwoBone(shoulder, elbow, wrist, target, pole);
            SettleElbowHinge(shoulder, elbow, wrist);
            // The proxy bones extend along -Y. Orient the shoulder to the bend plane,
            // then flex the elbow on its hinge instead of twisting it to aim the fist.
            Vector3 upper = (elbow.position - shoulder.position).normalized;
            Vector3 forearm = (wrist.position - elbow.position).normalized;
            Vector3 bendDirection = Vector3.ProjectOnPlane(forearm, upper);
            if (bendDirection.sqrMagnitude > 0.0001f)
            {
                shoulder.rotation = Quaternion.LookRotation(bendDirection, -upper);
                elbow.localRotation = Quaternion.Euler(-Mathf.Clamp(Vector3.Angle(upper, forearm), 5f, maxFlex), 0f, 0f);
            }
            wrist.localRotation = Quaternion.identity;
        }

    }
}
