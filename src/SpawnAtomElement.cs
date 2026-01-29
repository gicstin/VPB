using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace VPB
{
    public static class SpawnAtomElement
    {
        private static Collider s_FloorCollider;
        private static bool s_FloorColliderSearched;

        public sealed class SpawnSuppressionHandle
        {
            internal Atom Atom;
            internal CollisionToggleState CollisionState;
            internal RendererToggleState RendererState;
            internal PhysicsToggleState PhysicsState;

            public void Restore()
            {
                try { RestorePhysics(PhysicsState); } catch { }
                try { RestoreCollisions(CollisionState); } catch { }
                try { RestoreRenderers(RendererState); } catch { }
            }
        }

        internal sealed class CollisionToggleState
        {
            public readonly List<Collider> DisabledColliders = new List<Collider>();
            public readonly List<CharacterController> DisabledCharacterControllers = new List<CharacterController>();
            public readonly List<Rigidbody> DisabledRigidbodies = new List<Rigidbody>();
        }

        internal sealed class PhysicsToggleState
        {
            public readonly List<Rigidbody> FrozenRigidbodies = new List<Rigidbody>();
            public readonly List<bool> PrevIsKinematic = new List<bool>();
        }

        internal sealed class RendererToggleState
        {
            public readonly List<Renderer> DisabledRenderers = new List<Renderer>();
        }

        private static void DisableCollisions(GameObject root, CollisionToggleState state)
        {
            if (root == null || state == null) return;

            try
            {
                var colliders = root.GetComponentsInChildren<Collider>(true);
                if (colliders != null)
                {
                    for (int i = 0; i < colliders.Length; i++)
                    {
                        var c = colliders[i];
                        if (c == null) continue;
                        if (!c.enabled) continue;
                        c.enabled = false;
                        state.DisabledColliders.Add(c);
                    }
                }
            }
            catch { }

            try
            {
                var ccs = root.GetComponentsInChildren<CharacterController>(true);
                if (ccs != null)
                {
                    for (int i = 0; i < ccs.Length; i++)
                    {
                        var cc = ccs[i];
                        if (cc == null) continue;
                        if (!cc.enabled) continue;
                        cc.enabled = false;
                        state.DisabledCharacterControllers.Add(cc);
                    }
                }
            }
            catch { }

            try
            {
                var rbs = root.GetComponentsInChildren<Rigidbody>(true);
                if (rbs != null)
                {
                    for (int i = 0; i < rbs.Length; i++)
                    {
                        var rb = rbs[i];
                        if (rb == null) continue;
                        if (!rb.detectCollisions) continue;
                        rb.detectCollisions = false;
                        state.DisabledRigidbodies.Add(rb);
                    }
                }
            }
            catch { }
        }

        private static void FreezePhysics(GameObject root, PhysicsToggleState state)
        {
            if (root == null || state == null) return;

            try
            {
                var rbs = root.GetComponentsInChildren<Rigidbody>(true);
                if (rbs != null)
                {
                    for (int i = 0; i < rbs.Length; i++)
                    {
                        var rb = rbs[i];
                        if (rb == null) continue;
                        state.FrozenRigidbodies.Add(rb);
                        state.PrevIsKinematic.Add(rb.isKinematic);

                        try { rb.velocity = Vector3.zero; } catch { }
                        try { rb.angularVelocity = Vector3.zero; } catch { }
                        try { rb.isKinematic = true; } catch { }
                        try { rb.Sleep(); } catch { }
                    }
                }
            }
            catch { }
        }

        private static void RestorePhysics(PhysicsToggleState state)
        {
            if (state == null) return;
            try
            {
                int n = state.FrozenRigidbodies.Count;
                for (int i = 0; i < n; i++)
                {
                    var rb = state.FrozenRigidbodies[i];
                    if (rb == null) continue;
                    bool prevKinematic = (i < state.PrevIsKinematic.Count) ? state.PrevIsKinematic[i] : rb.isKinematic;
                    try { rb.isKinematic = prevKinematic; } catch { }
                }
            }
            catch { }
        }

        private static void RestoreCollisions(CollisionToggleState state)
        {
            if (state == null) return;

            try
            {
                for (int i = 0; i < state.DisabledColliders.Count; i++)
                {
                    var c = state.DisabledColliders[i];
                    if (c != null) c.enabled = true;
                }
            }
            catch { }

            try
            {
                for (int i = 0; i < state.DisabledCharacterControllers.Count; i++)
                {
                    var cc = state.DisabledCharacterControllers[i];
                    if (cc != null) cc.enabled = true;
                }
            }
            catch { }

            try
            {
                for (int i = 0; i < state.DisabledRigidbodies.Count; i++)
                {
                    var rb = state.DisabledRigidbodies[i];
                    if (rb != null) rb.detectCollisions = true;
                }
            }
            catch { }
        }

        private static void DisableRenderers(GameObject root, RendererToggleState state)
        {
            if (root == null || state == null) return;

            try
            {
                var rs = root.GetComponentsInChildren<Renderer>(true);
                if (rs != null)
                {
                    for (int i = 0; i < rs.Length; i++)
                    {
                        var r = rs[i];
                        if (r == null) continue;
                        if (!r.enabled) continue;
                        r.enabled = false;
                        state.DisabledRenderers.Add(r);
                    }
                }
            }
            catch { }
        }

        private static void RestoreRenderers(RendererToggleState state)
        {
            if (state == null) return;
            try
            {
                for (int i = 0; i < state.DisabledRenderers.Count; i++)
                {
                    var r = state.DisabledRenderers[i];
                    if (r != null) r.enabled = true;
                }
            }
            catch { }
        }

        private static Atom FindNewAtomByType(SuperController sc, string type, HashSet<string> before)
        {
            if (sc == null || string.IsNullOrEmpty(type) || before == null) return null;

            try
            {
                foreach (var a in sc.GetAtoms())
                {
                    if (a == null) continue;
                    if (a.type != type) continue;
                    if (string.IsNullOrEmpty(a.uid)) continue;
                    if (!before.Contains(a.uid)) return a;
                }
            }
            catch { }

            return null;
        }

        private static HashSet<string> SnapshotAtomUids(SuperController sc)
        {
            var set = new HashSet<string>();
            if (sc == null) return set;

            try
            {
                foreach (var a in sc.GetAtoms())
                {
                    if (a != null && !string.IsNullOrEmpty(a.uid)) set.Add(a.uid);
                }
            }
            catch { }

            return set;
        }

        private static void EnsureFloorCollider()
        {
            if (s_FloorColliderSearched) return;
            s_FloorColliderSearched = true;

            try
            {
                GameObject go = GameObject.Find("Floor");
                if (go != null)
                {
                    s_FloorCollider = go.GetComponent<Collider>();
                    if (s_FloorCollider != null) return;
                }
            }
            catch { }

            try
            {
                Collider[] cs = GameObject.FindObjectsOfType<Collider>();
                if (cs == null) return;
                for (int i = 0; i < cs.Length; i++)
                {
                    Collider c = cs[i];
                    if (c == null) continue;
                    string n = null;
                    try { n = c.name; } catch { }
                    if (string.IsNullOrEmpty(n)) continue;
                    if (string.Equals(n, "Floor", StringComparison.OrdinalIgnoreCase))
                    {
                        s_FloorCollider = c;
                        return;
                    }
                }
            }
            catch { }
        }

        private static Vector3 SnapToFloor(Vector3 position)
        {
            EnsureFloorCollider();

            try
            {
                if (s_FloorCollider != null)
                {
                    Ray ray = new Ray(new Vector3(position.x, 1000f, position.z), Vector3.down);
                    RaycastHit hit;
                    if (s_FloorCollider.Raycast(ray, out hit, 2000f))
                    {
                        position.y = hit.point.y;
                        return position;
                    }
                }
            }
            catch { }

            position.y = 0f;
            return position;
        }

        public static bool TryRaycastFloor(Ray ray, out Vector3 point)
        {
            point = Vector3.zero;
            EnsureFloorCollider();

            try
            {
                if (s_FloorCollider == null) return false;
                RaycastHit hit;
                if (!s_FloorCollider.Raycast(ray, out hit, 2000f)) return false;
                point = hit.point;
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static IEnumerator SpawnPersonAtFloor(Vector3 position, Action<Atom> onSpawned)
        {
            Vector3 p = SnapToFloor(position);
            yield return SpawnPersonAtPosition(p, onSpawned);
        }

        public static IEnumerator SpawnPersonAtFloorSuppressed(Vector3 position, Action<Atom, SpawnSuppressionHandle> onSpawned)
        {
            Vector3 p = SnapToFloor(position);
            yield return SpawnPersonAtPositionCore(p, true, onSpawned);
        }

        public static IEnumerator SpawnPersonAtPosition(Vector3 position, Action<Atom> onSpawned)
        {
            yield return SpawnPersonAtPositionCore(position, false, (a, h) => {
                if (onSpawned != null) onSpawned(a);
            });
        }

        private static void ApplySuppression(Atom atom, SpawnSuppressionHandle handle)
        {
            if (atom == null || handle == null) return;
            handle.Atom = atom;
            handle.RendererState = new RendererToggleState();
            handle.CollisionState = new CollisionToggleState();
            handle.PhysicsState = new PhysicsToggleState();

            try { DisableRenderers(atom.gameObject, handle.RendererState); } catch { }
            try { DisableCollisions(atom.gameObject, handle.CollisionState); } catch { }
            try { FreezePhysics(atom.gameObject, handle.PhysicsState); } catch { }
        }

        private static IEnumerator SpawnPersonAtPositionCore(Vector3 position, bool keepSuppressed, Action<Atom, SpawnSuppressionHandle> onSpawned)
        {
            SuperController sc = SuperController.singleton;
            if (sc == null) yield break;

            string uid = null;
            try { uid = "Person_" + Guid.NewGuid().ToString("N").Substring(0, 8); } catch { uid = "Person_" + UnityEngine.Random.Range(100000, 999999).ToString(); }
            Vector3 hiddenSpawnPos = new Vector3(position.x, position.y - 1000f, position.z);

            Atom prevSelected = null;
            try { prevSelected = sc.GetSelectedAtom(); } catch { }

            HashSet<string> before = SnapshotAtomUids(sc);

            bool didSpawnViaPositionalOverload = false;
            IEnumerator positionalSpawnCoroutine = null;
            Atom positionalSpawnAtom = null;

            try
            {
                MethodInfo[] ms = sc.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                for (int i = 0; i < ms.Length && positionalSpawnCoroutine == null && positionalSpawnAtom == null; i++)
                {
                    MethodInfo mi = ms[i];
                    if (mi == null) continue;
                    if (!string.Equals(mi.Name, "AddAtomByType", StringComparison.Ordinal)) continue;

                    var ps = mi.GetParameters();
                    if (ps == null) continue;

                    bool hasVector3 = false;
                    for (int pi = 0; pi < ps.Length; pi++)
                    {
                        if (ps[pi] != null && ps[pi].ParameterType == typeof(Vector3)) { hasVector3 = true; break; }
                    }
                    if (!hasVector3) continue;

                    object[] args = new object[ps.Length];
                    bool ok = true;

                    for (int pi = 0; pi < ps.Length; pi++)
                    {
                        Type pt = ps[pi].ParameterType;
                        if (pi == 0 && pt == typeof(string)) { args[pi] = "Person"; continue; }
                        if (pi == 1 && pt == typeof(string)) { args[pi] = uid; continue; }

                        if (pt == typeof(Vector3)) { args[pi] = hiddenSpawnPos; continue; }
                        if (pt == typeof(Quaternion)) { args[pi] = Quaternion.identity; continue; }
                        if (pt == typeof(bool)) { args[pi] = false; continue; }
                        if (pt == typeof(int)) { args[pi] = 0; continue; }
                        if (pt == typeof(float)) { args[pi] = 0f; continue; }
                        if (pt == typeof(string)) { args[pi] = ""; continue; }

                        ok = false;
                        break;
                    }

                    if (!ok) continue;

                    object res = null;
                    try { res = mi.Invoke(sc, args); } catch { }
                    if (res is IEnumerator ie) positionalSpawnCoroutine = ie;
                    else if (res is Atom a) positionalSpawnAtom = a;
                }
            }
            catch { }

            SpawnSuppressionHandle handle = null;

            if (positionalSpawnCoroutine != null)
            {
                didSpawnViaPositionalOverload = true;
                Atom spawnedPerson = null;
                bool moved = false;

                while (positionalSpawnCoroutine.MoveNext())
                {
                    if (!moved)
                    {
                        try
                        {
                            spawnedPerson = FindNewAtomByType(sc, "Person", before);
                        }
                        catch { }

                        if (spawnedPerson != null)
                        {
                            handle = new SpawnSuppressionHandle();
                            ApplySuppression(spawnedPerson, handle);

                            try { spawnedPerson.transform.position = hiddenSpawnPos; } catch { }
                            try
                            {
                                if (spawnedPerson.mainController != null) spawnedPerson.mainController.transform.position = hiddenSpawnPos;
                            }
                            catch { }

                            moved = true;
                        }
                    }

                    yield return positionalSpawnCoroutine.Current;
                }

                if (handle != null && !keepSuppressed)
                {
                    handle.Restore();
                }
            }
            else if (positionalSpawnAtom != null)
            {
                didSpawnViaPositionalOverload = true;

                handle = new SpawnSuppressionHandle();
                ApplySuppression(positionalSpawnAtom, handle);

                try { positionalSpawnAtom.transform.position = hiddenSpawnPos; } catch { }
                try
                {
                    if (positionalSpawnAtom.mainController != null) positionalSpawnAtom.mainController.transform.position = hiddenSpawnPos;
                }
                catch { }

                yield return new WaitForEndOfFrame();

                if (handle != null && !keepSuppressed)
                {
                    handle.Restore();
                }
            }

            Atom earlyPerson = null;
            bool earlyMoved = false;

            try
            {
                if (!didSpawnViaPositionalOverload)
                {
                    try
                    {
                        Type t = sc.GetType();

                        FieldInfo[] fs = null;
                        try { fs = t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic); } catch { }
                        if (fs != null)
                        {
                            for (int i = 0; i < fs.Length; i++)
                            {
                                FieldInfo f = fs[i];
                                if (f == null) continue;
                                string n = f.Name;
                                if (string.IsNullOrEmpty(n)) continue;

                                bool looksRelevant = false;
                                if (n.IndexOf("addAtom", StringComparison.OrdinalIgnoreCase) >= 0) looksRelevant = true;
                                if (n.IndexOf("add", StringComparison.OrdinalIgnoreCase) >= 0 && n.IndexOf("pos", StringComparison.OrdinalIgnoreCase) >= 0) looksRelevant = true;
                                if (n.IndexOf("spawn", StringComparison.OrdinalIgnoreCase) >= 0 && n.IndexOf("pos", StringComparison.OrdinalIgnoreCase) >= 0) looksRelevant = true;
                                if (n.IndexOf("new", StringComparison.OrdinalIgnoreCase) >= 0 && n.IndexOf("atom", StringComparison.OrdinalIgnoreCase) >= 0 && n.IndexOf("pos", StringComparison.OrdinalIgnoreCase) >= 0) looksRelevant = true;
                                if (!looksRelevant) continue;

                                try
                                {
                                    if (f.FieldType == typeof(Vector3)) f.SetValue(sc, hiddenSpawnPos);
                                    else if (typeof(Transform).IsAssignableFrom(f.FieldType))
                                    {
                                        Transform tr = f.GetValue(sc) as Transform;
                                        if (tr != null) tr.position = hiddenSpawnPos;
                                    }
                                }
                                catch { }
                            }
                        }

                        PropertyInfo[] ps = null;
                        try { ps = t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic); } catch { }
                        if (ps != null)
                        {
                            for (int i = 0; i < ps.Length; i++)
                            {
                                PropertyInfo p = ps[i];
                                if (p == null) continue;
                                if (!p.CanWrite) continue;
                                string n = p.Name;
                                if (string.IsNullOrEmpty(n)) continue;

                                bool looksRelevant = false;
                                if (n.IndexOf("addAtom", StringComparison.OrdinalIgnoreCase) >= 0) looksRelevant = true;
                                if (n.IndexOf("add", StringComparison.OrdinalIgnoreCase) >= 0 && n.IndexOf("pos", StringComparison.OrdinalIgnoreCase) >= 0) looksRelevant = true;
                                if (n.IndexOf("spawn", StringComparison.OrdinalIgnoreCase) >= 0 && n.IndexOf("pos", StringComparison.OrdinalIgnoreCase) >= 0) looksRelevant = true;
                                if (n.IndexOf("new", StringComparison.OrdinalIgnoreCase) >= 0 && n.IndexOf("atom", StringComparison.OrdinalIgnoreCase) >= 0 && n.IndexOf("pos", StringComparison.OrdinalIgnoreCase) >= 0) looksRelevant = true;
                                if (!looksRelevant) continue;

                                try
                                {
                                    if (p.PropertyType == typeof(Vector3)) p.SetValue(sc, hiddenSpawnPos, null);
                                    else if (typeof(Transform).IsAssignableFrom(p.PropertyType))
                                    {
                                        Transform tr = p.GetValue(sc, null) as Transform;
                                        if (tr != null) tr.position = hiddenSpawnPos;
                                    }
                                }
                                catch { }
                            }
                        }
                    }
                    catch { }

                    IEnumerator addIe = null;
                    try { addIe = sc.AddAtomByType("Person", uid, false, false, false); }
                    catch { addIe = null; }

                    while (addIe != null && addIe.MoveNext())
                    {
                        if (!earlyMoved)
                        {
                            if (earlyPerson == null)
                            {
                                earlyPerson = FindNewAtomByType(sc, "Person", before);
                                if (earlyPerson != null)
                                {
                                    handle = new SpawnSuppressionHandle();
                                    ApplySuppression(earlyPerson, handle);
                                    try { earlyPerson.transform.position = hiddenSpawnPos; } catch { }
                                    try
                                    {
                                        if (earlyPerson.mainController != null) earlyPerson.mainController.transform.position = hiddenSpawnPos;
                                    }
                                    catch { }
                                    earlyMoved = true;
                                }
                            }
                        }

                        yield return addIe.Current;
                    }
                }
            }
            finally
            {
                // Suppression/restore is managed via SpawnSuppressionHandle.
            }

            Atom newPerson = FindNewAtomByType(sc, "Person", before);
            if (newPerson == null)
            {
                yield break;
            }

            if (handle == null || handle.Atom != newPerson)
            {
                handle = new SpawnSuppressionHandle();
                ApplySuppression(newPerson, handle);
            }

            try { newPerson.transform.position = position; } catch { }

            try
            {
                if (newPerson.mainController != null)
                {
                    newPerson.mainController.transform.position = position;
                }
            }
            catch { }

            if (newPerson.mainController == null)
            {
                for (int i = 0; i < 60 && newPerson.mainController == null; i++)
                {
                    yield return new WaitForEndOfFrame();
                }

                try
                {
                    if (newPerson.mainController != null) newPerson.mainController.transform.position = position;
                }
                catch { }
            }

            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();

            try
            {
                if (prevSelected != null)
                {
                    MethodInfo selectAtom = sc.GetType().GetMethod("SelectAtom", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (selectAtom != null) selectAtom.Invoke(sc, new object[] { prevSelected });
                }
            }
            catch { }

            if (onSpawned != null)
            {
                try { onSpawned(newPerson, handle); } catch { }
            }

            if (handle != null && !keepSuppressed)
            {
                handle.Restore();
            }
        }
    }
}
