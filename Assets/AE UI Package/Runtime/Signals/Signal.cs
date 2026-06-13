using System;
using UnityEngine;

namespace AlterEyes.UI
{
    /// <summary>
    /// A message sent through a <see cref="SignalStream"/>. Payload-less by default;
    /// <see cref="MetaSignal{T}"/> carries a typed payload.
    /// </summary>
    public class Signal
    {
        /// <summary> The stream this signal was sent through. </summary>
        public SignalStream stream { get; internal set; }

        /// <summary> The object that sent the signal (component, plain object — may be null). </summary>
        public object senderObject { get; internal set; }

        /// <summary> The GameObject the signal originated from (may be null). </summary>
        public GameObject sourceGameObject { get; internal set; }

        /// <summary> Unscaled time the signal was sent (Time.realtimeSinceStartup; 0 outside play mode). </summary>
        public float timestamp { get; internal set; }

        /// <summary> Optional human-readable message. </summary>
        public string message { get; internal set; }

        public virtual bool hasValue => false;
        public virtual object valueAsObject => null;
        public virtual Type valueType => null;

        /// <summary> Tries to read the payload as T. False for payload-less signals or type mismatch. </summary>
        public bool TryGetValue<T>(out T value)
        {
            if (hasValue && valueAsObject is T typed)
            {
                value = typed;
                return true;
            }
            value = default;
            return false;
        }

        public T GetValue<T>() => TryGetValue(out T value)
            ? value
            : throw new InvalidOperationException($"Signal on '{stream}' does not carry a {typeof(T).Name} payload");

        public override string ToString() =>
            $"Signal({stream}{(hasValue ? $", {valueType?.Name}" : "")}{(string.IsNullOrEmpty(message) ? "" : $", \"{message}\"")})";

        // ------------------------------------------------------------------ static send API

        /// <summary> Sends a payload-less signal on the (category, name) stream. </summary>
        public static Signal Send(string category, string name, object sender = null, string signalMessage = null) =>
            Signals.Stream(category, name).SendSignal(sender, signalMessage);

        /// <summary> Sends a typed payload signal on the (category, name) stream. </summary>
        public static Signal Send<T>(string category, string name, T payload, object sender = null, string signalMessage = null) =>
            Signals.Stream(category, name).SendSignal(payload, sender, signalMessage);
    }

    /// <summary> A signal carrying a typed payload. </summary>
    public class MetaSignal<T> : Signal
    {
        public T value { get; internal set; }

        public override bool hasValue => true;
        public override object valueAsObject => value;
        public override Type valueType => typeof(T);
    }
}
