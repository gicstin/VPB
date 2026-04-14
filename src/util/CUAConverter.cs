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
        public static JSONClass GetConvertedScene(string sceneJsonPath, bool onlyPersonAtoms = true)
        {
            var json = SuperController.singleton.LoadJSON(sceneJsonPath).AsObject;

            // Replace any existing references to SELF: with the source package
            // Must be done before clothing items are created since they may reference self
            var packageName = Regex.Match(sceneJsonPath, "([^/]*:)");
            if (packageName.Success)
            {
                LogUtil.Log($"Applying package name {packageName.Groups[1].Value}");
                var externalized = json.ToString().Replace("SELF:", packageName.Groups[1].Value);
                json = JSONClass.Parse(externalized).AsObject;
            }

            ConvertCUAToCUAClothingMutable(json);
            if (onlyPersonAtoms) json.RemoveNonPersonAtomsMutable();
            return json;
        }

        /// <summary>
        /// Convert CUAs linked to Person atoms to CUAClothing items
        /// </summary>
        /// <param name="sceneJson"></param>
        public static void ConvertCUAToCUAClothingMutable(JSONClass sceneJson)
        {
            var atoms = sceneJson["atoms"].AsArray;

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

            foreach (var cua in cuas)
            {
                string linkTo = cua.GetStorable("control")?["linkTo"];
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

                var boneTransform = GetBoneTransform(linkedPerson, linkedAtomBone);

                var personTransform = linkedPerson.GetTransform();
                var cuaTransform = cua.GetTransform();

                var finalOffset = boneTransform.InverseTransformPoint(cuaTransform);

                var clothingItem = new JSONClass();
                clothingItem["id"] = "Custom/Clothing/Female/VPB/CUA Clothing/CUA Clothing Dummy.vam";
                clothingItem["internalId"] = "VPB:CUA Clothing Dummy";
                clothingItem["enabled"] = "true";

                linkedPerson.GetStorable("geometry")["clothing"].AsArray.Add("dummy", clothingItem);

                linkedPerson["storables"].Add(BuildCUAClothingStorable(finalOffset, linkedAtomBone, cua));
                cua["on"] = "false";
            }
        }

        public static JSONClass BuildCUAClothingStorable(SimpleTransform offset, string parentBone, JSONClass cua)
        {
            var pluginManager = new JSONClass();
            // TODO: handle multiple items
            pluginManager["id"] = "VPB:CUA Clothing Dummy:plugin#0_Stopper.ClothingPluginManager";

            var pluginMap = new JSONClass();
            pluginMap["plugin#0"] = "Stopper.ClothingPluginManager.7:/Custom/Scripts/Stopper/ClothingPluginManager/ClothingPluginManager.cs";
            pluginMap["plugin#1"] = "Skynet.CUAClothingAlt.7:/Custom/Scripts/Skynet/CUAClothingAlt.cs";
            pluginManager["plugins"] = pluginMap;

            var pluginData = new JSONClass();

            pluginData["id"] = "plugin#1_Stopper.CUAClothing";
            pluginData["xOffset"] = offset.Position.x.ToString("F5");
            pluginData["yOffset"] = offset.Position.y.ToString("F5");
            pluginData["zOffset"] = offset.Position.z.ToString("F5");
            pluginData["xRotation"] = offset.Rotation.eulerAngles.x.ToString("F5");
            pluginData["yRotation"] = offset.Rotation.eulerAngles.y.ToString("F5");
            pluginData["zRotation"] = offset.Rotation.eulerAngles.z.ToString("F5");

            var asset = cua.GetStorable("asset");

            pluginData["assetName"] = asset["assetName"];
            pluginData["Asset"] = asset["assetName"];
            pluginData["assetUrl"] = asset["assetUrl"];
            pluginData["Parent Bone"] = parentBone;

            pluginManager["storables"] = new JSONArray() { pluginData };

            return pluginManager;
        }


        static readonly Dictionary<string, string> SKELETON = new Dictionary<string, string>()
        {
            { "hip", null },
            { "abdomen", "hip" },
            { "abdomen2", "abdomen" },
            { "chest", "abdomen2" },
            { "neck", "chest" },
            { "head", "neck" },
            { "rightEye", "head" },
            { "leftEye", "head" },

        };


        /// <summary>
        /// Returns a list of bone names starting at the given bone and ending at the root bone
        /// </summary>
        /// <param name="bone">The starting bone</param>
        /// <returns></returns>
        private static List<string> BonePathToRoot(string bone)
        {
            string currentBone = bone;
            List<string> path = new List<string>() { bone };
            var i = 0;
            while (SKELETON.TryGetValue(currentBone, out currentBone) && currentBone != null && i < 50)
            {
                path.Add(currentBone);
                i++;
            }
            return path;
        }


        private static SimpleTransform GetBoneTransform(JSONClass person, string offsetBone)
        {
            var boneOffset = new SimpleTransform();
            foreach (var bone in BonePathToRoot(offsetBone).Reverse<string>())
            {
                var storable = person.GetStorable(bone);
                if (storable == null)
                {
                    LogUtil.LogError($"Bone {bone} not found in person {person["id"]}");
                    continue;
                    // to continue... or to return? close-ish or just give up?
                    //return boneOffset;
                }
                var localTransform = storable.GetTransform();

                boneOffset = boneOffset.TransformPoint(localTransform);
            }
            return boneOffset;
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
