using System;
using Ryona.Game;
using UnityEngine;

namespace Ryona.EditorTools
{
    public static partial class CharacterPreview
    {
        /// <summary>
        /// 撮影ツールが書き換える gameplay 側の static 姿勢パラメータを、スコープを出るときに必ず戻す。
        /// <c>using var guard = new StanceOverrideGuard();</c> を撮影メソッドの先頭に置く。
        /// </summary>
        internal sealed class StanceOverrideGuard : IDisposable
        {
            readonly Vector3 _lu, _lf, _ru, _rf;
            readonly float _rollL, _rollR;

            internal StanceOverrideGuard()
            {
                _lu = CharacterAnimator.StanceArmLUpper;
                _lf = CharacterAnimator.StanceArmLFore;
                _ru = CharacterAnimator.StanceArmRUpper;
                _rf = CharacterAnimator.StanceArmRFore;
                _rollL = CharacterAnimator.StanceHandRollL;
                _rollR = CharacterAnimator.StanceHandRollR;
            }

            public void Dispose()
            {
                CharacterAnimator.StanceArmLUpper = _lu;
                CharacterAnimator.StanceArmLFore = _lf;
                CharacterAnimator.StanceArmRUpper = _ru;
                CharacterAnimator.StanceArmRFore = _rf;
                CharacterAnimator.StanceHandRollL = _rollL;
                CharacterAnimator.StanceHandRollR = _rollR;
            }
        }
    }
}
