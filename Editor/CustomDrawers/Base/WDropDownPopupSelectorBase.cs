// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.CustomDrawers.Base
{
#if UNITY_EDITOR
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.UIElements;
    using WallstopStudios.UnityHelpers.Editor.Settings;

    /// <summary>
    /// Base class for UI Toolkit popup dropdown selectors that use IMGUI rendering
    /// via IMGUIContainer for displaying large option lists with a popup window.
    /// </summary>
    /// <typeparam name="TValue">The type of the field value (string, int, etc.).</typeparam>
    public abstract class WDropDownPopupSelectorBase<TValue> : BaseField<TValue>
    {
        /// <summary>
        /// Gets the total number of options available.
        /// </summary>
        protected abstract int OptionCount { get; }

        private SerializedObject _serializedObject;
        private string _propertyPath = string.Empty;
        private GUIContent _labelContent = GUIContent.none;
        private readonly GUIContent _buttonContent = new();
        private int _pageSize;

        protected WDropDownPopupSelectorBase()
            : base(string.Empty, CreateInputElement(out IMGUIContainer container))
        {
            AddToClassList("unity-base-field");
            AddToClassList("unity-base-field__aligned");
            labelElement.AddToClassList("unity-base-field__label");
            labelElement.AddToClassList("unity-label");

            _pageSize = Mathf.Max(1, UnityHelpersSettings.GetStringInListPageLimit());

            container.style.flexGrow = 1f;
            container.style.marginLeft = 0f;
            container.style.paddingLeft = 0f;
            container.onGUIHandler = OnGUIHandler;
        }

        private static VisualElement CreateInputElement(out IMGUIContainer container)
        {
            container = new IMGUIContainer();
            return container;
        }

        /// <summary>
        /// Binds this selector to a serialized property.
        /// </summary>
        /// <param name="property">The property to bind.</param>
        /// <param name="labelText">The label text to display.</param>
        public void BindProperty(SerializedProperty property, string labelText)
        {
            _serializedObject = property?.serializedObject;
            _propertyPath = property?.propertyPath ?? string.Empty;
            string resolvedLabel =
                labelText ?? property?.displayName ?? property?.name ?? string.Empty;
            label = resolvedLabel;
            _labelContent = new GUIContent(resolvedLabel);
        }

        /// <summary>
        /// Unbinds this selector from any property.
        /// </summary>
        public void UnbindProperty()
        {
            _serializedObject = null;
            _propertyPath = string.Empty;
        }

        /// <summary>
        /// Gets the display value to show on the popup button for the current property state.
        /// </summary>
        /// <param name="property">The serialized property.</param>
        /// <returns>The display string for the button.</returns>
        protected abstract string GetDisplayValue(SerializedProperty property);

        /// <summary>
        /// Gets the value to set via SetValueWithoutNotify.
        /// </summary>
        /// <param name="property">The serialized property.</param>
        /// <returns>The value to set on the field.</returns>
        protected abstract TValue GetFieldValue(SerializedProperty property);

        /// <summary>
        /// Shows the popup window for selecting from the options.
        /// </summary>
        /// <param name="controlRect">The rect of the control that triggered the popup.</param>
        /// <param name="property">The serialized property being edited.</param>
        /// <param name="pageSize">The page size for pagination.</param>
        protected abstract void ShowPopup(
            Rect controlRect,
            SerializedProperty property,
            int pageSize
        );

        private void OnGUIHandler()
        {
            if (_serializedObject == null || string.IsNullOrEmpty(_propertyPath))
            {
                return;
            }

            _serializedObject.UpdateIfRequiredOrScript();
            SerializedProperty property = _serializedObject.FindProperty(_propertyPath);
            if (property == null)
            {
                return;
            }

            string displayValue = GetDisplayValue(property);
            SetValueWithoutNotify(GetFieldValue(property));

            Rect controlRect = EditorGUILayout.GetControlRect();
            int resolvedPageSize = Mathf.Max(1, UnityHelpersSettings.GetStringInListPageLimit());
            if (resolvedPageSize != _pageSize)
            {
                _pageSize = resolvedPageSize;
            }

            _buttonContent.text = displayValue;
            if (EditorGUI.DropdownButton(controlRect, _buttonContent, FocusType.Keyboard))
            {
                ShowPopup(controlRect, property, _pageSize);
            }
            _serializedObject.ApplyModifiedProperties();
        }
    }
#endif
}
