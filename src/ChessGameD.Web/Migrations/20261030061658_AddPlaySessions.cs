using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChessGameD.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddPlaySessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlaySessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OwnerUserId = table.Column<int>(type: "INTEGER", nullable: true),
                    OwnerAnonKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Level = table.Column<int>(type: "INTEGER", nullable: false),
                    InitialFen = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    CurrentFen = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    PlyCount = table.Column<int>(type: "INTEGER", nullable: false),
                    MovesJson = table.Column<string>(type: "TEXT", nullable: false),
                    EvalsJson = table.Column<string>(type: "TEXT", nullable: false),
                    TakebacksJson = table.Column<string>(type: "TEXT", nullable: false),
                    HintsLeft = table.Column<int>(type: "INTEGER", nullable: false),
                    HintsUsed = table.Column<int>(type: "INTEGER", nullable: false),
                    TakebacksLeft = table.Column<int>(type: "INTEGER", nullable: false),
                    TakebacksUsed = table.Column<int>(type: "INTEGER", nullable: false),
                    MistakeCount = table.Column<int>(type: "INTEGER", nullable: false),
                    BlunderCount = table.Column<int>(type: "INTEGER", nullable: false),
                    MissedWinCount = table.Column<int>(type: "INTEGER", nullable: false),
                    GameId = table.Column<int>(type: "INTEGER", nullable: true),
                    Result = table.Column<string>(type: "TEXT", maxLength: 10, nullable: true),
                    Termination = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlaySessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlaySessions_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_PlaySessions_Users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlaySessions_GameId",
                table: "PlaySessions",
                column: "GameId");

            migrationBuilder.CreateIndex(
                name: "IX_PlaySessions_OwnerAnonKey",
                table: "PlaySessions",
                column: "OwnerAnonKey");

            migrationBuilder.CreateIndex(
                name: "IX_PlaySessions_OwnerUserId",
                table: "PlaySessions",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PlaySessions_Status",
                table: "PlaySessions",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlaySessions");
        }
    }
}
