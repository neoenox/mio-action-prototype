using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Ryona.Game
{
    /// <summary>
    /// 壁押さえつけに使う壁の場所。ベルトは 1 本道なので、レーンの外（+Z）に
    /// 壁を数か所だけ置き、その近くで正面の押さえつけが成立したときだけ
    /// 「壁へ押し出す」演出に切り替える。
    /// </summary>
    [AddComponentMenu("Ryona/Wall Pin Site")]
    public sealed class WallPinSite : MonoBehaviour
    {
        [Tooltip("この壁の前に立てる X の範囲（中心から ± この値）")]
        public float HalfWidth = 1.25f;

        [Tooltip("壁の表面 Z。彼女はこの手前に押し付けられる")]
        public float FaceZ = 1.78f;

        public float CenterX => transform.position.x;
        internal Transform VisualRoot;
        public float RiseProgress { get; private set; } = 1f;
        float _retractAfter = -1f;

        internal void SetRise(float progress)
        {
            RiseProgress = Mathf.Clamp01(progress);
            if (VisualRoot != null)
                VisualRoot.localPosition = Vector3.down * (2.5f * (1f - RiseProgress));
        }

        internal void BeginRise()
        {
            _retractAfter = -1f;
            SetRise(0f);
        }

        internal void RetractAfterRelease() => _retractAfter = 0.3f;

        void Update()
        {
            if (_retractAfter < 0f)
                return;
            _retractAfter -= Time.deltaTime;
            if (_retractAfter > 0f)
                return;
            SetRise(Mathf.MoveTowards(RiseProgress, 0f, Time.deltaTime * 2f));
            _retractAfter = RiseProgress > 0f ? 0f : -1f;
        }


        void OnEnable() => WallPinSites.Register(this);
        void OnDisable() => WallPinSites.Unregister(this);
    }

    /// <summary>壁の場所の登録。毎フレームの探索を避けるため、有効なものだけを持つ。</summary>
    public static class WallPinSites
    {
        static readonly List<WallPinSite> Sites = new List<WallPinSite>();

        public static void Register(WallPinSite site)
        {
            if (site != null && !Sites.Contains(site))
                Sites.Add(site);
        }

        public static void Unregister(WallPinSite site)
        {
            if (site != null)
                Sites.Remove(site);
        }

        /// <summary>
        /// その位置の近くにある壁を返す。X が壁の幅に入っていて、
        /// まだ壁の手前（レーン側）にいるときだけ。
        /// </summary>
        public static WallPinSite Find(Vector3 position)
        {
            WallPinSite best = null;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < Sites.Count; i++)
            {
                var site = Sites[i];
                if (site == null || !site.isActiveAndEnabled)
                    continue;

                if (position.z >= site.FaceZ - 0.35f)
                    continue;

                float dx = Mathf.Abs(position.x - site.CenterX);
                if (dx > site.HalfWidth || dx >= bestDistance)
                    continue;

                bestDistance = dx;
                best = site;
            }

            return best;
        }

        /// <summary>いちばん近い壁（テストと -wallpintest 用）。幅の外でも返す。</summary>
        public static WallPinSite Nearest(Vector3 position)
        {
            WallPinSite best = null;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < Sites.Count; i++)
            {
                var site = Sites[i];
                if (site == null || !site.isActiveAndEnabled)
                    continue;

                float dx = Mathf.Abs(position.x - site.CenterX);
                if (dx >= bestDistance)
                    continue;

                bestDistance = dx;
                best = site;
            }

            return best;
        }
    }

    /// <summary>
    /// 壁の見た目。シーンに壁が無いときだけ実行時に作る（シーンを作り直さなくても遊べる）。
    /// 当たり判定は付けない。押し付け位置は <see cref="PlayerGrapple"/> が決め、
    /// レーンの外へ出ないための見えない壁（Stage Bound Z）はシーン側に既にある。
    /// </summary>
    [AddComponentMenu("Ryona/Wall Pin Props")]
    public sealed class WallPinProps : MonoBehaviour
    {
        /// <summary>壁を置く X。10m 間隔だと 1 画面ごとに 1 か所くらいになる。</summary>
        static readonly float[] SiteX = { -6f, 4f, 14f, 24f };

        const float FaceZ = 1.78f;
        const float PanelHeight = 2.18f;
        const float PanelWidth = 2.60f;
        const float PanelDepth = 0.30f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() => Create();

        static bool _hooked;

        static void Hook()
        {
            if (_hooked)
                return;

            _hooked = true;
            SceneManager.sceneLoaded += (scene, mode) => Create();
        }

        static void Create()
        {
            Hook();

            if (HasFlag("-nowallprops"))
                return;

            if (Object.FindFirstObjectByType<PlayerStats>() == null)
                return;

            if (Object.FindFirstObjectByType<WallPinSite>() != null)
                return;

            var root = new GameObject("Wall Pin Props");
            root.AddComponent<WallPinProps>().Build(root.transform);
        }

        static bool HasFlag(string flag)
        {
            foreach (string a in System.Environment.GetCommandLineArgs())
            {
                if (string.Equals(a, flag, System.StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        void Build(Transform root)
        {
            var shader = Shader.Find("Standard");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");
            if (shader == null)
            {
                Debug.LogWarning("[WallPin] 壁のマテリアル用シェーダが見つからないため作成を中止");
                return;
            }

            var panel = MakeMaterial(shader, new Color(0.32f, 0.36f, 0.42f));
            var trim = MakeMaterial(shader, new Color(0.13f, 0.135f, 0.15f));
            var cap = MakeMaterial(shader, new Color(0.46f, 0.49f, 0.53f));

            for (int i = 0; i < SiteX.Length; i++)
            {
                float x = SiteX[i];

                var siteGo = new GameObject($"Wall Pin Site {i + 1}");
                siteGo.transform.SetParent(root, false);
                siteGo.transform.position = new Vector3(x, 0f, FaceZ);
                var site = siteGo.AddComponent<WallPinSite>();
                site.HalfWidth = PanelWidth * 0.47f;
                site.FaceZ = FaceZ;
                var visuals = new GameObject("Visuals").transform;
                visuals.SetParent(siteGo.transform, false);
                site.VisualRoot = visuals;

                // 本体（表面を z = FaceZ に合わせる）
                BuildBox(visuals, "Panel", panel,
                    new Vector3(x, PanelHeight * 0.5f, FaceZ + PanelDepth * 0.5f),
                    new Vector3(PanelWidth, PanelHeight, PanelDepth));

                // 根元の台座と天端。平らな箱だけだと「置いた板」に見える。
                BuildBox(visuals, "Base", trim,
                    new Vector3(x, 0.14f, FaceZ + PanelDepth * 0.5f),
                    new Vector3(PanelWidth + 0.16f, 0.28f, PanelDepth + 0.16f));

                BuildBox(visuals, "Cap", cap,
                    new Vector3(x, PanelHeight + 0.05f, FaceZ + PanelDepth * 0.5f),
                    new Vector3(PanelWidth + 0.10f, 0.10f, PanelDepth + 0.10f));

                // 縦の目地。2 枚並んだ壁に見せて、のっぺりさせない。
                BuildBox(visuals, "Seam", trim,
                    new Vector3(x, PanelHeight * 0.5f, FaceZ - 0.005f),
                    new Vector3(0.06f, PanelHeight, 0.02f));
                site.SetRise(0f);
            }

            Debug.Log($"[WallPin] 壁を {SiteX.Length} か所作成（faceZ={FaceZ:F2}）");
        }

        static void BuildBox(Transform parent, string name, Material material,
            Vector3 position, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = material;

            // 見た目だけ。押し付け位置は PlayerGrapple が決める。
            var collider = go.GetComponent<Collider>();
            if (collider != null)
                Object.Destroy(collider);
        }

        static Material MakeMaterial(Shader shader, Color color)
        {
            var material = new Material(shader) { name = "WallPin_" + color.ToString("F2") };
            material.color = color;
            return material;
        }
    }
}
