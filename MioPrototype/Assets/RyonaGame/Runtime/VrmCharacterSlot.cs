using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UniVRM10;

namespace Ryona.Game
{
    /// <summary>
    /// カプセルの代わりに VRM を差し込む受け口。
    ///
    /// 使い方は「ファイルを置くだけ」:
    ///   1. VRoid Studio から VRM を書き出す
    ///   2. Assets/StreamingAssets/ に <see cref="FileName"/> の名前で置く
    ///   3. Play する
    ///
    /// ファイルが無ければカプセルのまま動くので、ゲームが壊れることはない。
    /// </summary>
    [AddComponentMenu("Ryona/VRM Character Slot")]
    public sealed class VrmCharacterSlot : MonoBehaviour
    {
        [Header("Source (Assets/StreamingAssets/ からの相対名)")]
        public string FileName = "mio_v1.vrm";

        [Header("Scene")]
        [Tooltip("VRM が来たら隠す仮ボディ（カプセル）")]
        public Transform Placeholder;

        [Tooltip("VRM をぶら下げる先。未指定ならこのオブジェクト直下")]
        public Transform Mount;

        [Header("Orientation")]
        [Tooltip("VRM の向き補正。正面が逆なら 180 にする")]
        public float YawOffset = 0f;

        [Tooltip("VRM の身長をプレイヤーの身長に合わせる（VRoid 出力はメートルなので通常 1）")]
        public float Scale = 1f;

        public GameObject Instance { get; private set; }
        public Vrm10Instance Vrm { get; private set; }
        public bool Loaded => Instance != null;

        /// <summary>読み込みが終わったとき（成功・失敗どちらでも）に発火。</summary>
        public event Action<bool> LoadFinished;

        // 破棄されたかどうか。<c>this == null</c> でも分かるが、
        // 読み込み中に破棄されたことを catch 節から明示的に見分けるために持つ。
        bool _disposed;

        void OnDestroy() => _disposed = true;

        public string FullPath =>
            string.IsNullOrEmpty(FileName)
                ? null
                : Path.Combine(Application.streamingAssetsPath, FileName);

        async void Start()
        {
            await LoadAsync();
        }

        public async Task<bool> LoadAsync()
        {
            string path = FullPath;

            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                Debug.LogWarning(
                    $"[Ryona] VRM が見つかりません: {path}\n" +
                    "         カプセルのまま続行します。VRM をこのパスに置けば次回から差し替わります。");
                LoadFinished?.Invoke(false);
                return false;
            }

            Vrm10Instance vrm = null;

            // 読み込みが終わるまで（数秒かかる）カプセルを見せない。
            // 失敗したときは、下で元に戻してカプセルのまま続ける。
            if (Placeholder != null)
                Placeholder.gameObject.SetActive(false);

            // 読み込み中（数秒）に彼女が見えないまま敵に殴られないよう、時間を止めて待つ。
            // 読み込みは時間と無関係に進む。タイトルが時間を止めているときは触らない。
            bool held = Placeholder != null && Time.timeScale > 0f;
            if (held)
            {
                Time.timeScale = 0f;
                LoadingScreen.Show();
            }

            // 敵の VRM と同時に読むと固まる。1 体ずつ読む。
            await VrmLoadGate.Semaphore.WaitAsync();

            try
            {
                // canLoadVrm0X: VRoid Studio の既定出力（VRM 0.x）もそのまま読める
                vrm = await Vrm10.LoadPathAsync(
                    path,
                    canLoadVrm0X: true,
                    showMeshes: true);
            }
            catch (Exception e)
            {
                // 読み込みの途中でシーンが捨てられると、UniGLTF が自分で作った
                // Transform に触れて MissingReferenceException を投げる。
                // 破棄済みなら想定内なので、エラーではなく警告に落とす
                // （再開・やり直しのたびにエラーログが赤く出るのを防ぐ）。
                if (_disposed || this == null)
                    Debug.LogWarning($"[Ryona] シーン破棄に伴い VRM の読み込みを取りやめました: {path}");
                else
                    Debug.LogError($"[Ryona] VRM の読み込みに失敗: {path}\n{e}");
            }
            finally
            {
                VrmLoadGate.Semaphore.Release();

                if (held)
                    LoadingScreen.Hide();

                // タイトル・一時停止が止めているなら、そちらに任せる
                // （ここで 1 に戻すと、メニューを開いたままゲームが再開する）
                if (held && Time.timeScale == 0f
                    && FindFirstObjectByType<TitleGate>() == null
                    && !PauseMenu.IsPaused)
                    Time.timeScale = 1f;
            }

