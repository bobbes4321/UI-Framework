using Neo.UI.Editor;
using Neo.UI.Editor.Composer;
using NUnit.Framework;

namespace Neo.UI.Tests
{
    /// <summary>
    /// The Composer's document model (Plan 2): the <see cref="SpecDocument.ApplyEdit"/> choke point
    /// snapshots for undo, and representative GUI edits serialize to exactly the hand-authored spec —
    /// proving the window's edits round-trip losslessly into the spec (the source of truth). Pure
    /// model: no Unity assets, no window.
    /// </summary>
    public class SpecDocumentTests
    {
        /// <summary> Canonicalize both sides through the serializer so the comparison is structural,
        /// not whitespace-sensitive. </summary>
        private static string Canonical(string json) => UISpec.FromJson(json).ToJson();

        [Test]
        public void NewEmptySpec_HasOneEmptyView()
        {
            UISpec spec = SpecDocument.NewEmptySpec();
            Assert.AreEqual(1, spec.views.Count);
            Assert.AreEqual("Menu/Main", spec.views[0].id);
        }

        [Test]
        public void ApplyEdit_MarksDirty_AndRaisesChanged()
        {
            var doc = new SpecDocument();
            int changed = 0;
            doc.Changed += () => changed++;
            Assert.IsFalse(doc.Dirty);

            doc.ApplyEdit(() => doc.Spec.views[0].elements.Add(ComposerFactory.NewElement("text")), "add text");

            Assert.IsTrue(doc.Dirty);
            Assert.AreEqual(1, changed);
            Assert.IsTrue(doc.CanUndo);
            Assert.IsFalse(doc.CanRedo);
        }

        [Test]
        public void Undo_RestoresPreState_Redo_Reapplies()
        {
            var doc = new SpecDocument();
            string before = doc.Spec.ToJson();

            doc.ApplyEdit(() => doc.Spec.views[0].elements.Add(ComposerFactory.NewElement("button")), "add button");
            string after = doc.Spec.ToJson();
            Assert.AreNotEqual(before, after);

            doc.Undo();
            Assert.AreEqual(before, doc.Spec.ToJson(), "undo should restore the pre-edit spec exactly");
            Assert.IsTrue(doc.CanRedo);

            doc.Redo();
            Assert.AreEqual(after, doc.Spec.ToJson(), "redo should reapply the edit exactly");
        }

        [Test]
        public void ApplyEdit_AfterUndo_ClearsRedo()
        {
            var doc = new SpecDocument();
            doc.ApplyEdit(() => doc.Spec.views[0].elements.Add(ComposerFactory.NewElement("text")), "a");
            doc.Undo();
            Assert.IsTrue(doc.CanRedo);
            doc.ApplyEdit(() => doc.Spec.views[0].elements.Add(ComposerFactory.NewElement("button")), "b");
            Assert.IsFalse(doc.CanRedo, "a fresh edit must invalidate the redo stack");
        }

        [Test]
        public void AddTab_SerializesToHandAuthoredSpec()
        {
            var doc = new SpecDocument();
            doc.ApplyEdit(() =>
            {
                ElementSpec tab = ComposerFactory.NewElement("tab");
                tab.controls = "Panel/A";
                tab.id = "Tab/A";
                doc.Spec.views[0].elements.Add(tab);
            }, "add tab");

            const string expected = @"{ ""views"": [ { ""id"": ""Menu/Main"", ""elements"": [
                { ""tab"": { ""id"": ""Tab/A"", ""label"": ""Tab"", ""controls"": ""Panel/A"" } } ] } ] }";
            Assert.AreEqual(Canonical(expected), doc.Spec.ToJson());
        }

        [Test]
        public void ChangeSpacing_SerializesToHandAuthoredSpec()
        {
            var doc = new SpecDocument();
            doc.ApplyEdit(() =>
            {
                var stack = new ElementSpec { kind = "vstack", padding = 16, spacing = 24 };
                doc.Spec.views[0].elements.Add(stack);
            }, "add vstack");

            const string expected = @"{ ""views"": [ { ""id"": ""Menu/Main"", ""elements"": [
                { ""vstack"": { ""padding"": 16, ""spacing"": 24 } } ] } ] }";
            Assert.AreEqual(Canonical(expected), doc.Spec.ToJson());
        }

        [Test]
        public void AddMenuItem_SerializesToHandAuthoredSpec()
        {
            var doc = new SpecDocument();
            doc.ApplyEdit(() =>
            {
                var catalog = ComposerFactory.NewCatalog(MenuCatalogSpec.SettingsKind, "Settings", "Audio");
                var item = ComposerFactory.NewMenuItem("toggle");
                item.category = "Audio";
                item.name = "Music";
                item.label = "Music";
                catalog.items.Add(item);
                doc.Spec.settings.Add(catalog);
            }, "add settings item");

            const string expected = @"{ ""settings"": [ { ""id"": ""Settings/Audio"", ""items"": [
                { ""toggle"": { ""id"": ""Audio/Music"", ""label"": ""Music"", ""value"": true } } ] } ] }";
            Assert.AreEqual(Canonical(expected), doc.Spec.ToJson());
        }

        [Test]
        public void ReplaceFlow_UpdatesSpec_AndMarksDirty()
        {
            var doc = new SpecDocument();
            var flow = new FlowSpec { name = "UI", start = "Main" };
            flow.nodes.Add(new FlowNodeSpec { name = "Main", view = "Menu/Main" });

            doc.ReplaceFlow(flow);

            Assert.IsTrue(doc.Dirty);
            Assert.IsNotNull(doc.Spec.flow);
            Assert.AreEqual("Main", doc.Spec.flow.start);
        }
    }
}
