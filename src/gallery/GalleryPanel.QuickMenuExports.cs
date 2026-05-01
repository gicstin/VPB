using System.Collections.Generic;

namespace VPB
{
    public partial class GalleryPanel
    {
        internal class QuickMenuSaveOption
        {
            public string Label;
            public string Tooltip;
            public System.Action Action;
            public bool Enabled;
        }

        // Internal wrappers so VamHookPlugin quick-menu buttons can trigger existing private actions.
        // (GalleryPanel is partial, so this file can call private members defined in other parts.)

        internal void QuickMenu_Undo()
        {
            try { Undo(); } catch { }
        }

        internal void QuickMenu_Redo()
        {
            try { Redo(); } catch { }
        }

        internal void QuickMenu_LoadRandom()
        {
            try { LoadRandom(); } catch { }
        }

        internal void QuickMenu_Save()
        {
            // Save scene from gallery (opens VaM file dialog)
            try { SaveSceneFromGallery(); } catch { }
        }

        internal List<QuickMenuSaveOption> QuickMenu_GetSaveOptions()
        {
            var res = new List<QuickMenuSaveOption>();
            try
            {
                var opts = BuildSaveMenuOptions();
                if (opts == null) return res;
                for (int i = 0; i < opts.Count; i++)
                {
                    var o = opts[i];
                    if (o == null) continue;
                    res.Add(new QuickMenuSaveOption
                    {
                        Label = o.Label,
                        Tooltip = o.Tooltip,
                        Action = o.Action,
                        Enabled = o.Enabled,
                    });
                }
            }
            catch { }
            return res;
        }

        internal void QuickMenu_ToggleReplaceMode()
        {
            try { ToggleReplaceMode(); } catch { }
        }

        internal void QuickMenu_ToggleAutoHide()
        {
            try { ToggleAutoHideMode(); } catch { }
        }

        internal void QuickMenu_ToggleShowHiddenPackages()
        {
            try { ToggleGalleryShowHiddenPackages(); } catch { }
        }

        internal void QuickMenu_ToggleFpsCounter()
        {
            try
            {
                if (fpsText != null && fpsText.gameObject != null)
                    fpsText.gameObject.SetActive(!fpsText.gameObject.activeSelf);
            }
            catch { }
        }

        /// <summary>Same as the History side button: toggle History browse / usage filters.</summary>
        internal void QuickMenu_OpenGalleryHistory()
        {
            try
            {
                if (isFixedLocally) ToggleLeft(ContentType.History);
                else ToggleRight(ContentType.History);
            }
            catch { }
        }

        /// <summary>Open Cleanup mode using the same entry point as the toolbox Cleanup action.</summary>
        internal void QuickMenu_OpenCleanupMode()
        {
            try { TboxOpenCleanupView(); } catch { }
        }

        /// <summary>Toggle Cleanup mode: open when closed, exit to previous side state when open.</summary>
        internal void QuickMenu_ToggleCleanupMode()
        {
            try
            {
                if (cleanupModeActive) ExitCleanupModeForSidePanelNavigation();
                else TboxOpenCleanupView();
            }
            catch { }
        }
    }
}

