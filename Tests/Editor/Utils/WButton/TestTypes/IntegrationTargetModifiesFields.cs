// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if UNITY_EDITOR
namespace WallstopStudios.UnityHelpers.Tests.Editor.Utils.WButton
{
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    internal sealed class IntegrationTargetModifiesFields : ScriptableObject
    {
        public int counter;
        public string lastAction;

        [WButton]
        public void IncrementCounter()
        {
            counter++;
            lastAction = "Incremented";
        }

        [WButton]
        public void ResetCounter()
        {
            counter = 0;
            lastAction = "Reset";
        }
    }
}
#endif
