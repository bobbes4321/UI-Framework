using UnityEngine;

namespace AlterEyes.UI
{
    /// <summary>
    /// Lightweight static back-button system: one entry point the game calls (VR rigs fire it from
    /// controller input), riding the "Input/BackButton" signal stream. Disable levels are additive
    /// (each Disable() must be matched by an Enable()); fires are cooldown-gated.
    /// </summary>
    public static class BackButton
    {
        public const string StreamCategory = "Input";
        public const string StreamName = "BackButton";

        private static int s_disableLevel;
        private static float s_lastFireTime = float.MinValue;

        public static SignalStream stream => Signals.Stream(StreamCategory, StreamName);

        public static bool isEnabled => s_disableLevel == 0;

        public static float cooldown =>
            AEUISettings.instance != null ? AEUISettings.instance.backButtonCooldown : 0.1f;

        /// <summary> Adds a disable level (nested disables stack). </summary>
        public static void Disable() => s_disableLevel++;

        /// <summary> Removes one disable level. </summary>
        public static void Enable() => s_disableLevel = Mathf.Max(0, s_disableLevel - 1);

        /// <summary> Clears all disable levels. </summary>
        public static void EnableByForce() => s_disableLevel = 0;

        /// <summary> Fires the back button (if enabled and off cooldown). Returns whether it fired. </summary>
        public static bool Fire(object sender = null)
        {
            if (!isEnabled) return false;
            return DoFire(sender);
        }

        /// <summary> Fires regardless of the disabled state (still cooldown-gated). </summary>
        public static bool ForceFire(object sender = null) => DoFire(sender);

        private static bool DoFire(object sender)
        {
            float now = Time.realtimeSinceStartup;
            if (now - s_lastFireTime < cooldown) return false;
            s_lastFireTime = now;
            Signals.Send(StreamCategory, StreamName, sender: sender);
            return true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_disableLevel = 0;
            s_lastFireTime = float.MinValue;
        }
    }
}
