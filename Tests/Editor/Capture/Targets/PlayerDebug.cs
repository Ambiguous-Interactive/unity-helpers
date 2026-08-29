// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Capture.Targets
{
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    /// <summary>
    /// Documentation target for the gameplay-testing example: Health and Economy button groups
    /// over a component's own fields.
    /// </summary>
    public sealed class PlayerDebug : MonoBehaviour
    {
        public int health = 100;
        public int gold;

        [WButton("Heal", groupName: "Health", colorKey: "Default-Light")]
        private void Heal()
        {
            health = 100;
        }

        [WButton("Take Damage", groupName: "Health")]
        private void TakeDamage()
        {
            health -= 25;
        }

        [WButton("Kill Player", groupName: "Health", colorKey: "Default-Dark")]
        private void Kill()
        {
            health = 0;
        }

        [WButton("Add Gold", groupName: "Economy")]
        private void AddGold()
        {
            gold += 100;
        }

        [WButton("Roll Reward", groupName: "Economy", historyCapacity: 10)]
        private int RollReward()
        {
            gold += 25;
            return 25;
        }
    }
}
