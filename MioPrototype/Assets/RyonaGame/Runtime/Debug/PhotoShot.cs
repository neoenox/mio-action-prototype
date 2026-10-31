using System;
using UnityEngine;

namespace Ryona.Game
{
    /// <summary>
    /// 検証用の自動撮影。<c>-shotpath</c> を付けて起動すると、指定の状態にしてから
    /// 画面を PNG に保存して終了する。
    ///
    /// エディタ上で撮る撮影と違い、実際に時間を進めてから撮るので
    /// 髪のバネ（スプリングボーン）が落ち着いた状態を見られる。
    /// 引数が無ければ何もしない。
    ///
    /// 使い方:
    ///   MioPrototype.exe -shotpath out.png -shotstate Down -shotcam face
    ///   -shotstate  Idle / Down / Guard / Stagger / Attack
    ///   -shotcam    game（ゲームのカメラのまま） / face（顔の正面に回り込む）
    ///   -shotwarmup VRM の読み込みを待つ秒数
    ///   -shotsettle 状態を入れてから撮るまでの秒数（髪が落ち着くのを待つ）
    /// </summary>
    [AddComponentMenu("Ryona/Photo Shot")]
    public sealed class PhotoShot : MonoBehaviour
    {
        public string Path = "shot.png";
        public CharacterAnimator.State State = CharacterAnimator.State.Down;

        [Tooltip("true なら顔の正面にカメラを回り込ませる")]
        public bool FaceCamera;

        [Tooltip("頭からのワールド座標オフセット。指定すると頭の向きに関係なくその位置から撮る")]
        public Vector3 Offset;
        public bool UseOffset;

        public float Fov = 32f;

        [Tooltip("髪のメッシュを消して撮る（顔を隠している物の切り分け用）")]
        public bool HideHair;

        [Tooltip("主人公以外のオブジェクトを消して撮る")]
        public bool OnlyCharacter;

        [Tooltip("敵を消さずに撮る（敵の見た目の確認用）")]
        public bool ShowEnemy;

        [Tooltip("撮影用。0〜1 で武器の発光量を固定する（予備動作の発光を確認する）")]
        public float DebugCharge = -1f;

        [Tooltip("撮影用。被弾フラッシュを出した状態で撮る")]
        public bool FlashShot;

        [Tooltip("撮影用。苦悶の表情の濃さを固定する（0-1、負で通常）")]
        public float Pain = -1f;

        [Tooltip("撮影用。敵を振りかぶりの形で止める")]
        public bool EnemyWindup;

        [Tooltip("撮影用。敵を振り抜きの任意コマ（経過秒）で止める。負で通常")]
        public float EnemySwing = -1f;

        [Tooltip("撮影用。表情の配合を上書きする（負で既定値）。-shotangry/-shotsad/-shotmouth/-shotblink")]
        public float FaceAngry = -1f;
        public float FaceSad = -1f;
        public float FaceMouth = -1f;
        public float FaceGrit = -1f;
        public float FaceBlink = -1f;

        [Tooltip("状態を入れてから撮るまでの待ち時間。髪が落ち着くのを待つ")]
        public float SettleSeconds = 2.5f;

        [Tooltip("状態の経過時間。アニメーション途中のコマを撮るとき用")]
        public float StateTime = 5f;

        [Tooltip("状態の長さ。アニメーション途中のコマを撮るとき用")]
        public float StateDuration = 999f;

        [Tooltip("起動から状態を入れるまでの待ち時間。VRM の読み込みを待つ")]
        public float WarmupSeconds = 2.5f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            string[] args = Environment.GetCommandLineArgs();
            string path = Value(args, "-shotpath");
            if (string.IsNullOrEmpty(path))
                return;

            var go = new GameObject("[PhotoShot]");
            DontDestroyOnLoad(go);

            var shot = go.AddComponent<PhotoShot>();
            shot.Path = path;
            shot.State = ParseState(Value(args, "-shotstate"), CharacterAnimator.State.Down);
            shot.FaceCamera = string.Equals(Value(args, "-shotcam"), "face", StringComparison.OrdinalIgnoreCase);

