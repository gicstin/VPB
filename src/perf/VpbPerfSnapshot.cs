using System.Collections.Generic;

namespace VPB
{
    internal sealed class VpbPersonPerfSnapshot
    {
        public string AtomUid;
        public readonly List<VpbHairPerfSnapshot> Hair = new List<VpbHairPerfSnapshot>();
        public bool HasMirrorRender;
        public string MirrorTextureSize;
    }

    internal sealed class VpbHairPerfSnapshot
    {
        public string HairItemUid;
        public string ControlUid;
        public float CurveDensity;
        public float HairMultiplier;
    }

    internal sealed class VpbGlobalPerfSnapshot
    {
        public bool Captured;
        public float RenderScale;
        public int MsaaLevel;
        public int PixelLightCount;
        public int ShaderLod;
        public int SmoothPasses;
        public bool MirrorReflections;
        public bool RealtimeReflectionProbes;
        public bool SoftPhysics;
        public int GlowEffects;
    }

    internal sealed class VpbPerfSnapshot
    {
        public readonly List<VpbPersonPerfSnapshot> Persons = new List<VpbPersonPerfSnapshot>();
        public readonly VpbGlobalPerfSnapshot Global = new VpbGlobalPerfSnapshot();
        public bool Captured;
    }
}
