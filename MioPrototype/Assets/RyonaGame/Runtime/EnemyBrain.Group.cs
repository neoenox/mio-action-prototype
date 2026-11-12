using System;
using UnityEngine;

namespace Ryona.Game
{
    public sealed partial class EnemyBrain : MonoBehaviour, IDamageable
    {
        void RegisterGroup()
        {
            if (_registered || Group == null)
                return;

            Group.Register(this);
            _registered = true;
        }

        void UnregisterGroup()
        {
            if (!_registered)
                return;

            _registered = false;
            if (Group != null)
                Group.Unregister(this);
        }

        void ReleaseToken()
        {
            if (Group != null)
                Group.ReleaseAttackToken(this);
        }

        // --- 移動 ----------------------------------------------------------------

    }
}
