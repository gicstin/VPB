using System;
using UnityEngine;

namespace VPB
{
    /// <summary>
    /// Creator Strip Scene keep buckets. Bitmask persisted in <c>VPBConfig.CreatorStripKeepMask</c>.
    /// CoreControl / CameraRig always kept silently. Environment always dropped (blank sky like new scene).
    /// </summary>
    [Flags]
    public enum CreatorStripKeepKind
    {
        None = 0,
        Persons = 1 << 0,
        Lights = 1 << 1,
        Sound = 1 << 2,
        Cameras = 1 << 3,
        Forces = 1 << 4,
        Animation = 1 << 5,
        Assets = 1 << 6,
        Toys = 1 << 7,
        Props = 1 << 8,
        UI = 1 << 9,
        SubScenes = 1 << 10,
        Other = 1 << 11,
    }

    public static class SceneUtils
    {
        /// <summary>Default keep set for Strip Scene selector: persons + lights.</summary>
        public const CreatorStripKeepKind CreatorStripKeepDefault =
            CreatorStripKeepKind.Persons | CreatorStripKeepKind.Lights;

        /// <summary>All user-toggleable keep kinds (excludes None).</summary>
        public const CreatorStripKeepKind CreatorStripKeepAllUser =
            CreatorStripKeepKind.Persons
            | CreatorStripKeepKind.Lights
            | CreatorStripKeepKind.Sound
            | CreatorStripKeepKind.Cameras
            | CreatorStripKeepKind.Forces
            | CreatorStripKeepKind.Animation
            | CreatorStripKeepKind.Assets
            | CreatorStripKeepKind.Toys
            | CreatorStripKeepKind.Props
            | CreatorStripKeepKind.UI
            | CreatorStripKeepKind.SubScenes
            | CreatorStripKeepKind.Other;

        /// <summary>Stable display order for selector rows (power-user scan).</summary>
        public static readonly CreatorStripKeepKind[] CreatorStripKeepDisplayOrder =
        {
            CreatorStripKeepKind.Persons,
            CreatorStripKeepKind.Lights,
            CreatorStripKeepKind.Sound,
            CreatorStripKeepKind.Cameras,
            CreatorStripKeepKind.Forces,
            CreatorStripKeepKind.Animation,
            CreatorStripKeepKind.Assets,
            CreatorStripKeepKind.Toys,
            CreatorStripKeepKind.Props,
            CreatorStripKeepKind.UI,
            CreatorStripKeepKind.SubScenes,
            CreatorStripKeepKind.Other,
        };

        /// <summary>
        /// VaM character atom types usable as gallery/clothing targets. <c>InvisiblePerson</c> is omitted
        /// when only <c>Person</c> is checked, which breaks the target picker for invisible characters.
        /// </summary>
        public static bool IsPersonLikeAtomType(string type)
        {
            if (string.IsNullOrEmpty(type)) return false;
            return type == "Person" || type == "InvisiblePerson";
        }

        public static bool IsPersonLikeAtom(Atom atom)
        {
            if (atom == null) return false;
            try { return IsPersonLikeAtomType(atom.type); }
            catch { return false; }
        }

        /// <summary>
        /// VaM sound/audio atom types (visible + invisible sources used for scene audio / VPB preview).
        /// </summary>
        public static bool IsSoundAtomType(string type)
        {
            if (string.IsNullOrEmpty(type)) return false;
            return type == "AudioSource" || type == "InvisibleAudioSource";
        }

        public static bool IsSoundAtom(Atom atom)
        {
            if (atom == null) return false;
            try { return IsSoundAtomType(atom.type); }
            catch { return false; }
        }

        public static bool IsLightAtomType(string type)
        {
            if (string.IsNullOrEmpty(type)) return false;
            return type == "InvisibleLight" || type == "Light";
        }

        public static bool IsLightAtom(Atom atom)
        {
            if (atom == null) return false;
            try { return IsLightAtomType(atom.type); }
            catch { return false; }
        }

        /// <summary>
        /// Scene-critical atoms that must never be stripped (same keep set as Remove Atom side list).
        /// Kept silently — not listed in Strip Scene UI.
        /// </summary>
        public static bool IsSystemProtectedAtom(Atom atom)
        {
            if (atom == null) return false;
            string uid = null;
            try { uid = atom.uid; } catch { }
            return IsSystemProtectedAtomId(uid);
        }

