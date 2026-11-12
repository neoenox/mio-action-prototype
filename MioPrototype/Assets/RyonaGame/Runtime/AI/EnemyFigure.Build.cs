using UnityEngine;
using System.Collections.Generic;
using AttackId = Ryona.Game.EnemyBrain.AttackId;

namespace Ryona.Game
{
    public sealed partial class EnemyFigure : MonoBehaviour
    {
        void Build()
        {
            if (_root != null)
                return;
            // --- アンドロイド風の質感 ------------------------------------
            // 見た目だけをロボットに寄せる。プリミティブなので骨格・判定・モーションは
            // これまでと同じ（白い装甲・黒いインテーク・青い発光の 3 材質で組む）。
            var source = ClothMaterial != null ? ClothMaterial : BodyMaterial;
            var armor = Solid(source, new Color(0.84f, 0.86f, 0.90f));
            var joint = Solid(source, new Color(0.055f, 0.060f, 0.070f));
            var visor = Solid(source, new Color(0.012f, 0.015f, 0.022f));
            var glow = Solid(source, new Color(0.02f, 0.10f, 0.16f));
            SetSurface(armor, 0.10f, 0.80f);   // 白いセラミック装甲
            SetSurface(joint, 0.30f, 0.50f);   // 黒いインナー・関節
            SetSurface(visor, 0.60f, 0.92f);   // 黒いバイザー（鏡面寄り）
            SetGlow(glow, new Color(0.20f, 0.70f, 1.00f) * 2.6f);
            var skin = armor;
            var cloth = armor;
            var pants = armor;
            var trim = joint;
            var dark = joint;
            var hair = armor;
            var eye = glow;
            float h = Height;
            float hipsY = h * 0.515f;      // 腰の高さ（ここが回転の中心）
            float chestY = h * 0.080f;     // 腰から上体の付け根まで
            float headY = h * 0.255f;      // 胸から首の付け根まで
            float shoulderY = h * 0.200f;
            float shoulderX = h * 0.115f;
            float upperArm = h * 0.175f;
            float lowerArm = h * 0.165f;
            float hipX = h * 0.062f;
            float thigh = h * 0.235f;
            float shin = h * 0.245f;
            _root = Child("Root", transform, Vector3.zero);
            // --- 腰 -----------------------------------------------------
            // 箱ではなく楕円体で組む。角が立つと「プリミティブ」に見える。
            _hips = Child("Hips", _root, new Vector3(0f, hipsY, 0f));
            CapsuleShaped("Pelvis", _hips, new Vector3(0f, -h * 0.002f, 0f),
                h * 0.123f, h * 0.087f, h * 0.064f, pants);
            CapsuleShaped("Belt", _hips, new Vector3(0f, h * 0.024f, 0f),
                h * 0.029f, h * 0.080f, h * 0.062f, dark);
            CapsuleShaped("Waist", _hips, new Vector3(0f, h * 0.058f, 0f),
                h * 0.086f, h * 0.077f, h * 0.060f, pants);
            // --- 胴 -----------------------------------------------------
            // 側面がまっすぐなカプセルで組む。球だと腹だけが膨らんで「壺」に見える。
            _chest = Child("Chest", _hips, new Vector3(0f, chestY, 0f));
            CapsuleShaped("Torso", _chest, new Vector3(0f, h * 0.088f, 0f),
                h * 0.246f, h * 0.106f, h * 0.063f, cloth);
            CapsuleShaped("ChestTop", _chest, new Vector3(0f, h * 0.180f, -h * 0.002f),
                h * 0.077f, h * 0.082f, h * 0.058f, cloth);
            CapsuleShaped("Collar", _chest, new Vector3(0f, h * 0.200f, h * 0.006f),
                h * 0.034f, h * 0.049f, h * 0.049f, trim);
            // --- 頭 -----------------------------------------------------
            _head = Child("Head", _chest, new Vector3(0f, headY, 0f));
            Capsule("Neck", _head, new Vector3(0f, -h * 0.027f, 0f), h * 0.085f, h * 0.028f, joint);
            Blob("Skull", _head, new Vector3(0f, h * 0.071f, 0f),
                new Vector3(h * 0.114f, h * 0.140f, h * 0.126f), skin);
            Blob("Jaw", _head, new Vector3(0f, h * 0.047f, h * 0.016f),
                new Vector3(h * 0.089f, h * 0.077f, h * 0.100f), skin);
            // 髪は頭頂まで被せる。上に載せないと「後ろに黒い塊がある禿げ」に見える。
            // 顔の高さでは楕円が細るので、前に出しても目は隠れない。
            Blob("Hair", _head, new Vector3(0f, h * 0.086f, -h * 0.011f),
                new Vector3(h * 0.128f, h * 0.123f, h * 0.143f), hair);
            Blob("HairBack", _head, new Vector3(0f, h * 0.057f, -h * 0.049f),
                new Vector3(h * 0.109f, h * 0.099f, h * 0.075f), hair);
            Blob("EyeL", _head, new Vector3(-h * 0.025f, h * 0.064f, h * 0.055f),
                Vector3.one * (h * 0.022f), eye);
            Blob("EyeR", _head, new Vector3(h * 0.025f, h * 0.064f, h * 0.055f),
                Vector3.one * (h * 0.022f), eye);
            // 眉と口が無いと「目玉の付いた球」に見える。この 4 つで一気に顔になる。
            // 眉は目から少し離す。近いと 1 本の帯に見えてサングラスになる。
            Blob("BrowL", _head, new Vector3(-h * 0.026f, h * 0.079f, h * 0.054f),
                new Vector3(h * 0.028f, h * 0.006f, h * 0.012f), visor);
            Blob("BrowR", _head, new Vector3(h * 0.026f, h * 0.079f, h * 0.054f),
                new Vector3(h * 0.028f, h * 0.006f, h * 0.012f), visor);
            Blob("Mouth", _head, new Vector3(0f, h * 0.040f, h * 0.057f),
                new Vector3(h * 0.030f, h * 0.007f, h * 0.012f), visor);
            Blob("Nose", _head, new Vector3(0f, h * 0.055f, h * 0.060f),
                new Vector3(h * 0.020f, h * 0.020f, h * 0.022f), skin);
            _shoulderRRest = new Vector3(shoulderX, shoulderY, 0f);
            _shoulderR = Child("ShoulderR", _chest, _shoulderRRest);
            _shoulderLRest = new Vector3(-shoulderX, shoulderY, 0f);
            _shoulderL = Child("ShoulderL", _chest, _shoulderLRest);
            _elbowR = Arm(_shoulderR, upperArm, lowerArm, h, armor, joint, true);
            _elbowL = Arm(_shoulderL, upperArm, lowerArm, h, armor, joint, false);
            // 素手で戦う。予備動作は拳の色で伝える。拳は黒いグローブ。
            _weaponSteel = joint != null ? Solid(joint, joint.color) : null;
            if (_weaponSteel != null)
            {
                SetSurface(_weaponSteel, 0.30f, 0.55f);
                _weaponBase = _weaponSteel.color;
                _weaponSteel.EnableKeyword("_EMISSION");
                _weaponSteel.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                _weaponSteel.SetColor(EmissionId, Color.black);
                _fistR.GetComponent<Renderer>().sharedMaterial = _weaponSteel;
                _fistL.GetComponent<Renderer>().sharedMaterial = _weaponSteel;
            }
            _hipR = Child("HipR", _hips, new Vector3(hipX, -h * 0.030f, 0f));
            _hipL = Child("HipL", _hips, new Vector3(-hipX, -h * 0.030f, 0f));
            _kneeR = Leg(_hipR, thigh, shin, h, armor, joint);
            _kneeL = Leg(_hipL, thigh, shin, h, armor, joint);
            _ankleR = _kneeR.Find("Ankle");
            _ankleL = _kneeL.Find("Ankle");
            _footR = _ankleR.Find("Foot");
            _footL = _ankleL.Find("Foot");

            // 装甲・発光パーツ
            AddAndroidPlates(h, armor, joint, visor, glow);

            _lastPosition = transform.position;
        }
        Transform Arm(Transform shoulder, float upperLen, float lowerLen, float h, Material limb, Material joint,
            bool right)
        {
            // 肩の丸み。ここが無いと腕が胴から生えているように見える。
            Blob("Deltoid", shoulder, new Vector3(0f, 0f, 0f),
                new Vector3(h * 0.074f, h * 0.072f, h * 0.072f), limb);
            Capsule("UpperArm", shoulder, new Vector3(0f, -upperLen * 0.5f, 0f),
                upperLen + h * 0.015f, h * 0.030f, limb);
            var elbow = Child("Elbow", shoulder, new Vector3(0f, -upperLen, 0f));
            // 関節は黒。白い装甲の切れ目に見える。
            Blob("ElbowJoint", elbow, Vector3.zero,
                new Vector3(h * 0.056f, h * 0.056f, h * 0.056f), joint);
            // 前腕と手を分ける。長さの合計は変えず、関節の位置だけ分ける。
            // 手首が無いと拳が前腕と一直線のままになり「棒の先に塊」に見える。
            float handLen = lowerLen * 0.24f;
            float foreLen = lowerLen - handLen;
            Capsule("Forearm", elbow, new Vector3(0f, -foreLen * 0.5f, 0f),
                foreLen + h * 0.008f, h * 0.025f, limb);
            // 手首。ここを回すと拳の向きが変わる。
            var wrist = Child("Wrist", elbow, new Vector3(0f, -foreLen, 0f));
            // 関節の穴埋め。前腕の先と拳の間に隙間が空くと腕が千切れて見える。
            Blob("WristJoint", wrist, Vector3.zero,
                new Vector3(h * 0.042f, h * 0.042f, h * 0.042f), joint);
            // 拳は球ではなく握った形（縦長）に。球だと手に見えない。
            Blob("Fist", wrist, new Vector3(0f, -(handLen + h * 0.005f), h * 0.003f),
                new Vector3(h * 0.049f, h * 0.057f, h * 0.051f), joint);
            if (right)
            {
                _wristR = wrist;
                _fistR = wrist.Find("Fist");
            }
            else
            {
                _wristL = wrist;
                _fistL = wrist.Find("Fist");
            }
            return elbow;
        }

