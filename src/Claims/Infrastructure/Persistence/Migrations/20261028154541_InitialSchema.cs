using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Chubb.Claims.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // xmin is a PostgreSQL system column: the scaffolded "xmin" column was removed by hand on purpose.
            migrationBuilder.EnsureSchema(
                name: "claims");

            migrationBuilder.CreateTable(
                name: "claim_number_sequences",
                schema: "claims",
                columns: table => new
                {
                    market = table.Column<string>(type: "char(2)", nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    last_value = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_claim_number_sequences", x => new { x.market, x.year });
                    table.CheckConstraint("ck_claim_number_sequences_last_value", "last_value BETWEEN 1 AND 999999");
                });

            migrationBuilder.CreateTable(
                name: "policies",
                schema: "claims",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    policy_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    policyholder_id = table.Column<Guid>(type: "uuid", nullable: false),
                    market = table.Column<string>(type: "char(2)", nullable: false),
                    currency = table.Column<string>(type: "char(3)", nullable: false),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: false),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: false),
                    coverage_limit = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    excess = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_policies", x => x.id);
                    table.CheckConstraint("ck_policies_amounts", "coverage_limit >= 0 AND excess >= 0");
                    table.CheckConstraint("ck_policies_market", "market IN ('SG','HK','MY','TH','ID','AU')");
                    table.CheckConstraint("ck_policies_market_currency", "(market = 'SG' AND currency = 'SGD') OR (market = 'HK' AND currency = 'HKD') OR (market = 'MY' AND currency = 'MYR') OR (market = 'TH' AND currency = 'THB') OR (market = 'ID' AND currency = 'IDR') OR (market = 'AU' AND currency = 'AUD')");
                    table.CheckConstraint("ck_policies_period", "valid_from <= valid_to");
                });

            migrationBuilder.CreateTable(
                name: "claims",
                schema: "claims",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    claim_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    policy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    claimant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    market = table.Column<string>(type: "char(2)", nullable: false),
                    currency = table.Column<string>(type: "char(3)", nullable: false),
                    incident_date = table.Column<DateOnly>(type: "date", nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    assigned_officer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    paid = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    payee_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    payee_on_file = table.Column<bool>(type: "boolean", nullable: true),
                    assessed_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    estimated_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    net_payable = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    payable = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    reserve = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_claims", x => x.id);
                    table.CheckConstraint("ck_claims_amounts", "estimated_amount >= 0 AND reserve >= 0 AND COALESCE(assessed_amount, 0) >= 0 AND COALESCE(net_payable, 0) >= 0 AND COALESCE(payable, 0) >= 0 AND COALESCE(paid, 0) >= 0");
                    table.CheckConstraint("ck_claims_assignment", "(status = 'Submitted') = (assigned_officer_id IS NULL)");
                    table.CheckConstraint("ck_claims_claim_number", "claim_number ~ '^CLM-[A-Z]{2}-[0-9]{4}-[0-9]{6}$'");
                    table.CheckConstraint("ck_claims_market_currency", "(market = 'SG' AND currency = 'SGD') OR (market = 'HK' AND currency = 'HKD') OR (market = 'MY' AND currency = 'MYR') OR (market = 'TH' AND currency = 'THB') OR (market = 'ID' AND currency = 'IDR') OR (market = 'AU' AND currency = 'AUD')");
                    table.CheckConstraint("ck_claims_rejected_reserve", "status <> 'Rejected' OR reserve = 0");
                    table.CheckConstraint("ck_claims_status", "status IN ('Submitted','UnderReview','InfoRequested','PendingDecisionApproval','Approved','Rejected','PendingSettlementApproval','Settling','Settled')");
                    table.CheckConstraint("ck_claims_type", "type IN ('Motor','Property')");
                    table.ForeignKey(
                        name: "fk_claims_policy_id",
                        column: x => x.policy_id,
                        principalSchema: "claims",
                        principalTable: "policies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "claim_motor_details",
                schema: "claims",
                columns: table => new
                {
                    claim_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vehicle_registration = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    incident_location = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_claim_motor_details", x => x.claim_id);
                    table.ForeignKey(
                        name: "FK_claim_motor_details_claims_claim_id",
                        column: x => x.claim_id,
                        principalSchema: "claims",
                        principalTable: "claims",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "claim_property_details",
                schema: "claims",
                columns: table => new
                {
                    claim_id = table.Column<Guid>(type: "uuid", nullable: false),
                    property_address = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    damage_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_claim_property_details", x => x.claim_id);
                    table.ForeignKey(
                        name: "FK_claim_property_details_claims_claim_id",
                        column: x => x.claim_id,
                        principalSchema: "claims",
                        principalTable: "claims",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "proposals",
                schema: "claims",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    outcome = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    reason_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    rationale = table.Column<string>(type: "text", nullable: false),
                    justification = table.Column<string>(type: "text", nullable: true),
                    currency = table.Column<string>(type: "char(3)", nullable: true),
                    maker_id = table.Column<Guid>(type: "uuid", nullable: false),
                    checker_id = table.Column<Guid>(type: "uuid", nullable: true),
                    state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    resolution_reason = table.Column<string>(type: "text", nullable: true),
                    proposed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    claim_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "Decision"),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_proposals", x => x.id);
                    table.UniqueConstraint("ux_proposals_claim_id_id", x => new { x.claim_id, x.id });
                    table.CheckConstraint("ck_proposals_approve_amount", "outcome <> 'Approve' OR amount IS NOT NULL");
                    table.CheckConstraint("ck_proposals_checker_not_maker", "checker_id IS NULL OR checker_id <> maker_id");
                    table.CheckConstraint("ck_proposals_kind", "kind IN ('Decision','Settlement')");
                    table.CheckConstraint("ck_proposals_open_unresolved", "(state = 'Open') = (resolved_at IS NULL)");
                    table.CheckConstraint("ck_proposals_outcome", "outcome IN ('Approve','Reject')");
                    table.CheckConstraint("ck_proposals_reject_reason", "outcome <> 'Reject' OR reason_code IS NOT NULL");
                    table.CheckConstraint("ck_proposals_state", "state IN ('Open','Confirmed','SentBack','Withdrawn')");
                    table.ForeignKey(
                        name: "fk_proposals_claim_id",
                        column: x => x.claim_id,
                        principalSchema: "claims",
                        principalTable: "claims",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "claim_history",
                schema: "claims",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    claim_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    event_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    from_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    to_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_role = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    summary = table.Column<string>(type: "text", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: true),
                    visibility = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    proposal_id = table.Column<Guid>(type: "uuid", nullable: true),
                    maker_id = table.Column<Guid>(type: "uuid", nullable: true),
                    checker_id = table.Column<Guid>(type: "uuid", nullable: true),
                    referenced_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reserve_before = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    reserve_after = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_claim_history", x => x.id);
                    table.CheckConstraint("ck_claim_history_actor_role", "actor_role IN ('Claimant','Officer','Manager','System')");
                    table.CheckConstraint("ck_claim_history_event_type", "event_type IN ('Submitted','Assigned','Reassigned','InformationRequested','InformationProvided','Assessed','DecisionProposed','DecisionUnderReview','DecisionConfirmed','DecisionAnnounced','DecisionSentBack','ProposalWithdrawn','Correction','Refused')");
                    table.CheckConstraint("ck_claim_history_from_status", "from_status IS NULL OR from_status IN ('Submitted','UnderReview','InfoRequested','PendingDecisionApproval','Approved','Rejected','PendingSettlementApproval','Settling','Settled')");
                    table.CheckConstraint("ck_claim_history_public_has_no_staff_data", "visibility <> 'Public' OR (maker_id IS NULL AND checker_id IS NULL AND reason IS NULL AND referenced_entry_id IS NULL)");
                    table.CheckConstraint("ck_claim_history_reserve", "(reserve_before IS NULL OR (reserve_before >= 0 AND reserve_after IS NOT NULL)) AND (reserve_after IS NULL OR (reserve_after >= 0 AND (reserve_before IS NOT NULL OR event_type = 'Submitted')))");
                    table.CheckConstraint("ck_claim_history_status_pair", "(from_status IS NULL AND to_status IS NULL) OR (to_status IS NOT NULL AND (from_status IS NOT NULL OR event_type = 'Submitted'))");
                    table.CheckConstraint("ck_claim_history_summary", "summary <> ''");
                    table.CheckConstraint("ck_claim_history_to_status", "to_status IS NULL OR to_status IN ('Submitted','UnderReview','InfoRequested','PendingDecisionApproval','Approved','Rejected','PendingSettlementApproval','Settling','Settled')");
                    table.CheckConstraint("ck_claim_history_visibility", "visibility IN ('Public','Internal')");
                    table.ForeignKey(
                        name: "fk_claim_history_claim_id",
                        column: x => x.claim_id,
                        principalSchema: "claims",
                        principalTable: "claims",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_claim_history_claim_id_proposal_id",
                        columns: x => new { x.claim_id, x.proposal_id },
                        principalSchema: "claims",
                        principalTable: "proposals",
                        principalColumns: new[] { "claim_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_claim_history_claim_id_proposal_id",
                schema: "claims",
                table: "claim_history",
                columns: new[] { "claim_id", "proposal_id" });

            migrationBuilder.CreateIndex(
                name: "ix_claim_history_claim_id_occurred_at_id",
                schema: "claims",
                table: "claim_history",
                columns: new[] { "claim_id", "occurred_at", "id" });

            migrationBuilder.CreateIndex(
                name: "IX_claims_policy_id",
                schema: "claims",
                table: "claims",
                column: "policy_id");

            migrationBuilder.CreateIndex(
                name: "ix_claims_assigned_officer_id",
                schema: "claims",
                table: "claims",
                column: "assigned_officer_id");

            migrationBuilder.CreateIndex(
                name: "ix_claims_claimant_id_submitted_at",
                schema: "claims",
                table: "claims",
                columns: new[] { "claimant_id", "submitted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_claims_status_market",
                schema: "claims",
                table: "claims",
                columns: new[] { "status", "market" });

            migrationBuilder.CreateIndex(
                name: "ix_claims_submitted_at",
                schema: "claims",
                table: "claims",
                column: "submitted_at");

            migrationBuilder.CreateIndex(
                name: "ux_claims_claim_number",
                schema: "claims",
                table: "claims",
                column: "claim_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_policies_policy_number",
                schema: "claims",
                table: "policies",
                column: "policy_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_proposals_one_open_per_claim",
                schema: "claims",
                table: "proposals",
                column: "claim_id",
                unique: true,
                filter: "state = 'Open'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "claim_history",
                schema: "claims");

            migrationBuilder.DropTable(
                name: "claim_motor_details",
                schema: "claims");

            migrationBuilder.DropTable(
                name: "claim_number_sequences",
                schema: "claims");

            migrationBuilder.DropTable(
                name: "claim_property_details",
                schema: "claims");

            migrationBuilder.DropTable(
                name: "proposals",
                schema: "claims");

            migrationBuilder.DropTable(
                name: "claims",
                schema: "claims");

            migrationBuilder.DropTable(
                name: "policies",
                schema: "claims");
        }
    }
}
