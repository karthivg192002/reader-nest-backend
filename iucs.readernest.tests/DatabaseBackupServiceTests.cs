using iucs.readernest.application.Services;
using Xunit;

namespace iucs.readernest.tests;

public class DatabaseBackupServiceTests
{
    [Theory]
    [InlineData("", "reader_nest_20261008_020001.dump", "/var/backups/postgres/reader_nest_20261008_020001.dump")]
    [InlineData("hourly", "reader_nest_20261008_0615IST.dump", "/var/backups/postgres/hourly/reader_nest_20261008_0615IST.dump")]
    [InlineData(null, "ALL_databases_187_20261007_2223IST.sql.gz", "/var/backups/postgres/ALL_databases_187_20261007_2223IST.sql.gz")]
    public void Valid_folder_and_name_map_to_backup_path(string? folder, string file, string expected)
    {
        Assert.Equal(expected, DatabaseBackupService.RemotePath(folder, file));
    }

    [Theory]
    [InlineData("", "../../etc/shadow")]
    [InlineData("", "x.dump; rm -rf /")]
    [InlineData("", "/etc/passwd.dump")]
    [InlineData("", "a..b.dump")]
    [InlineData("", ".hidden.dump")]
    [InlineData("", "notes.txt")]
    [InlineData("", "")]
    [InlineData("hourly/..", "x.dump")]
    [InlineData("other", "x.dump")]
    [InlineData("", "x.dump.part")]
    public void Anything_else_is_rejected(string folder, string file)
    {
        Assert.Throws<ArgumentException>(() => DatabaseBackupService.RemotePath(folder, file));
    }

    [Fact]
    public void Find_output_is_classified_and_sorted_newest_first()
    {
        var output = string.Join('\n',
            "/var/backups/postgres|reader_nest_20261008_020001.dump|4870000|1791424801.5",
            "/var/backups/postgres/hourly|reader_nest_20261008_0615IST.dump|4880000|1791425100.0",
            "/var/backups/postgres/hourly|reader_nest_BEFORE_payout_total_fix_2308IST.dump|4850000|1791400000.0",
            "/var/backups/postgres|ALL_databases_187_20261007_2223IST.sql.gz|15912286|1791390000.0",
            "/var/backups/postgres/other|x.dump|1|1791425200.0",
            "/var/backups/postgres|reader_nest_20261008.dump.part|1|1791425300.0",
            "garbage line",
            "");

        var backups = DatabaseBackupService.ParseFindOutput(output);

        Assert.Equal(
            new[] { "hourly", "nightly", "manual", "manual" },
            backups.Select(b => b.Kind).ToArray());
        Assert.Equal("reader_nest_20261008_0615IST.dump", backups[0].FileName);
        Assert.Equal("hourly", backups[0].Folder);
        Assert.Equal("", backups[1].Folder);
        Assert.Equal(4880000, backups[0].SizeBytes);
        Assert.Equal(DateTime.UnixEpoch.AddSeconds(1791425100), backups[0].CreatedAtUtc);
    }
}