        /// <summary>
        /// 右手に持たせる鉄パイプ。素手より「人」に見えるうえ、
        /// 「振りかぶる → 振り抜く」という攻撃の意味が通るようになる。
        /// </summary>
        Transform BuildWeapon(Transform wrist, float lowerLen, float h, Material cloth)
        {
            var steel = Solid(cloth, new Color(0.56f, 0.58f, 0.62f));
            var grip = Tint(cloth, 0.26f);
            float len = h * 0.30f;
            float r = h * 0.017f;
            // 握る位置は拳の中心（手首から手の長さぶん先）
            float handLen = lowerLen * 0.24f;
            var pivot = Child("Weapon", wrist,
                new Vector3(0f, -(handLen + h * 0.004f), h * 0.004f));
            // 前腕の延長のままだと真下を向いて床に刺さる。前へ傾けて「提げている」形にする。
            // さらに外側へ振る。真下だと脚の線と重なって「足に刺さっている」ように見える。
            pivot.localRotation = Quaternion.Euler(-55f, 0f, 16f);
            // 握りから先へ伸びる。手の後ろにも少しだけ出す（握りしろが見える）
            CapsuleShaped("Shaft", pivot, new Vector3(0f, -len * 0.5f + h * 0.030f, 0f),
                len, r, r, steel);
            CapsuleShaped("Grip", pivot, new Vector3(0f, -h * 0.002f, 0f),
                h * 0.070f, r * 1.05f, r * 1.05f, grip);
            // 予備動作でこの材質だけ光らせる
            if (steel != null)
            {
                steel.EnableKeyword("_EMISSION");
                steel.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                steel.SetColor(EmissionId, Color.black);
                _weaponSteel = steel;
                _weaponBase = steel.color;
            }
            return pivot;
        }
        Transform Leg(Transform hip, float thighLen, float shinLen, float h, Material limb, Material joint)
        {
            Capsule("Thigh", hip, new Vector3(0f, -thighLen * 0.5f, 0f),
                thighLen + h * 0.030f, h * 0.050f, limb);
            var knee = Child("Knee", hip, new Vector3(0f, -thighLen, 0f));
            Blob("KneeJoint", knee, Vector3.zero,
                new Vector3(h * 0.078f, h * 0.078f, h * 0.078f), joint);
            Capsule("Shin", knee, new Vector3(0f, -shinLen * 0.5f, 0f),
                shinLen + h * 0.020f, h * 0.036f, limb);
            // IK の終点。足首の位置に置き、靴はここにぶら下げる
            var ankle = Child("Ankle", knee, new Vector3(0f, -shinLen, 0f));
            Blob("Foot", ankle, new Vector3(0f, h * 0.012f, h * 0.030f),
                new Vector3(h * 0.060f, h * 0.036f, h * 0.145f), joint);
            return knee;
        }

