using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Neo.UI
{
    /// <summary>
    /// Desktop/editor entry point for the back-button system: fires <see cref="BackButton"/> on
    /// Escape (Input System). VR titles call BackButton.Fire from their own controller input instead.
    /// </summary>
    [AddComponentMenu("Neo/UI/Input/Back Button Input")]
    public class BackButtonInput : MonoBehaviour
    {
        private void Update()
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                BackButton.Fire(this);
#endif
        }
    }
}
