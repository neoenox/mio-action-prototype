using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UniVRM10;

namespace Ryona.Game
{
    /// <summary>
    /// 敵の見た目を VRM に差し替える。
    ///
    /// 姿勢は EnemyFigure（見えないプリミティブ骨格）が作ったものをそのまま写す。
    /// 骨格は消さずに見た目だけ消すので、AI・移動・攻撃判定・接地判定は
    /// これまでと完全に同じように動く。
    ///
    /// 使い方：`Assets/StreamingAssets/enemy_v1.vrm` を置くだけ。
    /// ファイルが無ければプリミティブのまま動く。
    /// </summary>
    // Vrm10Instance は DefaultExecutionOrder(11000) の LateUpdate で
    // ヒューマノイドを初期姿勢に戻す。それより後に書かないと T ポーズに戻される。
    //
    // 順番: EnemyFigure(11500) → CharacterAnimator(20000) → BackHoldPose(21000) → EnemyVrm(22000) → OcclusionFade(23000)
    // 拘束の腕を主人公の姿勢に合わせてから VRM に写す。
    // プレイヤー側の CharacterAnimator も 20000 で動いている。
    [DefaultExecutionOrder(22000)]
    [AddComponentMenu("Ryona/Enemy VRM")]
    public sealed class EnemyVrm : MonoBehaviour
    {
        [Tooltip("Assets/StreamingAssets/ からの相対名")]
        public string FileName = "enemy_v1.vrm";

        [Tooltip("未指定なら同じオブジェクトの EnemyFigure を使う")]
        public EnemyFigure Figure;

        [Tooltip("身長の自動合わせを切るとき用。1 で VRM のまま")]
        public bool AutoFitHeight = true;

        /// <summary>武器を VRM の右手の骨に付ける（プリミティブの拳の位置だと手から外れる）。</summary>
        [System.NonSerialized] public bool AttachWeaponToVrmHand = true;

        /// <summary>手の骨から見た武器の握り位置。</summary>
        [System.NonSerialized] public Vector3 WeaponLocalPosition = new Vector3(0f, -0.02f, 0.02f);

        /// <summary>手の骨から見た武器の向き。指の向き（Y）は肘と同じなので、プリミティブで調整した値をそのまま使える。</summary>
        [System.NonSerialized] public Vector3 WeaponLocalEuler = new Vector3(-55f, 0f, 16f);

        public bool Ready => _ready;
        public GameObject Instance => _instance;

        ProceduralRig _rig;
        GameObject _instance;
        bool _ready;

        // 破棄されたかどうか。読み込み中に破棄された場合を catch 節から見分ける。
        bool _disposed;

        void OnDestroy() => _disposed = true;

        // VRM 側の骨
        Transform _hips, _chest, _head;
        Transform _armL, _armR, _foreL, _foreR, _handL, _handR;

        /// <summary>-vrmlog で手の追従のずれを出す。</summary>
        static bool _vrmLog;
        float _vrmLogTimer;
        Transform _thighL, _thighR, _shinL, _shinR, _footL, _footR;

        // 初期姿勢。腰は「位置のずれ」、それ以外は「向きのずれ」で持つ
        Vector3 _hipsRestOffset;
        Quaternion _hipsRest, _chestRest, _headRest, _footRestL, _footRestR;

        void Start()
        {
            if (Figure == null)
                Figure = GetComponent<EnemyFigure>();

            foreach (string a in System.Environment.GetCommandLineArgs())
            {
                if (string.Equals(a, "-vrmlog", System.StringComparison.OrdinalIgnoreCase))
                    _vrmLog = true;
            }

            _ = LoadAsync();
        }

