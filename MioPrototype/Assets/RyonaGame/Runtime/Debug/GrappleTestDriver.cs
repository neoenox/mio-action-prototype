using System.Collections;
using UnityEngine;

namespace Ryona.Game
{
    /// <summary>
    /// 掴み・もがきの通し確認（<c>-grappletest</c>）。
    ///
    /// 数秒ごとに一番近い敵をプレイヤーの<b>背後</b> 1m に立たせ、掴みを指示する。
    /// 掴みは背後からしか成立しないので、既定は背後。正面に立たせたいときは
    /// <c>-grapplefront</c>、真横は <c>-grappleside</c>。
    /// <c>-holdmash</c> を付ければ振りほどき、付けなければ時間切れ（投げ）を確認できる。
    /// </summary>
    [AddComponentMenu("Ryona/Grapple Test Driver")]
    public sealed class GrappleTestDriver : MonoBehaviour
    {
        /// <summary>次の掴みまでの秒数。</summary>
        public float Interval = 3.0f;

        /// <summary>掴ませる位置を真横にする（姿勢を横から見る確認用）。</summary>
        public bool SideView;

        /// <summary>掴ませる位置を背後にする。<c>-grapplefront</c> を付けたときだけ false。</summary>
        public bool BackView = true;

        float _timer = 1.5f;
        PlayerGrapple _grapple;
        PlayerStats _player;
        int _orders;

        // 掴んでいる間の接触を測る（敵の手首が彼女のどこに乗っているか）
        VrmCharacterSlot _slot;
        float _contactTimer;
        string _hitShotPath;
        string _heldPunchShotPath;
        float _heldPunchShotDelay = -1f;

        /// <summary>腹パン開始から撮影までの秒数（`-heldpunchshotdelay=` で差し替え）。</summary>
        float _heldPunchShotDelaySeconds = 0.12f;

        /// <summary><c>-heldpunchburst=</c> の接頭辞。コンボ開始から連番で撮る。</summary>
        string _burstPrefix;
        bool _burstDone;
        bool _once;
        bool _grabbedOnce;
        bool _hitAfterThrowOnly;
        bool _holdFinished;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            bool wanted = false;
            bool side = false;
            bool front = false;
            bool back = false;
            bool once = false;
            bool hitAfterThrow = false;
            string hitShotPath = null;
            string heldPunchShotPath = null;
            float heldPunchShotDelay = 0.12f;
            string heldPunchBurst = null;

            foreach (string a in System.Environment.GetCommandLineArgs())
            {
                if (string.Equals(a, "-grappleonce", System.StringComparison.OrdinalIgnoreCase))
                    once = true;
                else if (string.Equals(a, "-enemyhitafterthrow", System.StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(a, "-enemyhitafterhold", System.StringComparison.OrdinalIgnoreCase))
                    hitAfterThrow = true;
                else if (a.StartsWith("-enemyhitshot=", System.StringComparison.OrdinalIgnoreCase))
                    hitShotPath = a.Substring("-enemyhitshot=".Length);
                else if (a.StartsWith("-heldpunchshot=", System.StringComparison.OrdinalIgnoreCase))
                    heldPunchShotPath = a.Substring("-heldpunchshot=".Length);
                else if (a.StartsWith("-heldpunchburst=", System.StringComparison.OrdinalIgnoreCase))
                    heldPunchBurst = a.Substring("-heldpunchburst=".Length);
                else if (a.StartsWith("-heldpunchshotdelay=", System.StringComparison.OrdinalIgnoreCase))
                {
                    if (float.TryParse(a.Substring("-heldpunchshotdelay=".Length),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float delay) && delay >= 0f)
                        heldPunchShotDelay = delay;
                }
                else if (string.Equals(a, "-grappletest", System.StringComparison.OrdinalIgnoreCase))
                    wanted = true;
                else if (string.Equals(a, "-grappleside", System.StringComparison.OrdinalIgnoreCase))
                    side = true;
                else if (string.Equals(a, "-grapplefront", System.StringComparison.OrdinalIgnoreCase))
                    front = true;   // 掴めないことを確かめる用（背後に回れていない状態）
                else if (string.Equals(a, "-grappleback", System.StringComparison.OrdinalIgnoreCase))
                    back = true;    // 既定と同じ。明示して読みやすくするため残す
            }

            if (!wanted)
                return;

            var go = new GameObject("Grapple Test Driver");
            var driver = go.AddComponent<GrappleTestDriver>();
            driver.SideView = side;
            // 掴みは背後からしか成立しないので、既定は背後に立たせる。
            // 正面に立たせると「指示した敵が必ず空振りする」実行になってしまう。
            driver.BackView = back || !front;
            driver._hitShotPath = hitShotPath;
            driver._heldPunchShotPath = heldPunchShotPath;
            driver._heldPunchShotDelaySeconds = heldPunchShotDelay;
            driver._burstPrefix = heldPunchBurst;
            driver._once = once;
            driver._hitAfterThrowOnly = hitAfterThrow;
        }

