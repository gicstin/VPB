using Prime31.MessageKit;
using System.Runtime.InteropServices;
//using MVR.FileManagement;
using System;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using SimpleJSON;
using System.Text.RegularExpressions;
using System.Collections.Generic;

namespace VPB
{
    public class FileButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IEventSystemHandler
    {
        public void InitUI(uFileBrowser.FileButton ui)
        {
            this.button=ui.button;
            this.button.onClick.RemoveAllListeners();
            this.buttonImage=ui.buttonImage;
            this.fileIcon=ui.fileIcon;
            this.altIcon=ui.altIcon;
            this.label=ui.label;
            this.selectedSprite = ui.selectedSprite;
            this.renameButton=ui.renameButton;
            this.deleteButton= ui.deleteButton;
            this.hiddenToggle=ui.hiddenToggle;
            this.useFileAsTemplateToggle = ui.useFileAsTemplateToggle;
            this.fullPathLabel=ui.fullPathLabel;
            this.rectTransform=ui.rectTransform;
        }

        public Button button;
        public Image buttonImage;
        public Image fileIcon;
        public RawImage altIcon;
        public Text label;
        public Sprite selectedSprite;
        public Button renameButton;
        public Button deleteButton;
        public Toggle hiddenToggle;
        public Toggle useFileAsTemplateToggle;
        public Text fullPathLabel;
        public RectTransform rectTransform;

        //[HideInInspector]
        public string text;

        //[HideInInspector]
        public string textLowerInvariant;

        //[HideInInspector]
        public string fullPath;

        //[HideInInspector]
        public string removedPrefix;

        //[HideInInspector]
        public bool isDir;

        //[HideInInspector]
        public string imgPath;

        private FileBrowser browser;

