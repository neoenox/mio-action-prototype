using UnityEngine;

namespace Ryona.Game
{
    /// <summary>
    /// 腹パン中のカメラ構図。
    /// </summary>
    public sealed partial class HeldBellyPunchDirector
    {
        float BodyFold => _comboZone == PunchZone.Body && _anim != null ? _anim.HeldBellyPunchFold : 0f;
        float BodyFatigue => _comboZone == PunchZone.Body && _anim != null ? _anim.HeldBellyPunchFatigue : 0f;
        float BodyLift => _comboZone == PunchZone.Body && _anim != null ? _anim.HeldBellyCaptorLift : 0f;
        float BodyDangle => _comboZone == PunchZone.Body && _anim != null ? _anim.HeldBellyDangle : 0f;
        float FinisherHoist => _anim != null ? _anim.FinisherHoistWeight : 0f;

        float BaseBodyPivot =>
            LaunchOptions.TryFloat("-heldpunchcampivot", out _) ? CameraPivotOffset : 0.34f;

        float ComboCameraPivot =>
            (_comboZone == PunchZone.Face ? FaceCameraPivotOffset : BaseBodyPivot)
            + 0.03f * FinisherHoist
            - (_comboZone == PunchZone.Body
                ? 0.05f * BodyFold + 0.02f * BodyFatigue + 0.04f * BodyDangle - 0.015f * BodyLift
                : 0f);

        /// <summary>顔面パンチは「顔と拳」が同じ画に入るよう、腹パンと別のカメラにする。</summary>
        float ZoneDistanceScale => _comboZone == PunchZone.Face
            ? FaceCameraDistanceScale
            : (LaunchOptions.TryFloat("-heldpunchcamdist", out _)
                ? CameraDistanceScale
                : 0.27f);

        float ZonePitch => _comboZone == PunchZone.Face
            ? FaceCameraPitchOffset
            : (LaunchOptions.TryFloat("-heldpunchcampitch", out _)
                ? CameraPitchOffset
                : -4f);

        float ZoneYaw => _comboZone == PunchZone.Face
            ? FaceCameraYawOffset
            : (LaunchOptions.TryFloat("-heldpunchcamyaw", out _)
                ? CameraYawOffset
                : -32f);
        float FacingSign => _stats.transform.forward.x < -0.1f ? -1f : 1f;

        // 画面中心を合わせる横オフセット。寄るほど同じオフセットでも画面内のずれが
        // 大きくなるので、距離倍率に比例させる（基準は旧倍率 0.32 / 0.31 のときの値）。
        float FocusX => _stats.transform.position.x
            + FacingSign * (_comboZone == PunchZone.Face
                ? 0.30f * (FaceCameraDistanceScale / 0.31f)
                : 0.15f * (ZoneDistanceScale / 0.32f));

        /// <summary>当たった瞬間に少し寄り、腹パンでも後半の打数ほどじわっと寄る。</summary>
        float EffectiveDistanceScale =>
            ZoneDistanceScale * (1f - 0.04f * BodyFatigue - 0.05f * _impactPulse);

        /// <summary>
        /// 決め（グロッキー〜崩れ）の全身ショット。抱え上げで彼女が浮くので、
        /// 引いて注視点を下げないと足元が画から落ちる。
        /// </summary>
        float ShotDistanceScale => Mathf.Lerp(EffectiveDistanceScale, FinisherCameraDistanceScale, _finisherShotBlend);

        float ShotPivot => ComboCameraPivot + FinisherCameraPivotDelta * _finisherShotBlend;

        float ShotPitch => ZonePitch + FinisherCameraPitchDelta * _finisherShotBlend;

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
            _camera.PushDistance(ShotDistanceScale, hold);
            float pivot = ShotPivot;
            _camera.PushPivot(pivot, hold);

            float yaw = FocusYaw();
            _camera.PushFocus(FocusX, hold, ShotPitch, yaw);

            // ヒット時に押した値だけ出す（毎フレームの保持はログに出さない）
            if (hold >= 1.5f)
                GameLog.Log($"[HeldPunchCam] scale={ShotDistanceScale:F2} pivot={pivot:F2} " +
                          $"pitch={ShotPitch:F0} yaw={yaw:F0} " +
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

            _camera.HoldFocus(ShotDistanceScale, ShotPivot,
                FocusX, 0.5f, ShotPitch, FocusYaw());
        }

        /// <summary>左右どちらでも澪の正面側から撮る。</summary>
        float FocusYaw()
        {
            return ZoneYaw * FacingSign;
        }

        void ApplyCollapseCamera()
        {
            if (!ResolveCamera())
                return;

            // 拘束を解いて崩れ落ちる瞬間に、現在の寄りからゆっくり引いて全身の崩落を見届ける。
            _camera.HoldFocus(ShotDistanceScale, ShotPivot, FocusX, 2.4f, ShotPitch, FocusYaw());
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
