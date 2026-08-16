using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using MVR.FileManagement;

namespace VPB
{
    /// <summary>
    /// Hover preview for random buttons (desktop quick-menu grid + VR wrist watch + gallery toolbox):
    /// pointing at a random button draws the item it would launch and shows its thumbnail; clicking
    /// launches exactly that item; leaving and returning draws a new one.
    ///
    /// Desktop quick-actions card sits above the tooltip bar (grid is bottom-left HUD chrome).
    /// Toolbox Random greys the file grid and shows a centered card. Empty pool still shows a card.
    ///
    /// Cost control: the current-view pool is sampled by rejection (no copy). A category the gallery is not
    /// showing needs one navigate+refresh, so that runs at most once per category per TTL, only after a
    /// short hover dwell, and its result is kept as a small reel that later hovers pop for free.
    /// </summary>
    public partial class VamHookPlugin
    {
        private const int QmPreviewReelMax = 12;
        private const float QmPreviewReelTtlSec = 90f;
        private const float QmPreviewColdDwellSec = 0.35f;
        private const float QmPreviewRearmSec = 0.75f;
        private const float QmPreviewBuildGiveUpSec = 25f;
        private const string QmPreviewCurrentViewKey = "view";

        // Thumbnails are the gallery's own 1:1 plates, so the widget is sized from a square side plus
        // chrome rather than from an arbitrary box the image would have to stretch into.
        private const float QmPreviewDeskThumb = 138f;
        private const float QmPreviewWatchThumb = 150f;
        private const float QmPreviewDeskGap = 6f;
        private const int QmPreviewDeskFont = 14;
        private const int QmPreviewWatchFont = 13;
        private const float QmPreviewPad = 6f;
        private const float QmPreviewLabelH = 18f;
        private const float QmPreviewSubLabelH = 15f;
        // Fixed px at HUD local scale 1. A fraction of a panel this big rounds the backdrop into a lozenge.
        private const float QmPreviewCornerPx = 6f;

        private static readonly Color QmPreviewBackdrop = new Color(0.06f, 0.06f, 0.07f, 0.94f);
        private static readonly Color QmPreviewLabelColor = new Color(0.78f, 0.78f, 0.82f, 0.95f);
        private static readonly Color QmPreviewThumbPlaceholder = new Color(0.25f, 0.25f, 0.25f, 0.55f);

        private sealed class QmPreviewReel
        {
            public readonly List<FileEntry> Items = new List<FileEntry>(QmPreviewReelMax);
            public int Next;
            public float ExpiresRt;
            public int Epoch;
        }

        private Dictionary<string, QmPreviewReel> m_QmPreviewReels;
        private List<FileEntry> m_QmPreviewScratch;

        private QuickMenuAssignableAction m_QmPreviewAction = QuickMenuAssignableAction.None;
        private string m_QmPreviewCategory;
        private FileEntry m_QmPreviewPick;
        private FileEntry m_QmPreviewLastPick;
        private bool m_QmPreviewOnWatch;
        private bool m_QmPreviewWaiting;
        private bool m_QmPreviewBuilding;
        private float m_QmPreviewBuildStartRt;
        private float m_QmPreviewColdAt = -1f;
        private int m_QmPreviewSerial;

        private GameObject m_QmPreviewDeskGo;
        private RectTransform m_QmPreviewDeskRt;
        private Image m_QmPreviewDeskBg;
        private RawImage m_QmPreviewDeskImg;
        private Text m_QmPreviewDeskLabel;
        private Text m_QmPreviewDeskSubLabel;

        private GameObject m_QmPreviewWatchGo;
        private RectTransform m_QmPreviewWatchRt;
        private Image m_QmPreviewWatchBg;
        private RawImage m_QmPreviewWatchImg;
        private Text m_QmPreviewWatchLabel;
        private Text m_QmPreviewWatchSubLabel;

        private static bool QuickMenuRandomPreviewEnabled()
        {
            try
            {
                var c = VPBConfig.Instance;
                return c != null && c.QuickMenuRandomHoverPreview;
            }
            catch { return false; }
        }

