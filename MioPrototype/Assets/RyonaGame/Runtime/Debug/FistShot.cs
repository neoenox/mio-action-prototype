using System.Collections;
using UnityEngine;

namespace Ryona.Game
{
    /// <summary>
    /// パンチが伸び切った瞬間だけを撮る（<c>-fistshot &lt;png&gt;</c>）。
    ///
    /// 連写（-hudshotdelays）は撮影の間隔が 0.15 秒空くので、0.3 秒周期の攻撃と
    /// 位相が合って「構え」ばかりが写る。拳が前に出たフレームを条件で選んで撮る。
    /// </summary>
    [AddComponentMenu("Ryona/Fist Shot")]
    public sealed class FistShot : MonoBehaviour
    {
        string _path;
        int _taken;
        float _last;
        bool _busy;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            string path = LaunchOptions.Value("-fistshot");

            if (string.IsNullOrEmpty(path))
                return;

            var go = new GameObject("Fist Shot");
            var shot = go.AddComponent<FistShot>();
            shot._path = path;
        }

        void Update()
        {
            if (_busy || _taken >= 6 || Time.unscaledTime - _last < 0.55f)
                return;

            var anim = FindFirstObjectByType<CharacterAnimator>();
            if (anim == null || anim.Rig == null || !anim.Rig.Ready)
                return;
            if (anim.Current != CharacterAnimator.State.Attack)
                return;

            Transform hand = anim.Rig.RHand;
            if (hand == null)
                return;

            float z = anim.transform.InverseTransformPoint(hand.position).z;
            float zl = anim.transform.InverseTransformPoint(anim.Rig.LHand.position).z;
            if (z < 0.50f && zl < 0.50f)
                return;

            _last = Time.unscaledTime;
            _taken++;

            var rig = anim.Rig;
            Vector3 l = anim.transform.InverseTransformPoint(rig.LHand.position);
            Vector3 r = anim.transform.InverseTransformPoint(rig.RHand.position);
            Vector3 le = anim.transform.InverseTransformPoint(rig.LLowerArm.position);
            Vector3 re = anim.transform.InverseTransformPoint(rig.RLowerArm.position);
            Debug.Log($"[FistShot] {_taken} L=({l.x:0.00},{l.y:0.00},{l.z:0.00}) R=({r.x:0.00},{r.y:0.00},{r.z:0.00}) " +
                $"Lel=({le.y:0.00},{le.z:0.00}) Rel=({re.y:0.00},{re.z:0.00}) t={anim.StateTime:0.000}");

            StartCoroutine(Shoot());
        }

        IEnumerator Shoot()
        {
            _busy = true;

            string full = System.IO.Path.GetFullPath(_path);
            string dir = System.IO.Path.GetDirectoryName(full);
            string name = System.IO.Path.GetFileNameWithoutExtension(full);
            string ext = System.IO.Path.GetExtension(full);

            string file = System.IO.Path.Combine(dir, $"{name}_{_taken}{ext}");
            ScreenCapture.CaptureScreenshot(file);
            Debug.Log($"[FistShot] {_taken} 枚目 {file}");

            yield return new WaitForSecondsRealtime(0.25f);
            _busy = false;

            if (_taken >= 6)
            {
                yield return new WaitForSecondsRealtime(0.6f);
                Application.Quit();
            }
        }
    }
}
