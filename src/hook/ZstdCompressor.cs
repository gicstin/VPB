using System;
using System.IO;
using ZstdNet;

namespace VPB
{
    public class ZstdCompressor
    {
        /// <summary>
        /// Compresses the given data using Zstd compression.
        /// </summary>
        /// <param name="data">The raw data to compress.</param>
        /// <param name="level">The compression level (typically 1-22).</param>
        /// <returns>The compressed byte array.</returns>
        public static byte[] Compress(byte[] data, int level)
        {
            if (data == null || data.Length == 0)
                return new byte[0];

            // Explicitly initialize to ensure DLL is found/loaded before CompressionOptions static ctor runs
            try { ExternMethods.Initialize(); } catch { }

            using (var compressor = new Compressor(new CompressionOptions(level)))
            {
                return compressor.Wrap(data);
            }
        }

        /// <summary>
        /// Decompresses the given Zstd-compressed data.
        /// </summary>
        /// <param name="compressed">The compressed data.</param>
        /// <returns>The original uncompressed byte array.</returns>
        public static byte[] Decompress(byte[] compressed)
        {
            if (compressed == null || compressed.Length == 0)
                return new byte[0];

            try { ExternMethods.Initialize(); } catch { }

            using (var decompressor = new Decompressor())
            {
                return decompressor.Unwrap(compressed);
            }
        }

        /// <summary>
        /// Compresses the provided data (e.g., DXT texture data) and saves it to the specified file path.
        /// </summary>
        /// <param name="path">The full path where the cache file will be saved.</param>
        /// <param name="data">The raw data to compress and save.</param>
        /// <param name="level">The compression level to use.</param>
        public static void SaveCache(string path, byte[] data, int level)
        {
            if (data == null)
            {
                // If there's no data, we probably shouldn't create a file, or create an empty one.
                // Mimicking behavior of Compress which returns empty array.
                File.WriteAllBytes(path, new byte[0]);
                return;
            }

            // Perform compression
            byte[] compressed = Compress(data, level);

            // Write to disk
            File.WriteAllBytes(path, compressed);
        }

        /// <summary>
        /// Reads a compressed cache file from disk and decompresses it back to raw data (e.g., DXT texture).
        /// </summary>
        /// <param name="path">The full path to the cache file.</param>
        /// <returns>The decompressed data, or null if the file does not exist.</returns>
        public static byte[] LoadCache(string path)
        {
            if (!File.Exists(path))
            {
                return null;
            }

            // Read compressed bytes
            byte[] compressed = File.ReadAllBytes(path);

            // Decompress
            return Decompress(compressed);
        }

        /// <summary>
        /// Helper to ensure the compressor is initialized. With ZstdNet, this is handled by the library,
        /// but keeping the method signature for compatibility if needed by other callers (though existing callers seem to access static methods directly).
        /// </summary>
        private static void EnsureInitialized()
        {
            // No-op for ZstdNet implementation as it initializes on demand/static ctor.
        }
    }
}
