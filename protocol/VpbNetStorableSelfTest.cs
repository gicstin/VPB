using System;
using System.Text;

namespace VpbNet
{
    public static class VpbNetStorableSelfTest
    {
        public static int RunConsole()
        {
            StringBuilder sb = new StringBuilder(4096);
            bool ok = Run(sb);
            Console.Out.Write(sb.ToString());
            Console.Out.Flush();
            return ok ? 0 : 1;
        }

        public static bool Run(StringBuilder log)
        {
            int pass = 0;
            int fail = 0;

            Line(log, "===== storable whitelist self-test =====");

            AllowedShapes(log, ref pass, ref fail);
            DenyByDefault(log, ref pass, ref fail);
            PluginAndPresetNames(log, ref pass, ref fail);
            Malformed(log, ref pass, ref fail);
            StringValues(log, ref pass, ref fail);
            Triggers(log, ref pass, ref fail);
            AtomParams(log, ref pass, ref fail);
            Messages(log, ref pass, ref fail);

            Line(log, "-----");
            Line(log, "passed=" + pass + " failed=" + fail);
            Line(log, "EXIT 1/4 allow      only what the look sync actually drives : " + V(fail));
            Line(log, "EXIT 2/4 deny       anything unlisted is refused by default : " + V(fail));
            Line(log, "EXIT 3/4 escalation no plugin, preset or path name resolves : " + V(fail));
            Line(log, "EXIT 4/4 triggers   a trigger names a place, never an action : " + V(fail));
            Line(log, "EXIT 5/5 atom params values on a lamp, never a plugin or path : " + V(fail));
            if (fail == 0) Line(log, "RESULT: PASS");
            else Line(log, "RESULT: FAIL");
            Line(log, "===== end storable whitelist self-test =====");
            return fail == 0;
        }

        static void AllowedShapes(StringBuilder log, ref int pass, ref int fail)
        {
            Check(log, ref pass, ref fail, "a clothing param on geometry is allowed",
                VpbNetStorableWhitelist.IsAllowed("geometry", "clothing:Creator.Pack.1:/Custom/Clothing/x.vam"));
            Check(log, ref pass, ref fail, "a hair param on geometry is allowed",
                VpbNetStorableWhitelist.IsAllowed("geometry", "hair:Creator.Pack.1:/Custom/Hair/y.vam"));
            Check(log, ref pass, ref fail, "a morph param on geometry is allowed",
                VpbNetStorableWhitelist.IsAllowed("geometry", "morph:Creator.Pack.1:/Custom/Morphs/z.vmi"));
            Check(log, ref pass, ref fail, "geometry is the only known storable",
                VpbNetStorableWhitelist.IsKnownStorable("geometry")
                && !VpbNetStorableWhitelist.IsKnownStorable("control")
                && !VpbNetStorableWhitelist.IsKnownStorable("AutoExpressions"));
            Check(log, ref pass, ref fail, "a bare prefix with nothing after it is not a param",
                !VpbNetStorableWhitelist.IsAllowed("geometry", "clothing:"));
        }

        static void DenyByDefault(StringBuilder log, ref int pass, ref int fail)
        {
            Check(log, ref pass, ref fail, "an unlisted storable is refused",
                VpbNetStorableWhitelist.Check("SomethingElse", "clothing:a")
                    == VpbNetStorableVerdict.UnknownStorable);
            Check(log, ref pass, ref fail, "an unlisted param on a known storable is refused",
                VpbNetStorableWhitelist.Check("geometry", "useAuxBreastColliders")
                    == VpbNetStorableVerdict.UnknownParam);
            Check(log, ref pass, ref fail, "a param that merely contains a prefix is refused",
                VpbNetStorableWhitelist.Check("geometry", "evilclothing:a")
                    == VpbNetStorableVerdict.UnknownParam);
            Check(log, ref pass, ref fail, "case is not a way in",
                VpbNetStorableWhitelist.Check("Geometry", "clothing:a")
                    == VpbNetStorableVerdict.UnknownStorable);
            Check(log, ref pass, ref fail, "prefix matching is ordinal, not culture-sensitive",
                VpbNetStorableWhitelist.Check("geometry", "CLOTHING:a")
                    == VpbNetStorableVerdict.UnknownParam);
        }

