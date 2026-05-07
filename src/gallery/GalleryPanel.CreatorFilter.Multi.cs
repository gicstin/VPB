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

        private bool CreatorFilterContains(string creator)
        {
            EnsureCurrentCreatorSet();
            if (_currentCreatorSet.Count == 0) return false;
            if (string.IsNullOrEmpty(creator)) return false;
            return _currentCreatorSet.Contains(creator);
        }

        private string CanonicalizeCreatorFilterFromSet()
        {
            EnsureCurrentCreatorSet();
            if (_currentCreatorSet.Count == 0) return "";
            var list = _currentCreatorSet.ToList();
            list.Sort(StringComparer.OrdinalIgnoreCase);
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
            if (_currentCreatorSet.Contains(creator)) _currentCreatorSet.Remove(creator);
            else _currentCreatorSet.Add(creator);
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
            EnsureCurrentCreatorSet();
            if (_currentCreatorSet.Count == 0) return true;
            if (string.IsNullOrEmpty(packageCreator)) return false;
            return _currentCreatorSet.Contains(packageCreator);
        }

        private void OnCreatorFilterChanged(bool refreshFilesAndTabs)
        {
            categoriesCached = false;
            pathsCached = false;
            tagsCached = false;
            try { UpdateTitleCreatorButtonVisual(); } catch { }

            if (refreshFilesAndTabs) RefreshFilesAndTabs();
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

