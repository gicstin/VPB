using UnityEngine;
using UnityEngine.UI;

namespace VPB
{
    /// <summary>
    /// Solid-fill <see cref="Image"/> with rounded corners generated in <see cref="OnPopulateMesh"/>
    /// (no sprite required). Mirrors <see cref="ChamferedRect"/> but tessellates each corner into a
    /// quarter-circle arc instead of a single bevel cut.
    /// <para>
    /// Preferred sizing is <see cref="cornerRadiusFraction"/>: the effective radius is a fraction of the
    /// control's shorter side, evaluated at mesh-build time. Because Unity re-runs <c>OnPopulateMesh</c>
    /// whenever the RectTransform's dimensions change, this stays scale-resistant automatically — the
    /// corner keeps a constant visual proportion at every UI scale with no per-scale plumbing.
    /// <see cref="cornerRadius"/> (absolute local px) is the fallback when the fraction is 0.
    /// A resulting radius of 0 renders the stock quad for zero extra vertex cost.
    /// </para>
    /// </summary>
    public class RoundedRect : Image
    {
        [SerializeField] private float _cornerRadius = 0f;
        [SerializeField] private float _cornerRadiusFraction = 0f;
        [SerializeField] private int _cornerSegments = 6;

        /// <summary>Absolute corner radius in local pixels. Used only when <see cref="cornerRadiusFraction"/> is 0.</summary>
        public float cornerRadius
        {
            get { return _cornerRadius; }
            set { if (_cornerRadius != value) { _cornerRadius = value; SetVerticesDirty(); } }
        }

        /// <summary>Scale-resistant radius as a fraction (0..0.5) of the shorter side. Takes priority when &gt; 0.</summary>
        public float cornerRadiusFraction
        {
            get { return _cornerRadiusFraction; }
            set { float v = Mathf.Clamp(value, 0f, 0.5f); if (_cornerRadiusFraction != v) { _cornerRadiusFraction = v; SetVerticesDirty(); } }
        }

        /// <summary>Arc subdivisions per corner. Higher = smoother; each unit adds 4 vertices.</summary>
        public int cornerSegments
        {
            get { return _cornerSegments; }
            set { int v = Mathf.Max(1, value); if (_cornerSegments != v) { _cornerSegments = v; SetVerticesDirty(); } }
        }

        private float EffectiveRadius(Rect r)
        {
            float raw = _cornerRadiusFraction > 0f ? _cornerRadiusFraction * Mathf.Min(r.width, r.height) : _cornerRadius;
            return Mathf.Min(raw, r.width * 0.5f, r.height * 0.5f);
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            Rect r = rectTransform.rect;
            float radius = EffectiveRadius(r);
            // Sprites (icon fills) or a non-positive radius: defer to the stock quad renderer.
            if (sprite != null || radius <= 0f || _cornerSegments < 1)
            {
                base.OnPopulateMesh(vh);
                return;
            }

            vh.Clear();
            UIVertex vert = UIVertex.simpleVert;
            vert.color = color;
            vert.uv0 = Vector2.zero;

            // Fan origin at the rect centre.
            vert.position = r.center;
            vh.AddVert(vert);
            const int centerIdx = 0;

            // Corner arc centres, inset by the radius, in CCW draw order: BL, BR, TR, TL.
            Vector2[] arcCenter =
            {
                new Vector2(r.xMin + radius, r.yMin + radius),
                new Vector2(r.xMax - radius, r.yMin + radius),
                new Vector2(r.xMax - radius, r.yMax - radius),
                new Vector2(r.xMin + radius, r.yMax - radius),
            };
            // Each corner sweeps 90° CCW from this start angle (degrees).
            float[] startDeg = { 180f, 270f, 0f, 90f };

            int firstRim = -1;
            int prevRim = -1;
            for (int c = 0; c < 4; c++)
            {
                for (int s = 0; s <= _cornerSegments; s++)
                {
                    float ang = (startDeg[c] + 90f * s / _cornerSegments) * Mathf.Deg2Rad;
                    vert.position = arcCenter[c] + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * radius;
                    int cur = vh.currentVertCount;
                    vh.AddVert(vert);
                    if (firstRim < 0) firstRim = cur;
                    if (prevRim >= 0) vh.AddTriangle(centerIdx, prevRim, cur);
                    prevRim = cur;
                }
            }
            // Close the ring back to the first rim vertex.
            if (firstRim >= 0 && prevRim >= 0) vh.AddTriangle(centerIdx, prevRim, firstRim);
        }
    }

