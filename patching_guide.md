# VaM Texture Interception Guide (BepInEx 5 & Harmony)

This guide details how to correctly implement hooks to intercept texture handling in **Virt-A-Mate (VaM)** (Unity 2018.1.9) using **BepInEx 5.4.23.4**.

## 1. Overview and Architecture

To completely intercept texture handling, we must hook into the Unity Engine's API calls that generate or populate textures. Since VaM runs on the Mono runtime, we use **HarmonyX** (bundled with BepInEx 5) for runtime patching.

**Key Components:**
*   **Target Engine:** Unity 2018.1.9 (Mono .NET 4.x profile)
*   **Modding Framework:** BepInEx 5.4.23.4
*   **Hooking Library:** HarmonyX (0Harmony.dll)

### The Interception Point
Most texture loading in VaM (especially for user-generated content like Looks, Skins, and Clothes) happens via `Texture2D.LoadImage` or `ImageConversion.LoadImage`. 

To achieve "complete" interception, we target:
1.  `UnityEngine.Texture2D.LoadImage` (Main entry point for loading JPG/PNG bytes)
2.  `UnityEngine.ImageConversion.LoadImage` (Underlying implementation in newer Unity versions, often wrapped by Texture2D)
3.  `UnityEngine.Texture2D.LoadRawTextureData` (For raw byte manipulation)

---

## 2. Development Environment Setup

### Prerequisites
*   **IDE:** Visual Studio 2019/2022 or JetBrains Rider.
*   **Game Path:** Access to your `VaM` installation folder.

