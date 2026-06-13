using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AlterEyes.UI
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

        public override string ToString() => $"{type} {category}/{viewName}{(instant ? " (instant)" : "")}";
    }

    /// <summary>
    /// A container addressed by category/name, globally controllable without references:
    /// <c>UIView.Show("Menu", "Main")</c>. The static API rides the "UIView/Command" signal stream,
    /// so flows, portals and game code can drive (and observe) navigation through the same channel.
    /// Visibility changes are published on "UIView/VisibilityChanged".
    /// </summary>
    [AddComponentMenu("AlterEyes/UI/Containers/UI View")]
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

        public static void Hide(string category, string name, bool instant = false) =>
            SendCommand(new ViewCommand { type = ViewCommand.CommandType.Hide, category = category, viewName = name, instant = instant });

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
                            view.Execute(true, command.instant);
                            matched++;
                        }
                        break;
                    case ViewCommand.CommandType.Hide:
                        if (view.id.Matches(command.category, command.viewName))
                        {
                            view.Execute(false, command.instant);
                            matched++;
                        }
                        break;
                    case ViewCommand.CommandType.ShowCategory:
                        if (view.id.Category == command.category) view.Execute(true, command.instant);
                        break;
                    case ViewCommand.CommandType.HideCategory:
                        if (view.id.Category == command.category) view.Execute(false, command.instant);
                        break;
                    case ViewCommand.CommandType.HideAll:
                        view.Execute(false, command.instant);
                        break;
                }
            }

            // a by-name command that hit nothing is almost always a bug (typo, view not in the
            // scene, registry race) — fail loudly instead of black-screening silently
            if (matched == 0 &&
                (command.type == ViewCommand.CommandType.Show || command.type == ViewCommand.CommandType.Hide))
                Debug.LogWarning($"[AlterEyes.UI] UIView.{command.type}: no registered view matches " +
                                 $"'{command.category}/{command.viewName}' ({targets.Count} views registered).");
        }

        private void Execute(bool show, bool instant)
        {
            if (show)
            {
                if (instant) InstantShow();
                else Show();
            }
            else
            {
                if (instant) InstantHide();
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