        static void PluginAndPresetNames(StringBuilder log, ref int pass, ref int fail)
        {
            Check(log, ref pass, ref fail, "a plugin storable is refused",
                VpbNetStorableWhitelist.Check("PluginManager", "clothing:a")
                    == VpbNetStorableVerdict.DeniedName);
            Check(log, ref pass, ref fail, "a plugin param is refused",
                VpbNetStorableWhitelist.Check("geometry", "pluginUrl")
                    == VpbNetStorableVerdict.DeniedName);
            Check(log, ref pass, ref fail, "a preset load is refused",
                VpbNetStorableWhitelist.Check("geometry", "LoadPreset")
                    == VpbNetStorableVerdict.DeniedName);
            Check(log, ref pass, ref fail, "a path param is refused",
                VpbNetStorableWhitelist.Check("geometry", "filePath")
                    == VpbNetStorableVerdict.DeniedName);
            Check(log, ref pass, ref fail, "a .cs reference is refused before anything else looks at it",
                VpbNetStorableWhitelist.Check("geometry", "clothing:Creator.Pack.1:/Custom/evil.cs")
                    == VpbNetStorableVerdict.PluginReference);
            Check(log, ref pass, ref fail, "a trailing-dot .cs reference is refused by the identifier rule first",
                VpbNetStorableWhitelist.Check("geometry", "clothing:Creator.Pack.1:/Custom/evil.cs.")
                    == VpbNetStorableVerdict.BadIdentifier);
            Check(log, ref pass, ref fail, "a trailing-space .cs reference is refused",
                !VpbNetStorableWhitelist.IsAllowed("geometry", "clothing:Creator.Pack.1:/Custom/evil.cs "));
        }

        static void Malformed(StringBuilder log, ref int pass, ref int fail)
        {
            Check(log, ref pass, ref fail, "null is refused, never dereferenced",
                VpbNetStorableWhitelist.Check(null, "clothing:a") == VpbNetStorableVerdict.BadIdentifier
                && VpbNetStorableWhitelist.Check("geometry", null) == VpbNetStorableVerdict.BadIdentifier);
            Check(log, ref pass, ref fail, "empty is refused",
                VpbNetStorableWhitelist.Check("", "clothing:a") == VpbNetStorableVerdict.BadIdentifier
                && VpbNetStorableWhitelist.Check("geometry", "") == VpbNetStorableVerdict.BadIdentifier);
            Check(log, ref pass, ref fail, "a traversal is refused",
                VpbNetStorableWhitelist.Check("geometry", "clothing:../../x.vam")
                    == VpbNetStorableVerdict.BadIdentifier);
            Check(log, ref pass, ref fail, "a backslash is refused",
                VpbNetStorableWhitelist.Check("geometry", "clothing:C:\\Windows")
                    == VpbNetStorableVerdict.BadIdentifier);
            Check(log, ref pass, ref fail, "an over-long storable is refused",
                VpbNetStorableWhitelist.Check(new string('g', VpbNetStorableLimits.MaxStorableChars + 1), "clothing:a")
                    == VpbNetStorableVerdict.Oversize);
            Check(log, ref pass, ref fail, "an over-long param is refused",
                VpbNetStorableWhitelist.Check("geometry", new string('c', VpbNetStorableLimits.MaxParamChars + 1))
                    == VpbNetStorableVerdict.Oversize);
        }

        static void StringValues(StringBuilder log, ref int pass, ref int fail)
        {
            Check(log, ref pass, ref fail, "an ordinary string value is allowed",
                VpbNetStorableWhitelist.IsAllowedStringValue("Smile"));
            Check(log, ref pass, ref fail, "a plugin file as a value is refused",
                !VpbNetStorableWhitelist.IsAllowedStringValue("Creator.Pack.1:/Custom/evil.cslist"));
            Check(log, ref pass, ref fail, "a control character in a value is refused",
                !VpbNetStorableWhitelist.IsAllowedStringValue("bad\u0001value"));
            Check(log, ref pass, ref fail, "an over-long value is refused",
                !VpbNetStorableWhitelist.IsAllowedStringValue(
                    new string('v', VpbNetStorableLimits.MaxStringValueChars + 1)));
            Check(log, ref pass, ref fail, "null is refused as a value",
                !VpbNetStorableWhitelist.IsAllowedStringValue(null));
        }

