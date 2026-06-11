using Leap.Unity;
using Leap.Unity.Infix;
using SimpleJSON;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace VPB.src.util
{
    public class CUAConverter
    {
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

        public static List<string> BonePathToRoot(string startBoneName)
        {
            BoneMeta currentMeta = GetStartBoneMeta(startBoneName, out string side, out string currentName);
            if (currentMeta == null)
            {
                return null;
            }
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

        public static bool TryComputeCuaBoneOffset(JSONClass sourcePerson, SimpleTransform cuaWorld, string bone, out SimpleTransform offset)
        {
            offset = new SimpleTransform();
            if (sourcePerson == null || cuaWorld == null || string.IsNullOrEmpty(bone)) return false;
            if (!TryGetBoneTransform(sourcePerson, bone, out SimpleTransform boneRelToRoot)) return false;
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

        public static SimpleTransform FromJson(JSONClass json, string positionKey = "position", string rotationKey = "rotation")
        {
            if (!json.HasKey(positionKey) || !json.HasKey(rotationKey))
            {
                LogUtil.LogError($"Invalid transform JSON: {json}");
                return new SimpleTransform();
            }

            JSONClass positionJson = json[positionKey].AsObject;
            JSONClass rotationJson = json[rotationKey].AsObject;

            Vector3 position = new Vector3(positionJson["x"].AsFloat, positionJson["y"].AsFloat, positionJson["z"].AsFloat);
            Quaternion rotation = Quaternion.Euler(rotationJson["x"].AsFloat, rotationJson["y"].AsFloat, rotationJson["z"].AsFloat);

            return new SimpleTransform(position, rotation);
        }
    }
}
