using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChessGameD.Web.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppSettings",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppSettings", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "ImportBatches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 260, nullable: false),
                    ImportedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TotalGames = table.Column<int>(type: "INTEGER", nullable: false),
                    ImportedGames = table.Column<int>(type: "INTEGER", nullable: false),
                    SkippedGames = table.Column<int>(type: "INTEGER", nullable: false),
                    ErrorSummary = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportBatches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserName = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Email = table.Column<string>(type: "TEXT", maxLength: 254, nullable: true),
                    PasswordHash = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    IsAdmin = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    MustChangePassword = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastLoginAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Games",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Event = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Site = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Round = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Date = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Termination = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    TimeControl = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Eco = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    Opening = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    Variation = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    White = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Black = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    WhiteElo = table.Column<int>(type: "INTEGER", nullable: true),
                    BlackElo = table.Column<int>(type: "INTEGER", nullable: true),
                    Result = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    InitialFen = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    PgnText = table.Column<string>(type: "TEXT", nullable: false),
                    PlyCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ImportedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ImportBatchId = table.Column<int>(type: "INTEGER", nullable: false),
                    OwnerUserId = table.Column<int>(type: "INTEGER", nullable: true),
                    OwnerAnonKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Games", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Games_ImportBatches_ImportBatchId",
                        column: x => x.ImportBatchId,
                        principalTable: "ImportBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Games_Users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Analyses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GameId = table.Column<int>(type: "INTEGER", nullable: false),
                    Ply = table.Column<int>(type: "INTEGER", nullable: false),
                    Depth = table.Column<int>(type: "INTEGER", nullable: false),
                    ScoreCp = table.Column<int>(type: "INTEGER", nullable: true),
                    ScoreMate = table.Column<int>(type: "INTEGER", nullable: true),
                    Pv = table.Column<string>(type: "TEXT", nullable: false),
                    MultiPvJson = table.Column<string>(type: "TEXT", nullable: false),
                    Nodes = table.Column<long>(type: "INTEGER", nullable: false),
                    Nps = table.Column<int>(type: "INTEGER", nullable: false),
                    TimeMs = table.Column<int>(type: "INTEGER", nullable: false),
                    EngineVersion = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Analyses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Analyses_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Moves",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GameId = table.Column<int>(type: "INTEGER", nullable: false),
                    Ply = table.Column<int>(type: "INTEGER", nullable: false),
                    San = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Uci = table.Column<string>(type: "TEXT", maxLength: 10, nullable: true),
                    Fen = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Comment = table.Column<string>(type: "TEXT", nullable: true),
                    Nag = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Moves", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Moves_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Analyses_GameId_Ply_EngineVersion",
                table: "Analyses",
                columns: new[] { "GameId", "Ply", "EngineVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Games_Date",
                table: "Games",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_Games_ImportBatchId",
                table: "Games",
                column: "ImportBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_Games_OwnerAnonKey",
                table: "Games",
                column: "OwnerAnonKey");

            migrationBuilder.CreateIndex(
                name: "IX_Games_OwnerUserId",
                table: "Games",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Games_White_Black",
                table: "Games",
                columns: new[] { "White", "Black" });

            migrationBuilder.CreateIndex(
                name: "IX_ImportBatches_ImportedAt",
                table: "ImportBatches",
                column: "ImportedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Moves_GameId_Ply",
                table: "Moves",
                columns: new[] { "GameId", "Ply" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_UserName",
                table: "Users",
                column: "UserName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Analyses");

            migrationBuilder.DropTable(
                name: "AppSettings");

            migrationBuilder.DropTable(
                name: "Moves");

            migrationBuilder.DropTable(
                name: "Games");

            migrationBuilder.DropTable(
                name: "ImportBatches");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
