using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Neo.UI
{
    /// <summary>
    /// Hardware entry point for the back-button system (Input System): fires <see cref="BackButton"/>
    /// on Escape and the gamepad east button (B / Circle — the standard UI cancel), plus an optional
    /// project-supplied action for custom rigs (XR controllers, rebindable Cancel). Auto-added by
    /// <see cref="FlowController"/> when a flow starts with none in the scene, so back input works
    /// out of the box; VR titles can instead call BackButton.Fire from their own controller input.
    /// </summary>
    [AddComponentMenu("Neo/UI/Input/Back Button Input")]
    public class BackButtonInput : MonoBehaviour
    {
        [Tooltip("Fire back on the Escape key (also the Android hardware back button)")]
        public bool escapeKey = true;

        [Tooltip("Fire back on the gamepad east button (B on Xbox, Circle on PlayStation)")]
        public bool gamepadCancel = true;

#if ENABLE_INPUT_SYSTEM
        [Tooltip("Optional extra binding that fires back when performed (e.g. a rebindable Cancel " +
                 "action) — enabled/disabled with this component")]
        public InputActionReference backAction;
#endif

        private void OnEnable()
        {
            // back-named buttons should work anywhere back input does, controller or not
            BackButton.EnsureButtonBridge();
#if ENABLE_INPUT_SYSTEM
            if (backAction != null && backAction.action != null)
            {
                backAction.action.performed += OnBackActionPerformed;
                backAction.action.Enable();
            }
#endif
        }

#if ENABLE_INPUT_SYSTEM
        private void OnDisable()
        {
            if (backAction != null && backAction.action != null)
            {
                backAction.action.performed -= OnBackActionPerformed;
                backAction.action.Disable();
            }
        }

        private void OnBackActionPerformed(InputAction.CallbackContext context) => BackButton.Fire(this);
#endif

        private void Update()
        {
#if ENABLE_INPUT_SYSTEM
            if (escapeKey && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                BackButton.Fire(this);
                return;
            }
            if (gamepadCancel && Gamepad.current != null && Gamepad.current.buttonEast.wasPressedThisFrame)
                BackButton.Fire(this);
#endif
        }
    }
}
