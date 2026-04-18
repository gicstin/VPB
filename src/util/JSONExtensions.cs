using SimpleJSON;
using System;
using System.Collections.Generic;
using System.Linq;
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

            foreach(JSONClass atom in atoms)
            {
                if (!onlyPersonAtoms || atom["type"].Value == "Person") atoms.Add(atom.GetId());
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
    }

    public enum TransformType
    {
        Root,
        RelativeRoot,
        Local,
        Container,
    }
}
