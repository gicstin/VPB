using System;
using System.Collections.Generic;

namespace VPB.Tests
{
    public sealed class IndexLibrary
    {
        public Dictionary<string, VarPackage> Packages = new Dictionary<string, VarPackage>(StringComparer.OrdinalIgnoreCase);
        public DateTime Clock = new DateTime(2024, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        public List<Gallery.Category> Categories = IndexFixture.DefaultCategories();

        public void Rebuild()
        {
            VpbLocalDatabase.RebuildCoreWith(() => Categories, () => Clock, () => Packages);
        }

        public bool IncrementalAdd(params VarPackage[] added)
        {
            return VpbLocalDatabase.TryIncrementalGalleryIndexUpdateCoreWith(
                added, null, null, Clock.ToBinary(), () => Categories, () => Clock, () => Packages);
        }

        public bool IncrementalRemove(params string[] removedUids)
        {
            return VpbLocalDatabase.TryIncrementalGalleryIndexUpdateCoreWith(
                null, null, removedUids, Clock.ToBinary(), () => Categories, () => Clock, () => Packages);
        }

        public void AdvanceClock()
        {
            Clock = Clock.AddMinutes(1);
        }
    }

    public static class IndexFixture
    {
        public static List<Gallery.Category> DefaultCategories()
        {
            return new List<Gallery.Category>
            {
                Category("Scenes", "json", "Saves/scene"),
                Category("Clothing", "vam", "Custom/Clothing"),
                Category("Hair", "vam", "Custom/Hair"),
                Category("Appearance", "vap", "Custom/Atom/Person/Appearance"),
            };
        }

        public static Gallery.Category Category(string name, string extensionPipe, params string[] paths)
        {
            return new Gallery.Category
            {
                name = name,
                extension = extensionPipe,
                path = paths != null && paths.Length > 0 ? paths[0] : "",
                paths = paths != null ? new List<string>(paths) : null,
            };
        }

        public static IndexLibrary Build(TempInstall install, params VarFixture[] fixtures)
        {
            var library = new IndexLibrary();
            foreach (VarFixture fixture in fixtures)
            {
                fixture.WriteTo(install.AddonPackagesDir);
                VarPackage package = fixture.AsPackage();
                package.Scan();
                library.Packages[package.Uid] = package;
            }
            return library;
        }

        public static VarPackage Add(IndexLibrary library, TempInstall install, VarFixture fixture)
        {
            fixture.WriteTo(install.AddonPackagesDir);
            VarPackage package = fixture.AsPackage();
            package.Scan();
            library.Packages[package.Uid] = package;
            return package;
        }

        public static VarFixture SceneVar(string creator, string name, int version, params string[] dependencies)
        {
            return new VarFixture(creator, name, version)
                .WithText("Saves/scene/" + name + ".json", VarFixture.SceneJson(name, dependencies))
                .WithPlaceholderJpg("Saves/scene/" + name + ".jpg")
                .WithMeta(dependencies: dependencies);
        }

        public static VarFixture ClothingVar(string creator, string name, int version)
        {
            return new VarFixture(creator, name, version)
                .WithText("Custom/Clothing/Female/" + name + "/" + name + ".vam", "{ \"id\" : \"" + name + "\" }")
                .WithPlaceholderJpg("Custom/Clothing/Female/" + name + "/" + name + ".jpg")
                .WithMeta(tags: new[] { "dress" }, clothingTags: new[] { "dress" });
        }

        public static VarFixture HairVar(string creator, string name, int version)
        {
            return new VarFixture(creator, name, version)
                .WithText("Custom/Hair/Female/" + name + "/" + name + ".vam", "{ \"id\" : \"" + name + "\" }")
                .WithPlaceholderJpg("Custom/Hair/Female/" + name + "/" + name + ".jpg")
                .WithMeta(tags: new[] { "long" }, hairTags: new[] { "long" });
        }

        public static VarFixture MixedVar(string creator, string name, int version)
        {
            return new VarFixture(creator, name, version)
                .WithText("Saves/scene/" + name + ".json", VarFixture.SceneJson(name))
                .WithPlaceholderJpg("Saves/scene/" + name + ".jpg")
                .WithText("Custom/Clothing/Female/" + name + "/" + name + ".vam", "{ }")
                .WithPlaceholderJpg("Custom/Clothing/Female/" + name + "/" + name + ".jpg")
                .WithText("Custom/Atom/Person/Appearance/" + name + ".vap", "{ }")
                .WithText("Custom/Scripts/" + name + "/" + name + ".cs", "// not a gallery category")
                .WithMeta();
        }

        /// <summary>A scene and a clothing item with no sibling preview image next to them.</summary>
        public static VarFixture PreviewlessVar(string creator, string name, int version)
        {
            return new VarFixture(creator, name, version)
                .WithText("Saves/scene/" + name + ".json", VarFixture.SceneJson(name))
                .WithText("Custom/Clothing/Female/" + name + "/" + name + ".vam", "{ }")
                .WithMeta();
        }
    }

    public sealed class IndexDb : IDisposable
    {
        private readonly VpbSqlite3.Connection _connection;

        public IndexDb(string path)
        {
            _connection = new VpbSqlite3.Connection(path);
        }

        public long Scalar(string sql)
        {
            using (VpbSqlite3.Statement st = _connection.Prepare(sql))
                return st.Step() == VpbSqlite3.SqliteRow ? st.ColumnInt64(0) : -1;
        }

        public List<string> Column(string sql)
        {
            var rows = new List<string>();
            using (VpbSqlite3.Statement st = _connection.Prepare(sql))
            {
                while (st.Step() == VpbSqlite3.SqliteRow)
                    rows.Add(st.ColumnText(0));
            }
            rows.Sort(StringComparer.Ordinal);
            return rows;
        }

        public List<string> Pairs(string sql)
        {
            var rows = new List<string>();
            using (VpbSqlite3.Statement st = _connection.Prepare(sql))
            {
                while (st.Step() == VpbSqlite3.SqliteRow)
                    rows.Add(st.ColumnText(0) + " | " + st.ColumnText(1));
            }
            rows.Sort(StringComparer.Ordinal);
            return rows;
        }

        public bool TableExists(string name)
        {
            return Scalar("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='" + name + "'") == 1;
        }

        public void Dispose()
        {
            _connection.Dispose();
        }
    }
}
