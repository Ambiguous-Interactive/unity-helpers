// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

// UNH-SUPPRESS UNH003: TagsTestBase inherits from AttributeTagsTestBase which inherits from CommonTestBase
namespace WallstopStudios.UnityHelpers.Tests.Tags.Helpers
{
    using System;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Tags;

    public abstract class TagsTestBase : AttributeTagsTestBase
    {
        protected static void ClearAttributeUtilitiesCaches()
        {
            AttributeUtilities.AllAttributeNames = null;
            AttributeUtilities.AttributeFields.Clear();
        }

        protected GameObject CreateTrackedGameObject(string name, params Type[] componentTypes)
        {
            GameObject gameObject = Track(new GameObject(name));
            if (componentTypes is not { Length: > 0 })
            {
                return gameObject;
            }

            foreach (Type componentType in componentTypes)
            {
                if (gameObject.HasComponent(componentType))
                {
                    continue;
                }

                _ = gameObject.AddComponent(componentType);
            }

            return gameObject;
        }

        protected AttributeEffect CreateEffect(
            string name,
            Action<AttributeEffect> configure = null
        )
        {
            AttributeEffect effect = Track(ScriptableObject.CreateInstance<AttributeEffect>());
            effect.name = name;
            effect.durationType = ModifierDurationType.Duration;
            effect.duration = 1f;
            configure?.Invoke(effect);
            return effect;
        }
    }
}
