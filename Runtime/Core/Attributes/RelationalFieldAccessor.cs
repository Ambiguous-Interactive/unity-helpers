// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Attributes
{
    using System;
    using System.Reflection;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Helper;

    internal sealed class RelationalFieldAccessor : RelationalComponentProcessor.FieldAccessor
    {
        private readonly Func<object, object> _getter;
        private readonly Action<object, object> _setter;

        internal RelationalFieldAccessor(FieldInfo field)
        {
            _getter = ReflectionHelpers.GetFieldGetter(field);
            _setter = ReflectionHelpers.GetFieldSetter(field);
        }

        public override object Get(Component component)
        {
            return component == null ? null : _getter(component);
        }

        public override void Set(Component component, object value)
        {
            if (component != null)
            {
                _setter(component, value);
            }
        }
    }
}
