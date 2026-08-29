using System;
using System.Text;
using VpbNet;

namespace VPB
{
    // Enforce on the owning machine. Decide() reads the local table alone; sender check is UI-only.
    public static class VpbNetRulebook
    {
        public const int MaxPendingAsks = 4;
        public const int AskSlotBytes = VpbNetEventLimits.MaxPayload + VpbNetEventCodec.HeaderSize;

        public const double AskTimeoutMs = 30000.0;

        // Without this, "ask" is just slower "allowed".
        public const double DenyCooldownMs = 60000.0;

        sealed class AskSlot
        {
            public readonly byte[] Bytes = new byte[AskSlotBytes];
            public int Len;
            public byte Domain;
            public byte Axis;
            public string What;
            public double DeadlineMs;
            public bool Used;
            public bool Approved;
        }

        static readonly AskSlot[] _slots = new AskSlot[MaxPendingAsks];
        static readonly double[] _cooldownUntilMs = new double[VpbNetRuleDomain.Count];
        static readonly StringBuilder _sb = new StringBuilder(256);

        static VpbNetRuleTable _local;
        static VpbNetRuleTable _peer;
        static Action _sender;

        static bool _loaded;
        static bool _peerPublished;
        static uint _peerRevision;
        static double _lastTickMs;
        static int _asked;
        static int _approved;
        static int _denied;
        static int _timedOut;
        static int _refused;
        static int _superseded;
        static bool _haveUndo;
        static uint _undoLo;
        static uint _undoHi;

        static VpbNetRulebook()
        {
            for (int i = 0; i < _slots.Length; i++) _slots[i] = new AskSlot();
        }

        public static VpbNetRuleTable Local { get { EnsureLoaded(); return _local; } }
        public static VpbNetRuleTable Peer { get { return _peer; } }
        public static bool PeerPublished { get { return _peerPublished; } }
        public static uint PeerRevision { get { return _peerRevision; } }

        public static int Asked { get { return _asked; } }
        public static int Approved { get { return _approved; } }
        public static int Denied { get { return _denied; } }
        public static int TimedOut { get { return _timedOut; } }
        public static int Refused { get { return _refused; } }
        public static int Superseded { get { return _superseded; } }

        static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;

            VpbNetRuleTable t = VpbNetRuleTable.FromPreset(VpbNetRulePreset.WatchTogether);
            try
            {
                Settings s = Settings.Instance;
                if (s != null && s.NetRulesLo != null && s.NetRulesHi != null)
                {
                    t.Lo = unchecked((uint)s.NetRulesLo.Value);
                    t.Hi = unchecked((uint)s.NetRulesHi.Value);
                }
            }
            catch { }

            t = UpgradeStock(t);
            t.Revision = 1;
            _local = t;
        }

        // Unedited table follows current default so stock-lane changes don't read as custom.
        static VpbNetRuleTable UpgradeStock(VpbNetRuleTable t)
        {
            if (VpbNetRuleTable.SameLanes(t, StockWatch(VpbNetRuleLevel.Ask))
                || VpbNetRuleTable.SameLanes(t, StockWatch(VpbNetRuleLevel.Allowed)))
                return VpbNetRuleTable.FromPreset(VpbNetRulePreset.WatchTogether);
            return VpbNetRuleTable.Normalize(t);
        }

        // Older "watch together" wire: content used to ask, look was four rows.
        static VpbNetRuleTable StockWatch(byte content)
        {
            VpbNetRuleTable t = new VpbNetRuleTable();
            for (byte d = 0; d < VpbNetRuleDomain.Count; d++)
            {
                if (VpbNetRuleTable.HasAxis(d, VpbNetRuleAxis.Mirror))
                    t.Set(d, VpbNetRuleAxis.Mirror, VpbNetRuleLevel.Allowed);
            }
            t.Set(VpbNetRuleDomain.DualPose, VpbNetRuleAxis.Control, VpbNetRuleLevel.Ask);
            t.Set(VpbNetRuleDomain.Clothing, VpbNetRuleAxis.Control, VpbNetRuleLevel.Ask);
            t.Set(VpbNetRuleDomain.Hair, VpbNetRuleAxis.Control, VpbNetRuleLevel.Ask);
            t.Set(VpbNetRuleDomain.Scene, VpbNetRuleAxis.Control, VpbNetRuleLevel.Ask);
            t.Set(VpbNetRuleDomain.AvatarClaim, VpbNetRuleAxis.Control, VpbNetRuleLevel.Ask);
            t.Set(VpbNetRuleDomain.Content, VpbNetRuleAxis.Control, content);
            t.Set(VpbNetRuleDomain.Objects, VpbNetRuleAxis.Control, VpbNetRuleLevel.Allowed);
            t.Set(VpbNetRuleDomain.Params, VpbNetRuleAxis.Control, VpbNetRuleLevel.Allowed);
            t.Set(VpbNetRuleDomain.Triggers, VpbNetRuleAxis.Control, VpbNetRuleLevel.Allowed);
            return t;
        }

