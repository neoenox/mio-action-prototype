using UnityEngine;

namespace Ryona.Game
{
    /// <summary>
    /// 敵が「彼女とカメラの間」に入ったときだけ透けさせる。
    ///
    /// ベルトモードはカメラが彼女の真後ろにあるので、背後へ回られた敵が
    /// 画面中央を塞ぎ、彼女が見えなくなる。遮蔽している間だけ薄くする。
    ///
    /// ・Standard マテリアル（プリミティブの敵）は半透明にして透けさせる
    /// ・MToon（VRM の敵）は実行時の透明度切り替えが効かないので、
    ///   カメラに近いときだけ描画を切る（一瞬なので許容）
    /// </summary>
    [DefaultExecutionOrder(23000)]
    public sealed class OcclusionFade : MonoBehaviour
    {
        [Tooltip("透け始めてから消えるまでの秒数")]
        public float FadeSeconds = 0.10f;

        [Tooltip("最も透けたときの不透明度")]
        public float MinAlpha = 0.25f;

        [Tooltip("遮蔽とみなす角度（度）。画面中央のこの範囲に入ったら透けさせる")]
        public float FadeAngle = 15f;

        /// <summary>
        /// カメラの目の前まで来た敵を「手前の塊」とみなす距離（m）。
        /// 画面端で体の一部だけが巨大に切れるだけで、彼女は隠していない。
        /// それでも写っていると邪魔なので薄くする（攻撃は見えるので読める）。
        /// シーンに保存された値に影響されないよう NonSerialized。
        /// </summary>
        [System.NonSerialized] public float NearFadeDistance = 3.6f;

        /// <summary>
        /// 彼女までの距離に対してこれより手前なら「手前の塊」とみなす比率。
        /// 0.72 は「画面上で彼女の約 1.5 倍の大きさ」に相当する（1.79/1.55 ÷ 0.72 ≒ 1.6）。
        /// 0.62 だと 1.7 倍まで許してしまい、画面端で体が切れたまま残っていた（実測）。
        /// </summary>
        [System.NonSerialized] public float NearFadeRatio = 0.72f;

        Transform _player;
        EnemyBrain _brain;
        Renderer[] _renderers;
        Material[][] _originals;
        Material[][] _faded;
        bool[] _fadeable;

        float _alpha = 1f;
        bool _applied;
        float _rescanTimer = 1f;
        bool _scannedVrm;
        float _logAt;
        bool _wasFaded;
        float _dbgAngle;
        float _dbgDist;
        float _dbgPlayerDist;
        string _dbgWhy = "";

        void Start()
        {
            ResolvePlayer();
            Refresh();
        }

        /// <summary>彼女のトランスフォームを探す。敵の生成順に依存しないようにする。</summary>
        void ResolvePlayer()
        {
            if (_brain == null)
                _brain = GetComponent<EnemyBrain>();
            if (_brain != null && _brain.Target != null)
            {
                _player = _brain.Target;
                return;
            }

            var combat = Object.FindFirstObjectByType<PlayerCombat>();
            if (combat != null)
                _player = combat.transform;
        }

        /// <summary>描画対象を取り直す。VRM が後から差し込まれた場合にも追従する。</summary>
        void Refresh()
        {
            // 再スキャン（VRM 差し替え）で古いフェード用マテリアルを漏らさない
            DestroyFadedMaterials();

            _renderers = GetComponentsInChildren<Renderer>(true);
            _originals = new Material[_renderers.Length][];
            _faded = new Material[_renderers.Length][];
            _fadeable = new bool[_renderers.Length];
            _applied = false;

            for (int i = 0; i < _renderers.Length; i++)
            {
                var mats = _renderers[i].sharedMaterials;
                _originals[i] = mats;

                var faded = new Material[mats.Length];
                bool ok = true;

                for (int m = 0; m < mats.Length; m++)
                {
                    if (mats[m] == null)
                        continue;

                    // Standard 以外（MToon など）は実行時に alpha を差し替えられない
                    if (!mats[m].HasProperty("_Mode") || !mats[m].HasProperty("_Color"))
                    {
                        ok = false;
                        continue;
                    }

                    faded[m] = MakeFade(mats[m]);
                }

                _fadeable[i] = ok;
                _faded[i] = faded;
            }
        }

