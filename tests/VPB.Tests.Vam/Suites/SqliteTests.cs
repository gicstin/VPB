using System;
using System.IO;
using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace VPB.Tests
{
    [Collection(VamCollection.Name)]
    public class SqliteTests : IDisposable
    {
        private readonly ITestOutputHelper _out;
        private readonly string _dbPath;

        public SqliteTests(VamFixture vam, ITestOutputHelper output)
        {
            _out = output;
            _dbPath = Path.Combine(Path.GetTempPath(), "vpbtest_sqlite_" + Guid.NewGuid().ToString("N") + ".sqlite3");
        }

        public void Dispose()
        {
            try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { }
        }

        private VpbSqlite3.Connection Open()
        {
            return new VpbSqlite3.Connection(_dbPath);
        }

        [Fact]
        public void NativeSqliteLoadsFromTheVamInstall()
        {
            using (VpbSqlite3.Connection c = Open())
            {
                c.ExecUtf8("CREATE TABLE probe(x INTEGER);");
            }
            Assert.True(File.Exists(_dbPath),
                "sqlite3.dll bound but no database file was produced at " + _dbPath);
        }

        [Fact]
        public void TextIntegerAndBlobColumnsRoundTrip()
        {
            byte[] blob = new byte[257];
            for (int i = 0; i < blob.Length; i++) blob[i] = (byte)(i * 7);

            using (VpbSqlite3.Connection c = Open())
            {
                c.ExecUtf8("CREATE TABLE t(k TEXT, n INTEGER, b BLOB);");

                using (VpbSqlite3.Statement ins = c.Prepare("INSERT INTO t(k, n, b) VALUES (?, ?, ?)"))
                {
                    ins.BindText(1, "hello");
                    ins.BindInt64(2, 9007199254740993L);
                    ins.BindBlob(3, blob);
                    Assert.Equal(VpbSqlite3.SqliteDone, ins.Step());
                }

                using (VpbSqlite3.Statement sel = c.Prepare("SELECT k, n, b FROM t"))
                {
                    Assert.Equal(VpbSqlite3.SqliteRow, sel.Step());
                    Assert.Equal("hello", sel.ColumnText(0));
                    Assert.Equal(9007199254740993L, sel.ColumnInt64(1));
                    Assert.Equal(blob, sel.ColumnBlob(2));
                    Assert.Equal(VpbSqlite3.SqliteDone, sel.Step());
                }
            }
        }

        [Fact]
        public void NonAsciiTextSurvivesTheUtf8Marshalling()
        {
            const string value = "Créateur 日本語 éèü \U0001F600";

            using (VpbSqlite3.Connection c = Open())
            {
                c.ExecUtf8("CREATE TABLE t(k TEXT);");
                using (VpbSqlite3.Statement ins = c.Prepare("INSERT INTO t(k) VALUES (?)"))
                {
                    ins.BindText(1, value);
                    ins.Step();
                }
                using (VpbSqlite3.Statement sel = c.Prepare("SELECT k FROM t"))
                {
                    Assert.Equal(VpbSqlite3.SqliteRow, sel.Step());
                    Assert.Equal(value, sel.ColumnText(0));
                }
            }
        }

        [Fact]
        public void PathsWithSpacesAndUnicodeOpenCorrectly()
        {
            string dir = Path.Combine(Path.GetTempPath(), "vpb test é " + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string db = Path.Combine(dir, "Creator's Pack 1.sqlite3");
            try
            {
                using (var c = new VpbSqlite3.Connection(db))
                    c.ExecUtf8("CREATE TABLE t(x INTEGER);");
                Assert.True(File.Exists(db), "Spaced/unicode database path did not open: " + db);
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        [Fact]
        public void BindingNullOrEmptyValuesDoesNotThrow()
        {
            using (VpbSqlite3.Connection c = Open())
            {
                c.ExecUtf8("CREATE TABLE t(k TEXT, b BLOB);");
                using (VpbSqlite3.Statement ins = c.Prepare("INSERT INTO t(k, b) VALUES (?, ?)"))
                {
                    ins.BindText(1, null);
                    ins.BindBlob(2, null);
                    ins.Step();
                }
                using (VpbSqlite3.Statement sel = c.Prepare("SELECT k, b FROM t"))
                {
                    Assert.Equal(VpbSqlite3.SqliteRow, sel.Step());
                    Assert.Equal("", sel.ColumnText(0));
                    Assert.Null(sel.ColumnBlob(1));
                }
            }
        }

        [Fact]
        public void BadSqlThrowsRatherThanCorruptingState()
        {
            using (VpbSqlite3.Connection c = Open())
            {
                c.ExecUtf8("CREATE TABLE t(x INTEGER);");
                Assert.ThrowsAny<Exception>(() => c.Prepare("SELECT * FROM does_not_exist"));
                c.ExecUtf8("INSERT INTO t VALUES (1);");

                using (VpbSqlite3.Statement sel = c.Prepare("SELECT COUNT(*) FROM t"))
                {
                    Assert.Equal(VpbSqlite3.SqliteRow, sel.Step());
                    Assert.Equal(1L, sel.ColumnInt64(0));
                }
            }
        }

        [Fact]
        public void StatementResetAllowsReuse()
        {
            using (VpbSqlite3.Connection c = Open())
            {
                c.ExecUtf8("CREATE TABLE t(k TEXT);");
                using (VpbSqlite3.Statement ins = c.Prepare("INSERT INTO t(k) VALUES (?)"))
                {
                    for (int i = 0; i < 5; i++)
                    {
                        ins.Reset();
                        ins.BindText(1, "row" + i);
                        Assert.Equal(VpbSqlite3.SqliteDone, ins.Step());
                    }
                }
                using (VpbSqlite3.Statement sel = c.Prepare("SELECT COUNT(*) FROM t"))
                {
                    sel.Step();
                    Assert.Equal(5L, sel.ColumnInt64(0));
                }
            }
        }
    }
}
