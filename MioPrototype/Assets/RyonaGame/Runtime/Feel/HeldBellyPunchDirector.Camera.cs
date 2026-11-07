using UnityEngine;

namespace Ryona.Game
{
    /// <summary>
    /// 腹パン中のカメラ構図。
    /// </summary>
    public sealed partial class HeldBellyPunchDirector
    {
        void ApplyCameraOverrides()
        {
            foreach (string a in System.Environment.GetCommandLineArgs())
            {
                if (TryRead(a, "-heldpunchcamdist=", out float dist))
                    CameraDistanceScale = dist;
                else if (TryRead(a, "-heldpunchcampivot=", out float pivot))
                    CameraPivotOffset = pivot;
                else if (TryRead(a, "-heldpunchcampitch=", out float pitch))
                    CameraPitchOffset = pitch;
                else if (TryRead(a, "-heldpunchcamyaw=", out float yaw))
                    CameraYawOffset = yaw;
                else if (TryRead(a, "-heldpunchface=", out float face))
                    FacePunchChance = Mathf.Clamp01(face);
            }
        }

        static bool TryRead(string arg, string key, out float value)
        {
            value = 0f;

            return arg.StartsWith(key, System.StringComparison.OrdinalIgnoreCase)
                && float.TryParse(arg.Substring(key.Length),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out value);
        }

        void RefreshCamera(float seconds)
        {
            if (!ResolveCamera())
                return;

            float hold = Mathf.Max(0.2f, seconds);
            _camera.PushDistance(CameraDistanceScale, hold);
            _camera.PushPivot(CameraPivotOffset, hold);

            float yaw = FocusYaw();
            _camera.PushFocus(_stats.transform.position.x, hold, CameraPitchOffset, yaw);

            // ヒット時に押した値だけ出す（毎フレームの保持はログに出さない）
            if (hold >= 1.5f)
                Debug.Log($"[HeldPunchCam] scale={CameraDistanceScale:F2} pivot={CameraPivotOffset:F2} " +
                          $"pitch={CameraPitchOffset:F0} yaw={yaw:F0} " +
                          $"liveScale={_camera.EffectDistanceScale:F2} livePivot={_camera.EffectPivotOffset:F2}");
        }

        /// <summary>
        /// コンボ中、同じ構図を毎フレーム保つ。
        /// <see cref="RefreshCamera"/> の押し込みは 0.2 秒で抜けるので、
        /// 2 発目以降は通常の距離に戻って顔が小さくなる。ここでは保持用の API で固定する。
        /// </summary>
        void HoldFraming()
        {
            if (!ResolveCamera())
                return;

            _camera.HoldFocus(CameraDistanceScale, CameraPivotOffset,
                _stats.transform.position.x, 0.5f, CameraPitchOffset, FocusYaw());
        }

        /// <summary>腹パン中に回り込む向き。左向きのときに背中側へ回らないよう符号を合わせる。</summary>
        float FocusYaw()
        {
            float facing = _stats.transform.forward.x;
            float sign = Mathf.Abs(facing) > 0.1f ? Mathf.Sign(facing) : 1f;
            return CameraYawOffset * sign;
        }

        bool ResolveCamera()
        {
            if (_camera == null)
            {
                var cam = Camera.main;
                _camera = cam != null
                    ? cam.GetComponent<ThirdPersonCamera>()
                    : FindFirstObjectByType<ThirdPersonCamera>();
            }

            return _camera != null && _stats != null;
        }

        /// <summary>演出の間、周囲の敵が追撃しないようにする。</summary>
    }
}