        // --- アンドロイドの装甲・発光パーツ -----------------------------
        /// <summary>
        /// 白い装甲・黒いバイザー・青い発光を足して、プリミティブのままロボットに見せる。
        /// 追加するのは見た目だけ（Transform は既存の骨にぶら下げるので姿勢・判定は不変）。
        /// </summary>
        void AddAndroidPlates(float h, Material armor, Material joint, Material visor, Material glow)
        {
            // 胸の装甲とコア
            Blob("ChestPlate", _chest, new Vector3(0f, h * 0.118f, h * 0.026f),
                new Vector3(h * 0.162f, h * 0.136f, h * 0.082f), armor);
            Blob("ChestCoreRim", _chest, new Vector3(0f, h * 0.118f, h * 0.062f),
                new Vector3(h * 0.058f, h * 0.058f, h * 0.030f), joint);
            Blob("ChestCore", _chest, new Vector3(0f, h * 0.118f, h * 0.076f),
                Vector3.one * (h * 0.030f), glow);
            // 鎖骨のダクト（光る差し色）
            Box("DuctL", _chest, new Vector3(-h * 0.058f, h * 0.154f, h * 0.058f),
                new Vector3(h * 0.040f, h * 0.010f, h * 0.014f), glow);
            Box("DuctR", _chest, new Vector3(h * 0.058f, h * 0.154f, h * 0.058f),
                new Vector3(h * 0.040f, h * 0.010f, h * 0.014f), glow);
            // 腹の装甲帯
            Blob("AbPlate", _hips, new Vector3(0f, h * 0.052f, h * 0.036f),
                new Vector3(h * 0.112f, h * 0.072f, h * 0.062f), armor);
            // 腰のサイドアーマー
            Blob("HipPlateR", _hips, new Vector3(h * 0.072f, h * 0.010f, 0f),
                new Vector3(h * 0.050f, h * 0.075f, h * 0.090f), armor);
            Blob("HipPlateL", _hips, new Vector3(-h * 0.072f, h * 0.010f, 0f),
                new Vector3(h * 0.050f, h * 0.075f, h * 0.090f), armor);
            // 肩のパッド
            Blob("PauldronR", _shoulderR, new Vector3(h * 0.008f, h * 0.014f, 0f),
                new Vector3(h * 0.110f, h * 0.086f, h * 0.108f), armor);
            Blob("PauldronL", _shoulderL, new Vector3(-h * 0.008f, h * 0.014f, 0f),
                new Vector3(h * 0.110f, h * 0.086f, h * 0.108f), armor);
            Blob("PauldronGlowR", _shoulderR, new Vector3(h * 0.054f, h * 0.018f, 0f),
                new Vector3(h * 0.014f, h * 0.022f, h * 0.050f), glow);
            Blob("PauldronGlowL", _shoulderL, new Vector3(-h * 0.054f, h * 0.018f, 0f),
                new Vector3(h * 0.014f, h * 0.022f, h * 0.050f), glow);
            // 肘・膝当て
            Blob("ElbowCapR", _elbowR, new Vector3(0f, 0f, h * 0.030f),
                new Vector3(h * 0.070f, h * 0.066f, h * 0.052f), armor);
            Blob("ElbowCapL", _elbowL, new Vector3(0f, 0f, h * 0.030f),
                new Vector3(h * 0.070f, h * 0.066f, h * 0.052f), armor);
            Blob("KneeCapR", _kneeR, new Vector3(0f, h * 0.004f, h * 0.046f),
                new Vector3(h * 0.086f, h * 0.082f, h * 0.058f), armor);
            Blob("KneeCapL", _kneeL, new Vector3(0f, h * 0.004f, h * 0.046f),
                new Vector3(h * 0.086f, h * 0.082f, h * 0.058f), armor);
            // すねの装甲
            Blob("ShinPlateR", _kneeR, new Vector3(0f, -h * 0.120f, h * 0.034f),
                new Vector3(h * 0.070f, h * 0.150f, h * 0.050f), armor);
            Blob("ShinPlateL", _kneeL, new Vector3(0f, -h * 0.120f, h * 0.034f),
                new Vector3(h * 0.070f, h * 0.150f, h * 0.050f), armor);
            // 頭部: 黒いバイザー＋光る目＋ヘルメットのトサカ
            Blob("Visor", _head, new Vector3(0f, h * 0.064f, h * 0.052f),
                new Vector3(h * 0.118f, h * 0.058f, h * 0.052f), visor);
            Blob("EyeGlowL", _head, new Vector3(-h * 0.029f, h * 0.067f, h * 0.080f),
                new Vector3(h * 0.044f, h * 0.016f, h * 0.012f), glow);
            Blob("EyeGlowR", _head, new Vector3(h * 0.029f, h * 0.067f, h * 0.080f),
                new Vector3(h * 0.044f, h * 0.016f, h * 0.012f), glow);
            // ヘルメットのトサカ（白いフィン＋青いライン）
            Blob("Crest", _head, new Vector3(0f, h * 0.150f, -h * 0.012f),
                new Vector3(h * 0.018f, h * 0.086f, h * 0.062f), armor);
            Blob("CrestGlow", _head, new Vector3(0f, h * 0.158f, h * 0.006f),
                new Vector3(h * 0.010f, h * 0.040f, h * 0.022f), glow);
            // 耳のポッド。横から見ても「眼」に見えないよう薄い円盤＋発光リングにする。
            Blob("EarPodL", _head, new Vector3(-h * 0.060f, h * 0.058f, 0f),
                new Vector3(h * 0.026f, h * 0.050f, h * 0.052f), joint);
            Blob("EarPodR", _head, new Vector3(h * 0.060f, h * 0.058f, 0f),
                new Vector3(h * 0.026f, h * 0.050f, h * 0.052f), joint);
            Blob("EarGlowL", _head, new Vector3(-h * 0.070f, h * 0.058f, 0f),
                new Vector3(h * 0.010f, h * 0.034f, h * 0.038f), glow);
            Blob("EarGlowR", _head, new Vector3(h * 0.070f, h * 0.058f, 0f),
                new Vector3(h * 0.010f, h * 0.034f, h * 0.038f), glow);
            // 頸部のリング（横から見える発光の首輪）
            Blob("CollarGlow", _chest, new Vector3(0f, h * 0.228f, 0f),
                new Vector3(h * 0.094f, h * 0.016f, h * 0.094f), glow);
            // 背骨のライン（横から見える）
            Blob("SpineGlow", _chest, new Vector3(0f, h * 0.060f, -h * 0.062f),
                new Vector3(h * 0.014f, h * 0.150f, h * 0.014f), glow);
            // 腕・脚の外側ライン
            Box("UpperArmGlowR", _shoulderR, new Vector3(h * 0.040f, -h * 0.060f, 0f),
                new Vector3(h * 0.008f, h * 0.070f, h * 0.020f), glow);
            Box("UpperArmGlowL", _shoulderL, new Vector3(-h * 0.040f, -h * 0.060f, 0f),
                new Vector3(h * 0.008f, h * 0.070f, h * 0.020f), glow);
            Box("ForearmGlowR", _elbowR, new Vector3(h * 0.030f, -h * 0.060f, 0f),
                new Vector3(h * 0.007f, h * 0.060f, h * 0.016f), glow);
            Box("ForearmGlowL", _elbowL, new Vector3(-h * 0.030f, -h * 0.060f, 0f),
                new Vector3(h * 0.007f, h * 0.060f, h * 0.016f), glow);
            Box("HipGlowR", _hips, new Vector3(h * 0.080f, h * 0.020f, 0f),
                new Vector3(h * 0.008f, h * 0.040f, h * 0.030f), glow);
            Box("HipGlowL", _hips, new Vector3(-h * 0.080f, h * 0.020f, 0f),
                new Vector3(h * 0.008f, h * 0.040f, h * 0.030f), glow);
            Box("ShinGlowR", _kneeR, new Vector3(h * 0.036f, -h * 0.120f, 0f),
                new Vector3(h * 0.007f, h * 0.080f, h * 0.016f), glow);
            Box("ShinGlowL", _kneeL, new Vector3(-h * 0.036f, -h * 0.120f, 0f),
                new Vector3(h * 0.007f, h * 0.080f, h * 0.016f), glow);
            // 口元の装甲（顎を白く覆う）
            Blob("ChinPlate", _head, new Vector3(0f, h * 0.034f, h * 0.050f),
                new Vector3(h * 0.070f, h * 0.036f, h * 0.050f), armor);
        }

