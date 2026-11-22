using UnityEngine;

namespace Ryona.Game
{
    /// <summary>両手首を背中側でつなぐ光る輪。戦闘の道具として短時間だけ見せる。</summary>
    public sealed class WristLockVisual : MonoBehaviour
    {
        PlayerWristLock _lock;
        CharacterAnimator _anim;
        Transform _ringL;
        Transform _ringR;
        Transform _bar;
        Material _material;

        void Awake()
        {
            _lock = GetComponent<PlayerWristLock>();
            _anim = GetComponentInChildren<CharacterAnimator>();
            _material = new Material(Shader.Find("Standard"))
            {
                color = new Color(0.25f, 0.85f, 1f)
            };
            _material.SetColor("_EmissionColor", new Color(0.15f, 0.65f, 1f) * 2f);
            _ringL = Ring("Wrist Ring L");
            _ringR = Ring("Wrist Ring R");
            _bar = GameObject.CreatePrimitive(PrimitiveType.Cylinder).transform;
            _bar.name = "Wrist Link";
            _bar.SetParent(transform, false);
            _bar.GetComponent<Renderer>().sharedMaterial = _material;
            Destroy(_bar.GetComponent<Collider>());
            SetVisible(false);
        }

        Transform Ring(string name)
        {
            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder).transform;
            ring.name = name;
            ring.SetParent(transform, false);
            ring.GetComponent<Renderer>().sharedMaterial = _material;
            Destroy(ring.GetComponent<Collider>());
            return ring;
        }

        void LateUpdate()
        {
            bool show = _lock != null && _lock.IsLocked && _anim != null && _anim.Rig != null && _anim.Rig.Ready;
            SetVisible(show);
            if (!show)
                return;

            var l = _anim.Rig.LHand;
            var r = _anim.Rig.RHand;
            _ringL.position = l.position;
            _ringR.position = r.position;
            _ringL.rotation = l.rotation;
            _ringR.rotation = r.rotation;
            _ringL.localScale = new Vector3(0.16f, 0.03f, 0.16f);
            _ringR.localScale = _ringL.localScale;

            Vector3 mid = (l.position + r.position) * 0.5f;
            Vector3 delta = r.position - l.position;
            _bar.position = mid;
            _bar.rotation = Quaternion.FromToRotation(Vector3.up, delta.normalized);
            _bar.localScale = new Vector3(0.02f, delta.magnitude * 0.5f, 0.02f);
        }

        void OnDestroy()
        {
            if (_material != null)
                Destroy(_material);
        }

        void SetVisible(bool visible)
        {
            if (_ringL != null) _ringL.gameObject.SetActive(visible);
            if (_ringR != null) _ringR.gameObject.SetActive(visible);
            if (_bar != null) _bar.gameObject.SetActive(visible);
        }
    }
}
