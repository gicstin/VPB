using System;
using System.Collections.Generic;

namespace VPB
{
   [Flags]
    internal enum PkgIssueFlags
    {
        None = 0,
        MetaMissing = 1 << 0,
        MetaUnparsable = 1 << 1,
        NameMismatch = 1 << 2,
        NameFormatBad = 1 << 3,
        ReferenceIssues = 1 << 4,
        PreloadMorphs = 1 << 5,
        LocalReferences = 1 << 6,
        UndeclaredDeps = 1 << 7,
        MorphBloat = 1 << 8,
        ForeignAssets = 1 << 9,
        ScanFailed = 1 << 10,
    }

    [Flags]
    internal enum PkgRiskFlags
    {
        None = 0,
        HasScripts = 1 << 0,
        HasDll = 1 << 1,
        HasAssetBundle = 1 << 2,
        FlaggedHigh = 1 << 3,
        FlaggedLow = 1 << 4,
    }

    internal sealed class PackageInsightRecord
    {
        internal string Uid;
        internal long VarSize;
        internal long VarMtimeTicks;
        internal int ScanVersion;

        internal PkgIssueFlags Issues;
        internal PkgRiskFlags Risk;

        internal int MorphCount;
        internal int ScriptCount;
        internal int DllCount;
        internal int AssetBundleCount;

        internal string[] UndeclaredDeps;
        internal string[] Details;

        internal bool HasIssues { get { return Issues != PkgIssueFlags.None; } }
        internal bool HasPluginContent
        {
            get { return (Risk & (PkgRiskFlags.HasScripts | PkgRiskFlags.HasDll | PkgRiskFlags.HasAssetBundle)) != 0; }
        }
        internal bool IsFlagged
        {
            get { return (Risk & (PkgRiskFlags.FlaggedHigh | PkgRiskFlags.FlaggedLow)) != 0; }
        }

        internal int UndeclaredCount { get { return UndeclaredDeps != null ? UndeclaredDeps.Length : 0; } }

        internal string BuildReviewSignature()
        {
            return VarSize.ToString() + ":" + VarMtimeTicks.ToString() + ":" + ((int)Risk).ToString();
        }
    }

    internal sealed class InsightRollup
    {
        internal int Scanned;
        internal int WithIssues;
        internal int WithPluginContent;
        internal int Flagged;
        internal int Unreviewed;

        internal readonly int[] IssueCounts = new int[VpbInsightLabels.AllIssues.Length];
        internal readonly int[] RiskCounts = new int[VpbInsightLabels.AllRisks.Length];

