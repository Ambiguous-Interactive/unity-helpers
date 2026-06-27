// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Attributes
{
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.Core.TestTypes;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class ValidateAssignmentAttributeTests : CommonTestBase
    {
        [Test]
        public void AreAnyAssignmentsInvalidDetectsMissingValues()
        {
            GameObject go = Track(
                new GameObject("ValidateAssignments", typeof(AssignmentComponent))
            );
            AssignmentComponent component = go.GetComponent<AssignmentComponent>();

            Assert.IsTrue(component.AreAnyAssignmentsInvalid());

            component.requiredObject = Track(new GameObject("Assigned"));
            component.requiredString = "value";
            component.requiredList.Add(1);
            component.requiredCollection.Enqueue(2);
            component.requiredEnumerable = new[] { 3 };

            Assert.IsFalse(component.AreAnyAssignmentsInvalid());
            return;
        }

        [Test]
        public void ValidateAssignmentsLogsWarningsForMissingFields()
        {
            GameObject go = Track(
                new GameObject("ValidateAssignmentsLogs", typeof(AssignmentComponent))
            );
            AssignmentComponent component = go.GetComponent<AssignmentComponent>();

            LogAssert.Expect(
                LogType.Warning,
                new System.Text.RegularExpressions.Regex(
                    @"^\d+(\.\d+)?\|ValidateAssignmentsLogs\[AssignmentComponent\]\|requiredObject not found\.$"
                )
            );
            LogAssert.Expect(
                LogType.Warning,
                new System.Text.RegularExpressions.Regex(
                    @"^\d+(\.\d+)?\|ValidateAssignmentsLogs\[AssignmentComponent\]\|requiredString not found\.$"
                )
            );
            LogAssert.Expect(
                LogType.Warning,
                new System.Text.RegularExpressions.Regex(
                    @"^\d+(\.\d+)?\|ValidateAssignmentsLogs\[AssignmentComponent\]\|requiredList not found\.$"
                )
            );
            LogAssert.Expect(
                LogType.Warning,
                new System.Text.RegularExpressions.Regex(
                    @"^\d+(\.\d+)?\|ValidateAssignmentsLogs\[AssignmentComponent\]\|requiredCollection not found\.$"
                )
            );
            LogAssert.Expect(
                LogType.Warning,
                new System.Text.RegularExpressions.Regex(
                    @"^\d+(\.\d+)?\|ValidateAssignmentsLogs\[AssignmentComponent\]\|requiredEnumerable not found\.$"
                )
            );

            component.ValidateAssignments();
            return;
        }

        [Test]
        public void AttributeDefaultConstructorSetsWarningMessageType()
        {
            ValidateAssignmentAttribute attribute = new();
            Assert.That(attribute.MessageType, Is.EqualTo(ValidateAssignmentMessageType.Warning));
            Assert.That(attribute.CustomMessage, Is.Null);
        }

        [Test]
        public void AttributeMessageTypeConstructorSetsCorrectType()
        {
            ValidateAssignmentAttribute warningAttribute = new(
                ValidateAssignmentMessageType.Warning
            );
            Assert.That(
                warningAttribute.MessageType,
                Is.EqualTo(ValidateAssignmentMessageType.Warning)
            );
            Assert.That(warningAttribute.CustomMessage, Is.Null);

            ValidateAssignmentAttribute errorAttribute = new(ValidateAssignmentMessageType.Error);
            Assert.That(
                errorAttribute.MessageType,
                Is.EqualTo(ValidateAssignmentMessageType.Error)
            );
            Assert.That(errorAttribute.CustomMessage, Is.Null);
        }

        [Test]
        public void AttributeCustomMessageConstructorSetsMessage()
        {
            string customMessage = "Custom validation message";
            ValidateAssignmentAttribute attribute = new(customMessage);
            Assert.That(attribute.CustomMessage, Is.EqualTo(customMessage));
            Assert.That(attribute.MessageType, Is.EqualTo(ValidateAssignmentMessageType.Warning));
        }

        [Test]
        public void AttributeFullConstructorSetsBothProperties()
        {
            string customMessage = "Error validation message";
            ValidateAssignmentAttribute attribute = new(
                ValidateAssignmentMessageType.Error,
                customMessage
            );
            Assert.That(attribute.CustomMessage, Is.EqualTo(customMessage));
            Assert.That(attribute.MessageType, Is.EqualTo(ValidateAssignmentMessageType.Error));
        }

        [Test]
        public void AttributeWithWarningTypeAndCustomMessage()
        {
            string customMessage = "Warning: This field should be assigned";
            ValidateAssignmentAttribute attribute = new(
                ValidateAssignmentMessageType.Warning,
                customMessage
            );
            Assert.That(attribute.CustomMessage, Is.EqualTo(customMessage));
            Assert.That(attribute.MessageType, Is.EqualTo(ValidateAssignmentMessageType.Warning));
        }

        [Test]
        public void AttributeWithEmptyStringCustomMessage()
        {
            ValidateAssignmentAttribute attribute = new(string.Empty);
            Assert.That(attribute.CustomMessage, Is.EqualTo(string.Empty));
            Assert.That(attribute.MessageType, Is.EqualTo(ValidateAssignmentMessageType.Warning));
        }

        [Test]
        public void AttributeWithNullCustomMessage()
        {
            ValidateAssignmentAttribute attribute = new((string)null);
            Assert.That(attribute.CustomMessage, Is.Null);
            Assert.That(attribute.MessageType, Is.EqualTo(ValidateAssignmentMessageType.Warning));
        }

        [Test]
        public void MessageTypeEnumHasExpectedValues()
        {
            Assert.That((int)ValidateAssignmentMessageType.Warning, Is.EqualTo(0));
            Assert.That((int)ValidateAssignmentMessageType.Error, Is.EqualTo(1));
        }

        [Test]
        public void AreAnyAssignmentsInvalidDetectsEmptyString()
        {
            GameObject go = Track(
                new GameObject("ValidateStringAssignment", typeof(AssignmentComponent))
            );
            AssignmentComponent component = go.GetComponent<AssignmentComponent>();

            component.requiredObject = Track(new GameObject("Assigned"));
            component.requiredString = "";
            component.requiredList.Add(1);
            component.requiredCollection.Enqueue(2);
            component.requiredEnumerable = new[] { 3 };

            Assert.IsTrue(component.AreAnyAssignmentsInvalid());

            component.requiredString = "valid";
            Assert.IsFalse(component.AreAnyAssignmentsInvalid());
            return;
        }

        [Test]
        public void AreAnyAssignmentsInvalidDetectsWhitespaceString()
        {
            GameObject go = Track(
                new GameObject("ValidateWhitespaceAssignment", typeof(AssignmentComponent))
            );
            AssignmentComponent component = go.GetComponent<AssignmentComponent>();

            component.requiredObject = Track(new GameObject("Assigned"));
            component.requiredString = "   ";
            component.requiredList.Add(1);
            component.requiredCollection.Enqueue(2);
            component.requiredEnumerable = new[] { 3 };

            Assert.IsTrue(component.AreAnyAssignmentsInvalid());
            return;
        }

        [Test]
        public void AreAnyAssignmentsInvalidDetectsEmptyList()
        {
            GameObject go = Track(
                new GameObject("ValidateListAssignment", typeof(AssignmentComponent))
            );
            AssignmentComponent component = go.GetComponent<AssignmentComponent>();

            component.requiredObject = Track(new GameObject("Assigned"));
            component.requiredString = "valid";
            component.requiredCollection.Enqueue(2);
            component.requiredEnumerable = new[] { 3 };

            Assert.IsTrue(component.AreAnyAssignmentsInvalid());

            component.requiredList.Add(1);
            Assert.IsFalse(component.AreAnyAssignmentsInvalid());
            return;
        }

        [Test]
        public void AreAnyAssignmentsInvalidDetectsEmptyEnumerable()
        {
            GameObject go = Track(
                new GameObject("ValidateEnumerableAssignment", typeof(AssignmentComponent))
            );
            AssignmentComponent component = go.GetComponent<AssignmentComponent>();

            component.requiredObject = Track(new GameObject("Assigned"));
            component.requiredString = "valid";
            component.requiredList.Add(1);
            component.requiredCollection.Enqueue(2);
            component.requiredEnumerable = System.Array.Empty<int>();

            Assert.IsTrue(component.AreAnyAssignmentsInvalid());

            component.requiredEnumerable = new[] { 3 };
            Assert.IsFalse(component.AreAnyAssignmentsInvalid());
            return;
        }
    }
}