        void Update()
        {
            // 腹パン演出の撮影（演出開始から 0.12 秒後）
            if (_heldPunchShotDelay >= 0f)
            {
                _heldPunchShotDelay -= Time.unscaledDeltaTime;
                if (_heldPunchShotDelay <= 0f && _heldPunchShotPath != null)
                {
                    ScreenCapture.CaptureScreenshot(_heldPunchShotPath);
                    Debug.Log($"[HeldPunchShot] {_heldPunchShotPath}");
                    _heldPunchShotPath = null;
                }
            }

            if (_grapple == null)
            {
                _grapple = FindFirstObjectByType<PlayerGrapple>();
                _player = FindFirstObjectByType<PlayerStats>();

                if (_grapple != null)
                {
                    if (_player != null && _hitShotPath != null)
                        _player.HitReceived += CaptureHit;
                    _grapple.Started += kind => { _grabbedOnce = true; Debug.Log($"[GrappleTest] 掴まれた kind={kind}"); };
                    _grapple.Escaped += () => { _holdFinished = true; Debug.Log("[GrappleTest] 振りほどけた"); };
                    _grapple.Failed += () => { _holdFinished = true; Debug.Log("[GrappleTest] 振りほどけなかった"); };
                }

                var belly = FindFirstObjectByType<HeldBellyPunchDirector>();
                if (belly != null)
                    belly.Performed += OnBellyPunchPerformed;

                return;
            }

            if (_player == null || _player.IsDead)
                return;

            // 掴まれている間は「敵の手が彼女のどこに乗っているか」を測る
            if (_grapple.IsGrappled)
            {
                _contactTimer -= Time.deltaTime;
                if (_contactTimer <= 0f)
                {
                    _contactTimer = 0.25f;
                    LogContact();
                }
                return;
            }

            if (_once && _grabbedOnce)
                return;

            _timer -= Time.deltaTime;
            if (_timer > 0f)
                return;

            // もがいている最中は次を指示しない
            if (_grapple.IsGrappled)
                return;

            _timer = Mathf.Max(0.5f, Interval);

            EnemyBrain target = null;
            float best = float.MaxValue;

            foreach (var enemy in FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None))
            {
                if (enemy == null || !enemy.IsAlive || enemy.IsRising)
                    continue;

                float distance = Vector3.Distance(enemy.transform.position, _player.transform.position);
                if (distance >= best)
                    continue;

                best = distance;
                target = enemy;
            }

            if (target == null)
                return;

            // 正面 1m（-grappleside なら右真横 1m）に立たせて、掴みを指示する
            Vector3 forward = _player.transform.forward;
            forward.y = 0f;
            forward = forward.sqrMagnitude > 1e-4f ? forward.normalized : Vector3.forward;

            Vector3 place = SideView ? _player.transform.right : (BackView ? -forward : forward);
            place.y = 0f;
            place = place.sqrMagnitude > 1e-4f ? place.normalized : forward;

            Vector3 spot = _player.transform.position + place * 1.0f;
            spot.y = target.transform.position.y;   // 高さは元のまま（床に埋めない）

            target.transform.position = spot;
            target.transform.rotation = Quaternion.LookRotation(-place, Vector3.up);
            target.QueueGrab();

            _orders++;
            Debug.Log($"[GrappleTest] #{_orders} {target.name} に掴みを指示 dist={best:F2}");
        }

        void OnBellyPunchPerformed()
        {
            Debug.Log("[GrappleTest] 腹パン演出 開始");

            if (_burstPrefix != null && !_burstDone)
            {
                _burstDone = true;
                StartCoroutine(BurstShots());
            }

            if (_heldPunchShotPath != null && _heldPunchShotDelay < 0f)
                _heldPunchShotDelay = Mathf.Max(0f, _heldPunchShotDelaySeconds);
        }

