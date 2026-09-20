using System;
using System.Collections.Generic;

namespace VPB.Outliner
{
    internal sealed class OutlinerSelection
    {
        readonly HashSet<string> _uids = new HashSet<string>(StringComparer.Ordinal);
        internal string PrimaryUid;

        internal OutlinerSelection()
        {
            PrimaryUid = "";
        }

        internal int Count { get { return _uids.Count; } }

        internal bool Contains(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return false;
            return _uids.Contains(uid);
        }

        internal void Clear()
        {
            _uids.Clear();
            PrimaryUid = "";
        }

        internal void SelectOnly(string uid)
        {
            _uids.Clear();
            PrimaryUid = uid ?? "";
            if (!string.IsNullOrEmpty(PrimaryUid))
                _uids.Add(PrimaryUid);
        }

        internal void Add(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return;
            _uids.Add(uid);
            PrimaryUid = uid;
        }

        internal void ToggleExtend(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return;
            if (_uids.Contains(uid))
            {
                _uids.Remove(uid);
                if (string.Equals(PrimaryUid, uid, StringComparison.Ordinal))
                    PrimaryUid = FirstUid();
            }
            else
            {
                _uids.Add(uid);
                PrimaryUid = uid;
            }
        }

        string FirstUid()
        {
            foreach (string u in _uids)
                return u;
            return "";
        }

        internal void CopyUids(List<string> into)
        {
            into.Clear();
            foreach (string u in _uids)
                into.Add(u);
        }
    }
}