        internal void QuickMenuOnRandomPreviewEnabledChanged()
        {
            if (!QuickMenuRandomPreviewEnabled())
                QuickMenuEndRandomPreview();
        }

        /// <summary>Random actions and the gallery category their pool comes from (null = current view).</summary>
        private static bool QuickMenuTryGetRandomPreviewCategory(QuickMenuAssignableAction a, out string category)
        {
            category = null;
            switch (a)
            {
                case QuickMenuAssignableAction.Random: return true;
                case QuickMenuAssignableAction.RandomScenes: category = "Scenes"; return true;
                case QuickMenuAssignableAction.RandomSubScenes: category = "SubScenes"; return true;
                case QuickMenuAssignableAction.RandomClothing: category = "Clothing"; return true;
                case QuickMenuAssignableAction.RandomHair: category = "Hair"; return true;
                case QuickMenuAssignableAction.RandomPose: category = "Pose"; return true;
                case QuickMenuAssignableAction.RandomAppearance: category = "Appearance"; return true;
                case QuickMenuAssignableAction.RandomSkin: category = "Skin"; return true;
                case QuickMenuAssignableAction.RandomSceneImport: category = "Scenes"; return true;
                default: return false;
            }
        }

        private static string QuickMenuPreviewReelKey(string category)
        {
            return string.IsNullOrEmpty(category) ? QmPreviewCurrentViewKey : category;
        }

        // ---------------------------------------------------------------- hover

        // The action enum is private to this class, so the watch's namespace-level pointer handlers get
        // slot-index entry points instead (an internal member may not expose a private type).
        internal void QuickMenuBeginWatchHudRandomPreview(int hudIdx)
        {
            QuickMenuBeginRandomPreview(QuickMenuWatchHudSlotAction(hudIdx), true);
        }

        internal void QuickMenuBeginWatchAssignRandomPreview(int slotIdx)
        {
            QuickMenuBeginRandomPreview(QuickMenuWatchAssignSlotAction(slotIdx), true);
        }

        private void QuickMenuBeginRandomPreview(QuickMenuAssignableAction act, bool onWatch)
        {
            if (!QuickMenuRandomPreviewEnabled()) return;
            if (m_QuickMenuEditMode) return;

            string category;
            if (!QuickMenuTryGetRandomPreviewCategory(act, out category))
            {
                QuickMenuEndRandomPreview();
                return;
            }

            // Re-entering the same button while it is still shown must still re-roll (that is the feature),
            // so no early-out on same action here.
            unchecked { m_QmPreviewSerial++; }
            m_QmPreviewAction = act;
            m_QmPreviewCategory = category;
            m_QmPreviewOnWatch = onWatch;
            m_QmPreviewPick = null;
            m_QmPreviewWaiting = false;
            m_QmPreviewColdAt = -1f;

            var panel = QuickMenuGetTargetPanel();
            if (panel == null)
            {
                QuickMenuApplyRandomPreviewVisual();
                return;
            }

            if (string.IsNullOrEmpty(category) || panel.QuickMenu_IsShowingRandomCategory(category))
            {
                m_QmPreviewPick = QuickMenuSampleFromCurrentView(panel);
                QuickMenuApplyRandomPreviewVisual();
                return;
            }

            QmPreviewReel reel = QuickMenuGetPreviewReel(category, false);
            if (reel != null)
            {
                m_QmPreviewPick = QuickMenuTakeFromReel(reel);
                QuickMenuApplyRandomPreviewVisual();
                return;
            }

            // Cold category: defer the pool trip until the pointer actually settles on the button.
            m_QmPreviewWaiting = true;
            m_QmPreviewColdAt = Time.unscaledTime + QmPreviewColdDwellSec;
            QuickMenuApplyRandomPreviewVisual();
        }

        internal void QuickMenuEndRandomPreview()
        {
            if (m_QmPreviewAction == QuickMenuAssignableAction.None
                && !m_QmPreviewWaiting && m_QmPreviewPick == null) return;

            unchecked { m_QmPreviewSerial++; }
            m_QmPreviewAction = QuickMenuAssignableAction.None;
            m_QmPreviewCategory = null;
            m_QmPreviewPick = null;
            m_QmPreviewWaiting = false;
            m_QmPreviewColdAt = -1f;
            QuickMenuApplyRandomPreviewVisual();
        }

