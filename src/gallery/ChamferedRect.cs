using UnityEngine;
using UnityEngine.UI;

namespace VPB
{
    public class ChamferedRect : Image
    {
        public enum ChamferSide { Left, Top }

        public float chamferSize = 20f;
        public ChamferSide chamferSide = ChamferSide.Left;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            if (sprite != null) { base.OnPopulateMesh(vh); return; }
            vh.Clear();
            Rect r = rectTransform.rect;
            float cX = chamferSide == ChamferSide.Left
                ? Mathf.Min(chamferSize, r.width)
                : Mathf.Min(chamferSize, r.width * 0.5f);
            float cY = Mathf.Min(chamferSize, r.height * 0.5f);
            UIVertex v = UIVertex.simpleVert;
            v.color = color;
            v.uv0 = Vector2.zero;

            if (chamferSide == ChamferSide.Left)
            {
                // Chamfer top-left and bottom-left corners — used for right-edge panels
                v.position = new Vector3(r.xMin, r.yMin + cY);  vh.AddVert(v); // 0
                v.position = new Vector3(r.xMin + cX, r.yMin);  vh.AddVert(v); // 1
                v.position = new Vector3(r.xMax, r.yMin);        vh.AddVert(v); // 2
                v.position = new Vector3(r.xMax, r.yMax);        vh.AddVert(v); // 3
                v.position = new Vector3(r.xMin + cX, r.yMax);  vh.AddVert(v); // 4
                v.position = new Vector3(r.xMin, r.yMax - cY);  vh.AddVert(v); // 5

                vh.AddTriangle(1, 2, 3);
                vh.AddTriangle(1, 3, 4);
                vh.AddTriangle(0, 1, 4);
                vh.AddTriangle(0, 4, 5);
            }
            else // Top
            {
                // Chamfer top-left and top-right corners — used for bottom-mounted panels expanding upward
                v.position = new Vector3(r.xMin, r.yMin);        vh.AddVert(v); // 0 bottom-left
                v.position = new Vector3(r.xMax, r.yMin);        vh.AddVert(v); // 1 bottom-right
                v.position = new Vector3(r.xMax, r.yMax - cY);  vh.AddVert(v); // 2 right, below top-right
                v.position = new Vector3(r.xMax - cX, r.yMax);  vh.AddVert(v); // 3 top-right chamfer
                v.position = new Vector3(r.xMin + cX, r.yMax);  vh.AddVert(v); // 4 top-left chamfer
                v.position = new Vector3(r.xMin, r.yMax - cY);  vh.AddVert(v); // 5 left, below top-left

                vh.AddTriangle(0, 1, 2);
                vh.AddTriangle(0, 2, 3);
                vh.AddTriangle(0, 3, 4);
                vh.AddTriangle(0, 4, 5);
            }
        }
    }
}
