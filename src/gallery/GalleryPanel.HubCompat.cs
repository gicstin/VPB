using System.Collections.Generic;
using UnityEngine;

namespace VPB
{
    // Compatibility shim: other GalleryPanel partials reference these Hub members.
    // The old in-gallery "Gallery Hub" implementation was removed; keep these as no-ops.
    public partial class GalleryPanel
    {
        // Legacy hub state fields referenced by UI/tabs code
        private string currentHubCategory = "All";
        private string currentHubCreator = "All";
        private string currentHubPayType = "All";
        private string currentHubSearch = "";
        private HashSet<string> currentHubTags = new HashSet<string>();
        private int hubTagPage = 0;
        private int hubCreatorPage = 0;

        public bool IsHubMode => false;

        public void RefreshHubItems()
        {
            // No-op: feature removed.
        }

        private void UpdateHubCategories(GameObject container, List<GameObject> trackedButtons, bool isLeft)
        {
            ShowHubRemovedPlaceholder(container, trackedButtons);
        }

        private void UpdateHubTags(GameObject container, List<GameObject> trackedButtons, bool isLeft)
        {
            ShowHubRemovedPlaceholder(container, trackedButtons);
        }

        private void UpdateHubPayTypes(GameObject container, List<GameObject> trackedButtons, bool isLeft)
        {
            ShowHubRemovedPlaceholder(container, trackedButtons);
        }

        private void UpdateHubCreators(GameObject container, List<GameObject> trackedButtons, bool isLeft)
        {
            ShowHubRemovedPlaceholder(container, trackedButtons);
        }

        private void ShowHubRemovedPlaceholder(GameObject container, List<GameObject> trackedButtons)
        {
            if (container == null) return;
            if (trackedButtons != null)
            {
                for (int i = 0; i < trackedButtons.Count; i++) ReturnTabButton(trackedButtons[i]);
                trackedButtons.Clear();
            }

            // Keep it cheap: a single row.
            CreateTabButton(container.transform, "Hub (Gallery) removed", Color.gray, false, null, trackedButtons);
        }
    }
}

