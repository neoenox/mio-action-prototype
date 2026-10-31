using System.Collections;
using UnityEngine;

namespace Ryona.Game
{
    /// <summary>
    /// 敵の被弾リアクション。プロトタイプの敵はカプセル（スキンメッシュ無し）なので、
    /// リグや Animator が無くても成立する「フラッシュ / つぶれ / ノックバック」だけで作る。
    /// VRM が入っても同じ 3 点はそのまま効くので、ここは差し替えずに重ねられる。
    /// </summary>
    [AddComponentMenu("Ryona/Enemy Reaction")]
    public sealed class EnemyReaction : MonoBehaviour
    {
        [Header("References")]
        public EnemyBrain Brain;
        public Renderer Body;

        [Header("Tuning")]
        [Tooltip("発光が白に飛んでから戻るまでの秒数")]
        public float FlashSeconds = 0.08f;

        [Tooltip("つぶれが戻るまでの秒数")]
        public float SquashSeconds = 0.12f;

        [Tooltip("怯み段階 1.0 のときの押し出し距離（m）")]
        public float KnockbackDistance = 0.06f;

        [Tooltip("フラッシュの発光色")]
        public Color FlashColor = Color.white;

        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        MaterialPropertyBlock _block;
        Renderer[] _parts;
        Vector3 _baseScale = Vector3.one;
        Color _baseEmission = Color.black;
        bool _emissionReady;

        Coroutine _flashRoutine;
        Coroutine _squashRoutine;

        void Awake()
        {
            ResolveReferences();
            _baseScale = transform.localScale;

            if (Body != null && Body.sharedMaterial != null && Body.sharedMaterial.HasProperty(EmissionId))
                _baseEmission = Body.sharedMaterial.GetColor(EmissionId);
        }

        void OnEnable()
        {
            ResolveReferences();
            if (Brain != null)
                Brain.Hit += OnHit;
        }

        void OnDisable()
        {
            if (Brain != null)
                Brain.Hit -= OnHit;

            StopAllCoroutines();
            _flashRoutine = null;
            _squashRoutine = null;

            // 途中で無効化されても、つぶれたまま・光ったままにしない
            RestoreVisual();
        }

        void ResolveReferences()
        {
            if (Brain == null)
                Brain = GetComponent<EnemyBrain>();

            // 子に Telegraph の球があるので、必ず自分自身の Renderer を優先して拾う
            if (Body == null)
                Body = GetComponent<Renderer>();
            if (Body == null)
                Body = GetComponentInChildren<Renderer>();
        }

        void OnHit(HitResult result)
        {
            if (Brain == null)
                return;

            // EnemyBrain はイベント発火の後に Die() を呼ぶので、ここではもう IsAlive が false になっている
            bool dead = !Brain.IsAlive;

            float weight = dead ? 2.4f : TierWeight(result.Stagger);
            if (weight <= 0f)
                return;   // StaggerTier.None は無反応（軽い攻撃で常にビクつかせない）

            Flash(weight);
            Squash(weight, dead);
            Knockback(weight);
        }

        /// <summary>怯み段階ごとの演出の強さ。None は 0（無反応）。</summary>
        static float TierWeight(StaggerTier tier)
        {
            switch (tier)
            {
                case StaggerTier.Small: return 0.5f;
                case StaggerTier.Medium: return 1.0f;
                case StaggerTier.Large: return 1.6f;
                case StaggerTier.GuardBreak: return 2.0f;
                default: return 0f;
            }
        }

        // --- フラッシュ -------------------------------------------------

        void Flash(float weight)
        {
            if (Parts.Length == 0)
                return;   // Renderer が無ければ何もしない

            if (_flashRoutine != null)
                StopCoroutine(_flashRoutine);

            _flashRoutine = StartCoroutine(FlashRoutine(weight));
        }

        IEnumerator FlashRoutine(float weight)
        {
            if (Parts.Length == 0)
                yield break;

            EnsureEmission();

            // 強い怯みほど白く飛ばす（HDR なので 1 を超えてよい）
            float gain = Mathf.Lerp(1.2f, 2.4f, Mathf.Clamp01(weight * 0.5f));
            SetEmission(new Color(FlashColor.r * gain, FlashColor.g * gain, FlashColor.b * gain, 1f));

            // ヒットストップ中でも光が戻るよう unscaled で計時する
            float duration = Mathf.Max(0.01f, FlashSeconds);
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }

