namespace VPB
{
    /// <summary>
    /// Product risk friction matrix (desktop HCI: harm × friction consistency).
    /// Cold/warm policy only — not per-frame.
    ///
    /// | Level  | Friction                         | Examples                                      |
    /// |--------|----------------------------------|-----------------------------------------------|
    /// | Low    | Immediate + Undo                 | Scene Eraser clothing/hair                    |
    /// | Medium | Soft-confirm (2nd Confirm/Enter) | Strip Scene (any drop &gt; 0)                  |
    /// | High   | Modal / world confirm            | Atom/Person erase, package Delete, Cleanup    |
    ///
    /// Fail closed on confirm UI failure (never execute High without UI).
    /// </summary>
    public partial class GalleryPanel
    {
        internal enum GalleryRiskLevel
        {
            Low = 0,
            Medium = 1,
            High = 2
        }

        /// <summary>Strip always medium when anything would drop — no threshold lottery.</summary>
        private static bool RiskPolicyStripNeedsSoftConfirm(int dropCount)
        {
            return dropCount > 0;
        }
    }
}