        public static byte LocalLevel(byte domain, byte axis)
        {
            EnsureLoaded();
            return _local.Effective(domain, axis);
        }

        public static void SetLocalLevel(byte domain, byte axis, byte level)
        {
            EnsureLoaded();
            domain = VpbNetRuleTable.Answerable(domain);
            if (!VpbNetRuleTable.IsEditable(domain, axis)) return;
            if (_local.Get(domain, axis) == VpbNetRuleLevel.Sanitize(level)) return;

            PushUndo();
            _local.Set(domain, axis, level);
            _local = VpbNetRuleTable.Normalize(_local);
            CommitLocal();
        }

        public static void ApplyPreset(byte preset)
        {
            EnsureLoaded();
            VpbNetRuleTable next = VpbNetRuleTable.FromPreset(preset);
            if (VpbNetRuleTable.SameLanes(next, _local)) return;

            PushUndo();
            next.Revision = _local.Revision;
            _local = next;
            CommitLocal();
        }

        public static bool CanUndo { get { return _haveUndo; } }

        public static void Undo()
        {
            if (!_haveUndo) return;
            EnsureLoaded();
            _local.Lo = _undoLo;
            _local.Hi = _undoHi;
            _haveUndo = false;
            CommitLocal();
        }

        static void PushUndo()
        {
            _undoLo = _local.Lo;
            _undoHi = _local.Hi;
            _haveUndo = true;
        }

        public static byte LocalPreset()
        {
            EnsureLoaded();
            return VpbNetRuleTable.MatchPreset(_local);
        }

        static void CommitLocal()
        {
            _local.Revision++;

            try
            {
                Settings s = Settings.Instance;
                if (s != null && s.NetRulesLo != null && s.NetRulesHi != null)
                {
                    s.NetRulesLo.Value = unchecked((int)_local.Lo);
                    s.NetRulesHi.Value = unchecked((int)_local.Hi);
                }
            }
            catch { }

            ReconcilePending();

            if (_sender == null) return;
            try { _sender(); }
            catch { }
        }

        // Loosened rule should not leave a wait; tightened-to-blocked should not still offer Allow.
        static void ReconcilePending()
        {
            for (int i = 0; i < _slots.Length; i++)
            {
                AskSlot slot = _slots[i];
                if (!slot.Used || slot.Approved) continue;

                byte level = _local.Effective(slot.Domain, slot.Axis);
                if (level == VpbNetRuleLevel.Allowed)
                {
                    slot.Approved = true;
                    _approved++;
                }
                else if (level == VpbNetRuleLevel.Blocked)
                {
                    Release(slot);
                    _denied++;
                }
            }
        }

        public static void SetSender(Action sender)
        {
            _sender = sender;
        }

        public static void ResetForSession()
        {
            EnsureLoaded();
            _peer = VpbNetRuleTable.LegacyPeer();
            _peerPublished = false;
            _peerRevision = 0;
            _asked = 0;
            _approved = 0;
            _denied = 0;
            _timedOut = 0;
            _refused = 0;
            _superseded = 0;
            for (int i = 0; i < _slots.Length; i++) Release(_slots[i]);
            for (int i = 0; i < _cooldownUntilMs.Length; i++) _cooldownUntilMs[i] = 0.0;
        }

        public static void NotePeerTable(VpbNetRuleTable t)
        {
            _peer = t;
            _peerRevision = t.Revision;
            _peerPublished = true;
        }

        // No Rules cap → older build; give it only the grants that build could ever have acted on.
        public static void NotePeerCaps(uint peerCaps)
        {
            if ((peerCaps & VpbNetCapability.Rules) != 0) return;
            _peer = VpbNetRuleTable.LegacyPeer();
            _peerPublished = false;
            _peerRevision = 0;
        }

        // Advisory only — keep local UI honest; this is not the decision.
        public static bool PeerWouldAccept(byte domain, byte axis)
        {
            return _peer.Effective(domain, axis) != VpbNetRuleLevel.Blocked;
        }

        public static byte Decide(byte domain, byte axis)
        {
            EnsureLoaded();
            return _local.Effective(domain, axis);
        }

        public static bool Allows(byte domain, byte axis)
        {
            return Decide(domain, axis) == VpbNetRuleLevel.Allowed;
        }

        public static void NoteRefused()
        {
            _refused++;
        }

        // Copy bytes and return as refused.
        public static bool Hold(byte domain, byte axis, string what,
            byte[] buf, int offset, int len, double nowMs)
        {
            EnsureLoaded();
            if (buf == null || len <= 0 || len > AskSlotBytes) return false;
            if (offset < 0 || offset + len > buf.Length) return false;
            if (!VpbNetRuleDomain.IsKnown(domain)) return false;

            // Hold under the answerable lane so the prompt names an editable row.
            domain = VpbNetRuleTable.Answerable(domain);
            if (nowMs < _cooldownUntilMs[domain]) return false;

            // Lane already waiting keeps its bytes; a re-send must not reset the deadline.
            if (FindPending(domain, axis) != null)
            {
                _superseded++;
                return true;
            }

            AskSlot slot = FindFree();
            if (slot == null) return false;
            _asked++;

            Buffer.BlockCopy(buf, offset, slot.Bytes, 0, len);
            slot.Len = len;
            slot.Domain = domain;
            slot.Axis = axis;
            slot.What = what;
            slot.DeadlineMs = nowMs + AskTimeoutMs;
            slot.Used = true;
            slot.Approved = false;
            return true;
        }