        public void OnPointerEnter(PointerEventData eventData)
        {
            if ((bool)browser)
            {
                browser.OnFilePointerEnter(this);
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if ((bool)browser)
            {
                browser.OnFilePointerExit(this);
            }
        }

        public void Select()
        {
            button.transition = Selectable.Transition.None;
            buttonImage.overrideSprite = selectedSprite;
        }

        public void Unselect()
        {
            button.transition = Selectable.Transition.SpriteSwap;
            buttonImage.overrideSprite = null;
        }

        void InstallInBackground()
        {
            LogUtil.Log("InstallInBackground "+fullPath);
            if (browser != null)
            {
                if (browser.inGame)
                {
                    EnsureInstalled();
                }
                else
                {
                    try
                    {
                        // Some var packages have incomplete dependencies, so we need to ensure installation.
                        if (fullPath.EndsWith(".json"))
                        {
                            using (FileEntryStream fileEntryStream = FileManager.OpenStream(fullPath))
                            {
                                using (StreamReader streamReader = new StreamReader(fileEntryStream.Stream))
                                {
                                    string aJSON = streamReader.ReadToEnd();
                                    bool dirty = EnsureInstalledByText(aJSON);
                                    if (dirty)
                                    {
                                        MVR.FileManagement.FileManager.Refresh();
                                        VPB.FileManager.Refresh();
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        LogUtil.Log(e.ToString());
                    }
                    OnInstalled(true);
                }
            }
        }
        public void OnClick()
        {
            LogUtil.Log("OnClick "+this.fullPath);
            try
            {
                if (!string.IsNullOrEmpty(fullPath)
                    && fullPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                    && fullPath.Replace('\\', '/').IndexOf("/Saves/scene/", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    LogUtil.BeginSceneClick(fullPath);
                }
            }
            catch { }
            if (browser!=null)
            {
                if (browser.inGame)
                {
                    EnsureInstalled();
                    browser.OnFileClick(this);
                }
                else
                {
                    try
                    {
                        if (fullPath.EndsWith(".json"))
                        {
                            using (FileEntryStream fileEntryStream = FileManager.OpenStream(fullPath))
                            {
                                using (StreamReader streamReader = new StreamReader(fileEntryStream.Stream))
                                {
                                    string aJSON = streamReader.ReadToEnd();
                                    bool dirty = EnsureInstalledByText(aJSON);
                                    if (dirty)
                                    {
                                        MVR.FileManagement.FileManager.Refresh();
                                        VPB.FileManager.Refresh();
                                    }
                                }
                            }
                        }
                    }
                    catch(Exception e)
                    {
                        LogUtil.Log(e.ToString());
                    }
                    

                    OnInstalled(true);
                    browser.OnFileClick(this);
                }
            }
        }

        public void OnDeleteClick()
        {
            if ((bool)browser)
            {
                browser.OnDeleteClick(this);
            }
        }

        public void OnHiddenChange(bool b)
        {
            if ((bool)browser)
            {
                browser.OnHiddenChange(this, b);
            }
        }
        public void OnSetAutoInstall(bool b)
        {
            bool flag = false;
            FileEntry fileEntry = VPB.FileManager.GetFileEntry(fullPath, true);
            if (fileEntry != null && (fileEntry is VarFileEntry))
            {
                bool dirty=fileEntry.SetAutoInstall(b);
                if (dirty) flag = true;
            }
            else if (fileEntry != null && (fileEntry is SystemFileEntry))
            {
                bool dirty = fileEntry.SetAutoInstall(b);
                if (dirty) flag = true;
            }
            if (flag)
            {
                MVR.FileManagement.FileManager.Refresh();
                // Refresh this as well; this will raise events.
                VPB.FileManager.Refresh();
            }
        }
        public void EnsureInstalled()
        {
            string text = File.ReadAllText(fullPath);
            EnsureInstalledInternal(text);
        }
        public static bool EnsureInstalledByText(string text)
        {
            //Regex varInVapRegex = new Regex(@"""([^\\\/\:\*\?\""\<\>\.]+)\.([^\\\/\:\*\?\""\<\>\.]+)\.(\w+):");
            //var ms = varInVapRegex.Matches(text);
            //HashSet<string> set = new HashSet<string>();

            //foreach (Match item in ms)
            //{
            //    set.Add(string.Format("{0}.{1}.{2}", item.Groups[1], item.Groups[2], item.Groups[3]));
            //}
            var set= VarNameParser.Parse(text);
            return EnsureInstalledBySet(set);
        }

        public static bool EnsureInstalledBySet(HashSet<string> set)
        {
            if (set == null)
                return false;

            bool flag = false;
            foreach (var key in set)
            {
                VarPackage package = FileManager.GetPackage(key, false);
                if (package != null)
                {
                    string path = package.Path;
                    bool dirty = package.InstallRecursive();
                    if (dirty)
                    {
                        LogUtil.Log("Installed " + key + " path=" + path);
                        flag = true;
                    }
                }
                else
                {
                    if (!key.EndsWith(".latest"))
                    {
                        string newKey = key.Substring(0, key.LastIndexOf('.'))+ ".latest";
                        VarPackage packageNewest = FileManager.GetPackage(newKey, false);
                        if (packageNewest != null)
                        {
                            string pathLatest = packageNewest.Path;
                            bool dirty = packageNewest.InstallRecursive();
                            if (dirty)
                            {
                                LogUtil.Log("Installed " + newKey + " path=" + pathLatest + " (requested " + key + ")");
                                flag = true;
                            }
                        }
                        else
                        {
                            LogUtil.LogError("Install Failed (package not found) " + newKey);
                        }
                    }
                    else
                    {
                        LogUtil.LogError("Install Failed (package not found) " + key);
                    }
                }
            }
            if (flag)
                return true;
            return false;
        }

        public static void EnsureInstalledInternal(string text)
        {
            bool dirty=EnsureInstalledByText(text);
            if (dirty)
            {
                MVR.FileManagement.FileManager.Refresh();
                VPB.FileManager.Refresh();
            }
        }

        public void OnInstalled(bool b)
        {
            bool flag = false;
            FileEntry fileEntry = FileManager.GetFileEntry(fullPath, true);// Without the AllPackages prefix
            if (fileEntry != null && (fileEntry is VarFileEntry))
            {
                var entry = fileEntry as VarFileEntry;
                // Uninstall
                if (!b)
                {
                    bool dirty=entry.Package.UninstallSelf();
                    if (dirty)
                    {
                        LogUtil.Log("Uninstalled " + entry.Package.Uid + " path=" + entry.Package.Path);
                        flag = true;
                    }
                }
                else
                {
                    bool dirty = entry.Package.InstallRecursive();
                    if (dirty)
                    {
                        LogUtil.Log("Installed " + entry.Package.Uid + " path=" + entry.Package.Path);
                        flag = true;
                    }
                }
            }
            else if (fileEntry != null && (fileEntry is SystemFileEntry))
            {
                SystemFileEntry entry = fileEntry as SystemFileEntry;
                if (entry.isVar)
                {
                    if (!b)
                    {
                        bool dirty = entry.Uninstall();
                    if (dirty) 
                    {
                        LogUtil.Log("Uninstalled " + entry.Uid + " path=" + entry.Path);
                        flag = true;
                    }
                    }

                    else
                    {
                        bool dirty = entry.Install();
                        if (dirty) 
                        {
                            LogUtil.Log("Installed " + entry.Uid + " path=" + entry.Path);
                            flag = true;
                        }
                    }
                }
                else
                {
                    LogUtil.Log("impossible filebutton OnInstalled");
                }
            }
            if (flag)
            {
                MVR.FileManagement.FileManager.Refresh();
                // Refresh this as well; this will raise events.
                VPB.FileManager.Refresh();
            }
        }


        void OnEnable()
        {
            MessageKit.addObserver(MessageDef.FileManagerRefresh, OnFileManagerRefresh);
        }
        void OnDisable()
        {
            MessageKit.removeObserver(MessageDef.FileManagerRefresh, OnFileManagerRefresh);
        }
        void OnFileManagerRefresh()
        {
            RefreshInstallStatus();
        }
        public void Set(FileBrowser b, string txt, string path, bool dir, bool hidden, bool hiddenModifiable, bool isAutoInstall, bool allowUseFileAsTemplateSelect, bool isTemplate, bool isTemplateModifiable)
        {
            altIcon.texture = null;
            rectTransform = GetComponent<RectTransform>();
            browser = b;
            text = txt;
            textLowerInvariant = txt.ToLowerInvariant();
            fullPath = path;// This path is in the format uid:subPath
            isDir = dir;
            label.text = text;

            this.button.onClick.RemoveAllListeners();
            this.button.onClick.AddListener(OnClick);

            if (fullPathLabel != null)
            {
                fullPathLabel.text = fullPath;
            }
            if (isDir)
            {
                fileIcon.sprite = b.folderIcon;
            }
            else
            {
                fileIcon.sprite = b.GetFileIcon(txt);
            }

            if (hiddenToggle != null)
            {
                hiddenToggle.isOn = hidden;
                if (hiddenModifiable)
                {
                    hiddenToggle.interactable = true;
                    hiddenToggle.onValueChanged.RemoveAllListeners();
                    hiddenToggle.onValueChanged.AddListener(OnHiddenChange);
                }
                else
                {
                    hiddenToggle.interactable = false;
                }
            }
            //deleteButton.transform.Find("Text").GetComponent<Text>().text = "Install In Background";
            renameButton.transform.Find("Text").GetComponent<Text>().text = "Install In Background";
            var rt = renameButton.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(250,40);
            rt.anchoredPosition = new Vector2(-130, 130);

            useFileAsTemplateToggle.transform.Find("Label").GetComponent<Text>().text = "Auto Install";

            this.buttonImage.color = Color.white;
            if (browser.inGame)
            {
                deleteButton.gameObject.SetActive(false);
                //deleteButton.onClick.RemoveAllListeners();
                //deleteButton.onClick.AddListener(EnsureInstalled);

                renameButton.gameObject.SetActive(true);
                renameButton.onClick.RemoveAllListeners();
                renameButton.onClick.AddListener(InstallInBackground);

                hiddenToggle.gameObject.SetActive(false);

                useFileAsTemplateToggle.gameObject.SetActive(false);
            }
            else
            {
                deleteButton.gameObject.SetActive(false);//ensureInstallButton
                //deleteButton.onClick.RemoveAllListeners();
                //deleteButton.onClick.AddListener(EnsureInstalled);

                renameButton.gameObject.SetActive(true);
                renameButton.onClick.RemoveAllListeners();
                renameButton.onClick.AddListener(InstallInBackground);

                hiddenToggle.gameObject.SetActive(false);
                // Install
                useFileAsTemplateToggle.gameObject.SetActive(true);
                useFileAsTemplateToggle.onValueChanged.RemoveAllListeners();
                useFileAsTemplateToggle.isOn = isAutoInstall;
                useFileAsTemplateToggle.onValueChanged.AddListener(OnSetAutoInstall);

                RefreshInstallStatus();

            }
        }
        void RefreshInstallStatus()
        {
            useFileAsTemplateToggle.onValueChanged.RemoveAllListeners();
            bool isInstalled = false;
            bool isAutoInstall = false;
            FileEntry fileEntry = VPB.FileManager.GetFileEntry(fullPath, true);
            if (fileEntry != null && fileEntry is VarFileEntry)
            {
                isInstalled = fileEntry.IsInstalled();
                isAutoInstall = fileEntry.IsAutoInstall();
            }
            else if (fileEntry != null && fileEntry is SystemFileEntry)
            {
                isInstalled = fileEntry.IsInstalled();
                isAutoInstall = fileEntry.IsAutoInstall();
            }
            else
            {
                // After plugin installation, the path changes
                if (fullPath.StartsWith("AllPackages"))
                {
                    fullPath = "AddonPackages" + fullPath.Substring("AllPackages".Length);
                }
                else if (fullPath.StartsWith("AddonPackages"))
                {
                    fullPath = "AllPackages" + fullPath.Substring("AddonPackages".Length);
                }
                fileEntry = VPB.FileManager.GetFileEntry(fullPath, true);
                if (fileEntry != null)
                {
                    isInstalled = fileEntry.IsInstalled();
                    isAutoInstall = fileEntry.IsAutoInstall();
                }
                else
                {
                    isInstalled = false;
                }
            }

            useFileAsTemplateToggle.isOn = isAutoInstall;
            useFileAsTemplateToggle.onValueChanged.AddListener(OnSetAutoInstall);

            UpdateButtonImageColor(isInstalled, isAutoInstall);
        }
        void UpdateButtonImageColor(bool isInstalled, bool isAutoInstall)
        {
            if (isAutoInstall)
            {
                this.buttonImage.color = new Color32(255, 150, 0, 255);
                return;
            }
            if (isInstalled)
            {
                this.buttonImage.color = new Color32(120, 220, 255, 255);
                return;
            }
            this.buttonImage.color = Color.white;
        }
    }
}
