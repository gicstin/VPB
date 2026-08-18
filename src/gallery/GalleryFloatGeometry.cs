using SimpleJSON;
using UnityEngine;
using VPB.src.util;

namespace VPB
{
    internal sealed class FloatGeometryKeys
    {
        internal readonly string PosSaved;
        internal readonly string PosX;
        internal readonly string PosY;
        internal readonly string SizeSaved;
        internal readonly string WidthRef;
        internal readonly string HeightRef;

        internal FloatGeometryKeys(string prefix, string suffix)
        {
            PosSaved = prefix + "PosSaved" + suffix;
            PosX = prefix + "PosX" + suffix;
            PosY = prefix + "PosY" + suffix;
            SizeSaved = prefix + "SizeSaved" + suffix;
            WidthRef = prefix + "WidthRef" + suffix;
            HeightRef = prefix + "HeightRef" + suffix;
        }
    }

    /// <summary>Saved geometry of one float window for one interaction mode (VR or desktop).</summary>
    public sealed class FloatGeometrySlot
    {
        public bool PosSaved;
        public float PosX;
        public float PosY;
        public bool SizeSaved;
        public float WidthRef;
        public float HeightRef;

        private readonly float _defaultWidthRef;
        private readonly float _defaultHeightRef;

        internal FloatGeometrySlot(float defaultWidthRef, float defaultHeightRef)
        {
            _defaultWidthRef = defaultWidthRef;
            _defaultHeightRef = defaultHeightRef;
            Reset();
        }

        public void Reset()
        {
            PosSaved = false;
            PosX = 0f;
            PosY = 0f;
            SizeSaved = false;
            WidthRef = _defaultWidthRef;
            HeightRef = _defaultHeightRef;
        }

        public void CopyFrom(FloatGeometrySlot other)
        {
            if (other == null) return;
            PosSaved = other.PosSaved;
            PosX = other.PosX;
            PosY = other.PosY;
            SizeSaved = other.SizeSaved;
            WidthRef = other.WidthRef;
            HeightRef = other.HeightRef;
        }

        internal static bool HasAnyKey(JSONNode node, FloatGeometryKeys keys)
        {
            return node != null && keys != null
                && (node[keys.PosSaved] != null || node[keys.SizeSaved] != null
                    || node[keys.PosX] != null || node[keys.WidthRef] != null);
        }

        internal void Load(JSONNode node, FloatGeometryKeys keys)
        {
            if (node == null || keys == null) return;
            if (node[keys.PosSaved] != null) PosSaved = node[keys.PosSaved].AsBool;
            if (node[keys.PosX] != null) PosX = node[keys.PosX].AsFloat;
            if (node[keys.PosY] != null) PosY = node[keys.PosY].AsFloat;
            if (node[keys.SizeSaved] != null) SizeSaved = node[keys.SizeSaved].AsBool;
            if (node[keys.WidthRef] != null) WidthRef = Mathf.Max(0f, node[keys.WidthRef].AsFloat);
            if (node[keys.HeightRef] != null) HeightRef = Mathf.Max(0f, node[keys.HeightRef].AsFloat);
        }

        internal void Save(JSONClass node, FloatGeometryKeys keys)
        {
            if (node == null || keys == null) return;
            node[keys.PosSaved].AsBool = PosSaved;
            node[keys.PosX].AsFloat = PosX;
            node[keys.PosY].AsFloat = PosY;
            node[keys.SizeSaved].AsBool = SizeSaved;
            node[keys.WidthRef].AsFloat = Mathf.Max(0f, WidthRef);
            node[keys.HeightRef].AsFloat = Mathf.Max(0f, HeightRef);
        }
    }

    /// <summary>
    /// Per-mode float geometry. VR and desktop keep independent slots; <see cref="Current"/>
    /// resolves to the live mode so existing call sites stay mode-agnostic.
    /// Configs written before the split seed both slots from the legacy unsuffixed keys.
    /// </summary>
    public sealed class FloatGeometryPair
    {
        public readonly FloatGeometrySlot VR;
        public readonly FloatGeometrySlot Desktop;

        private readonly FloatGeometryKeys _vrKeys;
        private readonly FloatGeometryKeys _desktopKeys;
        private readonly FloatGeometryKeys _legacyKeys;

        internal FloatGeometryPair(string prefix, float defaultWidthRef, float defaultHeightRef)
        {
            VR = new FloatGeometrySlot(defaultWidthRef, defaultHeightRef);
            Desktop = new FloatGeometrySlot(defaultWidthRef, defaultHeightRef);
            _vrKeys = new FloatGeometryKeys(prefix, "_VR");
            _desktopKeys = new FloatGeometryKeys(prefix, "_Desktop");
            _legacyKeys = new FloatGeometryKeys(prefix, "");
        }

        public FloatGeometrySlot Current
        {
            get { return XrUtils.IsVrActive() ? VR : Desktop; }
        }

        public FloatGeometrySlot ForMode(bool vr)
        {
            return vr ? VR : Desktop;
        }

        public void Reset()
        {
            VR.Reset();
            Desktop.Reset();
        }

        internal void Load(JSONNode node)
        {
            if (node == null) return;

            bool hasVr = FloatGeometrySlot.HasAnyKey(node, _vrKeys);
            bool hasDesktop = FloatGeometrySlot.HasAnyKey(node, _desktopKeys);

            if (!hasVr && !hasDesktop)
            {
                if (!FloatGeometrySlot.HasAnyKey(node, _legacyKeys)) return;
                Desktop.Load(node, _legacyKeys);
                VR.CopyFrom(Desktop);
                return;
            }

            if (hasVr) VR.Load(node, _vrKeys);
            if (hasDesktop) Desktop.Load(node, _desktopKeys);
        }

        internal void Save(JSONClass node)
        {
            if (node == null) return;
            VR.Save(node, _vrKeys);
            Desktop.Save(node, _desktopKeys);
        }
    }
}