            if (vrm == null)
            {
                if (!_disposed && this != null && Placeholder != null)
                    Placeholder.gameObject.SetActive(true);

                LoadFinished?.Invoke(false);
                return false;
            }

            // await の間にシーンが破棄されることがある（シーン切り替え・テスト）。
            // 破棄済みなら読み込んだ VRM を片付けて静かに終わる。
            if (_disposed || this == null)
            {
                if (vrm != null)
                    Destroy(vrm.gameObject);
                return false;
            }

            Vrm = vrm;
            Instance = vrm.gameObject;

            var mount = Mount != null ? Mount : transform;
            Instance.transform.SetParent(mount, false);
            Instance.transform.localPosition = Vector3.zero;
            Instance.transform.localRotation = Quaternion.Euler(0f, YawOffset, 0f);
            Instance.transform.localScale = Vector3.one * Scale;

            // 当たり判定は CharacterController 側で持つので、VRM のコライダーは切る
            foreach (var col in Instance.GetComponentsInChildren<Collider>(true))
                col.enabled = false;

            // 手続きポーズで体が大きく動く（倒れると腰が 0.7m 下がる）ので、
            // 既定の境界のままだと画面内でもカリングされて消えることがある
            foreach (var smr in Instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                smr.updateWhenOffscreen = true;

            // T ポーズのままなので、リグを組んで立ち姿勢にする
            var rig = Instance.AddComponent<ProceduralRig>();
            rig.Capture();

            // 読み込んだ直後のモデルは休止姿勢（T ポーズ）。CharacterAnimator の Start は
            // 翌フレームの頭なので最初の描画に間に合わず、T ポーズが 1 フレーム混ざる。
            // 姿勢を掛け終わるまで描画を止めて、その隙間を消す。
            var renderers = Instance.GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in renderers)
                renderer.enabled = false;

            var animator = Instance.AddComponent<CharacterAnimator>();
            animator.Rig = rig;
            animator.Stats = GetComponent<PlayerStats>();

            // 倒れたときだけ髪の重力を弱めて、前髪が顔を覆わないようにする
            if (Instance.GetComponent<VrmHairSpring>() == null)
                Instance.AddComponent<VrmHairSpring>();

            // 殴られたら顔を歪ませる（眉・口・目を重ねて苦悶を作る）
            var face = Instance.AddComponent<PlayerFace>();
            face.Vrm = vrm;
            face.Stats = GetComponent<PlayerStats>();
            face.Anim = animator;

            // Start を待たずに、ここで即座に姿勢を掛ける
            animator.ApplyPose();

            foreach (var renderer in renderers)
            {
                if (renderer != null)
                    renderer.enabled = true;
            }

            if (Placeholder != null)
                Placeholder.gameObject.SetActive(false);

            Debug.Log($"[Ryona] VRM を差し込みました: {FileName}");
            LoadFinished?.Invoke(true);
            return true;
        }

        /// <summary>表情を切り替える。happy / angry / sad / relaxed / surprised など。</summary>
        public void SetExpression(string presetName, float weight)
        {
            if (Vrm == null || string.IsNullOrEmpty(presetName))
                return;

            if (!Enum.TryParse(presetName, true, out ExpressionPreset preset))
                return;

            Vrm.Runtime.Expression.SetWeight(new ExpressionKey(preset), Mathf.Clamp01(weight));
        }

        /// <summary>表情の合成をリセットする。</summary>
        public void ClearExpressions()
        {
            if (Vrm == null)
                return;

            Vrm.Runtime.Expression.SetWeight(ExpressionKey.Happy, 0f);
            Vrm.Runtime.Expression.SetWeight(ExpressionKey.Angry, 0f);
            Vrm.Runtime.Expression.SetWeight(ExpressionKey.Sad, 0f);
            Vrm.Runtime.Expression.SetWeight(ExpressionKey.Relaxed, 0f);
            Vrm.Runtime.Expression.SetWeight(ExpressionKey.Surprised, 0f);
        }
    }
}