        internal int Issue(PkgIssueFlags f)
        {
            PkgIssueFlags[] all = VpbInsightLabels.AllIssues;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == f) return IssueCounts[i];
            }
            return 0;
        }

        internal int RiskOf(PkgRiskFlags f)
        {
            PkgRiskFlags[] all = VpbInsightLabels.AllRisks;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == f) return RiskCounts[i];
            }
            return 0;
        }
    }

    internal static class VpbInsightLabels
    {
        internal static readonly PkgIssueFlags[] AllIssues = new[]
        {
            PkgIssueFlags.UndeclaredDeps,
            PkgIssueFlags.LocalReferences,
            PkgIssueFlags.ReferenceIssues,
            PkgIssueFlags.PreloadMorphs,
            PkgIssueFlags.MetaMissing,
            PkgIssueFlags.MetaUnparsable,
            PkgIssueFlags.NameMismatch,
            PkgIssueFlags.NameFormatBad,
            PkgIssueFlags.MorphBloat,
            PkgIssueFlags.ForeignAssets,
            PkgIssueFlags.ScanFailed,
        };

        internal static readonly PkgRiskFlags[] AllRisks = new[]
        {
            PkgRiskFlags.FlaggedHigh,
            PkgRiskFlags.FlaggedLow,
            PkgRiskFlags.HasDll,
            PkgRiskFlags.HasScripts,
            PkgRiskFlags.HasAssetBundle,
        };

        internal static string Issue(PkgIssueFlags f)
        {
            switch (f)
            {
                case PkgIssueFlags.MetaMissing:
                    return VPBTranslation.T("insights.issue.meta_missing", "No meta.json");
                case PkgIssueFlags.MetaUnparsable:
                    return VPBTranslation.T("insights.issue.meta_unparsable", "meta.json will not parse");
                case PkgIssueFlags.NameMismatch:
                    return VPBTranslation.T("insights.issue.name_mismatch", "meta.json name ≠ file name");
                case PkgIssueFlags.NameFormatBad:
                    return VPBTranslation.T("insights.issue.name_format", "File name not Creator.Package.N.var");
                case PkgIssueFlags.ReferenceIssues:
                    return VPBTranslation.T("insights.issue.ref_issues", "Packaged with reference issues");
                case PkgIssueFlags.PreloadMorphs:
                    return VPBTranslation.T("insights.issue.preload_morphs", "Forces morph preload");
                case PkgIssueFlags.LocalReferences:
                    return VPBTranslation.T("insights.issue.local_refs", "References local Custom/ files");
                case PkgIssueFlags.UndeclaredDeps:
                    return VPBTranslation.T("insights.issue.undeclared", "Undeclared dependencies");
                case PkgIssueFlags.MorphBloat:
                    return VPBTranslation.T("insights.issue.morph_bloat", "Large morph payload");
                case PkgIssueFlags.ForeignAssets:
                    return VPBTranslation.T("insights.issue.foreign_assets", "Contains another creator's assets");
                case PkgIssueFlags.ScanFailed:
                    return VPBTranslation.T("insights.issue.scan_failed", "Could not be read");
                default:
                    return f.ToString();
            }
        }

        internal static string IssueTip(PkgIssueFlags f)
        {
            switch (f)
            {
                case PkgIssueFlags.MetaMissing:
                    return VPBTranslation.T("insights.tip.meta_missing", "The archive has no meta.json, so VaM cannot resolve its dependencies or license.");
                case PkgIssueFlags.MetaUnparsable:
                    return VPBTranslation.T("insights.tip.meta_unparsable", "meta.json is present but malformed — usually a hand edit that broke the JSON.");
                case PkgIssueFlags.NameMismatch:
                    return VPBTranslation.T("insights.tip.name_mismatch", "The creator/package recorded inside meta.json does not match the .var file name. References to this package can fail to resolve.");
                case PkgIssueFlags.NameFormatBad:
                    return VPBTranslation.T("insights.tip.name_format", "The file name does not follow Creator.Package.Version.var, which VaM relies on for version resolution.");
                case PkgIssueFlags.ReferenceIssues:
                    return VPBTranslation.T("insights.tip.ref_issues", "meta.json records hadReferenceIssues — the author packaged it with unresolved references.");
                case PkgIssueFlags.PreloadMorphs:
                    return VPBTranslation.T("insights.tip.preload_morphs", "meta.json sets preloadMorphs, so every morph in this package is loaded at startup. Costs launch time and memory.");
                case PkgIssueFlags.LocalReferences:
                    return VPBTranslation.T("insights.tip.local_refs", "Content points at loose Custom/ paths instead of packaged files. Works on the author's machine, breaks elsewhere.");
                case PkgIssueFlags.UndeclaredDeps:
                    return VPBTranslation.T("insights.tip.undeclared", "Package content references other packages that meta.json never declares. These are counted as missing dependencies.");
                case PkgIssueFlags.MorphBloat:
                    return VPBTranslation.T("insights.tip.morph_bloat", "An unusually large number of morphs is bundled here — a common source of load time and memory growth.");
                case PkgIssueFlags.ForeignAssets:
                    return VPBTranslation.T("insights.tip.foreign_assets", "Clothing/hair authored by a different creator is bundled inside instead of referenced.");
                case PkgIssueFlags.ScanFailed:
                    return VPBTranslation.T("insights.tip.scan_failed", "The archive could not be opened or read during the scan.");
                default:
                    return "";
            }
        }

        internal static string Risk(PkgRiskFlags f)
        {
            switch (f)
            {
                case PkgRiskFlags.HasScripts:
                    return VPBTranslation.T("insights.risk.scripts", "Contains scripts (.cs)");
                case PkgRiskFlags.HasDll:
                    return VPBTranslation.T("insights.risk.dll", "Contains a compiled DLL");
                case PkgRiskFlags.HasAssetBundle:
                    return VPBTranslation.T("insights.risk.assetbundle", "Contains an assetbundle");
                case PkgRiskFlags.FlaggedHigh:
                    return VPBTranslation.T("insights.risk.flagged_high", "Script uses sensitive APIs");
                case PkgRiskFlags.FlaggedLow:
                    return VPBTranslation.T("insights.risk.flagged_low", "Script uses notable APIs");
                default:
                    return f.ToString();
            }
        }

        internal static string RiskTip(PkgRiskFlags f)
        {
            switch (f)
            {
                case PkgRiskFlags.HasScripts:
                    return VPBTranslation.T("insights.tip.scripts", "Plugin source is bundled. Source is readable — open the package to review it before running.");
                case PkgRiskFlags.HasDll:
                    return VPBTranslation.T("insights.tip.dll", "A pre-compiled assembly is bundled. Its behaviour cannot be read from inside VaM, so it can only be taken on trust.");
                case PkgRiskFlags.HasAssetBundle:
                    return VPBTranslation.T("insights.tip.assetbundle", "An assetbundle is bundled. Bundles can carry scripts, so they cannot be inspected here.");
                case PkgRiskFlags.FlaggedHigh:
                    return VPBTranslation.T("insights.tip.flagged_high", "Bundled script text mentions APIs worth a read-through (process launch, network, file writes). This is a prompt to review, not a verdict.");
                case PkgRiskFlags.FlaggedLow:
                    return VPBTranslation.T("insights.tip.flagged_low", "Bundled script text mentions APIs worth knowing about (reflection, web requests). Common in legitimate plugins.");
                default:
                    return "";
            }
        }
    }
}
