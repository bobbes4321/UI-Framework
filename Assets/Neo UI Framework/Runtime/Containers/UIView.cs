using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Neo.UI
{
    /// <summary> Payload for view commands sent over the "UIView/Command" signal stream. </summary>
    [Serializable]
    public struct ViewCommand
    {
        public enum CommandType
        {
            Show = 0,
            Hide = 1,
            ShowCategory = 2,
            HideCategory = 3,
            HideAll = 4
        }

        public CommandType type;
        public string category;
        public string viewName;
        public bool instant;

        /// <summary>
        /// A view transition's override channel for this one call — in-process only (a signal
        /// payload, never persisted), so an object reference is fine here. Copied into the
        /// receiving <see cref="IContainerAnimator"/>'s own scratch instance, never played
        /// directly. Null = the container's own show/hide animation (pre-transition behavior).
        /// </summary>
        [NonSerialized] public UIAnimation overrideAnimation;

        public override string ToString() => $"{type} {category}/{viewName}{(instant ? " (instant)" : "")}";
    }

    /// <summary>
    /// A container addressed by category/name, globally controllable without references:
    /// <c>UIView.Show("Menu", "Main")</c>. The static API rides the "UIView/Command" signal stream,
    /// so flows, portals and game code can drive (and observe) navigation through the same channel.
    /// Visibility changes are published on "UIView/VisibilityChanged".
    /// </summary>
    [AddComponentMenu("Neo/UI/Containers/UI View")]
    public class UIView : UIContainer
    {
        public const string StreamCategory = "UIView";
        public const string CommandStreamName = "Command";
        public const string VisibilityStreamName = "VisibilityChanged";

        public ViewId id = new ViewId();

        private static readonly HashSet<UIView> Registry = new HashSet<UIView>();
        private static readonly ViewCommandReceiver CommandReceiver = new ViewCommandReceiver();

        private sealed class ViewCommandReceiver : ISignalReceiver
        {
            public void OnSignal(Signal signal)
            {
                if (signal.TryGetValue(out ViewCommand command)) ProcessCommand(command);
            }
        }

        /// <summary> All enabled views. </summary>
        public static IEnumerable<UIView> allViews => Registry;

        public static IEnumerable<UIView> visibleViews =>
            Registry.Where(v => v.visibilityState == VisibilityState.Visible || v.visibilityState == VisibilityState.IsShowing);

        public static IEnumerable<UIView> GetViews(string category, string name) =>
            Registry.Where(v => v.id.Matches(category, name));

        public static UIView GetFirstView(string category, string name) =>
            Registry.FirstOrDefault(v => v.id.Matches(category, name));

        // ------------------------------------------------------------------ static API (rides the signal stream)

        public static void Show(string category, string name, bool instant = false) =>
            SendCommand(new ViewCommand { type = ViewCommand.CommandType.Show, category = category, viewName = name, instant = instant });

        /// <summary>
        /// Shows with a transition-owned override animation instead of the view's own show
        /// animation — the per-call choreography <see cref="ViewTransitionRunner"/> plants over a
        /// navigation cut. Pass the transition asset's channel directly; the receiving animator
        /// copies it into its own scratch instance rather than playing the shared asset live.
        /// </summary>
        public static void Show(string category, string name, UIAnimation overrideAnimation, bool instant = false) =>
            SendCommand(new ViewCommand { type = ViewCommand.CommandType.Show, category = category, viewName = name, instant = instant, overrideAnimation = overrideAnimation });

        public static void Hide(string category, string name, bool instant = false) =>
            SendCommand(new ViewCommand { type = ViewCommand.CommandType.Hide, category = category, viewName = name, instant = instant });

        /// <summary> Hide counterpart of <see cref="Show(string, string, UIAnimation, bool)"/>. </summary>
        public static void Hide(string category, string name, UIAnimation overrideAnimation, bool instant = false) =>
            SendCommand(new ViewCommand { type = ViewCommand.CommandType.Hide, category = category, viewName = name, instant = instant, overrideAnimation = overrideAnimation });

        public static void ShowCategory(string category, bool instant = false) =>
            SendCommand(new ViewCommand { type = ViewCommand.CommandType.ShowCategory, category = category, instant = instant });

        public static void HideCategory(string category, bool instant = false) =>
            SendCommand(new ViewCommand { type = ViewCommand.CommandType.HideCategory, category = category, instant = instant });

        public static void HideAllViews(bool instant = false) =>
            SendCommand(new ViewCommand { type = ViewCommand.CommandType.HideAll, instant = instant });

        private static void SendCommand(ViewCommand command)
        {
            EnsureCommandReceiver();
            Signals.Send(StreamCategory, CommandStreamName, command);
        }

        private static void EnsureCommandReceiver()
        {
            SignalStream stream = Signals.Stream(StreamCategory, CommandStreamName);
            if (!stream.IsConnected(CommandReceiver)) stream.ConnectReceiver(CommandReceiver);
        }

        private static void ProcessCommand(ViewCommand command)
        {
            // snapshot — executing commands can enable/disable views and mutate the registry
            var targets = new List<UIView>(Registry);
            int matched = 0;
            foreach (UIView view in targets)
            {
                switch (command.type)
                {
                    case ViewCommand.CommandType.Show:
                        if (view.id.Matches(command.category, command.viewName))
                        {
                            view.Execute(true, command.instant, command.overrideAnimation);
                            matched++;
                        }
                        break;
                    case ViewCommand.CommandType.Hide:
                        if (view.id.Matches(command.category, command.viewName))
                        {
                            view.Execute(false, command.instant, command.overrideAnimation);
                            matched++;
                        }
                        break;
                    case ViewCommand.CommandType.ShowCategory:
                        if (view.id.Category == command.category)
                        {
                            view.Execute(true, command.instant, command.overrideAnimation);
                            matched++;
                        }
                        break;
                    case ViewCommand.CommandType.HideCategory:
                        if (view.id.Category == command.category)
                        {
                            view.Execute(false, command.instant, command.overrideAnimation);
                            matched++;
                        }
                        break;
                    case ViewCommand.CommandType.HideAll:
                        view.Execute(false, command.instant, command.overrideAnimation);
                        break;
                }
            }

            // a by-name or by-category command that hit nothing is almost always a bug (typo, view
            // not in the scene, registry race) — fail loudly instead of black-screening silently
            if (matched == 0)
            {
                switch (command.type)
                {
                    case ViewCommand.CommandType.Show:
                    case ViewCommand.CommandType.Hide:
                        Debug.LogWarning($"[Neo.UI] UIView.{command.type}: no registered view matches " +
                                         $"'{command.category}/{command.viewName}' ({targets.Count} views registered).");
                        break;
                    case ViewCommand.CommandType.ShowCategory:
                    case ViewCommand.CommandType.HideCategory:
                        Debug.LogWarning($"[Neo.UI] UIView.{command.type}: no registered view matches " +
                                         $"category '{command.category}' ({targets.Count} views registered).");
                        break;
                }
            }
        }

        private void Execute(bool show, bool instant, UIAnimation overrideAnimation)
        {
            if (show)
            {
                // instant has no timeline to play the override on — it stays a plain instant snap
                if (instant) InstantShow();
                else if (overrideAnimation != null) Show(overrideAnimation);
                else Show();
            }
            else
            {
                if (instant) InstantHide();
                else if (overrideAnimation != null) Hide(overrideAnimation);
                else Hide();
            }
        }

        // ------------------------------------------------------------------ lifecycle

        protected virtual void OnEnable()
        {
            EnsureCommandReceiver();
            Registry.Add(this);
            OnVisibilityChanged += PublishVisibility;
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            Registry.Remove(this);
            OnVisibilityChanged -= PublishVisibility;
        }

        private void PublishVisibility(VisibilityState state)
        {
            Signals.Send(StreamCategory, VisibilityStreamName,
                new ViewVisibilityData { category = id.Category, viewName = id.Name, state = state }, this);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Registry.Clear();
        }
    }

    /// <summary> Payload published on "UIView/VisibilityChanged" whenever any view changes state. </summary>
    [Serializable]
    public struct ViewVisibilityData
    {
        public string category;
        public string viewName;
        public VisibilityState state;
    }
}
