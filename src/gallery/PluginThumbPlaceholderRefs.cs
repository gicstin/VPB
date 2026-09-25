using UnityEngine;
using UnityEngine.UI;

namespace VPB
{
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
