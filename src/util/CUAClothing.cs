using SimpleJSON;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace VPB.src.util
{
    // DEAD CODE: only ConvertCUAToCUAClothingMutable (itself dead) called this. CUA import is native atoms now.
    public partial class CUAClothing
    {
        /// <summary>
        /// Create and save clothing items to `Custom/Clothing`
        /// </summary>
        /// <param name="clothingItem">Storable of `id` (path name) and `internalId` (unique ID)</param>
        /// <param name="cua">The CUA this clothing is based on</param>
        /// <param name="index"></param>
        /// <param name="packageName"></param>
        /// <param name="gender"></param>
        /// <param name="replaceExisting"></param>
        /// <returns>True if a clothing item was created, False if one already exists</returns>
        public static bool CreateAndSaveCUAClothing(out JSONClass clothingItem, JSONClass cua, int index, string packageName = "Local", string gender = "Neutral", bool replaceExisting = false)
        {
            var cuaId = cua.GetId();
            var assetStorable = cua.GetStorable("asset");
            string assetUrl = (assetStorable != null && assetStorable["assetUrl"] != null) ? assetStorable["assetUrl"].Value : null;
            if (string.IsNullOrEmpty(assetUrl))
                throw new InvalidOperationException($"CUA {cuaId} has no asset/assetUrl");
            var assetName = assetUrl.Split('/').Last().Split('.')[0];
            var path = $"Custom/Clothing/{gender}/VPB/{packageName}/{assetName}/{cuaId}";
            var uid = $"VPB:{packageName} - {cuaId}";

            clothingItem = new JSONClass();
            clothingItem["id"] = path + ".vam";
            clothingItem["internalId"] = uid;
            clothingItem["enabled"] = "true";

            var metaExists = File.Exists(path + ".vam");
            if (metaExists && !replaceExisting) return false;

            var clothingMeta = JSONClass.Parse(GetClothingMetadataJSON(gender, uid, cuaId)).AsObject;
            var clothingPreset = JSONClass.Parse(GetClothingPresetJSON(uid)).AsObject;
            var clothingBinary = GetClothingBinaryData();

            if (metaExists) File.Delete(path + ".vam");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            // should SuperController be used?
            SuperController.singleton.SaveJSON(clothingMeta, path + ".vam");

            if (File.Exists(path + ".vaj")) File.Delete(path + ".vaj");
            // should SuperController be used?
            SuperController.singleton.SaveJSON(clothingPreset, path + ".vaj");

            // Create the dummy binary if it does not exist
            if (!File.Exists(path + ".vab"))
            {
                using (var output = FileManager.OpenStreamForCreate(path + ".vab")) {
                    using (var writer = new BinaryWriter(output))
                    {
                        writer.Write(clothingBinary);
                    }
                }

            }

            return true;
        }



        public static JSONClass BuildCUAClothingStorable(JSONClass cua, SimpleTransform offset, string parentBone, string clothingId = "VPB:CUA Clothing Dummy")
        {
            var pluginManager = new JSONClass();
            pluginManager["id"] = $"{clothingId}:plugin#0_Stopper.ClothingPluginManager";

            var pluginMap = new JSONClass();
            pluginMap["plugin#0"] = "Stopper.ClothingPluginManager.7:/Custom/Scripts/Stopper/ClothingPluginManager/ClothingPluginManager.cs";
            pluginMap["plugin#1"] = "Skynet.CUAClothingAlt.7:/Custom/Scripts/Skynet/CUAClothingAlt.cs";
            pluginManager["plugins"] = pluginMap;

            var pluginConfig = new JSONClass();

            pluginConfig["id"] = "plugin#1_Stopper.CUAClothing";
            pluginConfig["xOffset"] = offset.Position.x.ToString("F5");
            pluginConfig["yOffset"] = offset.Position.y.ToString("F5");
            pluginConfig["zOffset"] = offset.Position.z.ToString("F5");
            pluginConfig["xRotation"] = offset.Rotation.eulerAngles.x.ToString("F5");
            pluginConfig["yRotation"] = offset.Rotation.eulerAngles.y.ToString("F5");
            pluginConfig["zRotation"] = offset.Rotation.eulerAngles.z.ToString("F5");

            var asset = cua.GetStorable("asset");
            if (asset != null)
            {
                pluginConfig["assetName"] = asset["assetName"];
                pluginConfig["Asset"] = asset["assetName"];
                pluginConfig["assetUrl"] = asset["assetUrl"];
                pluginConfig["Parent Bone"] = parentBone;
            }
            else
            {
                LogUtil.LogError($"CUA {cua.GetId()} has no asset!");
            }

            pluginManager["storables"] = new JSONArray() { pluginConfig };

            return pluginManager;
        }
    }
}
