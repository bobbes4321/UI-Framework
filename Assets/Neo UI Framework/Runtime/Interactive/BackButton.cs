using System;
using System.Collections.Generic;
using UnityEngine;

namespace Neo.UI
{
    /// <summary>
    /// Lightweight static back-button system riding the "Input/BackButton" signal stream. Back can
    /// originate three ways, all converging here: hardware input (<see cref="BackButtonInput"/>, or
    /// a VR rig calling <see cref="Fire"/> from its own controller input), any clicked
    /// <see cref="UIButton"/> whose id Name matches <see cref="NeoUISettings.backButtonName"/>
    /// ("Back" by default — the button bridge, no wiring needed), or a direct <see cref="Fire"/>
    /// call. Disable levels are additive (each Disable() must be matched by an Enable()); fires are
    /// cooldown-gated. A fire that originated from a button click carries its
    /// <see cref="ButtonSignalData"/> as the signal payload so consumers can tell where it came from.
    /// </summary>
    public static class BackButton
    {
        public const string StreamCategory = "Input";
        public const string StreamName = "BackButton";

        private static int s_disableLevel;
        private static float s_lastFireTime = float.MinValue;
        private static readonly HashSet<string> ExtraButtonNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly ButtonBridge Bridge = new ButtonBridge();
        private static SignalStream s_bridgedButtonStream;

        public static SignalStream stream => Signals.Stream(StreamCategory, StreamName);

        public static bool isEnabled => s_disableLevel == 0;

        public static float cooldown =>
            NeoUISettings.instance != null ? NeoUISettings.instance.backButtonCooldown : 0.1f;

        /// <summary>
        /// The button id Name that auto-fires back when clicked (<see cref="NeoUISettings.backButtonName"/>;
        /// "Back" when no settings asset exists). Empty disables the convention.
        /// </summary>
        public static string configuredButtonName =>
            NeoUISettings.instance != null ? NeoUISettings.instance.backButtonName : "Back";

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

        // ------------------------------------------------------------------ back-named buttons

        /// <summary>
        /// Registers an additional button id Name (case-insensitive) that fires back when clicked —
        /// the project seam next to the single configured <see cref="NeoUISettings.backButtonName"/>.
        /// </summary>
        public static void RegisterButtonName(string buttonName)
        {
            if (!string.IsNullOrEmpty(buttonName)) ExtraButtonNames.Add(buttonName);
        }

        public static void UnregisterButtonName(string buttonName)
        {
            if (!string.IsNullOrEmpty(buttonName)) ExtraButtonNames.Remove(buttonName);
        }

        /// <summary> Whether a button id Name counts as a back button (case-insensitive). </summary>
        public static bool IsBackButtonName(string buttonName)
        {
            if (string.IsNullOrEmpty(buttonName)) return false;
            string configured = configuredButtonName;
            if (!string.IsNullOrEmpty(configured) &&
                string.Equals(buttonName, configured, StringComparison.OrdinalIgnoreCase)) return true;
            return ExtraButtonNames.Contains(buttonName);
        }

        /// <summary>
        /// Connects the button bridge: any UIButton Click/Submit whose id Name passes
        /// <see cref="IsBackButtonName"/> fires back automatically. Idempotent, and reconnects when
        /// the signal streams have been reset — called from <see cref="FlowController.StartFlow"/>
        /// and <see cref="BackButtonInput"/>, so any scene with either gets the convention for free.
        /// </summary>
        public static void EnsureButtonBridge()
        {
            SignalStream buttonStream = Signals.Stream(UIButton.StreamCategory, UIButton.StreamName);
            if (ReferenceEquals(buttonStream, s_bridgedButtonStream)) return;
            s_bridgedButtonStream?.DisconnectReceiver(Bridge);
            s_bridgedButtonStream = buttonStream;
            buttonStream.ConnectReceiver(Bridge);
        }

        private sealed class ButtonBridge : ISignalReceiver
        {
            public void OnSignal(Signal signal)
            {
                if (!signal.TryGetValue(out ButtonSignalData data)) return;
                if (data.trigger != BehaviourTrigger.Click && data.trigger != BehaviourTrigger.Submit) return;
                if (!IsBackButtonName(data.buttonName)) return;
                if (!isEnabled) return;
                DoFire(signal.senderObject, data);
            }
        }

        // ------------------------------------------------------------------ fire

        private static bool DoFire(object sender, ButtonSignalData? source = null)
        {
            float now = Time.realtimeSinceStartup;
            if (now - s_lastFireTime < cooldown) return false;
            s_lastFireTime = now;
            // button-originated fires carry their source so consumers (FlowController) can yield to
            // explicit ButtonClick edges wired to the same button instead of double-navigating
            if (source.HasValue) Signals.Send(StreamCategory, StreamName, source.Value, sender);
            else Signals.Send(StreamCategory, StreamName, sender: sender);
            return true;
        }

        /// <summary> Lets tests fire repeatedly without waiting out the cooldown window. </summary>
        internal static void ResetCooldown() => s_lastFireTime = float.MinValue;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_disableLevel = 0;
            s_lastFireTime = float.MinValue;
            s_bridgedButtonStream = null;
            ExtraButtonNames.Clear();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ConnectBridgeOnLoad() => EnsureButtonBridge();
    }
}
