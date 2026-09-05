// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.TestTypes
{
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    /// <summary>
    /// Test target with multiple draw orders, each containing buttons in reverse alphabetical declaration order.
    /// Verifies declaration order is preserved within each draw order bucket.
    /// </summary>
    public sealed class WButtonMultipleDrawOrdersWithDeclarationOrderTarget : ScriptableObject
    {
        [WButton("Z First in Order 0", drawOrder: 0)]
        public void Order0Z() { }

        [WButton("Y Second in Order 0", drawOrder: 0)]
        public void Order0Y() { }

        [WButton("X Third in Order 0", drawOrder: 0)]
        public void Order0X() { }

        [WButton("C First in Order 1", drawOrder: 1)]
        public void Order1C() { }

        [WButton("B Second in Order 1", drawOrder: 1)]
        public void Order1B() { }

        [WButton("A Third in Order 1", drawOrder: 1)]
        public void Order1A() { }

        [WButton("Q First in Order -1", drawOrder: -1)]
        public void OrderMinus1Q() { }

        [WButton("P Second in Order -1", drawOrder: -1)]
        public void OrderMinus1P() { }

        [WButton("O Third in Order -1", drawOrder: -1)]
        public void OrderMinus1O() { }
    }
}
