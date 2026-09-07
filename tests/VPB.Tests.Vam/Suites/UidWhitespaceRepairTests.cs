using System;
using System.Collections.Generic;
using System.IO;
using Xunit;
using Xunit.Abstractions;

namespace VPB.Tests
{
    [Collection(VamCollection.Name)]
    public class UidWhitespaceRepairTests : IDisposable
    {
        private const string Spaced = "Some Creator.Pack Name.3";
        private const string Exact = "SomeCreator.PackName.3";

        private readonly ITestOutputHelper _out;
        private readonly string _dbPath;

        public UidWhitespaceRepairTests(VamFixture vam, ITestOutputHelper output)
        {
            _out = output;
            _dbPath = Path.Combine(Path.GetTempPath(), "vpbtest_uidrepair_" + Guid.NewGuid().ToString("N") + ".sqlite3");
        }

        public void Dispose()
        {
            try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { }
        }

        private VpbSqlite3.Connection Open()
        {
            var conn = new VpbSqlite3.Connection(_dbPath);
            conn.ExecUtf8(
                "CREATE TABLE IF NOT EXISTS pkg (uid TEXT PRIMARY KEY, creator TEXT, wtime INTEGER, psize INTEGER, first_scanned INTEGER);" +
                "CREATE TABLE IF NOT EXISTS cat_mem (category TEXT NOT NULL, pkg_uid TEXT NOT NULL, internal_path TEXT NOT NULL, PRIMARY KEY(category, pkg_uid, internal_path));" +
                "CREATE TABLE IF NOT EXISTS pkg_dep (src_uid TEXT NOT NULL, dep_uid TEXT NOT NULL, PRIMARY KEY(src_uid, dep_uid));" +
                "CREATE TABLE IF NOT EXISTS cleanup_exclude (uid TEXT PRIMARY KEY, added_utc_binary INTEGER NOT NULL);");
            VpbUidWhitespaceIdentityRepair.EnsureSchema(conn);
            return conn;
        }

        private static void InsertPkg(VpbSqlite3.Connection conn, string uid, long firstScanned)
        {
            using (VpbSqlite3.Statement st = conn.Prepare(
                "INSERT OR REPLACE INTO pkg(uid, creator, wtime, psize, first_scanned) VALUES(?, 'Creator', 1, 1, ?)"))
            {
                st.BindText(1, uid);
                st.BindInt64(2, firstScanned);
                st.Step();
            }
        }

        private static void InsertCatMem(VpbSqlite3.Connection conn, string pkgUid, string internalPath)
        {
            using (VpbSqlite3.Statement st = conn.Prepare(
                "INSERT OR IGNORE INTO cat_mem(category, pkg_uid, internal_path) VALUES('Scenes', ?, ?)"))
            {
                st.BindText(1, pkgUid);
                st.BindText(2, internalPath);
                st.Step();
            }
        }

        private static void InsertDep(VpbSqlite3.Connection conn, string src, string dep)
        {
            using (VpbSqlite3.Statement st = conn.Prepare(
                "INSERT OR IGNORE INTO pkg_dep(src_uid, dep_uid) VALUES(?, ?)"))
            {
                st.BindText(1, src);
                st.BindText(2, dep);
                st.Step();
            }
        }

        private static long Scalar(VpbSqlite3.Connection conn, string sql)
        {
            using (VpbSqlite3.Statement st = conn.Prepare(sql))
                return st.Step() == VpbSqlite3.SqliteRow ? st.ColumnInt64(0) : -1;
        }

        private static List<string> Column(VpbSqlite3.Connection conn, string sql)
        {
            var rows = new List<string>();
            using (VpbSqlite3.Statement st = conn.Prepare(sql))
            {
                while (st.Step() == VpbSqlite3.SqliteRow) rows.Add(st.ColumnText(0));
            }
            rows.Sort(StringComparer.Ordinal);
            return rows;
        }

        private static List<VpbUidWhitespaceIdentityRepair.Candidate> One(string stored, string exact, long firstScanned)
        {
            return new List<VpbUidWhitespaceIdentityRepair.Candidate>
            {
                new VpbUidWhitespaceIdentityRepair.Candidate(stored, exact, firstScanned),
            };
        }

        [Fact]
        public void ASpacedUidIsRenamedWhenTheExactUidIsNotPresent()
        {
            using (VpbSqlite3.Connection conn = Open())
            {
                InsertPkg(conn, Spaced, 1000);
                InsertCatMem(conn, Spaced, "Saves/scene/one.json");

                int repaired = VpbUidWhitespaceIdentityRepair.Apply(conn, One(Spaced, Exact, 1000));

                _out.WriteLine("repaired=" + repaired);
                Assert.Equal(1, repaired);
                Assert.Equal(new[] { Exact }, Column(conn, "SELECT uid FROM pkg").ToArray());
                Assert.Equal(new[] { Exact }, Column(conn, "SELECT pkg_uid FROM cat_mem").ToArray());
            }
        }

