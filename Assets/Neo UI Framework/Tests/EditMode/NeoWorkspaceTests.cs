using System;
using Neo.UI.Editor;
using NUnit.Framework;

namespace Neo.UI.Tests
{
    /// <summary>
    /// Contract for <see cref="NeoWorkspace"/> — the blessed, always-restored redirect of
    /// <see cref="UISpecGenerator.GeneratedRoot"/>: it sets the root for the duration of the scope and
    /// restores it on <c>Dispose</c> (even on exception), nests correctly, and refuses to ever scope the
    /// committed demo root or a null/empty root. The original <c>GeneratedRoot</c> is saved/restored
    /// around every test so a failure can never leave the static root dirty for sibling suites.
    /// </summary>
    public class NeoWorkspaceTests
    {
        private string _saved;

        [SetUp]
        public void SaveRoot() => _saved = UISpecGenerator.GeneratedRoot;

        [TearDown]
        public void RestoreRoot() => UISpecGenerator.GeneratedRoot = _saved;

        private const string RootA = "Assets/Showcases/buttons/Generated";
        private const string RootB = "Assets/Showcases/toggles/Generated";

        [Test]
        public void Scoped_SetsRoot_ThenRestoresOnDispose()
        {
            string before = UISpecGenerator.GeneratedRoot;
            using (var ws = NeoWorkspace.Scoped(RootA))
            {
                Assert.AreEqual(RootA, UISpecGenerator.GeneratedRoot);
                Assert.AreEqual(RootA, ws.Root);
            }
            Assert.AreEqual(before, UISpecGenerator.GeneratedRoot);
        }

        [Test]
        public void Scoped_RestoresEvenWhenBodyThrows()
        {
            string before = UISpecGenerator.GeneratedRoot;
            Assert.Throws<InvalidOperationException>(() =>
            {
                using (NeoWorkspace.Scoped(RootA))
                {
                    Assert.AreEqual(RootA, UISpecGenerator.GeneratedRoot);
                    throw new InvalidOperationException("boom");
                }
            });
            Assert.AreEqual(before, UISpecGenerator.GeneratedRoot, "the using-block must restore on exception");
        }

        [Test]
        public void NestedScopes_RestoreInOrder()
        {
            string before = UISpecGenerator.GeneratedRoot;
            using (NeoWorkspace.Scoped(RootA))
            {
                Assert.AreEqual(RootA, UISpecGenerator.GeneratedRoot);
                using (NeoWorkspace.Scoped(RootB))
                {
                    Assert.AreEqual(RootB, UISpecGenerator.GeneratedRoot);
                }
                Assert.AreEqual(RootA, UISpecGenerator.GeneratedRoot, "inner scope restores to the outer root");
            }
            Assert.AreEqual(before, UISpecGenerator.GeneratedRoot);
        }

        [Test]
        public void Scoped_FromShowcase_UsesDerivedRootAndScenePath()
        {
            var showcase = new Showcase { id = "buttons" };
            using (var ws = NeoWorkspace.Scoped(showcase))
            {
                Assert.AreEqual(showcase.GeneratedRoot, UISpecGenerator.GeneratedRoot);
                Assert.AreEqual(showcase.GeneratedRoot, ws.Root);
                Assert.AreEqual(showcase.ScenePath, ws.ScenePath);
            }
        }

        [Test]
        public void Scoped_ThrowsOnDefaultGeneratedRoot()
        {
            Assert.Throws<ArgumentException>(() => NeoWorkspace.Scoped(UISpecGenerator.DefaultGeneratedRoot));
            // and the failed construction must not have changed the root
            Assert.AreEqual(_saved, UISpecGenerator.GeneratedRoot);
        }

        [Test]
        public void Scoped_ThrowsOnNullOrEmptyRoot()
        {
            Assert.Throws<ArgumentException>(() => NeoWorkspace.Scoped((string)null));
            Assert.Throws<ArgumentException>(() => NeoWorkspace.Scoped(""));
            Assert.Throws<ArgumentException>(() => NeoWorkspace.Scoped((Showcase)null));
        }
    }
}
