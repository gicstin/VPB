using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace VPB
{
    public partial class GalleryPanel
    {
        private struct UserTagAvailScrollSnap
        {
            public bool Valid;
            public float OffsetPx;
        }

        private UserTagAvailScrollSnap _snapUserTagAvailScrollLeft;
        private UserTagAvailScrollSnap _snapUserTagAvailScrollRight;
        private bool _leftUserTagAvailScrollTrackHooked;
        private bool _rightUserTagAvailScrollTrackHooked;
        private Coroutine _leftUserTagAvailScrollRestoreCo;
        private Coroutine _rightUserTagAvailScrollRestoreCo;

        private ScrollRect GetUserTagAvailScrollRect(bool isLeft)
        {
            GameObject scrollGo = isLeft ? leftTabScrollGO : rightTabScrollGO;
            if (scrollGo == null || !scrollGo.activeSelf) return null;
            try { return scrollGo.GetComponent<ScrollRect>(); } catch { return null; }
        }

        private bool IsUserTagAvailPaneActive(bool isLeft)
        {
            ContentType? ac = isLeft ? leftActiveContent : rightActiveContent;
            if (!ac.HasValue || ac.Value != ContentType.UserTags) return false;
            GameObject scrollGo = isLeft ? leftTabScrollGO : rightTabScrollGO;
            return scrollGo != null && scrollGo.activeSelf;
        }

        private void EnsureUserTagAvailScrollTrackingHooks()
        {
            EnsureUserTagAvailScrollTrackingHookOne(true);
            EnsureUserTagAvailScrollTrackingHookOne(false);
        }

        private void EnsureUserTagAvailScrollTrackingHookOne(bool isLeft)
        {
            if (isLeft ? _leftUserTagAvailScrollTrackHooked : _rightUserTagAvailScrollTrackHooked)
                return;
            ScrollRect sr = GetUserTagAvailScrollRect(isLeft);
            if (sr == null) return;
            if (isLeft)
            {
                _leftUserTagAvailScrollTrackHooked = true;
                sr.onValueChanged.AddListener(OnUserTagAvailScrollLeft);
            }
            else
            {
                _rightUserTagAvailScrollTrackHooked = true;
                sr.onValueChanged.AddListener(OnUserTagAvailScrollRight);
            }
        }

        private void OnUserTagAvailScrollLeft(Vector2 _)
        {
            _snapUserTagAvailScrollLeft = CaptureUserTagAvailScrollSnap(true);
        }

        private void OnUserTagAvailScrollRight(Vector2 _)
        {
            _snapUserTagAvailScrollRight = CaptureUserTagAvailScrollSnap(false);
        }

        private UserTagAvailScrollSnap CaptureUserTagAvailScrollSnap(bool isLeft)
        {
            var snap = new UserTagAvailScrollSnap();
            if (!IsUserTagAvailPaneActive(isLeft)) return snap;
            float? px = TryGetUserTagAvailScrollOffsetPx(isLeft);
            if (!px.HasValue) return snap;
            snap.Valid = true;
            snap.OffsetPx = px.Value;
            return snap;
        }

        private float? TryGetUserTagAvailScrollOffsetPx(bool isLeft)
        {
            ScrollRect sr = GetUserTagAvailScrollRect(isLeft);
            if (sr == null || sr.content == null || sr.viewport == null) return null;
            try
            {
                float contentH = sr.content.rect.height;
                float viewportH = sr.viewport.rect.height;
                float scrollRange = Mathf.Max(0f, contentH - viewportH);
                if (scrollRange <= 0.5f) return 0f;
                return (1f - Mathf.Clamp01(sr.verticalNormalizedPosition)) * scrollRange;
            }
            catch { return null; }
        }

        private void SnapshotUserTagAvailScrollForPreserve(bool isLeft)
        {
            if (!IsUserTagAvailPaneActive(isLeft)) return;
            var snap = CaptureUserTagAvailScrollSnap(isLeft);
            if (isLeft) _snapUserTagAvailScrollLeft = snap;
            else _snapUserTagAvailScrollRight = snap;
        }

        private void SnapshotUserTagAvailScrollForPreserveBoth()
        {
            SnapshotUserTagAvailScrollForPreserve(true);
            SnapshotUserTagAvailScrollForPreserve(false);
        }

        private void ApplyUserTagAvailScrollOffsetPx(bool isLeft, float offsetPx)
        {
            ScrollRect sr = GetUserTagAvailScrollRect(isLeft);
            if (sr == null || sr.content == null || sr.viewport == null) return;
            try
            {
                float contentH = sr.content.rect.height;
                float viewportH = sr.viewport.rect.height;
                float scrollRange = Mathf.Max(0f, contentH - viewportH);
                if (scrollRange <= 0.5f)
                {
                    sr.verticalNormalizedPosition = 1f;
                    return;
                }
                offsetPx = Mathf.Clamp(offsetPx, 0f, scrollRange);
                sr.verticalNormalizedPosition = 1f - (offsetPx / scrollRange);
            }
            catch { }
        }

        private void RequestRestoreUserTagAvailScrollPx(bool isLeft, float offsetPx)
        {
            if (isLeft)
            {
                StopCo(ref _leftUserTagAvailScrollRestoreCo);
                _leftUserTagAvailScrollRestoreCo = StartCoroutine(CoRestoreUserTagAvailScroll(isLeft, offsetPx));
            }
            else
            {
                StopCo(ref _rightUserTagAvailScrollRestoreCo);
                _rightUserTagAvailScrollRestoreCo = StartCoroutine(CoRestoreUserTagAvailScroll(isLeft, offsetPx));
            }
        }

        private void RestorePreservedUserTagAvailScroll()
        {
            if (_snapUserTagAvailScrollLeft.Valid)
                RequestRestoreUserTagAvailScrollPx(true, _snapUserTagAvailScrollLeft.OffsetPx);
            if (_snapUserTagAvailScrollRight.Valid)
                RequestRestoreUserTagAvailScrollPx(false, _snapUserTagAvailScrollRight.OffsetPx);
        }

        private IEnumerator CoRestoreUserTagAvailScroll(bool isLeft, float offsetPx)
        {
            for (int frame = 0; frame < 4; frame++)
            {
                yield return null;
                try
                {
                    GameObject content = isLeft ? leftTabContainerGO : rightTabContainerGO;
                    if (content != null)
                    {
                        RectTransform crt = content.GetComponent<RectTransform>();
                        if (crt != null)
                            LayoutRebuilder.ForceRebuildLayoutImmediate(crt);
                    }
                    ApplyUserTagAvailScrollOffsetPx(isLeft, offsetPx);
                    ScrollRect sr = GetUserTagAvailScrollRect(isLeft);
                    if (sr != null)
                    {
                        ScrollbarSync sync = sr.GetComponentInChildren<ScrollbarSync>(true);
                        if (sync != null) sync.UpdateScrollbarSize();
                    }
                    Transform tabContainer = content != null ? content.transform : null;
                    if (tabContainer != null)
                        UpdateUserTagVirtualVisible(isLeft, UserTagStateOnColor, tabContainer);
                }
                catch { }
            }
            if (isLeft) _leftUserTagAvailScrollRestoreCo = null;
            else _rightUserTagAvailScrollRestoreCo = null;
        }

        /// <summary>Rebind User Tags rows without full <see cref="UpdateTabs"/> (avoids scroll jump).</summary>
        private void RefreshUserTagsAvailPaneInPlace(bool isLeft)
        {
            if (!IsUserTagsSideTabOpen(isLeft)) return;
            GameObject container = isLeft ? leftTabContainerGO : rightTabContainerGO;
            if (container == null) return;
            EnsureUserTagAvailScrollTrackingHooks();
            SnapshotUserTagAvailScrollForPreserve(isLeft);
            CacheAppliedUserTagsForSelection();

            if (!userTagsCached)
            {
                try { CacheUserTagsSideTab(); } catch { }
            }

            string sigNew = ComputeUserTagVirtDataSignature();
            bool dataSigChanged = !string.Equals(_userTagVirtViewSig, sigNew, StringComparison.Ordinal);
            if (dataSigChanged)
            {
                _userTagVirtViewSig = sigNew;
                RebuildUserTagVirtViewList(isLeft, resetScrollToTop: false);
            }

            SyncUserTagAvailPinnedStickyRows(isLeft, UserTagStateOnColor, container.transform);
            UpdateUserTagVirtualVisible(isLeft, UserTagStateOnColor, container.transform);
            SyncUserTagAvailTitleCount(isLeft);
            SyncUserTagApplyBtnCount(isLeft);
            try { ApplyUserTagsStickyScrollChrome(TabScrollTopOffset()); } catch { }
            RestorePreservedUserTagAvailScroll();
        }
    }
}