        /// <summary>Standard シェーダの金属感・光沢を設定する（URP でも名前が同じ）。</summary>
        static void SetSurface(Material mat, float metallic, float smoothness)
        {
            if (mat == null)
                return;
            if (mat.HasProperty(MetallicId))
                mat.SetFloat(MetallicId, metallic);
            if (mat.HasProperty(GlossinessId))
                mat.SetFloat(GlossinessId, smoothness);
        }

        /// <summary>発光（HDR カラー）を設定する。</summary>
        static void SetGlow(Material mat, Color emission)
        {
            if (mat == null)
                return;
            mat.EnableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            mat.SetColor(EmissionId, emission);
        }

        static Transform Child(string name, Transform parent, Vector3 localPos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            return go.transform;
        }

        /// <summary>服の色を暗くした派生マテリアル。髪・ベルト・靴を同じ系統でまとめる。</summary>
        Material Tint(Material source, float mul)
        {
            if (source == null)
                return null;
            var mat = new Material(source)
            {
                name = source.name + "_tint",
                color = new Color(source.color.r * mul, source.color.g * mul, source.color.b * mul,
                    source.color.a)
            };
            _ownedMaterials.Add(mat);
            return mat;
        }

        /// <summary>色をそのまま指定して作る派生マテリアル。金属など、服と別の質感を出したいとき用。</summary>
        Material Solid(Material source, Color color)
        {
            if (source == null)
                return null;
            var mat = new Material(source) { name = source.name + "_solid", color = color };
            _ownedMaterials.Add(mat);
            return mat;
        }
        void OnDestroy()
        {
            for (int i = 0; i < _ownedMaterials.Count; i++)
            {
                if (_ownedMaterials[i] == null)
                    continue;
                if (Application.isPlaying)
                    Destroy(_ownedMaterials[i]);
                else
                    DestroyImmediate(_ownedMaterials[i]);
            }
            _ownedMaterials.Clear();
        }

    }
}
