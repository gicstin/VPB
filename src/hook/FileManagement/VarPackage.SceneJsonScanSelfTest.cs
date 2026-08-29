using System.Collections.Generic;
using System.Text;

namespace VPB
{
    internal static class VarPackageSceneJsonScanSelfTest
    {
        public static bool Run(StringBuilder log)
        {
            int fail = 0;
            Check(log, ref fail, "fwd scene",
                VarPackage.IsZipGallerySceneJsonInternalPath("Saves/scene/EvilBox/Home Studio 1.3 SD.json"), true);
            Check(log, ref fail, "backslash scene",
                VarPackage.IsZipGallerySceneJsonInternalPath("Saves\\scene\\EvilBox\\Home Studio 1.3 SD.json"), true);
            Check(log, ref fail, "subscene",
                VarPackage.IsZipGallerySceneJsonInternalPath("Custom/SubScene/room.json"), true);
            Check(log, ref fail, "meta",
                VarPackage.IsZipGallerySceneJsonInternalPath("meta.json"), false);
            Check(log, ref fail, "plugin json",
                VarPackage.IsZipGallerySceneJsonInternalPath("Custom/Scripts/foo.json"), false);
            Check(log, ref fail, "slash norm",
                VarPackage.NormalizeZipInternalPath("Saves\\scene\\a.json"), "Saves/scene/a.json");

            var dropped = new List<string>
            {
                "meta.json",
                "Custom/Assets/a1_home_studio_props.assetbundle",
                "Custom/Assets/a1_home_studio_sd.assetbundle"
            };
            Check(log, ref fail, "cua dropped stamp0",
                VarPackage.CachedNamesNeedSceneJsonRescan(dropped, 0), true);
            Check(log, ref fail, "cua dropped stamp1",
                VarPackage.CachedNamesNeedSceneJsonRescan(dropped, VarPackage.CurrentScanRuleStamp), false);

            var indexed = new List<string>(dropped);
            indexed.Add("Saves/scene/EvilBox/Home Studio 1.3 SD.json");
            Check(log, ref fail, "cua+scene stamp0",
                VarPackage.CachedNamesNeedSceneJsonRescan(indexed, 0), false);

            var clothing = new List<string> { "meta.json", "Custom/Clothing/x.vam" };
            Check(log, ref fail, "clothing stamp0",
                VarPackage.CachedNamesNeedSceneJsonRescan(clothing, 0), false);

            if (log != null)
            {
                log.Append("VarPackageSceneJsonScanSelfTest fail=");
                log.Append(fail);
                log.Append('\n');
            }
            return fail == 0;
        }

        static void Check(StringBuilder log, ref int fail, string name, bool got, bool want)
        {
            if (got == want) return;
            fail++;
            if (log != null)
                log.Append("FAIL ").Append(name).Append(" got=").Append(got).Append(" want=").Append(want).Append('\n');
        }

        static void Check(StringBuilder log, ref int fail, string name, string got, string want)
        {
            if (string.Equals(got, want, System.StringComparison.Ordinal)) return;
            fail++;
            if (log != null)
                log.Append("FAIL ").Append(name).Append(" got=").Append(got ?? "null").Append(" want=").Append(want ?? "null").Append('\n');
        }
    }
}