        public async Task<bool> LoadAsync()
        {
            if (Figure == null)
                return false;

            string path = ResolvePath(FileName);
            if (path == null)
            {
                // 置かれるまではプリミティブのまま。毎回警告を出すと邪魔なので 1 回だけ
                Debug.Log($"[Ryona] 敵 VRM は未配置（プリミティブで続行）: {FileName}  " +
                          $"探索={DescribeSearchPaths(FileName)}");
                return false;
            }

            // UniVRM の読み込みを同時に走らせると固まる（4 体ぶん並行でハングを確認）。
            // プレイヤーと敵で同じ関門を使い、必ず 1 体ずつ読む。
            await VrmLoadGate.Semaphore.WaitAsync();

            Vrm10Instance vrm = null;
            try
            {
                Debug.Log($"[Ryona] 敵 VRM 読み込み開始: {FileName} from={path}");
                vrm = await Vrm10.LoadPathAsync(path, canLoadVrm0X: true, showMeshes: true);
                Debug.Log($"[Ryona] 敵 VRM 読み込み完了: {FileName} ok={(vrm != null)}");
            }
            catch (System.Exception e)
            {
                // 破棄済みなら、UniGLTF が捨てられた Transform に触れただけ。想定内。
                if (_disposed || this == null)
                    Debug.LogWarning($"[Ryona] シーン破棄に伴い敵 VRM の読み込みを取りやめました: {path}");
                else
                    Debug.LogError($"[Ryona] 敵 VRM の読み込みに失敗: {path}\n{e}");
            }
            finally
            {
                VrmLoadGate.Semaphore.Release();
            }

            if (vrm == null)
                return false;

            // await の間にシーンが破棄されることがある
            if (_disposed || this == null)
            {
                Destroy(vrm.gameObject);
                return false;
            }

            // 組み込みの失敗は async だと握り潰されるので、必ずログに出す
            try
            {
                return Setup(vrm);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[Ryona] 敵 VRM の組み込みに失敗: {FileName}\n{e}");
                return false;
            }
        }

        /// <summary>
        /// 置き場所を候補順に探す。
        /// 1) ビルドに同梱された StreamingAssets（本来の場所）
        /// 2) プロジェクトの Assets/StreamingAssets（書き出しただけで効く＝再ビルド不要）
        /// 3) exe の隣（Build/enemy_v1.vrm。一番分かりやすい）
        /// 4) persistentDataPath（ゲーム本体を触らずに差し替えたいとき）
        /// </summary>
        static string ResolvePath(string fileName)
        {
            string[] candidates = BuildSearchPaths(fileName);

            for (int i = 0; i < candidates.Length; i++)
            {
                if (File.Exists(candidates[i]))
                    return candidates[i];
            }

            return null;
        }

        /// <summary>
        /// 敵 VRM を置くべき場所の候補（この順に探す）。 Editor の確認ツールからも使う。
        /// </summary>
        public static string[] BuildSearchPaths(string fileName)
        {
            var raw = new[]
            {
                Path.Combine(Application.streamingAssetsPath, fileName),
                Path.Combine(Application.streamingAssetsPath, "..", "..", "..", "Assets", "StreamingAssets", fileName),
                Path.Combine(Application.dataPath, "..", fileName),
                Path.Combine(Application.persistentDataPath, fileName),
            };

            var result = new string[raw.Length];
            for (int i = 0; i < raw.Length; i++)
            {
                try
                {
                    result[i] = Path.GetFullPath(raw[i]);
                }
                catch (System.Exception)
                {
                    result[i] = raw[i];
                }
            }

            return result;
        }

        static string DescribeSearchPaths(string fileName)
        {
            string[] paths = BuildSearchPaths(fileName);
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < paths.Length; i++)
            {
                if (i > 0)
                    sb.Append(" | ");
                sb.Append(paths[i]);
            }
            return sb.ToString();
        }

        /// <summary>
        /// 鉄パイプを VRM の右手に握らせる。
        /// プリミティブの拳は体格差のスケール・腰のオフセット・1 フレーム遅れのぶんだけ
        /// VRM の手とずれるので、その位置に描くと手から外れて宙に浮く。
        /// </summary>
        void AttachWeaponToHand()
        {
            if (!AttachWeaponToVrmHand)
                return;

            Transform weapon = Figure != null ? Figure.WeaponPivot : null;
            if (weapon == null || _handR == null)
                return;

            weapon.SetParent(_handR, false);
            weapon.localPosition = WeaponLocalPosition;
            weapon.localRotation = Quaternion.Euler(WeaponLocalEuler);
        }

