using System.Text;
using VpbNet;

namespace VPB
{
    // Answerable rules in session-list order. Covered/unfusable rows are omitted.
    public static class VpbNetRuleCatalog
    {
        public const byte SectionBody = 0;
        public const byte SectionWorld = 1;
        public const int SectionCount = 2;

        public sealed class Entry
        {
            public byte Section;
            public byte Domain;
            public byte Axis;
            public string Label;
            public string Short;
            public string Tip;
        }

        static Entry[] _entries;

        public static Entry[] Entries
        {
            get
            {
                if (_entries == null) _entries = Build();
                return _entries;
            }
        }

        public static void Invalidate()
        {
            _entries = null;
            _searchBlob = null;
        }

        public static string LabelOf(byte domain, byte axis)
        {
            byte answerable = VpbNetRuleTable.Answerable(domain);
            Entry[] all = Entries;
            for (int i = 0; i < all.Length; i++)
            {
                Entry e = all[i];
                if (e.Domain == answerable && e.Axis == axis) return e.Label;
            }
            return null;
        }

        static string _searchBlob;

        // Labels stay search text so "dress" still finds the window.
        public static string SearchBlob()
        {
            if (_searchBlob != null) return _searchBlob;
            Entry[] all = Entries;
            StringBuilder sb = new StringBuilder(512);
            for (int i = 0; i < all.Length; i++)
            {
                if (i > 0) sb.Append(' ');
                Entry e = all[i];
                if (!string.IsNullOrEmpty(e.Label)) sb.Append(e.Label);
                sb.Append(' ');
                if (!string.IsNullOrEmpty(e.Short)) sb.Append(e.Short);
            }
            sb.Append(VPBTranslation.T("net_rules.search.folded",
                " clothing clothes dress hair skin morphs body shape appearance look"
                + " objects props lights triggers settings"));
            _searchBlob = sb.ToString();
            return _searchBlob;
        }

        public static string SectionTitle(byte section)
        {
            if (section == SectionBody)
                return VPBTranslation.T("net_rules.section.body", "What they may do to you");
            return VPBTranslation.T("net_rules.section.world", "What they may do to your scene");
        }

        public static string SectionHint(byte section)
        {
            if (section == SectionBody)
                return VPBTranslation.T("net_rules.hint.body",
                    "Lands on the person you are playing as. Seeing each other move is the session itself and is always on; none of this is that.");
            return VPBTranslation.T("net_rules.hint.world",
                "Lands on the scene you are both in. People are never included - the rows above cover those. Holding collisions off while you play is a Physics button on the session window, not a permission here.");
        }

