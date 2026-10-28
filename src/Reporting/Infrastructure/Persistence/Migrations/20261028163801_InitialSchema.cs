using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Chubb.Reporting.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "reporting");

            migrationBuilder.CreateTable(
                name: "claim_snapshot",
                schema: "reporting",
                columns: table => new
                {
                    claim_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    market = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    reserve = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    assignee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_event_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_claim_snapshot", x => x.claim_id);
                });

            migrationBuilder.CreateTable(
                name: "processed_events",
                schema: "reporting",
                columns: table => new
                {
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_processed_events", x => x.event_id);
                });

            migrationBuilder.CreateTable(
                name: "pending_proposal",
                schema: "reporting",
                columns: table => new
                {
                    proposal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    claim_id = table.Column<Guid>(type: "uuid", nullable: false),
                    maker_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    proposed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pending_proposal", x => x.proposal_id);
                    table.ForeignKey(
                        name: "FK_pending_proposal_claim_snapshot_claim_id",
                        column: x => x.claim_id,
                        principalSchema: "reporting",
                        principalTable: "claim_snapshot",
                        principalColumn: "claim_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_claim_snapshot_assignee_id",
                schema: "reporting",
                table: "claim_snapshot",
                column: "assignee_id");

            migrationBuilder.CreateIndex(
                name: "ix_claim_snapshot_status_market",
                schema: "reporting",
                table: "claim_snapshot",
                columns: new[] { "status", "market" });

            migrationBuilder.CreateIndex(
                name: "ix_claim_snapshot_submitted_at",
                schema: "reporting",
                table: "claim_snapshot",
                column: "submitted_at");

            migrationBuilder.CreateIndex(
                name: "ix_pending_proposal_proposed_at",
                schema: "reporting",
                table: "pending_proposal",
                column: "proposed_at");

            migrationBuilder.CreateIndex(
                name: "ux_pending_proposal_claim_id",
                schema: "reporting",
                table: "pending_proposal",
                column: "claim_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pending_proposal",
                schema: "reporting");

            migrationBuilder.DropTable(
                name: "processed_events",
                schema: "reporting");

            migrationBuilder.DropTable(
                name: "claim_snapshot",
                schema: "reporting");
        }
    }
}
