using AlterEyes.UI;
using NUnit.Framework;
using UnityEngine;

namespace AlterEyes.UI.Tests
{
    public class SignalsTests
    {
        [SetUp]
        public void SetUp() => Signals.ClearAll();

        [TearDown]
        public void TearDown() => Signals.ClearAll();

        [Test]
        public void Stream_IsGetOrCreate()
        {
            SignalStream first = Signals.Stream("Music", "Volume");
            SignalStream second = Signals.Stream("Music", "Volume");
            Assert.AreSame(first, second);
            Assert.AreEqual("Music", first.category);
            Assert.AreEqual("Volume", first.name);
            Assert.IsTrue(Signals.StreamExists("Music", "Volume"));
            Assert.IsFalse(Signals.StreamExists("Music", "Pitch"));
        }

        [Test]
        public void Send_ReachesSubscribedHandler()
        {
            int received = 0;
            void Handler() => received++;

            Signals.On("Input", "Fire", Handler);
            Signal.Send("Input", "Fire");
            Assert.AreEqual(1, received);

            Signals.Off("Input", "Fire", (System.Action)Handler);
            Signal.Send("Input", "Fire");
            Assert.AreEqual(1, received, "handler should be removed after Off");
        }

        [Test]
        public void TypedPayload_IsDelivered()
        {
            int receivedValue = 0;
            System.Action<int> handler = v => receivedValue = v;
            Signals.On("Shop", "ItemBought", handler);

            Signal.Send("Shop", "ItemBought", 42);
            Assert.AreEqual(42, receivedValue);
        }

        [Test]
        public void TypedHandler_IgnoresMismatchedPayload()
        {
            bool called = false;
            Signals.On<string>("Shop", "ItemBought", _ => called = true);
            Signal.Send("Shop", "ItemBought", 42); // int payload, string handler
            Assert.IsFalse(called);
        }

        [Test]
        public void SignalCarriesMetadata()
        {
            Signal received = null;
            Signals.On("Meta", "Test", (Signal s) => received = s);
            Signals.Send("Meta", "Test", "payload-text", sender: this, message: "hello");

            Assert.IsNotNull(received);
            Assert.AreSame(this, received.senderObject);
            Assert.AreEqual("hello", received.message);
            Assert.IsTrue(received.hasValue);
            Assert.AreEqual(typeof(string), received.valueType);
            Assert.IsTrue(received.TryGetValue(out string text));
            Assert.AreEqual("payload-text", text);
        }

        [Test]
        public void SignalReceiver_ConnectsAndDisconnects()
        {
            var receiver = new SignalReceiver("Music", "Mute");
            int count = 0;
            receiver.SetOnSignalCallback(_ => count++);

            receiver.Connect();
            Assert.IsTrue(receiver.isConnected);
            Assert.AreEqual(1, Signals.Stream("Music", "Mute").receiverCount);

            Signal.Send("Music", "Mute");
            Assert.AreEqual(1, count);

            receiver.Disconnect();
            Assert.IsFalse(receiver.isConnected);
            Signal.Send("Music", "Mute");
            Assert.AreEqual(1, count);
        }

        [Test]
        public void ReceiverCanDisconnectDuringDelivery()
        {
            var receiver = new SignalReceiver("Test", "Reentrant");
            receiver.SetOnSignalCallback(_ => receiver.Disconnect());
            receiver.Connect();

            Assert.DoesNotThrow(() => Signal.Send("Test", "Reentrant"));
            Assert.IsFalse(receiver.isConnected);
        }

        [Test]
        public void LastSignal_IsRecorded()
        {
            SignalStream stream = Signals.Stream("Track", "Me");
            Assert.IsNull(stream.lastSignal);
            stream.SendSignal();
            Assert.IsNotNull(stream.lastSignal);
        }

        [Test]
        public void BackButton_RespectsDisableLevels()
        {
            int fired = 0;
            Signals.On(BackButton.StreamCategory, BackButton.StreamName, () => fired++);

            Assert.IsTrue(BackButton.isEnabled);
            BackButton.Disable();
            BackButton.Disable();
            Assert.IsFalse(BackButton.isEnabled);
            Assert.IsFalse(BackButton.Fire());
            Assert.AreEqual(0, fired);

            BackButton.Enable();
            Assert.IsFalse(BackButton.isEnabled, "two disables need two enables");
            BackButton.EnableByForce();
            Assert.IsTrue(BackButton.isEnabled);

            Assert.IsTrue(BackButton.ForceFire());
            Assert.AreEqual(1, fired);
        }
    }
}