        /// <summary>
        /// コンボ開始から 0.05 秒ごとに連番で撮る（<c>-heldpunchburst=&lt;接頭辞&gt;</c>）。
        /// 1 枚ずつ別プロセスで撮ると位相がずれて、パンチの振りが確認できないため。
        /// </summary>
        IEnumerator BurstShots()
        {
            const int count = 30;
            const float interval = 0.05f;

            string dir = System.IO.Path.GetDirectoryName(_burstPrefix);
            string name = System.IO.Path.GetFileNameWithoutExtension(_burstPrefix);

            float next = Time.unscaledTime;
            for (int i = 0; i < count; i++)
            {
                while (Time.unscaledTime < next)
                    yield return null;

                string file = System.IO.Path.Combine(dir, $"{name}_{i:00}.png");
                ScreenCapture.CaptureScreenshot(file);
                next += interval;
            }

            Debug.Log($"[GrappleTest] burst {count} 枚 {_burstPrefix}_XX.png");
            yield return new WaitForSecondsRealtime(0.6f);
            Application.Quit();
        }

        void CaptureHit(HitResult result)
        {
            if (_hitShotPath == null || (_hitAfterThrowOnly && !_holdFinished)) return;
            ScreenCapture.CaptureScreenshot(_hitShotPath);
            Debug.Log($"[EnemyHitShot] {_hitShotPath}");
            _hitShotPath = null;
        }

        /// <summary>
        /// 掴んでいる間の接触。敵の手首と彼女の肩・胸の距離を、敵のローカル座標で出す。
        /// ローカルなので z = 前方の距離、x = 左右、y = 高さ。実行ごとに比べられる。
        /// </summary>
        void LogContact()
        {
            var captor = _grapple.Captor;
            if (captor == null)
                return;

            var figure = captor.GetComponent<EnemyFigure>();
            if (figure == null)
                return;

            if (_slot == null)
                _slot = FindFirstObjectByType<VrmCharacterSlot>();
            if (_slot == null || !_slot.Loaded || _slot.Instance == null)
                return;

            var rig = _slot.Instance.GetComponent<ProceduralRig>();
            if (rig == null || !rig.Ready)
                return;

            var t = captor.transform;

            Vector3 wr = t.InverseTransformPoint(figure.WristBone(false).position);
            Vector3 wl = t.InverseTransformPoint(figure.WristBone(true).position);
            Vector3 chest = t.InverseTransformPoint(rig.Chest.position);
            Vector3 shR = t.InverseTransformPoint(rig.RUpperArm.position);
            Vector3 shL = t.InverseTransformPoint(rig.LUpperArm.position);
            Vector3 root = t.InverseTransformPoint(_player.transform.position);

            Vector3 handR = t.InverseTransformPoint(rig.RHand.position);
            Vector3 handL = t.InverseTransformPoint(rig.LHand.position);

            float dR = Mathf.Min(Vector3.Distance(wr, shR), Vector3.Distance(wr, chest));
            float dL = Mathf.Min(Vector3.Distance(wl, shL), Vector3.Distance(wl, chest));
            float aR = Mathf.Min(Vector3.Distance(wr, handR), Vector3.Distance(wr, chest));
            float aL = Mathf.Min(Vector3.Distance(wl, handL), Vector3.Distance(wl, chest));

            Debug.Log($"[GrappleContact] rootZ={root.z:F2} " +
                      $"wristR=({wr.x:F2},{wr.y:F2},{wr.z:F2}) wristL=({wl.x:F2},{wl.y:F2},{wl.z:F2}) " +
                      $"chest=({chest.x:F2},{chest.y:F2},{chest.z:F2}) " +
                      $"shR=({shR.x:F2},{shR.y:F2},{shR.z:F2}) shL=({shL.x:F2},{shL.y:F2},{shL.z:F2}) " +
                      $"herHandR=({handR.x:F2},{handR.y:F2},{handR.z:F2}) herHandL=({handL.x:F2},{handL.y:F2},{handL.z:F2}) " +
                      $"dR={dR:F2} dL={dL:F2} gripR={aR:F2} gripL={aL:F2}");
        }
    }
}