    /// <summary>
    /// Hollow rounded-rectangle border ring (used for the gallery hover/selection highlight). Draws a band
    /// of <see cref="borderThickness"/> px along the inner edge of its rect, with corners rounded to
    /// <see cref="cornerRadiusFraction"/> of the shorter side — matching a <see cref="RoundedRect"/> fill of
    /// the same fraction. Scale-resistant for the same reason (radius derived from the live rect).
    /// A fraction of 0 degrades gracefully to a plain rectangular ring.
    /// </summary>
    public class RoundedRectOutline : Image
    {
        [SerializeField] private float _cornerRadiusFraction = 0f;
        [SerializeField] private float _borderThickness = 2f;
        [SerializeField] private int _cornerSegments = 6;

        public float cornerRadiusFraction
        {
            get { return _cornerRadiusFraction; }
            set { float v = Mathf.Clamp(value, 0f, 0.5f); if (_cornerRadiusFraction != v) { _cornerRadiusFraction = v; SetVerticesDirty(); } }
        }

        public float borderThickness
        {
            get { return _borderThickness; }
            set { float v = Mathf.Max(0f, value); if (_borderThickness != v) { _borderThickness = v; SetVerticesDirty(); } }
        }

        public int cornerSegments
        {
            get { return _cornerSegments; }
            set { int v = Mathf.Max(1, value); if (_cornerSegments != v) { _cornerSegments = v; SetVerticesDirty(); } }
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            Rect r = rectTransform.rect;
            float t = Mathf.Min(_borderThickness, r.width * 0.5f, r.height * 0.5f);
            if (sprite != null || t <= 0f || _cornerSegments < 1)
            {
                base.OnPopulateMesh(vh);
                return;
            }

            float ro = Mathf.Clamp(_cornerRadiusFraction * Mathf.Min(r.width, r.height), 0f, Mathf.Min(r.width, r.height) * 0.5f);
            float ri = Mathf.Max(ro - t, 0f);

            vh.Clear();
            UIVertex vert = UIVertex.simpleVert;
            vert.color = color;
            vert.uv0 = Vector2.zero;

            // Outer arc centres (inset by ro) and inner arc centres (inset from the t-shrunk rect by ri).
            Vector2[] outerC =
            {
                new Vector2(r.xMin + ro, r.yMin + ro),
                new Vector2(r.xMax - ro, r.yMin + ro),
                new Vector2(r.xMax - ro, r.yMax - ro),
                new Vector2(r.xMin + ro, r.yMax - ro),
            };
            Vector2[] innerC =
            {
                new Vector2(r.xMin + t + ri, r.yMin + t + ri),
                new Vector2(r.xMax - t - ri, r.yMin + t + ri),
                new Vector2(r.xMax - t - ri, r.yMax - t - ri),
                new Vector2(r.xMin + t + ri, r.yMax - t - ri),
            };
            float[] startDeg = { 180f, 270f, 0f, 90f };

            int firstOuter = -1, firstInner = -1, prevOuter = -1, prevInner = -1;
            for (int c = 0; c < 4; c++)
            {
                for (int s = 0; s <= _cornerSegments; s++)
                {
                    float ang = (startDeg[c] + 90f * s / _cornerSegments) * Mathf.Deg2Rad;
                    Vector2 dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));

                    vert.position = outerC[c] + dir * ro;
                    int io = vh.currentVertCount; vh.AddVert(vert);
                    vert.position = innerC[c] + dir * ri;
                    int ii = vh.currentVertCount; vh.AddVert(vert);

                    if (firstOuter < 0) { firstOuter = io; firstInner = ii; }
                    if (prevOuter >= 0)
                    {
                        vh.AddTriangle(prevOuter, io, ii);
                        vh.AddTriangle(prevOuter, ii, prevInner);
                    }
                    prevOuter = io; prevInner = ii;
                }
            }
            // Close the band back to the first pair.
            if (firstOuter >= 0 && prevOuter >= 0)
            {
                vh.AddTriangle(prevOuter, firstOuter, firstInner);
                vh.AddTriangle(prevOuter, firstInner, prevInner);
            }
        }
    }
}
