using System.Collections.Generic;
using UnityEngine;

namespace Ryona.Game
{
    /// <summary>
    /// 「彼女の顔が画面に映っているか」を数値で測る（AGENTS.md の演出の原則を、目視ではなく数字で守るため）。
    ///
    /// 顔の周りの 5 点（中心と上下左右）について、
    /// <list type="bullet">
    /// <item>画面の中にあるか（フレームアウトしていないか）</item>
    /// <item>カメラとの間に、敵の体が入っていないか</item>
    /// </list>
    /// を調べ、両方を満たす点の割合を返す。
    ///
    /// 敵の体には当たり判定が無い（<c>EnemyFigure</c> のプリミティブは生成時にコライダーを外す）ので、
    /// 物理のレイキャストは使えない。プリミティブ（球・カプセル・箱）の形に対して、
    /// カメラから顔への線分が交わるかを直接計算する。VRM の敵も骨格は同じプリミティブなので、
    /// 体の近似として使える。
    ///
    /// 測らないもの: 彼女自身の前髪・腕が顔にかかること、HUD の帯が重なること。
    /// </summary>
    public static class FaceVisibility
    {
        /// <summary>判定に使う画面の縦横比。バッチ実行（-nographics）は画面サイズが実機と違うので固定する。</summary>
        public const float Aspect = 16f / 9f;

        /// <summary>画面の端からこの割合より内側にあれば「画面の中」とみなす。</summary>
        public const float FrameMargin = 0.03f;

        /// <summary>顔の中心から、上下左右の点までの距離（m）。</summary>
        public const float FaceRadius = 0.07f;

        /// <summary>
        /// 顔のすぐ手前（m）は遮蔽に数えない。顔に当たっている拳まで「隠している」と数えると、
        /// 顔パンの瞬間が必ず失敗になる。
        /// </summary>
        public const float NearFaceSlack = 0.10f;

        /// <summary>この可視率以上なら「顔が映っている」コマとみなす（5 点中 3 点以上）。</summary>
        public const float GoodVisible = 0.6f;

        /// <summary>
        /// 顔の正面とカメラの向きのなす角がこれを超えたら「顔が読めない」とみなす（度）。
        /// うつむく・横を向くと、前髪や頬で表情が隠れる。頭がカメラに見えていても顔は読めない。
        /// </summary>
        public const float FacingLimit = 60f;

        /// <summary>
        /// 壁拘束の各段階（拘束・膝 / 猶予 / 二人がかり / 3 人がかり）で、顔が読めるコマの割合の下限。
        /// 実測（2026-11-15、壁の膝のカメラを下げた後）: 86% / 85% / 96% / 83% より少し下に置いて、悪化を止める。
        /// 下げる前は拘束・膝 41〜72%、猶予 59〜61% だった（隠していたのは全て拘束役。PinHoldPose.WallHeldShot）。
        /// 3 人がかりで残っている遮蔽は、主に拘束役（約 7%）とアシスト。改善したら、ここも引き上げる。
        /// </summary>
        public static readonly float[] RequiredRatio = { 0.75f, 0.75f, 0.90f, 0.75f };

        /// <summary>この不透明度より薄い敵は、遮蔽に数えない（<see cref="OcclusionFade"/> が透かしている）。</summary>
        public const float OpaqueAlpha = 0.5f;

        public struct Result
        {
            /// <summary>画面の中にあり、かつ隠れていない点の割合（0..1）。</summary>
            public float Visible;

            /// <summary>画面の中にある点の割合（0..1）。</summary>
            public float InFrame;

            /// <summary>敵の体に隠れていない点の割合（0..1）。</summary>
            public float Unblocked;

            /// <summary>顔を隠していた敵の名前（複数なら最初の一体）。隠れていなければ null。</summary>
            public string Blocker;

            /// <summary>顔の正面と「顔からカメラへの向き」のなす角（度）。0 で真正面、90 で真横。</summary>
            public float FacingAngle;

