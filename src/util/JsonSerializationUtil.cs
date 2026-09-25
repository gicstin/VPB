using System.Text;
using SimpleJSON;

namespace VPB.src.util
{
    // SimpleJSON's string-returning ToString() overloads are O(N^2): each '+' on the running string allocates a fresh copy.
    internal static class JsonSerializationUtil
    {
        public static string Serialize(JSONNode node, int initialCapacity)
        {
            if (node == null) return string.Empty;
            // Pre-size buffer to avoid repeated growth on large scene saves.
            StringBuilder sb = new StringBuilder(initialCapacity);
            node.ToString(string.Empty, sb);
            return sb.ToString();
        }
    }
}