        static Entry[] Build()
        {
            Entry[] all = new Entry[]
            {
                Make(SectionBody, VpbNetRuleDomain.Pose, VpbNetRuleAxis.Control,
                    VPBTranslation.T("settings.net_rules.pose_control", "Put a pose on me"),
                    VPBTranslation.T("net_rules.short.pose_control",
                        "A pose file landing on your own body."),
                    VPBTranslation.T("settings.tip.net_rules.pose_control",
                        "Let them drop a pose file onto the person you are playing as. Seeing each other move is always on and is not this. Blocked by default.")),

                Make(SectionBody, VpbNetRuleDomain.DualPose, VpbNetRuleAxis.Control,
                    VPBTranslation.T("settings.net_rules.dualpose_control", "Start a two-person pose"),
                    VPBTranslation.T("net_rules.short.dualpose_control",
                        "A pose built for two: your side agrees to take its half."),
                    VPBTranslation.T("settings.tip.net_rules.dualpose_control",
                        "Let them start a pose built for two people, which moves you as well as them. Each machine still only moves the body it is playing, so this is your side agreeing to take its half. Asks by default.")),

                Make(SectionBody, VpbNetRuleDomain.Look, VpbNetRuleAxis.Control,
                    VPBTranslation.T("settings.net_rules.look_control", "Change how I look"),
                    VPBTranslation.T("net_rules.short.look_control",
                        "Clothes, hair, skin and body shape - one answer for all of it."),
                    VPBTranslation.T("settings.tip.net_rules.look_control",
                        "Let them change how you look: clothes, hair, skin, colours, body shape, or a whole appearance preset. One answer covers all of it. Ask means every one of those names the file first and waits for you. Asks by default.")),

                Make(SectionWorld, VpbNetRuleDomain.AvatarClaim, VpbNetRuleAxis.Control,
                    VPBTranslation.T("settings.net_rules.claim_control", "Play as someone in my scene"),
                    VPBTranslation.T("net_rules.short.claim_control",
                        "Blocked keeps them watching: you never see them on anybody."),
                    VPBTranslation.T("settings.tip.net_rules.claim_control",
                        "Let them take one of the people in your scene and play as them. Only matters while you are the one hosting. Blocked means they stay a watcher: they see you, and you see them on nobody. Asks by default.")),

                Make(SectionWorld, VpbNetRuleDomain.Scene, VpbNetRuleAxis.Control,
                    VPBTranslation.T("settings.net_rules.scene_control", "Load their scene files on my PC"),
                    VPBTranslation.T("net_rules.short.scene_control",
                        "A scene file can carry a plugin, and a plugin runs code on your PC."),
                    VPBTranslation.T("settings.tip.net_rules.scene_control",
                        "Let them load a subscene into your scene. Read this one before loosening it: a subscene is a scene file, a scene file can carry plugins, and a plugin loaded this way runs its code on your PC. Only ever allow this for someone you would already hand a scene file to. Asks by default, and the prompt names the file.")),

                Make(SectionWorld, VpbNetRuleDomain.Objects, VpbNetRuleAxis.Control,
                    VPBTranslation.T("settings.net_rules.objects_control", "Move things and change the room"),
                    VPBTranslation.T("net_rules.short.objects_control",
                        "Furniture, lights, doors. People are never included."),
                    VPBTranslation.T("settings.tip.net_rules.objects_control",
                        "Let them move, add and delete scene objects, change what one is set to (a light's colour, a range, an on/off), and set off triggers already in the scene, so a door one of you opens is open for both. People are never included. Moving is a live stream rather than a request, so \"ask\" behaves as \"allowed\" for movement; adding and deleting still prompt. Allowed by default.")),

                Make(SectionWorld, VpbNetRuleDomain.Content, VpbNetRuleAxis.Control,
                    VPBTranslation.T("settings.net_rules.content_control", "Download content their scene needs"),
                    VPBTranslation.T("net_rules.short.content_control",
                        "Downloads from the Hub, by name. No file crosses between your two PCs."),
                    VPBTranslation.T("settings.tip.net_rules.content_control",
                        "Let a scene they pick fetch the packages you are missing, from the VaM Hub, so you can join without hunting for them. Only names cross the connection - no file ever travels between the two PCs - and every download comes from the Hub exactly as if you had pressed it yourself. What it costs is disk space and bandwidth, capped by the download limit in settings. Allowed by default, because a session where one side is missing the scene is not a session.")),
            };

            int keep = 0;
            for (int i = 0; i < all.Length; i++)
            {
                if (VpbNetRuleTable.IsEditable(all[i].Domain, all[i].Axis)) keep++;
            }
            if (keep == all.Length) return all;

            Entry[] kept = new Entry[keep];
            int k = 0;
            for (int i = 0; i < all.Length; i++)
            {
                if (VpbNetRuleTable.IsEditable(all[i].Domain, all[i].Axis)) kept[k++] = all[i];
            }
            return kept;
        }

        static Entry Make(byte section, byte domain, byte axis, string label, string shortText, string tip)
        {
            Entry e = new Entry();
            e.Section = section;
            e.Domain = domain;
            e.Axis = axis;
            e.Label = label;
            e.Short = shortText;
            e.Tip = tip;
            return e;
        }
    }
}
