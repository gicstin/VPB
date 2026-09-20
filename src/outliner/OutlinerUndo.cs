using System;
using System.Collections.Generic;
using UnityEngine;

namespace VPB.Outliner
{
    internal sealed class OutlinerUndo
    {
        internal const int MaxRecords = 64;
        internal const float CoalesceSeconds = 1f;

        readonly List<OutlinerUndoRecord> _undo = new List<OutlinerUndoRecord>(MaxRecords);
        readonly List<OutlinerUndoRecord> _redo = new List<OutlinerUndoRecord>(16);

        internal int UndoCount { get { return _undo.Count; } }
        internal int RedoCount { get { return _redo.Count; } }

        internal string PeekUndoLabel()
        {
            if (_undo.Count == 0) return "";
            OutlinerUndoRecord r = _undo[_undo.Count - 1];
            return r != null ? r.Label : "";
        }

        internal void Push(string key, string label, string before, string after)
        {
            float now = 0f;
            try { now = Time.realtimeSinceStartup; } catch { now = 0f; }
            PushAt(key, label, before, after, now);
        }

        internal void PushAt(string key, string label, string before, string after, float now)
        {
            if (string.IsNullOrEmpty(key)) return;
            if (_undo.Count > 0)
            {
                OutlinerUndoRecord top = _undo[_undo.Count - 1];
                if (top != null
                    && string.Equals(top.Key, key, StringComparison.Ordinal)
                    && (now - top.Time) <= CoalesceSeconds)
                {
                    top.After = after ?? "";
                    top.Time = now;
                    _redo.Clear();
                    return;
                }
            }

            var rec = new OutlinerUndoRecord();
            rec.Key = key;
            rec.Label = label ?? "";
            rec.Before = before ?? "";
            rec.After = after ?? "";
            rec.Time = now;
            _undo.Add(rec);
            while (_undo.Count > MaxRecords)
                _undo.RemoveAt(0);
            _redo.Clear();
        }

        internal OutlinerUndoRecord Undo()
        {
            if (_undo.Count == 0) return null;
            int last = _undo.Count - 1;
            OutlinerUndoRecord rec = _undo[last];
            _undo.RemoveAt(last);
            _redo.Add(rec);
            return rec;
        }

        internal OutlinerUndoRecord Redo()
        {
            if (_redo.Count == 0) return null;
            int last = _redo.Count - 1;
            OutlinerUndoRecord rec = _redo[last];
            _redo.RemoveAt(last);
            _undo.Add(rec);
            return rec;
        }

        internal void Clear()
        {
            _undo.Clear();
            _redo.Clear();
        }
    }
}
