using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VPB
{
    public static class BoneViewMode
    {
        private static BoneViewRenderer _renderer;
        private static bool _enabled;

        public static bool IsEnabled => _enabled;

        public static void Toggle()
        {
            if (_enabled)
                Disable();
            else
                Enable();
        }

        public static void Enable()
        {
            _enabled = true;
            EnsureRenderer();
            if (_renderer != null)
                _renderer.enabled = true;
            LogUtil.Log("[BoneView] Bone View ON");
        }

        public static void Disable()
        {
            _enabled = false;
            if (_renderer != null)
                _renderer.enabled = false;
            LogUtil.Log("[BoneView] Bone View OFF");
        }

        private static void EnsureRenderer()
        {
            if (_renderer != null && _renderer.gameObject != null)
                return;

            Camera cam = Camera.main;
            if (cam == null)
            {
                Camera[] cams = Camera.allCameras;
                for (int i = 0; i < cams.Length; i++)
                {
                    if (cams[i] != null && cams[i].gameObject.activeInHierarchy)
                    {
                        cam = cams[i];
                        break;
                    }
                }
            }
            if (cam == null)
            {
                LogUtil.LogWarning("[BoneView] No camera found - renderer not attached");
                return;
            }

            _renderer = cam.gameObject.GetComponent<BoneViewRenderer>();
            if (_renderer == null)
                _renderer = cam.gameObject.AddComponent<BoneViewRenderer>();
            LogUtil.Log("[BoneView] Renderer attached to camera: " + cam.gameObject.name);
        }
    }

    public class BoneViewRenderer : MonoBehaviour
    {
        private Material _mat;

        private static readonly Color ColorBone = new Color(0f, 1f, 0.25f, 0.85f);
        private static readonly Color ColorJoint = new Color(1f, 0.8f, 0f, 0.9f);

        private void Awake()
        {
            RebuildMaterial();
        }

        private void OnDestroy()
        {
            if (_mat != null)
            {
                Destroy(_mat);
                _mat = null;
            }
        }

        private void RebuildMaterial()
        {
            var shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null)
                shader = Shader.Find("Particles/Additive");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");
            _mat = new Material(shader);
            _mat.hideFlags = HideFlags.HideAndDontSave;
            _mat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
            _mat.SetInt("_ZWrite", 0);
            _mat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            _mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        }

        private static readonly List<SkinnedMeshRenderer> _smrBuf = new List<SkinnedMeshRenderer>();
        private static readonly HashSet<Transform> _drawnBones = new HashSet<Transform>();

        private void OnPostRender()
        {
            if (_mat == null)
            {
                RebuildMaterial();
                if (_mat == null) return;
            }

            _drawnBones.Clear();
            _smrBuf.Clear();

            GL.PushMatrix();
            _mat.SetPass(0);

            Scene scene = SceneManager.GetActiveScene();
            GameObject[] roots = scene.GetRootGameObjects();
            for (int r = 0; r < roots.Length; r++)
            {
                if (roots[r] == null) continue;
                roots[r].GetComponentsInChildren(true, _smrBuf);
                for (int s = 0; s < _smrBuf.Count; s++)
                {
                    SkinnedMeshRenderer smr = _smrBuf[s];
                    if (smr == null || !smr.gameObject.activeInHierarchy) continue;
                    DrawSmr(smr);
                }
                _smrBuf.Clear();
            }

            GL.PopMatrix();
        }

        private void DrawSmr(SkinnedMeshRenderer smr)
        {
            Transform[] bones = smr.bones;
            if (bones == null || bones.Length == 0) return;

            GL.Begin(GL.LINES);
            for (int i = 0; i < bones.Length; i++)
            {
                Transform bone = bones[i];
                if (bone == null) continue;

                Transform parent = bone.parent;
                bool parentIsBone = parent != null && BoneIsInArray(bones, parent);

                if (parentIsBone)
                {
                    if (_drawnBones.Add(bone))
                    {
                        GL.Color(ColorBone);
                        GL.Vertex(parent.position);
                        GL.Vertex(bone.position);
                    }
                }

                GL.Color(ColorJoint);
                DrawDot(bone.position, 0.01f);
            }
            GL.End();
        }

        private static void DrawDot(Vector3 center, float size)
        {
            GL.Vertex(center + Vector3.right * size);
            GL.Vertex(center - Vector3.right * size);
            GL.Vertex(center + Vector3.up * size);
            GL.Vertex(center - Vector3.up * size);
            GL.Vertex(center + Vector3.forward * size);
            GL.Vertex(center - Vector3.forward * size);
        }

        private static bool BoneIsInArray(Transform[] arr, Transform t)
        {
            for (int i = 0; i < arr.Length; i++)
                if (arr[i] == t) return true;
            return false;
        }
    }
}