        public static double LastTickMs { get { return _lastTickMs; } }

        public static void Tick(double nowMs)
        {
            _lastTickMs = nowMs;
            for (int i = 0; i < _slots.Length; i++)
            {
                AskSlot slot = _slots[i];
                if (!slot.Used || slot.Approved) continue;
                if (nowMs < slot.DeadlineMs) continue;

                byte domain = slot.Domain;
                Release(slot);
                _timedOut++;
                _refused++;
                if (domain < _cooldownUntilMs.Length)
                    _cooldownUntilMs[domain] = nowMs + DenyCooldownMs;
            }
        }

        public static int TryTakeApproved(byte[] dst)
        {
            if (dst == null) return 0;
            for (int i = 0; i < _slots.Length; i++)
            {
                AskSlot slot = _slots[i];
                if (!slot.Used || !slot.Approved) continue;

                int len = slot.Len;
                if (len > dst.Length)
                {
                    Release(slot);
                    continue;
                }
                Buffer.BlockCopy(slot.Bytes, 0, dst, 0, len);
                Release(slot);
                return len;
            }
            return 0;
        }

        public static bool HasPending
        {
            get
            {
                for (int i = 0; i < _slots.Length; i++)
                {
                    if (_slots[i].Used && !_slots[i].Approved) return true;
                }
                return false;
            }
        }

        public static int PendingCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _slots.Length; i++)
                {
                    if (_slots[i].Used && !_slots[i].Approved) n++;
                }
                return n;
            }
        }

        public static int PendingSecondsLeft
        {
            get
            {
                AskSlot slot = OldestPending();
                if (slot == null) return 0;
                double left = (slot.DeadlineMs - _lastTickMs) / 1000.0;
                if (left <= 0.0) return 0;
                return (int)left;
            }
        }

        public static string PendingText
        {
            get
            {
                AskSlot slot = OldestPending();
                return slot == null ? string.Empty : slot.What;
            }
        }

        public static bool HasPendingFor(byte domain, byte axis)
        {
            return FindPending(VpbNetRuleTable.Answerable(domain), axis) != null;
        }

        public static bool TryOldestPending(out byte domain, out byte axis)
        {
            AskSlot slot = OldestPending();
            if (slot == null)
            {
                domain = 0;
                axis = 0;
                return false;
            }
            domain = slot.Domain;
            axis = slot.Axis;
            return true;
        }

        public static void ApproveOldest()
        {
            AskSlot slot = OldestPending();
            if (slot == null) return;
            slot.Approved = true;
            _approved++;
        }

        public static void ApproveOldestAlways()
        {
            AskSlot slot = OldestPending();
            if (slot == null) return;

            byte domain = slot.Domain;
            byte axis = slot.Axis;
            slot.Approved = true;
            _approved++;

            // Persist like any setting.
            SetLocalLevel(domain, axis, VpbNetRuleLevel.Allowed);
        }

        public static void DenyOldest()
        {
            AskSlot slot = OldestPending();
            if (slot == null) return;

            byte domain = slot.Domain;
            Release(slot);
            _denied++;
            _refused++;
            if (domain < _cooldownUntilMs.Length)
                _cooldownUntilMs[domain] = _lastTickMs + DenyCooldownMs;
        }

        public static string DescribeLocal()
        {
            EnsureLoaded();
            _sb.Length = 0;
            VpbNetRuleTable.Describe(_sb, _local);
            return _sb.ToString();
        }

        public static string DescribePeer()
        {
            if (!_peerPublished) return "not stated yet";
            _sb.Length = 0;
            VpbNetRuleTable.Describe(_sb, _peer);
            return _sb.ToString();
        }

        static AskSlot OldestPending()
        {
            AskSlot best = null;
            for (int i = 0; i < _slots.Length; i++)
            {
                AskSlot slot = _slots[i];
                if (!slot.Used || slot.Approved) continue;
                if (best == null || slot.DeadlineMs < best.DeadlineMs) best = slot;
            }
            return best;
        }

        static AskSlot FindPending(byte domain, byte axis)
        {
            for (int i = 0; i < _slots.Length; i++)
            {
                AskSlot slot = _slots[i];
                if (!slot.Used || slot.Approved) continue;
                if (slot.Domain == domain && slot.Axis == axis) return slot;
            }
            return null;
        }

        static AskSlot FindFree()
        {
            for (int i = 0; i < _slots.Length; i++)
            {
                if (!_slots[i].Used) return _slots[i];
            }
            return null;
        }

        static void Release(AskSlot slot)
        {
            slot.Used = false;
            slot.Approved = false;
            slot.Len = 0;
            slot.What = null;
            slot.DeadlineMs = 0.0;
        }
    }
}
