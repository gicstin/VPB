using AssetBundles;
using HarmonyLib;
using MeshVR;
using SimpleJSON;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace VPB.src.util
{
    public static class JSONExtensions
    {
        public static IEnumerable<JSONNode> AsEnumerable(this JSONArray arr)
        {
            for (int i = 0; i < arr.Count; i++)
            {
                yield return arr[i];
            }
        }

        public static List<string> GetAtomIds(this JSONClass cls, bool onlyPersonAtoms)
        {
            var ids = new List<string>();
            if (!cls.HasKey("atoms")) return ids;
            var atoms = cls["atoms"].AsArray;
            if (atoms.Count == 0) return ids;

            foreach (JSONClass atom in atoms)
            {
                if (!onlyPersonAtoms || atom["type"].Value == "Person") ids.Add(atom.GetId());
            }

            return ids;
        }

        /// <summary>
        /// Remove any non-Person atoms from the "atoms" array, if there is one
        /// </summary>
        public static JSONClass RemoveNonPersonAtomsMutable(this JSONClass cls)
        {
            if (cls.HasKey("atoms"))
            {
                var atoms = cls["atoms"].AsArray;
                var personAtoms = new JSONArray();
                foreach (JSONClass atom in atoms)
                {
                    if (atom["type"].Value == "Person")
                    {
                        personAtoms.Add(atom);
                    }
                }
                cls["atoms"] = personAtoms;
            }
            return cls;
        }

        /// <summary>
        /// Replaces literal <c>SELF:</c> VaM path prefixes with <paramref name="packageUid"/> without
        /// serializing/reparsing the entire JSON. Mocap/timeline saves are huge; <see cref="JSONClass.ToString"/>
        /// + <see cref="JSON.Parse"/> duplicates multi‑MB buffers and fragments Mono (“too many heap sections”).
        /// </summary>
        public static void ReplaceSelfPrefixWithPackageUidMutable(JSONNode root, string packageUid)
        {
            if (root == null || string.IsNullOrEmpty(packageUid)) return;

            ReplaceSelfPrefixWalk(root, packageUid + ":");
        }

        static void ReplaceSelfPrefixWalk(JSONNode node, string replacementPrefix)
        {
            if (node == null) return;

            // JSONArray MUST be handled before JSONClass: some SimpleJSON forks derive JSONArray from JSONClass;
            // treating arrays as objects corrupts traversal (wrong keys / hung walks on mocap-timeline JSON).
            JSONArray ja = node as JSONArray;
            if (ja != null)
            {
                for (int i = 0; i < ja.Count; i++)
                    ReplaceSelfPrefixWalk(ja[i], replacementPrefix);
                return;
            }

            JSONClass jc = node as JSONClass;
            if (jc != null)
            {
                foreach (string key in jc.Keys)
                    ReplaceSelfPrefixWalk(jc[key], replacementPrefix);
                return;
            }

            try
            {
                string v = node.Value;
                if (!string.IsNullOrEmpty(v) && v.IndexOf("SELF:", StringComparison.Ordinal) >= 0)
                    node.Value = v.Replace("SELF:", replacementPrefix);
            }
            catch
            {
            }
        }

        /// <summary>
        /// Return the storable with the given id from the "storables" array, or null if not found
        /// </summary>
        /// <param name="id">The ID of the storable</param>
        /// <returns>The storable with the given ID, or null if not found, or if the class has no storables</returns>
        public static JSONClass GetStorable(this JSONClass cls, string id)
        {
            return cls["storables"]
                ?.AsArray
                .Cast<JSONClass>()
                .AsEnumerable()
                ?.FirstOrDefault(s => s.GetId() == id)
                ?.AsObject;
        }

        /// <summary>
        /// Fetch the "id" field of a JSONClass, or null if it doesn't exist
        /// </summary>
        public static string GetId(this JSONClass cls)
        {
            return cls.HasKey("id") ? cls["id"].Value : null;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="cls"></param>
        /// <returns></returns>
        public static SimpleTransform GetTransform(this JSONClass cls)
        {
            if (cls.HasKey("rootPosition") && cls.HasKey("rootRotation"))
            {
                var transform = SimpleTransform.FromJson(cls, positionKey: "rootPosition", rotationKey: "rootRotation");

                //if (cls.HasKey("relativeRootPosition") && cls.HasKey("relativeRootRotation"))
                //{
                //    return transform.Combine(Transform.FromJson(cls, positionKey: "relativeRootPosition", rotationKey: "relativeRootRotation"));
                //}

                return transform;
            }
            else
            {
                if (cls.HasKey("position") && cls.HasKey("rotation"))
                {
                    var transform = SimpleTransform.FromJson(cls);

                    if (cls.HasKey("containerPosition") && cls.HasKey("containerRotation"))
                    {
                        return transform.TransformPoint(SimpleTransform.FromJson(cls, positionKey: "containerPosition", rotationKey: "containerRotation"));
                    }

                    return transform;
                }
            }

            return new SimpleTransform();
        }

        private static bool _hasInitCharacterGenderMap = false;
        private static bool _characterGenderMapInitCompleted = false;
        private static bool _characterGenderMapLoadRequested = false;
        private static bool _characterGenderMapPendingLogged = false;
        public static Dictionary<string, string> CharacterGenderMap = new Dictionary<string, string>();
        private const int CharacterMapBuildYieldBatch = 64;

        private struct CharacterGenderFlagsJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<int> InputMaleFlags;
            [WriteOnly] public NativeArray<int> OutputGenderFlags;

            public void Execute(int index)
            {
                // Normalize any non-zero truthy values to 1 for cheap branchless consumption later.
                OutputGenderFlags[index] = InputMaleFlags[index] != 0 ? 1 : 0;
            }
        }

        public static bool IsCharacterGenderMapInitComplete()
        {
            return _hasInitCharacterGenderMap || _characterGenderMapInitCompleted;
        }

        public static void RequestCharacterGenderMapLoad()
        {
            if (_hasInitCharacterGenderMap || _characterGenderMapInitCompleted) return;
            if (_characterGenderMapLoadRequested) return;

            var gallery = global::VPB.Gallery.singleton;
            if (gallery == null) return;

            _characterGenderMapLoadRequested = true;
            gallery.StartCoroutine(LoadCharacterGenderMap());
            LogUtil.Log("[VPB] Character gender map lazy-load scheduled");
        }

        public static IEnumerator LoadCharacterGenderMap()
        {
            if (_hasInitCharacterGenderMap) yield break;
            _characterGenderMapLoadRequested = true;
            VamStartupProfiler.Milestone("gender_map_async_begin");
            var timer = new Stopwatch();
            timer.Start();
            object load = null;
            GameObject prefab = null;
            try
            {
                load = AssetBundleManager.LoadAssetAsync("a_per", "assets/vamassets/prefabs/people/person.prefab", typeof(GameObject));
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning("[VPB] Deferred character gender map load (asset manager not ready): " + ex.Message);
                _characterGenderMapInitCompleted = true;
                yield break;
            }

            if (load == null)
            {
                LogUtil.LogWarning("[VPB] Character gender map load skipped: async request was null");
                _characterGenderMapInitCompleted = true;
                yield break;
            }

            MethodInfo isDoneMethod = load.GetType().GetMethod("IsDone", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            if (isDoneMethod == null)
            {
                LogUtil.LogWarning("[VPB] Character gender map load failed: IsDone() not found on async request");
                _characterGenderMapInitCompleted = true;
                yield break;
            }
            while (true)
            {
                bool done = false;
                try
                {
                    object doneObj = isDoneMethod.Invoke(load, null);
                    if (doneObj is bool b) done = b;
                }
                catch (Exception ex)
                {
                    LogUtil.LogWarning("[VPB] Character gender map load failed while polling IsDone(): " + ex.Message);
                    _characterGenderMapInitCompleted = true;
                    yield break;
                }
                if (done) break;
                yield return null;
            }

            try
            {
                MethodInfo getAssetMethod = load.GetType().GetMethod("GetAsset", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (getAssetMethod == null || !getAssetMethod.IsGenericMethodDefinition)
                {
                    LogUtil.LogWarning("[VPB] Character gender map load failed: GetAsset<T>() not found on async request");
                    _characterGenderMapInitCompleted = true;
                    yield break;
                }
                MethodInfo getAssetGameObject = getAssetMethod.MakeGenericMethod(typeof(GameObject));
                object prefabObj = getAssetGameObject.Invoke(load, null);
                prefab = prefabObj as GameObject;
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning("[VPB] Character gender map load failed while resolving prefab: " + ex.Message);
                _characterGenderMapInitCompleted = true;
                yield break;
            }
            if (prefab == null)
            {
                LogUtil.LogWarning("[VPB] Character gender map load failed: person prefab was null");
                _characterGenderMapInitCompleted = true;
                yield break;
            }

            var selector = prefab.GetComponentInChildren<DAZCharacterSelector>();

            if (selector != null)
            {
                var allCharacters = new List<DAZCharacter>();
                if (selector.femaleCharactersPrefab != null)
                    allCharacters.AddRange(selector.femaleCharactersPrefab.GetComponentsInChildren<DAZCharacter>(true));
                if (selector.maleCharactersPrefab != null)
                    allCharacters.AddRange(selector.maleCharactersPrefab.GetComponentsInChildren<DAZCharacter>(true));

                int count = allCharacters.Count;
                var inputFlags = new NativeArray<int>(count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                var outputFlags = new NativeArray<int>(count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                try
                {
                    for (int i = 0; i < count; i++)
                        inputFlags[i] = allCharacters[i].isMale ? 1 : 0;

                    var flagsJob = new CharacterGenderFlagsJob
                    {
                        InputMaleFlags = inputFlags,
                        OutputGenderFlags = outputFlags
                    };
                    JobHandle flagsHandle = flagsJob.Schedule(count, 64);
                    flagsHandle.Complete();

                    if (CharacterGenderMap.Count > 0)
                        CharacterGenderMap.Clear();

                    var loadedMale = 0;
                    var loadedFemale = 0;
                    for (int i = 0; i < count; i++)
                    {
                        var character = allCharacters[i];
                        if (character == null) continue;
                        if (string.IsNullOrEmpty(character.displayName)) continue;

                        if (CharacterGenderMap.ContainsKey(character.displayName))
                        {
                            LogUtil.LogWarning("Skipping registration of duplicate character " + character.displayName + " (" + character.displayNameAlt + ")");
                            continue;
                        }

                        bool isMale = outputFlags[i] == 1;
                        CharacterGenderMap[character.displayName] = isMale ? "Male" : "Female";
                        if (isMale) loadedMale++;
                        else loadedFemale++;

                        if ((i & (CharacterMapBuildYieldBatch - 1)) == 0)
                            yield return null;
                    }

                    _hasInitCharacterGenderMap = true;
                    LogUtil.Log("Loaded " + loadedFemale.ToString() + " female characters, " + loadedMale.ToString() + " male characters");
                }
                finally
                {
                    if (outputFlags.IsCreated) outputFlags.Dispose();
                    if (inputFlags.IsCreated) inputFlags.Dispose();
                }
            }
            else
            {
                LogUtil.LogError("Failed to initialize character gender mapping (could not locate DAZCharacterSelector)");
            }
            _characterGenderMapInitCompleted = true;
            timer.Stop();
            try { VamStartupProfiler.AddPhaseMs("gender_map_load", timer.Elapsed.TotalMilliseconds); } catch { }
            try { VamStartupProfiler.Milestone("gender_map_async_done ms=" + timer.ElapsedMilliseconds); } catch { }
            LogUtil.Log("Initialized character gender map in " + timer.ElapsedMilliseconds + "ms");
        }

        public static string GetGenderForCharacter(string characterName)
        {
            if (!_hasInitCharacterGenderMap)
            {
                RequestCharacterGenderMapLoad();
                if (!_characterGenderMapPendingLogged)
                {
                    _characterGenderMapPendingLogged = true;
                    LogUtil.Log("[VPB] Character gender map not ready yet; using temporary Neutral fallback");
                }
                return "Neutral";
            }

            if (!CharacterGenderMap.TryGetValue(characterName, out string gender) || gender == null)
            {
                LogUtil.LogWarning("Failed to determine gender for " + characterName);
                return "Neutral";
            }
            
            return gender;
        }
    }

    public enum TransformType
    {
        Root,
        RelativeRoot,
        Local,
        Container,
    }
}
