using System;
using System.Collections.Generic;

namespace VPB.Tests
{
    /// <summary>Builds an <see cref="ExclusiveDependencyFinder.ScanInput"/> from uid -> dependency-token edges.</summary>
    internal sealed class DependencyGraphFixture
    {
        private readonly ExclusiveDependencyFinder.ScanInput _input = new ExclusiveDependencyFinder.ScanInput
        {
            SeedUids = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            InstalledUids = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            GroupToNewestUid = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            GroupToUids = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase),
            UidToVersion = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
            Forward = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase),
            SeedVarPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            LockedUids = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        };

        public DependencyGraphFixture Installed(params string[] uids)
        {
            foreach (string uid in uids) Register(uid);
            return this;
        }

        public DependencyGraphFixture Seed(params string[] uids)
        {
            foreach (string uid in uids)
            {
                Register(uid);
                _input.SeedUids.Add(uid);
                _input.SeedVarPaths[uid] = "";
            }
            return this;
        }

        public DependencyGraphFixture Locked(params string[] uids)
        {
            foreach (string uid in uids) _input.LockedUids.Add(uid);
            return this;
        }

        public DependencyGraphFixture DependsOn(string uid, params string[] tokens)
        {
            Register(uid);
            List<string> list;
            if (!_input.Forward.TryGetValue(uid, out list) || list == null)
            {
                list = new List<string>();
                _input.Forward[uid] = list;
            }
            list.AddRange(tokens);
            return this;
        }

        public ExclusiveDependencyFinder.ScanInput Build()
        {
            return _input;
        }

        public List<string> FindExclusive()
        {
            ExclusiveDependencyFinder.ScanResult result = ExclusiveDependencyFinder.Find(_input, null);
            var uids = new List<string>(result.ExclusiveUids);
            uids.Sort(StringComparer.OrdinalIgnoreCase);
            return uids;
        }

        public ExclusiveDependencyFinder.ScanResult Run()
        {
            return ExclusiveDependencyFinder.Find(_input, null);
        }

        private void Register(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return;
            if (!_input.InstalledUids.Add(uid)) return;

            string group = GroupOf(uid);
            int version = VersionOf(uid);
            _input.UidToVersion[uid] = version;

            List<string> members;
            if (!_input.GroupToUids.TryGetValue(group, out members) || members == null)
            {
                members = new List<string>();
                _input.GroupToUids[group] = members;
            }
            members.Add(uid);

            string newest;
            if (!_input.GroupToNewestUid.TryGetValue(group, out newest)
                || string.IsNullOrEmpty(newest)
                || VersionOf(newest) < version)
            {
                _input.GroupToNewestUid[group] = uid;
            }
        }

        public static string GroupOf(string uid)
        {
            int lastDot = uid.LastIndexOf('.');
            return lastDot > 0 ? uid.Substring(0, lastDot) : uid;
        }

        public static int VersionOf(string uid)
        {
            int lastDot = uid.LastIndexOf('.');
            int version;
            if (lastDot >= 0 && lastDot + 1 < uid.Length && int.TryParse(uid.Substring(lastDot + 1), out version))
                return version;
            return 0;
        }
    }
}
