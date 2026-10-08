using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "anonymous_users",
                columns: table => new
                {
                    anonymous_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    session_token_hash = table.Column<string>(type: "char(64)", nullable: false),
                    display_name = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    avatar_color = table.Column<string>(type: "char(7)", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    last_seen_at_utc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    expires_at_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_anonymous_users", x => x.anonymous_user_id);
                });

            migrationBuilder.CreateTable(
                name: "boards",
                columns: table => new
                {
                    board_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    public_id = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    owner_token_hash = table.Column<string>(type: "char(64)", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    updated_at_utc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    last_activity_at_utc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    expires_at_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_boards", x => x.board_id);
                    table.CheckConstraint("CK_boards_expiry", "[expires_at_utc] > [created_at_utc]");
                    table.CheckConstraint("CK_boards_last_activity", "[last_activity_at_utc] >= [created_at_utc]");
                });

            migrationBuilder.CreateTable(
                name: "board_columns",
                columns: table => new
                {
                    board_column_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    board_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    column_key = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    sort_order = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    created_at_utc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    updated_at_utc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_board_columns", x => x.board_column_id);
                    table.UniqueConstraint("AK_board_columns_column_board", x => new { x.board_column_id, x.board_id });
                    table.ForeignKey(
                        name: "FK_board_columns_boards_board_id",
                        column: x => x.board_id,
                        principalTable: "boards",
                        principalColumn: "board_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "board_participants",
                columns: table => new
                {
                    board_participant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    board_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    anonymous_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    joined_at_utc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    last_seen_at_utc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_board_participants", x => x.board_participant_id);
                    table.UniqueConstraint("AK_board_participants_participant_board", x => new { x.board_participant_id, x.board_id });
                    table.ForeignKey(
                        name: "FK_board_participants_anonymous_users_anonymous_user_id",
                        column: x => x.anonymous_user_id,
                        principalTable: "anonymous_users",
                        principalColumn: "anonymous_user_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_board_participants_boards_board_id",
                        column: x => x.board_id,
                        principalTable: "boards",
                        principalColumn: "board_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "task_items",
                columns: table => new
                {
                    task_item_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    board_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    board_column_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    title = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    description_markdown = table.Column<string>(type: "nvarchar(max)", maxLength: 20000, nullable: true),
                    priority = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false, defaultValue: "Medium"),
                    sort_order = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false, defaultValue: 0m),
                    assigned_to_participant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    start_date = table.Column<DateOnly>(type: "date", nullable: true),
                    due_date = table.Column<DateOnly>(type: "date", nullable: true),
                    created_by_participant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    updated_at_utc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_task_items", x => x.task_item_id);
                    table.CheckConstraint("CK_task_items_dates", "[start_date] IS NULL OR [due_date] IS NULL OR [start_date] <= [due_date]");
                    table.CheckConstraint("CK_task_items_priority", "[priority] IN ('Low', 'Medium', 'High')");
                    table.CheckConstraint("CK_task_items_sort_order", "[sort_order] >= 0");
                    table.ForeignKey(
                        name: "FK_task_items_board_columns_board_column_id_board_id",
                        columns: x => new { x.board_column_id, x.board_id },
                        principalTable: "board_columns",
                        principalColumns: new[] { "board_column_id", "board_id" });
                    table.ForeignKey(
                        name: "FK_task_items_board_participants_assigned_to_participant_id_board_id",
                        columns: x => new { x.assigned_to_participant_id, x.board_id },
                        principalTable: "board_participants",
                        principalColumns: new[] { "board_participant_id", "board_id" });
                    table.ForeignKey(
                        name: "FK_task_items_board_participants_created_by_participant_id_board_id",
                        columns: x => new { x.created_by_participant_id, x.board_id },
                        principalTable: "board_participants",
                        principalColumns: new[] { "board_participant_id", "board_id" });
                    table.ForeignKey(
                        name: "FK_task_items_boards_board_id",
                        column: x => x.board_id,
                        principalTable: "boards",
                        principalColumn: "board_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_anonymous_users_expires_at_utc",
                table: "anonymous_users",
                column: "expires_at_utc");

            migrationBuilder.CreateIndex(
                name: "UX_anonymous_users_session_token_hash",
                table: "anonymous_users",
                column: "session_token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_board_columns_board_sort",
                table: "board_columns",
                columns: new[] { "board_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "UX_board_columns_board_key",
                table: "board_columns",
                columns: new[] { "board_id", "column_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_board_participants_anonymous_user_id",
                table: "board_participants",
                column: "anonymous_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_board_participants_board_last_seen",
                table: "board_participants",
                columns: new[] { "board_id", "last_seen_at_utc" });

            migrationBuilder.CreateIndex(
                name: "UX_board_participants_board_user",
                table: "board_participants",
                columns: new[] { "board_id", "anonymous_user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_boards_expires_at_utc",
                table: "boards",
                column: "expires_at_utc");

            migrationBuilder.CreateIndex(
                name: "UX_boards_owner_token_hash",
                table: "boards",
                column: "owner_token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_boards_public_id",
                table: "boards",
                column: "public_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_task_items_assigned_to_participant_id_board_id",
                table: "task_items",
                columns: new[] { "assigned_to_participant_id", "board_id" });

            migrationBuilder.CreateIndex(
                name: "IX_task_items_board_assignee",
                table: "task_items",
                columns: new[] { "board_id", "assigned_to_participant_id" });

            migrationBuilder.CreateIndex(
                name: "IX_task_items_board_column_id_board_id",
                table: "task_items",
                columns: new[] { "board_column_id", "board_id" });

            migrationBuilder.CreateIndex(
                name: "IX_task_items_board_column_sort",
                table: "task_items",
                columns: new[] { "board_id", "board_column_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "IX_task_items_created_by_participant_id_board_id",
                table: "task_items",
                columns: new[] { "created_by_participant_id", "board_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "task_items");

            migrationBuilder.DropTable(
                name: "board_columns");

            migrationBuilder.DropTable(
                name: "board_participants");

            migrationBuilder.DropTable(
                name: "anonymous_users");

            migrationBuilder.DropTable(
                name: "boards");
        }
    }
}