            /// <summary>顔が読めるか（映っていて、かつカメラの方を向いている）。</summary>
            public bool Readable;
        }

        enum Shape
        {
            Sphere,
            Capsule,
            Cube
        }

        struct Proxy
        {
            public Transform Transform;
            public Shape Shape;
        }

        static readonly Dictionary<EnemyFigure, Proxy[]> Cache = new Dictionary<EnemyFigure, Proxy[]>();
        static readonly Vector3[] Points = new Vector3[5];

        /// <summary>
        /// 顔の可視率を測る。<paramref name="head"/> は彼女の頭の骨（<c>ProceduralRig.Head</c>）。
        /// </summary>
        public static Result Measure(Camera camera, Transform head, IReadOnlyList<EnemyFigure> enemies)
        {
            var result = new Result();
            if (camera == null || head == null)
                return result;

            Transform cam = camera.transform;
            Vector3 eye = cam.position;
            // 頭の骨は首の付け根にある。顔の中心は少し上・前（PhotoShot の顔カメラと同じ扱い）。
            Vector3 center = head.position + head.up * 0.06f + head.forward * 0.05f;
            result.FacingAngle = Vector3.Angle(head.forward, eye - center);
            Points[0] = center;
            Points[1] = center + cam.right * FaceRadius;
            Points[2] = center - cam.right * FaceRadius;
            Points[3] = center + cam.up * FaceRadius;
            Points[4] = center - cam.up * FaceRadius;

            int inFrame = 0;
            int unblocked = 0;
            int visible = 0;
            for (int i = 0; i < Points.Length; i++)
            {
                bool framed = IsInFrame(camera, Points[i]);
                bool clear = true;
                if (enemies != null)
                {
                    for (int e = 0; e < enemies.Count && clear; e++)
                    {
                        EnemyFigure enemy = enemies[e];
                        if (enemy == null || !enemy.isActiveAndEnabled || IsFaded(enemy))
                            continue;

                        if (Blocks(enemy, eye, Points[i]))
                        {
                            clear = false;
                            if (result.Blocker == null)
                                result.Blocker = enemy.name;
                        }
                    }
                }

                if (framed) inFrame++;
                if (clear) unblocked++;
                if (framed && clear) visible++;
            }

            result.InFrame = inFrame / (float)Points.Length;
            result.Unblocked = unblocked / (float)Points.Length;
            result.Visible = visible / (float)Points.Length;
            result.Readable = result.Visible >= GoodVisible && result.FacingAngle <= FacingLimit;
            return result;
        }

        /// <summary>
        /// 顔（直径 <see cref="FaceRadius"/> × 2）が画面の高さに占める割合（0..1）。
        /// 可視率は「顔の 5 点が見えるか」なので、遠くて豆粒でも満点になる。読める大きさかはこれで見る。
        /// </summary>
        public static float ScreenFaceSize(Camera camera, Transform head)
        {
            if (camera == null || head == null)
                return 0f;

            Vector3 center = head.position + head.up * 0.06f + head.forward * 0.05f;
            float depth = camera.transform.InverseTransformPoint(center).z;
            if (depth <= camera.nearClipPlane)
                return 0f;

            float screenHeight = 2f * depth * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            return screenHeight > 1e-4f ? FaceRadius * 2f / screenHeight : 0f;
        }

        /// <summary>点が画面の中にあるか。縦横比は <see cref="Aspect"/> で固定して計算する。</summary>
        public static bool IsInFrame(Camera camera, Vector3 world)
        {
            Vector3 local = camera.transform.InverseTransformPoint(world);
            if (local.z <= camera.nearClipPlane)
                return false;

            float tanV = Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float x = local.x / (local.z * tanV * Aspect);
            float y = local.y / (local.z * tanV);
            float limit = 1f - FrameMargin * 2f;
            return Mathf.Abs(x) <= limit && Mathf.Abs(y) <= limit;
        }

