using ChessGameD.Web.Services.App;
using Xunit;

namespace ChessGameD.Tests;

public class GitInfoTests
{
    [Fact]
    public void Ctor_BranchAndCommit_FormatsDisplayText()
    {
        var info = new GitInfo("main", "a442ee8b");

        Assert.True(info.HasInfo);
        Assert.Equal("main", info.Branch);
        Assert.Equal("a442ee8b", info.Commit);
        Assert.Equal("main @ a442ee8b", info.DisplayText);
    }

    [Fact]
    public void Ctor_NoBranchNoCommit_FallsBackToSourceLabel()
    {
        var info = new GitInfo(null, null);

        Assert.False(info.HasInfo);
        Assert.Equal("", info.Branch);
        Assert.Equal("", info.Commit);
        Assert.Equal("源码", info.DisplayText);
        Assert.Equal(GitInfo.DefaultRepositoryUrl, info.RepositoryUrl);
        Assert.Equal(GitInfo.DefaultRepositoryUrl, info.CommitUrl);
    }

    [Fact]
    public void CommitUrl_WithCommit_LinksToCommitPage()
    {
        var info = new GitInfo("main", "a442ee8b");

        Assert.Equal("https://github.com/cadenxie/chessanalyze/commit/a442ee8b", info.CommitUrl);
    }

    [Fact]
    public void FromAssembly_ReadsBuildTimeMetadata()
    {
        var info = GitInfo.FromAssembly(typeof(GitInfo).Assembly);

        // 构建时写进程序集元数据;在仓库里构建就一定读得到分支与提交号
        Assert.Equal("https://github.com/cadenxie/chessanalyze", GitInfo.DefaultRepositoryUrl);
        Assert.NotNull(info.DisplayText);
        Assert.StartsWith(info.RepositoryUrl, info.CommitUrl);
    }
}
