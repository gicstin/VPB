using UnityEngine;
using UnityEngine.UI;

namespace VPB
{
    /// <summary>Per thumbnail cell: plugin placeholder overlay (text or baked bitmap label).</summary>
    internal sealed class PluginThumbPlaceholderRefs : MonoBehaviour
    {
        public GameObject Root;
        public Text Label;
        public RawImage LabelImage;
        public bool WantsLabel;
        public bool UseBitmapLabel;
        public long CachedBitmapKey;
        public string CachedText;
        public int CachedFontSize;
    }
}