### Project Configuration
1.  Create a **Class Library (.NET Framework)** project.
2.  **Target Framework:** `.NET Framework 4.6` (Compatible with VaM's Mono profile).
3.  **References:**
    Add references to the following DLLs from your VaM installation:
    *   `BepInEx/core/BepInEx.dll`
    *   `BepInEx/core/0Harmony.dll`
    *   `VaM_Data/Managed/UnityEngine.dll` (or `UnityEngine.CoreModule.dll`, `UnityEngine.ImageConversionModule.dll` if split)
    *   `VaM_Data/Managed/Assembly-CSharp.dll` (Optional, if accessing game-specific logic)

---

## 3. Implementation

The following code demonstrates a robust plugin structure that installs hooks immediately upon loading.

### 3.1. Main Plugin Class
This class initializes Harmony and applies all patches.

```csharp
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace VaMTextureInterceptor
{
    [BepInPlugin(PluginInfo.GUID, PluginInfo.Name, PluginInfo.Version)]
    [BepInProcess("VaM.exe")] // Ensure it only runs on VaM
    public class TextureInterceptorPlugin : BaseUnityPlugin
    {
        public static class PluginInfo
        {
            public const string GUID = "com.yourname.vam.textureinterceptor";
            public const string Name = "Texture Interceptor";
            public const string Version = "1.0.0";
        }

        private void Awake()
        {
            // Initialize Logger
            Logger.LogInfo($"Plugin {PluginInfo.Name} is loading...");

            // Create Harmony Instance
            var harmony = new Harmony(PluginInfo.GUID);

            // Apply all attributes patches in the assembly
            harmony.PatchAll();

            Logger.LogInfo("Texture hooks applied successfully.");
        }
    }
}
```

### 3.2. Hooking Texture2D.LoadImage
This is the most critical hook. It captures almost all external image loading (skins, props, UI).

We use a **Prefix** to intercept the data *before* Unity processes it. This allows you to:
*   Inspect the image data (hash/analyze).
*   Replace the data (return false and manually load).
*   Modify the parameters.

```csharp
using HarmonyLib;
using UnityEngine;
using System;

namespace VaMTextureInterceptor.Patches
{
    // Hook Texture2D.LoadImage(byte[] data, bool markNonReadable)
    [HarmonyPatch(typeof(Texture2D))]
    [HarmonyPatch("LoadImage", new Type[] { typeof(byte[]), typeof(bool) })]
    public static class TextureLoadImagePatch
    {
        /// <summary>
        /// Intercepts texture loading.
        /// </summary>
        /// <param name="__instance">The Texture2D instance being loaded into.</param>
        /// <param name="data">The raw byte array of the PNG/JPG.</param>
        /// <param name="markNonReadable">Whether the texture should be strictly GPU-only.</param>
        /// <returns>true to run original Unity logic; false to skip it.</returns>
        public static bool Prefix(Texture2D __instance, ref byte[] data, bool markNonReadable)
        {
            if (data == null || data.Length == 0) return true;

            // Example 1: Logging
            // Debug.Log($"[TextureHook] Loading texture. Size: {data.Length} bytes");

            // Example 2: Interception / Replacement
            // If you want to replace the texture, modify the 'data' ref array here.
            // data = MyCustomEncryption.Decrypt(data);

            // Example 3: Aborting original load
            // return false; 
            
            return true; // Execute original method
        }

        // Optional: Postfix to act after the texture is created/filled
        public static void Postfix(Texture2D __instance, bool __result)
        {
            if (__result)
            {
                // Texture is now loaded in __instance
                // You can change filter mode, wrap mode, etc.
                // __instance.filterMode = FilterMode.Trilinear;
            }
        }
    }
}
```

### 3.3. Hooking ImageConversion (Deep Hook)
In Unity 2018, `Texture2D.LoadImage` often calls `ImageConversion.LoadImage` internally. If the high-level hook misses something, target this.

```csharp
[HarmonyPatch(typeof(ImageConversion), "LoadImage", new Type[] { typeof(Texture2D), typeof(byte[]), typeof(bool) })]
public static class ImageConversionPatch
{
    public static bool Prefix(Texture2D tex, byte[] data, bool markNonReadable)
    {
        // Similar logic to Texture2D hook
        return true;
    }
}
```

### 3.4. Hooking Raw Data (Advanced)
Some efficient loaders use `LoadRawTextureData`.

```csharp
[HarmonyPatch(typeof(Texture2D), "LoadRawTextureData", new Type[] { typeof(byte[]) })]
public static class LoadRawDataPatch
{
    public static bool Prefix(Texture2D __instance, byte[] data)
    {
        // Intercept raw byte injection
        return true;
    }
}
```

---

## 4. Building and Deployment

1.  **Build Solution:** Build the project in **Release** mode.
2.  **Output:** Locate the generated DLL in `bin/Release/`.
3.  **Install:**
    *   Copy the DLL to `[VaM Folder]/BepInEx/plugins/`.
4.  **Verify:**
    *   Run `VaM.exe`.
    *   Check `[VaM Folder]/BepInEx/LogOutput.log` for your "Texture hooks applied successfully" message.

## 5. Troubleshooting Common Issues

*   **Missing Method Exception:** Ensure you are patching the correct method signature. Unity API changes slightly between versions. Use `[HarmonyPatch("MethodName", typeof(arg1), typeof(arg2))]` to be explicit.
*   **Crash on Load:** Intercepting textures on the rendering thread can be sensitive. Avoid heavy processing in the `Prefix`. If you need to do heavy work, offload it or cache it.
*   **Infinite Loops:** Do not call `tex.LoadImage` inside your Hook's `Prefix` without unpatching or ensuring your logic doesn't trigger the hook again.

## 6. Advanced: Accessing VaM Specifics
If you need to intercept textures based on VaM contexts (e.g., "is this a skin?"), you may need to inspect the call stack or hook VaM's `ImageLoader` class (if accessible via `Assembly-CSharp.dll`).

```csharp
// Example of checking stack trace (expensive, use sparingly)
// var stack = new System.Diagnostics.StackTrace();
// if (stack.ToString().Contains("SkinLoader")) { ... }
```
