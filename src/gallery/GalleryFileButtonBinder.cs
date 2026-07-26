using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace VPB
{
    /// <summary>
    /// Cached comps/transforms for pooled gallery file rows.
    /// Resolve once (create or first bind); BindFileButton / visuals reuse fields — no Find/GetComponent storm on scroll.
    /// Path class: warm → hot on recycle. Unity 2018: cache GetComponent (Unity Game Optimization → Caching component references).
    /// </summary>
    internal sealed class FileButtonBinder : MonoBehaviour
    {
        /// <summary>
        /// Badges hidden in grid bind / hover-exit. Scan whitelist "W" stays ambient in grid (status cue).
        /// </summary>
        public static readonly string[] GridBadgeHideNames =
        {
            "AutoInstallBadge", "HidePackageBadge", "UserTagsBadge", "DepsBadge", "DepsDownloadBtn"
        };

        public static readonly string[] TopLeftBadgeNames =
        {
            "AutoInstallBadge", "HidePackageBadge", "ScanExcludedBadge", "UserTagsBadge"
        };

        public RectTransform rectTransform;
        public Button button;
        public Image image;
        public UIHoverBorder hoverBorder;
        public UIHoverDelegate hoverDelegate;
        public UIFileEntryLeftReleaseSelect leftRelease;
        public UIHoverReveal hoverReveal;
        public HoldToApplyOnHover holdToApply;
        public RatingHandler ratingHandler;
        public UIDraggableItem draggable;
        public RecyclingGridItem gridItem;

        public Transform thumbTr;
        public RectTransform thumbRT;
        public RawImage thumbRaw;
        public UIHoverPreviewTrigger hoverPreview;
        internal UIFileEntryPointerForwarder thumbPointerFwd;

        public Transform listRowTr;
        public RectTransform listRowRT;
        public Transform listDetailsTr;
        public Transform listBadgesTr;
        public Transform listNameTr;
        public Text listNameText;

        public Transform depsTr;
        public Text depsText;
        public UIRichValueHover depsHover;
        public UIScrollPassthrough depsScrollPassthrough;
        public EventTrigger depsEventTrigger;

        public Transform missingTr;
        public Text missingText;
        public UIRichValueHover missingHover;
        public UIScrollPassthrough missingScrollPassthrough;
        public EventTrigger missingEventTrigger;

        public Transform categoryTr;
        public Text categoryText;

        public Transform dependentsTr;
        public Text dependentsText;
        public UIRichValueHover dependentsHover;
        public UIScrollPassthrough dependentsScrollPassthrough;
        public EventTrigger dependentsEventTrigger;

        public Transform sizeTr;
        public Text sizeText;
        public Transform dateTr;
        public Text dateText;

        public Transform gridLabelTr;
        public Transform cardTr;
        public Transform cardLabelTr;
        public Text cardLabelText;

        public Transform ratingTr;
        public Transform ratingSelectorTr;
        public Transform ratingStarTr;
        public Text ratingStarText;

        public Transform navTextTr;
        public Transform gridInnerBorderTr;
        public Transform listHoverBarTr;
        public Transform listSelectionBarTr;

        public Transform autoInstallBadgeTr;
        public Transform hidePackageBadgeTr;
        public Transform scanExcludedBadgeTr;
        public Transform userTagsBadgeTr;
        public Transform depsBadgeTr;
        public Transform depsDownloadBtnTr;

        private bool _resolved;

        public static FileButtonBinder GetOrAdd(GameObject go)
        {
            if (go == null) return null;
            FileButtonBinder b = go.GetComponent<FileButtonBinder>();
            if (b == null) b = go.AddComponent<FileButtonBinder>();
            b.Ensure();
            return b;
        }

        /// <summary>Cold path: call once after CreateNewFileButtonGO builds hierarchy.</summary>
        public static FileButtonBinder Attach(GameObject go)
        {
            return GetOrAdd(go);
        }

        public void Ensure()
        {
            if (_resolved) return;
            Resolve();
            _resolved = true;
        }

        /// <summary>Force re-resolve if hierarchy rebuilt (rare). Normal recycle keeps transforms valid.</summary>
        public void Invalidate()
        {
            _resolved = false;
        }

        private void Resolve()
        {
            Transform root = transform;
            rectTransform = transform as RectTransform;
            button = GetComponent<Button>();
            image = GetComponent<Image>();
            hoverBorder = GetComponent<UIHoverBorder>();
            hoverDelegate = GetComponent<UIHoverDelegate>();
            leftRelease = GetComponent<UIFileEntryLeftReleaseSelect>();
            if (leftRelease == null) leftRelease = gameObject.AddComponent<UIFileEntryLeftReleaseSelect>();
            hoverReveal = GetComponent<UIHoverReveal>();
            holdToApply = GetComponent<HoldToApplyOnHover>();
            if (holdToApply == null) holdToApply = gameObject.AddComponent<HoldToApplyOnHover>();
            ratingHandler = GetComponent<RatingHandler>();
            draggable = GetComponent<UIDraggableItem>();
            gridItem = GetComponent<RecyclingGridItem>();

            thumbTr = root.Find("Thumbnail");
            if (thumbTr == null) thumbTr = root.Find("ThumbContainer/Thumbnail");
            if (thumbTr != null)
            {
                thumbRT = thumbTr as RectTransform;
                thumbRaw = thumbTr.GetComponent<RawImage>();
                hoverPreview = thumbTr.GetComponent<UIHoverPreviewTrigger>();
                if (hoverPreview == null) hoverPreview = thumbTr.gameObject.AddComponent<UIHoverPreviewTrigger>();
                thumbPointerFwd = thumbTr.GetComponent<UIFileEntryPointerForwarder>();
            }

            listRowTr = root.Find("ListRow");
            if (listRowTr != null)
            {
                listRowRT = listRowTr as RectTransform;
                listDetailsTr = listRowTr.Find("Details");
                listBadgesTr = listRowTr.Find("ListBadges");
                listNameTr = listRowTr.Find("Name");
                if (listNameTr != null) listNameText = listNameTr.GetComponent<Text>();

                if (listDetailsTr != null)
                {
                    depsTr = listDetailsTr.Find("Deps");
                    missingTr = listDetailsTr.Find("Missing");
                    categoryTr = listDetailsTr.Find("Category");
                    dependentsTr = listDetailsTr.Find("Dependents");
                    sizeTr = listDetailsTr.Find("Size");
                    dateTr = listDetailsTr.Find("Date");
                }

                CacheDetail(depsTr, out depsText, out depsHover, out depsScrollPassthrough, out depsEventTrigger);
                CacheDetail(missingTr, out missingText, out missingHover, out missingScrollPassthrough, out missingEventTrigger);
                CacheDetail(dependentsTr, out dependentsText, out dependentsHover, out dependentsScrollPassthrough, out dependentsEventTrigger);
                if (categoryTr != null) categoryText = categoryTr.GetComponent<Text>();
                if (sizeTr != null) sizeText = sizeTr.GetComponent<Text>();
                if (dateTr != null) dateText = dateTr.GetComponent<Text>();
            }

            gridLabelTr = root.Find("GridLabel");
            cardTr = root.Find("Card");
            if (cardTr != null)
            {
                cardLabelTr = cardTr.Find("Label");
                if (cardLabelTr != null) cardLabelText = cardLabelTr.GetComponent<Text>();
            }

            ratingTr = root.Find("Rating");
            ratingSelectorTr = root.Find("RatingSelector");
            ratingStarTr = root.Find("Rating/Star");
            if (ratingStarTr == null) ratingStarTr = root.Find("ListRow/Details/Rating/Star");
            if (ratingStarTr != null) ratingStarText = ratingStarTr.GetComponentInChildren<Text>();

            navTextTr = root.Find("NavText");
            gridInnerBorderTr = root.Find("GridInnerBorder");
            listHoverBarTr = root.Find("ListHoverBar");
            listSelectionBarTr = root.Find("ListSelectionBar");

            autoInstallBadgeTr = FindBadge(root, "AutoInstallBadge");
            hidePackageBadgeTr = FindBadge(root, "HidePackageBadge");
            scanExcludedBadgeTr = FindBadge(root, "ScanExcludedBadge");
            userTagsBadgeTr = FindBadge(root, "UserTagsBadge");
            depsBadgeTr = FindBadge(root, "DepsBadge");
            depsDownloadBtnTr = FindBadge(root, "DepsDownloadBtn");
        }

        private static void CacheDetail(
            Transform tr,
            out Text text,
            out UIRichValueHover hover,
            out UIScrollPassthrough scrollPassthrough,
            out EventTrigger eventTrigger)
        {
            text = null;
            hover = null;
            scrollPassthrough = null;
            eventTrigger = null;
            if (tr == null) return;
            text = tr.GetComponent<Text>();
            hover = tr.GetComponent<UIRichValueHover>();
            scrollPassthrough = tr.GetComponent<UIScrollPassthrough>();
            eventTrigger = tr.GetComponent<EventTrigger>();
        }

        private static Transform FindBadge(Transform btnRoot, string badgeName)
        {
            if (btnRoot == null || string.IsNullOrEmpty(badgeName)) return null;
            Transform t = btnRoot.Find(badgeName);
            if (t != null) return t;
            return btnRoot.Find("ListRow/ListBadges/" + badgeName);
        }

        public Transform GetBadge(string badgeName)
        {
            if (string.IsNullOrEmpty(badgeName)) return null;
            if (badgeName == "AutoInstallBadge") return autoInstallBadgeTr;
            if (badgeName == "HidePackageBadge") return hidePackageBadgeTr;
            if (badgeName == "ScanExcludedBadge") return scanExcludedBadgeTr;
            if (badgeName == "UserTagsBadge") return userTagsBadgeTr;
            if (badgeName == "DepsBadge") return depsBadgeTr;
            if (badgeName == "DepsDownloadBtn") return depsDownloadBtnTr;
            return FindBadge(transform, badgeName);
        }

        public static void SetActive(Transform t, bool active)
        {
            if (t == null) return;
            if (t.gameObject.activeSelf != active)
                t.gameObject.SetActive(active);
        }

        public UIRichValueHover EnsureDepsHover()
        {
            if (depsTr == null) return null;
            if (depsHover == null) depsHover = depsTr.gameObject.AddComponent<UIRichValueHover>();
            return depsHover;
        }

        public UIRichValueHover EnsureMissingHover()
        {
            if (missingTr == null) return null;
            if (missingHover == null) missingHover = missingTr.gameObject.AddComponent<UIRichValueHover>();
            return missingHover;
        }

        public UIRichValueHover EnsureDependentsHover()
        {
            if (dependentsTr == null) return null;
            if (dependentsHover == null) dependentsHover = dependentsTr.gameObject.AddComponent<UIRichValueHover>();
            return dependentsHover;
        }

        public UIScrollPassthrough EnsureDepsScrollPassthrough()
        {
            if (depsTr == null) return null;
            if (depsScrollPassthrough == null) depsScrollPassthrough = depsTr.gameObject.AddComponent<UIScrollPassthrough>();
            return depsScrollPassthrough;
        }

        public UIScrollPassthrough EnsureMissingScrollPassthrough()
        {
            if (missingTr == null) return null;
            if (missingScrollPassthrough == null) missingScrollPassthrough = missingTr.gameObject.AddComponent<UIScrollPassthrough>();
            return missingScrollPassthrough;
        }

        public UIScrollPassthrough EnsureDependentsScrollPassthrough()
        {
            if (dependentsTr == null) return null;
            if (dependentsScrollPassthrough == null) dependentsScrollPassthrough = dependentsTr.gameObject.AddComponent<UIScrollPassthrough>();
            return dependentsScrollPassthrough;
        }

        public EventTrigger EnsureDepsEventTrigger()
        {
            if (depsTr == null) return null;
            if (depsEventTrigger == null) depsEventTrigger = depsTr.gameObject.AddComponent<EventTrigger>();
            return depsEventTrigger;
        }

        public EventTrigger EnsureMissingEventTrigger()
        {
            if (missingTr == null) return null;
            if (missingEventTrigger == null) missingEventTrigger = missingTr.gameObject.AddComponent<EventTrigger>();
            return missingEventTrigger;
        }

        public EventTrigger EnsureDependentsEventTrigger()
        {
            if (dependentsTr == null) return null;
            if (dependentsEventTrigger == null) dependentsEventTrigger = dependentsTr.gameObject.AddComponent<EventTrigger>();
            return dependentsEventTrigger;
        }
    }
}
