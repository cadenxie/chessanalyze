using System.Reflection;

namespace ChessGameD.Web.Services.App;

/// <summary>
/// 项目的 Git 信息:仓库地址、分支、提交号。
/// 构建时由 MSBuild 直接读取 .git 目录里的文本文件写进程序集元数据,
/// 所以发布出去以后也不需要本机装 git,也不会在运行时去碰工作目录。
/// </summary>
public sealed class GitInfo
{
    /// <summary>项目仓库地址(默认值)。</summary>
    public const string DefaultRepositoryUrl = "https://github.com/cadenxie/chessanalyze";

    /// <summary>
    /// 构造一份 Git 信息。
    /// </summary>
    /// <param name="branch">分支名,例如 main;没有则为 null 或空串。</param>
    /// <param name="commit">短提交号(8 位);没有则为 null 或空串。</param>
    /// <param name="repositoryUrl">仓库地址,默认就是项目自己的仓库。</param>
    public GitInfo(string? branch, string? commit, string? repositoryUrl = null)
    {
        Branch = branch?.Trim() ?? "";
        Commit = commit?.Trim() ?? "";
        RepositoryUrl = string.IsNullOrWhiteSpace(repositoryUrl) ? DefaultRepositoryUrl : repositoryUrl.Trim();
    }

    /// <summary>项目仓库地址。</summary>
    public string RepositoryUrl { get; }

    /// <summary>分支名,例如 main;没有则为空串。</summary>
    public string Branch { get; }

    /// <summary>短提交号(8 位);没有则为空串。</summary>
    public string Commit { get; }

    /// <summary>是否拿到了分支或提交号。</summary>
    public bool HasInfo => Branch.Length > 0 || Commit.Length > 0;

    /// <summary>顶部菜单里显示的文本,例如 <c>main @ a442ee8</c>;没有信息时显示「源码」。</summary>
    public string DisplayText
    {
        get
        {
            if (Branch.Length > 0 && Commit.Length > 0)
            {
                return $"{Branch} @ {Commit}";
            }

            if (Branch.Length > 0)
            {
                return Branch;
            }

            if (Commit.Length > 0)
            {
                return Commit;
            }

            return "源码";
        }
    }

    /// <summary>指向该提交在 GitHub 上的页面;没有提交号时指向仓库首页。</summary>
    public string CommitUrl => Commit.Length > 0 ? $"{RepositoryUrl}/commit/{Commit}" : RepositoryUrl;

    /// <summary>悬停提示:说明这个角标是从哪来的。</summary>
    public string Tooltip => HasInfo
        ? $"分支 {Branch} · 提交 {Commit}"
        : $"项目源码仓库:{RepositoryUrl}";

    /// <summary>
    /// 从程序集元数据里读构建时写进去的 Git 信息。
    /// </summary>
    /// <param name="assembly">目标程序集,通常是 Web 项目自己的程序集。</param>
    public static GitInfo FromAssembly(Assembly assembly)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var attr in assembly.GetCustomAttributes<AssemblyMetadataAttribute>())
        {
            if (!string.IsNullOrEmpty(attr.Key))
            {
                metadata[attr.Key] = attr.Value ?? "";
            }
        }

        metadata.TryGetValue("GitBranch", out var branch);
        metadata.TryGetValue("GitCommit", out var commit);
        metadata.TryGetValue("GitRepository", out var repositoryUrl);
        return new GitInfo(branch, commit, repositoryUrl);
    }
}
