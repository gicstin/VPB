using HarmonyLib;
using Leap.Unity;
using Leap.Unity.Infix;
using MVR.FileManagement;
using SimpleJSON;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace VPB.src.util
{
    public class CUAConverter
    {
        // DEAD CODE (+ ConvertCUAToCUAClothingMutable below): CUA-to-clothing has no live caller; CUA import is native atoms.
        /// <param name="fileEntry">When set, <see cref="VPB.UI.LoadJSONWithFallback"/> can read the JSON from the .var via VPB when native LoadJSON fails.</param>
        public static JSONClass GetConvertedScene(string sceneJsonPath, bool onlyPersonAtoms = true, global::VPB.FileEntry fileEntry = null)
        {
            if (SuperController.singleton == null)
                throw new InvalidOperationException("GetConvertedScene: SuperController.singleton is null.");

            // Do not run FileManager.NormalizePath here: for virtual refs it can substitute paths in ways that break
            // lookups for entries with '!', parentheses, or spaces; slashes are enough for LoadJSONWithFallback.
            string loadPath = string.IsNullOrEmpty(sceneJsonPath) ? sceneJsonPath : sceneJsonPath.Replace('\\', '/');

            JSONNode loaded = VPB.UI.LoadJSONWithFallback(loadPath ?? sceneJsonPath, fileEntry);
            if (loaded == null)
            {
                LogUtil.LogError($"[VPB] GetConvertedScene: could not load JSON for '{sceneJsonPath}'. Use virtual path form packageUid:/pathInsideVar (note colon+slash); see VPB.UI.LoadJSONWithFallback.");
                throw new InvalidOperationException("Scene JSON could not be loaded: " + sceneJsonPath);
            }

            var json = loaded.AsObject;
            if (json == null)
            {
                LogUtil.LogError($"[VPB] GetConvertedScene: JSON is not an object for '{sceneJsonPath}'.");
                throw new InvalidOperationException("Scene JSON root is not an object: " + sceneJsonPath);
            }

            var packageNameRegex = Regex.Match(sceneJsonPath, "^([^:]+)");
            string packageName = packageNameRegex.Success ? packageNameRegex.Groups[1].Value : null;

            // Replace any existing references to SELF: with the source package
            // Must be done before clothing items are created since they may reference self
            if (packageName != null)
            {
                LogUtil.Log($"Applying package name {packageName}");
                // In-place tree walk — never json.ToString()+Parse on multi‑MB mocap/timeline scenes (Mono heap blow‑ups).
                JSONExtensions.ReplaceSelfPrefixWithPackageUidMutable(json, packageName);
            }

            ConvertCUAToCUAClothingMutable(json, packageName);

            if (onlyPersonAtoms) json.RemoveNonPersonAtomsMutable();

            return json;
        }

        // DEAD CODE: no live caller (see GetConvertedScene above). forcedGender chose Female/Male over the unscanned Neutral folder.
        public static void ConvertCUAToCUAClothingMutable(JSONClass sceneJson, string sourcePackageId = null, string forcedGender = null)
        {
            if (sceneJson == null) return;

            JSONNode atomsRoot = sceneJson["atoms"];
            JSONArray atoms = atomsRoot != null ? atomsRoot.AsArray : null;
            if (atoms == null)
            {
                LogUtil.LogWarning("[VPB] ConvertCUAToCUAClothingMutable: scene has no atoms array — skipping CUA conversion.");
                return;
            }

            var persons = new List<JSONClass>();
            var cuas = new List<JSONClass>();

            foreach (JSONClass atom in atoms)
            {
                string id = atom["id"];
                string type = atom["type"];

                if (type == "Person")
                {
                    persons.Add(atom);
                }
                else if (type == "CustomUnityAsset")
                {
                    cuas.Add(atom);
                }
            }

            LogUtil.Log($"[VPB][CUA] convert start: {persons.Count} person(s), {cuas.Count} CUA(s); sourcePkg={sourcePackageId ?? "(none)"}");
            int converted = 0;
            int i = 0;
            foreach (var cua in cuas)
            {
              // Isolate each CUA: a malformed one (missing asset, unmapped bone, null transform) must not abort the
              // whole conversion, the rest still convert and the import proceeds with whatever succeeded.
              try
              {
                string linkTo = cua.GetStorable("control")?["linkTo"];
                LogUtil.Log($"[VPB][CUA] {cua.GetId()}: linkTo={linkTo ?? "(null)"}");
                if (linkTo == null) continue;

                var parts = linkTo.Split(':');

                if (parts.Length < 2)
                {
                    LogUtil.LogWarning($"Invalid linkTo format for {cua["id"]}: {linkTo}");
                    continue;
                }

                string linkedAtomId = parts[0];
                string linkedAtomBone = parts[1];

                var linkedPerson = persons.FirstOrDefault(p => p.GetId() == linkedAtomId);
                if (linkedPerson == null) continue;

                if (!TryGetBoneTransform(linkedPerson, linkedAtomBone, out SimpleTransform boneTransform))
                {
                    LogUtil.LogWarning($"[VPB] CUA {cua.GetId()}: could not resolve link bone '{linkedAtomBone}' — skipping clothing conversion for this CUA.");
                    continue;
                }

                var personTransform = linkedPerson.GetTransform();
                var cuaTransform = cua.GetTransform();

                var finalOffset = boneTransform.InverseTransformPoint(cuaTransform);

                JSONClass geom = linkedPerson.GetStorable("geometry");
                if (geom == null)
                {
                    LogUtil.LogWarning($"[VPB] CUA {cua.GetId()}: Person has no geometry storable.");
                    continue;
                }

                JSONNode charNode = geom["character"];
                if (charNode == null || string.IsNullOrEmpty(charNode.Value))
                {
                    LogUtil.LogWarning($"[VPB] CUA {cua.GetId()}: geometry.character missing.");
                    continue;
                }

                string gender = !string.IsNullOrEmpty(forcedGender)
                    ? forcedGender
                    : JSONExtensions.GetGenderForCharacter(charNode.Value);

                var needRefresh = CUAClothing.CreateAndSaveCUAClothing(out JSONClass clothingItem, cua, i, sourcePackageId, gender);

                if (geom["clothing"] == null)
                    geom["clothing"] = new JSONArray();
                JSONArray clothingArr = geom["clothing"].AsArray;
                if (clothingArr == null)
                {
                    LogUtil.LogWarning($"[VPB] CUA {cua.GetId()}: geometry.clothing is not an array.");
                    continue;
                }
                clothingArr.Add("dummy", clothingItem);

                if (linkedPerson["storables"] == null)
                    linkedPerson["storables"] = new JSONArray();
                linkedPerson["storables"].AsArray.Add(CUAClothing.BuildCUAClothingStorable(cua, finalOffset, linkedAtomBone, clothingItem["internalId"]));

                LogUtil.Log($"[VPB][CUA] {cua.GetId()}: converted to clothing '{(clothingItem["internalId"] != null ? clothingItem["internalId"].Value : "?")}' on {linkedAtomId} bone {linkedAtomBone} (created={needRefresh}).");
                converted++;
                i++;
              }
              catch (Exception ex)
              {
                LogUtil.LogWarning($"[VPB][CUA] {cua.GetId()}: conversion failed, skipping this CUA: {ex.Message}");
              }
            }
            LogUtil.Log($"[VPB][CUA] convert done: {converted}/{cuas.Count} CUA(s) converted to clothing.");
        }

        public class BoneMeta
        {
            public string ParentName;
            public bool Symmetric;

            public BoneMeta(string parentName, bool symmetric)
            {
                this.ParentName = parentName;
                this.Symmetric = symmetric;
            }
        }

        public static readonly Dictionary<string, BoneMeta> SKELETON = new Dictionary<string, BoneMeta>()
        {
            { "hip", new BoneMeta(null, false) },
                { "pelvis", new BoneMeta("hip", false) },
                    { "Thigh", new BoneMeta("pelvis", true) },
                        { "Shin", new BoneMeta("Thigh", true) },
                            { "Foot", new BoneMeta("Shin", true) },
                                { "Toe", new BoneMeta("Foot", true) },
                                    { "BigToe", new BoneMeta("Toe", true) },
                                    { "SmallToe1", new BoneMeta("Toe", true) },
                                    { "SmallToe2", new BoneMeta("Toe", true) },
                                    { "SmallToe3", new BoneMeta("Toe", true) },
                                    { "SmallToe4", new BoneMeta("Toe", true) },
                { "abdomen", new BoneMeta("hip", false) },
                    { "abdomen2", new BoneMeta("abdomen", false) },
                        { "chest", new BoneMeta("abdomen2", false) },
                            { "neck", new BoneMeta("chest", false) },
                                { "head", new BoneMeta("neck", false) },
                                    { "Eye", new BoneMeta("head", true) },
                            { "Collar", new BoneMeta("chest", true) },
                                { "Shldr", new BoneMeta("Collar", true) },
                                    { "ForeArm", new BoneMeta("Shldr", true) },
                                        { "Hand", new BoneMeta("ForeArm", true) },
                                            { "Thumb1", new BoneMeta("Hand", true) },
                                                { "Thumb2", new BoneMeta("Thumb1", true) },
                                                    { "Thumb3", new BoneMeta("Thumb2", true) },
                                            { "Carpal1", new BoneMeta("Hand", true) },
                                                { "Index1", new BoneMeta("Carpal1", true) },
                                                    { "Index2", new BoneMeta("Index1", true) },
                                                        { "Index3", new BoneMeta("Index2", true) },
                                                { "Mid1", new BoneMeta("Carpal1", true) },
                                                    { "Mid2", new BoneMeta("Mid1", true) },
                                                        { "Mid3", new BoneMeta("Mid2", true) },
                                            { "Carpal2", new BoneMeta("Hand", true) },
                                                { "Ring1", new BoneMeta("Carpal1", true) },
                                                    { "Ring2", new BoneMeta("Ring1", true) },
                                                        { "Ring3", new BoneMeta("Ring2", true) },
                                                { "Pinky1", new BoneMeta("Carpal1", true) },
                                                    { "Pinky2", new BoneMeta("Pinky1", true) },
                                                        { "Pinky3", new BoneMeta("Pinky2", true) },
                            { "Pectoral", new BoneMeta("chest", true) },
        };

        /// <summary>Resolves <see cref="BoneMeta"/> for a person bone name; returns null if unknown (e.g. extra mocap/rig bones not in <see cref="SKELETON"/>).</summary>
        private static BoneMeta GetStartBoneMeta(string bone, out string side, out string name)
        {
            var regex = Regex.Match(bone, "^((?<side>[lr])(?<name>[A-Z].*))|(?<name>.*)");
            if (!regex.Success)
            {
                side = null;
                name = null;
                return null;
            }

            side = regex.Groups["side"].Value;
            if (side == string.Empty) side = null;
            name = regex.Groups["name"].Value;

            if (SKELETON.TryGetValue(name, out BoneMeta meta))
                return meta;

            foreach (var kvp in SKELETON)
            {
                if (string.Equals(kvp.Key, name, StringComparison.OrdinalIgnoreCase))
                    return kvp.Value;
            }

            return null;
        }


        /// <summary>
        /// Returns a list of bone names starting at the given bone and ending at the root bone
        /// </summary>
        /// <param name="startBoneName">The starting bone</param>
        /// <returns></returns>
        public static List<string> BonePathToRoot(string startBoneName)
        {
            BoneMeta currentMeta = GetStartBoneMeta(startBoneName, out string side, out string currentName);
            if (currentMeta == null) {
                return null;
            };
            List<string> path = new List<string>();
            for (var i = 0; currentMeta != null && i < 50; i++)
            {
                path.Add((currentMeta.Symmetric ? side : "") + currentName);
                if (currentMeta.ParentName == null) break;
                currentName = currentMeta.ParentName;
                SKELETON.TryGetValue(currentMeta.ParentName, out currentMeta);
            }
            if (path.Count == 0)
            {
                LogUtil.LogError("Failed to trace skeleton from " + startBoneName + "! (invalid bone name)");
                return null;
            }
            if (path.Last() != "hip")
            {
                LogUtil.LogError("Failed to trace skeleton from " + startBoneName + "! (no path to root bone)");
            }
            return path;
        }


        // Bone-relative offset of a source CUA control: lets a re-spawned CUA atom sit at the same place on a target
        // person of a different scale/pose. Reuses the same bone-walk transform math the clothing conversion used.
        public static bool TryComputeCuaBoneOffset(JSONClass sourcePerson, SimpleTransform cuaWorld, string bone, out SimpleTransform offset)
        {
            offset = new SimpleTransform();
            if (sourcePerson == null || cuaWorld == null || string.IsNullOrEmpty(bone)) return false;
            if (!TryGetBoneTransform(sourcePerson, bone, out SimpleTransform boneRelToRoot)) return false;
            // Compose the source person root so the bone is in world space; else a non-origin person offsets the CUA.
            SimpleTransform personRoot = new SimpleTransform();
            JSONClass control = sourcePerson.GetStorable("control");
            if (control != null) personRoot = control.GetTransform();
            SimpleTransform boneWorld = personRoot.TransformPoint(boneRelToRoot);
            offset = boneWorld.InverseTransformPoint(cuaWorld);
            return true;
        }

        private static bool TryGetBoneTransform(JSONClass person, string offsetBone, out SimpleTransform boneOffset)
        {
            boneOffset = new SimpleTransform();
            var path = BonePathToRoot(offsetBone);
            if (path == null)
                return false;

            foreach (var bone in Enumerable.Reverse(path))
            {
                var storable = person.GetStorable(bone);
                if (storable == null)
                {
                    LogUtil.LogError($"Bone {bone} not found in person {person["id"]}");
                    continue;
                }
                var localTransform = storable.GetTransform();
                boneOffset = boneOffset.TransformPoint(localTransform);
            }
            return true;
        }
    }

    public class SimpleTransform
    {
        public Vector3 Position;
        public Quaternion Rotation;

        public SimpleTransform()
        {
            Position = new Vector3();
            Rotation = Quaternion.identity;
        }

        public SimpleTransform(Vector3 position, Quaternion rotation)
        {
            this.Position = position;
            this.Rotation = rotation;
        }

        /// <summary>
        /// Convert a transform from local space to world space
        /// </summary>
        /// <param name="transform"></param>
        public SimpleTransform TransformPoint(SimpleTransform transform)
        {
            return new SimpleTransform(Position + transform.Position.RotatedBy(Rotation), Rotation * transform.Rotation);
        }

        public SimpleTransform InverseTransformPoint(SimpleTransform worldTransform)
        {
            Quaternion invRot = Quaternion.Inverse(Rotation);
            Vector3 localPos = invRot * (worldTransform.Position - Position);
            Quaternion localRot = invRot * worldTransform.Rotation;

            return new SimpleTransform(localPos, localRot);
        }

        public override string ToString()
        {
            return $"{Position.ToString("F5")}{Rotation.eulerAngles.ToString("F5").Replace("(", "[").Replace(")", "]")}";
        }

        /// <summary>
        /// Create a Transform from the "position" and "rotation" fields of a JSONClass
        /// </summary>
        /// <param name="json">The JSON holding the transform data</param>
        public static SimpleTransform FromJson(JSONClass json, string positionKey = "position", string rotationKey = "rotation")
        {
            if (!json.HasKey(positionKey) || !json.HasKey(rotationKey))
            {
                LogUtil.LogError($"Invalid transform JSON: {json}");
                return new SimpleTransform();
            }

            JSONClass positionJson = json[positionKey].AsObject;
            JSONClass rotationJson = json[rotationKey].AsObject;

            // Will return 0.0f if the keys are missing
            Vector3 position = new Vector3(positionJson["x"].AsFloat, positionJson["y"].AsFloat, positionJson["z"].AsFloat);
            Quaternion rotation = Quaternion.Euler(rotationJson["x"].AsFloat, rotationJson["y"].AsFloat, rotationJson["z"].AsFloat);

            return new SimpleTransform(position, rotation);
        }
    }
}
