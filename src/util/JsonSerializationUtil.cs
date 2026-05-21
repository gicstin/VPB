using System.Text;
using SimpleJSON;

namespace VPB.src.util
{
    // SimpleJSON's string-returning ToString() overloads are O(N^2): each '+' on the running
    // string allocates a fresh copy. The (prefix, StringBuilder) overload streams into a
    // shared SB so the whole tree serializes in O(N).
    internal static class JsonSerializationUtil
    {
        public static string Serialize(JSONNode node, int initialCapacity)
        {
            if (node == null) return string.Empty;
            // Pre-size to expected payload ballpark: avoids ~log2(N) internal buffer doublings
            // (each a full backing-array copy) for large scene saves.
            StringBuilder sb = new StringBuilder(initialCapacity);
            node.ToString(string.Empty, sb);
            return sb.ToString();
        }
    }
}
