using UnityEngine;
using UnityEngine.Rendering;

namespace Ryona.Game
{
    /// <summary>
    /// 接地影（ブラブ）。
    ///
    /// 太陽の影だけだと、光が高いぶん手足の影が体から離れて「浮いて」見える。
    /// 体の真下に薄い影を敷いて、必ず足元（体の直下）が暗くなるようにする。
    ///
    /// 立っているときは足元の小さな楕円、倒れているときは体の形に沿った大きな楕円になる。
    /// </summary>
    [AddComponentMenu("Ryona/Contact Shadow")]
    public sealed class ContactShadow : MonoBehaviour
    {
        [Tooltip("影の濃さ（立っているとき）")]
        public float StandingAlpha = 0.46f;

        [Tooltip("影の濃さ（寝ているとき）")]
        public float LyingAlpha = 0.44f;

        [Tooltip("楕円の余白（メートル）")]
        public float Padding = 0.16f;

        [Tooltip("床からの高さ。0 だと床とチラつく")]
        public float GroundY = 0.014f;

        [Tooltip("これより高い骨は範囲の計算に入れない（立っているとき足元だけにする）")]
        public float LowBoneHeight = 0.40f;

        [Tooltip("姿勢の変化に影が追従するまでの時間（秒）")]
        public float FollowSeconds = 0.10f;

        public CharacterAnimator Character;

        Transform _quad;
        Material _material;

        float _cx, _cz, _rx, _rz, _alpha;
        bool _primed;

        static readonly int ColorId = Shader.PropertyToID("_Color");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var go = new GameObject("Contact Shadow");
            go.AddComponent<ContactShadow>();
        }

        void Awake()
        {
            Build();
        }

        void OnDestroy()
        {
            if (_material != null)
                Destroy(_material);
        }

        void Build()
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                Debug.LogWarning("[Blob] shader not found; contact shadow disabled");
                enabled = false;
                return;
            }

            _material = new Material(shader) { name = "ContactShadow" };
            _material.mainTexture = BuildRadialTexture(64);
            _material.renderQueue = 3000;

            var quad = new GameObject("Blob");
            quad.transform.SetParent(transform, false);

            var filter = quad.AddComponent<MeshFilter>();
            filter.sharedMesh = BuildQuadMesh();

            var renderer = quad.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

            _quad = quad.transform;
        }

        void LateUpdate()
        {
            if (_quad == null)
                return;

            if (Character == null)
                Character = FindFirstObjectByType<CharacterAnimator>();

            if (Character == null || Character.Rig == null || !Character.Rig.Ready)
            {
                _quad.gameObject.SetActive(false);
                return;
            }

            ProceduralRig rig = Character.Rig;

            Vector3[] points =
            {
                rig.Hips.position, rig.Head.position,
                rig.LHand.position, rig.RHand.position,
                rig.LFoot.position, rig.RFoot.position
            };

            // 床に近い骨だけで範囲を決める。
            // 立っているときは足元だけ、倒れているときは全身が入る。
            float sumX = 0f, sumZ = 0f;
            int count = 0;
            for (int i = 0; i < points.Length; i++)
            {
                if (points[i].y > LowBoneHeight)
                    continue;
                sumX += points[i].x;
                sumZ += points[i].z;
                count++;
            }

            if (count == 0)
            {
                sumX = rig.LFoot.position.x;
                sumZ = rig.LFoot.position.z;
                count = 1;
            }

            float cx = sumX / count;
            float cz = sumZ / count;

            float rx = 0f, rz = 0f, highest = 0f;
            for (int i = 0; i < points.Length; i++)
            {
                if (points[i].y > LowBoneHeight)
                    continue;

                rx = Mathf.Max(rx, Mathf.Abs(points[i].x - cx));
                rz = Mathf.Max(rz, Mathf.Abs(points[i].z - cz));
                highest = Mathf.Max(highest, points[i].y);
            }

            rx += Padding;
            rz += Padding;

            // 低いほど濃い（寝ているときは広くて薄い）
            float t = Mathf.Clamp01(highest / LowBoneHeight);
            float alpha = Mathf.Lerp(LyingAlpha, StandingAlpha, t);

            // 姿勢が切り替わった瞬間に楕円が飛ばないよう、少し遅らせて追従させる
            if (!_primed)
            {
                _cx = cx;
                _cz = cz;
                _rx = rx;
                _rz = rz;
                _alpha = alpha;
                _primed = true;
            }
            else
            {
                float k = 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(0.001f, FollowSeconds));
                _cx = Mathf.Lerp(_cx, cx, k);
                _cz = Mathf.Lerp(_cz, cz, k);
                _rx = Mathf.Lerp(_rx, rx, k);
                _rz = Mathf.Lerp(_rz, rz, k);
                _alpha = Mathf.Lerp(_alpha, alpha, k);
            }

            _quad.gameObject.SetActive(true);
            _quad.position = new Vector3(_cx, GroundY, _cz);
            _quad.rotation = Quaternion.identity;
            _quad.localScale = new Vector3(_rx * 2f, 1f, _rz * 2f);

            _material.SetColor(ColorId, new Color(0f, 0f, 0f, _alpha));
        }

        static Mesh BuildQuadMesh()
        {
            var mesh = new Mesh { name = "Blob Quad" };
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, 0f, -0.5f),
                new Vector3(0.5f, 0f, -0.5f),
                new Vector3(0.5f, 0f, 0.5f),
                new Vector3(-0.5f, 0f, 0.5f)
            };
            mesh.uv = new[]
            {
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f)
            };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateNormals();
            return mesh;
        }

        static Texture2D BuildRadialTexture(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "Blob Radial",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            var pixels = new Color32[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size * 2f - 1f;
                    float v = (y + 0.5f) / size * 2f - 1f;
                    float r = Mathf.Sqrt(u * u + v * v);

                    // 中心は濃く、外へ向かってなだらかに抜く。
                    // 寝ているときは体の下（中心）が体で隠れるので、
                    // 中心から 45% までは濃さを保ち、外側だけをぼかす。
                    float a = Mathf.Clamp01((1f - r) / 0.55f);
                    a *= a;

                    pixels[y * size + x] = new Color(0f, 0f, 0f, a);
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, false);
            return tex;
        }
    }
}
