using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kart.Wishlist.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:pgcrypto", ",,");

            migrationBuilder.CreateTable(
                name: "wishlist_alert_dedup",
                columns: table => new
                {
                    dedup_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku = table.Column<string>(type: "text", nullable: false),
                    price_observed = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    alerted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wishlist_alert_dedup", x => x.dedup_id);
                });

            migrationBuilder.CreateTable(
                name: "wishlist_audit_log",
                columns: table => new
                {
                    entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_name = table.Column<string>(type: "text", nullable: false),
                    actor_id = table.Column<string>(type: "text", nullable: false),
                    actor_type = table.Column<string>(type: "text", nullable: false),
                    action = table.Column<string>(type: "text", nullable: false),
                    entity_type = table.Column<string>(type: "text", nullable: false),
                    entity_id = table.Column<string>(type: "text", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    metadata = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wishlist_audit_log", x => x.entry_id);
                });

            migrationBuilder.CreateTable(
                name: "wishlist_entries",
                columns: table => new
                {
                    entry_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku = table.Column<string>(type: "text", nullable: false),
                    reference_price = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    last_alerted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    added_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wishlist_entries", x => x.entry_id);
                    table.CheckConstraint("ck_wishlist_entries_status", "status IN ('active', 'stale')");
                });

            migrationBuilder.CreateTable(
                name: "wishlist_outbox_events",
                columns: table => new
                {
                    outbox_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku = table.Column<string>(type: "text", nullable: false),
                    event_type = table.Column<string>(type: "text", nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    projected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wishlist_outbox_events", x => x.outbox_id);
                    table.CheckConstraint("ck_wishlist_outbox_events_event_type", "event_type IN ('WishlistPriceAlertTriggered', 'WishlistEntryMutated')");
                });

            migrationBuilder.CreateIndex(
                name: "idx_wishlist_alert_dedup_user_sku",
                table: "wishlist_alert_dedup",
                columns: new[] { "user_id", "sku" });

            migrationBuilder.CreateIndex(
                name: "uq_wishlist_alert_dedup",
                table: "wishlist_alert_dedup",
                columns: new[] { "user_id", "sku", "price_observed" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_wishlist_audit_log_entity",
                table: "wishlist_audit_log",
                columns: new[] { "entity_type", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "idx_wishlist_entries_sku",
                table: "wishlist_entries",
                column: "sku",
                filter: "status = 'active'");

            migrationBuilder.CreateIndex(
                name: "idx_wishlist_entries_status_sku",
                table: "wishlist_entries",
                columns: new[] { "status", "sku" });

            migrationBuilder.CreateIndex(
                name: "idx_wishlist_entries_user_status",
                table: "wishlist_entries",
                columns: new[] { "user_id", "status" });

            migrationBuilder.CreateIndex(
                name: "uq_wishlist_entries_user_sku",
                table: "wishlist_entries",
                columns: new[] { "user_id", "sku" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_wishlist_outbox_unprojected",
                table: "wishlist_outbox_events",
                column: "occurred_at",
                filter: "projected_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "idx_wishlist_outbox_unpublished",
                table: "wishlist_outbox_events",
                column: "occurred_at",
                filter: "published_at IS NULL");

            // database-design.md's Row-Level Security Policy section — the two tables with a
            // genuine per-row user_id ownership concept. wishlist_outbox_events is deliberately
            // NOT covered: no end-user- or admin-facing request path ever queries it directly
            // (written exclusively by the digest-flush process, read exclusively by the Outbox
            // poller — both system:*-attributed), the same carve-out kart-identity-service's own
            // outbox_events table draws.
            migrationBuilder.Sql(
                """
                ALTER TABLE wishlist_entries ENABLE ROW LEVEL SECURITY;
                CREATE POLICY wishlist_entries_owner_or_system ON wishlist_entries
                    USING (
                        user_id = current_setting('app.current_principal', true)::uuid
                        OR current_setting('app.current_principal_kind', true) IN ('service', 'system')
                    );
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE wishlist_alert_dedup ENABLE ROW LEVEL SECURITY;
                CREATE POLICY wishlist_alert_dedup_owner_or_system ON wishlist_alert_dedup
                    USING (
                        user_id = current_setting('app.current_principal', true)::uuid
                        OR current_setting('app.current_principal_kind', true) IN ('service', 'system')
                    );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP POLICY IF EXISTS wishlist_alert_dedup_owner_or_system ON wishlist_alert_dedup;");
            migrationBuilder.Sql("DROP POLICY IF EXISTS wishlist_entries_owner_or_system ON wishlist_entries;");

            migrationBuilder.DropTable(
                name: "wishlist_alert_dedup");

            migrationBuilder.DropTable(
                name: "wishlist_audit_log");

            migrationBuilder.DropTable(
                name: "wishlist_entries");

            migrationBuilder.DropTable(
                name: "wishlist_outbox_events");
        }
    }
}