        static void Triggers(StringBuilder log, ref int pass, ref int fail)
        {
            Check(log, ref pass, ref fail, "an ordinary scene trigger passes the string rules",
                VpbNetStorableWhitelist.IsAllowedTrigger("Door", "CollisionTrigger"));
            Check(log, ref pass, ref fail, "a trigger on a packaged atom passes",
                VpbNetStorableWhitelist.IsAllowedTrigger("Creator.Scene.1:/Lamp", "ButtonTrigger"));

            Check(log, ref pass, ref fail,
                "the trigger door carries no param, so there is no action name a peer could choose",
                VpbNetStorableWhitelist.CheckTrigger("Door", "CollisionTrigger")
                    == VpbNetStorableVerdict.Allowed
                && VpbNetStorableWhitelist.Check("CollisionTrigger", "active")
                    == VpbNetStorableVerdict.UnknownStorable);

            Check(log, ref pass, ref fail, "a plugin-named storable is refused as a trigger too",
                VpbNetStorableWhitelist.CheckTrigger("Door", "PluginManager")
                    == VpbNetStorableVerdict.DeniedName);
            Check(log, ref pass, ref fail, "a preset-named storable is refused as a trigger",
                VpbNetStorableWhitelist.CheckTrigger("Door", "PresetTrigger")
                    == VpbNetStorableVerdict.DeniedName);
            Check(log, ref pass, ref fail, "a path in the atom uid is refused as a trigger",
                VpbNetStorableWhitelist.CheckTrigger("Door", "filePathTrigger")
                    == VpbNetStorableVerdict.DeniedName);
            Check(log, ref pass, ref fail, "a .cslist as a storable id is refused as a trigger",
                VpbNetStorableWhitelist.CheckTrigger("Door", "evil.cslist")
                    == VpbNetStorableVerdict.PluginReference);
            Check(log, ref pass, ref fail, "a traversal in the atom uid is refused as a trigger",
                VpbNetStorableWhitelist.CheckTrigger("../../Door", "CollisionTrigger")
                    == VpbNetStorableVerdict.BadIdentifier);
            Check(log, ref pass, ref fail, "a backslash in the atom uid is refused as a trigger",
                VpbNetStorableWhitelist.CheckTrigger("C:\\Windows", "CollisionTrigger")
                    == VpbNetStorableVerdict.BadIdentifier);
            Check(log, ref pass, ref fail, "null and empty are refused as a trigger",
                VpbNetStorableWhitelist.CheckTrigger(null, "CollisionTrigger")
                    == VpbNetStorableVerdict.BadIdentifier
                && VpbNetStorableWhitelist.CheckTrigger("Door", null)
                    == VpbNetStorableVerdict.BadIdentifier
                && VpbNetStorableWhitelist.CheckTrigger("", "CollisionTrigger")
                    == VpbNetStorableVerdict.BadIdentifier
                && VpbNetStorableWhitelist.CheckTrigger("Door", "")
                    == VpbNetStorableVerdict.BadIdentifier);
            Check(log, ref pass, ref fail, "an over-long trigger name is refused",
                VpbNetStorableWhitelist.CheckTrigger("Door",
                    new string('t', VpbNetStorableLimits.MaxStorableChars + 1))
                    == VpbNetStorableVerdict.Oversize
                && VpbNetStorableWhitelist.CheckTrigger(
                    new string('a', VpbNetStorableLimits.MaxParamChars + 1), "CollisionTrigger")
                    == VpbNetStorableVerdict.Oversize);

            Check(log, ref pass, ref fail,
                "opening the trigger door did not open the storable door: geometry is still the only drivable storable",
                VpbNetStorableWhitelist.IsKnownStorable("geometry")
                && !VpbNetStorableWhitelist.IsKnownStorable("CollisionTrigger")
                && !VpbNetStorableWhitelist.IsKnownStorable("ButtonTrigger"));
        }

