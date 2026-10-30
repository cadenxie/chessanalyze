using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ChessGameD.Web.Data;

/// <summary>
/// 设计时用的 DbContext 工厂(给 <c>dotnet ef</c> 用)。
///
/// 用它而不是让工具去跑 <c>Program.cs</c>:工具会真的把应用启动一遍,
/// 容易碰到启动逻辑里的副作用。这里指向一个专门的文件,不会碰到正式库。
/// </summary>
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    /// <summary>设计时用的库文件(放在 App_Data 下,已 gitignore)。</summary>
    public const string DesignTimeConnectionString = "Data Source=App_Data/chessgame-design.db";

    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(DesignTimeConnectionString)
            .Options;

        return new AppDbContext(options);
    }
}
