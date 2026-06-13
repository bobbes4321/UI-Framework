using System;
using System.Collections.Generic;
using UnityEngine;

namespace AlterEyes.UI
{
    /// <summary>
    /// Static signal service: the stream registry plus the code-first subscribe API.
    /// <code>
    /// Signals.On("Gameplay", "StartPainting", () => StartPainting());
    /// Signals.On&lt;int&gt;("Shop", "ItemBought", itemId => Handle(itemId));
    /// Signals.Off("Gameplay", "StartPainting", handler);
    /// </code>
    /// Prefer this over serialized UnityEvents for UI→gameplay wiring — it stays greppable C#.
    /// </summary>
    public static class Signals
    {
        private sealed class DelegateReceiver : ISignalReceiver
        {
            public Delegate userHandler;
            public Action<Signal> invoke;
            public void OnSignal(Signal signal) => invoke(signal);
        }

        private static readonly Dictionary<string, SignalStream> Streams = new Dictionary<string, SignalStream>(StringComparer.Ordinal);
        private static readonly List<DelegateReceiver> DelegateReceivers = new List<DelegateReceiver>();
        private static readonly Dictionary<DelegateReceiver, SignalStream> ReceiverStreams = new Dictionary<DelegateReceiver, SignalStream>();

        /// <summary> Gets (or creates) the stream addressed by category + name. </summary>
        public static SignalStream Stream(string category, string name)
        {
            category = string.IsNullOrWhiteSpace(category) ? CategoryNameId.DefaultCategory : category.Trim();
            name = string.IsNullOrWhiteSpace(name) ? CategoryNameId.DefaultName : name.Trim();
            string key = $"{category}/{name}";
            if (!Streams.TryGetValue(key, out SignalStream stream))
            {
                stream = new SignalStream(category, name);
                Streams[key] = stream;
            }
            return stream;
        }

        public static SignalStream Stream(StreamId id) => Stream(id?.Category, id?.Name);

        public static bool StreamExists(string category, string name) => Streams.ContainsKey($"{category}/{name}");

        public static IEnumerable<SignalStream> GetAllStreams() => Streams.Values;

        // ------------------------------------------------------------------ send

        public static Signal Send(string category, string name, object sender = null, string message = null) =>
            Stream(category, name).SendSignal(sender, message);

        public static MetaSignal<T> Send<T>(string category, string name, T payload, object sender = null, string message = null) =>
            Stream(category, name).SendSignal(payload, sender, message);

        // ------------------------------------------------------------------ subscribe

        /// <summary> Subscribes a parameterless handler. </summary>
        public static void On(string category, string name, Action handler)
        {
            if (handler == null) return;
            Connect(category, name, handler, _ => handler());
        }

        /// <summary> Subscribes a handler that receives the full signal (sender, message, payload). </summary>
        public static void On(string category, string name, Action<Signal> handler)
        {
            if (handler == null) return;
            Connect(category, name, handler, handler);
        }

        /// <summary> Subscribes a typed-payload handler; only invoked when the payload is a T. </summary>
        public static void On<T>(string category, string name, Action<T> handler)
        {
            if (handler == null) return;
            Connect(category, name, handler, signal =>
            {
                if (signal.TryGetValue(out T value)) handler(value);
            });
        }

        public static void Off(string category, string name, Action handler) => Disconnect(category, name, handler);
        public static void Off(string category, string name, Action<Signal> handler) => Disconnect(category, name, handler);
        public static void Off<T>(string category, string name, Action<T> handler) => Disconnect(category, name, handler);

        private static void Connect(string category, string name, Delegate userHandler, Action<Signal> invoke)
        {
            SignalStream stream = Stream(category, name);
            var receiver = new DelegateReceiver { userHandler = userHandler, invoke = invoke };
            DelegateReceivers.Add(receiver);
            ReceiverStreams[receiver] = stream;
            stream.ConnectReceiver(receiver);
        }

        private static void Disconnect(string category, string name, Delegate userHandler)
        {
            if (userHandler == null) return;
            SignalStream stream = Stream(category, name);
            for (int i = DelegateReceivers.Count - 1; i >= 0; i--)
            {
                DelegateReceiver receiver = DelegateReceivers[i];
                if (!Equals(receiver.userHandler, userHandler)) continue;
                if (!ReceiverStreams.TryGetValue(receiver, out SignalStream owner) || owner != stream) continue;
                stream.DisconnectReceiver(receiver);
                DelegateReceivers.RemoveAt(i);
                ReceiverStreams.Remove(receiver);
            }
        }

        /// <summary> Removes all streams and delegate subscriptions (test isolation / domain-reload-off safety). </summary>
        public static void ClearAll()
        {
            Streams.Clear();
            DelegateReceivers.Clear();
            ReceiverStreams.Clear();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => ClearAll();
    }
}
