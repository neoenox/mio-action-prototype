using System;
using UnityEngine;

namespace Ryona.Game
{
    /// <summary>
    /// 地面マウント（<see cref="GroundMountAttack"/>）の姿勢確認用。
    ///
    /// <c>-mountburst=&lt;prefix&gt;</c> で起動すると、2 秒後に最寄りの敵の前で彼女をダウンさせ、
    /// マウントが始まった瞬間から 0.05 秒ごとに 60 枚（3 秒ぶん）撮る。
    /// <c>-mountprone</c> を付けると前へ崩れる（うつ伏せ）ダウンで試す。
    /// 撮影後は自動で終了する。
    /// </summary>
    [AddComponentMenu("Ryona/Ground Mount Test Driver")]
    [DefaultExecutionOrder(30000)]
    public sealed class GroundMountTestDriver : MonoBehaviour
    {
        const int ShotCount = 60;
        const float ShotInterval = 0.05f;

        PlayerStats _player;
        GroundMountAttack _mount;
        string _prefix;
        bool _prone;
        bool _wide;
        bool _solo;
        bool _top;
        bool _entry;
        bool _hidden;
        bool _started;
        bool _capturing;
        int _shots;
        float _nextShotAt;
        float _age;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            string prefix = LaunchOptions.Value("-mountburst");
            bool prone = LaunchOptions.Has("-mountprone");
            bool wide = LaunchOptions.Has("-mountwide");
            bool solo = LaunchOptions.Has("-mountsolo");
            bool top = LaunchOptions.Has("-mounttop");
            bool entry = LaunchOptions.Has("-mountentry");

            if (string.IsNullOrEmpty(prefix))
                return;

            var go = new GameObject("Ground Mount Test Driver");
            var driver = go.AddComponent<GroundMountTestDriver>();
            driver._prefix = prefix;
            driver._prone = prone;
            driver._wide = wide;
            driver._solo = solo;
            driver._top = top;
            driver._entry = entry;
        }

        void Update()
        {
            if (_player == null)
            {
                _player = FindFirstObjectByType<PlayerStats>();
                if (_player == null)
                    return;

                _mount = _player.GroundMount;
            }

            _age += Time.deltaTime;
            if (!_started && _age >= 2f)
            {
                _started = true;
                ForceDown();
            }

            if (!_capturing && _mount != null && (_entry
                ? _started && _player.IsDown
                : _mount.Attacker != null && _mount.Stage >= GroundMountAttack.Beat.Mount &&
                    _mount.Stage < GroundMountAttack.Beat.Dismount))
            {
                _capturing = true;
                _nextShotAt = Time.unscaledTime;
                Debug.Log($"[MountTest] capture start stage={_mount.Stage} side={_mount.Side:F0} " +
                          $"leftPunch={_mount.LeftPunch}");
            }

            if (_capturing)
                CaptureTick();
        }

        void ForceDown()
        {
            EnemyBrain best = null;
            float bestDistance = 4f;
            foreach (var enemy in FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None))
            {
                if (enemy == null || !enemy.isActiveAndEnabled || !enemy.IsAlive)
                    continue;

                float distance = Vector3.Distance(enemy.transform.position, _player.transform.position);
                if (distance >= bestDistance)
                    continue;

                bestDistance = distance;
                best = enemy;
            }

            if (best == null)
            {
                Debug.LogWarning("[MountTest] 敵が見つからない");
                return;
            }