            string offset = Value(args, "-shotoffset");
            if (!string.IsNullOrEmpty(offset))
            {
                string[] parts = offset.Split(',');
                if (parts.Length == 3 &&
                    float.TryParse(parts[0], out float ox) &&
                    float.TryParse(parts[1], out float oy) &&
                    float.TryParse(parts[2], out float oz))
                {
                    shot.Offset = new Vector3(ox, oy, oz);
                    shot.UseOffset = true;
                }
            }

            if (float.TryParse(Value(args, "-shotfov"), out float fov))
                shot.Fov = fov;

            shot.HideHair = Value(args, "-shothidehair") == "1";
            shot.OnlyCharacter = Value(args, "-shotonly") == "1";
            shot.ShowEnemy = Value(args, "-shotenemy") == "1";

            shot.EnemyWindup = Value(args, "-shotenemywindup") == "1";
            if (float.TryParse(Value(args, "-shotenemyswing"), out float eswing))
                shot.EnemySwing = eswing;

            if (float.TryParse(Value(args, "-shotcharge"), out float charge))
                shot.DebugCharge = charge;

            shot.FlashShot = Value(args, "-shotflash") == "1";

            if (float.TryParse(Value(args, "-shotangry"), out float ang))
                shot.FaceAngry = ang;
            if (float.TryParse(Value(args, "-shotsad"), out float sad))
                shot.FaceSad = sad;
            if (float.TryParse(Value(args, "-shotmouth"), out float mouth))
                shot.FaceMouth = mouth;
            if (float.TryParse(Value(args, "-shotih"), out float grit))
                shot.FaceGrit = grit;
            if (float.TryParse(Value(args, "-shotblink"), out float blink))
                shot.FaceBlink = blink;

            if (float.TryParse(Value(args, "-shotpain"), out float pain))
                shot.Pain = pain;

            if (float.TryParse(Value(args, "-shotwarmup"), out float warmup))
                shot.WarmupSeconds = warmup;
            if (float.TryParse(Value(args, "-shotsettle"), out float settle))
                shot.SettleSeconds = settle;
            if (float.TryParse(Value(args, "-shottime"), out float shotTime))
                shot.StateTime = shotTime;
            if (float.TryParse(Value(args, "-shotdur"), out float shotDur))
                shot.StateDuration = shotDur;
        }

        System.Collections.IEnumerator Start()
        {
            // VRM の読み込みを待つ前に敵を止める。
            // 待っている間に殴られると、倒れた姿勢や怯みのまま撮れてしまう。
            foreach (var brain in FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None))
                brain.enabled = false;

            if (!ShowEnemy)
            {
                foreach (var figure in FindObjectsByType<EnemyFigure>(FindObjectsSortMode.None))
                    figure.gameObject.SetActive(false);
            }

            yield return new WaitForSeconds(Mathf.Max(0.1f, WarmupSeconds));

            var anims = FindObjectsByType<CharacterAnimator>(FindObjectsSortMode.None);
            if (anims == null || anims.Length == 0)
            {
                Debug.LogError("[Shot] CharacterAnimator が見つからない");
                Application.Quit();
                yield break;
            }

            // 待っている間の被弾を消す（スタンが残っていると、指定した姿勢にならない）
            foreach (var stats in anims[0].GetComponentsInParent<PlayerStats>())
                stats.RestoreAll();

