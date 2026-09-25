using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace VPB
{
    public class RoundedRect : Image
    {
        private static readonly List<RoundedRect> s_Live = new List<RoundedRect>(256);

        /// <summary>Enabled instances, so global radius sync never has to scan every loaded object.</summary>
        public static List<RoundedRect> Live { get { return s_Live; } }

        protected override void OnEnable()
        {
            base.OnEnable();
            s_Live.Add(this);
        }

        protected override void OnDisable()
        {
            s_Live.Remove(this);
            base.OnDisable();
        }

        [SerializeField] private float _cornerRadius = 0f;
        [SerializeField] private float _cornerRadiusFraction = 0f;
        [SerializeField] private int _cornerSegments = 6;
        [SerializeField] private bool _excludeFromGlobalRadiusSync;

        /// <summary>Absolute corner radius in local pixels. Used only when <see cref="cornerRadiusFraction"/> is 0.</summary>
        public float cornerRadius
        {
            get { return _cornerRadius; }
            set { if (_cornerRadius != value) { _cornerRadius = value; SetVerticesDirty(); } }
        }

        public float cornerRadiusFraction
        {
            get { return _cornerRadiusFraction; }
            set { float v = Mathf.Clamp(value, 0f, 0.5f); if (_cornerRadiusFraction != v) { _cornerRadiusFraction = v; SetVerticesDirty(); } }
        }

        public int cornerSegments
        {
            get { return _cornerSegments; }
            set { int v = Mathf.Max(1, value); if (_cornerSegments != v) { _cornerSegments = v; SetVerticesDirty(); } }
        }

        public bool excludeFromGlobalRadiusSync
        {
            get { return _excludeFromGlobalRadiusSync; }
            set { _excludeFromGlobalRadiusSync = value; }
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
            if (sprite != null || radius <= 0f || _cornerSegments < 1)
            {
                base.OnPopulateMesh(vh);
                return;
            }

            vh.Clear();
            UIVertex vert = UIVertex.simpleVert;
            vert.color = color;
            vert.uv0 = Vector2.zero;

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
            if (firstRim >= 0 && prevRim >= 0) vh.AddTriangle(centerIdx, prevRim, firstRim);
        }
    }

    /// <summary>Hollow rounded-rectangle border ring (used for the gallery hover/selection highlight).</summary>
    public class RoundedRectOutline : Image
    {
        private static readonly List<RoundedRectOutline> s_Live = new List<RoundedRectOutline>(256);

        /// <summary>Enabled instances, so global radius sync never has to scan every loaded object.</summary>
        public static List<RoundedRectOutline> Live { get { return s_Live; } }

        protected override void OnEnable()
        {
            base.OnEnable();
            s_Live.Add(this);
        }

        protected override void OnDisable()
        {
            s_Live.Remove(this);
            base.OnDisable();
        }

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
            if (firstOuter >= 0 && prevOuter >= 0)
            {
                vh.AddTriangle(prevOuter, firstOuter, firstInner);
                vh.AddTriangle(prevOuter, firstInner, prevInner);
            }
        }
    }
}
