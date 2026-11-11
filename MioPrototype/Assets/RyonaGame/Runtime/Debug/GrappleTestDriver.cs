using System.Collections;
using UniVRM10;
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
    [DefaultExecutionOrder(30000)]
    public sealed class GrappleTestDriver : MonoBehaviour
    {
        /// <summary>次の掴みまでの秒数。</summary>
        public float Interval = 3.0f;

        /// <summary>掴ませる位置を真横にする（姿勢を横から見る確認用）。</summary>
        public bool SideView;

        /// <summary>掴ませる位置を背後にする。<c>-grapplefront</c> を付けたときだけ false。</summary>
        public bool BackView = true;

        /// <summary>押さえつけ（Pin）を正面から指示する（<c>-pintest</c>）。</summary>
        public bool PinTest;

        /// <summary>壁の前へ移動してから押さえつけを指示する（<c>-wallpintest</c>）。</summary>
        bool _wallPin;

        /// <summary><c>-overheadburst=</c> の接頭辞。頭上拘束の開始から連番で撮る。</summary>
        string _overheadBurstPrefix;
        bool _overheadBurstDone;

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

        /// <summary>バーストの枚数と間隔（<c>-heldpunchburstframes=</c> / <c>-heldpunchburstinterval=</c>）。</summary>
        int _burstFrames = 30;
        float _burstInterval = 0.05f;

        /// <summary>タッグパンチ中の撮影カメラを真横へ固定する（<c>-tagcam</c>）。</summary>
        bool _tagCam;

        /// <summary><c>-tagcamflip</c>。真横カメラを反対側へ回す（奥側の腕を見る）。</summary>
        bool _tagCamFlip;
        HeldBellyPunchDirector _belly;

        /// <summary><c>-pinshot=</c>。押さえつけ開始から 0.3 秒後を撮る。</summary>
        string _pinShotPath;
        float _pinShotDelay = -1f;

        /// <summary><c>-pinburst=</c> の接頭辞。押さえつけ開始から連番で撮る（膝蹴りまで含める）。</summary>
        string _pinBurstPrefix;
        bool _pinBurstDone;

        /// <summary>押さえつけ開始から撮影までの秒数（<c>-pinshotdelay=</c>）。</summary>
        float _pinShotDelaySeconds = 0.30f;
        bool _once;
        bool _faceLeft;
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
            bool faceLeft = false;
            bool hitAfterThrow = false;
            string hitShotPath = null;
            string heldPunchShotPath = null;
            float heldPunchShotDelay = 0.12f;
            string heldPunchBurst = null;
            int heldPunchBurstFrames = 30;
            float heldPunchBurstInterval = 0.05f;
            bool pin = false;
            bool wallPin = false;
            bool wallLog = false;
            string overheadBurst = null;
            string pinShotPath = null;
            float pinShotDelay = 0.30f;
            string pinBurst = null;
            bool tagCam = false;
            bool tagCamFlip = false;

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
                else if (a.StartsWith("-heldpunchburstframes=", System.StringComparison.OrdinalIgnoreCase))
                {
                    if (int.TryParse(a.Substring("-heldpunchburstframes=".Length), out int frames)
                        && frames > 0)
                        heldPunchBurstFrames = frames;
                }
                else if (a.StartsWith("-heldpunchburstinterval=", System.StringComparison.OrdinalIgnoreCase))
                {
                    if (float.TryParse(a.Substring("-heldpunchburstinterval=".Length),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float interval)
                        && interval > 0f)
                        heldPunchBurstInterval = interval;
                }
                else if (a.StartsWith("-heldpunchshotdelay=", System.StringComparison.OrdinalIgnoreCase))
                {
                    if (float.TryParse(a.Substring("-heldpunchshotdelay=".Length),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float delay) && delay >= 0f)
                        heldPunchShotDelay = delay;
                }
                else if (string.Equals(a, "-grappletest", System.StringComparison.OrdinalIgnoreCase))
                    wanted = true;
                else if (string.Equals(a, "-grappleleft", System.StringComparison.OrdinalIgnoreCase))
                    faceLeft = true;
                else if (string.Equals(a, "-grappleside", System.StringComparison.OrdinalIgnoreCase))
                    side = true;
                else if (string.Equals(a, "-grapplefront", System.StringComparison.OrdinalIgnoreCase))
                    front = true;   // 掴めないことを確かめる用（背後に回れていない状態）
                else if (string.Equals(a, "-grappleback", System.StringComparison.OrdinalIgnoreCase))
                    back = true;    // 既定と同じ。明示して読みやすくするため残す
                else if (string.Equals(a, "-pintest", System.StringComparison.OrdinalIgnoreCase))
                    pin = true;
                else if (string.Equals(a, "-wallpintest", System.StringComparison.OrdinalIgnoreCase))
                {
                    pin = true;
                    wallPin = true;
                }
                else if (string.Equals(a, "-walllog", System.StringComparison.OrdinalIgnoreCase))
                    wallLog = true;
                else if (string.Equals(a, "-overheadtest", System.StringComparison.OrdinalIgnoreCase))
                {
                    pin = true;
                    wallPin = true;
                }
                else if (a.StartsWith("-overheadburst=", System.StringComparison.OrdinalIgnoreCase))
                    overheadBurst = a.Substring("-overheadburst=".Length);
                else if (a.StartsWith("-pinshot=", System.StringComparison.OrdinalIgnoreCase))
                    pinShotPath = a.Substring("-pinshot=".Length);
                else if (a.StartsWith("-pinshotdelay=", System.StringComparison.OrdinalIgnoreCase))
                {
                    if (float.TryParse(a.Substring("-pinshotdelay=".Length),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float d) && d >= 0f)
                        pinShotDelay = d;
                }
                else if (a.StartsWith("-pinburst=", System.StringComparison.OrdinalIgnoreCase))
                    pinBurst = a.Substring("-pinburst=".Length);
                else if (string.Equals(a, "-tagcam", System.StringComparison.OrdinalIgnoreCase))
                    tagCam = true;
                else if (string.Equals(a, "-tagcamflip", System.StringComparison.OrdinalIgnoreCase))
                    tagCamFlip = true;
            }

            if (!wanted)
                return;

            var go = new GameObject("Grapple Test Driver");
            var driver = go.AddComponent<GrappleTestDriver>();
            driver.SideView = side;
            // 掴みは背後からしか成立しないので、既定は背後に立たせる。
            // 正面に立たせると「指示した敵が必ず空振りする」実行になってしまう。
            // 押さえつけ（Pin）は正面からなので、-pintest のときだけ正面に立たせる。
            driver.PinTest = pin;
            driver._wallPin = wallPin;
            driver._wallLog = wallLog;
            driver._overheadBurstPrefix = overheadBurst;
            driver.BackView = !pin && (back || !front);

            // 壁の前へ移すときは画面ロックを外す。ロックが残っていると、
            // 移した彼女が PlayerMotor に元の画面内へ戻され、敵が届かない。
            if (wallPin)
            {
                var gate = Object.FindFirstObjectByType<BeltScreenLock>();
                if (gate != null)
                    gate.enabled = false;
            }
            driver._hitShotPath = hitShotPath;
            driver._heldPunchShotPath = heldPunchShotPath;
            driver._heldPunchShotDelaySeconds = heldPunchShotDelay;
            driver._burstPrefix = heldPunchBurst;
            driver._burstFrames = heldPunchBurstFrames;
            driver._burstInterval = heldPunchBurstInterval;
            driver._pinShotPath = pinShotPath;
            driver._pinShotDelaySeconds = pinShotDelay;
            driver._pinBurstPrefix = pinBurst;
            driver._tagCam = tagCam;
            driver._tagCamFlip = tagCamFlip;
            driver._once = once;
            driver._faceLeft = faceLeft;
            driver._hitAfterThrowOnly = hitAfterThrow;
        }

        void Update()
        {
            // 押さえつけ開始から 0.3 秒後を撮る（-pinshot）
            if (_pinShotDelay >= 0f)
            {
                _pinShotDelay -= Time.unscaledDeltaTime;
                if (_pinShotDelay <= 0f && _pinShotPath != null)
                {
                    ScreenCapture.CaptureScreenshot(_pinShotPath);
                    Debug.Log($"[PinShot] {_pinShotPath}");
                    _pinShotPath = null;
                }
            }

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
                    _grapple.Started += kind =>
                    {
                        _grabbedOnce = true;
                        Debug.Log($"[GrappleTest] 掴まれた kind={kind}");
                        if (kind == GrappleKind.Pin && _pinShotPath != null)
                            _pinShotDelay = Mathf.Max(0f, _pinShotDelaySeconds);
                        if (kind == GrappleKind.Pin && _pinBurstPrefix != null && !_pinBurstDone)
                        {
                            _pinBurstDone = true;
                            StartCoroutine(PinBurstShots());
                        }
                        if (_grapple.IsWallPin && _overheadBurstPrefix != null
                            && !_overheadBurstDone)
                        {
                            _overheadBurstDone = true;
                            StartCoroutine(HoldBurstShots(_overheadBurstPrefix, 50));
                        }
                    };
                    _grapple.Escaped += () => { _holdFinished = true; Debug.Log("[GrappleTest] 振りほどけた"); };
                    _grapple.Failed += () => { _holdFinished = true; Debug.Log("[GrappleTest] 押さえつけが時間切れで解放"); };
                }

                _belly = FindFirstObjectByType<HeldBellyPunchDirector>();
                if (_belly != null)
                    _belly.Performed += OnBellyPunchPerformed;

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
                    LogWallClearance();
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

            // -wallpintest: 壁の前（レーン上）へ移動してから押さえつけを指示する。
            if (_wallPin)
            {
                // 関所が最後に決めた X の制限も外す（無効化だけでは値が残る）。
                var motor = _player.GetComponent<PlayerMotor>();
                if (motor != null)
                {
                    motor.ForwardLimitX = float.PositiveInfinity;
                    motor.BackLimitX = float.NegativeInfinity;
                }

                var site = WallPinSites.Nearest(_player.transform.position);
                if (site != null)
                {
                    var wallSpot = _player.transform.position;
                    wallSpot.x = site.CenterX;
                    wallSpot.z = 0f;
                    _player.transform.position = wallSpot;
                    _player.transform.rotation = Quaternion.LookRotation(Vector3.right);
                    Debug.Log($"[GrappleTest] 壁の前へ移動 x={wallSpot.x:F2} faceZ={site.FaceZ:F2}");
                }
            }

            if (_faceLeft)
                _player.transform.rotation = Quaternion.LookRotation(Vector3.left);

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

            if (PinTest)
            {
                // 押さえつけの確認中は、他の敵が背後から掴んで邪魔しないようにする。
                // （背後拘束は時間切れが無いので、一度掴まれると押さえつけの機会が無くなる）
                foreach (var other in FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None))
                {
                    if (other != null && other != target && other.IsAlive)
                        other.HoldFire(30f);
                }

                target.QueuePin();
                _orders++;
                Debug.Log($"[GrappleTest] #{_orders} {target.name} に押さえつけを指示 dist={best:F2}");
                return;
            }

            target.QueueGrab();

            _orders++;
            Debug.Log($"[GrappleTest] #{_orders} {target.name} に掴みを指示 dist={best:F2}");
        }

        /// <summary>
        /// 押さえつけ開始から 0.05 秒ごとに連番で撮る（<c>-pinburst=&lt;接頭辞&gt;</c>）。
        /// 膝蹴りは開始 1.5 秒後なので、2.0 秒ぶん撮って前後が分かるようにする。
        /// </summary>
        IEnumerator PinBurstShots()
        {
            int count = _wallPin ? 200 : 80;   // 壁版はレーンに戻るまで10秒撮る。
            const float interval = 0.05f;

            string dir = System.IO.Path.GetDirectoryName(_pinBurstPrefix);
            string name = System.IO.Path.GetFileNameWithoutExtension(_pinBurstPrefix);

            float next = Time.unscaledTime;
            for (int i = 0; i < count; i++)
            {
                while (Time.unscaledTime < next)
                    yield return null;

                string file = System.IO.Path.Combine(dir, $"{name}_{i:00}.png");
                ScreenCapture.CaptureScreenshot(file);
                next += interval;
            }

            Debug.Log($"[GrappleTest] pin burst {count} 枚 {_pinBurstPrefix}_XX.png");
        }

        /// <summary>
        /// 頭上拘束の開始から 0.05 秒ごとに連番で撮る（<c>-overheadburst=&lt;接頭辞&gt;</c>）。
        /// </summary>
        IEnumerator HoldBurstShots(string prefix, int count)
        {
            const float interval = 0.05f;

            string dir = System.IO.Path.GetDirectoryName(prefix);
            string name = System.IO.Path.GetFileNameWithoutExtension(prefix);

            float next = Time.unscaledTime;
            for (int i = 0; i < count; i++)
            {
                while (Time.unscaledTime < next)
                    yield return null;

                string file = System.IO.Path.Combine(dir, $"{name}_{i:00}.png");
                ScreenCapture.CaptureScreenshot(file);
                next += interval;
            }

            Debug.Log($"[GrappleTest] overhead burst {count} 枚 {prefix}_XX.png");
        }

        /// <summary>
        /// 壁押さえつけ中、体と髪が壁の表面を越えていないかを測る（<c>-walllog</c>）。
        /// 壁の表面は <c>FaceZ</c>。これより +Z 側にある点は壁の内側（貫通）。
        /// </summary>
        void LogWallClearance()
        {
            if (!_wallLog || _player == null || _grapple == null || !_grapple.IsWallPin)
                return;

            var site = WallPinSites.Nearest(_player.transform.position);
            if (site == null)
                return;

            float face = site.FaceZ;
            var anim = _player.GetComponentInChildren<CharacterAnimator>();
            var rig = anim != null ? anim.Rig : null;

            string bones = "rig=none";
            if (rig != null && rig.Ready)
            {
                bones = $"hips={rig.Hips.position.z - face:F3}" +
                        $" chest={rig.Chest.position.z - face:F3}" +
                        $" head={rig.Head.position.z - face:F3}" +
                        $" shL={rig.LUpperArm.position.z - face:F3}" +
                        $" shR={rig.RUpperArm.position.z - face:F3}" +
                        $" handL={rig.LHand.position.z - face:F3}" +
                        $" handR={rig.RHand.position.z - face:F3}" +
                        $" footL={rig.LFoot.position.z - face:F3}" +
                        $" footR={rig.RFoot.position.z - face:F3}";
            }

            // 髪のスプリング（VRM のジョイント）が壁を越えている数と、最も深いもの。
            int through = 0;
            float deepest = float.NegativeInfinity;
            float deepestSigned = 0f;
            var joints = _player.GetComponentsInChildren<VRM10SpringBoneJoint>(true);
            for (int i = 0; i < joints.Length; i++)
            {
                if (joints[i] == null)
                    continue;

                float d = joints[i].transform.position.z - face;
                if (d <= 0f)
                    continue;

                through++;
                if (d > deepest)
                {
                    deepest = d;
                    deepestSigned = d;
                }
            }

            Vector3 root = _player.transform.position;
            Debug.Log($"[WallClear] face={face:F2} rootZ={root.z:F2} rootOver={root.z - face:F3} " +
                      $"{bones} hairOver={through}/{joints.Length} hairMax={deepestSigned:F3} " +
                      $"captorZ={(_grapple.Captor != null ? _grapple.Captor.transform.position.z - face : 0f):F3}");

            // 骨ではなく、実際に描かれるメッシュの頂点で測る（骨が手前でも髪や頭の
            // 表面が壁へ入ることがあるため）。ここが正なら見た目で壁を貫通している。
            float meshZ = MaxSkinnedZ(_player.transform, face);
            if (!float.IsNaN(meshZ))
            {
                string verdict = meshZ > face ? "貫通" : "OK";
                Debug.Log($"[WallMesh] maxZ-face={meshZ - face:F3} ({verdict})");
            }
        }

        /// <summary>
        /// スキンメッシュを焼いて、全頂点の最大 Z（world）を返す。
        /// 壁押さえつけ中の見た目の最終判定に使う。
        /// </summary>
        float MaxSkinnedZ(Transform root, float face)
        {
            float maxZ = float.NegativeInfinity;
            bool any = false;
            var mesh = new Mesh();

            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                // 非アクティブ（VRM 読込後に切られる仮カプセル等）は描かれないので数えない。
                if (smr == null || !smr.enabled || !smr.gameObject.activeInHierarchy ||
                    smr.sharedMesh == null)
                    continue;

                smr.BakeMesh(mesh, true);
                Vector3[] verts = mesh.vertices;
                Matrix4x4 m = smr.transform.localToWorldMatrix;
                float localMax = float.NegativeInfinity;
                for (int i = 0; i < verts.Length; i++)
                {
                    float z = m.MultiplyPoint3x4(verts[i]).z;
                    if (z > localMax) localMax = z;
                }
                if (localMax > maxZ) maxZ = localMax;

                // どのメッシュが貫通しているか分かるよう、越えたものだけ名前を出す。
                if (localMax > face && Time.time - _meshLogAt > 1f)
                {
                    _meshLogAt = Time.time;
                    Debug.Log($"[WallMesh] {smr.name} maxZ-face={localMax - face:F3}");
                }
                any = true;
            }

            foreach (var mf in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (mf == null || !mf.enabled || !mf.gameObject.activeInHierarchy)
                    continue;

                if (mf.GetComponent<MeshFilter>() != null && mf.GetComponent<MeshFilter>().sharedMesh != null
                    && Time.time - _meshLogAt > 1f)
                {
                    float zz = mf.transform.position.z;
                    if (zz > face) { _meshLogAt = Time.time; Debug.Log($"[WallMesh] mesh {mf.name} z={zz - face:F3}"); }
                }

                var filter = mf.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null)
                    continue;

                {
                    Bounds bb = filter.sharedMesh.bounds;
                    Vector3 cc = mf.transform.TransformPoint(bb.center);
                    Vector3 ee = bb.extents;
                    float zOnly = cc.z +
                        Mathf.Abs(Vector3.Dot(mf.transform.right, Vector3.forward)) * ee.x +
                        Mathf.Abs(Vector3.Dot(mf.transform.up, Vector3.forward)) * ee.y +
                        Mathf.Abs(Vector3.Dot(mf.transform.forward, Vector3.forward)) * ee.z;
                    if (zOnly > face && Time.time - _meshLogAt > 1f)
                    {
                        _meshLogAt = Time.time;
                        Debug.Log($"[WallMesh] bounds {mf.name} mesh={filter.sharedMesh.name} " +
                                  $"maxZ-face={zOnly - face:F3} centerZ={cc.z:F2} ext=({ee.x:F2},{ee.y:F2},{ee.z:F2})");
                    }
                }

                Bounds b = filter.sharedMesh.bounds;
                Vector3 c = mf.transform.TransformPoint(b.center);
                Vector3 e = b.extents;
                float z = c.z + Mathf.Abs(mf.transform.lossyScale.z) * 0f +
                    Mathf.Abs(Vector3.Dot(mf.transform.right, Vector3.forward)) * e.x +
                    Mathf.Abs(Vector3.Dot(mf.transform.up, Vector3.forward)) * e.y +
                    Mathf.Abs(Vector3.Dot(mf.transform.forward, Vector3.forward)) * e.z;
                if (z > maxZ) maxZ = z;
                any = true;
            }

            Object.Destroy(mesh);
            return any ? maxZ : float.NaN;
        }

        /// <summary>壁とのクリアランスをログる（<c>-walllog</c>）。</summary>
        bool _wallLog;

        /// <summary>メッシュ名のログ間隔（1 秒）。</summary>
        float _meshLogAt;

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
            int count = Mathf.Max(1, _burstFrames);
            float interval = Mathf.Max(0.005f, _burstInterval);

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

        /// <summary>
        /// タッグパンチ中だけカメラを真横へ固定する（<c>-tagcam</c>）。
        /// ゲーム内カメラは腹と顔へ寄るため、殴る側の上体と肘が画面の外へ出て
        /// 「敵のモーション」を読めない。検証用に横から全身を写す。
        /// </summary>
        void LateUpdate()
        {
            if (!_tagCam || _belly == null || !_belly.IsPerforming || _player == null)
                return;

            var cam = Camera.main;
            if (cam == null)
                return;

            EnemyBrain puncher = null;
            foreach (var enemy in FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None))
            {
                if (enemy != null && enemy.IsTagPunch)
                {
                    puncher = enemy;
                    break;
                }
            }

            Vector3 center = _player.transform.position + Vector3.up * 1.0f;
            Vector3 axis = puncher != null
                ? puncher.transform.position - _player.transform.position
                : _player.transform.forward;
            axis.y = 0f;
            axis = axis.sqrMagnitude > 1e-4f
                ? axis.normalized
                : new Vector3(_player.transform.forward.x, 0f, _player.transform.forward.z).normalized;
            Vector3 side = Vector3.Cross(Vector3.up, axis).normalized;
            if (_tagCamFlip)
                side = -side;

            // 殴る側と彼女の両方が入るよう、中点を写して少し引く。
            if (puncher != null)
                center = Vector3.Lerp(center, puncher.transform.position + Vector3.up * 0.95f, 0.35f);
            cam.transform.position = center + side * 2.7f - axis * 0.15f + Vector3.up * 0.45f;
            cam.transform.LookAt(center);
        }

        void CaptureHit(HitResult result)        {
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