        void LateUpdate()
        {
            // 敵の生成順によっては Start の時点で brain.Target がまだ入っていない。
            // null のまま諦めると、その敵は二度と透けないので毎フレーム取り直す。
            if (_player == null)
                ResolvePlayer();

            if (_player == null || _renderers == null)
                return;

            // VRM が後から読み込まれたら描画対象を取り直す
            _rescanTimer -= Time.deltaTime;
            if (_rescanTimer <= 0f)
            {
                _rescanTimer = 1f;

                var vrm = GetComponent<EnemyVrm>();
                if (vrm != null && vrm.Instance != null && !_scannedVrm)
                {
                    _scannedVrm = true;
                    Refresh();
                }
            }

            var mount = _brain != null && _brain.Player != null ? _brain.Player.GroundMount : null;
            bool inMountShot = mount != null && mount.Attacker == _brain && mount.BlocksGetUp;
            var hold = _brain != null && _brain.Player != null
                ? _brain.Player.GetComponent<PlayerGrapple>() : null;
            bool wallShot = hold != null && hold.IsWallPin;
            // 二体がかり（Y 字）の正面ショットでは、拘束役とアシストは消さない
            // （消すと「二人がかり」に見えなくなる）。
            bool splitShot = hold != null && hold.IsWallSplitActive &&
                (hold.Captor == _brain || hold.WallAssist == _brain);
            bool inFrontOfWallShot = wallShot && hold.Captor != _brain && !splitShot &&
                InFrontOfWallShot(hold);
            float target = inFrontOfWallShot ? 0f : 1f;
            if (!wallShot && !inMountShot && IsOccluding())
                target = MinAlpha;
            if (Mathf.Approximately(target, _alpha) && _applied)
                return;

            _alpha = inFrontOfWallShot || inMountShot || FadeSeconds <= 0.001f
                ? target
                : Mathf.MoveTowards(_alpha, target, Time.deltaTime / FadeSeconds);

            Apply(_alpha);

            // 効いているかを実機ログで確認できるようにする（1 秒に 1 回まで）
            // 効いているかを実機ログで確認できるようにする。
            // 通常は変化したときだけ、-occlog を付けたときは毎秒、判定値も出す。
            bool changed = _alpha < 0.9f || _wasFaded;
            if (Time.time - _logAt > 1f && (changed || VerboseLog))
            {
                _logAt = Time.time;
                _wasFaded = _alpha < 0.9f;

                if (VerboseLog)
                    Debug.Log($"[Occlusion] {name} alpha={_alpha:F2} " +
                        $"angle={_dbgAngle:F1} dist={_dbgDist:F2}/{_dbgPlayerDist:F2} why={_dbgWhy}");
                else
                    Debug.Log($"[Occlusion] {name} alpha={_alpha:F2} on={_wasFaded}");
            }
        }

        static bool _verbose;
        static bool _verboseRead;

        /// <summary>-occlog を付けたときだけ、毎秒の判定値ログを出す。</summary>
        static bool VerboseLog
        {
            get
            {
                if (_verboseRead)
                    return _verbose;

                _verboseRead = true;
                _verbose = LaunchOptions.Has("-occlog");

                return _verbose;
            }
        }

        /// <summary>カメラ → 彼女 の線の上に、彼女より手前で入っているか。</summary>
        bool IsOccluding()
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                _dbgWhy = "nocam";
                return false;
            }

            Vector3 camPos = cam.transform.position;
            Vector3 toPlayer = _player.position + Vector3.up * 1.0f - camPos;
            Vector3 toSelf = transform.position + Vector3.up * 0.9f - camPos;

            float playerDist = toPlayer.magnitude;
            _dbgPlayerDist = playerDist;
            _dbgDist = toSelf.magnitude;

            if (playerDist < 0.01f)
            {
                _dbgWhy = "nodist";
                return false;
            }

            // 彼女より奥にいるなら遮蔽していない
            if (toSelf.magnitude > playerDist)
            {
                _dbgWhy = "behind";
                return false;
            }

            // カメラに近い敵ほど画面上で大きく、少しずれていても視界を塞ぐ。
            // だから「彼女までの距離に対してどれだけ手前か」で許容量を変える。
            float ratio = toSelf.magnitude / playerDist;

            // 3D の角度で見ると、カメラのすぐ横にいる敵は角度が大きく出るのに
            // 画面上では彼女を覆う。画面座標で「どれだけ離れて写っているか」で判定する。
            Vector3 vpSelf = cam.WorldToViewportPoint(transform.position + Vector3.up * 0.9f);
            Vector3 vpPlayer = cam.WorldToViewportPoint(_player.position + Vector3.up * 1.0f);

            _dbgAngle = Vector3.Angle(toPlayer, toSelf);
            _dbgWhy = $"ok ratio={ratio:F2}";

            if (vpSelf.z <= 0f || vpPlayer.z <= 0f)
                return false;

            // カメラがほとんど敵の中に入っている場合、画面座標の比較が成立しない
            // （体が画面の大半を覆っているのに中心点は画面外、ということが起きる）。
            if (toSelf.magnitude < 1.3f)
                return true;

