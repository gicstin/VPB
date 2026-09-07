using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace VPB.Tests
{
    [Collection(VamCollection.Name)]
    public class GalleryIndexTests
    {
        private readonly ITestOutputHelper _out;
        public GalleryIndexTests(VamFixture vam, ITestOutputHelper output) { _out = output; }


        [Fact]
        public void RebuildWritesOnePkgRowPerPackage()
        {
            using (var install = new TempInstall("idx_pkg"))
            {
                IndexLibrary library = IndexFixture.Build(install,
                    IndexFixture.SceneVar("Alpha", "Scene", 1),
                    IndexFixture.ClothingVar("Beta", "Dress", 2),
                    IndexFixture.HairVar("Gamma", "Bob", 1),
                    IndexFixture.MixedVar("Delta", "Everything", 3));

                library.Rebuild();

                using (var db = new IndexDb(install.DatabasePath))
                {
                    _out.WriteLine("db: " + install.DatabasePath);
                    _out.WriteLine("pkg rows: " + string.Join(", ", db.Column("SELECT uid FROM pkg").ToArray()));

                    Assert.Equal(4, db.Scalar("SELECT COUNT(*) FROM pkg"));
                    Assert.Equal(
                        new[] { "Alpha.Scene.1", "Beta.Dress.2", "Delta.Everything.3", "Gamma.Bob.1" },
                        db.Column("SELECT uid FROM pkg").ToArray());
                    Assert.Equal(
                        new[] { "Alpha", "Beta", "Delta", "Gamma" },
                        db.Column("SELECT DISTINCT creator FROM pkg").ToArray());
                }
            }
        }

        [Fact]
        public void CategoryMembershipFollowsExtensionAndPathPrefix()
        {
            using (var install = new TempInstall("idx_cat"))
            {
                IndexLibrary library = IndexFixture.Build(install, IndexFixture.MixedVar("Delta", "Everything", 3));
                library.Rebuild();

                using (var db = new IndexDb(install.DatabasePath))
                {
                    List<string> rows = db.Pairs("SELECT category, internal_path FROM cat_mem");
                    foreach (string r in rows) _out.WriteLine("cat_mem: " + r);

                    Assert.Contains("Scenes | Saves/scene/Everything.json", rows);
                    Assert.Contains("Clothing | Custom/Clothing/Female/Everything/Everything.vam", rows);
                    Assert.Contains("Appearance | Custom/Atom/Person/Appearance/Everything.vap", rows);

                    Assert.DoesNotContain(rows, r => r.StartsWith("Scenes |", StringComparison.Ordinal) && r.EndsWith(".jpg", StringComparison.Ordinal));
                    Assert.DoesNotContain(rows, r => r.StartsWith("Hair |", StringComparison.Ordinal));
                    Assert.DoesNotContain(rows, r => r.EndsWith(".jpg", StringComparison.Ordinal));

                    Assert.Contains("EVERYTHING | Custom/Scripts/Everything/Everything.cs", rows);
                    Assert.Contains("EVERYTHING | meta.json", rows);
                }
            }
        }

        [Fact]
        public void ClothingAndHairShareAnExtensionAndAreSeparatedByPath()
        {
            using (var install = new TempInstall("idx_vam_split"))
            {
                IndexLibrary library = IndexFixture.Build(install,
                    IndexFixture.ClothingVar("Beta", "Dress", 1),
                    IndexFixture.HairVar("Gamma", "Bob", 1));
                library.Rebuild();

                using (var db = new IndexDb(install.DatabasePath))
                {
                    Assert.Equal(new[] { "Beta.Dress.1" }, db.Column("SELECT pkg_uid FROM cat_mem WHERE category='Clothing'").ToArray());
                    Assert.Equal(new[] { "Gamma.Bob.1" }, db.Column("SELECT pkg_uid FROM cat_mem WHERE category='Hair'").ToArray());
                }
            }
        }

        [Fact]
        public void DeclaredDependenciesLandInPkgDep()
        {
            using (var install = new TempInstall("idx_dep"))
            {
                IndexLibrary library = IndexFixture.Build(install,
                    IndexFixture.SceneVar("Alpha", "Consumer", 1, "Beta.Dress.2", "Gamma.Bob.1"),
                    IndexFixture.ClothingVar("Beta", "Dress", 2),
                    IndexFixture.HairVar("Gamma", "Bob", 1));

                library.Rebuild();

                using (var db = new IndexDb(install.DatabasePath))
                {
                    List<string> deps = db.Column("SELECT dep_uid FROM pkg_dep WHERE src_uid='Alpha.Consumer.1'");
                    _out.WriteLine("deps: " + string.Join(", ", deps.ToArray()));

                    Assert.Contains("Beta.Dress.2", deps);
                    Assert.Contains("Gamma.Bob.1", deps);
                    Assert.Equal(0, db.Scalar("SELECT COUNT(*) FROM pkg_dep WHERE src_uid='Beta.Dress.2'"));
                }
            }
        }

        [Fact]
        public void RebuildingTwiceIsStable()
        {
            using (var install = new TempInstall("idx_stable"))
            {
                IndexLibrary library = IndexFixture.Build(install,
                    IndexFixture.SceneVar("Alpha", "Scene", 1),
                    IndexFixture.ClothingVar("Beta", "Dress", 2));

                library.Rebuild();
                List<string> first;
                using (var db = new IndexDb(install.DatabasePath))
                    first = db.Pairs("SELECT category, pkg_uid || '/' || internal_path FROM cat_mem");

                library.Rebuild();
                List<string> second;
                using (var db = new IndexDb(install.DatabasePath))
                    second = db.Pairs("SELECT category, pkg_uid || '/' || internal_path FROM cat_mem");

                Assert.Equal(first, second);
            }
        }

        [Fact]
        public void RemovedPackageDisappearsOnRebuild()
        {
            using (var install = new TempInstall("idx_remove"))
            {
                IndexLibrary library = IndexFixture.Build(install,
                    IndexFixture.SceneVar("Alpha", "Scene", 1),
                    IndexFixture.ClothingVar("Beta", "Dress", 2));
                library.Rebuild();

                using (var db = new IndexDb(install.DatabasePath))
                    Assert.Equal(2, db.Scalar("SELECT COUNT(*) FROM pkg"));

                library.Packages.Remove("Beta.Dress.2");
                library.AdvanceClock();
                library.Rebuild();

                using (var db = new IndexDb(install.DatabasePath))
                {
                    Assert.Equal(new[] { "Alpha.Scene.1" }, db.Column("SELECT uid FROM pkg").ToArray());
                    Assert.Equal(0, db.Scalar("SELECT COUNT(*) FROM cat_mem WHERE pkg_uid='Beta.Dress.2'"));
                    Assert.Equal(0, db.Scalar("SELECT COUNT(*) FROM pkg_dep WHERE src_uid='Beta.Dress.2'"));
                }
            }
        }

        private static List<string> IndexSnapshot(string dbPath)
        {
            var rows = new List<string>();
            using (var db = new IndexDb(dbPath))
            {
                foreach (string r in db.Pairs("SELECT 'pkg', uid || '|' || ifnull(creator,'') FROM pkg"))
                    rows.Add(r);
                foreach (string r in db.Pairs("SELECT 'pkg_dep', src_uid || '|' || dep_uid FROM pkg_dep"))
                    rows.Add(r);
                foreach (string r in db.Pairs("SELECT 'cat_mem', category || '|' || pkg_uid || '|' || internal_path FROM cat_mem"))
                    rows.Add(r);
            }
            rows.Sort(StringComparer.Ordinal);
            return rows;
        }

        [Fact]
        public void IncrementalAddMatchesAFullRebuild()
        {
            using (var install = new TempInstall("idx_incr"))
            {
                IndexLibrary library = IndexFixture.Build(install,
                    IndexFixture.SceneVar("Alpha", "Scene", 1),
                    IndexFixture.ClothingVar("Beta", "Dress", 2));
                library.Rebuild();

                VarPackage added = IndexFixture.Add(library, install, IndexFixture.HairVar("Gamma", "Bob", 1));
                bool applied = library.IncrementalAdd(added);
                _out.WriteLine("incremental applied: " + applied);

                List<string> incremental = IndexSnapshot(install.DatabasePath);

                library.Rebuild();

                List<string> full = IndexSnapshot(install.DatabasePath);

                Assert.True(applied,
                    "The incremental path bailed out, so this test compared a full rebuild against itself and " +
                    "proved nothing. Check the gates at the top of TryIncrementalGalleryIndexUpdateCoreWith.");

                _out.WriteLine("rows compared across pkg + pkg_dep + cat_mem: " + full.Count);
                Assert.Equal(full, incremental);
            }
        }

        [Fact]
        public void IncrementalRemoveMatchesAFullRebuild()
        {
            using (var install = new TempInstall("idx_incr_rm"))
            {
                IndexLibrary library = IndexFixture.Build(install,
                    IndexFixture.SceneVar("Alpha", "Scene", 1),
                    IndexFixture.ClothingVar("Beta", "Dress", 2),
                    IndexFixture.HairVar("Gamma", "Bob", 1));
                library.Rebuild();

                library.Packages.Remove("Gamma.Bob.1");
                bool applied = library.IncrementalRemove("Gamma.Bob.1");
                _out.WriteLine("incremental applied: " + applied);

                List<string> incremental = IndexSnapshot(install.DatabasePath);

                library.Rebuild();

                List<string> full = IndexSnapshot(install.DatabasePath);

                Assert.True(applied, "The incremental removal path bailed out; this test proved nothing.");

                _out.WriteLine("rows compared across pkg + pkg_dep + cat_mem: " + full.Count);
                Assert.Equal(full, incremental);
            }
        }

        [Fact]
        public void EmptyCategoryListStillCreatesTheSchema()
        {
            using (var install = new TempInstall("idx_nocat"))
            {
                IndexLibrary library = IndexFixture.Build(install, IndexFixture.SceneVar("Alpha", "Scene", 1));
                library.Categories = new List<Gallery.Category>();
                library.Rebuild();

                Assert.True(File.Exists(install.DatabasePath),
                    "With no categories the rebuild must still create an empty, schema-complete database.");

                using (var db = new IndexDb(install.DatabasePath))
                {
                    Assert.True(db.TableExists("pkg"));
                    Assert.True(db.TableExists("cat_mem"));
                    Assert.True(db.TableExists("pkg_dep"));
                    Assert.Equal(0, db.Scalar("SELECT COUNT(*) FROM cat_mem"));
                }
            }
        }

        [Fact]
        public void AnUnstampedClockIsNeverPublishedAsAScan()
        {
            using (var install = new TempInstall("idx_clock"))
            {
                IndexLibrary library = IndexFixture.Build(install, IndexFixture.SceneVar("Alpha", "Scene", 1));
                library.Clock = DateTime.MinValue;
                library.Rebuild();

                bool wroteRows = false;
                if (File.Exists(install.DatabasePath))
                {
                    using (var db = new IndexDb(install.DatabasePath))
                        wroteRows = db.TableExists("pkg") && db.Scalar("SELECT COUNT(*) FROM pkg") > 0;
                }

                Assert.False(wroteRows,
                    "DateTime.MinValue.ToBinary() is 0. Publishing that as the ready-scan stamp leaves the SQL fast " +
                    "path disabled for the rest of the session, so an unstamped clock must abort the rebuild.");
            }
        }

        [Fact]
        public void PreviewlessScenesAreIndexedButPreviewlessClothingIsNot()
        {
            using (var install = new TempInstall("idx_nopreview"))
            {
                IndexLibrary library = IndexFixture.Build(install,
                    IndexFixture.PreviewlessVar("Alpha", "NoThumbs", 1),
                    IndexFixture.SceneVar("Beta", "WithThumbs", 1));
                library.Rebuild();

                using (var db = new IndexDb(install.DatabasePath))
                {
                    List<string> scenes = db.Column("SELECT pkg_uid FROM cat_mem WHERE category='Scenes'");
                    _out.WriteLine("scene rows: " + string.Join(", ", scenes.ToArray()));

                    Assert.Contains("Beta.WithThumbs.1", scenes);
                    Assert.Contains("Alpha.NoThumbs.1", scenes);
                    Assert.Equal(2, scenes.Count);
                    Assert.Equal(0, db.Scalar(
                        "SELECT COUNT(*) FROM cat_mem WHERE pkg_uid='Alpha.NoThumbs.1' AND category NOT IN ('EVERYTHING','Scenes')"));
                    Assert.Equal(1, db.Scalar("SELECT COUNT(*) FROM pkg WHERE uid='Alpha.NoThumbs.1'"));
                }
            }
        }

        [Fact]
        public void PackagesWithNoIndexableContentStillGetAPkgRow()
        {
            using (var install = new TempInstall("idx_empty_pkg"))
            {
                VarFixture barren = new VarFixture("Alpha", "ScriptsOnly", 1)
                    .WithText("Custom/Scripts/Alpha/tool.cs", "// nothing the gallery lists")
                    .WithMeta();

                IndexLibrary library = IndexFixture.Build(install, barren);
                library.Rebuild();

                using (var db = new IndexDb(install.DatabasePath))
                {
                    Assert.Equal(new[] { "Alpha.ScriptsOnly.1" }, db.Column("SELECT uid FROM pkg").ToArray());
                    Assert.Equal(0, db.Scalar(
                        "SELECT COUNT(*) FROM cat_mem WHERE pkg_uid='Alpha.ScriptsOnly.1' AND category<>'EVERYTHING'"));
                }
            }
        }
    }
}