        [Fact]
        public void DependencyRowsAreRemappedOnBothColumns()
        {
            using (VpbSqlite3.Connection conn = Open())
            {
                InsertPkg(conn, Spaced, 1000);
                InsertDep(conn, Spaced, "Other.Pack.1");
                InsertDep(conn, "Other.Pack.1", Spaced);

                VpbUidWhitespaceIdentityRepair.Apply(conn, One(Spaced, Exact, 1000));

                Assert.Equal(new[] { "Other.Pack.1", Exact }, Column(conn, "SELECT src_uid FROM pkg_dep").ToArray());
                Assert.Equal(new[] { "Other.Pack.1", Exact }, Column(conn, "SELECT dep_uid FROM pkg_dep").ToArray());
                Assert.Equal(0, Scalar(conn, "SELECT COUNT(*) FROM pkg_dep WHERE src_uid='" + Spaced + "' OR dep_uid='" + Spaced + "'"));
            }
        }

        [Fact]
        public void WhenBothUidsExistTheSpacedRowIsDroppedAndTheExactOneSurvives()
        {
            using (VpbSqlite3.Connection conn = Open())
            {
                InsertPkg(conn, Spaced, 1000);
                InsertPkg(conn, Exact, 5000);

                int repaired = VpbUidWhitespaceIdentityRepair.Apply(conn, One(Spaced, Exact, 1000));

                Assert.Equal(1, repaired);
                Assert.Equal(new[] { Exact }, Column(conn, "SELECT uid FROM pkg").ToArray());
            }
        }

        [Fact]
        public void TheOlderFirstScannedWinsOnMerge()
        {
            using (VpbSqlite3.Connection conn = Open())
            {
                InsertPkg(conn, Spaced, 1000);
                InsertPkg(conn, Exact, 5000);

                VpbUidWhitespaceIdentityRepair.Apply(conn, One(Spaced, Exact, 1000));

                long kept = Scalar(conn, "SELECT first_scanned FROM pkg WHERE uid='" + Exact + "'");
                _out.WriteLine("first_scanned kept = " + kept);

                Assert.Equal(1000, kept);
            }
        }

        [Fact]
        public void ANewerPreservedDateDoesNotOverwriteAnOlderExistingOne()
        {
            using (VpbSqlite3.Connection conn = Open())
            {
                InsertPkg(conn, Spaced, 9000);
                InsertPkg(conn, Exact, 2000);

                VpbUidWhitespaceIdentityRepair.Apply(conn, One(Spaced, Exact, 9000));

                Assert.Equal(2000, Scalar(conn, "SELECT first_scanned FROM pkg WHERE uid='" + Exact + "'"));
            }
        }

        [Fact]
        public void ThePreservedDateIsRecordedForALaterRebuild()
        {
            using (VpbSqlite3.Connection conn = Open())
            {
                InsertPkg(conn, Spaced, 1234);
                VpbUidWhitespaceIdentityRepair.Apply(conn, One(Spaced, Exact, 1234));

                var firstScannedByUid = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
                VpbUidWhitespaceIdentityRepair.MergePreservedFirstScanned(conn, firstScannedByUid);

                Assert.True(firstScannedByUid.ContainsKey(Exact),
                    "A rebuild that does not see the preserved date resets the package's 'first scanned' to today, " +
                    "so it jumps to the top of the New sort for no reason.");
                Assert.Equal(1234, firstScannedByUid[Exact]);
            }
        }