            // カメラの目の前まで来た敵は、画面端で体の一部だけが巨大に切れる。
            // 彼女を隠していなくても「手前の塊」として邪魔なので薄くする。
            if (toSelf.magnitude < NearFadeDistance && ratio < NearFadeRatio)
            {
                _dbgWhy = $"near ratio={ratio:F2} dist={toSelf.magnitude:F2}";
                return true;
            }

            float dx = (vpSelf.x - vpPlayer.x) * Screen.width;
            float dy = (vpSelf.y - vpPlayer.y) * Screen.height;
            float screenGap = Mathf.Sqrt(dx * dx + dy * dy);

            // 手前にあるほど画面を大きく占めるので、許容する隔たりも広げる。
            float allowed = Screen.height * Mathf.Lerp(0.46f, 0.16f, Mathf.Clamp01(ratio));

            _dbgWhy = $"ok ratio={ratio:F2} gap={screenGap:F0}/{allowed:F0}";

            return screenGap < allowed;
        }

        bool InFrontOfWallShot(PlayerGrapple hold)
        {
            var cam = Camera.main;
            if (cam == null || hold.Captor == null)
                return false;

            Vector3 middle = (_player.position + hold.Captor.transform.position) * 0.5f;
            Vector3 cameraForward = cam.transform.forward;
            // 腕や胴が画面を塞ぐので、敵の中心が画面外でも奥行きで判定する。
            return Vector3.Dot(transform.position - middle, cameraForward) < 0.45f;
        }

        /// <summary>
        /// テスト用: 走査結果を直接差し込む。走査は実行中のシーンでしか揃わないので、
        /// 生成直後のコンポーネントで Apply の分岐だけを検証したいときに使う。
        /// </summary>
        internal void ConfigureForTest(Renderer[] renderers, Material[][] originals, bool[] fadeable)
        {
            _renderers = renderers;
            _originals = originals;
            _fadeable = fadeable;
            _faded = new Material[renderers.Length][];

            for (int i = 0; i < renderers.Length; i++)
                _faded[i] = new Material[originals[i].Length];
        }

        internal void Apply(float alpha)
        {
            bool faded = alpha < 0.995f;

            for (int i = 0; i < _renderers.Length; i++)
            {
                var renderer = _renderers[i];
                if (renderer == null)
                    continue;

                renderer.forceRenderingOff = alpha <= 0.05f;
                if (!faded)
                {
                    if (_applied)
                        renderer.sharedMaterials = _originals[i];
                    if (!_fadeable[i])
                        renderer.enabled = true;
                    continue;
                }

                if (_fadeable[i])
                {
                    var mats = _faded[i];
                    for (int m = 0; m < mats.Length; m++)
                    {
                        if (mats[m] == null)
                            continue;

                        Color c = mats[m].color;
                        c.a = alpha;
                        mats[m].color = c;
                    }

                    renderer.sharedMaterials = mats;
                }
                else
                {
                    // MToon は半透明にできないので、近いときだけ描画を切る
                    renderer.enabled = alpha > 0.55f;
                }
            }

            _applied = faded;
        }

        static Material MakeFade(Material source)
        {
            var mat = new Material(source)
            {
                name = source.name + "_fade",
                renderQueue = 3000
            };

            mat.SetFloat("_Mode", 3f);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            return mat;
        }

        void OnDisable()
        {
            if (_applied)
                Apply(1f);
            _alpha = 1f;
        }

        void OnDestroy()
        {
            DestroyFadedMaterials();
        }

        void DestroyFadedMaterials()
        {
            if (_faded == null)
                return;

            for (int i = 0; i < _faded.Length; i++)
            {
                if (_faded[i] == null)
                    continue;

                for (int m = 0; m < _faded[i].Length; m++)
                {
                    if (_faded[i][m] != null)
                        Destroy(_faded[i][m]);
                }
            }

            _faded = null;
        }
    }

    /// <summary>シーンに OcclusionFade を足す。シーンを作り直さなくても効くようにしてある。</summary>
    static class OcclusionFadeBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void InstallSceneHook()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
        }

        static void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene,
            UnityEngine.SceneManagement.LoadSceneMode mode) => Boot();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var figures = Object.FindObjectsByType<EnemyFigure>(FindObjectsInactive.Exclude);

            for (int i = 0; i < figures.Length; i++)
            {
                var figure = figures[i];
                if (figure == null || figure.GetComponent<OcclusionFade>() != null)
                    continue;

                figure.gameObject.AddComponent<OcclusionFade>();
            }
        }
    }
}
