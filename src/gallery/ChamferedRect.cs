using UnityEngine;
using UnityEngine.UI;

namespace VPB
{
    public class ChamferedRect : Image
    {
        public float chamferSize = 20f;
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            if (sprite != null) { base.OnPopulateMesh(vh); return; }
            vh.Clear();
            Rect r = rectTransform.rect;
            float cX = Mathf.Min(chamferSize, r.width);
            float cY = Mathf.Min(chamferSize, r.height * 0.5f);
            UIVertex v = UIVertex.simpleVert;
            v.color = color;
            v.uv0 = Vector2.zero;

            v.position = new Vector3(r.xMin, r.yMin + cY); vh.AddVert(v);
            v.position = new Vector3(r.xMin + cX, r.yMin); vh.AddVert(v);
            v.position = new Vector3(r.xMax, r.yMin); vh.AddVert(v);
            v.position = new Vector3(r.xMax, r.yMax); vh.AddVert(v);
            v.position = new Vector3(r.xMin + cX, r.yMax); vh.AddVert(v);
            v.position = new Vector3(r.xMin, r.yMax - cY); vh.AddVert(v);

            vh.AddTriangle(1, 2, 3);
            vh.AddTriangle(1, 3, 4);
            vh.AddTriangle(0, 1, 4);
            vh.AddTriangle(0, 4, 5);
        }
    }
}
