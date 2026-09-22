using UnityEngine;
using Xunit;

namespace VPB.Tests
{
    [Collection(VamCollection.Name)]
    public class PluginHotkeyTests
    {
        public PluginHotkeyTests(VamFixture vam) { }

        [Fact]
        public void HomeReplacesCtrlGAsABareKeyThePollerCanLoadBack()
        {
            KeyUtil gallery;
            KeyUtil create;
            KeyUtil hub;
            KeyUtil clear;
            string error;
            bool ok = KeyUtil.TryParsePluginHotkeySet(
                "Home", "Ctrl+N", "Ctrl+H", "F2",
                out gallery, out create, out hub, out clear, out error);

            Assert.True(ok, "Show/Hide Panes set to Home must be accepted. The chip was showing Home while Ctrl+G stayed live.");
            Assert.Equal(KeyCode.Home, gallery.key);
            Assert.Empty(gallery.supportKeys);
            Assert.Equal("Home", gallery.keyPattern);

            KeyUtil reloaded = KeyUtil.Parse(gallery.keyPattern);
            Assert.Equal(KeyCode.Home, reloaded.key);
            Assert.Empty(reloaded.supportKeys);
        }

        [Fact]
        public void ClearedPluginHotkeyIsOffInsteadOfAMouseRejection()
        {
            KeyUtil gallery;
            KeyUtil create;
            KeyUtil hub;
            KeyUtil clear;
            string error;
            bool ok = KeyUtil.TryParsePluginHotkeySet(
                "", "Ctrl+N", "Ctrl+H", "F2",
                out gallery, out create, out hub, out clear, out error);

            Assert.True(ok, "CLEAR on Show/Hide Panes must turn that hotkey off. A blank binding was rejected, so the old Ctrl+G binding stayed active.");
            Assert.Equal(KeyCode.None, gallery.key);
        }

        [Fact]
        public void MouseButtonIsRejected()
        {
            KeyUtil gallery;
            KeyUtil create;
            KeyUtil hub;
            KeyUtil clear;
            string error;
            bool ok = KeyUtil.TryParsePluginHotkeySet(
                "Mouse0", "Ctrl+N", "Ctrl+H", "F2",
                out gallery, out create, out hub, out clear, out error);

            Assert.False(ok, "Mouse buttons must not become plugin hotkeys.");
        }

        [Fact]
        public void DuplicatePluginHotkeysAreRejected()
        {
            KeyUtil gallery;
            KeyUtil create;
            KeyUtil hub;
            KeyUtil clear;
            string error;
            bool ok = KeyUtil.TryParsePluginHotkeySet(
                "Ctrl+N", "Ctrl+N", "Ctrl+H", "F2",
                out gallery, out create, out hub, out clear, out error);

            Assert.False(ok, "Two plugin hotkeys on the same combo must be rejected so one press cannot run both.");
        }
    }
}