            // 体も消す。倒れた子の目の前に立っていて、カメラを塞ぐため
            foreach (var brain in FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None))
                brain.enabled = false;

            foreach (var figure in FindObjectsByType<EnemyFigure>(FindObjectsSortMode.None))
            {
                if (ShowEnemy)
                {
                    figure.Frozen = true;   // 近づいてこないように、その場で止める
                    if (DebugCharge >= 0f)
                        figure.DebugCharge = DebugCharge;

                    // 攻撃モーションの任意コマを止めて撮る（振りかぶり／振り抜き）
                    if (EnemyWindup || EnemySwing >= 0f)
                    {
                        if (EnemyWindup)
                            figure.Preview(EnemyPhase.Windup, 1f, EnemyBrain.AttackId.Heavy, 0f);
                        else
                            figure.Preview(EnemyPhase.Recover, 1f, EnemyBrain.AttackId.Heavy, 0f, EnemySwing);

                        var weapon = figure.WeaponPivot;
                        if (weapon != null)
                        {
                            // 体の正面方向への張り出しで「踏み込んだか」を測る
                            Vector3 local = figure.transform.InverseTransformPoint(weapon.position);
                            Debug.Log($"[Shot] enemy swing={EnemySwing:F3} windup={EnemyWindup} " +
                                      $"weaponLocal={local.x:F3},{local.y:F3},{local.z:F3}");
                        }
                    }
                }
                else
                    figure.gameObject.SetActive(false);
            }

            if (FaceCamera || UseOffset)
            {
                foreach (var tpc in FindObjectsByType<ThirdPersonCamera>(FindObjectsSortMode.None))
                    tpc.enabled = false;
            }

            if (OnlyCharacter)
            {
                var root = anims[0].transform.root;
                int kept = 0;
                foreach (var go in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
                {
                    if (go == root ||
                        go.GetComponentInChildren<Camera>(true) != null ||
                        go.GetComponentInChildren<Light>(true) != null ||
                        go == gameObject)
                    {
                        kept++;
                        continue;
                    }

                    go.SetActive(false);
                }

                Debug.Log($"[Shot] only character kept={kept}");
            }

            if (HideHair)
            {
                int hidden = 0;
                foreach (var r in anims[0].GetComponentsInChildren<Renderer>(true))
                {
                    bool isHair = r.gameObject.name.ToLowerInvariant().Contains("hair");
                    foreach (var m in r.sharedMaterials)
                    {
                        if (m != null && m.name.ToLowerInvariant().Contains("hair"))
                            isHair = true;
                    }

                    if (isHair)
                    {
                        r.enabled = false;
                        hidden++;
                    }
                }
                Debug.Log($"[Shot] hid hair renderers={hidden}");
            }

            // 頭の近くにあるメッシュを列挙する（何が顔を隠しているか特定するため）
            {
                var rig0 = anims[0].Rig;
                if (rig0 != null && rig0.Ready)
                {
                    Vector3 hp = rig0.Head.position;
                    foreach (var r in anims[0].GetComponentsInChildren<Renderer>(true))
                    {
                        if (Vector3.Distance(r.bounds.center, hp) > 0.45f)
                            continue;

                        var mats = new System.Text.StringBuilder();
                        foreach (var m in r.sharedMaterials)
                            mats.Append(m != null ? m.name : "null").Append('|');

                        Debug.Log($"[Mesh] {r.gameObject.name} enabled={r.enabled} " +
                                  $"dist={Vector3.Distance(r.bounds.center, hp):F2} mats={mats}");
                    }
                }
            }

            // 落ち着いた状態を見たいので、長い時間で固定する
            for (int i = 0; i < anims.Length; i++)
            {
                Debug.Log($"[Shot] animator[{i}] name={anims[i].name} pos={anims[i].transform.position} " +
                          $"rig={anims[i].Rig != null} ready={(anims[i].Rig != null && anims[i].Rig.Ready)}");

                anims[i].ForceState(State, StateTime, StateDuration, StaggerTier.GuardBreak);
            }

            // 表情を固定して撮る（苦悶の濃さの確認用）
            if (Pain >= 0f)
            {
                // 撮影中は一人しか写さないので、animator の持ち主だけ見れば足りる。
                // （FindObjectsByType は非アクティブを拾わないため、これで確実に取る）
                var face = anims[0].GetComponent<PlayerFace>();
                if (face == null)
                    face = anims[0].GetComponentInChildren<PlayerFace>();

                if (face != null)
                {
                    if (FaceAngry >= 0f) face.PainAngry = FaceAngry;
                    if (FaceSad >= 0f) face.PainSad = FaceSad;
                    if (FaceMouth >= 0f) face.PainMouth = FaceMouth;
                    if (FaceGrit >= 0f) face.PainGrit = FaceGrit;
                    if (FaceBlink >= 0f) face.PainBlink = FaceBlink;

                    face.PreviewAmount = Pain;
                    Debug.Log($"[Shot] face {face.name} pain={Pain:F2} " +
                              $"angry={face.PainAngry:F2} sad={face.PainSad:F2} " +
                              $"mouth={face.PainMouth:F2} grit={face.PainGrit:F2} " +
                              $"blink={face.PainBlink:F2}");
                }
                else
                {
                    Debug.LogWarning("[Shot] PlayerFace が見つからない");
                }
            }

            // 髪のバネが落ち着くまで待つ。途中で状態を上書きされても戻せるよう毎フレーム入れ直す
            float end = Time.time + Mathf.Max(0.2f, SettleSeconds);
            float nextTick = 0f;
            while (Time.time < end)
            {
                for (int i = 0; i < anims.Length; i++)
                    anims[i].ForceState(State, StateTime, StateDuration, StaggerTier.GuardBreak, false);

                if (UseOffset)
                    PlaceCameraAtOffset(anims[0], Offset, Fov);
                else if (FaceCamera)
                    PlaceFaceCamera(anims[0], Fov);

                if (Time.time >= nextTick)
                {
                    nextTick = Time.time + 0.5f;
                    var tpc = FindFirstObjectByType<ThirdPersonCamera>();
                    string tpcInfo = tpc == null
                        ? "none"
                        : $"enabled={tpc.enabled} target={(tpc.Target != null ? tpc.Target.name : "null")} " +
                          $"pos={tpc.transform.position} dist={tpc.Distance}";
                    Debug.Log($"[Shot] tick t={Time.time:F2} remain={(end - Time.time):F2} " +
                              $"state={anims[0].Current} tpc[{tpcInfo}]");
                }

                yield return null;
            }

            Debug.Log("[Shot] settle done");

            var cam = Camera.main;
            for (int i = 0; i < anims.Length; i++)
            {
                var rig = anims[i].Rig;
                string head = "none";
                if (rig != null && rig.Ready && rig.Head != null)
                {
                    var h = rig.Head;
                    float upAngle = Vector3.Angle(h.forward, Vector3.up);
                    head = $"fwd={h.forward} upAngle={upAngle:F0} yaw={h.eulerAngles.y:F0}";
                }

                Debug.Log($"[Shot] at capture animator[{i}] state={anims[i].Current} " +
                          $"hipsY={(rig != null && rig.Ready ? rig.Hips.position.y : -1f):F3} " +
                          $"headY={(rig != null && rig.Ready ? rig.Head.position.y : -1f):F3} " +
                          $"{head} cam={(cam != null ? cam.transform.position.ToString() : "none")}");
            }

            string full = System.IO.Path.GetFullPath(Path);

            // 診断: プレイヤーの近くにある見た目を列挙する（正体不明の物体の特定用）
            if (Value(System.Environment.GetCommandLineArgs(), "-shotnear") == "1")
            {
                Vector3 center = anims[0].transform.position;
                foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                {
                    float d = Vector3.Distance(r.bounds.center, center + Vector3.up * 1.0f);
                    if (d < 3f)
                        Debug.Log($"[Near] {r.name} root={r.transform.root.name} d={d:F2} " +
                                  $"pos={r.transform.position} size={r.bounds.size}");
                }

                // 敵の状態（間合い・攻撃の可否）を確認する
                var stats = anims[0].transform.root.GetComponent<PlayerStats>();
                if (stats != null && stats.State != null)
                    Debug.Log($"[Ai] hp={stats.State.Hp:F1} stamina={stats.State.Stamina:F1} " +
                              $"down={stats.State.IsDown} dead={stats.IsDead} t={Time.time:F1}");

                foreach (var brain in FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None))
                {
                    float dist = Vector3.Distance(brain.transform.position, anims[0].transform.position);
                    Debug.Log($"[Ai] {brain.name} phase={brain.Phase} dist={dist:F2} " +
                              $"token={brain.HasAttackToken} pos={brain.transform.position}");
                }
            }

            // 検証用。被弾フラッシュを出した状態で撮る（見た目の濃さの確認）
            if (FlashShot)
            {
                var flash = FindAnyObjectByType<HitFlash>();
                if (flash != null)
                    flash.FlashNow(flash.HitAlpha);

                yield return null;   // 1 フレーム描かせてから撮る
            }

            // 撮影の直前に姿勢を固定する。
            // CharacterAnimator は LateUpdate で姿勢を作り直すので、撮影フレームで
            // 一度 Idle に戻されてしまう（=待機姿勢が写る）。LateUpdate の中で
            // 最後に上書きし直すことで、狙ったコマがそのまま写る。
            var freezer = gameObject.AddComponent<PoseFreezer>();
            freezer.Setup(anims, State, StateTime, StateDuration);

            // スキンの反映は 1 フレーム遅れることがあるため、姿勢を保ったまま数フレーム待つ
            yield return null;
            yield return null;

            ScreenCapture.CaptureScreenshot(full);
            Debug.Log($"[Shot] state={State} face={FaceCamera} path={full}");

            yield return new WaitForSeconds(1.5f);
            Application.Quit();
        }

        /// <summary>顔の正面へカメラを回り込ませる。倒れたときに髪が顔を隠していないかを見るため。</summary>
        static void PlaceFaceCamera(CharacterAnimator anim, float fov)
        {
            var cam = Camera.main;
            if (cam == null || anim.Rig == null || !anim.Rig.Ready)
                return;

            var head = anim.Rig.Head;

            // head ボーンは首の付け根なので、そのまま狙うと画の中心が頭頂になる。
            // 顔の中心（少し上・前）を狙い、額から顎までが入る距離に置く。
            Vector3 target = head.position + head.up * 0.06f + head.forward * 0.05f;

            cam.transform.position = target + head.forward * 0.62f + head.up * 0.05f;
            cam.transform.LookAt(target);
            cam.fieldOfView = fov;
        }

        /// <summary>頭からのワールド座標オフセットでカメラを置く。頭の向きに依存しない確認用。</summary>
        static void PlaceCameraAtOffset(CharacterAnimator anim, Vector3 offset, float fov)
        {
            var cam = Camera.main;
            if (cam == null || anim.Rig == null || !anim.Rig.Ready)
                return;

            Vector3 target = anim.Rig.Head.position;
            cam.transform.position = target + offset;
            cam.transform.LookAt(target);
            cam.fieldOfView = fov;
        }

        void OnApplicationQuit() => Debug.Log("[Shot] OnApplicationQuit");
        void OnDestroy() => Debug.Log("[Shot] OnDestroy");

        static string Value(string[] args, string key)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], key, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            }

            return null;
        }

        static CharacterAnimator.State ParseState(string name, CharacterAnimator.State fallback)
        {
            if (!string.IsNullOrEmpty(name) &&
                Enum.TryParse(name, true, out CharacterAnimator.State parsed))
                return parsed;

            return fallback;
        }

        /// <summary>
        /// 撮影フレームの LateUpdate で姿勢を入れ直す。CharacterAnimator は
        /// LateUpdate の最後に Idle へ戻すため、Update の時点で姿勢を決めても
        /// 描画までに上書きされる。ここで一番最後に書くことで撮影コマを保証する。
        /// </summary>
        sealed class PoseFreezer : MonoBehaviour
        {
            CharacterAnimator[] _anims;
            CharacterAnimator.State _state;
            float _time;
            float _duration;
            bool _logged;

            public void Setup(CharacterAnimator[] anims, CharacterAnimator.State state,
                              float time, float duration)
            {
                _anims = anims;
                _state = state;
                _time = time;
                _duration = duration;

                // 二重に書くと、どちらが最後か分からなくなる。撮影中は止める。
                foreach (var a in anims)
                    if (a != null)
                        a.enabled = false;
            }

            void LateUpdate()
            {
                if (_anims == null)
                    return;

                foreach (var a in _anims)
                {
                    if (a == null || a.Rig == null || !a.Rig.Ready)
                        continue;

                    a.ForceState(_state, _time, _duration, StaggerTier.GuardBreak, false);
                    a.ApplyPose();

                    if (!_logged)
                    {
                        _logged = true;
                        Debug.Log($"[Shot] freeze {_state} t={_time:F2} hipsY={a.Rig.Hips.position.y:F3} " +
                                  $"headY={a.Rig.Head.position.y:F3}");
                    }
                }
            }
        }
    }
}
