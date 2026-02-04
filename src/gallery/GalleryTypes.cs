using System;
using System.Collections.Generic;
using MeshVR;

namespace VPB
{
    public enum ContentType { Category, Creator, License, Tags, Hub, HubTags, HubPayTypes, HubCreators, Ratings, Size, SceneSource, AppearanceSource }
    public enum ApplyMode { SingleClick, DoubleClick }
    public enum TabSide { Hidden, Left, Right }
    public enum GalleryLayoutMode { Grid, VerticalCard }
    
    public struct CreatorCacheEntry 
    { 
        public string Name; 
        public int Count; 
    }

    public class PresetLockStore
    {
        public bool _generalPresetLock;
        public bool _appPresetLock;
        public bool _posePresetLock;
        public bool _animationPresetLock;
        public bool _glutePhysPresetLock;
        public bool _breastPhysPresetLock;
        public bool _pluginPresetLock;
        public bool _skinPresetLock;
        public bool _morphPresetLock;
        public bool _hairPresetLock;
        public bool _clothingPresetLock;

        public void StorePresetLocks(Atom atom, bool clearAllLocks = false, bool lockClothingPreset = false, bool lockMorphPreset = false)
        {
            if (atom == null || atom.presetManagerControls == null) return;
            
            List<PresetManagerControl> pmControlList = atom.presetManagerControls;
            foreach (PresetManagerControl pmc in pmControlList)
            {
                if (pmc.name == "geometry") _generalPresetLock = pmc.lockParams;
                else if (pmc.name == "AppearancePresets") _appPresetLock = pmc.lockParams;
                else if (pmc.name == "PosePresets") _posePresetLock = pmc.lockParams;
                else if (pmc.name == "AnimationPresets") _animationPresetLock = pmc.lockParams;
                else if (pmc.name == "FemaleGlutePhysicsPresets") _glutePhysPresetLock = pmc.lockParams;
                else if (pmc.name == "FemaleBreastPhysicsPresets") _breastPhysPresetLock = pmc.lockParams;
                else if (pmc.name == "PluginPresets") _pluginPresetLock = pmc.lockParams;
                else if (pmc.name == "SkinPresets") _skinPresetLock = pmc.lockParams;
                else if (pmc.name == "MorphPresets") _morphPresetLock = pmc.lockParams;
                else if (pmc.name == "HairPresets") _hairPresetLock = pmc.lockParams;
                else if (pmc.name == "ClothingPresets") _clothingPresetLock = pmc.lockParams;

                if (pmc.name == "ClothingPresets" && lockClothingPreset) pmc.lockParams = true;
                else if (pmc.name == "MorphPresets" && lockMorphPreset) pmc.lockParams = true;
                else if (clearAllLocks) pmc.lockParams = false;
            }
        }

        public void RestorePresetLocks(Atom atom)
        {
            if (atom == null || atom.presetManagerControls == null) return;

            List<PresetManagerControl> pmControlList = atom.presetManagerControls;
            foreach (PresetManagerControl pmc in pmControlList)
            {
                if (pmc.name == "geometry") pmc.lockParams = _generalPresetLock;
                else if (pmc.name == "AppearancePresets") pmc.lockParams = _appPresetLock;
                else if (pmc.name == "PosePresets") pmc.lockParams = _posePresetLock;
                else if (pmc.name == "AnimationPresets") pmc.lockParams = _animationPresetLock;
                else if (pmc.name == "FemaleGlutePhysicsPresets") pmc.lockParams = _glutePhysPresetLock;
                else if (pmc.name == "FemaleBreastPhysicsPresets") pmc.lockParams = _breastPhysPresetLock;
                else if (pmc.name == "PluginPresets") pmc.lockParams = _pluginPresetLock;
                else if (pmc.name == "SkinPresets") pmc.lockParams = _skinPresetLock;
                else if (pmc.name == "MorphPresets") pmc.lockParams = _morphPresetLock;
                else if (pmc.name == "HairPresets") pmc.lockParams = _hairPresetLock;
                else if (pmc.name == "ClothingPresets") pmc.lockParams = _clothingPresetLock;
            }
        }
    }
}