        /// <summary>Take the pinned pick for <paramref name="act"/> (click). Null = launch path picks its own.</summary>
        private FileEntry QuickMenuConsumeRandomPreviewPick(QuickMenuAssignableAction act)
        {
            if (m_QmPreviewAction != act) return null;
            FileEntry pick = m_QmPreviewPick;
            m_QmPreviewPick = null;
            unchecked { m_QmPreviewSerial++; }

            // The pointer usually stays on the button after a click; queue the next candidate so a
            // second press is not a blind draw. Delayed so it never samples during the launch itself.
            m_QmPreviewWaiting = true;
            m_QmPreviewColdAt = Time.unscaledTime + QmPreviewRearmSec;

            QuickMenuApplyRandomPreviewVisual();
            return pick;
        }

        /// <summary>Per-frame tick. Returns immediately unless a deferred preview draw is owed.</summary>
        private void QuickMenuAdvanceRandomPreview()
        {
            if (!QuickMenuRandomPreviewEnabled())
            {
                if (m_QmPreviewAction != QuickMenuAssignableAction.None
                    || m_QmPreviewWaiting || m_QmPreviewPick != null)
                    QuickMenuEndRandomPreview();
                return;
            }
            if (m_QmPreviewBuilding)
            {
                // A panel torn down mid-build would never call back and would wedge every later hover.
                if (Time.unscaledTime - m_QmPreviewBuildStartRt < QmPreviewBuildGiveUpSec) return;
                m_QmPreviewBuilding = false;
            }
            if (m_QmPreviewColdAt < 0f) return;
            if (Time.unscaledTime < m_QmPreviewColdAt) return;

            m_QmPreviewColdAt = -1f;
            if (m_QmPreviewAction == QuickMenuAssignableAction.None)
            {
                m_QmPreviewWaiting = false;
                return;
            }

            var panel = QuickMenuGetTargetPanel();
            if (panel == null)
            {
                m_QmPreviewWaiting = false;
                QuickMenuApplyRandomPreviewVisual();
                return;
            }

            string category = m_QmPreviewCategory;
            if (string.IsNullOrEmpty(category) || panel.QuickMenu_IsShowingRandomCategory(category))
            {
                m_QmPreviewWaiting = false;
                m_QmPreviewPick = QuickMenuSampleFromCurrentView(panel);
                QuickMenuApplyRandomPreviewVisual();
                return;
            }

            QmPreviewReel warm = QuickMenuGetPreviewReel(category, false);
            if (warm != null)
            {
                m_QmPreviewWaiting = false;
                m_QmPreviewPick = QuickMenuTakeFromReel(warm);
                QuickMenuApplyRandomPreviewVisual();
                return;
            }

            QmPreviewReel reel = QuickMenuGetPreviewReel(category, true);
            int serial = m_QmPreviewSerial;
            m_QmPreviewBuilding = true;
            m_QmPreviewBuildStartRt = Time.unscaledTime;
            panel.QuickMenu_PrepareRandomSampleForCategory(category, reel.Items, QmPreviewReelMax, count =>
            {
                m_QmPreviewBuilding = false;
                reel.Next = 0;
                reel.Epoch = GalleryFileListSnapshotCache.Epoch;
                reel.ExpiresRt = Time.unscaledTime + QmPreviewReelTtlSec;
                if (count <= 0)
                {
                    QuickMenuDropPreviewReel(category);
                    if (serial != m_QmPreviewSerial) return;
                    m_QmPreviewWaiting = false;
                    QuickMenuApplyRandomPreviewVisual();
                    return;
                }

                if (serial != m_QmPreviewSerial) return; // pointer moved on — keep the reel, drop the show
                m_QmPreviewWaiting = false;
                m_QmPreviewPick = QuickMenuTakeFromReel(reel);
                QuickMenuApplyRandomPreviewVisual();
            });
        }

        // ---------------------------------------------------------------- pools

