using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VPB
{
    public partial class GalleryPanel
    {
        private void EnsureCurrentCreatorSet()
        {
            string src = currentCreator ?? "";
            if (string.Equals(_currentCreatorSetSrc, src, StringComparison.Ordinal)) return;
            _currentCreatorSetSrc = src;
            _currentCreatorSet.Clear();
            if (string.IsNullOrEmpty(src)) return;
            string[] parts = src.Split('|');
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i] != null ? parts[i].Trim() : "";
                if (p.Length == 0) continue;
                _currentCreatorSet.Add(p);
            }
        }

        private bool HasCreatorFilter()
        {
            EnsureCurrentCreatorSet();
            return _currentCreatorSet.Count > 0;
        }

        private bool CreatorNameMatchesActiveFilter(string creator)
        {
            EnsureCurrentCreatorSet();
            if (_currentCreatorSet.Count == 0) return true;
            if (string.IsNullOrEmpty(creator)) return false;
            if (GalleryConsolidateCreatorNamesEnabled)
            {
                foreach (string sel in _currentCreatorSet)
                {
                    if (string.Equals(sel, creator, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                return false;
            }
            return _currentCreatorSet.Contains(creator);
        }

        private bool CreatorFilterContains(string creator)
        {
            return CreatorNameMatchesActiveFilter(creator);
        }

        private bool ActiveFilterContainsCreatorSelection(string selectedCreator)
        {
            EnsureCurrentCreatorSet();
            if (_currentCreatorSet.Count == 0) return false;
            if (string.IsNullOrEmpty(selectedCreator)) return false;
            if (GalleryConsolidateCreatorNamesEnabled)
            {
                foreach (string sel in _currentCreatorSet)
                {
                    if (string.Equals(sel, selectedCreator, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                return false;
            }
            return _currentCreatorSet.Contains(selectedCreator);
        }

        private void RemoveCreatorFromActiveFilter(string creator)
        {
            if (string.IsNullOrEmpty(creator)) return;
            if (GalleryConsolidateCreatorNamesEnabled)
            {
                var remove = new List<string>();
                foreach (string sel in _currentCreatorSet)
                {
                    if (string.Equals(sel, creator, StringComparison.OrdinalIgnoreCase))
                        remove.Add(sel);
                }
                for (int i = 0; i < remove.Count; i++)
                    _currentCreatorSet.Remove(remove[i]);
            }
            else
                _currentCreatorSet.Remove(creator);
        }

        private string CanonicalizeCreatorFilterFromSet()
        {
            EnsureCurrentCreatorSet();
            if (_currentCreatorSet.Count == 0) return "";
            var list = _currentCreatorSet.ToList();
            list.Sort(GalleryConsolidateCreatorNamesEnabled
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal);
            return string.Join("|", list.ToArray());
        }

        private void SetCreatorFilterFromSetAndSync()
        {
            string canon = CanonicalizeCreatorFilterFromSet();
            if (string.Equals(currentCreator ?? "", canon, StringComparison.Ordinal)) return;
            currentCreator = canon;
            _currentCreatorSetSrc = null;
        }

        private void ToggleCreatorFilter(string creator)
        {
            if (string.IsNullOrEmpty(creator)) return;
            EnsureCurrentCreatorSet();
            if (ActiveFilterContainsCreatorSelection(creator)) RemoveCreatorFromActiveFilter(creator);
            else
            {
                _currentCreatorSet.Add(creator);
                // A picked creator implies .var content; force global filter to All so the user does not see a contradictory "Local + Creator: X" state.
                if (currentGlobalSourceFilter != VPBConfig.GlobalSourceFilterValue.All)
                {
                    currentGlobalSourceFilter = VPBConfig.GlobalSourceFilterValue.All;
                    if (VPBConfig.Instance != null)
                    {
                        VPBConfig.Instance.GlobalSourceFilter = VPBConfig.GlobalSourceFilterValue.All;
                        try { VPBConfig.Instance.Save(); }
                        catch (System.Exception ex) { LogUtil.LogWarning("[VPB] VPBConfig.Save failed on creator/global-filter reset: " + ex.Message); }
                    }
                    try { UpdateGlobalSourceFilterButtonLabel(); }
                    catch (System.Exception ex) { LogUtil.LogWarning("[VPB] UpdateGlobalSourceFilterButtonLabel failed on creator/global-filter reset: " + ex.Message); }
                    LogUtil.Log("[VPB] Creator filter picked; global source filter reset to All.");
                }
            }
            SetCreatorFilterFromSetAndSync();
        }

        private void ClearCreatorFilters()
        {
            if (!HasCreatorFilter()) return;
            currentCreator = "";
            _currentCreatorSetSrc = null;
            _currentCreatorSet.Clear();
        }

        private bool CreatorFilterMatchesPackageCreator(string packageCreator)
        {
            return CreatorNameMatchesActiveFilter(packageCreator);
        }

        private void OnCreatorFilterChanged(bool refreshFilesAndTabs)
        {
            try { UpdateTitleCreatorButtonVisual(); } catch { }
            PushCreatorFilterSqlModeForDatabase();
            categoriesCached = false;
            pathsCached = false;
            tagsCached = false;
            creatorsCached = false;
            InvalidateDisplayCreatorsCache();
            GalleryFileListSnapshotCache.Clear();

            if (refreshFilesAndTabs) RefreshFilesAndTabs();
            SyncBrowseFilterChipChrome();
        }

        private void UpdateTitleCreatorButtonVisual()
        {
            if (titleCreatorBtnBackdrop == null) return;
            bool active = HasCreatorFilter();
            titleCreatorBtnBackdrop.color = active ? ColorCreator : new Color(0f, 0f, 0f, 0.5f);
            if (titleCreatorBtnText != null) titleCreatorBtnText.color = Color.white;
            if (titleCreatorBtnIconImage != null) titleCreatorBtnIconImage.color = UI.BarIconGlyphTint;
        }
    }
}
