// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Attributes
{
    using System;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class WNotNullAttributeTests : CommonTestBase
    {
        [Test]
        public void CheckForNullsThrowsWhenAnnotatedFieldIsNull()
        {
            WNotNullHolder holder = new();
            Assert.Throws<ArgumentNullException>(() => holder.CheckForNulls());

            holder.reference = new object();
            Assert.DoesNotThrow(() => holder.CheckForNulls());
        }

        [Test]
        public void CheckForNullsThrowsWhenUnityObjectFieldIsNull()
        {
            WNotNullUnityObjectHolder holder = new();
            Assert.Throws<ArgumentNullException>(() => holder.CheckForNulls());

            holder.unityObject = NewGameObject("TestObject");
            Assert.DoesNotThrow(() => holder.CheckForNulls());
        }

        [Test]
        public void CheckForNullsThrowsForDestroyedUnityObject()
        {
            WNotNullUnityObjectHolder holder = new();
            GameObject gameObject = NewGameObject("DestroyedObject");
            holder.unityObject = gameObject;
            Assert.DoesNotThrow(() => holder.CheckForNulls());

            UnityEngine.Object.DestroyImmediate(gameObject); // UNH-SUPPRESS: Test verifies null detection after immediate destruction
            _trackedObjects.Remove(gameObject);
            Assert.Throws<ArgumentNullException>(() => holder.CheckForNulls());
        }

        [Test]
        public void CheckForNullsThrowsForMultipleNullFields()
        {
            WNotNullMultipleHolder holder = new();
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() =>
                holder.CheckForNulls()
            );
            /*
                Reflection does not guarantee field order, so either expected null field may appear in the
                exception.
            */
            string[] expectedNullFields =
            {
                nameof(WNotNullMultipleHolder.firstField),
                nameof(WNotNullMultipleHolder.secondField),
                nameof(WNotNullMultipleHolder.thirdField),
            };
            Assert.That(
                expectedNullFields,
                Does.Contain(exception.ParamName),
                $"Expected ParamName to be one of [{string.Join(", ", expectedNullFields)}], but was '{exception.ParamName}'"
            );
        }

        [Test]
        public void CheckForNullsDoesNotThrowWhenAllFieldsAssigned()
        {
            WNotNullMultipleHolder holder = new()
            {
                firstField = new object(),
                secondField = "test",
                thirdField = NewGameObject("TestObject"),
            };
            Assert.DoesNotThrow(() => holder.CheckForNulls());
        }

        [Test]
        public void AttributeDefaultConstructorSetsWarningMessageType()
        {
            WNotNullAttribute attribute = new();
            Assert.That(attribute.MessageType, Is.EqualTo(WNotNullMessageType.Warning));
            Assert.That(attribute.CustomMessage, Is.Null);
        }

        [Test]
        public void AttributeMessageTypeConstructorSetsCorrectType()
        {
            WNotNullAttribute warningAttribute = new(WNotNullMessageType.Warning);
            Assert.That(warningAttribute.MessageType, Is.EqualTo(WNotNullMessageType.Warning));
            Assert.That(warningAttribute.CustomMessage, Is.Null);

            WNotNullAttribute errorAttribute = new(WNotNullMessageType.Error);
            Assert.That(errorAttribute.MessageType, Is.EqualTo(WNotNullMessageType.Error));
            Assert.That(errorAttribute.CustomMessage, Is.Null);
        }

        [Test]
        public void AttributeCustomMessageConstructorSetsMessage()
        {
            string customMessage = "Custom validation message";
            WNotNullAttribute attribute = new(customMessage);
            Assert.That(attribute.CustomMessage, Is.EqualTo(customMessage));
            Assert.That(attribute.MessageType, Is.EqualTo(WNotNullMessageType.Warning));
        }

        [Test]
        public void AttributeFullConstructorSetsBothProperties()
        {
            string customMessage = "Error validation message";
            WNotNullAttribute attribute = new(WNotNullMessageType.Error, customMessage);
            Assert.That(attribute.CustomMessage, Is.EqualTo(customMessage));
            Assert.That(attribute.MessageType, Is.EqualTo(WNotNullMessageType.Error));
        }

        [Test]
        public void AttributeWithWarningTypeAndCustomMessage()
        {
            string customMessage = "Warning: This field should be assigned";
            WNotNullAttribute attribute = new(WNotNullMessageType.Warning, customMessage);
            Assert.That(attribute.CustomMessage, Is.EqualTo(customMessage));
            Assert.That(attribute.MessageType, Is.EqualTo(WNotNullMessageType.Warning));
        }

        [Test]
        public void AttributeWithEmptyStringCustomMessage()
        {
            WNotNullAttribute attribute = new(string.Empty);
            Assert.That(attribute.CustomMessage, Is.EqualTo(string.Empty));
            Assert.That(attribute.MessageType, Is.EqualTo(WNotNullMessageType.Warning));
        }

        [Test]
        public void AttributeWithNullCustomMessage()
        {
            WNotNullAttribute attribute = new((string)null);
            Assert.That(attribute.CustomMessage, Is.Null);
            Assert.That(attribute.MessageType, Is.EqualTo(WNotNullMessageType.Warning));
        }

        [Test]
        public void MessageTypeEnumHasExpectedValues()
        {
            Assert.That((int)WNotNullMessageType.Warning, Is.EqualTo(0));
            Assert.That((int)WNotNullMessageType.Error, Is.EqualTo(1));
        }

        [Test]
        public void CheckForNullsWithMixedAttributeConfigurations()
        {
            WNotNullMixedConfigHolder holder = new();
            Assert.Throws<ArgumentNullException>(() => holder.CheckForNulls());

            holder.warningField = new object();
            Assert.Throws<ArgumentNullException>(() => holder.CheckForNulls());

            holder.errorField = new object();
            Assert.Throws<ArgumentNullException>(() => holder.CheckForNulls());

            holder.customMessageField = new object();
            Assert.DoesNotThrow(() => holder.CheckForNulls());
        }

        [Test]
        public void CheckForNullsWithNoAnnotatedFieldsDoesNotThrow()
        {
            WNotNullEmptyHolder holder = new();
            Assert.DoesNotThrow(() => holder.CheckForNulls());

            holder.regularField = null;
            Assert.DoesNotThrow(() => holder.CheckForNulls());
        }

        [Test]
        public void CheckForNullsOnNullObjectDoesNotThrow()
        {
            object nullObject = null;
            Assert.DoesNotThrow(() => nullObject.CheckForNulls());
        }

        [Test]
        public void CheckForNullsWithPrivateAnnotatedFieldThrows()
        {
            WNotNullPrivateFieldHolder holder = new();
            Assert.Throws<ArgumentNullException>(() => holder.CheckForNulls());

            holder.SetPrivateField(new object());
            Assert.DoesNotThrow(() => holder.CheckForNulls());
        }

        [Test]
        public void CheckForNullsExceptionContainsFieldName()
        {
            WNotNullHolder holder = new();
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() =>
                holder.CheckForNulls()
            );

            Assert.That(
                exception.ParamName,
                Is.EqualTo(nameof(WNotNullHolder.reference)),
                $"Expected ParamName to be '{nameof(WNotNullHolder.reference)}', but was '{exception.ParamName}'"
            );
        }

        [TestCase(WNotNullMessageType.Warning, null)]
        [TestCase(WNotNullMessageType.Warning, "")]
        [TestCase(WNotNullMessageType.Warning, "Custom message")]
        [TestCase(WNotNullMessageType.Error, null)]
        [TestCase(WNotNullMessageType.Error, "")]
        [TestCase(WNotNullMessageType.Error, "Custom error message")]
        public void AttributeConstructorPreservesParameters(
            WNotNullMessageType messageType,
            string customMessage
        )
        {
            WNotNullAttribute attribute = new(messageType, customMessage);
            Assert.That(
                attribute.MessageType,
                Is.EqualTo(messageType),
                $"Expected MessageType to be {messageType}, but was {attribute.MessageType}"
            );
            Assert.That(
                attribute.CustomMessage,
                Is.EqualTo(customMessage),
                $"Expected CustomMessage to be '{customMessage}', but was '{attribute.CustomMessage}'"
            );
        }

        [Test]
        public void CheckForNullsWithSingleFieldAssignedStillThrowsForOtherNullFields()
        {
            WNotNullMultipleHolder holder = new() { firstField = new object() };

            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() =>
                holder.CheckForNulls()
            );

            string[] remainingNullFields =
            {
                nameof(WNotNullMultipleHolder.secondField),
                nameof(WNotNullMultipleHolder.thirdField),
            };
            Assert.That(
                remainingNullFields,
                Does.Contain(exception.ParamName),
                $"Expected ParamName to be one of [{string.Join(", ", remainingNullFields)}], but was '{exception.ParamName}'"
            );
        }

        [Test]
        public void CheckForNullsWithTwoFieldsAssignedStillThrowsForLastNullField()
        {
            WNotNullMultipleHolder holder = new()
            {
                firstField = new object(),
                secondField = "test",
            };

            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() =>
                holder.CheckForNulls()
            );

            Assert.That(
                exception.ParamName,
                Is.EqualTo(nameof(WNotNullMultipleHolder.thirdField)),
                $"Expected ParamName to be '{nameof(WNotNullMultipleHolder.thirdField)}', but was '{exception.ParamName}'"
            );
        }

        [Test]
        public void CheckForNullsWithEmptyStringDoesNotThrow()
        {
            WNotNullStringHolder holder = new() { stringField = string.Empty };
            Assert.DoesNotThrow(() => holder.CheckForNulls());
        }

        [Test]
        public void CheckForNullsWithWhitespaceStringDoesNotThrow()
        {
            WNotNullStringHolder holder = new() { stringField = "   " };
            Assert.DoesNotThrow(() => holder.CheckForNulls());
        }

        [Test]
        public void CheckForNullsWithNullStringThrows()
        {
            WNotNullStringHolder holder = new() { stringField = null };
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() =>
                holder.CheckForNulls()
            );
            Assert.That(
                exception.ParamName,
                Is.EqualTo(nameof(WNotNullStringHolder.stringField)),
                $"Expected ParamName to be '{nameof(WNotNullStringHolder.stringField)}', but was '{exception.ParamName}'"
            );
        }
    }

    internal sealed class WNotNullHolder
    {
        [WNotNull]
        public object reference;
    }

    internal sealed class WNotNullUnityObjectHolder
    {
        [WNotNull]
        public UnityEngine.Object unityObject;
    }

    internal sealed class WNotNullMultipleHolder
    {
        [WNotNull]
        public object firstField;

        [WNotNull]
        public string secondField;

        [WNotNull]
        public GameObject thirdField;
    }

    internal sealed class WNotNullMixedConfigHolder
    {
        [WNotNull(WNotNullMessageType.Warning)]
        public object warningField;

        [WNotNull(WNotNullMessageType.Error)]
        public object errorField;

        [WNotNull("Custom error message")]
        public object customMessageField;
    }

    internal sealed class WNotNullEmptyHolder
    {
        public object regularField;
    }

    internal sealed class WNotNullPrivateFieldHolder
    {
        [WNotNull]
        private object _privateField;

        public void SetPrivateField(object value)
        {
            _privateField = value;
        }
    }

    internal sealed class WNotNullStringHolder
    {
        [WNotNull]
        public string stringField;
    }
}