        static bool IsFaded(EnemyFigure enemy)
        {
            var fade = enemy.GetComponent<OcclusionFade>();
            return fade != null && fade.CurrentAlpha < OpaqueAlpha;
        }

        static bool Blocks(EnemyFigure enemy, Vector3 eye, Vector3 point)
        {
            // 顔のすぐ手前は数えない（顔に当たっている拳など）。
            Vector3 toPoint = point - eye;
            float length = toPoint.magnitude;
            if (length <= NearFaceSlack + 0.01f)
                return false;
            Vector3 end = eye + toPoint * ((length - NearFaceSlack) / length);

            Proxy[] proxies = ProxiesOf(enemy);
            for (int i = 0; i < proxies.Length; i++)
            {
                Transform t = proxies[i].Transform;
                if (t == null || !t.gameObject.activeInHierarchy)
                    continue;

                if (SegmentHits(t, proxies[i].Shape, eye, end))
                    return true;
            }
            return false;
        }

        static Proxy[] ProxiesOf(EnemyFigure enemy)
        {
            if (Cache.TryGetValue(enemy, out Proxy[] cached))
                return cached;

            var list = new List<Proxy>();
            foreach (var filter in enemy.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh mesh = filter.sharedMesh;
                if (mesh == null)
                    continue;

                // Unity の組み込みプリミティブの名前で形を決める（EnemyFigure.Primitives が作るもの）。
                Shape shape;
                if (mesh.name.StartsWith("Sphere")) shape = Shape.Sphere;
                else if (mesh.name.StartsWith("Capsule")) shape = Shape.Capsule;
                else if (mesh.name.StartsWith("Cube")) shape = Shape.Cube;
                else continue;

                list.Add(new Proxy { Transform = filter.transform, Shape = shape });
            }

            Proxy[] proxies = list.ToArray();
            Cache[enemy] = proxies;
            return proxies;
        }

        /// <summary>
        /// カメラ位置から敵のプリミティブ表面までの最短距離（m）。レンズのすぐ前に来た体を検出する。
        /// </summary>
        public static float LensClearance(Vector3 cameraPosition, IReadOnlyList<EnemyFigure> enemies)
        {
            float best = float.MaxValue;
            if (enemies == null)
                return best;

            for (int e = 0; e < enemies.Count; e++)
            {
                EnemyFigure enemy = enemies[e];
                if (enemy == null || !enemy.isActiveAndEnabled || IsFaded(enemy))
                    continue;

                Proxy[] proxies = ProxiesOf(enemy);
                for (int i = 0; i < proxies.Length; i++)
                {
                    Transform t = proxies[i].Transform;
                    if (t == null || !t.gameObject.activeInHierarchy)
                        continue;

                    Vector3 p = t.InverseTransformPoint(cameraPosition);
                    float distance;
                    switch (proxies[i].Shape)
                    {
                        case Shape.Cube:
                            Vector3 q = new Vector3(
                                Mathf.Max(0f, Mathf.Abs(p.x) - 0.5f),
                                Mathf.Max(0f, Mathf.Abs(p.y) - 0.5f),
                                Mathf.Max(0f, Mathf.Abs(p.z) - 0.5f));
                            distance = q.magnitude;
                            break;
                        case Shape.Capsule:
                            distance = Mathf.Sqrt(SegmentToPointSqr(
                                new Vector3(0f, -0.5f, 0f), new Vector3(0f, 0.5f, 0f), p)) - 0.5f;
                            break;
                        default:
                            distance = p.magnitude - 0.5f;
                            break;
                    }

                    Vector3 s = t.lossyScale;
                    distance *= Mathf.Min(s.x, Mathf.Min(s.y, s.z));
                    if (distance < best)
                        best = distance;
                }
            }
            return best;
        }


