using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace VPB
{
    /// <summary>
    /// Keyboard path + soft-delete undo + collapse mini palette for filter presets.
    /// Warm path only (visible panel / key events).
    /// </summary>
    public partial class QuickFiltersUI
    {
        /// <summary>
        /// Esc / arrows / Enter / D / Ctrl+S / U while presets panel visible.
        /// Returns true when consumed.
        /// </summary>
        public bool TryHandleKeyboard()
        {
            ExpireSoftDeleteIfNeeded();

            if (!IsVisible) return false;

            // Soft-delete undo works even when rename field focused? Prefer when not typing.
            bool inputFocused = IsAnyInputFieldFocused();

            if (!inputFocused || renamingEntry == null)
            {
                if (Input.GetKeyDown(KeyCode.U) && softDeleteEntry != null && Time.unscaledTime <= softDeleteExpireTime)
                {
                    TryUndoSoftDelete();
                    return true;
                }
            }

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (TryCancelPendingDelete())
                    return true;
                if (mergeMode)
                {
                    ExitMergeMode();
                    return true;
                }
                if (detached)
                    HideFloatKeepDetach();
                else
                    SetVisible(false);
                if (panel != null) panel.SyncQuickFilterToggleState();
                return true;
            }

            if (inputFocused) return false;
            if (mergeMode || renamingEntry != null || pendingDeleteEntry != null) return false;

            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);

            if (ctrl && Input.GetKeyDown(KeyCode.S))
            {
                // Expert: typed list-search becomes name. Else Update if dirty active, else Save+rename.
                string typed = (listFilter ?? "").Trim();
                if (typed.Length == 0 && searchInput != null)
                    typed = (searchInput.text ?? "").Trim();

                if (typed.Length > 0)
                {
                    CaptureCurrentFilter(useListSearchAsName: true);
                }
                else if (panel != null
                    && panel.ActiveQuickFilter != null
                    && panel.IsActiveQuickFilterDirty())
                {
                    UpdateActivePreset();
                }
                else
                {
                    CaptureCurrentFilter(useListSearchAsName: false);
                }
                return true;
            }

            if (ctrl || alt) return false;

            List<QuickFilterEntry> display = BuildDisplayList();
            if (display.Count == 0) return false;

            if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.UpArrow))
            {
                int idx = IndexOfDisplayEntry(display, keyboardFocusEntry);
                if (Input.GetKeyDown(KeyCode.DownArrow))
                    idx = idx < 0 ? 0 : Mathf.Min(display.Count - 1, idx + 1);
                else
                    idx = idx < 0 ? 0 : Mathf.Max(0, idx - 1);
                keyboardFocusEntry = display[idx];
                Refresh();
                return true;
            }

            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                QuickFilterEntry e = ResolveKeyboardTarget(display);
                if (e != null)
                {
                    ApplyFilter(e);
                    HideAfterApplyIfDocked();
                }
                return true;
            }

            if (Input.GetKeyDown(KeyCode.D))
            {
                QuickFilterEntry e = ResolveKeyboardTarget(display);
                if (e != null && panel != null)
                    panel.RandomizeFromFilterPreset(e, true);
                return true;
            }

            return false;
        }

        private static bool IsAnyInputFieldFocused()
        {
            try
            {
                if (EventSystem.current == null) return false;
                GameObject sel = EventSystem.current.currentSelectedGameObject;
                if (sel == null) return false;
                return sel.GetComponent<InputField>() != null;
            }
            catch { return false; }
        }

        private static int IndexOfDisplayEntry(List<QuickFilterEntry> display, QuickFilterEntry entry)
        {
            if (display == null || entry == null) return -1;
            for (int i = 0; i < display.Count; i++)
            {
                if (ReferenceEquals(display[i], entry)) return i;
            }
            return -1;
        }

        private QuickFilterEntry ResolveKeyboardTarget(List<QuickFilterEntry> display)
        {
            if (display == null || display.Count == 0) return null;
            if (keyboardFocusEntry != null)
            {
                for (int i = 0; i < display.Count; i++)
                {
                    if (ReferenceEquals(display[i], keyboardFocusEntry))
                        return keyboardFocusEntry;
                }
            }
            if (panel != null && panel.ActiveQuickFilter != null)
            {
                for (int i = 0; i < display.Count; i++)
                {
                    if (ReferenceEquals(display[i], panel.ActiveQuickFilter))
                        return panel.ActiveQuickFilter;
                }
            }
            return display[0];
        }

        private void UpdateActivePreset()
        {
            if (panel == null) return;
            QuickFilterEntry active = panel.ActiveQuickFilter;
            if (!panel.UpdateActiveQuickFilterFromLive()) return;
            flashEntry = active;
            Refresh();
        }

        private bool HasSoftDeletePending()
        {
            ExpireSoftDeleteIfNeeded();
            return softDeleteEntry != null && Time.unscaledTime <= softDeleteExpireTime;
        }

        private void ExpireSoftDeleteIfNeeded()
        {
            if (softDeleteEntry == null) return;
            if (Time.unscaledTime > softDeleteExpireTime)
            {
                softDeleteEntry = null;
                softDeleteIndex = -1;
                SyncSoftDeleteUndoButton();
            }
        }

        private void CommitSoftDelete(QuickFilterEntry entry)
        {
            if (entry == null) return;
            int idx = QuickFilterSettings.Instance.IndexOfFilter(entry);
            QuickFilterSettings.Instance.RemoveFilter(entry);
            if (panel != null) panel.NotifyQuickFilterRemoved(entry);

            softDeleteEntry = entry;
            softDeleteIndex = idx;
            softDeleteExpireTime = Time.unscaledTime + SoftDeleteUndoSeconds;
            pendingDeleteEntry = null;
            expandedMoreEntry = null;
            if (ReferenceEquals(keyboardFocusEntry, entry))
                keyboardFocusEntry = null;
            SyncSoftDeleteUndoButton();
            Refresh();

            if (panel != null)
            {
                panel.ShowTemporaryStatus(
                    string.Format(
                        VPBTranslation.T("quickfilters.soft_deleted", "Deleted '{0}' — Undo (U) for {1}s"),
                        entry.Name ?? "",
                        (int)SoftDeleteUndoSeconds),
                    SoftDeleteUndoSeconds);
            }
        }

        private void TryUndoSoftDelete()
        {
            ExpireSoftDeleteIfNeeded();
            if (softDeleteEntry == null) return;
            QuickFilterEntry e = softDeleteEntry;
            int idx = softDeleteIndex;
            softDeleteEntry = null;
            softDeleteIndex = -1;
            QuickFilterSettings.Instance.InsertFilterAt(e, idx);
            SyncSoftDeleteUndoButton();
            flashEntry = e;
            Refresh();
            if (panel != null)
            {
                panel.ShowTemporaryStatus(
                    string.Format(
                        VPBTranslation.T("quickfilters.soft_restored", "Restored preset '{0}'"),
                        e.Name ?? ""),
                    2f);
            }
        }

        private void SyncSoftDeleteUndoButton()
        {
            bool show = HasSoftDeletePending();
            if (floatPresetUndoBtnGO != null && floatPresetUndoBtnGO.activeSelf != show)
                floatPresetUndoBtnGO.SetActive(show);
        }

        private void SyncCollapsePalette()
        {
            if (collapsePaletteGO == null) return;
            // Clear children
            for (int i = collapsePaletteGO.transform.childCount - 1; i >= 0; i--)
            {
                Transform ch = collapsePaletteGO.transform.GetChild(i);
                if (ch != null) GameObject.Destroy(ch.gameObject);
            }

            bool show = detached && floatCollapsed;
            collapsePaletteGO.SetActive(show);
            if (!show || panel == null) return;

            var pinned = new List<QuickFilterEntry>(4);
            try { QuickFilterSettings.Instance.CollectPinnedFilters(pinned); } catch { }
            // Match title collapse/close: design ButtonSizeRef, then ChromeScale via ScaleChromeIconBtn.
            float sq = GalleryUiDesignTokens.ButtonSizeRef;
            float s = panel.ChromeScale > 0f ? panel.ChromeScale : 1f;
            float sortSq = sq * s;
            int n = Mathf.Min(4, pinned.Count);
            for (int i = 0; i < n; i++)
            {
                QuickFilterEntry pe = pinned[i];
                if (pe == null) continue;
                QuickFilterEntry captured = pe;
                GameObject btn = UI.CreateUIButton(collapsePaletteGO, sq, sq, " ", 12, 0, 0, AnchorPresets.middleCenter, () =>
                {
                    if (panel != null) panel.RandomizeFromFilterPreset(captured, true);
                });
                if (btn == null) continue;
                btn.name = "CollapseDice";
                StyleChromeIconBtn(btn, sq, "vpb_icons/random.png", new Color(0.18f, 0.32f, 0.28f, 1f));
                ScaleChromeIconBtn(btn, sortSq, s);
                var h = btn.AddComponent<UIHoverDelegate>();
                h.OnHoverChange += (enter) =>
                {
                    if (panel == null) return;
                    if (enter)
                    {
                        panel.SetStatus(string.Format(
                            VPBTranslation.T("quickfilters.tip.randomize", "Dice: random item from '{0}', restores current view"),
                            captured.Name ?? "?"));
                    }
                    else panel.SetStatus(null);
                };
            }
            ScaleCollapsePaletteChrome(sortSq, s);
        }
    }
}
