using System;
using System.Collections.Generic;

namespace VPB.Outliner
{
    internal static class OutlinerParamPriority
    {
        internal const int Uncurated = 1000;
        internal const int RankSpread = 10000;

        static readonly string[] AtomOrder =
        {
            "on", "hidden", "collisionEnabled", "freezePhysics", "ResetPhysics",
            "IsolateEditAtom", "SelectContainingSubScene"
        };

        static readonly string[] ControlOrder =
        {
            "on", "physicsEnabled", "collisionEnabled", "useGravity",
            "positionState", "rotationState",
            "holdPositionSpring", "holdPositionDamper",
            "holdRotationSpring", "holdRotationDamper",
            "mass", "interactableInPlayMode", "possessable", "Reset"
        };

        static readonly string[] LightOrder =
        {
            "on", "type", "intensity", "color", "range", "spotAngle",
            "shadowsOn", "shadowStrength", "shadowResolution", "renderType",
            "showHalo", "showDust", "pointBias"
        };

        static readonly string[] AudioOrder =
        {
            "volume", "pitch", "loop", "spatialBlend", "minDistance", "maxDistance",
            "Pause", "UnPause", "Stop", "StopLoop", "StopAndClearQueue", "ClearQueue",
            "audioRolloffMode", "stereoPan", "stereoSpread", "equalizeVolume", "spatialize",
            "delayBetweenQueuedClips", "volumeTriggerMultiplier", "volumeTriggerQuickness"
        };

        static readonly string[] CameraOrder =
        {
            "cameraOn", "FOV", "useAsMainCamera", "showHUDView", "useAudioListener", "maskSelection"
        };

        static readonly string[] AssetOrder =
        {
            "assetUrl", "assetName", "registerCanvases", "showCanvases",
            "importLightmaps", "importLightProbes", "loadDll", "assetDllUrl"
        };

        static readonly string[] GeometryOrder =
        {
            "character", "useAdvancedColliders", "useAuxBreastColliders", "clothingItem", "hairItem"
        };

        internal static int Rank(string storableId, string paramId, OutlinerParamKind kind, int arrival)
        {
            int curated = CuratedIndex(storableId, paramId);
            int band = curated >= 0 ? curated : Uncurated + KindBand(kind);
            int order = band * RankSpread + arrival;
            return order;
        }

        internal static int KindBand(OutlinerParamKind kind)
        {
            switch (kind)
            {
                case OutlinerParamKind.StringChooser: return 0;
                case OutlinerParamKind.Bool: return 1;
                case OutlinerParamKind.Color: return 2;
                case OutlinerParamKind.Url: return 3;
                case OutlinerParamKind.Float: return 4;
                case OutlinerParamKind.String: return 5;
                case OutlinerParamKind.Action: return 6;
                default: return 7;
            }
        }

        internal static int CuratedIndex(string storableId, string paramId)
        {
            string[] table = TableFor(storableId);
            if (table == null) return -1;
            for (int i = 0; i < table.Length; i++)
            {
                if (string.Equals(table[i], paramId, StringComparison.Ordinal)) return i;
            }
            return -1;
        }

        static string[] TableFor(string storableId)
        {
            if (string.IsNullOrEmpty(storableId)) return null;
            if (string.Equals(storableId, OutlinerParamCatalog.AtomStorableId, StringComparison.Ordinal))
                return AtomOrder;
            if (string.Equals(storableId, "control", StringComparison.OrdinalIgnoreCase)) return ControlOrder;
            if (string.Equals(storableId, "Light", StringComparison.OrdinalIgnoreCase)) return LightOrder;
            if (string.Equals(storableId, "AudioSource", StringComparison.OrdinalIgnoreCase)) return AudioOrder;
            if (string.Equals(storableId, "CameraControl", StringComparison.OrdinalIgnoreCase)) return CameraOrder;
            if (string.Equals(storableId, "asset", StringComparison.OrdinalIgnoreCase)) return AssetOrder;
            if (string.Equals(storableId, "geometry", StringComparison.OrdinalIgnoreCase)) return GeometryOrder;
            return null;
        }

        internal static void Sort(List<OutlinerParamDescriptor> list)
        {
            if (list == null || list.Count < 2) return;
            list.Sort(ByOrder);
        }

        static readonly Comparison<OutlinerParamDescriptor> ByOrder = CompareOrder;

        static int CompareOrder(OutlinerParamDescriptor a, OutlinerParamDescriptor b)
        {
            int oa = a == null ? int.MaxValue : a.Order;
            int ob = b == null ? int.MaxValue : b.Order;
            return oa.CompareTo(ob);
        }
    }
}
