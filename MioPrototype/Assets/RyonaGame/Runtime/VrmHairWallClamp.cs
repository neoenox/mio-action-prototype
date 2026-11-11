using UnityEngine;
using UniVRM10;

namespace Ryona.Game
{
    /// <summary>
    /// 壁押さえつけ中、壁の内側（+Z）へ出た髪を壁の表面まで押し戻す。
    ///
    /// VRM のスプリングボーンは物理なので、壁を意識しない。壁に背中を付けると
    /// 後頭部の髪が壁を突き抜ける（実測: 108 本中 43 本が壁の内側、最深 +0.15m）。
    ///
    /// 実行順は **姿勢の適用（<see cref="CharacterAnimator"/> = 20000）より後** にする。
    /// 髪のジョイントは頭の子なので、先に押し戻しても頭を回した時点で一緒に壁へ入り直す
    /// （実測: 12000 で押し戻すと 0 本になるが、頭の回転で 11 本・最深 0.054m が戻ってきた）。
    /// 髪は物理の結果を「壁の手前へ」clamp するだけで、揺れ自体は止めない。
    /// </summary>
    [AddComponentMenu("Ryona/VRM Hair Wall Clamp")]
    [DefaultExecutionOrder(26000)]
    public sealed class VrmHairWallClamp : MonoBehaviour
    {
        /// <summary>
        /// 壁の表面よりこれだけ手前で止める（m）。
        /// ジョイントの位置で止めても、毛束のメッシュは関節より外へ張り出す
        /// （実測: ジョイントは全部手前なのに Hair メッシュだけ +0.006m 越えた）。
        /// その張り出しぶんを余裕として持つ。
        /// </summary>
        public float Margin = 0.10f;

        PlayerGrapple _grapple;
        VRM10SpringBoneJoint[] _joints;
        Transform[] _parents;
        CharacterAnimator _anim;

        void Awake()
        {
            _anim = GetComponent<CharacterAnimator>();
        }

        void LateUpdate()
        {
            if (_grapple == null)
                _grapple = FindFirstObjectByType<PlayerGrapple>();
            if (_grapple == null || !_grapple.HasWallFace)
                return;

            if (_joints == null || _joints.Length == 0)
                Cache();
            if (_joints == null || _joints.Length == 0)
                return;

            float limit = _grapple.WallFaceZ - Margin;
            float weight = Mathf.Clamp01(_grapple.WallPinWeight);
            if (weight <= 0f)
                return;

            for (int i = 0; i < _joints.Length; i++)
            {
                var joint = _joints[i];
                if (joint == null)
                    continue;

                Vector3 pos = joint.transform.position;
                if (pos.z <= limit)
                    continue;

                // 押し戻す量は重みで薄める（押し出しの途中で髪だけ先に止まらないように）。
                pos.z = Mathf.Lerp(pos.z, limit, weight);
                joint.transform.position = pos;
            }

            // 押し戻した後の最深値（-walllog 用）。ここが正なら clamp が効いていない。
            if (_log)
            {
                float deepest = 0f;
                int over = 0;
                for (int i = 0; i < _joints.Length; i++)
                {
                    if (_joints[i] == null)
                        continue;
                    float d = _joints[i].transform.position.z - limit;
                    if (d <= 0f)
                        continue;
                    over++;
                    if (d > deepest) deepest = d;
                }

                if (Time.time - _logAt > 1f)
                {
                    _logAt = Time.time;
                    Debug.Log($"[HairWall] 押し戻し後 over={over}/{_joints.Length} deepest={deepest:F3}");
                }
            }
        }

        float _logAt;
        bool _log;

        void Start()
        {
            foreach (string a in System.Environment.GetCommandLineArgs())
            {
                if (string.Equals(a, "-walllog", System.StringComparison.OrdinalIgnoreCase))
                    _log = true;
            }
        }

        void Cache()
        {
            _joints = GetComponentsInChildren<VRM10SpringBoneJoint>(true);
            _parents = new Transform[_joints.Length];
            for (int i = 0; i < _joints.Length; i++)
                _parents[i] = _joints[i] != null ? _joints[i].transform.parent : null;

            int n = 0;
            for (int i = 0; i < _joints.Length; i++)
                if (_joints[i] != null && _parents[i] != null) n++;

            Debug.Log($"[HairWall] 髪のジョイント {n}/{_joints.Length} を壁で押し戻す");
        }
    }
}