        bool Setup(Vrm10Instance vrm)
        {
            _instance = vrm.gameObject;
            _instance.name = "Enemy VRM";
            _instance.transform.SetParent(transform, false);
            _instance.transform.localPosition = Vector3.zero;
            _instance.transform.localRotation = Quaternion.identity;
            _instance.transform.localScale = Vector3.one;

            // 当たり判定はキャラクター側で持つので切る
            foreach (var col in _instance.GetComponentsInChildren<Collider>(true))
                col.enabled = false;

            // 倒れたときに画面内でもカリングされないように
            foreach (var smr in _instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                smr.updateWhenOffscreen = true;

            _rig = _instance.GetComponent<ProceduralRig>();
            if (_rig == null)
                _rig = _instance.AddComponent<ProceduralRig>();

            _rig.Capture();

            if (!_rig.Ready)
            {
                Debug.LogError("[Ryona] 敵 VRM にヒューマノイドボーンがありません。" +
                               "VRoid の既定ボーンで書き出してください。");
                return false;
            }

            if (AutoFitHeight)
                FitHeight();
            // 身長を合わせたあとの初期姿勢を控え直す
            _rig.Capture();

            _hips = _rig.Hips;
            _chest = _rig.Chest;
            _head = _rig.Head;
            _armL = _rig.LUpperArm;
            _armR = _rig.RUpperArm;
            _foreL = _rig.LLowerArm;
            _foreR = _rig.RLowerArm;
            _handL = _rig.LHand;
            _handR = _rig.RHand;
            _thighL = _rig.LUpperLeg;
            _thighR = _rig.RUpperLeg;
            _shinL = _rig.LLowerLeg;
            _shinR = _rig.RLowerLeg;
            _footL = _rig.LFoot;
            _footR = _rig.RFoot;

            Quaternion root = transform.rotation;
            _hipsRest = Quaternion.Inverse(root) * _hips.rotation;
            _chestRest = Quaternion.Inverse(root) * _chest.rotation;
            _headRest = Quaternion.Inverse(root) * _head.rotation;
            _footRestL = Quaternion.Inverse(root) * _footL.rotation;
            _footRestR = Quaternion.Inverse(root) * _footR.rotation;

            // プリミティブの腰は「足元 + 身長 * 0.515」の位置に置かれている
            Vector3 proxyRest = transform.position + Vector3.up * (Figure.Height * 0.515f);
            _hipsRestOffset = _hips.position - proxyRest;

            // 見た目だけ消す（骨格は姿勢の計算に使うので残す）
            Figure.HidePrimitives();

            // 武器は VRM の手の骨に直接付ける。
            // プリミティブの拳の位置に描くと、体格差のスケール・腰のオフセット・1 フレーム遅れで
            // 手から外れて宙に浮く（実測で確認済み）。
            Figure.ShowWeapon();
            AttachWeaponToHand();

            _ready = true;
            Debug.Log($"[Ryona] 敵 VRM を差し込みました: {FileName}  " +
                      $"scale={_instance.transform.localScale.x:0.000} root={transform.position} " +
                      $"headRest={_head.position.y - transform.position.y:0.000} " +
                      $"hipsRest={_hips.position.y:0.000} proxyRest={proxyRest.y:0.000} " +
                      $"offset={_hipsRestOffset}");
            return true;
        }

        /// <summary>
        /// 身長をプリミティブ骨格に合わせる。合わないと足が浮く／埋まる。
        /// 頭のボーンの高さで合わせる（VRM は足元が原点）。
        /// </summary>
        void FitHeight()
        {
            if (_rig == null || _rig.Head == null)
                return;

            float vrmHead = _rig.Head.position.y - transform.position.y;
            if (vrmHead < 0.5f)
                return;

            // プリミティブ骨格の頭ボーンは 身長 * 0.85 の高さ
            float target = Figure.Height * 0.85f;
            float scale = target / vrmHead;

            // 極端な値は信じない（単位の取り違え等）
            if (scale < 0.5f || scale > 2f)
                return;

            _instance.transform.localScale = Vector3.one * scale;
        }

        void LateUpdate()
        {
            if (!_ready || Figure == null || _rig == null)
                return;

            Transform proxyHips = Figure.HipsBone;
            if (proxyHips == null)
                return;

            Quaternion root = transform.rotation;

            // 腰は位置も写す（しゃがみ・倒れ込みで上下する）
            _hips.position = proxyHips.position + _hipsRestOffset;
            CopyRotation(_hips, proxyHips, _hipsRest, root);
            CopyRotation(_chest, Figure.ChestBone, _chestRest, root);
            CopyRotation(_head, Figure.HeadBone, _headRest, root);

            AimArm(_armR, _foreR, _handR,
                Figure.ShoulderBone(false), Figure.ElbowBone(false), Figure.WristBone(false), Figure.Fist(false));
            AimArm(_armL, _foreL, _handL,
                Figure.ShoulderBone(true), Figure.ElbowBone(true), Figure.WristBone(true), Figure.Fist(true));

            AimLeg(_thighR, _shinR, _footR, Figure.HipBone(false), Figure.KneeBone(false), Figure.AnkleBone(false));
            AimLeg(_thighL, _shinL, _footL, Figure.HipBone(true), Figure.KneeBone(true), Figure.AnkleBone(true));

            CopyRotation(_footR, Figure.Foot(false), _footRestR, root);
            CopyRotation(_footL, Figure.Foot(true), _footRestL, root);

            // 敵は常に拳を握っている
            _rig.CurlFingers(true, 1f);
            _rig.CurlFingers(false, 1f);

            LogHandGap();
        }

        /// <summary>
        /// -vrmlog: 「プリミティブの手首」と「VRM の手の骨」のずれを 0.25 秒ごとに出す。
        /// ここが 0 に近ければ同じフレームの姿勢を写せている（1 フレーム遅れが無い）。
        /// 遅れていると振りの速い瞬間だけ数値が跳ねる。
        /// </summary>
        void LogHandGap()
        {
            if (!_vrmLog || _handR == null || Figure == null)
                return;

            _vrmLogTimer -= Time.deltaTime;
            if (_vrmLogTimer > 0f)
                return;

            _vrmLogTimer = 0.25f;

            Transform wrist = Figure.WristBone(false);
            if (wrist == null)
                return;

            Debug.Log($"[VrmLog] hand-wrist={Vector3.Distance(_handR.position, wrist.position):F3}");
        }

        /// <summary>骨の向きを、プリミティブ側の関節の位置関係に合わせる。</summary>
        static void AimArm(Transform upper, Transform lower, Transform hand,
            Transform pShoulder, Transform pElbow, Transform pWrist, Transform pFist)
        {
            if (upper == null || lower == null || pShoulder == null || pElbow == null)
                return;

            ProceduralRig.Aim(upper, lower, pElbow.position - pShoulder.position);

            if (hand == null || pFist == null)
                return;

            // 前腕は「肘 → 手首」に合わせる。拳まで含めると、手首を折ったぶん
            // 前腕まで一緒に曲がってしまう（手首が無いのと同じ見え方になる）。
            Vector3 forearm = pWrist != null
                ? pWrist.position - pElbow.position
                : pFist.position - pElbow.position;

            ProceduralRig.Aim(lower, hand, forearm);

            if (pWrist == null)
                return;

            // 手首の折り＝「前腕の向き」と「手首 → 拳の向き」の差。
            // ボーンのローカル軸はモデルごとに違うので、ワールドの回転差として掛ける。
            Vector3 handDir = pFist.position - pWrist.position;
            if (handDir.sqrMagnitude < 1e-8f || forearm.sqrMagnitude < 1e-8f)
                return;

            Quaternion bend = Quaternion.FromToRotation(forearm.normalized, handDir.normalized);
            hand.rotation = bend * hand.rotation;

            // ひねり（前腕の軸まわり）。プリミティブの骨は -Y が手足の向きなので、
            // ローカルの +Y 回転は前腕の軸まわりの逆回転になる。
            float twist = Mathf.DeltaAngle(0f, pWrist.localEulerAngles.y);
            if (Mathf.Abs(twist) > 0.01f)
                hand.Rotate(forearm.normalized, -twist, Space.World);
        }

        static void AimLeg(Transform thigh, Transform shin, Transform foot,
            Transform pHip, Transform pKnee, Transform pAnkle)
        {
            if (thigh == null || shin == null || foot == null ||
                pHip == null || pKnee == null || pAnkle == null)
                return;

            ProceduralRig.Aim(thigh, shin, pKnee.position - pHip.position);
            ProceduralRig.Aim(shin, foot, pAnkle.position - pKnee.position);
        }

        /// <summary>
        /// プリミティブ側の「根元からの向き」を VRM の初期姿勢に掛ける。
        /// ボーンのローカル軸はモデルごとに違うので、ワールドの差分で回す。
        /// </summary>
        static void CopyRotation(Transform vrm, Transform proxy, Quaternion restRel, Quaternion root)
        {
            if (vrm == null || proxy == null)
                return;

            Quaternion proxyRel = Quaternion.Inverse(root) * proxy.rotation;
            vrm.rotation = root * proxyRel * restRel;
        }
    }

    /// <summary>
    /// シーンに EnemyVrm を足す。シーンを作り直さなくても効くようにしてある。
    /// </summary>
    static class EnemyVrmBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var figures = Object.FindObjectsByType<EnemyFigure>(FindObjectsInactive.Exclude);

            for (int i = 0; i < figures.Length; i++)
            {
                var figure = figures[i];
                if (figure == null || figure.GetComponent<EnemyVrm>() != null)
                    continue;

                var vrm = figure.gameObject.AddComponent<EnemyVrm>();
                vrm.Figure = figure;
            }
        }
    }
}