            SetEmission(_baseEmission);
            _flashRoutine = null;
        }

        /// <summary>
        /// 光らせる対象。敵の見た目は EnemyFigure が実行時に組み立てるので、
        /// ここで子の Renderer をまとめて拾っておく（予備動作の目印だけは除く）。
        /// </summary>
        Renderer[] Parts
        {
            get
            {
                if (_parts != null && _parts.Length > 0)
                    return _parts;

                var list = new System.Collections.Generic.List<Renderer>();
                if (Body != null)
                    list.Add(Body);

                foreach (var renderer in GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer.name == "Telegraph")
                        continue;
                    if (!list.Contains(renderer))
                        list.Add(renderer);
                }

                _parts = list.ToArray();
                return _parts;
            }
        }

        void EnsureEmission()
        {
            if (_emissionReady)
                return;

            _emissionReady = true;

            // Standard シェーダは _EMISSION キーワードが無いと発光を描かない。
            // アセットを汚さないよう、実行時マテリアル（インスタンス）側で有効にする。
            foreach (var renderer in Parts)
            {
                if (renderer == null)
                    continue;

                var mat = renderer.material;
                if (mat != null && mat.HasProperty(EmissionId))
                    mat.EnableKeyword("_EMISSION");
            }
        }

        void SetEmission(Color color)
        {
            if (Parts.Length == 0)
                return;

            _block ??= new MaterialPropertyBlock();

            foreach (var renderer in Parts)
            {
                if (renderer == null)
                    continue;

                renderer.GetPropertyBlock(_block);
                _block.SetColor(EmissionId, color);
                renderer.SetPropertyBlock(_block);
            }
        }

        // --- つぶれ -----------------------------------------------------

        void Squash(float weight, bool dead)
        {
            float w = Mathf.Clamp(weight, 0f, 2.5f);

            // 怯み段階 Medium(1.0) で仕様値の y=0.86 / xz=1.08 になる
            float yScale = 1f - 0.14f * w;
            float xzScale = 1f + 0.08f * w;

            // 死亡時は大きく潰し、戻りもゆっくりにする
            float seconds = dead ? SquashSeconds * 2.5f : SquashSeconds;

            if (_squashRoutine != null)
                StopCoroutine(_squashRoutine);

            _squashRoutine = StartCoroutine(SquashRoutine(yScale, xzScale, seconds));
        }

        IEnumerator SquashRoutine(float yScale, float xzScale, float seconds)
        {
            Vector3 squashed = new Vector3(
                _baseScale.x * xzScale,
                _baseScale.y * yScale,
                _baseScale.z * xzScale);

            transform.localScale = squashed;

            float duration = Mathf.Max(0.01f, seconds);
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / duration);
                // 戻りは smoothstep。等速で戻すとゴムっぽく見えない
                float e = k * k * (3f - 2f * k);
                transform.localScale = Vector3.LerpUnclamped(squashed, _baseScale, e);
                yield return null;
            }

            transform.localScale = _baseScale;
            _squashRoutine = null;
        }

        // --- ノックバック -----------------------------------------------

        void Knockback(float weight)
        {
            // 攻撃者の位置は HitResult に無いので、Target（プレイヤー）の反対方向へ押し出す。
            // EnemyBrain は Update で position を書き換えるため、押すのは 1 回だけの瞬間移動でよい。
            if (Brain == null || Brain.Target == null)
                return;

            Vector3 direction = transform.position - Brain.Target.position;
            direction.y = 0f;

            // 真上に重なっていると方向が定まらないので、その時は押さない
            if (direction.sqrMagnitude < 0.0001f)
                return;

            float distance = KnockbackDistance * Mathf.Clamp(weight, 0f, 2.5f);
            transform.position += direction.normalized * distance;
        }

        void RestoreVisual()
        {
            // つぶれは Renderer の有無に関係なく走るので、スケールは必ず戻す
            transform.localScale = _baseScale;
            SetEmission(_baseEmission);
        }
    }
}
