using System;
using System.Collections.Generic;

namespace VPB.src.util
{
    internal sealed class VpbLogRepeatGovernor
    {
        internal const int Capacity = 4096;
        internal const int MaxKeyCharacters = 2048;
        internal const double WindowSeconds = 30;
        internal const int Copies = 10;

        internal struct Key : IEquatable<Key>
        {
            internal string Source;
            internal string Message;
            internal int Level;
            internal bool ShowInGame;

            public bool Equals(Key other)
            {
                return Source == other.Source && Message == other.Message
                    && Level == other.Level && ShowInGame == other.ShowInGame;
            }

            public override bool Equals(object obj) { return obj is Key && Equals((Key)obj); }
            public override int GetHashCode()
            {
                return Source.GetHashCode() ^ Message.GetHashCode() ^ Level ^ ShowInGame.GetHashCode();
            }
        }

        internal sealed class Entry
        {
            internal Key Key;
            internal double Started;
            internal long Count;
        }

        private readonly object gate = new object();
        private readonly Dictionary<Key, Entry> entries = new Dictionary<Key, Entry>();
        internal int Count { get { lock (gate) return entries.Count; } }

        // A negative summary means the first suppression notice; positive means omitted copies.
        internal bool Accept(Key key, double now, out long summary)
        {
            summary = 0;
            // Preserve evidence rather than truncate or merge oversized/overflow messages.
            if (key.Source.Length + key.Message.Length > MaxKeyCharacters) return true;
            lock (gate)
            {
                Entry entry;
                if (!entries.TryGetValue(key, out entry))
                {
                    if (entries.Count >= Capacity) return true;
                    entry = new Entry { Key = key, Started = now };
                    entries.Add(key, entry);
                }
                else if (now - entry.Started >= WindowSeconds)
                {
                    summary = Math.Max(0, entry.Count - Copies);
                    entry.Count = 0;
                    entry.Started = now;
                }
                entry.Count++;
                if (entry.Count == Copies + 1) summary = -1;
                return entry.Count <= Copies;
            }
        }

        internal List<Entry> Flush(double now, bool all)
        {
            var summaries = new List<Entry>();
            lock (gate)
            {
                var expired = new List<Key>();
                // ponytail: scan at most 4096 keys once per second; use expiry queue if cap grows.
                foreach (var pair in entries)
                {
                    if (!all && now - pair.Value.Started < WindowSeconds) continue;
                    expired.Add(pair.Key);
                    if (pair.Value.Count > Copies) summaries.Add(pair.Value);
                }
                foreach (var key in expired) entries.Remove(key);
            }
            return summaries;
        }
    }
}
