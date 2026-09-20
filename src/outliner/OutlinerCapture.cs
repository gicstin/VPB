using System;
using System.Collections.Generic;

namespace VPB.Outliner
{
    internal static class OutlinerCapture
    {
        internal static void CaptureAtoms(List<OutlinerAtomFacts> into)
        {
            into.Clear();
            SuperController sc = SuperController.singleton;
            if (sc == null) return;
            List<Atom> atoms = null;
            try { atoms = sc.GetAtoms(); } catch { return; }
            if (atoms == null) return;
            for (int i = 0; i < atoms.Count; i++)
            {
                OutlinerAtomFacts f = FromAtom(atoms[i]);
                if (f != null) into.Add(f);
            }
        }

        internal static OutlinerAtomFacts FromAtom(Atom atom)
        {
            if (atom == null) return null;
            var f = new OutlinerAtomFacts();
            try { f.Uid = atom.uid ?? ""; } catch { return null; }
            if (string.IsNullOrEmpty(f.Uid)) return null;
            try { f.Type = atom.type ?? ""; } catch { f.Type = ""; }
            f.DisplayName = f.Uid;
            f.IsSystemProtected = SceneUtils.IsSystemProtectedAtom(atom);
            CreatorStripKeepKind kind = SceneUtils.ClassifyCreatorStripKeepKind(atom);
            f.Kind = kind == CreatorStripKeepKind.None ? CreatorStripKeepKind.Other : kind;
            try { f.On = atom.on; } catch { f.On = true; }
            try { f.Hidden = atom.hidden; } catch { f.Hidden = false; }
            try { f.Collision = atom.collisionEnabled; } catch { f.Collision = true; }
            try
            {
                JSONStorableBool fr = OutlinerEdits.FreezePhysicsParam(atom);
                f.Physics = fr == null || !fr.val;
            }
            catch { f.Physics = true; }
            try
            {
                Atom parent = atom.parentAtom;
                f.ParentUid = parent != null ? parent.uid : "";
            }
            catch { f.ParentUid = ""; }
            try
            {
                if (atom.containingSubScene != null && atom.containingSubScene.containingAtom != null)
                    f.SubSceneUid = atom.containingSubScene.containingAtom.uid ?? "";
            }
            catch { f.SubSceneUid = ""; }

            return f;
        }
    }
}
