using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace VPB
{
    /// <summary>
    /// Loads the official SQLite native DLL (<c>sqlite3.dll</c>) with <see cref="LoadLibrary"/>, then binds entry points via <see cref="GetProcAddress"/>.
    /// Mono does not resolve <c>DllImport("sqlite3")</c> to a module loaded only by <c>LoadLibrary</c> (DllNotFoundException), so we never P/Invoke the short name.
    /// Do not place <c>sqlite3.dll</c> under <c>BepInEx\scripts</c> — VaM's Script Engine loads every .dll there as managed IL and will throw BadImageFormatException.
    /// Default deploy: <c>BepInEx\plugins\sqlite3.dll</c> under the VaM install folder (see PostBuild in csproj).
    /// </summary>
    internal static class VpbSqlite3
    {
        internal const int SqliteRow = 100;
        internal const int SqliteDone = 101;

        private const int SQLITE_OK = 0;
        private const int SQLITE_ROW = 100;
        private const int SQLITE_DONE = 101;
        private static readonly IntPtr SQLITE_TRANSIENT = new IntPtr(-1);

        private static bool loadAttempted;
        private static bool loadOk;
        private static string s_GameInstallRoot;
        /// <summary>Absolute <c>Cache\VPB</c> for the gallery index DB; set on main thread for use from workers (not the default sqlite3.dll location).</summary>
        private static string s_CacheVpbDirectory;
        private static bool s_LoggedSqliteLoadDetail;
        /// <summary>Module handle from <see cref="LoadLibrary"/>; kept for process lifetime once bound.</summary>
        private static IntPtr s_sqliteModule;

        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport("kernel32", SetLastError = true)]
        private static extern bool FreeLibrary(IntPtr hModule);

        [DllImport("kernel32", CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int D_sqlite3_open_v2(IntPtr filename, out IntPtr db, int flags, IntPtr vfs);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int D_sqlite3_close(IntPtr db);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int D_sqlite3_exec(IntPtr db, IntPtr sql, IntPtr callback, IntPtr arg, out IntPtr errMsg);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void D_sqlite3_free(IntPtr p);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int D_sqlite3_prepare_v2(IntPtr db, IntPtr sql, int nByte, out IntPtr stmt, out IntPtr tail);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int D_sqlite3_step(IntPtr stmt);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int D_sqlite3_reset(IntPtr stmt);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int D_sqlite3_finalize(IntPtr stmt);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int D_sqlite3_bind_text(IntPtr stmt, int index, IntPtr value, int n, IntPtr destructor);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int D_sqlite3_bind_int64(IntPtr stmt, int index, long value);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr D_sqlite3_column_text(IntPtr stmt, int iCol);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate long D_sqlite3_column_int64(IntPtr stmt, int iCol);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int D_sqlite3_bind_blob(IntPtr stmt, int index, IntPtr data, int n, IntPtr destructor);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr D_sqlite3_column_blob(IntPtr stmt, int iCol);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int D_sqlite3_column_bytes(IntPtr stmt, int iCol);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int D_sqlite3_busy_timeout(IntPtr db, int ms);

        private static D_sqlite3_open_v2 s_open_v2;
        private static D_sqlite3_close s_close;
        private static D_sqlite3_exec s_exec;
        private static D_sqlite3_free s_free;
        private static D_sqlite3_prepare_v2 s_prepare_v2;
        private static D_sqlite3_step s_step;
        private static D_sqlite3_reset s_reset;
        private static D_sqlite3_finalize s_finalize;
        private static D_sqlite3_bind_text s_bind_text;
        private static D_sqlite3_bind_int64 s_bind_int64;
        private static D_sqlite3_bind_blob s_bind_blob;
        private static D_sqlite3_column_text s_column_text;
        private static D_sqlite3_column_int64 s_column_int64;
        private static D_sqlite3_column_blob s_column_blob;
        private static D_sqlite3_column_bytes s_column_bytes;
        private static D_sqlite3_busy_timeout s_busy_timeout;

        private static T LoadDelegate<T>(IntPtr module, string name) where T : class
        {
            IntPtr p = GetProcAddress(module, name);
            if (p == IntPtr.Zero) return null;
            return (T)(object)Marshal.GetDelegateForFunctionPointer(p, typeof(T));
        }

        private static void ClearSqliteDelegates()
        {
            s_open_v2 = null;
            s_close = null;
            s_exec = null;
            s_free = null;
            s_prepare_v2 = null;
            s_step = null;
            s_reset = null;
            s_finalize = null;
            s_bind_text = null;
            s_bind_int64 = null;
            s_bind_blob = null;
            s_column_text = null;
            s_column_int64 = null;
            s_column_blob = null;
            s_column_bytes = null;
            s_busy_timeout = null;
        }

        private static bool TryBindSqliteFunctions(IntPtr h)
        {
            s_open_v2 = LoadDelegate<D_sqlite3_open_v2>(h, "sqlite3_open_v2");
            s_close = LoadDelegate<D_sqlite3_close>(h, "sqlite3_close");
            s_exec = LoadDelegate<D_sqlite3_exec>(h, "sqlite3_exec");
            s_free = LoadDelegate<D_sqlite3_free>(h, "sqlite3_free");
            s_prepare_v2 = LoadDelegate<D_sqlite3_prepare_v2>(h, "sqlite3_prepare_v2");
            s_step = LoadDelegate<D_sqlite3_step>(h, "sqlite3_step");
            s_reset = LoadDelegate<D_sqlite3_reset>(h, "sqlite3_reset");
            s_finalize = LoadDelegate<D_sqlite3_finalize>(h, "sqlite3_finalize");
            s_bind_text = LoadDelegate<D_sqlite3_bind_text>(h, "sqlite3_bind_text");
            s_bind_int64 = LoadDelegate<D_sqlite3_bind_int64>(h, "sqlite3_bind_int64");
            s_bind_blob = LoadDelegate<D_sqlite3_bind_blob>(h, "sqlite3_bind_blob");
            s_column_text = LoadDelegate<D_sqlite3_column_text>(h, "sqlite3_column_text");
            s_column_int64 = LoadDelegate<D_sqlite3_column_int64>(h, "sqlite3_column_int64");
            s_column_blob = LoadDelegate<D_sqlite3_column_blob>(h, "sqlite3_column_blob");
            s_column_bytes = LoadDelegate<D_sqlite3_column_bytes>(h, "sqlite3_column_bytes");
            s_busy_timeout = LoadDelegate<D_sqlite3_busy_timeout>(h, "sqlite3_busy_timeout");
            return s_open_v2 != null && s_close != null && s_exec != null && s_free != null
                && s_prepare_v2 != null && s_step != null && s_reset != null && s_finalize != null
                && s_bind_text != null && s_bind_int64 != null && s_bind_blob != null
                && s_column_text != null && s_column_int64 != null && s_column_blob != null && s_column_bytes != null
                && s_busy_timeout != null;
        }

        private static int NativeOpen(IntPtr filename, out IntPtr db, int flags, IntPtr vfs)
        {
            return s_open_v2(filename, out db, flags, vfs);
        }

        private static int NativeClose(IntPtr db)
        {
            return s_close(db);
        }

        private static int NativeExec(IntPtr db, IntPtr sql, IntPtr callback, IntPtr arg, out IntPtr errMsg)
        {
            return s_exec(db, sql, callback, arg, out errMsg);
        }

        private static void NativeFree(IntPtr p)
        {
            s_free(p);
        }

        private static int NativePrepare(IntPtr db, IntPtr sql, int nByte, out IntPtr stmt, out IntPtr tail)
        {
            return s_prepare_v2(db, sql, nByte, out stmt, out tail);
        }

        private static int NativeStep(IntPtr stmt)
        {
            return s_step(stmt);
        }

        private static int NativeReset(IntPtr stmt)
        {
            return s_reset(stmt);
        }

        private static int NativeFinalize(IntPtr stmt)
        {
            return s_finalize(stmt);
        }

        private static int NativeBindText(IntPtr stmt, int index, IntPtr value, int n, IntPtr destructor)
        {
            return s_bind_text(stmt, index, value, n, destructor);
        }

        private static int NativeBindInt64(IntPtr stmt, int index, long value)
        {
            return s_bind_int64(stmt, index, value);
        }

        private static int NativeBindBlob(IntPtr stmt, int index, IntPtr data, int n, IntPtr destructor)
        {
            return s_bind_blob(stmt, index, data, n, destructor);
        }

        private static IntPtr NativeColumnText(IntPtr stmt, int iCol)
        {
            return s_column_text(stmt, iCol);
        }

        private static long NativeColumnInt64(IntPtr stmt, int iCol)
        {
            return s_column_int64(stmt, iCol);
        }

        private static IntPtr NativeColumnBlob(IntPtr stmt, int iCol)
        {
            return s_column_blob(stmt, iCol);
        }

        private static int NativeColumnBytes(IntPtr stmt, int iCol)
        {
            return s_column_bytes(stmt, iCol);
        }

        private static int NativeBusyTimeout(IntPtr db, int ms)
        {
            return s_busy_timeout(db, ms);
        }

        internal static bool IsAvailable
        {
            get
            {
                EnsureLoaded();
                return loadOk;
            }
        }

        /// <summary>Call from main thread (e.g. <see cref="Gallery.Awake"/>) so native search uses the real install folder; background workers use the wrong <see cref="AppDomain.CurrentDomain.BaseDirectory"/> (VaM_Data/Managed).</summary>
        internal static void SetGameInstallRootForNativeDll(string gameInstallRoot)
        {
            if (string.IsNullOrEmpty(gameInstallRoot)) return;
            try
            {
                s_GameInstallRoot = Path.GetFullPath(gameInstallRoot.Trim());
                s_CacheVpbDirectory = Path.GetFullPath(Path.Combine(Path.Combine(s_GameInstallRoot, "Cache"), "VPB"));
            }
            catch
            {
                s_GameInstallRoot = null;
                s_CacheVpbDirectory = null;
            }
        }

        internal static string GetCacheVpbDirectoryOrFallback()
        {
            if (!string.IsNullOrEmpty(s_CacheVpbDirectory)) return s_CacheVpbDirectory;
            try { return Path.GetFullPath(Path.Combine("Cache", "VPB")); }
            catch { return null; }
        }

        private static void EnsureLoaded()
        {
            if (loadOk) return;
            if (loadAttempted) return;
            loadAttempted = true;

            string[] cands = BuildSqliteNativeCandidates();
            bool sawExistingFile = false;
            for (int i = 0; i < cands.Length; i++)
            {
                string p = cands[i];
                if (string.IsNullOrEmpty(p)) continue;
                try
                {
                    if (!File.Exists(p)) continue;
                    sawExistingFile = true;
                    IntPtr h = LoadLibrary(p);
                    if (h == IntPtr.Zero)
                    {
                        LogSqliteLoadDetailOnce("[VPB] VpbSqlite3: LoadLibrary failed for \"" + p + "\" Win32=" + Marshal.GetLastWin32Error() + " (193 often means 32-bit DLL in 64-bit VaM).");
                        continue;
                    }

                    ClearSqliteDelegates();
                    if (!TryBindSqliteFunctions(h))
                    {
                        LogSqliteLoadDetailOnce("[VPB] VpbSqlite3: \"" + p + "\" loaded but required SQLite exports are missing (wrong sqlite3.dll build).");
                        ClearSqliteDelegates();
                        try { FreeLibrary(h); } catch { }
                        continue;
                    }

                    try
                    {
                        byte[] mem = Encoding.UTF8.GetBytes(":memory:\0");
                        IntPtr pMem = Marshal.AllocHGlobal(mem.Length);
                        try
                        {
                            Marshal.Copy(mem, 0, pMem, mem.Length);
                            IntPtr test;
                            int rc = NativeOpen(pMem, out test, 6, IntPtr.Zero);
                            if (rc == SQLITE_OK && test != IntPtr.Zero)
                            {
                                NativeClose(test);
                                s_sqliteModule = h;
                                loadOk = true;
                                return;
                            }
                            LogSqliteLoadDetailOnce("[VPB] VpbSqlite3: sqlite3_open(:memory:) failed rc=" + rc + " after loading \"" + p + "\".");
                        }
                        finally
                        {
                            Marshal.FreeHGlobal(pMem);
                        }
                    }
                    catch (Exception ex)
                    {
                        LogSqliteLoadDetailOnce("[VPB] VpbSqlite3: probe after \"" + p + "\": " + ex.GetType().Name + " " + ex.Message);
                    }

                    ClearSqliteDelegates();
                    try { FreeLibrary(h); } catch { }
                }
                catch { }
            }

            if (!sawExistingFile)
                LogSqliteLoadDetailOnce("[VPB] VpbSqlite3: no sqlite3.dll found in candidate paths (install 64-bit sqlite3.dll under BepInEx\\plugins next to the game).");
            else if (!loadOk)
                LogSqliteLoadDetailOnce("[VPB] VpbSqlite3: sqlite3.dll found but unusable — use a 64-bit official SQLite amalgamation DLL for this VaM build.");
        }

        private static void LogSqliteLoadDetailOnce(string msg)
        {
            if (s_LoggedSqliteLoadDetail) return;
            s_LoggedSqliteLoadDetail = true;
            try { LogUtil.LogWarning(msg); } catch { }
        }

        private static string SafeAssemblyDirectory()
        {
            try
            {
                string loc = Assembly.GetExecutingAssembly().Location;
                if (string.IsNullOrEmpty(loc)) return "";
                loc = loc.Trim();
                if (loc.Length == 0) return "";
                return Path.GetDirectoryName(loc) ?? "";
            }
            catch (ArgumentException)
            {
                return "";
            }
            catch
            {
                return "";
            }
        }

        private static string[] BuildSqliteNativeCandidates()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory ?? "";
            string cacheUnderBase = Path.Combine(Path.Combine(Path.Combine(baseDir, "Cache"), "VPB"), "sqlite3.dll");
            string cacheUnderCwd = Path.GetFullPath(Path.Combine(Path.Combine("Cache", "VPB"), "sqlite3.dll"));
            string pluginsUnderCwd = Path.GetFullPath(Path.Combine(Path.Combine("BepInEx", "plugins"), "sqlite3.dll"));
            string asmDir = SafeAssemblyDirectory();

            List<string> list = new List<string>(16);
            if (!string.IsNullOrEmpty(s_GameInstallRoot))
            {
                AddCandidate(list, Path.Combine(Path.Combine(Path.Combine(s_GameInstallRoot, "BepInEx"), "plugins"), "sqlite3.dll"));
                AddCandidate(list, Path.Combine(Path.Combine(Path.Combine(s_GameInstallRoot, "Cache"), "VPB"), "sqlite3.dll"));
            }
            AddCandidate(list, pluginsUnderCwd);
            AddCandidate(list, cacheUnderCwd);
            AddCandidate(list, cacheUnderBase);
            AddCandidate(list, Path.Combine(baseDir, "sqlite3.dll"));
            AddCandidate(list, Path.Combine(Environment.CurrentDirectory, "sqlite3.dll"));
            if (!string.IsNullOrEmpty(asmDir) && !IsUnderBepInExScriptsFolder(asmDir))
                AddCandidate(list, Path.Combine(asmDir, "sqlite3.dll"));
            return list.ToArray();
        }

        private static void AddCandidate(List<string> list, string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i], path, StringComparison.OrdinalIgnoreCase))
                    return;
            }
            list.Add(path);
        }

        private static bool IsUnderBepInExScriptsFolder(string dir)
        {
            if (string.IsNullOrEmpty(dir)) return false;
            string marker = Path.Combine("BepInEx", "scripts");
            return dir.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal sealed class Connection : IDisposable
        {
            internal IntPtr Handle;

            internal Connection(string path)
            {
                EnsureLoaded();
                if (!loadOk) throw new InvalidOperationException("sqlite3.dll not loaded");
                byte[] utf8 = Encoding.UTF8.GetBytes(path);
                byte[] z = new byte[utf8.Length + 1];
                Buffer.BlockCopy(utf8, 0, z, 0, utf8.Length);
                IntPtr pPath = Marshal.AllocHGlobal(z.Length);
                try
                {
                    Marshal.Copy(z, 0, pPath, z.Length);
                    int rc = NativeOpen(pPath, out Handle, 6, IntPtr.Zero);
                    if (rc != SQLITE_OK || Handle == IntPtr.Zero)
                        throw new InvalidOperationException("sqlite3_open failed: " + rc);
                }
                finally
                {
                    Marshal.FreeHGlobal(pPath);
                }
                NativeBusyTimeout(Handle, 8000);
            }

            internal void ExecUtf8(string sqlUtf8)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(sqlUtf8);
                IntPtr pSql = Marshal.AllocHGlobal(bytes.Length + 1);
                try
                {
                    Marshal.Copy(bytes, 0, pSql, bytes.Length);
                    Marshal.WriteByte(pSql, bytes.Length, 0);
                    IntPtr err;
                    int rc = NativeExec(Handle, pSql, IntPtr.Zero, IntPtr.Zero, out err);
                    if (err != IntPtr.Zero)
                    {
                        try { NativeFree(err); } catch { }
                    }
                    if (rc != SQLITE_OK)
                        throw new InvalidOperationException("sqlite3_exec failed: " + rc);
                }
                finally
                {
                    Marshal.FreeHGlobal(pSql);
                }
            }

            internal Statement Prepare(string sql)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(sql);
                IntPtr pSql = Marshal.AllocHGlobal(bytes.Length + 1);
                try
                {
                    Marshal.Copy(bytes, 0, pSql, bytes.Length);
                    Marshal.WriteByte(pSql, bytes.Length, 0);
                    IntPtr stmt;
                    IntPtr tail;
                    int rc = NativePrepare(Handle, pSql, -1, out stmt, out tail);
                    if (rc != SQLITE_OK || stmt == IntPtr.Zero)
                        throw new InvalidOperationException("sqlite3_prepare failed: " + rc);
                    return new Statement(stmt);
                }
                finally
                {
                    Marshal.FreeHGlobal(pSql);
                }
            }

            public void Dispose()
            {
                if (Handle != IntPtr.Zero)
                {
                    try { NativeClose(Handle); } catch { }
                    Handle = IntPtr.Zero;
                }
            }
        }

        internal sealed class Statement : IDisposable
        {
            internal IntPtr Handle;

            internal Statement(IntPtr h) { Handle = h; }

            internal void BindText(int oneBasedIndex, string value)
            {
                if (value == null) value = "";
                byte[] utf8 = Encoding.UTF8.GetBytes(value);
                byte[] z = new byte[utf8.Length + 1];
                Buffer.BlockCopy(utf8, 0, z, 0, utf8.Length);
                IntPtr p = Marshal.AllocHGlobal(z.Length);
                try
                {
                    Marshal.Copy(z, 0, p, z.Length);
                    int rc = NativeBindText(Handle, oneBasedIndex, p, -1, SQLITE_TRANSIENT);
                    if (rc != SQLITE_OK)
                        throw new InvalidOperationException("sqlite3_bind_text failed: " + rc);
                }
                finally
                {
                    Marshal.FreeHGlobal(p);
                }
            }

            internal void BindInt64(int oneBasedIndex, long value)
            {
                int rc = NativeBindInt64(Handle, oneBasedIndex, value);
                if (rc != SQLITE_OK)
                    throw new InvalidOperationException("sqlite3_bind_int64 failed: " + rc);
            }

            internal void BindBlob(int oneBasedIndex, byte[] value)
            {
                if (value == null || value.Length == 0)
                {
                    int rcNull = NativeBindBlob(Handle, oneBasedIndex, IntPtr.Zero, 0, SQLITE_TRANSIENT);
                    if (rcNull != SQLITE_OK)
                        throw new InvalidOperationException("sqlite3_bind_blob failed: " + rcNull);
                    return;
                }
                IntPtr p = Marshal.AllocHGlobal(value.Length);
                try
                {
                    Marshal.Copy(value, 0, p, value.Length);
                    int rc = NativeBindBlob(Handle, oneBasedIndex, p, value.Length, SQLITE_TRANSIENT);
                    if (rc != SQLITE_OK)
                        throw new InvalidOperationException("sqlite3_bind_blob failed: " + rc);
                }
                finally
                {
                    Marshal.FreeHGlobal(p);
                }
            }

            internal int Step()
            {
                return NativeStep(Handle);
            }

            internal void Reset()
            {
                NativeReset(Handle);
            }

            internal string ColumnText(int zeroBased)
            {
                IntPtr p = NativeColumnText(Handle, zeroBased);
                if (p == IntPtr.Zero) return "";
                int len = 0;
                while (Marshal.ReadByte(p, len) != 0) len++;
                if (len == 0) return "";
                byte[] buf = new byte[len];
                Marshal.Copy(p, buf, 0, len);
                return Encoding.UTF8.GetString(buf);
            }

            internal long ColumnInt64(int zeroBased)
            {
                return NativeColumnInt64(Handle, zeroBased);
            }

            internal byte[] ColumnBlob(int zeroBased)
            {
                IntPtr p = NativeColumnBlob(Handle, zeroBased);
                if (p == IntPtr.Zero) return null;
                int len = NativeColumnBytes(Handle, zeroBased);
                if (len <= 0) return null;
                byte[] buf = new byte[len];
                Marshal.Copy(p, buf, 0, len);
                return buf;
            }

            public void Dispose()
            {
                if (Handle != IntPtr.Zero)
                {
                    try { NativeFinalize(Handle); } catch { }
                    Handle = IntPtr.Zero;
                }
            }
        }
    }
}