        static void AtomParams(StringBuilder log, ref int pass, ref int fail)
        {
            Check(log, ref pass, ref fail, "a light intensity on Light is an ordinary setting and is allowed",
                VpbNetStorableWhitelist.IsAllowedAtomParam("Lamp", "Light", "intensity"));
            Check(log, ref pass, ref fail, "a colour on Light is allowed",
                VpbNetStorableWhitelist.IsAllowedAtomParam("Lamp", "Light", "color"));
            Check(log, ref pass, ref fail, "on/off on Light is allowed",
                VpbNetStorableWhitelist.IsAllowedAtomParam("Lamp", "Light", "on"));
            Check(log, ref pass, ref fail, "a chooser on Light is allowed",
                VpbNetStorableWhitelist.IsAllowedAtomParam("Lamp", "Light", "type"));

            Check(log, ref pass, ref fail, "control is position, not a setting, and is refused",
                VpbNetStorableWhitelist.CheckAtomParam("Lamp", "control", "position")
                    == VpbNetStorableVerdict.UnknownStorable);
            Check(log, ref pass, ref fail, "geometry is a look, not an object setting, and is refused",
                VpbNetStorableWhitelist.CheckAtomParam("Person", "geometry", "clothing:a")
                    == VpbNetStorableVerdict.UnknownStorable);
            Check(log, ref pass, ref fail, "a plugin-named storable is refused as a setting",
                VpbNetStorableWhitelist.CheckAtomParam("Lamp", "PluginManager", "on")
                    == VpbNetStorableVerdict.DeniedName);
            Check(log, ref pass, ref fail, "a preset-named param is refused as a setting",
                VpbNetStorableWhitelist.CheckAtomParam("Lamp", "Light", "LoadPreset")
                    == VpbNetStorableVerdict.DeniedName);
            Check(log, ref pass, ref fail, "a path-named param is refused as a setting",
                VpbNetStorableWhitelist.CheckAtomParam("Lamp", "Light", "filePath")
                    == VpbNetStorableVerdict.DeniedName);
            Check(log, ref pass, ref fail, "a .cs as a param name is refused before the denylist",
                VpbNetStorableWhitelist.CheckAtomParam("Lamp", "Light", "evil.cs")
                    == VpbNetStorableVerdict.PluginReference);
            Check(log, ref pass, ref fail, "a subscene atom uid is refused as a setting target",
                VpbNetStorableWhitelist.CheckAtomParam("Room/Lamp", "Light", "intensity")
                    == VpbNetStorableVerdict.DeniedName);
            Check(log, ref pass, ref fail, "null and empty are refused as a setting",
                VpbNetStorableWhitelist.CheckAtomParam(null, "Light", "on")
                    == VpbNetStorableVerdict.BadIdentifier
                && VpbNetStorableWhitelist.CheckAtomParam("Lamp", null, "on")
                    == VpbNetStorableVerdict.BadIdentifier
                && VpbNetStorableWhitelist.CheckAtomParam("Lamp", "Light", null)
                    == VpbNetStorableVerdict.BadIdentifier
                && VpbNetStorableWhitelist.CheckAtomParam("", "Light", "on")
                    == VpbNetStorableVerdict.BadIdentifier);
            Check(log, ref pass, ref fail, "an over-long setting name is refused",
                VpbNetStorableWhitelist.CheckAtomParam("Lamp", "Light",
                    new string('n', VpbNetStorableLimits.MaxParamChars + 1))
                    == VpbNetStorableVerdict.Oversize);

            Check(log, ref pass, ref fail, "scene lighting on CoreControl GlobalLighting is allowed",
                VpbNetStorableWhitelist.IsAllowedAtomParam("CoreControl", "GlobalLighting", "masterIntensity")
                && VpbNetStorableWhitelist.IsAllowedAtomParam("CoreControl", "GlobalLighting", "skyName")
                && VpbNetStorableWhitelist.IsAllowedAtomParam("CoreControl", "GlobalLighting", "showSkybox")
                && VpbNetStorableWhitelist.IsAllowedAtomParam("CoreControl", "GlobalLighting", "skyboxIntensity")
                && VpbNetStorableWhitelist.IsAllowedAtomParam("CoreControl", "GlobalLighting", "diffuseIntensity"));
            Check(log, ref pass, ref fail, "a GlobalLighting sky file url is named as a setting, not refused as a path",
                VpbNetStorableWhitelist.IsAllowedAtomParam("CoreControl", "GlobalLighting", "url"));
            Check(log, ref pass, ref fail, "the Skyshop class name is still recognised if a build stores it that way",
                VpbNetStorableWhitelist.IsAllowedAtomParam("CoreControl", "SkyshopLightController", "skyName"));
            Check(log, ref pass, ref fail, "CoreControl height and camera knobs stay local",
                VpbNetStorableWhitelist.CheckAtomParam("CoreControl", "HeightAdjust", "val")
                    == VpbNetStorableVerdict.UnknownStorable
                && VpbNetStorableWhitelist.CheckAtomParam("CoreControl", "control", "position")
                    == VpbNetStorableVerdict.UnknownStorable);
            Check(log, ref pass, ref fail, "a lamp url is still refused - only scene lighting may name a sky file",
                VpbNetStorableWhitelist.CheckAtomParam("Lamp", "Light", "url")
                    == VpbNetStorableVerdict.DeniedName);
            Check(log, ref pass, ref fail, "a built-in skybox style name is an ordinary string, not a file",
                VpbNetStorableWhitelist.IsAllowedStringValue("SkyCyber1Blur")
                && VpbNetStorableWhitelist.IsAllowedStringValue("SkyDaySunMidClear"));
            Check(log, ref pass, ref fail, "an ordinary sky file under Custom/ is allowed",
                VpbNetStorableWhitelist.IsAllowedSkyRef("Custom/Sky/sunset.png")
                && VpbNetStorableWhitelist.IsAllowedSkyRef("Creator.Pack.1:/Custom/Sky/cloud.hdr")
                && VpbNetStorableWhitelist.IsAllowedSkyRef("Custom/Sky/night.gif"));
            Check(log, ref pass, ref fail, "clearing the sky file is allowed",
                VpbNetStorableWhitelist.IsAllowedSkyRef(""));
            Check(log, ref pass, ref fail, "a sky file outside VaM content or with a drive letter is refused",
                VpbNetStorableWhitelist.CheckSkyRef("C:/Windows/sky.png")
                    == VpbNetStorableVerdict.BadIdentifier
                && VpbNetStorableWhitelist.CheckSkyRef("Elsewhere/sky.png")
                    == VpbNetStorableVerdict.BadIdentifier
                && VpbNetStorableWhitelist.CheckSkyRef("Custom/Sky/evil.cs")
                    == VpbNetStorableVerdict.PluginReference);

            Check(log, ref pass, ref fail,
                "opening the object-settings door did not open the look door: geometry is still the only look storable",
                VpbNetStorableWhitelist.IsKnownStorable("geometry")
                && !VpbNetStorableWhitelist.IsKnownStorable("Light")
                && VpbNetStorableWhitelist.IsDeniedParamStorable("control")
                && VpbNetStorableWhitelist.IsDeniedParamStorable("geometry"));
        }