        /// <summary>
        /// 線分 <paramref name="from"/>→<paramref name="to"/> が、<paramref name="primitive"/> の形と交わるか。
        /// 線分をプリミティブのローカル空間へ移し、単位の形（球: 半径 0.5 / カプセル: 半径 0.5・全高 2 /
        /// 箱: 一辺 1）と交差を取る。スケールが軸ごとに違っても、ローカル空間では単位の形なので正確。
        /// </summary>
        public static bool SegmentHits(Transform primitive, PrimitiveType type, Vector3 from, Vector3 to)
        {
            Shape shape = type == PrimitiveType.Capsule ? Shape.Capsule
                : type == PrimitiveType.Cube ? Shape.Cube
                : Shape.Sphere;
            return SegmentHits(primitive, shape, from, to);
        }

        static bool SegmentHits(Transform primitive, Shape shape, Vector3 from, Vector3 to)
        {
            Vector3 a = primitive.InverseTransformPoint(from);
            Vector3 b = primitive.InverseTransformPoint(to);
            switch (shape)
            {
                case Shape.Cube:
                    return SegmentHitsUnitCube(a, b);
                case Shape.Capsule:
                    // 軸は Y。円柱部分の半分の高さが 0.5、両端に半径 0.5 の半球。
                    return SegmentToSegmentSqr(a, b, new Vector3(0f, -0.5f, 0f), new Vector3(0f, 0.5f, 0f)) <= 0.25f;
                default:
                    return SegmentToPointSqr(a, b, Vector3.zero) <= 0.25f;
            }
        }

        static float SegmentToPointSqr(Vector3 a, Vector3 b, Vector3 p)
        {
            Vector3 ab = b - a;
            float len = ab.sqrMagnitude;
            float t = len > 1e-10f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / len) : 0f;
            return (a + ab * t - p).sqrMagnitude;
        }

        static bool SegmentHitsUnitCube(Vector3 a, Vector3 b)
        {
            Vector3 d = b - a;
            float tMin = 0f;
            float tMax = 1f;
            for (int axis = 0; axis < 3; axis++)
            {
                float o = a[axis];
                float dir = d[axis];
                if (Mathf.Abs(dir) < 1e-8f)
                {
                    if (o < -0.5f || o > 0.5f)
                        return false;
                    continue;
                }

                float t1 = (-0.5f - o) / dir;
                float t2 = (0.5f - o) / dir;
                if (t1 > t2)
                {
                    float swap = t1;
                    t1 = t2;
                    t2 = swap;
                }

                tMin = Mathf.Max(tMin, t1);
                tMax = Mathf.Min(tMax, t2);
                if (tMin > tMax)
                    return false;
            }
            return true;
        }

        /// <summary>2 本の線分の最短距離の 2 乗。</summary>
        static float SegmentToSegmentSqr(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2)
        {
            Vector3 d1 = q1 - p1;
            Vector3 d2 = q2 - p2;
            Vector3 r = p1 - p2;
            float a = Vector3.Dot(d1, d1);
            float e = Vector3.Dot(d2, d2);
            float f = Vector3.Dot(d2, r);
            float s;
            float t;

            if (a <= 1e-10f && e <= 1e-10f)
                return r.sqrMagnitude;

            if (a <= 1e-10f)
            {
                s = 0f;
                t = Mathf.Clamp01(f / e);
            }
            else
            {
                float c = Vector3.Dot(d1, r);
                if (e <= 1e-10f)
                {
                    t = 0f;
                    s = Mathf.Clamp01(-c / a);
                }
                else
                {
                    float b = Vector3.Dot(d1, d2);
                    float denom = a * e - b * b;
                    s = denom > 1e-10f ? Mathf.Clamp01((b * f - c * e) / denom) : 0f;
                    t = (b * s + f) / e;
                    if (t < 0f)
                    {
                        t = 0f;
                        s = Mathf.Clamp01(-c / a);
                    }
                    else if (t > 1f)
                    {
                        t = 1f;
                        s = Mathf.Clamp01((b - c) / a);
                    }
                }
            }

            Vector3 c1 = p1 + d1 * s;
            Vector3 c2 = p2 + d2 * t;
            return (c1 - c2).sqrMagnitude;
        }
    }
}
