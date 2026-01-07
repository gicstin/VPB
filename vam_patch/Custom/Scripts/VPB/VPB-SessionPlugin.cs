using System;
using UnityEngine;
using UnityEngine.UI;
using SimpleJSON;

namespace VPB
{
    public class VPB_SessionPlugin : MVRScript
    {
        // IMPORTANT - DO NOT make custom enums. The dynamic C# complier crashes Unity when it encounters these for
        // some reason

        // IMPORTANT - DO NOT OVERRIDE Awake() as it is used internally by MVRScript - instead use Init() function which
        // is called right after creation

        private GameObject _messager;
        public GameObject Messager
        {
            get
            {
                if (_messager == null)
                {
                    _messager = GameObject.Find("var_browser_messager");
                }
                return _messager;
            }
        }

        void CreateHeader(string v, bool rightSide, Color color)
        {
            var header = CreateSpacer(rightSide);
            header.height = 40;
            var text = header.gameObject.AddComponent<Text>();
            text.text = v;
            text.font = (Font)Resources.GetBuiltinResource(typeof(Font), "Arial.ttf");
            text.fontSize = 30;
            text.fontStyle = FontStyle.Bold;
            text.color = color;
        }

        UIDynamicButton CreateBigButton(string label, bool rightSide = false)
        {
            var btn = CreateButton(label, rightSide);
            btn.height = 80;
            return btn;
        }

        public override void Init()
        {
            try
            {
                // Initial check to show status
                if (Messager == null)
                    CreateHeader("VPB Inactive", false, Color.red);
                else
                    CreateHeader("VPB Active", false, Color.white);

                CreateButton("Refresh").button.onClick.AddListener(Refresh);
                RegisterAction(new JSONStorableAction("Refresh", Refresh));
                CreateButton("Remove Invalid Vars").button.onClick.AddListener(RemoveInvalidVars);
                RegisterAction(new JSONStorableAction("RemoveInvalidVars", RemoveInvalidVars));

                CreateButton("Uninstall All").button.onClick.AddListener(UninstallAll);
                RegisterAction(new JSONStorableAction("UninstallAll", UninstallAll));

                CreateButton("Hub Browse").button.onClick.AddListener(OpenHubBrowse);
                RegisterAction(new JSONStorableAction("OpenHubBrowse", OpenHubBrowse));

                CreateHeader("Custom", true, Color.white);
                CreateBigButton("Scene", true).button.onClick.AddListener(OpenCustomScene);
                RegisterAction(new JSONStorableAction("OpenCustomScene", OpenCustomScene));
                CreateButton("Saved Person", true).button.onClick.AddListener(OpenCustomSavedPerson);
                RegisterAction(new JSONStorableAction("OpenCustomSavedPerson", OpenCustomSavedPerson));
                CreateButton("Person Preset", true).button.onClick.AddListener(OpenPersonPreset);
                RegisterAction(new JSONStorableAction("OpenPersonPreset", OpenPersonPreset));

                CreateHeader("Category", true, Color.white);
                CreateBigButton("Scene", true).button.onClick.AddListener(OpenCategoryScene);
                RegisterAction(new JSONStorableAction("OpenCategoryScene", OpenCategoryScene));
                CreateButton("Clothing", true).button.onClick.AddListener(OpenCategoryClothing);
                RegisterAction(new JSONStorableAction("OpenCategoryClothing", OpenCategoryClothing));
                CreateButton("Hair", true).button.onClick.AddListener(OpenCategoryHair);
                RegisterAction(new JSONStorableAction("OpenCategoryHair", OpenCategoryHair));
                CreateButton("Pose", true).button.onClick.AddListener(OpenCategoryPose);
                RegisterAction(new JSONStorableAction("OpenCategoryPose", OpenCategoryPose));

                CreateHeader("Plugin", true, Color.white);
                CreateButton("Person", true).button.onClick.AddListener(OpenPresetPerson);
                RegisterAction(new JSONStorableAction("OpenPresetPerson", OpenPresetPerson));
                CreateButton("Clothing", true).button.onClick.AddListener(OpenPresetClothing);
                RegisterAction(new JSONStorableAction("OpenPresetClothing", OpenPresetClothing));
                CreateButton("Hair", true).button.onClick.AddListener(OpenPresetHair);
                RegisterAction(new JSONStorableAction("OpenPresetHair", OpenPresetHair));
                CreateButton("Other", true).button.onClick.AddListener(OpenPresetOther);
                RegisterAction(new JSONStorableAction("OpenPresetOther", OpenPresetOther));

                CreateHeader("Misc", true, Color.white);
                CreateButton("AssetBundle", true).button.onClick.AddListener(OpenMiscCUA);
                RegisterAction(new JSONStorableAction("OpenMiscCUA", OpenMiscCUA));
                CreateButton("All", true).button.onClick.AddListener(OpenMiscAll);
                RegisterAction(new JSONStorableAction("OpenMiscAll", OpenMiscAll));
            }
            catch (Exception e)
            {
                SuperController.LogError("Exception caught: " + e);
            }
        }

        private void InvokeMsg(string msg)
        {
            if (Messager != null)
                Messager.SendMessage("Invoke", msg);
        }

        void Refresh() => InvokeMsg("Refresh");
        void RemoveInvalidVars() => InvokeMsg("RemoveInvalidVars");
        void UninstallAll() => InvokeMsg("UninstallAll");
        void OpenHubBrowse() => InvokeMsg("OpenHubBrowse");
        void OpenCustomScene() => InvokeMsg("OpenCustomScene");
        void OpenCustomSavedPerson() => InvokeMsg("OpenCustomSavedPerson");
        void OpenPersonPreset() => InvokeMsg("OpenPersonPreset");
        void OpenCategoryScene() => InvokeMsg("OpenCategoryScene");
        void OpenCategoryClothing() => InvokeMsg("OpenCategoryClothing");
        void OpenCategoryHair() => InvokeMsg("OpenCategoryHair");
        void OpenCategoryPose() => InvokeMsg("OpenCategoryPose");
        void OpenPresetPerson() => InvokeMsg("OpenPresetPerson");
        void OpenPresetClothing() => InvokeMsg("OpenPresetClothing");
        void OpenPresetHair() => InvokeMsg("OpenPresetHair");
        void OpenPresetOther() => InvokeMsg("OpenPresetOther");
        void OpenMiscCUA() => InvokeMsg("OpenMiscCUA");
        void OpenMiscAll() => InvokeMsg("OpenMiscAll");
    }
}
