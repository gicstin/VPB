using UnityEngine;

namespace VPB
{
    public partial class VamHookPlugin
    {
        private class SettingsDraftState
        {
            public bool ShowSettings;
            public string UiKeyDraft;
            public string GalleryKeyDraft;
            public string CreateGalleryKeyDraft;
            public string HubKeyDraft;
            public string ClearConsoleKeyDraft;
            public bool PluginsAlwaysEnabledDraft;
            public bool LoadDependenciesWithPackageDraft;
            public bool IsDevModeDraft;
            public bool EnableUiTransparencyDraft;
            public float UiTransparencyValueDraft;
            public string Error;
        }

        private class QuickMenuPositionState
        {
            public bool ShowWindow;
            public Rect WindowRect = new Rect(300, 200, 520, 320);
            public Vector2 OriginalCreate;
            public Vector2 OriginalShowHide;
            public float CreateX;
            public float CreateY;
            public float ShowHideX;
            public float ShowHideY;
            public float CreateXVR;
            public float CreateYVR;
            public float ShowHideXVR;
            public float ShowHideYVR;
            public string CreateXText;
            public string CreateYText;
            public string ShowHideXText;
            public string ShowHideYText;
            public string CreateXVRText;
            public string CreateYVRText;
            public string ShowHideXVRText;
            public string ShowHideYVRText;
            public bool UseSameCreateInVR;
            public bool UseSameShowHideInVR;
        }

        private readonly SettingsDraftState m_SettingsDraft = new SettingsDraftState();
        private readonly QuickMenuPositionState m_QuickMenuPos = new QuickMenuPositionState();

        private bool m_ShowSettings { get => m_SettingsDraft.ShowSettings; set => m_SettingsDraft.ShowSettings = value; }
        private string m_SettingsUiKeyDraft { get => m_SettingsDraft.UiKeyDraft; set => m_SettingsDraft.UiKeyDraft = value; }
        private string m_SettingsGalleryKeyDraft { get => m_SettingsDraft.GalleryKeyDraft; set => m_SettingsDraft.GalleryKeyDraft = value; }
        private string m_SettingsCreateGalleryKeyDraft { get => m_SettingsDraft.CreateGalleryKeyDraft; set => m_SettingsDraft.CreateGalleryKeyDraft = value; }
        private string m_SettingsHubKeyDraft { get => m_SettingsDraft.HubKeyDraft; set => m_SettingsDraft.HubKeyDraft = value; }
        private string m_SettingsClearConsoleKeyDraft { get => m_SettingsDraft.ClearConsoleKeyDraft; set => m_SettingsDraft.ClearConsoleKeyDraft = value; }
        private bool m_SettingsPluginsAlwaysEnabledDraft { get => m_SettingsDraft.PluginsAlwaysEnabledDraft; set => m_SettingsDraft.PluginsAlwaysEnabledDraft = value; }
        private bool m_SettingsLoadDependenciesWithPackageDraft { get => m_SettingsDraft.LoadDependenciesWithPackageDraft; set => m_SettingsDraft.LoadDependenciesWithPackageDraft = value; }
        private bool m_SettingsIsDevModeDraft { get => m_SettingsDraft.IsDevModeDraft; set => m_SettingsDraft.IsDevModeDraft = value; }
        private bool m_SettingsEnableUiTransparencyDraft { get => m_SettingsDraft.EnableUiTransparencyDraft; set => m_SettingsDraft.EnableUiTransparencyDraft = value; }
        private float m_SettingsUiTransparencyValueDraft { get => m_SettingsDraft.UiTransparencyValueDraft; set => m_SettingsDraft.UiTransparencyValueDraft = value; }
        private string m_SettingsError { get => m_SettingsDraft.Error; set => m_SettingsDraft.Error = value; }

        private bool m_ShowQuickMenuPosWindow { get => m_QuickMenuPos.ShowWindow; set => m_QuickMenuPos.ShowWindow = value; }
        private Rect m_QuickMenuPosWindowRect { get => m_QuickMenuPos.WindowRect; set => m_QuickMenuPos.WindowRect = value; }
        private Vector2 m_QuickMenuPosOriginalCreate { get => m_QuickMenuPos.OriginalCreate; set => m_QuickMenuPos.OriginalCreate = value; }
        private Vector2 m_QuickMenuPosOriginalShowHide { get => m_QuickMenuPos.OriginalShowHide; set => m_QuickMenuPos.OriginalShowHide = value; }
        private float m_QuickMenuPosCreateX { get => m_QuickMenuPos.CreateX; set => m_QuickMenuPos.CreateX = value; }
        private float m_QuickMenuPosCreateY { get => m_QuickMenuPos.CreateY; set => m_QuickMenuPos.CreateY = value; }
        private float m_QuickMenuPosShowHideX { get => m_QuickMenuPos.ShowHideX; set => m_QuickMenuPos.ShowHideX = value; }
        private float m_QuickMenuPosShowHideY { get => m_QuickMenuPos.ShowHideY; set => m_QuickMenuPos.ShowHideY = value; }
        private float m_QuickMenuPosCreateXVR { get => m_QuickMenuPos.CreateXVR; set => m_QuickMenuPos.CreateXVR = value; }
        private float m_QuickMenuPosCreateYVR { get => m_QuickMenuPos.CreateYVR; set => m_QuickMenuPos.CreateYVR = value; }
        private float m_QuickMenuPosShowHideXVR { get => m_QuickMenuPos.ShowHideXVR; set => m_QuickMenuPos.ShowHideXVR = value; }
        private float m_QuickMenuPosShowHideYVR { get => m_QuickMenuPos.ShowHideYVR; set => m_QuickMenuPos.ShowHideYVR = value; }
        private string m_QuickMenuPosCreateXText { get => m_QuickMenuPos.CreateXText; set => m_QuickMenuPos.CreateXText = value; }
        private string m_QuickMenuPosCreateYText { get => m_QuickMenuPos.CreateYText; set => m_QuickMenuPos.CreateYText = value; }
        private string m_QuickMenuPosShowHideXText { get => m_QuickMenuPos.ShowHideXText; set => m_QuickMenuPos.ShowHideXText = value; }
        private string m_QuickMenuPosShowHideYText { get => m_QuickMenuPos.ShowHideYText; set => m_QuickMenuPos.ShowHideYText = value; }
        private string m_QuickMenuPosCreateXVRText { get => m_QuickMenuPos.CreateXVRText; set => m_QuickMenuPos.CreateXVRText = value; }
        private string m_QuickMenuPosCreateYVRText { get => m_QuickMenuPos.CreateYVRText; set => m_QuickMenuPos.CreateYVRText = value; }
        private string m_QuickMenuPosShowHideXVRText { get => m_QuickMenuPos.ShowHideXVRText; set => m_QuickMenuPos.ShowHideXVRText = value; }
        private string m_QuickMenuPosShowHideYVRText { get => m_QuickMenuPos.ShowHideYVRText; set => m_QuickMenuPos.ShowHideYVRText = value; }
        private bool m_QuickMenuPosUseSameCreateInVR { get => m_QuickMenuPos.UseSameCreateInVR; set => m_QuickMenuPos.UseSameCreateInVR = value; }
        private bool m_QuickMenuPosUseSameShowHideInVR { get => m_QuickMenuPos.UseSameShowHideInVR; set => m_QuickMenuPos.UseSameShowHideInVR = value; }
    }
}
