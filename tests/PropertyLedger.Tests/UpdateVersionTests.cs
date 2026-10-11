using PropertyLedger.Core.Services;

namespace PropertyLedger.Tests;

/// <summary>在线更新：版本比较与 GitHub latest release JSON 解析。</summary>
public class UpdateVersionTests
{
    [Theory]
    [InlineData("1.9", "1.8", true)]
    [InlineData("v1.9", "1.8", true)]
    [InlineData("V2.0", "1.9", true)]
    [InlineData("1.10", "1.9", true)]
    [InlineData("1.9.1", "1.9", true)]
    [InlineData("1.9", "1.9", false)]
    [InlineData("1.8", "1.9", false)]
    [InlineData("1.9-beta", "1.8", true)]  // 预发布后缀被忽略（GitHub latest 接口本就不返回预发布版）
    [InlineData("1.9-beta", "1.9", false)]
    [InlineData("", "1.8", false)]
    [InlineData("abc", "1.8", false)]
    [InlineData("v", "1.8", false)]
    public void IsNewerVersion_ComparesSemverish(string latest, string current, bool expected)
        => Assert.Equal(expected, UpdateService.IsNewerVersion(latest, current));

    [Fact]
    public void ParseRelease_ReadsGitHubLatestJson()
    {
        const string json = """
        {
          "tag_name": "v1.10",
          "html_url": "https://github.com/szboboxing/property-ledger/releases/tag/v1.10",
          "published_at": "2026-10-12T08:00:00Z",
          "body": "- 新功能 A\n- 修复 B"
        }
        """;

        var r = UpdateService.ParseRelease("1.9", json);

        Assert.True(r.HasUpdate);
        Assert.Equal("1.9", r.CurrentVersion);
        Assert.Equal("1.10", r.LatestVersion);
        Assert.Equal("https://github.com/szboboxing/property-ledger/releases/tag/v1.10", r.ReleaseUrl);
        Assert.Equal("- 新功能 A\n- 修复 B", r.ReleaseNotes);
        Assert.NotNull(r.PublishedAt);
        Assert.Equal(2026, r.PublishedAt!.Value.Year);
    }

    [Fact]
    public void ParseRelease_SameVersion_NoUpdate()
    {
        const string json = """{ "tag_name": "v1.9", "html_url": "https://x/y" }""";
        var r = UpdateService.ParseRelease("1.9", json);
        Assert.False(r.HasUpdate);
        Assert.Equal("1.9", r.LatestVersion);
    }

    [Fact]
    public void ParseRelease_EmptyBody_NotesNull()
    {
        const string json = """{ "tag_name": "v2.0", "body": "" }""";
        var r = UpdateService.ParseRelease("1.9", json);
        Assert.True(r.HasUpdate);
        Assert.Null(r.ReleaseNotes);
    }
}
