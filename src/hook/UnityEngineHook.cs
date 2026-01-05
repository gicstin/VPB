using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VPB
{
    public static class UnityEngineHook
    {
        // Cache for GameObject.Find
        private static Dictionary<string, GameObject> _goFindCache = new Dictionary<string, GameObject>();
        private static bool _initialized = false;

        // Debounce storage for Mesh operations
        // Key: Mesh Instance ID, Value: Last execution time
        private static Dictionary<int, float> _meshRecalcDebounce = new Dictionary<int, float>();
        private const float MIN_RECALC_INTERVAL = 0.033f; // Limit to ~30 times per second per mesh

        // Stats
        private static int _statFindHits;
        private static int _statFindCalls;
        private static int _statRaycastHits;
        private static int _statRaycastCalls;
        private static int _statMeshNormalsSkipped;
        private static int _statMeshNormalsCalled;
        private static int _statMeshBoundsSkipped;
        private static int _statMeshBoundsCalled;
        private static int _statMeshTangentsSkipped;
        private static int _statMeshTangentsCalled;
        private static float _lastStatLogTime;
        private const float STAT_LOG_INTERVAL = 10.0f;

        public static void Update()
        {
            if (Time.unscaledTime - _lastStatLogTime >= STAT_LOG_INTERVAL)
            {
                _lastStatLogTime = Time.unscaledTime;
                if (_statFindCalls > 0 || _statRaycastCalls > 0 || _statMeshNormalsCalled > 0 || _statMeshBoundsCalled > 0 || _statMeshTangentsCalled > 0)
                {
                    // Reset stats
                    _statFindHits = 0;
                    _statFindCalls = 0;
                    _statRaycastHits = 0;
                    _statRaycastCalls = 0;
                    _statMeshNormalsSkipped = 0;
                    _statMeshNormalsCalled = 0;
                    _statMeshTangentsSkipped = 0;
                    _statMeshTangentsCalled = 0;
                    _statMeshBoundsSkipped = 0;
                    _statMeshBoundsCalled = 0;
                }
            }
        }

        public static void Init()
        {
            if (_initialized) return;
            LogUtil.Log($"UnityEngineHook Initialized. Unity Version: {Application.unityVersion}");
            SceneManager.sceneLoaded += OnSceneLoaded;
            _initialized = true;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            _goFindCache.Clear();
            _meshRecalcDebounce.Clear();
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(GameObject), "Find")]
        public static bool GameObject_Find_Prefix(string name, ref GameObject __result)
        {
            if (Settings.Instance.OptimizeGameObjectFind != null && !Settings.Instance.OptimizeGameObjectFind.Value) return true;

            _statFindCalls++;
            if (string.IsNullOrEmpty(name)) return true;

            if (_goFindCache.TryGetValue(name, out GameObject cached))
            {
                // Verify object is still valid and active (Find only returns active objects)
                if (cached != null && cached.activeInHierarchy)
                {
                    __result = cached;
                    _statFindHits++;
                    return false; // Skip original
                }
                else
                {
                    // Invalid or inactive, remove from cache
                    _goFindCache.Remove(name);
                }
            }
            return true;
        }
        
        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameObject), "Find")]
        public static void GameObject_Find_Postfix(string name, GameObject __result)
        {
            if (Settings.Instance.OptimizeGameObjectFind != null && !Settings.Instance.OptimizeGameObjectFind.Value) return;

            if (__result != null && !string.IsNullOrEmpty(name))
            {
                // Only cache active objects (Find returns active objects)
                _goFindCache[name] = __result;
            }
        }

        // Hook for Mesh.RecalculateNormals
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Mesh), "RecalculateNormals", new Type[] { })]
        public static bool Mesh_RecalculateNormals_Prefix(Mesh __instance)
        {
            if (Settings.Instance.OptimizeMeshNormals != null && !Settings.Instance.OptimizeMeshNormals.Value) return true;

            _statMeshNormalsCalled++;
            // Debounce: Skip if called too recently
            int id = __instance.GetInstanceID();
            float lastTime;
            if (_meshRecalcDebounce.TryGetValue(id, out lastTime))
            {
                if (Time.time - lastTime < MIN_RECALC_INTERVAL)
                {
                    _statMeshNormalsSkipped++;
                    return false; // Skip execution
                }
            }
            _meshRecalcDebounce[id] = Time.time;
            return true; 
        }

        // Hook for Mesh.RecalculateBounds
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Mesh), "RecalculateBounds", new Type[] { })]
        public static bool Mesh_RecalculateBounds_Prefix(Mesh __instance)
        {
            if (Settings.Instance.OptimizeMeshBounds != null && !Settings.Instance.OptimizeMeshBounds.Value) return true;

            _statMeshBoundsCalled++;
            // Debounce: Skip if called too recently
            int id = __instance.GetInstanceID();
            float lastTime;
            if (_meshRecalcDebounce.TryGetValue(id, out lastTime))
            {
                if (Time.time - lastTime < MIN_RECALC_INTERVAL)
                {
                    _statMeshBoundsSkipped++;
                    return false; // Skip execution
                }
            }
            _meshRecalcDebounce[id] = Time.time;
            return true;
        }

        // Hook for Mesh.RecalculateTangents
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Mesh), "RecalculateTangents", new Type[] { })]
        public static bool Mesh_RecalculateTangents_Prefix(Mesh __instance)
        {
            if (Settings.Instance.OptimizeMeshTangents != null && !Settings.Instance.OptimizeMeshTangents.Value) return true;

            _statMeshTangentsCalled++;
            // Debounce: Skip if called too recently
            int id = __instance.GetInstanceID();
            float lastTime;
            if (_meshRecalcDebounce.TryGetValue(id, out lastTime))
            {
                if (Time.time - lastTime < MIN_RECALC_INTERVAL)
                {
                    _statMeshTangentsSkipped++;
                    return false; // Skip execution
                }
            }
            _meshRecalcDebounce[id] = Time.time;
            return true;
        }

        // Raycast Cache
        struct RaycastKey : IEquatable<RaycastKey>
        {
            public Vector3 origin;
            public Vector3 direction;
            public float maxDistance;
            public int layerMask;

            public bool Equals(RaycastKey other)
            {
                // Simple equality check
                return origin == other.origin && direction == other.direction && maxDistance == other.maxDistance && layerMask == other.layerMask;
            }
            public override int GetHashCode()
            {
                int hash = 17;
                hash = hash * 23 + origin.GetHashCode();
                hash = hash * 23 + direction.GetHashCode();
                hash = hash * 23 + maxDistance.GetHashCode();
                hash = hash * 23 + layerMask;
                return hash;
            }
        }

        struct RaycastResult
        {
            public bool hit;
            public RaycastHit info;
        }

        private static Dictionary<RaycastKey, RaycastResult> _raycastCache = new Dictionary<RaycastKey, RaycastResult>();
        private static int _lastRaycastFrame = -1;

        // Hook for Physics.Raycast
        // Note: Raycast has many overloads. Patching them all requires multiple patches.
        // This is an example for the most common one.
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Physics), "Raycast", new Type[] { typeof(Ray), typeof(RaycastHit), typeof(float), typeof(int) }, new ArgumentType[] { ArgumentType.Normal, ArgumentType.Out, ArgumentType.Normal, ArgumentType.Normal })]
        public static bool Physics_Raycast_Prefix(Ray ray, out RaycastHit hitInfo, float maxDistance, int layerMask, ref bool __result)
        {
            if (Settings.Instance.OptimizePhysicsRaycast != null && !Settings.Instance.OptimizePhysicsRaycast.Value)
            {
                hitInfo = default(RaycastHit);
                return true;
            }

            _statRaycastCalls++;
            // Frame-based Caching: Clear cache if new frame
            if (Time.frameCount != _lastRaycastFrame)
            {
                _raycastCache.Clear();
                _lastRaycastFrame = Time.frameCount;
            }

            var key = new RaycastKey { origin = ray.origin, direction = ray.direction, maxDistance = maxDistance, layerMask = layerMask };
            if (_raycastCache.TryGetValue(key, out var res))
            {
                hitInfo = res.info;
                __result = res.hit;
                _statRaycastHits++;
                return false; // Skip original execution (Optimization)
            }
            
            hitInfo = default(RaycastHit);
            return true;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Physics), "Raycast", new Type[] { typeof(Ray), typeof(RaycastHit), typeof(float), typeof(int) }, new ArgumentType[] { ArgumentType.Normal, ArgumentType.Out, ArgumentType.Normal, ArgumentType.Normal })]
        public static void Physics_Raycast_Postfix(Ray ray, ref RaycastHit hitInfo, float maxDistance, int layerMask, bool __result)
        {
            if (Settings.Instance.OptimizePhysicsRaycast != null && !Settings.Instance.OptimizePhysicsRaycast.Value) return;

            // Store result in cache for future calls this frame
            var key = new RaycastKey { origin = ray.origin, direction = ray.direction, maxDistance = maxDistance, layerMask = layerMask };
            if (!_raycastCache.ContainsKey(key))
            {
                _raycastCache[key] = new RaycastResult { hit = __result, info = hitInfo };
            }
        }
    }
}
