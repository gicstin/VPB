using SimpleJSON;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace VPB.src.util
{
    public partial class CUAClothing
    {

        public const string ClothingMetaTemplateJson = @"
{
    ""itemType"" : ""ClothingFemale"",
    ""uid"" : ""$ID"",
    ""displayName"" : ""$DisplayName"",
    ""creatorName"" : ""VPB"",
    ""tags"" : """",
    ""isRealItem"" : ""true""
}
";

        public const string ClothingPresetTemplateJson = @"
{
	""components"": [
		{
			""type"": ""DAZMesh""
		},
		{
			""type"": ""DAZSkinWrap""
		},
		{
			""type"": ""DAZSkinWrapMaterialOptions""
		},
		{
			""type"": ""MVRPluginManager""
		}
	],
	""storables"": [
		{
			""id"": ""$ID"",
			""plugins"": {
				""plugin#0"": ""Stopper.ClothingPluginManager.latest:/Custom/Scripts/Stopper/ClothingPluginManager/ClothingPluginManager.cs""
			}
		},
		{
			""id"": ""$IDWrapControl"",
			""wrapToSmoothedVerts"": ""false"",
			""surfaceOffset"": ""0.0003"",
			""additionalThicknessMultiplier"": ""0.001"",
			""smoothIterations"": ""1""
		},
		{
			""id"": ""$IDSim"",
			""simEnabled"": ""false"",
			""integrateEnabled"": ""true"",
			""collisionEnabled"": ""true"",
			""allowDetach"": ""false"",
			""collisionRadius"": ""0.01"",
			""drag"": ""0.06"",
			""weight"": ""1"",
			""distanceScale"": ""1"",
			""stiffness"": ""0.5"",
			""compressionResistance"": ""0.5"",
			""friction"": ""0.5"",
			""staticMultiplier"": ""2"",
			""collisionPower"": ""0.5"",
			""gravityMultiplier"": ""1"",
			""iterations"": ""3"",
			""detachThreshold"": ""0.005"",
			""jointStrength"": ""1"",
			""force"": [
				""0"",
				""0"",
				""0""
			]
		},
		{
			""id"": ""$IDItemControl"",
			""disableAnatomy"": ""false"",
			""isRealClothingItem"": ""true"",
			""enableJointSpringAndDamperAdjust"": ""true"",
			""enableBreastJointAdjust"": ""false"",
			""enableGluteJointAdjust"": ""false"",
			""breastJointSpringAndDamperMultiplier"": ""3"",
			""gluteJointSpringAndDamperMultiplier"": ""3""
		},
		{
			""id"": ""$IDMaterialdummyMat"",
			""hideMaterial"": ""false"",
			""renderQueue"": ""2400"",
			""Specular Texture Offset"": ""0"",
			""Specular Intensity"": ""1"",
			""Gloss"": ""5"",
			""Specular Fresnel"": ""0.5"",
			""Gloss Texture Offset"": ""0"",
			""Global Illumination Filter"": ""0"",
			""Alpha Adjust"": ""-1"",
			""Diffuse Texture Offset"": ""0"",
			""Diffuse Bumpiness"": ""1"",
			""Specular Bumpiness"": ""1"",
			""customTexture1TileX"": ""1"",
			""customTexture1TileY"": ""1"",
			""customTexture1OffsetX"": ""0"",
			""customTexture1OffsetY"": ""0"",
			""customTexture2TileX"": ""1"",
			""customTexture2TileY"": ""1"",
			""customTexture2OffsetX"": ""0"",
			""customTexture2OffsetY"": ""0"",
			""customTexture3TileX"": ""1"",
			""customTexture3TileY"": ""1"",
			""customTexture3OffsetX"": ""0"",
			""customTexture3OffsetY"": ""0"",
			""customTexture4TileX"": ""1"",
			""customTexture4TileY"": ""1"",
			""customTexture4OffsetX"": ""0"",
			""customTexture4OffsetY"": ""0"",
			""customTexture5TileX"": ""1"",
			""customTexture5TileY"": ""1"",
			""customTexture5OffsetX"": ""0"",
			""customTexture5OffsetY"": ""0"",
			""customTexture6TileX"": ""1"",
			""customTexture6TileY"": ""1"",
			""customTexture6OffsetX"": ""0"",
			""customTexture6OffsetY"": ""0"",
			""customTexture_MainTex"": """",
			""customTexture_SpecTex"": """",
			""customTexture_GlossTex"": """",
			""customTexture_AlphaTex"": """",
			""customTexture_BumpMap"": """",
			""customTexture_DecalTex"": """",
			""simTexture"": """",
			""Diffuse Color"": {
				""h"": ""0"",
				""s"": ""0"",
				""v"": ""1""
			},
			""Specular Color"": {
				""h"": ""0"",
				""s"": ""0"",
				""v"": ""0""
			},
			""Subsurface Color"": {
				""h"": ""0"",
				""s"": ""0"",
				""v"": ""1""
			}
		}
	]
}
";

        public const string ClothingBinaryBase64 = "DER5bmFtaWNTdG9yZQMxLjAHREFaTWVzaAMxLjATZHVtbXljbG90aGluZ2ltcG9ydBVkdW1teWNsb3RoaW5naW1wb3J0LTIVZHVtbXljbG90aGluZ2ltcG9ydC0xFWR1bW15Y2xvdGhpbmdpbXBvcnQtMwQAAABGq8+9JZ+NPlPinryUJb29xYiNPgJkibxiYtO9DzyMPjJrM7xdxsK9KiqMPufKB7wBAAAACGR1bW15TWF0AQAAAAAAAAAEAAAAAwAAAAIAAAAAAAAAAQAAAAAAAAAEAAAAAwAAAAIAAAAAAAAAAQAAAAQAAAD/Pwo/AkhtPwCADD/6e20/BCAKPwKEbz8F4As/+rNvPwAAAAALREFaU2tpbldyYXADMS4wBk5vcm1hbBBEQVpTa2luV3JhcFN0b3JlAzEuMAQAAAB9BQAADhgAAGcoAAAPGAAAUkmdOZauhD/+moE+zIsDPh93mcKIzbzAfQUAAGcoAAAOGAAADxgAAFJJnTmgpH8/VNpRvllABT7k52HCdIQ/QsUBAADxAQAADhgAACECAABSSZ05N3umP9GT6z1L8BA+fNiZwlvMpMDFAQAADhgAAPEBAAAhAgAAUkmdOW1ZjD9rY+W+JTkSPuiKX8KTDD9CD01hdGVyaWFsT3B0aW9ucwMxLjARK01hdGVyaWFsZHVtbXlNYXQBAAAAAAAAAAA=";

		public static string GetClothingMetadataJSON(string gender, string id, string displayName)
		{
			return ClothingMetaTemplateJson.Replace("$Gender", gender).Replace("$ID", id).Replace("$DisplayName", displayName);
        }

		public static string GetClothingPresetJSON(string id)
		{
			return ClothingPresetTemplateJson.Replace("$ID", id);
        }

		public static byte[] GetClothingBinaryData()
		{
			return Convert.FromBase64String(ClothingBinaryBase64);
		}
    }
}
