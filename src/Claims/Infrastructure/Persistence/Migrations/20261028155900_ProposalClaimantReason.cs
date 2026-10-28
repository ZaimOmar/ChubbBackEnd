using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Chubb.Claims.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProposalClaimantReason : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "claimant_reason",
                schema: "claims",
                table: "proposals",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_proposals_reject_claimant_reason",
                schema: "claims",
                table: "proposals",
                sql: "outcome <> 'Reject' OR claimant_reason IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_proposals_reject_claimant_reason",
                schema: "claims",
                table: "proposals");

            migrationBuilder.DropColumn(
                name: "claimant_reason",
                schema: "claims",
                table: "proposals");
        }
    }
}