        private FileEntry QuickMenuSampleFromCurrentView(GalleryPanel panel)
        {
            if (panel == null) return null;
            if (m_QmPreviewScratch == null) m_QmPreviewScratch = new List<FileEntry>(4);

            int n = panel.QuickMenu_FillRandomSampleFromCurrentView(m_QmPreviewScratch, 3);
            if (n <= 0) return null;

            // Prefer something other than the item shown last time so re-hovering visibly re-rolls.
            for (int i = 0; i < m_QmPreviewScratch.Count; i++)
            {
                FileEntry cand = m_QmPreviewScratch[i];
                if (cand == null) continue;
                if (!QuickMenuPreviewSameEntry(cand, m_QmPreviewLastPick))
                {
                    m_QmPreviewLastPick = cand;
                    return cand;
                }
            }
            m_QmPreviewLastPick = m_QmPreviewScratch[0];
            return m_QmPreviewScratch[0];
        }

        private static bool QuickMenuPreviewSameEntry(FileEntry a, FileEntry b)
        {
            if (a == null || b == null) return false;
            if (ReferenceEquals(a, b)) return true;
            string pa = a.Path ?? a.Uid;
            string pb = b.Path ?? b.Uid;
            return !string.IsNullOrEmpty(pa)
                && string.Equals(pa, pb, System.StringComparison.OrdinalIgnoreCase);
        }

        private QmPreviewReel QuickMenuGetPreviewReel(string category, bool createIfMissing)
        {
            if (m_QmPreviewReels == null)
            {
                if (!createIfMissing) return null;
                m_QmPreviewReels = new Dictionary<string, QmPreviewReel>(8, System.StringComparer.OrdinalIgnoreCase);
            }

            string key = QuickMenuPreviewReelKey(category);
            QmPreviewReel reel;
            if (m_QmPreviewReels.TryGetValue(key, out reel) && reel != null)
            {
                bool stale = reel.Items.Count == 0
                    || Time.unscaledTime >= reel.ExpiresRt
                    || reel.Epoch != GalleryFileListSnapshotCache.Epoch;
                if (!stale) return reel;
                if (!createIfMissing)
                {
                    m_QmPreviewReels.Remove(key);
                    return null;
                }
                reel.Items.Clear();
                reel.Next = 0;
                return reel;
            }

            if (!createIfMissing) return null;
            reel = new QmPreviewReel();
            m_QmPreviewReels[key] = reel;
            return reel;
        }

        private void QuickMenuDropPreviewReel(string category)
        {
            if (m_QmPreviewReels == null) return;
            m_QmPreviewReels.Remove(QuickMenuPreviewReelKey(category));
        }

        private FileEntry QuickMenuTakeFromReel(QmPreviewReel reel)
        {
            if (reel == null || reel.Items.Count == 0) return null;
            if (reel.Next >= reel.Items.Count) reel.Next = 0;
            FileEntry pick = reel.Items[reel.Next++];
            if (pick == null && reel.Items.Count > 1)
            {
                if (reel.Next >= reel.Items.Count) reel.Next = 0;
                pick = reel.Items[reel.Next++];
            }
            m_QmPreviewLastPick = pick;
            return pick;
        }

        // ---------------------------------------------------------------- widgets