        static void Messages(StringBuilder log, ref int pass, ref int fail)
        {
            bool named = true;
            for (int i = 0; i <= (int)VpbNetStorableVerdict.Oversize; i++)
            {
                string s = VpbNetStorableWhitelist.Explain((VpbNetStorableVerdict)i, "geometry", "someParam");
                if (string.IsNullOrEmpty(s) || s.Length < 2) named = false;
            }
            Check(log, ref pass, ref fail, "every verdict has prose, never a bare code", named);

            string m = VpbNetStorableWhitelist.Explain(
                VpbNetStorableVerdict.UnknownStorable, "AudioSource", "x");
            Check(log, ref pass, ref fail, "a refusal names what was refused",
                m.IndexOf("AudioSource", StringComparison.Ordinal) >= 0);
        }

        static string V(int fail)
        {
            return fail == 0 ? "PASS" : "see FAIL lines";
        }

        static void Check(StringBuilder log, ref int pass, ref int fail, string what, bool ok)
        {
            if (ok)
            {
                pass++;
                Line(log, "  ok   " + what);
            }
            else
            {
                fail++;
                Line(log, "  FAIL " + what);
            }
        }

        static void Line(StringBuilder log, string s)
        {
            log.Append(s);
            log.Append('\n');
        }
    }
}