            // 彼女の正面（+forward）に立たせる。ForceDown は攻撃者が正面なら後ろへ倒す。
            Vector3 spot = _player.transform.position + _player.transform.forward * 1.15f;
            best.transform.position = spot;
            best.transform.rotation = Quaternion.LookRotation(
                (_player.transform.position - spot).normalized, Vector3.up);
            best.HoldFire(0f);
            _player.ForceDown(8f, "mounttest", best.transform, forward: _prone);
            Debug.Log($"[MountTest] down by {best.name} prone={_prone} at {spot}");
        }

        void LateUpdate()
        {
            if (!_wide || !_capturing)
                return;

            var cam = Camera.main;
            if (cam == null || _mount == null || _mount.TargetRig == null || !_mount.TargetRig.Ready)
                return;

            if (_solo)
            {
                if (!_hidden)
                {
                    _hidden = true;
                    Debug.Log("[MountTest] 敵を非表示（彼女の腕だけを見る）");
                }
                // OcclusionFade が毎フレーム material を差し替えるので、
                // enabled だけでなく forceRenderingOff も毎フレーム立てる。
                foreach (var enemy in FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None))
                {
                    foreach (var part in enemy.GetComponentsInChildren<Renderer>())
                    {
                        part.enabled = false;
                        part.forceRenderingOff = true;
                    }
                }
            }

            var rig = _mount.TargetRig;
            Vector3 axis = Vector3.ProjectOnPlane(rig.Head.position - rig.Hips.position, Vector3.up);
            if (axis.sqrMagnitude < 1e-4f)
                return;
            axis.Normalize();
            Vector3 across = Vector3.Cross(Vector3.up, axis).normalized;
            Vector3 target = Vector3.Lerp(rig.Head.position, rig.Hips.position, 0.55f) + Vector3.up * 0.05f;
            if (_top)
            {
                // 真上から。腕が体のどちら側にどう出ているかを読むため。
                cam.transform.position = target + Vector3.up * 2.1f + axis * 0.45f;
            }
            else
            {
                cam.transform.position = target - across * 1.55f + axis * 0.10f + Vector3.up * 1.75f;
            }
            cam.transform.LookAt(target);
        }

        void CaptureTick()
        {
            if (Time.unscaledTime < _nextShotAt)
                return;

            if (_mount != null)
                LogArms();

            string dir = System.IO.Path.GetDirectoryName(_prefix);
            string name = System.IO.Path.GetFileNameWithoutExtension(_prefix);
            if (string.IsNullOrEmpty(dir))
                dir = ".";

            string file = System.IO.Path.Combine(dir, $"{name}_{_shots:00}.png");
            ScreenCapture.CaptureScreenshot(file);
            Debug.Log($"[MountTest] shot {_shots} stage={(_mount != null ? _mount.Stage.ToString() : "none")} {file}");

            _shots++;
            _nextShotAt += ShotInterval;
            if (_shots >= (_entry ? 110 : ShotCount))
            {
                _capturing = false;
                Application.Quit();
            }
        }

        /// <summary>腕の形を数値で残す（肘の伸び・床からの高さ・フレーム間の移動量）。</summary>
        void LogArms()
        {
            var rig = _mount.TargetRig;
            if (rig == null || !rig.Ready)
                return;

            float groundY = _player.transform.position.y;
            Vector3 handL = rig.LHand.position;
            Vector3 handR = rig.RHand.position;
            string left = ArmText(true, rig, groundY);
            string right = ArmText(false, rig, groundY);
            string moved;
            if (_hasPrevHands)
                moved = $" dL={(handL - _prevL).magnitude:F3} dR={(handR - _prevR).magnitude:F3}";
            else
                moved = "";
            _prevL = handL;
            _prevR = handR;
            _hasPrevHands = true;

            Debug.Log($"[MountArm] stage={_mount.Stage} p={_mount.Progress:F3} side={_mount.Side:F0} " +
                      $"chest={_mount.ShoulderRoll:F3} pelvis={_mount.RollProgress:F3} | {left} | {right}{moved}");
        }

        Vector3 _prevL;
        Vector3 _prevR;
        bool _hasPrevHands;

        static string ArmText(bool isLeft, ProceduralRig rig, float groundY)
        {
            Transform shoulder = isLeft ? rig.LUpperArm : rig.RUpperArm;
            Transform elbow = isLeft ? rig.LLowerArm : rig.RLowerArm;
            Transform hand = isLeft ? rig.LHand : rig.RHand;
            if (shoulder == null || elbow == null || hand == null)
                return "none";

            Vector3 upper = shoulder.position - elbow.position;
            Vector3 fore = hand.position - elbow.position;
            float bend = Vector3.Angle(upper, fore);                 // 180 = 真っ直ぐ
            float reach = Vector3.Distance(shoulder.position, hand.position) /
                          Mathf.Max(0.001f, upper.magnitude + fore.magnitude);
            Vector3 local = rig.Chest.InverseTransformPoint(hand.position);
            string side = isLeft ? "L" : "R";
            return $"{side} bend={bend:F0} reach={reach:F2} handY={hand.position.y - groundY:F2} " +
                   $"local=({local.x:F2},{local.y:F2},{local.z:F2})";
        }
    }
}