        [Fact]
        public void MergePreservedKeepsTheOldestOfTheTwoDates()
        {
            using (VpbSqlite3.Connection conn = Open())
            {
                InsertPkg(conn, Spaced, 1000);
                VpbUidWhitespaceIdentityRepair.Apply(conn, One(Spaced, Exact, 1000));

                var map = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase) { { Exact, 8000 } };
                VpbUidWhitespaceIdentityRepair.MergePreservedFirstScanned(conn, map);
                Assert.Equal(1000, map[Exact]);

                var newer = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase) { { Exact, 500 } };
                VpbUidWhitespaceIdentityRepair.MergePreservedFirstScanned(conn, newer);
                Assert.Equal(500, newer[Exact]);
            }
        }

        [Fact]
        public void ConsumeAppliedOnlyClearsRowsThePackageTableNowCarries()
        {
            using (VpbSqlite3.Connection conn = Open())
            {
                InsertPkg(conn, Spaced, 1000);
                VpbUidWhitespaceIdentityRepair.Apply(conn, One(Spaced, Exact, 1000));
                Assert.Equal(1, Scalar(conn, "SELECT COUNT(*) FROM pkg_uid_first_scanned_repair"));

                VpbUidWhitespaceIdentityRepair.ConsumeApplied(conn);
                Assert.Equal(0, Scalar(conn, "SELECT COUNT(*) FROM pkg_uid_first_scanned_repair"));

                using (VpbSqlite3.Statement st = conn.Prepare(
                    "INSERT OR REPLACE INTO pkg_uid_first_scanned_repair(uid, first_scanned) VALUES('Not.Indexed.1', 42)"))
                    st.Step();

                VpbUidWhitespaceIdentityRepair.ConsumeApplied(conn);
                Assert.Equal(1, Scalar(conn, "SELECT COUNT(*) FROM pkg_uid_first_scanned_repair"));
            }
        }

        [Fact]
        public void CandidatesThatOnlyDifferByCaseAreSkipped()
        {
            using (VpbSqlite3.Connection conn = Open())
            {
                InsertPkg(conn, "Creator.Pack.1", 1000);

                int repaired = VpbUidWhitespaceIdentityRepair.Apply(conn, One("Creator.Pack.1", "creator.pack.1", 1000));

                Assert.Equal(0, repaired);
                Assert.Equal(new[] { "Creator.Pack.1" }, Column(conn, "SELECT uid FROM pkg").ToArray());
            }
        }

        [Theory]
        [InlineData(null, Exact)]
        [InlineData("", Exact)]
        [InlineData(Spaced, null)]
        [InlineData(Spaced, "")]
        public void IncompleteCandidatesAreSkipped(string stored, string exact)
        {
            using (VpbSqlite3.Connection conn = Open())
            {
                InsertPkg(conn, Spaced, 1000);
                Assert.Equal(0, VpbUidWhitespaceIdentityRepair.Apply(conn, One(stored, exact, 1000)));
                Assert.Equal(new[] { Spaced }, Column(conn, "SELECT uid FROM pkg").ToArray());
            }
        }

        [Fact]
        public void AnEmptyOrNullCandidateListIsANoOp()
        {
            using (VpbSqlite3.Connection conn = Open())
            {
                InsertPkg(conn, Spaced, 1000);

                Assert.Equal(0, VpbUidWhitespaceIdentityRepair.Apply(conn, null));
                Assert.Equal(0, VpbUidWhitespaceIdentityRepair.Apply(conn, new List<VpbUidWhitespaceIdentityRepair.Candidate>()));
                Assert.Equal(0, VpbUidWhitespaceIdentityRepair.Apply(null, One(Spaced, Exact, 1000)));

                Assert.Equal(new[] { Spaced }, Column(conn, "SELECT uid FROM pkg").ToArray());
            }
        }

        [Fact]
        public void OptionalTablesThatDoNotExistAreSkippedWithoutFailing()
        {
            using (VpbSqlite3.Connection conn = Open())
            {
                InsertPkg(conn, Spaced, 1000);

                Exception thrown = Record.Exception(
                    () => VpbUidWhitespaceIdentityRepair.Apply(conn, One(Spaced, Exact, 1000)));

                Assert.True(thrown == null,
                    "Repair must tolerate a database that predates some of the tables it remaps. This fixture " +
                    "deliberately creates only pkg / cat_mem / pkg_dep / cleanup_exclude. Got: " +
                    (thrown == null ? "" : HeadlessVam.DescribeUnwrapped(thrown)));
                Assert.Equal(new[] { Exact }, Column(conn, "SELECT uid FROM pkg").ToArray());
            }
        }

        [Fact]
        public void SeveralCandidatesAreRepairedInOnePass()
        {
            using (VpbSqlite3.Connection conn = Open())
            {
                InsertPkg(conn, "A Creator.One.1", 1000);
                InsertPkg(conn, "B Creator.Two.2", 2000);
                InsertPkg(conn, "Untouched.Three.1", 3000);

                var candidates = new List<VpbUidWhitespaceIdentityRepair.Candidate>
                {
                    new VpbUidWhitespaceIdentityRepair.Candidate("A Creator.One.1", "ACreator.One.1", 1000),
                    new VpbUidWhitespaceIdentityRepair.Candidate("B Creator.Two.2", "BCreator.Two.2", 2000),
                };

                Assert.Equal(2, VpbUidWhitespaceIdentityRepair.Apply(conn, candidates));
                Assert.Equal(
                    new[] { "ACreator.One.1", "BCreator.Two.2", "Untouched.Three.1" },
                    Column(conn, "SELECT uid FROM pkg").ToArray());
            }
        }

        [Fact]
        public void RemappingIntoAnExistingRowDoesNotDuplicateCategoryMembership()
        {
            using (VpbSqlite3.Connection conn = Open())
            {
                InsertPkg(conn, Spaced, 1000);
                InsertPkg(conn, Exact, 1000);
                InsertCatMem(conn, Spaced, "Saves/scene/one.json");
                InsertCatMem(conn, Exact, "Saves/scene/one.json");

                VpbUidWhitespaceIdentityRepair.Apply(conn, One(Spaced, Exact, 1000));

                Assert.Equal(1, Scalar(conn, "SELECT COUNT(*) FROM cat_mem"));
                Assert.Equal(0, Scalar(conn, "SELECT COUNT(*) FROM cat_mem WHERE pkg_uid='" + Spaced + "'"));
            }
        }

        [Fact]
        public void RequireReadCompletedRejectsAnIncompleteRead()
        {
            VpbUidWhitespaceIdentityRepair.RequireReadCompleted(VpbSqlite3.SqliteDone);
            Assert.ThrowsAny<Exception>(() => VpbUidWhitespaceIdentityRepair.RequireReadCompleted(VpbSqlite3.SqliteRow));
        }
    }
}
