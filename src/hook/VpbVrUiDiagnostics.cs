using System;
using UnityEngine;

namespace VPB
{
    internal static class VpbVrUiDiagnostics
    {
        private static int snapshotNumber;

        // Called from scene completion on the main thread, never from a render callback.
        internal static void CaptureSceneComplete(string context)
        {
            try
            {
                if (!UnityEngine.XR.XRSettings.enabled) return;
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning("[VPB][VRUI] XR state failed: " + ex.Message);
                return;
            }
            string prefix = "[VPB][VRUI] snapshot=" + (++snapshotNumber) + " frame=" + Time.frameCount
                + " context=" + (context ?? "").Replace('\r', ' ').Replace('\n', ' ') + " ";
            SuperController sc = SuperController.singleton;
            Section(prefix, "native", delegate
            {
                if (sc == null) { LogUtil.LogWarning(prefix + "native=null"); return; }
                LogUtil.LogWarning(prefix + "monitorOnly=" + sc.IsMonitorOnly + " hudOnMonitor=" + sc.MainHUDAnchoredOnMonitor);
                LogTransform(prefix, "mainHUD", sc.mainHUD);
                LogTransform(prefix, "worldUI", sc.worldUI);
            });
            Section(prefix, "OVR camera", delegate { LogCamera(prefix, "OVR", sc != null ? sc.OVRCenterCamera : null); });
            Section(prefix, "Vive camera", delegate { LogCamera(prefix, "Vive", sc != null ? sc.ViveCenterCamera : null); });
            Section(prefix, "monitor camera", delegate { LogCamera(prefix, "monitor", sc != null ? sc.MonitorCenterCamera : null); });
            Section(prefix, "main camera", delegate { LogCamera(prefix, "main", Camera.main); });
            Section(prefix, "center UI camera", delegate
            {
                Camera center = CameraTarget.centerTarget != null ? CameraTarget.centerTarget.targetCamera : null;
                LogCamera(prefix, "centerTarget", center);
                Transform hook = center != null ? center.transform.Find("CameraHook") : null;
                LogCamera(prefix, "centerTarget/CameraHook", hook != null ? hook.GetComponent<Camera>() : null);
            });
            Section(prefix, "UI material", delegate
            {
                var prefs = UserPreferences.singleton;
                if (prefs == null) { LogUtil.LogWarning(prefix + "preferences=null"); return; }
                var panel = prefs.panelForUIMaterial;
                Material material = panel != null ? panel.defaultMaterial : null;
                Shader shader = material != null ? material.shader : null;
                LogUtil.LogWarning(prefix + "overlayUI=" + prefs.overlayUI + " material=" + Id(material)
                    + " shader=" + Id(shader) + " shaderName=" + (shader != null ? shader.name : "null")
                    + " supported=" + (shader != null && shader.isSupported)
                    + " queue=" + (material != null ? material.renderQueue : -1)
                    + " texture=" + Id(material != null ? material.mainTexture : null)
                    + " overlayShader=" + Id(prefs.overlayUIShader) + " defaultShader=" + Id(prefs.defaultUIShader));
            });
            Section(prefix, "VPB canvas", delegate
            {
                var gallery = Gallery.singleton;
                var panels = gallery != null ? gallery.Panels : null;
                if (panels == null || panels.Count == 0) { LogUtil.LogWarning(prefix + "VPB panels=0"); return; }
                // One representative panel keeps diagnostics bounded even with many galleries.
                for (int i = 0; i < panels.Count; i++)
                {
                    if (panels[i] == null || panels[i].canvas == null) continue;
                    Canvas canvas = panels[i].canvas;
                    LogUtil.LogWarning(prefix + "VPB panel=" + i + " canvas=" + Id(canvas) + " enabled=" + canvas.enabled
                        + " mode=" + canvas.renderMode + " eventCamera=" + Id(canvas.worldCamera));
                    LogTransform(prefix, "VPB", canvas.transform);
                    break;
                }
            });
        }

        private static void Section(string prefix, string name, Action read)
        {
            try { read(); }
            catch (Exception ex) { LogUtil.LogWarning(prefix + name + " failed: " + ex.Message); }
        }

        private static int Id(UnityEngine.Object value)
        {
            return value != null ? value.GetInstanceID() : 0;
        }

        private static void LogTransform(string prefix, string name, Transform value)
        {
            if (value == null) { LogUtil.LogWarning(prefix + name + "=null"); return; }
            LogUtil.LogWarning(prefix + name + " id=" + Id(value) + " active=" + value.gameObject.activeInHierarchy
                + " layer=" + value.gameObject.layer + " position=" + value.position + " rotation=" + value.eulerAngles
                + " scale=" + value.lossyScale + " parent=" + Id(value.parent));
        }

        private static void LogCamera(string prefix, string name, Camera camera)
        {
            if (camera == null) { LogUtil.LogWarning(prefix + "camera=" + name + " null"); return; }
            LogUtil.LogWarning(prefix + "camera=" + name + " id=" + Id(camera) + " enabled=" + camera.enabled
                + " active=" + camera.gameObject.activeInHierarchy + " mask=" + camera.cullingMask
                + " uiLayer=" + ((camera.cullingMask & (1 << 5)) != 0) + " stereo=" + camera.stereoTargetEye
                + " uiLayers=" + (camera.cullingMask & LayerMask.GetMask("UI", "LoadUI", "ScreenUI", "GUI"))
                + " target=" + Id(camera.targetTexture) + " near=" + camera.nearClipPlane + " far=" + camera.farClipPlane
                + " position=" + camera.transform.position + " rotation=" + camera.transform.eulerAngles);
        }
    }
}