        private void QuickMenuBuildRandomPreviewWidget(Transform parent, string name, float thumb, int font,
            out GameObject go, out RectTransform rt, out Image bg, out RawImage img, out Text label, out Text subLabel)
        {
            float captionH = QmPreviewLabelH + QmPreviewSubLabelH;
            float w = thumb + QmPreviewPad * 2f;
            float h = thumb + QmPreviewPad * 2f + captionH;

            go = new GameObject(name);
            go.transform.SetParent(parent, false);
            rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(w, h);
            bg = AddQuickMenuRoundedBg(go, QmPreviewBackdrop, false);
            QuickMenuApplyPreviewCornerRadius(bg, UI.ResolveGalleryElementCornerRadiusFraction());

            GameObject thumbGo = new GameObject("Thumb");
            thumbGo.transform.SetParent(go.transform, false);
            RectTransform thumbRt = thumbGo.AddComponent<RectTransform>();
            thumbRt.anchorMin = new Vector2(0.5f, 0.5f);
            thumbRt.anchorMax = new Vector2(0.5f, 0.5f);
            thumbRt.pivot = new Vector2(0.5f, 0.5f);
            // Square box, centred over the caption block: gallery plates are 1:1 and a stretched
            // RawImage has no way to letterbox them back.
            thumbRt.sizeDelta = new Vector2(thumb, thumb);
            thumbRt.anchoredPosition = new Vector2(0f, captionH * 0.5f);
            img = thumbGo.AddComponent<RawImage>();
            img.raycastTarget = false;
            // Same placeholder tint the gallery uses, so an unresolved thumbnail is a grey plate, not white.
            img.color = QmPreviewThumbPlaceholder;

            // Two-band caption in the grid's own shape: leaf on top, creator/package muted underneath.
            label = UI.CreateLabel(go, "", font, GalleryUiColorTokens.TextPrimary, TextAnchor.MiddleCenter,
                HorizontalWrapMode.Overflow, VerticalWrapMode.Truncate, false, false,
                AnchorPresets.bottomMiddle, new Vector2(w - QmPreviewPad * 2f, QmPreviewLabelH),
                new Vector2(0f, QmPreviewPad * 0.5f + QmPreviewSubLabelH), "PreviewLabel");
            if (label != null) label.resizeTextForBestFit = false;

            int subFont = font - 2;
            if (subFont < 9) subFont = 9;
            subLabel = UI.CreateLabel(go, "", subFont, QmPreviewLabelColor, TextAnchor.MiddleCenter,
                HorizontalWrapMode.Overflow, VerticalWrapMode.Truncate, false, false,
                AnchorPresets.bottomMiddle, new Vector2(w - QmPreviewPad * 2f, QmPreviewSubLabelH),
                new Vector2(0f, QmPreviewPad * 0.5f), "PreviewSubLabel");
            if (subLabel != null) subLabel.resizeTextForBestFit = false;

            go.SetActive(false);
        }

        private static void QuickMenuApplyPreviewCornerRadius(Image img, float frac)
        {
            RoundedRect rr = img as RoundedRect;
            if (rr == null) return;
            rr.excludeFromGlobalRadiusSync = true;
            rr.cornerRadiusFraction = 0f;
            rr.cornerRadius = frac <= 0f ? 0f : QmPreviewCornerPx;
        }

        private void QuickMenuEnsureDeskPreviewWidget()
        {
            if (m_QmPreviewDeskGo != null || m_QuickMenuCanvas == null) return;
            QuickMenuBuildRandomPreviewWidget(m_QuickMenuCanvas.transform, "VPB_QM_RandomPreview",
                QmPreviewDeskThumb, QmPreviewDeskFont,
                out m_QmPreviewDeskGo, out m_QmPreviewDeskRt, out m_QmPreviewDeskBg, out m_QmPreviewDeskImg,
                out m_QmPreviewDeskLabel, out m_QmPreviewDeskSubLabel);
            QuickMenuPositionDeskPreview();
        }

        internal void QuickMenuBuildWatchRandomPreview(GameObject root)
        {
            if (root == null) return;
            QuickMenuBuildRandomPreviewWidget(root.transform, "RandomPreview",
                QmPreviewWatchThumb, QmPreviewWatchFont,
                out m_QmPreviewWatchGo, out m_QmPreviewWatchRt, out m_QmPreviewWatchBg, out m_QmPreviewWatchImg,
                out m_QmPreviewWatchLabel, out m_QmPreviewWatchSubLabel);
            // Sits under the tip panel, which itself hangs under the bezel.
            if (m_QmPreviewWatchRt != null) m_QmPreviewWatchRt.pivot = new Vector2(0.5f, 1f);
        }

        /// <summary>
        /// Desktop quick-actions grid is bottom-left HUD chrome. Park the card above the tooltip
        /// bar (same X), growing upward — not under the buttons.
        /// </summary>
        private void QuickMenuPositionDeskPreview()
        {
            if (m_QmPreviewDeskRt == null || m_QmTooltipRT == null) return;
            m_QmPreviewDeskRt.pivot = new Vector2(0.5f, 0f);
            float tipTop = m_QmTooltipRT.anchoredPosition.y + m_QmTooltipRT.sizeDelta.y;
            m_QmPreviewDeskRt.anchoredPosition = new Vector2(m_QmTooltipRT.anchoredPosition.x, tipTop + QmPreviewDeskGap);
        }