        public static bool IsSystemProtectedAtomId(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return true;
            if (uid.StartsWith("CoreControl", StringComparison.Ordinal)) return true;
            if (string.Equals(uid, "CameraRig", StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>
        /// VaM Environment atom (sky/sphere image) — always strip.
        /// Skyshop sphere on CoreControl blanked separately in keepers JSON / after load.
        /// </summary>
        public static bool IsCreatorStripAlwaysDropAtomType(string type)
        {
            if (string.IsNullOrEmpty(type)) return false;
            return string.Equals(type, "Environment", StringComparison.Ordinal);
        }

        public static bool IsCreatorStripAlwaysDropAtom(Atom atom)
        {
            if (atom == null) return false;
            string type = null;
            string uid = null;
            try { type = atom.type; } catch { type = null; }
            try { uid = atom.uid; } catch { uid = null; }
            if (IsCreatorStripAlwaysDropAtomType(type)) return true;
            // Some scenes name the sky atom Environment with a nonstandard type.
            if (!string.IsNullOrEmpty(uid)
                && string.Equals(uid, "Environment", StringComparison.Ordinal))
                return true;
            return false;
        }

        /// <summary>
        /// Map VaM atom <c>type</c> to Strip keep bucket. Unknown types → Other.
        /// Cold/warm path only — not for Update.
        /// </summary>
        public static CreatorStripKeepKind ClassifyCreatorStripKeepKind(string type)
        {
            if (string.IsNullOrEmpty(type)) return CreatorStripKeepKind.Other;

            if (IsPersonLikeAtomType(type)) return CreatorStripKeepKind.Persons;
            if (IsLightAtomType(type)) return CreatorStripKeepKind.Lights;
            if (IsSoundAtomType(type)) return CreatorStripKeepKind.Sound;

            if (type == "WindowCamera"
                || type == "ImageCapture"
                || type == "VRController")
                return CreatorStripKeepKind.Cameras;

            if (type == "CycleForce"
                || type == "SyncActionWithAudio"
                || type == "Rhythm"
                || type == "GrabPoint"
                || type == "RigidbodyMagnet"
                || type == "Cloth"
                || type == "SoftBody")
                return CreatorStripKeepKind.Forces;

            if (type == "AnimationPattern"
                || type == "AnimationStep"
                || type == "MotionAnimationMaster"
                || type == "Timeline"
                || type.StartsWith("Animation", StringComparison.Ordinal))
                return CreatorStripKeepKind.Animation;

            if (type == "CustomUnityAsset")
                return CreatorStripKeepKind.Assets;

            if (type == "Dildo"
                || type == "Toy"
                || type.StartsWith("Toy", StringComparison.Ordinal)
                || type.StartsWith("Dildo", StringComparison.Ordinal))
                return CreatorStripKeepKind.Toys;

            // Environment is always-dropped (blank new-scene look) — never a keep bucket.
            if (IsCreatorStripAlwaysDropAtomType(type))
                return CreatorStripKeepKind.None;

            if (type == "Empty"
                || type == "Cube"
                || type == "Sphere"
                || type == "Capsule"
                || type == "Cylinder"
                || type == "Plane"
                || type == "Mirror"
                || type == "Reflective"
                || type == "SimpleSign"
                || type == "Text"
                || type == "Poster"
                || type == "Glass"
                || type == "Table"
                || type == "Chair"
                || type == "Alcove"
                || type == "Door"
                || type == "Wall"
                || type == "Floor"
                || type == "Ceiling"
                || type == "Iray")
                return CreatorStripKeepKind.Props;

            if (type == "UIText"
                || type == "UIButton"
                || type == "UISlider"
                || type == "UIToggle"
                || type == "UIPanel"
                || type == "PlayerNavigationPanel"
                || type.StartsWith("UI", StringComparison.Ordinal))
                return CreatorStripKeepKind.UI;

            if (IsSubSceneAtomType(type))
                return CreatorStripKeepKind.SubScenes;

            // VaM catalog category on live atoms sometimes helps; type string alone is enough for JSON.
            return CreatorStripKeepKind.Other;
        }

        /// <summary>
        /// Triggers / Buttons subset — UI buttons/toggles/sliders + type names with Trigger.
        /// Used by Strip Keep view filter (recognition); still classified under UI/Other kinds.
        /// </summary>
        public static bool IsCreatorStripTriggerOrButtonType(string type)
        {
            if (string.Equals(type, "UIButton", StringComparison.Ordinal)
                || string.Equals(type, "UIToggle", StringComparison.Ordinal)
                || string.Equals(type, "UISlider", StringComparison.Ordinal)
                || string.Equals(type, "UIPanel", StringComparison.Ordinal)
                || string.Equals(type, "PlayerNavigationPanel", StringComparison.Ordinal))
                return true;
            if (string.IsNullOrEmpty(type)) return false;
            if (type.IndexOf("Trigger", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (type.IndexOf("Button", StringComparison.OrdinalIgnoreCase) >= 0
                && type.StartsWith("UI", StringComparison.Ordinal))
                return true;
            return false;
        }

        public static CreatorStripKeepKind ClassifyCreatorStripKeepKind(Atom atom)
        {
            if (atom == null) return CreatorStripKeepKind.Other;
            if (IsSystemProtectedAtom(atom)) return CreatorStripKeepKind.None;
            if (IsCreatorStripAlwaysDropAtom(atom)) return CreatorStripKeepKind.None;
            string type = null;
            try { type = atom.type; } catch { type = null; }
            return ClassifyCreatorStripKeepKind(type);
        }

        public static bool CreatorStripMaskContains(CreatorStripKeepKind mask, CreatorStripKeepKind kind)
        {
            if (kind == CreatorStripKeepKind.None) return false;
            return (mask & kind) != 0;
        }

        public static string CreatorStripKeepKindLabel(CreatorStripKeepKind kind)
        {
            switch (kind)
            {
                case CreatorStripKeepKind.Persons: return "Persons";
                case CreatorStripKeepKind.Lights: return "Lights";
                case CreatorStripKeepKind.Sound: return "Sound";
                case CreatorStripKeepKind.Cameras: return "Cameras";
                case CreatorStripKeepKind.Forces: return "Forces";
                case CreatorStripKeepKind.Animation: return "Animation";
                case CreatorStripKeepKind.Assets: return "Assets (CUA)";
                case CreatorStripKeepKind.Toys: return "Toys";
                case CreatorStripKeepKind.Props: return "Props";
                case CreatorStripKeepKind.UI: return "UI";
                case CreatorStripKeepKind.SubScenes: return "SubScenes";
                case CreatorStripKeepKind.Other: return "Other";
                default: return kind.ToString();
            }
        }

        public static bool IsSubSceneAtomType(string type)
        {
            return string.Equals(type, "SubScene", StringComparison.Ordinal);
        }

        public static bool IsSubSceneAtom(Atom atom)
        {
            if (atom == null) return false;
            try { return IsSubSceneAtomType(atom.type); }
            catch { return false; }
        }

        public static bool IsCustomUnityAssetAtomType(string type)
        {
            return string.Equals(type, "CustomUnityAsset", StringComparison.Ordinal);
        }

        /// <summary>Target type for the CUA category — assetbundle picks apply to these, not to Persons.</summary>
        public static bool IsCustomUnityAssetAtom(Atom atom)
        {
            if (atom == null) return false;
            try { return IsCustomUnityAssetAtomType(atom.type); }
            catch { return false; }
        }

        public static Atom DetectAtom(Vector2 screenPos, Camera cam, out string statusMsg)
        {
            RaycastHit hit;
            return RaycastAtom(screenPos, cam, out statusMsg, out hit);
        }

        public static Atom RaycastAtom(Vector2 screenPos, Camera cam, out string statusMsg, out RaycastHit hit)
        {
            statusMsg = "";
            hit = new RaycastHit();
            if (cam == null) return null;

            Ray ray = cam.ScreenPointToRay(screenPos);

            // Mask out UI layer (5) and Ignore Raycast (2)
            int layerMask = Physics.DefaultRaycastLayers & ~(1 << 5);

            if (Physics.Raycast(ray, out hit, 1000f, layerMask))
            {
                Atom atom = hit.collider.GetComponentInParent<Atom>();
                if (atom != null && IsPersonLikeAtom(atom))
                {
                    statusMsg = $"Target: {atom.name}";
                    return atom;
                }
                // Return the atom for drag-drop logic even if it's not a Person, but skip message processing
                return atom;
            }
            return null;
        }
    }
}
