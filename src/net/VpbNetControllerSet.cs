using System;
using System.Text;
using UnityEngine;

namespace VPB
{
    public sealed class VpbNetControllerSet
    {
        public static readonly string[] Names =
        {
            "control",
            "hipControl",
            "pelvisControl",
            "chestControl",
            "headControl",
            "rHandControl",
            "lHandControl",
            "rFootControl",
            "lFootControl",
            "rElbowControl",
            "lElbowControl",
            "rKneeControl",
            "lKneeControl",
            "rThighControl",
            "lThighControl",
            "rArmControl",
            "lArmControl",
        };

        public const int RootIndex = 0;
        public const int ChestIndex = 3;
        public const int RightHandIndex = 5;

        public Atom Atom;
        public readonly FreeControllerV3[] Controllers = new FreeControllerV3[Names.Length];
        public readonly Transform[] Controls = new Transform[Names.Length];
        public readonly Rigidbody[] Bodies = new Rigidbody[Names.Length];

        readonly FreeControllerV3.PositionState[] _savedPositionState = new FreeControllerV3.PositionState[Names.Length];
        readonly FreeControllerV3.RotationState[] _savedRotationState = new FreeControllerV3.RotationState[Names.Length];
        bool _statesSaved;
        int _resolvedCount;

        public int ResolvedCount { get { return _resolvedCount; } }

        public bool Resolve(Atom atom)
        {
            Clear();
            if (atom == null) return false;

            FreeControllerV3[] all = null;
            try { all = atom.freeControllers; } catch { }
            if (all == null) return false;

            for (int i = 0; i < all.Length; i++)
            {
                FreeControllerV3 fc = all[i];
                if (fc == null) continue;
                int slot = IndexOfName(fc.name);
                if (slot < 0 || Controllers[slot] != null) continue;

                Controllers[slot] = fc;
                Controls[slot] = fc.control != null ? fc.control : fc.transform;
                Rigidbody rb = null;
                try { rb = fc.followWhenOffRB; } catch { }
                Bodies[slot] = rb;
                _resolvedCount++;
            }

            if (Controllers[RootIndex] == null || Controls[RootIndex] == null)
            {
                Clear();
                return false;
            }

            Atom = atom;
            return true;
        }

        public void Clear()
        {
            Atom = null;
            _statesSaved = false;
            _resolvedCount = 0;
            for (int i = 0; i < Names.Length; i++)
            {
                Controllers[i] = null;
                Controls[i] = null;
                Bodies[i] = null;
            }
        }

        public bool IsAlive()
        {
            if (Atom == null) return false;
            for (int i = 0; i < Names.Length; i++)
            {
                if (Controllers[i] == null) continue;
                if (Controls[i] == null) return false;
            }
            return true;
        }

        public void SaveStates()
        {
            if (_statesSaved) return;
            for (int i = 0; i < Names.Length; i++)
            {
                FreeControllerV3 fc = Controllers[i];
                if (fc == null) continue;
                try
                {
                    _savedPositionState[i] = fc.currentPositionState;
                    _savedRotationState[i] = fc.currentRotationState;
                }
                catch { }
            }
            _statesSaved = true;
        }

        public void RestoreStates()
        {
            if (!_statesSaved) return;
            for (int i = 0; i < Names.Length; i++)
            {
                FreeControllerV3 fc = Controllers[i];
                if (fc == null) continue;
                try
                {
                    fc.currentPositionState = _savedPositionState[i];
                    fc.currentRotationState = _savedRotationState[i];
                }
                catch { }
            }
            _statesSaved = false;
        }

        public void ApplyStates(FreeControllerV3.PositionState positionState, FreeControllerV3.RotationState rotationState)
        {
            for (int i = 0; i < Names.Length; i++)
            {
                FreeControllerV3 fc = Controllers[i];
                if (fc == null) continue;
                try
                {
                    fc.currentPositionState = positionState;
                    fc.currentRotationState = rotationState;
                }
                catch { }
            }
        }

        public void PauseComply(int frames)
        {
            for (int i = 0; i < Names.Length; i++)
            {
                FreeControllerV3 fc = Controllers[i];
                if (fc == null) continue;
                try { fc.PauseComply(frames); } catch { }
            }
        }

        public string DescribeMissing(StringBuilder sb)
        {
            sb.Length = 0;
            for (int i = 0; i < Names.Length; i++)
            {
                if (Controllers[i] != null) continue;
                if (sb.Length > 0) sb.Append(',');
                sb.Append(Names[i]);
            }
            return sb.Length == 0 ? "none" : sb.ToString();
        }

        static int IndexOfName(string name)
        {
            if (string.IsNullOrEmpty(name)) return -1;
            for (int i = 0; i < Names.Length; i++)
            {
                if (string.Equals(name, Names[i], StringComparison.Ordinal)) return i;
            }
            return -1;
        }
    }
}
