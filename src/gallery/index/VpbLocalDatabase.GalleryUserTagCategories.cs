using System;
using System.Collections.Generic;

namespace VPB
{
    /// <summary>
    /// Named, color-coded user-tag categories (US-02). A category groups tags and owns a display color;
    /// each tag references at most one category via <c>gallery_user_tag.category_id</c>. Store is authoritative
    /// SQLite (<c>gallery_user_tag_category</c>). Same static partial type as the rest of the local DB layer.
    /// </summary>
    internal static partial class VpbLocalDatabase
    {
        internal const int GalleryUserTagCategoryNameMaxLength = 64;
        internal const int GalleryUserTagCategoryMaxCount = 512;

        internal struct GalleryUserTagCategoryInfo
        {
            public long Id;
            public string Name;
            public string Color;
        }

        /// <summary>Trim + collapse; reject empty/oversized. Display case preserved (unlike tag names), uniqueness enforced by DB (case-sensitive TEXT UNIQUE).</summary>
        internal static string NormalizeGalleryUserTagCategoryName(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            string s = raw.Trim();
            if (s.Length == 0 || s.Length > GalleryUserTagCategoryNameMaxLength) return "";
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\0' || c == '\n' || c == '\r') return "";
                if (c != '\t' && char.IsControl(c)) return "";
            }
            return s;
        }

        internal static string NormalizeGalleryUserTagCategoryColor(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            string s = raw.Trim();
            if (s.Length == 0 || s.Length > 16) return "";
            return s;
        }

