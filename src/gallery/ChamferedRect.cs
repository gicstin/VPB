using UnityEngine;
using UnityEngine.UI;

namespace VPB
{
    public class ChamferedRect : Image
    {
        public enum ChamferSide { Left, Right, Top, Bottom }

        public float chamferSize = 20f;
        public ChamferSide chamferSide = ChamferSide.Left;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            if (sprite != null) { base.OnPopulateMesh(vh); return; }
            vh.Clear();
            Rect r = rectTransform.rect;
            float c;
            if (chamferSide == ChamferSide.Left || chamferSide == ChamferSide.Right)
                c = Mathf.Min(chamferSize, r.width, r.height * 0.5f);
            else
                c = Mathf.Min(chamferSize, r.height, r.width * 0.5f);
            float cX = c;
            float cY = c;
            UIVertex v = UIVertex.simpleVert;
            v.color = color;
            v.uv0 = Vector2.zero;

            if (chamferSide == ChamferSide.Left)
            {
                v.position = new Vector3(r.xMin, r.yMin + cY);  vh.AddVert(v);
                v.position = new Vector3(r.xMin + cX, r.yMin);  vh.AddVert(v);
                v.position = new Vector3(r.xMax, r.yMin);        vh.AddVert(v);
                v.position = new Vector3(r.xMax, r.yMax);        vh.AddVert(v);
                v.position = new Vector3(r.xMin + cX, r.yMax);  vh.AddVert(v);
                v.position = new Vector3(r.xMin, r.yMax - cY);  vh.AddVert(v);

                vh.AddTriangle(1, 2, 3);
                vh.AddTriangle(1, 3, 4);
                vh.AddTriangle(0, 1, 4);
                vh.AddTriangle(0, 4, 5);
            }
            else if (chamferSide == ChamferSide.Right)
            {
                v.position = new Vector3(r.xMin, r.yMin);        vh.AddVert(v);
                v.position = new Vector3(r.xMax - cX, r.yMin);  vh.AddVert(v);
                v.position = new Vector3(r.xMax, r.yMin + cY);  vh.AddVert(v);
                v.position = new Vector3(r.xMax, r.yMax - cY);  vh.AddVert(v);
                v.position = new Vector3(r.xMax - cX, r.yMax);  vh.AddVert(v);
                v.position = new Vector3(r.xMin, r.yMax);        vh.AddVert(v);

                vh.AddTriangle(0, 1, 4);
                vh.AddTriangle(0, 4, 5);
                vh.AddTriangle(1, 2, 3);
                vh.AddTriangle(1, 3, 4);
            }
            else if (chamferSide == ChamferSide.Top)
            {
                v.position = new Vector3(r.xMin, r.yMin);        vh.AddVert(v);
                v.position = new Vector3(r.xMax, r.yMin);        vh.AddVert(v);
                v.position = new Vector3(r.xMax, r.yMax - cY);  vh.AddVert(v);
                v.position = new Vector3(r.xMax - cX, r.yMax);  vh.AddVert(v);
                v.position = new Vector3(r.xMin + cX, r.yMax);  vh.AddVert(v);
                v.position = new Vector3(r.xMin, r.yMax - cY);  vh.AddVert(v);

                vh.AddTriangle(0, 1, 2);
                vh.AddTriangle(0, 2, 3);
                vh.AddTriangle(0, 3, 4);
                vh.AddTriangle(0, 4, 5);
            }
            else
            {
                v.position = new Vector3(r.xMin, r.yMin + cY);  vh.AddVert(v);
                v.position = new Vector3(r.xMin + cX, r.yMin);  vh.AddVert(v);
                v.position = new Vector3(r.xMax - cX, r.yMin);  vh.AddVert(v);
                v.position = new Vector3(r.xMax, r.yMin + cY);  vh.AddVert(v);
                v.position = new Vector3(r.xMax, r.yMax);        vh.AddVert(v);
                v.position = new Vector3(r.xMin, r.yMax);        vh.AddVert(v);

                vh.AddTriangle(0, 3, 4);
                vh.AddTriangle(0, 4, 5);
                vh.AddTriangle(0, 1, 2);
                vh.AddTriangle(0, 2, 3);
            }
        }
    }
}