        internal void QuickMenuClearWatchRandomPreviewRefs()
        {
            m_QmPreviewWatchGo = null;
            m_QmPreviewWatchRt = null;
            m_QmPreviewWatchBg = null;
            m_QmPreviewWatchImg = null;
            m_QmPreviewWatchLabel = null;
            m_QmPreviewWatchSubLabel = null;
            if (m_QmPreviewOnWatch) QuickMenuEndRandomPreview();
        }

        internal void QuickMenuSyncRandomPreviewCornerRadius(float frac)
        {
            QuickMenuApplyPreviewCornerRadius(m_QmPreviewDeskBg, frac);
            QuickMenuApplyPreviewCornerRadius(m_QmPreviewWatchBg, frac);
        }

        private void QuickMenuApplyRandomPreviewVisual()
        {
            // Show for any live random-button hover — empty pool must still evaluate (Norman gulf).
            bool show = m_QmPreviewAction != QuickMenuAssignableAction.None;

            bool showDesk = show && !m_QmPreviewOnWatch;
            bool showWatch = show && m_QmPreviewOnWatch;

            if (!showDesk) QuickMenuHidePreviewWidget(m_QmPreviewDeskGo, m_QmPreviewDeskImg);
            if (!showWatch) QuickMenuHidePreviewWidget(m_QmPreviewWatchGo, m_QmPreviewWatchImg);
            if (!show) return;

            if (showDesk) QuickMenuEnsureDeskPreviewWidget();

            GameObject go = showWatch ? m_QmPreviewWatchGo : m_QmPreviewDeskGo;
            RawImage img = showWatch ? m_QmPreviewWatchImg : m_QmPreviewDeskImg;
            Text label = showWatch ? m_QmPreviewWatchLabel : m_QmPreviewDeskLabel;
            Text subLabel = showWatch ? m_QmPreviewWatchSubLabel : m_QmPreviewDeskSubLabel;
            if (go == null) return;

            if (showDesk) QuickMenuPositionDeskPreview();
            if (!go.activeSelf)
            {
                go.SetActive(true);
                if (showDesk) go.transform.SetAsLastSibling();
            }

            var panel = QuickMenuGetTargetPanel();
            if (m_QmPreviewPick == null)
            {
                if (panel != null && img != null) panel.QuickMenu_ClearPreviewThumbnail(img);
                if (m_QmPreviewWaiting)
                {
                    QuickMenuSetPreviewLabel(label, VPBTranslation.T("hook.qmpreview.picking", "Picking…"));
                    QuickMenuSetPreviewLabel(subLabel, "");
                }
                else
                {
                    QuickMenuSetPreviewLabel(label, VPBTranslation.T("hook.qmpreview.empty", "No items"));
                    QuickMenuSetPreviewLabel(subLabel, VPBTranslation.T("hook.qmpreview.empty_sub", "Nothing in this pool"));
                }
                return;
            }

            if (panel != null && img != null) panel.QuickMenu_LoadPreviewThumbnail(m_QmPreviewPick, img);

            string primary;
            string secondary;
            if (panel != null) panel.QuickMenu_GetPreviewLabelLines(m_QmPreviewPick, out primary, out secondary);
            else
            {
                primary = GalleryPanel.QuickMenu_GetPreviewLabel(m_QmPreviewPick);
                secondary = "";
            }
            QuickMenuSetPreviewLabel(label, primary);
            QuickMenuSetPreviewLabel(subLabel, secondary);
        }

        private void QuickMenuHidePreviewWidget(GameObject go, RawImage img)
        {
            if (go == null || !go.activeSelf) return;
            var panel = QuickMenuGetTargetPanel();
            if (panel != null && img != null) panel.QuickMenu_ClearPreviewThumbnail(img);
            go.SetActive(false);
        }

        private static void QuickMenuSetPreviewLabel(Text label, string s)
        {
            if (label == null) return;
            string next = s ?? "";
            if (!string.Equals(label.text, next, System.StringComparison.Ordinal)) label.text = next;
        }
    }
}
