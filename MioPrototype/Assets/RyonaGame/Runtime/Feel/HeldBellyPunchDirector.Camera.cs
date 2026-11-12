using UnityEngine;

namespace Ryona.Game
{
    /// <summary>
    /// 腹パン中のカメラ構図。
    /// </summary>
    public sealed partial class HeldBellyPunchDirector
    {
        float ComboCameraPivot =>
            (_comboZone == PunchZone.Face ? FaceCameraPivotOffset : CameraPivotOffset)
            - (_comboZone == PunchZone.Body ? 0.18f : 0f);

        /// <summary>顔面パンチは「顔と拳」が同じ画に入るよう、腹パンと別のカメラにする。</summary>
        float ZoneDistanceScale =>
            _comboZone == PunchZone.Face ? FaceCameraDistanceScale : CameraDistanceScale;

        float ZonePitch => _comboZone == PunchZone.Face ? FaceCameraPitchOffset : CameraPitchOffset;

        float ZoneYaw => _comboZone == PunchZone.Face ? FaceCameraYawOffset : CameraYawOffset;
        float FacingSign => _stats.transform.forward.x < -0.1f ? -1f : 1f;

        // 画面中心を合わせる横オフセット。寄るほど同じオフセットでも画面内のずれが
        // 大きくなるので、距離倍率に比例させる（基準は旧倍率 0.32 / 0.31 のときの値）。
        float FocusX => _stats.transform.position.x
            + FacingSign * (_comboZone == PunchZone.Face
                ? 0.38f * (FaceCameraDistanceScale / 0.31f)
                : 0.14f * (CameraDistanceScale / 0.32f));

        /// <summary>当たった瞬間だけ少し寄る（顔面パンチの衝撃を画で押す）。</summary>
        float EffectiveDistanceScale =>
            ZoneDistanceScale * (1f - 0.07f * _impactPulse * (_comboZone == PunchZone.Face ? 1f : 0f));

        void ApplyCameraOverrides()
        {
            if (LaunchOptions.TryFloat("-heldpunchcamdist", out float dist))
                CameraDistanceScale = dist;
            if (LaunchOptions.TryFloat("-heldpunchcampivot", out float pivot))
                CameraPivotOffset = pivot;
            if (LaunchOptions.TryFloat("-heldpunchcampitch", out float pitch))
                CameraPitchOffset = pitch;
            if (LaunchOptions.TryFloat("-heldpunchcamyaw", out float yaw))
                CameraYawOffset = yaw;
            if (LaunchOptions.TryFloat("-heldpunchface", out float face))
                FacePunchChance = Mathf.Clamp01(face);
            if (LaunchOptions.TryFloat("-heldpunchfaceyaw", out float faceYaw))
                FaceCameraYawOffset = faceYaw;
            if (LaunchOptions.TryFloat("-heldpunchfacepitch", out float facePitch))
                FaceCameraPitchOffset = facePitch;
            if (LaunchOptions.TryFloat("-heldpunchfacepivot", out float facePivot))
                FaceCameraPivotOffset = facePivot;
            if (LaunchOptions.TryFloat("-heldpunchfacedist", out float faceDist))
                FaceCameraDistanceScale = faceDist;
        }

        void RefreshCamera(float seconds)
        {
            if (!ResolveCamera())
                return;

            float hold = Mathf.Max(0.2f, seconds);
            _camera.PushDistance(EffectiveDistanceScale, hold);
            float pivot = ComboCameraPivot;
            _camera.PushPivot(pivot, hold);

            float yaw = FocusYaw();
            _camera.PushFocus(FocusX, hold, ZonePitch, yaw);

            // ヒット時に押した値だけ出す（毎フレームの保持はログに出さない）
            if (hold >= 1.5f)
                Debug.Log($"[HeldPunchCam] scale={EffectiveDistanceScale:F2} pivot={pivot:F2} " +
                          $"pitch={ZonePitch:F0} yaw={yaw:F0} " +
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

            _camera.HoldFocus(EffectiveDistanceScale, ComboCameraPivot,
                FocusX, 0.5f, ZonePitch, FocusYaw());
        }

        /// <summary>左右どちらでも澪の正面側から撮る。</summary>
        float FocusYaw()
        {
            return ZoneYaw * FacingSign;
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
