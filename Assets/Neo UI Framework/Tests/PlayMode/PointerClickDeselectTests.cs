using System.Collections;
using Neo.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;

namespace Neo.UI.Tests
{
    /// <summary>
    /// A pointer click must not park a widget in the Selected state: Selected outranks Highlighted
    /// and survives pointer exit, and every button variant colors Selected like hover — so a clicked
    /// button read as "stuck on the hover tint" until the user clicked somewhere else.
    /// </summary>
    public class PointerClickDeselectTests : PlayModeTestBase
    {
        private EventSystem _eventSystem;

        [SetUp]
        public override void SetUp()
        {
            base.SetUp();
            _eventSystem = Track(new GameObject("EventSystem", typeof(EventSystem))).GetComponent<EventSystem>();
        }

        private PointerEventData LeftClick() =>
            new PointerEventData(_eventSystem) { button = PointerEventData.InputButton.Left };

        [UnityTest]
        public IEnumerator ButtonPointerClick_ClearsSelection_SoSelectedColorDoesNotStick()
        {
            var button = CreateUIObject("Button").AddComponent<UIButton>();
            yield return null;

            PointerEventData data = LeftClick();
            button.OnPointerDown(data);
            Assert.AreEqual(button.gameObject, _eventSystem.currentSelectedGameObject,
                "pointer-down should grab the EventSystem selection (Unity Selectable behavior)");

            button.OnPointerUp(data);
            button.OnPointerClick(data);

            Assert.IsNull(_eventSystem.currentSelectedGameObject,
                "a pointer click must release the selection so the widget leaves the Selected state");
        }

        [UnityTest]
        public IEnumerator ButtonPointerClick_KeepsSelection_WhenDeselectAfterClickIsOff()
        {
            var button = CreateUIObject("Button").AddComponent<UIButton>();
            button.deselectAfterClick = false;
            yield return null;

            PointerEventData data = LeftClick();
            button.OnPointerDown(data);
            button.OnPointerUp(data);
            button.OnPointerClick(data);

            Assert.AreEqual(button.gameObject, _eventSystem.currentSelectedGameObject);
        }

        [UnityTest]
        public IEnumerator ButtonSubmit_KeepsSelection_SoKeyboardNavigationStillWorks()
        {
            var button = CreateUIObject("Button").AddComponent<UIButton>();
            yield return null;

            _eventSystem.SetSelectedGameObject(button.gameObject);
            button.OnSubmit(new BaseEventData(_eventSystem));

            Assert.AreEqual(button.gameObject, _eventSystem.currentSelectedGameObject,
                "Submit is keyboard/gamepad-originated — deselecting would strand navigation");
        }

        [UnityTest]
        public IEnumerator TogglePointerClick_ClearsSelection()
        {
            var toggle = CreateUIObject("Toggle").AddComponent<UIToggle>();
            yield return null;

            PointerEventData data = LeftClick();
            toggle.OnPointerDown(data);
            toggle.OnPointerUp(data);
            toggle.OnPointerClick(data);

            Assert.IsNull(_eventSystem.currentSelectedGameObject);
        }
    }
}
