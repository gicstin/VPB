namespace VPB
{
    /// <summary>Risk friction: Low = immediate+Undo, Medium = soft-confirm, High = modal confirm; fail closed without confirm UI.</summary>
    public partial class GalleryPanel
    {
        internal enum GalleryRiskLevel
        {
            Low = 0,
            Medium = 1,
            High = 2
        }

        private static bool RiskPolicyStripNeedsSoftConfirm(int dropCount)
        {
            return dropCount > 0;
        }
    }
}
