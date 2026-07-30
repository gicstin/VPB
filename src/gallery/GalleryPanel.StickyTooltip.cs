using UnityEngine;

namespace VPB
{
    public partial class GalleryPanel
    {
        /// <summary>
        /// Info-bar hover tip: instant show + tip→tip swap; hide grace only
        /// (<see cref="GalleryUiDesignTokens.TooltipHideGraceSec"/>). No fade / show dwell.
        /// </summary>
        private void SetHoverTooltip(string msg, GameObject owner)
        {
            if (string.IsNullOrEmpty(msg) || owner == null) return;

            if (temporaryStatusCoroutine != null)
            {
                StopCoroutine(temporaryStatusCoroutine);
                temporaryStatusCoroutine = null;
            }

            _stickyTipDesired = msg;
            _stickyTipDesiredOwner = owner;
            _stickyTipHidePending = false;
            temporaryStatusMsg = msg;
            temporaryStatusOwner = owner;
        }

        /// <summary>Pointer left tip target. Hide grace only if this owner still owns tip.</summary>
        private void ClearHoverTooltip(GameObject owner)
        {
            if (owner == null) return;

            bool ownsDesired = _stickyTipDesiredOwner == owner;
            bool ownsShown = temporaryStatusOwner == owner;
            if (!ownsDesired && !ownsShown) return;

            if (ownsDesired)
            {
                _stickyTipDesired = null;
                _stickyTipDesiredOwner = null;
            }

            // Sibling already claimed — keep shown tip.
            if (_stickyTipDesiredOwner != null) return;

            _stickyTipHidePending = true;
            _stickyTipHideAt = Time.unscaledTime + GalleryUiDesignTokens.TooltipHideGraceSec;
        }

        private void CancelStickyHoverTooltip()
        {
            _stickyTipDesired = null;
            _stickyTipDesiredOwner = null;
            _stickyTipHidePending = false;
            _stickyTipHideAt = 0f;
        }

        /// <summary>Once per Update before status resolve. No heap alloc.</summary>
        private void AdvanceStickyHoverTooltip()
        {
            if (_stickyTipDesired != null)
            {
                _stickyTipHidePending = false;
                if (temporaryStatusMsg != _stickyTipDesired
                    || temporaryStatusOwner != _stickyTipDesiredOwner)
                {
                    temporaryStatusMsg = _stickyTipDesired;
                    temporaryStatusOwner = _stickyTipDesiredOwner;
                }
                return;
            }

            if (!_stickyTipHidePending) return;
            if (Time.unscaledTime < _stickyTipHideAt) return;

            temporaryStatusMsg = null;
            temporaryStatusOwner = null;
            _stickyTipHidePending = false;
        }
    }
}