        internal static bool TryListGalleryUserTagCategories(List<GalleryUserTagCategoryInfo> rowsOut)
        {
            rowsOut?.Clear();
            if (!VpbSqlite3.IsAvailable || rowsOut == null) return false;
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    using (var stmt = conn.Prepare("SELECT id, name, color FROM gallery_user_tag_category ORDER BY name COLLATE NOCASE"))
                    {
                        while (stmt.Step() == VpbSqlite3.SqliteRow)
                        {
                            rowsOut.Add(new GalleryUserTagCategoryInfo
                            {
                                Id = stmt.ColumnInt64(0),
                                Name = stmt.ColumnText(1) ?? "",
                                Color = stmt.ColumnText(2) ?? ""
                            });
                        }
                    }
                }
                return true;
            }
            catch { return false; }
        }

        internal static bool TryCreateGalleryUserTagCategory(string name, string colorHex, out long categoryId)
        {
            categoryId = -1;
            string n = NormalizeGalleryUserTagCategoryName(name);
            string c = NormalizeGalleryUserTagCategoryColor(colorHex);
            if (!VpbSqlite3.IsAvailable || string.IsNullOrEmpty(n) || string.IsNullOrEmpty(c)) return false;
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    using (var cnt = conn.Prepare("SELECT COUNT(*) FROM gallery_user_tag_category"))
                    {
                        if (cnt.Step() == VpbSqlite3.SqliteRow && cnt.ColumnInt64(0) >= GalleryUserTagCategoryMaxCount)
                            return false;
                    }
                    using (var ins = conn.Prepare("INSERT OR IGNORE INTO gallery_user_tag_category(name, color) VALUES(?, ?)"))
                    {
                        ins.BindText(1, n);
                        ins.BindText(2, c);
                        ins.Step();
                    }
                    using (var sel = conn.Prepare("SELECT id FROM gallery_user_tag_category WHERE name=?"))
                    {
                        sel.BindText(1, n);
                        if (sel.Step() == VpbSqlite3.SqliteRow) categoryId = sel.ColumnInt64(0);
                    }
                }
                return categoryId >= 0;
            }
            catch { return false; }
        }

        internal static bool TryRenameGalleryUserTagCategory(long categoryId, string newName)
        {
            string n = NormalizeGalleryUserTagCategoryName(newName);
            if (!VpbSqlite3.IsAvailable || categoryId < 0 || string.IsNullOrEmpty(n)) return false;
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    using (var upd = conn.Prepare("UPDATE gallery_user_tag_category SET name=? WHERE id=?"))
                    {
                        upd.BindText(1, n);
                        upd.BindInt64(2, categoryId);
                        upd.Step();
                    }
                }
                return true;
            }
            catch { return false; }
        }

        internal static bool TrySetGalleryUserTagCategoryColor(long categoryId, string colorHex)
        {
            string c = NormalizeGalleryUserTagCategoryColor(colorHex);
            if (!VpbSqlite3.IsAvailable || categoryId < 0 || string.IsNullOrEmpty(c)) return false;
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    using (var upd = conn.Prepare("UPDATE gallery_user_tag_category SET color=? WHERE id=?"))
                    {
                        upd.BindText(1, c);
                        upd.BindInt64(2, categoryId);
                        upd.Step();
                    }
                }
                return true;
            }
            catch { return false; }
        }

        internal static bool TryDeleteGalleryUserTagCategory(long categoryId)
        {
            if (!VpbSqlite3.IsAvailable || categoryId < 0) return false;
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    using (var clr = conn.Prepare("UPDATE gallery_user_tag SET category_id=NULL WHERE category_id=?"))
                    {
                        clr.BindInt64(1, categoryId);
                        clr.Step();
                    }
                    using (var del = conn.Prepare("DELETE FROM gallery_user_tag_category WHERE id=?"))
                    {
                        del.BindInt64(1, categoryId);
                        del.Step();
                    }
                }
                return true;
            }
            catch { return false; }
        }

        /// <summary>Assign a tag to a category, or pass <paramref name="categoryId"/> &lt; 0 to clear. Creates the tag row if it does not exist.</summary>
        internal static bool TryAssignGalleryUserTagCategory(string tagName, long categoryId)
        {
            string n = NormalizeGalleryUserTagName(tagName);
            if (!VpbSqlite3.IsAvailable || string.IsNullOrEmpty(n)) return false;
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    long tagId = TryGetOrCreateGalleryUserTagId(conn, n);
                    if (tagId < 0) return false;
                    if (categoryId < 0)
                    {
                        using (var clr = conn.Prepare("UPDATE gallery_user_tag SET category_id=NULL WHERE tag_id=?"))
                        {
                            clr.BindInt64(1, tagId);
                            clr.Step();
                        }
                    }
                    else
                    {
                        using (var upd = conn.Prepare("UPDATE gallery_user_tag SET category_id=? WHERE tag_id=?"))
                        {
                            upd.BindInt64(1, categoryId);
                            upd.BindInt64(2, tagId);
                            upd.Step();
                        }
                    }
                }
                return true;
            }
            catch { return false; }
        }

        /// <summary>Assign many tags to one category (or clear when <paramref name="categoryId"/> &lt; 0) in a SINGLE connection + transaction.
        /// Avoids the per-tag connection/EnsureSchema cost that made bulk assignment slow.</summary>
        internal static bool TryAssignGalleryUserTagCategoryBatch(IEnumerable<string> tagNames, long categoryId)
        {
            if (!VpbSqlite3.IsAvailable || tagNames == null) return false;
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    conn.ExecUtf8("BEGIN;");
                    try
                    {
                        foreach (string raw in tagNames)
                        {
                            string n = NormalizeGalleryUserTagName(raw);
                            if (string.IsNullOrEmpty(n)) continue;
                            long tagId = TryGetOrCreateGalleryUserTagId(conn, n);
                            if (tagId < 0) continue;
                            if (categoryId < 0)
                            {
                                using (var clr = conn.Prepare("UPDATE gallery_user_tag SET category_id=NULL WHERE tag_id=?"))
                                {
                                    clr.BindInt64(1, tagId);
                                    clr.Step();
                                }
                            }
                            else
                            {
                                using (var upd = conn.Prepare("UPDATE gallery_user_tag SET category_id=? WHERE tag_id=?"))
                                {
                                    upd.BindInt64(1, categoryId);
                                    upd.BindInt64(2, tagId);
                                    upd.Step();
                                }
                            }
                        }
                        conn.ExecUtf8("COMMIT;");
                    }
                    catch
                    {
                        try { conn.ExecUtf8("ROLLBACK;"); } catch { }
                        return false;
                    }
                }
                return true;
            }
            catch { return false; }
        }

        /// <summary>Map of tag name (normalized) → color hex for every tag with an assigned category. Used for row tinting.</summary>
        internal static bool TryReadGalleryUserTagColorMap(Dictionary<string, string> mapOut)
        {
            mapOut?.Clear();
            if (!VpbSqlite3.IsAvailable || mapOut == null) return false;
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    using (var stmt = conn.Prepare(
                        "SELECT gt.name, c.color FROM gallery_user_tag gt " +
                        "INNER JOIN gallery_user_tag_category c ON c.id=gt.category_id"))
                    {
                        while (stmt.Step() == VpbSqlite3.SqliteRow)
                        {
                            string name = stmt.ColumnText(0) ?? "";
                            string color = stmt.ColumnText(1) ?? "";
                            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(color)) continue;
                            mapOut[name] = color;
                        }
                    }
                }
                return true;
            }
            catch { return false; }
        }

        /// <summary>Map of tag name (normalized) → category id for every assigned tag.</summary>
        internal static bool TryReadGalleryUserTagCategoryAssignments(Dictionary<string, long> mapOut)
        {
            mapOut?.Clear();
            if (!VpbSqlite3.IsAvailable || mapOut == null) return false;
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    using (var stmt = conn.Prepare("SELECT name, category_id FROM gallery_user_tag WHERE category_id IS NOT NULL"))
                    {
                        while (stmt.Step() == VpbSqlite3.SqliteRow)
                        {
                            string name = stmt.ColumnText(0) ?? "";
                            if (string.IsNullOrEmpty(name)) continue;
                            mapOut[name] = stmt.ColumnInt64(1);
                        }
                    }
                }
                return true;
            }
            catch { return false; }
        }
    }
}
