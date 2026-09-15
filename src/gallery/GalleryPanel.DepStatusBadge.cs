using System;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.UI;
using VPB.src.util;

namespace VPB
{
    public partial class GalleryPanel
    {
        internal const string DepStatusBadgeName = "DepStatusBadge";

        private static readonly Color DepStatusLetterMissing = new Color(0.95f, 0.72f, 0.28f, 1f);
        private static readonly Color DepStatusLetterBroken = new Color(0.95f, 0.42f, 0.38f, 1f);

        private const float DepStatusResolveBudgetMs = 1.2f;
        private const int DepStatusResolveMaxPerTick = 24;

        private static readonly Stopwatch _depStatusWatch = new Stopwatch();
        private bool _depStatusResolvedThisTick;

        private static GameObject CreateDepStatusBadge(GameObject btnGO)
        {
            GameObject go = new GameObject(DepStatusBadgeName);
            go.transform.SetParent(btnGO.transform, false);
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(1, 0);
            rt.anchorMax = new Vector2(1, 0);
            rt.pivot = new Vector2(1, 0);
            rt.sizeDelta = new Vector2(36, 32);
            rt.anchoredPosition = new Vector2(-6, 6);

            RoundedRect rr = go.AddComponent<RoundedRect>();
            rr.color = GalleryItemLabelBarBackdrop;
            rr.raycastTarget = false;
            rr.cornerRadiusFraction = UI.ResolveGalleryElementCornerRadiusFraction();

            UI.CreateLabel(
                go, "", GalleryUiDesignTokens.FontBodyRef, DepStatusLetterMissing, TextAnchor.MiddleCenter,
                HorizontalWrapMode.Overflow, VerticalWrapMode.Overflow, raycastTarget: false, name: "Text");
            UI.AddLE(go, minWidth: 36f, minHeight: 32f, preferredWidth: 36f, preferredHeight: 32f);
            go.SetActive(false);
            return go;
        }

        private bool DepStatusBadgesEnabled
        {
            get { return VPBConfig.Instance == null || VPBConfig.Instance.GalleryDepStatusBadgeEnabled; }
        }

        private static bool IsDepStatusEligibleRow(FileEntry file)
        {
            return file != null && !(file is InternalSettingRowEntry);
        }

        private void ApplyDepStatusBadgeVisual(GameObject btnGO, FileEntry file, FileButtonBinder b, string rowKey)
        {
            Transform tr = b != null ? b.depStatusBadgeTr : null;
            if (tr == null)
            {
                tr = FindGalleryBadgeTransform(btnGO.transform, DepStatusBadgeName);
                if (b != null) b.depStatusBadgeTr = tr;
            }
            if (tr == null) return;

            if (!DepStatusBadgesEnabled || !IsDepStatusEligibleRow(file))
            {
                if (tr.gameObject.activeSelf) tr.gameObject.SetActive(false);
                return;
            }

            string key = string.IsNullOrEmpty(rowKey) ? GetSelectionIdentityKey(file, false) : rowKey;
            byte status;
            int missing;
            if (!GalleryDepStatus.TryGet(key, out status, out missing))
            {
                GalleryDepStatus.Enqueue(key, file);
                if (tr.gameObject.activeSelf) tr.gameObject.SetActive(false);
                return;
            }

            if (status != GalleryDepStatus.Missing && status != GalleryDepStatus.Broken)
            {
                if (tr.gameObject.activeSelf) tr.gameObject.SetActive(false);
                return;
            }

            Text label = tr.GetComponentInChildren<Text>(true);
            if (label != null)
            {
                if (status == GalleryDepStatus.Broken)
                {
                    label.color = DepStatusLetterBroken;
                    label.text = "!";
                }
                else
                {
                    label.color = DepStatusLetterMissing;
                    label.text = missing > 99 ? "99+" : DepStatusCountText(missing);
                }
            }
            if (!tr.gameObject.activeSelf) tr.gameObject.SetActive(true);
        }

        private static readonly string[] DepStatusSmallCounts =
        {
            "0", "1", "2", "3", "4", "5", "6", "7", "8", "9",
            "10", "11", "12", "13", "14", "15", "16", "17", "18", "19", "20"
        };

        private static string DepStatusCountText(int n)
        {
            if (n >= 0 && n < DepStatusSmallCounts.Length) return DepStatusSmallCounts[n];
            return n.ToString();
        }

        private void DepStatusResolverTick()
        {
            if (!DepStatusBadgesEnabled) return;
            if (GalleryDepStatus.PendingCount == 0) return;

            _depStatusResolvedThisTick = false;
            _depStatusWatch.Reset();
            _depStatusWatch.Start();

            int done = 0;
            while (done < DepStatusResolveMaxPerTick)
            {
                string key;
                FileEntry file;
                if (!GalleryDepStatus.TryDequeue(out key, out file)) break;

                int missing;
                byte status = GalleryDepStatus.Compute(file, out missing);
                GalleryDepStatus.Store(key, status, missing);
                _depStatusResolvedThisTick = true;
                done++;

                if (_depStatusWatch.Elapsed.TotalMilliseconds >= DepStatusResolveBudgetMs) break;
            }

            _depStatusWatch.Stop();
            if (_depStatusResolvedThisTick) RefreshVisibleDepStatusBadges();
        }

        private void RefreshVisibleDepStatusBadges()
        {
            if (recyclingGrid == null) return;
            int n = recyclingGrid.ActiveItemCount;
            for (int i = 0; i < n; i++)
            {
                RecyclingGridItem item = recyclingGrid.GetActiveItemAt(i);
                if (item == null || item.gameObject == null || !item.gameObject.activeSelf) continue;
                FileButtonBinder b = item.binder;
                FileEntry fe = ResolveVisibleFileEntryFromItem(item, b);
                if (fe == null) continue;
                if (b == null) b = FileButtonBinder.GetOrAdd(item.gameObject);
                ApplyDepStatusBadgeVisual(item.gameObject, fe, b, null);
            }
        }

        internal void RefreshAllDepStatusBadges()
        {
            try { RefreshVisibleDepStatusBadges(); } catch { }
        }

        private void ToggleDepStatusSearchChip(string token)
        {
            if (_titleSearchChips == null || string.IsNullOrEmpty(token)) return;

            for (int i = 0; i < _titleSearchChips.Count; i++)
            {
                TitleSearchChip chip = _titleSearchChips[i];
                if (chip.Kind != TitleSearchChipKind.Status) continue;
                if (!string.Equals(chip.Value, token, StringComparison.OrdinalIgnoreCase)) continue;
                RemoveTitleSearchChipAt(i);
                return;
            }

            string opposite = string.Equals(token, "missing", StringComparison.OrdinalIgnoreCase) ? "complete" : "missing";
            for (int i = _titleSearchChips.Count - 1; i >= 0; i--)
            {
                TitleSearchChip chip = _titleSearchChips[i];
                if (chip.Kind == TitleSearchChipKind.Status
                    && string.Equals(chip.Value, opposite, StringComparison.OrdinalIgnoreCase))
                    _titleSearchChips.RemoveAt(i);
            }

            if (GalleryTitleSearchChipUtil.TryAdd(
                    _titleSearchChips, TitleSearchChipKind.Status, TitleSearchChipPolarity.Include, token, 0))
                ApplySerializedTitleSearchChips();
        }
    }
}
