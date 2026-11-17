using System;
using System.Collections.Generic;
using System.Net;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace InviteCraft.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:citext", ",,")
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,");

            migrationBuilder.CreateSequence(
                name: "order_number_seq");

            migrationBuilder.CreateSequence(
                name: "template_code_seq",
                startValue: 10001L);

            migrationBuilder.CreateTable(
                name: "banners",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    subtitle = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    image_url = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    mobile_image_url = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    cta_text = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    cta_url = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    placement = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_banners", x => x.id);
                    table.CheckConstraint("ck_banners_dates", "ends_at IS NULL OR starts_at IS NULL OR ends_at > starts_at");
                    table.CheckConstraint("ck_banners_placement", "placement IN ('HomeHero', 'HomeStrip', 'CategoryTop')");
                });

            migrationBuilder.CreateTable(
                name: "categories",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    slug = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    icon = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    image_url = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    show_on_home = table.Column<bool>(type: "boolean", nullable: false),
                    seo_title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    seo_description = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_categories", x => x.id);
                    table.CheckConstraint("ck_categories_kind", "kind IN ('Religion', 'Occasion', 'Festival', 'Other')");
                });

            migrationBuilder.CreateTable(
                name: "coupons",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "citext", nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    discount_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    discount_value = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    max_discount_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    min_order_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    applicable_package_ids = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    applicable_category_ids = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    usage_limit = table.Column<int>(type: "integer", nullable: true),
                    per_user_limit = table.Column<int>(type: "integer", nullable: false),
                    used_count = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_coupons", x => x.id);
                    table.CheckConstraint("ck_coupons_dates", "ends_at IS NULL OR starts_at IS NULL OR ends_at > starts_at");
                    table.CheckConstraint("ck_coupons_discount_type", "discount_type IN ('Percentage', 'FixedAmount')");
                    table.CheckConstraint("ck_coupons_limits", "per_user_limit >= 1 AND used_count >= 0 AND (usage_limit IS NULL OR usage_limit >= 1)");
                    table.CheckConstraint("ck_coupons_percentage", "discount_type <> 'Percentage' OR discount_value <= 100");
                    table.CheckConstraint("ck_coupons_value", "discount_value > 0");
                });

            migrationBuilder.CreateTable(
                name: "festival_collections",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    slug = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    banner_image_url = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    theme_color = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    show_on_home = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_festival_collections", x => x.id);
                    table.CheckConstraint("ck_festival_collections_dates", "ends_at IS NULL OR starts_at IS NULL OR ends_at > starts_at");
                });

            migrationBuilder.CreateTable(
                name: "fonts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    family_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    display_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    scripts = table.Column<List<string>>(type: "text[]", nullable: false),
                    weight = table.Column<int>(type: "integer", nullable: false),
                    style = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    woff2key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ttf_key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    license = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fonts", x => x.id);
                    table.CheckConstraint("ck_fonts_weight", "weight BETWEEN 100 AND 900");
                });

            migrationBuilder.CreateTable(
                name: "packages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    features = table.Column<string>(type: "jsonb", nullable: false),
                    base_price = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    allowed_formats = table.Column<string[]>(type: "text[]", nullable: false),
                    max_dpi = table.Column<int>(type: "integer", nullable: false),
                    max_downloads = table.Column<int>(type: "integer", nullable: true),
                    removes_watermark = table.Column<bool>(type: "boolean", nullable: false),
                    edit_window_days = table.Column<int>(type: "integer", nullable: true),
                    includes_custom_design = table.Column<bool>(type: "boolean", nullable: false),
                    is_recommended = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_packages", x => x.id);
                    table.CheckConstraint("ck_packages_base_price", "base_price >= 0");
                    table.CheckConstraint("ck_packages_max_downloads", "max_downloads IS NULL OR max_downloads > 0");
                    table.CheckConstraint("ck_packages_max_dpi", "max_dpi BETWEEN 72 AND 1200");
                });

            migrationBuilder.CreateTable(
                name: "payment_webhook_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    event_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    event_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    signature_valid = table.Column<bool>(type: "boolean", nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_webhook_events", x => x.id);
                    table.CheckConstraint("ck_payment_webhook_events_status", "status IN ('Received', 'Processed', 'Ignored', 'Failed')");
                });

            migrationBuilder.CreateTable(
                name: "roles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_roles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "site_settings",
                columns: table => new
                {
                    key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    value = table.Column<string>(type: "jsonb", nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_site_settings", x => x.key);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "citext", nullable: false),
                    password_hash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    full_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    avatar_url = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    email_verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    failed_login_count = table.Column<int>(type: "integer", nullable: false),
                    lockout_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_login_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                    table.CheckConstraint("ck_users_failed_login_count", "failed_login_count >= 0");
                });

            migrationBuilder.CreateTable(
                name: "sub_categories",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    slug = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    image_url = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sub_categories", x => x.id);
                    table.ForeignKey(
                        name: "fk_sub_categories_categories_category_id",
                        column: x => x.category_id,
                        principalTable: "categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "audit_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    entity_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    entity_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    old_values = table.Column<string>(type: "jsonb", nullable: true),
                    new_values = table.Column<string>(type: "jsonb", nullable: true),
                    ip_address = table.Column<IPAddress>(type: "inet", nullable: true),
                    user_agent = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    correlation_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_logs", x => x.id);
                    table.ForeignKey(
                        name: "fk_audit_logs_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    type = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    body = table.Column<string>(type: "text", nullable: false),
                    data = table.Column<string>(type: "jsonb", nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notifications", x => x.id);
                    table.CheckConstraint("ck_notifications_channel", "channel IN ('InApp', 'Email', 'WhatsApp')");
                    table.CheckConstraint("ck_notifications_status", "status IN ('Pending', 'Sent', 'Failed')");
                    table.ForeignKey(
                        name: "fk_notifications_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    subtotal = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    discount_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    tax_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    total_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    coupon_id = table.Column<Guid>(type: "uuid", nullable: true),
                    gateway_order_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    idempotency_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    notes = table.Column<string>(type: "jsonb", nullable: true),
                    paid_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_orders", x => x.id);
                    table.CheckConstraint("ck_orders_amounts", "subtotal >= 0 AND discount_amount >= 0 AND tax_amount >= 0 AND total_amount >= 0");
                    table.CheckConstraint("ck_orders_status", "status IN ('Pending', 'AwaitingPayment', 'Paid', 'Failed', 'Cancelled', 'Expired', 'Refunded')");
                    table.CheckConstraint("ck_orders_total", "total_amount = subtotal - discount_amount");
                    table.ForeignKey(
                        name: "fk_orders_coupons_coupon_id",
                        column: x => x.coupon_id,
                        principalTable: "coupons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_orders_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "refresh_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    family_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_ip = table.Column<IPAddress>(type: "inet", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    replaced_by_token_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_refresh_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_refresh_tokens_refresh_tokens_replaced_by_token_id",
                        column: x => x.replaced_by_token_id,
                        principalTable: "refresh_tokens",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_refresh_tokens_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_roles",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assigned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_roles", x => new { x.user_id, x.role_id });
                    table.ForeignKey(
                        name: "fk_user_roles_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_user_roles_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    purpose = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    token_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    consumed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_tokens", x => x.id);
                    table.CheckConstraint("ck_user_tokens_purpose", "purpose IN ('EmailVerification', 'PasswordReset')");
                    table.ForeignKey(
                        name: "fk_user_tokens_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "templates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    slug = table.Column<string>(type: "character varying(220)", maxLength: 220, nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sub_category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    tier = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    orientation = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    width_mm = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: false),
                    height_mm = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: false),
                    dpi = table.Column<int>(type: "integer", nullable: false),
                    bleed_mm = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    supported_languages = table.Column<List<string>>(type: "text[]", nullable: false),
                    tags = table.Column<List<string>>(type: "text[]", nullable: false),
                    design = table.Column<string>(type: "jsonb", nullable: false),
                    layout_version = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    is_featured = table.Column<bool>(type: "boolean", nullable: false),
                    starting_price = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    view_count = table.Column<long>(type: "bigint", nullable: false),
                    sales_count = table.Column<int>(type: "integer", nullable: false),
                    avg_rating = table.Column<decimal>(type: "numeric(3,2)", precision: 3, scale: 2, nullable: false),
                    review_count = table.Column<int>(type: "integer", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_templates", x => x.id);
                    table.CheckConstraint("ck_templates_avg_rating", "avg_rating BETWEEN 0 AND 5");
                    table.CheckConstraint("ck_templates_dimensions", "width_mm > 0 AND height_mm > 0 AND bleed_mm >= 0");
                    table.CheckConstraint("ck_templates_dpi", "dpi BETWEEN 72 AND 1200");
                    table.CheckConstraint("ck_templates_orientation", "orientation IN ('Portrait', 'Landscape', 'Square')");
                    table.CheckConstraint("ck_templates_starting_price", "starting_price IS NULL OR starting_price >= 0");
                    table.CheckConstraint("ck_templates_status", "status IN ('Draft', 'Published', 'Archived')");
                    table.CheckConstraint("ck_templates_tier", "tier IN ('Free', 'Premium')");
                    table.ForeignKey(
                        name: "fk_templates_categories_category_id",
                        column: x => x.category_id,
                        principalTable: "categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_templates_sub_categories_sub_category_id",
                        column: x => x.sub_category_id,
                        principalTable: "sub_categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "coupon_redemptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    coupon_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    discount_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    redeemed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_coupon_redemptions", x => x.id);
                    table.CheckConstraint("ck_coupon_redemptions_amount", "discount_amount >= 0");
                    table.CheckConstraint("ck_coupon_redemptions_status", "status IN ('Reserved', 'Redeemed', 'Released')");
                    table.ForeignKey(
                        name: "fk_coupon_redemptions_coupons_coupon_id",
                        column: x => x.coupon_id,
                        principalTable: "coupons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_coupon_redemptions_orders_order_id",
                        column: x => x.order_id,
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_coupon_redemptions_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    gateway_order_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    gateway_payment_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    gateway_signature = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    method = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    verified_via = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    error_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    error_description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    raw_response = table.Column<string>(type: "jsonb", nullable: true),
                    captured_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payments", x => x.id);
                    table.CheckConstraint("ck_payments_amount", "amount >= 0");
                    table.CheckConstraint("ck_payments_status", "status IN ('Created', 'Authorized', 'Captured', 'Failed', 'Refunded')");
                    table.CheckConstraint("ck_payments_verified_via", "verified_via IN ('Checkout', 'Webhook', 'Reconciliation')");
                    table.ForeignKey(
                        name: "fk_payments_orders_order_id",
                        column: x => x.order_id,
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "festival_collection_templates",
                columns: table => new
                {
                    collection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_festival_collection_templates", x => new { x.collection_id, x.template_id });
                    table.ForeignKey(
                        name: "fk_festival_collection_templates_festival_collections_collecti",
                        column: x => x.collection_id,
                        principalTable: "festival_collections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_festival_collection_templates_templates_template_id",
                        column: x => x.template_id,
                        principalTable: "templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "invitations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    language = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    layout_version = table.Column<int>(type: "integer", nullable: false),
                    style_overrides = table.Column<string>(type: "jsonb", nullable: true),
                    visibility = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    share_slug = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    share_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    unlocked_package_id = table.Column<Guid>(type: "uuid", nullable: true),
                    unlocked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    editable_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    preview_image_key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    last_rendered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    view_count = table.Column<long>(type: "bigint", nullable: false),
                    duplicated_from_id = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invitations", x => x.id);
                    table.CheckConstraint("ck_invitations_status", "status IN ('Draft', 'Ready', 'Archived')");
                    table.CheckConstraint("ck_invitations_unlock", "(unlocked_package_id IS NULL) = (unlocked_at IS NULL)");
                    table.CheckConstraint("ck_invitations_visibility", "visibility IN ('Private', 'LinkOnly', 'Public')");
                    table.ForeignKey(
                        name: "fk_invitations_invitations_duplicated_from_id",
                        column: x => x.duplicated_from_id,
                        principalTable: "invitations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_invitations_packages_unlocked_package_id",
                        column: x => x.unlocked_package_id,
                        principalTable: "packages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_invitations_templates_template_id",
                        column: x => x.template_id,
                        principalTable: "templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_invitations_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "reviews",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_id = table.Column<Guid>(type: "uuid", nullable: true),
                    order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    rating = table.Column<short>(type: "smallint", nullable: false),
                    title = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    comment = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    is_featured = table.Column<bool>(type: "boolean", nullable: false),
                    admin_reply = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reviews", x => x.id);
                    table.CheckConstraint("ck_reviews_rating", "rating BETWEEN 1 AND 5");
                    table.CheckConstraint("ck_reviews_status", "status IN ('Pending', 'Approved', 'Rejected')");
                    table.ForeignKey(
                        name: "fk_reviews_orders_order_id",
                        column: x => x.order_id,
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_reviews_templates_template_id",
                        column: x => x.template_id,
                        principalTable: "templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_reviews_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "saved_templates",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_saved_templates", x => new { x.user_id, x.template_id });
                    table.ForeignKey(
                        name: "fk_saved_templates_templates_template_id",
                        column: x => x.template_id,
                        principalTable: "templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_saved_templates_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "template_assets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_id = table.Column<Guid>(type: "uuid", nullable: false),
                    asset_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    variant = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    storage_key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    public_url = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    mime_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    width_px = table.Column<int>(type: "integer", nullable: true),
                    height_px = table.Column<int>(type: "integer", nullable: true),
                    page_index = table.Column<int>(type: "integer", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_template_assets", x => x.id);
                    table.CheckConstraint("ck_template_assets_asset_type", "asset_type IN ('Thumbnail', 'Preview', 'Background', 'Decoration', 'Sample')");
                    table.CheckConstraint("ck_template_assets_size", "size_bytes >= 0");
                    table.CheckConstraint("ck_template_assets_variant", "variant IN ('Original', 'Web', 'Thumb')");
                    table.ForeignKey(
                        name: "fk_template_assets_templates_template_id",
                        column: x => x.template_id,
                        principalTable: "templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "template_editable_fields",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_id = table.Column<Guid>(type: "uuid", nullable: false),
                    field_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    label_translations = table.Column<string>(type: "jsonb", nullable: true),
                    field_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    group_name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    is_required = table.Column<bool>(type: "boolean", nullable: false),
                    max_length = table.Column<int>(type: "integer", nullable: true),
                    placeholder = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    default_value = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    options = table.Column<string>(type: "jsonb", nullable: true),
                    display_format = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    page_index = table.Column<int>(type: "integer", nullable: false),
                    x = table.Column<decimal>(type: "numeric(7,6)", precision: 7, scale: 6, nullable: false),
                    y = table.Column<decimal>(type: "numeric(7,6)", precision: 7, scale: 6, nullable: false),
                    width = table.Column<decimal>(type: "numeric(7,6)", precision: 7, scale: 6, nullable: false),
                    height = table.Column<decimal>(type: "numeric(7,6)", precision: 7, scale: 6, nullable: false),
                    rotation = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false),
                    z_index = table.Column<int>(type: "integer", nullable: false),
                    style = table.Column<string>(type: "jsonb", nullable: false),
                    permissions = table.Column<string>(type: "jsonb", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_template_editable_fields", x => x.id);
                    table.CheckConstraint("ck_template_editable_fields_field_type", "field_type IN ('Text', 'MultilineText', 'Date', 'Time', 'Phone', 'Email', 'Number', 'Select', 'Image')");
                    table.CheckConstraint("ck_template_editable_fields_frame", "x BETWEEN 0 AND 1 AND y BETWEEN 0 AND 1 AND width > 0 AND width <= 1 AND height > 0 AND height <= 1");
                    table.CheckConstraint("ck_template_editable_fields_max_length", "max_length IS NULL OR max_length > 0");
                    table.ForeignKey(
                        name: "fk_template_editable_fields_templates_template_id",
                        column: x => x.template_id,
                        principalTable: "templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "template_font_configurations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_id = table.Column<Guid>(type: "uuid", nullable: false),
                    font_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_template_font_configurations", x => x.id);
                    table.CheckConstraint("ck_template_font_configurations_role", "role IN ('Heading', 'Body', 'Accent', 'Script')");
                    table.ForeignKey(
                        name: "fk_template_font_configurations_fonts_font_id",
                        column: x => x.font_id,
                        principalTable: "fonts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_template_font_configurations_templates_template_id",
                        column: x => x.template_id,
                        principalTable: "templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "template_pricing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_id = table.Column<Guid>(type: "uuid", nullable: false),
                    package_id = table.Column<Guid>(type: "uuid", nullable: false),
                    price = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    compare_at_price = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    valid_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    valid_to = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_template_pricing", x => x.id);
                    table.CheckConstraint("ck_template_pricing_compare_at", "compare_at_price IS NULL OR compare_at_price >= price");
                    table.CheckConstraint("ck_template_pricing_price", "price >= 0");
                    table.CheckConstraint("ck_template_pricing_validity", "valid_to IS NULL OR valid_from IS NULL OR valid_to > valid_from");
                    table.ForeignKey(
                        name: "fk_template_pricing_packages_package_id",
                        column: x => x.package_id,
                        principalTable: "packages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_template_pricing_templates_template_id",
                        column: x => x.template_id,
                        principalTable: "templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "order_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    invitation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_id = table.Column<Guid>(type: "uuid", nullable: false),
                    package_id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    package_name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    discount_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    line_total = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_items", x => x.id);
                    table.CheckConstraint("ck_order_items_amounts", "unit_price >= 0 AND discount_amount >= 0 AND line_total >= 0");
                    table.CheckConstraint("ck_order_items_quantity", "quantity > 0");
                    table.ForeignKey(
                        name: "fk_order_items_invitations_invitation_id",
                        column: x => x.invitation_id,
                        principalTable: "invitations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_order_items_orders_order_id",
                        column: x => x.order_id,
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_order_items_packages_package_id",
                        column: x => x.package_id,
                        principalTable: "packages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_order_items_templates_template_id",
                        column: x => x.template_id,
                        principalTable: "templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "invitation_field_values",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    invitation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    field_id = table.Column<Guid>(type: "uuid", nullable: false),
                    value = table.Column<string>(type: "text", nullable: true),
                    style_override = table.Column<string>(type: "jsonb", nullable: true),
                    frame_override = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invitation_field_values", x => x.id);
                    table.ForeignKey(
                        name: "fk_invitation_field_values_invitations_invitation_id",
                        column: x => x.invitation_id,
                        principalTable: "invitations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_invitation_field_values_template_editable_fields_field_id",
                        column: x => x.field_id,
                        principalTable: "template_editable_fields",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "downloads",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    invitation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    format = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    dpi = table.Column<int>(type: "integer", nullable: false),
                    has_watermark = table.Column<bool>(type: "boolean", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    storage_key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    file_size_bytes = table.Column<long>(type: "bigint", nullable: true),
                    error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ip_address = table.Column<IPAddress>(type: "inet", nullable: true),
                    user_agent = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_downloads", x => x.id);
                    table.CheckConstraint("ck_downloads_dpi", "dpi BETWEEN 72 AND 1200");
                    table.CheckConstraint("ck_downloads_format", "format IN ('Jpg', 'Png', 'Pdf', 'PrintPdf')");
                    table.CheckConstraint("ck_downloads_status", "status IN ('Queued', 'Rendering', 'Ready', 'Failed', 'Expired')");
                    table.ForeignKey(
                        name: "fk_downloads_invitations_invitation_id",
                        column: x => x.invitation_id,
                        principalTable: "invitations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_downloads_order_items_order_item_id",
                        column: x => x.order_item_id,
                        principalTable: "order_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_downloads_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "categories",
                columns: new[] { "id", "created_at", "created_by", "deleted_at", "description", "icon", "image_url", "is_active", "is_deleted", "kind", "name", "seo_description", "seo_title", "show_on_home", "slug", "sort_order", "updated_at", "updated_by" },
                values: new object[,]
                {
                    { new Guid("49bb845c-71a0-ad33-a05e-11c4ff893640"), new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "Elegant invitations for Nikah, Walima and other Islamic occasions.", "bi-moon-stars", null, true, false, "Religion", "Muslim Ceremonies", null, "Muslim Ceremonies Invitation Cards | InviteCraft", true, "muslim-ceremonies", 20, new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { new Guid("4bd802e1-7096-8037-9f54-a1faa3d1b522"), new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "Fun and stylish birthday invitations for every age.", "bi-cake2", null, true, false, "Occasion", "Birthday", null, "Birthday Invitation Cards | InviteCraft", true, "birthday", 50, new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { new Guid("aa5fcef9-2add-0f36-8ffb-a1b2802d0ea8"), new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "Everything for the big day — from save the date to anniversary.", "bi-heart", null, true, false, "Occasion", "Wedding", null, "Wedding Invitation Cards | InviteCraft", true, "wedding", 40, new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { new Guid("b494b5c8-1d19-8f34-a187-72588b5a943d"), new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "Festive greetings and celebration invites.", "bi-stars", null, true, false, "Festival", "Festivals", null, "Festivals Invitation Cards | InviteCraft", true, "festivals", 60, new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { new Guid("b54bbf92-55c6-373e-9900-da8d75a98e34"), new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "Baby showers, graduations, housewarmings and corporate events.", "bi-calendar-event", null, true, false, "Other", "Other Events", null, "Other Events Invitation Cards | InviteCraft", false, "other", 70, new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { new Guid("c574b7a3-09c1-7434-9ab9-f84f253a8c36"), new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "Traditional invitations for Hindu rituals and samskaras.", "bi-flower1", null, true, false, "Religion", "Hindu Ceremonies", null, "Hindu Ceremonies Invitation Cards | InviteCraft", true, "hindu-ceremonies", 10, new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { new Guid("cab987ed-b84b-dc30-8c96-0861e50bc5e5"), new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "Graceful invitations for church weddings, sacraments and Christmas.", "bi-brightness-high", null, true, false, "Religion", "Christian Ceremonies", null, "Christian Ceremonies Invitation Cards | InviteCraft", true, "christian-ceremonies", 30, new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null }
                });

            migrationBuilder.InsertData(
                table: "packages",
                columns: new[] { "id", "allowed_formats", "base_price", "code", "created_at", "created_by", "description", "edit_window_days", "features", "includes_custom_design", "is_active", "is_recommended", "max_downloads", "max_dpi", "name", "removes_watermark", "sort_order", "updated_at", "updated_by" },
                values: new object[,]
                {
                    { new Guid("0803c103-2811-833f-a2d6-6c2316b58d35"), new[] { "Jpg", "Png", "Pdf", "PrintPdf" }, 1999m, "CUSTOM", new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "A designer crafts a bespoke invitation for you.", 180, "[\"Personal designer\",\"Bespoke layout & artwork\",\"Two revision rounds\",\"All formats incl. print-ready PDF\"]", true, true, false, null, 300, "Custom Design", true, 50, new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { new Guid("30fb72c0-d2f2-c13e-a376-8c45cdd59edd"), new[] { "Jpg", "Png" }, 99m, "BASIC", new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "Clean, watermark-free digital invitation.", 7, "[\"No watermark\",\"HD JPG & PNG\",\"5 downloads\",\"Edit for 7 days\"]", false, true, false, 5, 150, "Basic", true, 20, new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { new Guid("5c4e42e5-9a51-843d-9a8b-b38a9c8732d8"), new[] { "Jpg", "Png", "Pdf", "PrintPdf" }, 499m, "LUXURY", new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "Print-shop ready with bleed and crop marks.", 90, "[\"Everything in Premium\",\"Print-ready PDF with bleed & crop marks\",\"Edit for 90 days\",\"Priority support\"]", false, true, false, null, 300, "Luxury", true, 40, new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { new Guid("7a62d57e-f5eb-df32-85e3-ec83b4bd1ca7"), new[] { "Jpg", "Png", "Pdf" }, 249m, "PREMIUM", new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "High-resolution files for sharing and home printing.", 30, "[\"300 DPI high resolution\",\"JPG, PNG & PDF\",\"Unlimited downloads\",\"Edit for 30 days\",\"Shareable invitation page\"]", false, true, true, null, 300, "Premium", true, 30, new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { new Guid("81c2f3f7-ed9d-313c-a2a5-d866c69bb71c"), new[] { "Jpg", "Png" }, 0m, "FREE", new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "For free templates — share digitally at no cost.", null, "[\"Free templates only\",\"JPG & PNG downloads\",\"Share via WhatsApp & link\"]", false, true, false, null, 150, "Free", true, 10, new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null }
                });

            migrationBuilder.InsertData(
                table: "roles",
                columns: new[] { "id", "description", "name" },
                values: new object[,]
                {
                    { new Guid("4215f668-7c7a-0f32-8c4c-46a4e15f9aaf"), "Full access to the admin panel.", "Admin" },
                    { new Guid("4586c2f9-0b7e-bc3e-b599-6eb958255979"), "Creates, buys and shares invitations.", "Customer" }
                });

            migrationBuilder.InsertData(
                table: "site_settings",
                columns: new[] { "key", "description", "updated_at", "updated_by", "value" },
                values: new object[,]
                {
                    { "contact", "Public contact details shown in the footer and Contact page.", new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "{\"email\":\"support@invitecraft.example\",\"phone\":null,\"whatsapp\":null}" },
                    { "downloads", "Lifetime of signed download links.", new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "{\"signedUrlMinutes\":15}" },
                    { "orders", "Unpaid orders expire after this many minutes.", new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "{\"pendingExpiryMinutes\":30}" },
                    { "tax.gst", "GST applied to orders. Prices shown to customers include tax.", new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "{\"rate\":18,\"pricesIncludeTax\":true}" },
                    { "watermark", "Watermark drawn on locked premium previews.", new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "{\"text\":\"InviteCraft · Preview\",\"opacity\":0.18}" }
                });

            migrationBuilder.InsertData(
                table: "sub_categories",
                columns: new[] { "id", "category_id", "created_at", "created_by", "deleted_at", "description", "image_url", "is_active", "is_deleted", "name", "slug", "sort_order", "updated_at", "updated_by" },
                values: new object[,]
                {
                    { new Guid("06762c51-12f8-dd3f-81c0-464aeb4f0778"), new Guid("cab987ed-b84b-dc30-8c96-0861e50bc5e5"), new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, null, null, true, false, "First Communion", "first-communion", 30, new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { new Guid("07f85e90-c3d0-8e3c-a9b4-a1aa0a5881bb"), new Guid("49bb845c-71a0-ad33-a05e-11c4ff893640"), new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, null, null, true, false, "Nikah", "nikah", 10, new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { new Guid("0bd95792-8c4f-2e34-bf07-06a5b10d9504"), new Guid("b54bbf92-55c6-373e-9900-da8d75a98e34"), new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, null, null, true, false, "Retirement", "retirement", 30, new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { new Guid("0e87c0f5-a755-fe34-91ed-864ec3edafc6"), new Guid("49bb845c-71a0-ad33-a05e-11c4ff893640"), new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, null, null, true, false, "Aqeeqah", "aqeeqah", 30, new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { new Guid("0efd47c5-8507-6f39-af0d-1bc0a79e5aa5"), new Guid("b54bbf92-55c6-373e-9900-da8d75a98e34"), new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, null, null, true, false, "Corporate Events", "corporate-events", 40, new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { new Guid("27876d76-651d-1d34-b7c5-47f614d1ef2e"), new Guid("4bd802e1-7096-8037-9f54-a1faa3d1b522"), new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, null, null, true, false, "Milestone Birthday", "milestone-birthday", 30, new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { new Guid("2a0e80b6-6de8-c23b-89fb-1b3830485bab"), new Guid("4bd802e1-7096-8037-9f54-a1faa3d1b522"), new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, null, null, true, false, "Kids Birthday", "kids-birthday", 10, new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { new Guid("33160178-ac44-d638-acd7-657a7a56a194"), new Guid("b494b5c8-1d19-8f34-a187-72588b5a943d"), new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, null, null, true, false, "Diwali", "diwali", 10, new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { new Guid("426cd9bd-76f3-a53c-9b91-c12b25c7a680"), new Guid("cab987ed-b84b-dc30-8c96-0861e50bc5e5"), new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, null, null, true, false, "Baptism", "baptism", 20, new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { new Guid("497f4827-f748-c13c-b9e1-9e3dbed24847"), new Guid("b54bbf92-55c6-373e-9900-da8d75a98e34"), new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, null, null, true, false, "Graduation", "graduation", 20, new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { new Guid("4b79d911-9157-ae36-aa7c-8b2cc1db840e"), new Guid("c574b7a3-09c1-7434-9ab9-f84f253a8c36"), new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, null, null, true, false, "Wedding", "wedding", 10, new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { new Guid("54689c7d-0294-8c33-ad3e-106123b22b61"), new Guid("b54bbf92-55c6-373e-9900-da8d75a98e34"), new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, null, null, true, false, "Housewarming", "housewarming", 50, new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { new Guid("5b274d51-08e4-e232-a09b-e8a2b24bc639"), new Guid("49bb845c-71a0-ad33-a05e-11c4ff893640"), new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, null, null, true, false, "Eid", "eid", 40, new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { new Guid("5b3862af-b1c7-b236-98bd-59a239265cbe"), new Guid("c574b7a3-09c1-7434-9ab9-f84f253a8c36"), new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, null, null, true, false, "Annaprashan", "annaprashan", 70, new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { new Guid("6977c232-5e18-c132-989d-eb5ba75ef159"), new Guid("c574b7a3-09c1-7434-9ab9-f84f253a8c36"), new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, null, null, true, false, "Puja", "puja", 60, new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { new Guid("6db0572a-4f05-6b32-9a7e-160df9014875"), new Guid("c574b7a3-09c1-7434-9ab9-f84f253a8c36"), new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, null, null, true, false, "Mehndi", "mehndi", 40, new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { new Guid("7de68409-4e5c-783e-9375-9f5346a22936"), new Guid("b494b5c8-1d19-8f34-a187-72588b5a943d"), new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, null, null, true, false, "Eid", "eid", 30, new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { new Guid("7ee7afb3-a901-7736-97d3-ea90a5b1d3eb"), new Guid("cab987ed-b84b-dc30-8c96-0861e50bc5e5"), new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, null, null, true, false, "Christmas", "christmas", 40, new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { new Guid("88fbe71e-036d-b33d-a3da-ad7890688210"), new Guid("aa5fcef9-2add-0f36-8ffb-a1b2802d0ea8"), new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, null, null, true, false, "Reception", "reception", 30, new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { new Guid("89136775-d0fa-1535-b240-baec12b068c4"), new Guid("cab987ed-b84b-dc30-8c96-0861e50bc5e5"), new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, null, null, true, false, "Wedding", "wedding", 10, new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { new Guid("a3b197c1-ce39-4f3c-8f87-e9239c9cad2d"), new Guid("aa5fcef9-2add-0f36-8ffb-a1b2802d0ea8"), new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, null, null, true, false, "Save the Date", "save-the-date", 10, new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { new Guid("a7a51dac-383f-0831-b9ee-0c41ff8517c0"), new Guid("49bb845c-71a0-ad33-a05e-11c4ff893640"), new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, null, null, true, false, "Walima", "walima", 20, new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { new Guid("acece593-8ac2-853f-bb58-59cebf9d847c"), new Guid("c574b7a3-09c1-7434-9ab9-f84f253a8c36"), new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, null, null, true, false, "Haldi", "haldi", 30, new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { new Guid("b2aafd3b-8192-9932-a96f-6e62b38b3bf4"), new Guid("c574b7a3-09c1-7434-9ab9-f84f253a8c36"), new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, null, null, true, false, "Naming Ceremony", "naming-ceremony", 80, new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { new Guid("b3d5129f-a109-5131-9658-b93609f098d5"), new Guid("b54bbf92-55c6-373e-9900-da8d75a98e34"), new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, null, null, true, false, "Baby Shower", "baby-shower", 10, new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { new Guid("b5b72143-be6a-db33-86ac-b47952299f87"), new Guid("4bd802e1-7096-8037-9f54-a1faa3d1b522"), new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, null, null, true, false, "Adult Birthday", "adult-birthday", 20, new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { new Guid("cdf7c28a-211e-e836-84d5-76babab86f26"), new Guid("c574b7a3-09c1-7434-9ab9-f84f253a8c36"), new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, null, null, true, false, "Engagement", "engagement", 20, new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { new Guid("d6104f5e-59d1-2733-8959-556ac4e94958"), new Guid("aa5fcef9-2add-0f36-8ffb-a1b2802d0ea8"), new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, null, null, true, false, "Wedding Invitation", "wedding-invitation", 20, new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { new Guid("d8dc5b13-9dad-5930-a700-fe00dd2b04b6"), new Guid("b494b5c8-1d19-8f34-a187-72588b5a943d"), new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, null, null, true, false, "Holi", "holi", 20, new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { new Guid("f1e62231-95e5-4e3f-b78d-1fc77cc42141"), new Guid("b494b5c8-1d19-8f34-a187-72588b5a943d"), new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, null, null, true, false, "Christmas", "christmas", 40, new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { new Guid("f588c33a-6e89-5b32-bdaf-820cc2591322"), new Guid("b494b5c8-1d19-8f34-a187-72588b5a943d"), new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, null, null, true, false, "New Year", "new-year", 60, new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { new Guid("f868f9ef-27d4-a93e-b2e6-061dc29558b7"), new Guid("b494b5c8-1d19-8f34-a187-72588b5a943d"), new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, null, null, true, false, "Raksha Bandhan", "raksha-bandhan", 50, new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { new Guid("f907b1a2-5098-a037-bdb7-eec01985389b"), new Guid("aa5fcef9-2add-0f36-8ffb-a1b2802d0ea8"), new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, null, null, true, false, "Anniversary", "anniversary", 40, new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { new Guid("f9699f35-166c-d630-b1a9-338e6a7303cf"), new Guid("c574b7a3-09c1-7434-9ab9-f84f253a8c36"), new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, null, null, true, false, "Griha Pravesh", "griha-pravesh", 50, new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null }
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_created_at_brin",
                table: "audit_logs",
                column: "created_at")
                .Annotation("Npgsql:IndexMethod", "brin");

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_entity_type_entity_id",
                table: "audit_logs",
                columns: new[] { "entity_type", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_user_id_created_at",
                table: "audit_logs",
                columns: new[] { "user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_banners_placement_is_active_sort_order",
                table: "banners",
                columns: new[] { "placement", "is_active", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_categories_is_active_sort_order",
                table: "categories",
                columns: new[] { "is_active", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_categories_slug",
                table: "categories",
                column: "slug",
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_coupon_redemptions_coupon_id_user_id",
                table: "coupon_redemptions",
                columns: new[] { "coupon_id", "user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_coupon_redemptions_order_id",
                table: "coupon_redemptions",
                column: "order_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_coupon_redemptions_user_id",
                table: "coupon_redemptions",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_coupons_code",
                table: "coupons",
                column: "code",
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_downloads_invitation_id",
                table: "downloads",
                column: "invitation_id");

            migrationBuilder.CreateIndex(
                name: "ix_downloads_order_item_id",
                table: "downloads",
                column: "order_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_downloads_status",
                table: "downloads",
                column: "status",
                filter: "status IN ('Queued', 'Rendering')");

            migrationBuilder.CreateIndex(
                name: "ix_downloads_user_id_created_at",
                table: "downloads",
                columns: new[] { "user_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_festival_collection_templates_template_id",
                table: "festival_collection_templates",
                column: "template_id");

            migrationBuilder.CreateIndex(
                name: "ix_festival_collections_is_active_sort_order",
                table: "festival_collections",
                columns: new[] { "is_active", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_festival_collections_slug",
                table: "festival_collections",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_fonts_family_name_weight_style",
                table: "fonts",
                columns: new[] { "family_name", "weight", "style" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_fonts_scripts",
                table: "fonts",
                column: "scripts")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "ix_invitation_field_values_field_id",
                table: "invitation_field_values",
                column: "field_id");

            migrationBuilder.CreateIndex(
                name: "ix_invitation_field_values_invitation_id_field_id",
                table: "invitation_field_values",
                columns: new[] { "invitation_id", "field_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_invitations_duplicated_from_id",
                table: "invitations",
                column: "duplicated_from_id");

            migrationBuilder.CreateIndex(
                name: "ix_invitations_share_slug",
                table: "invitations",
                column: "share_slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_invitations_template_id",
                table: "invitations",
                column: "template_id");

            migrationBuilder.CreateIndex(
                name: "ix_invitations_unlocked_package_id",
                table: "invitations",
                column: "unlocked_package_id");

            migrationBuilder.CreateIndex(
                name: "ix_invitations_user_id_updated_at",
                table: "invitations",
                columns: new[] { "user_id", "updated_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_notifications_status",
                table: "notifications",
                column: "status",
                filter: "status = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_user_id_read_at",
                table: "notifications",
                columns: new[] { "user_id", "read_at" });

            migrationBuilder.CreateIndex(
                name: "ix_order_items_invitation_id",
                table: "order_items",
                column: "invitation_id");

            migrationBuilder.CreateIndex(
                name: "ix_order_items_order_id",
                table: "order_items",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_order_items_package_id",
                table: "order_items",
                column: "package_id");

            migrationBuilder.CreateIndex(
                name: "ix_order_items_template_id",
                table: "order_items",
                column: "template_id");

            migrationBuilder.CreateIndex(
                name: "ix_orders_coupon_id",
                table: "orders",
                column: "coupon_id");

            migrationBuilder.CreateIndex(
                name: "ix_orders_gateway_order_id",
                table: "orders",
                column: "gateway_order_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_orders_order_number",
                table: "orders",
                column: "order_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_orders_status_created_at",
                table: "orders",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_orders_user_id_created_at",
                table: "orders",
                columns: new[] { "user_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_orders_user_id_idempotency_key",
                table: "orders",
                columns: new[] { "user_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_packages_code",
                table: "packages",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payment_webhook_events_provider_event_id",
                table: "payment_webhook_events",
                columns: new[] { "provider", "event_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payment_webhook_events_status",
                table: "payment_webhook_events",
                column: "status",
                filter: "status IN ('Received', 'Failed')");

            migrationBuilder.CreateIndex(
                name: "ix_payments_gateway_order_id",
                table: "payments",
                column: "gateway_order_id");

            migrationBuilder.CreateIndex(
                name: "ix_payments_gateway_payment_id",
                table: "payments",
                column: "gateway_payment_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payments_order_id",
                table: "payments",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_family_id",
                table: "refresh_tokens",
                column: "family_id");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_replaced_by_token_id",
                table: "refresh_tokens",
                column: "replaced_by_token_id");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_token_hash",
                table: "refresh_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_user_id_expires_at",
                table: "refresh_tokens",
                columns: new[] { "user_id", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "ix_reviews_order_id",
                table: "reviews",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_reviews_template_id_status",
                table: "reviews",
                columns: new[] { "template_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_reviews_user_id_template_id",
                table: "reviews",
                columns: new[] { "user_id", "template_id" },
                unique: true,
                filter: "template_id IS NOT NULL AND is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_roles_name",
                table: "roles",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_saved_templates_template_id",
                table: "saved_templates",
                column: "template_id");

            migrationBuilder.CreateIndex(
                name: "ix_sub_categories_category_id_slug",
                table: "sub_categories",
                columns: new[] { "category_id", "slug" },
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_template_assets_template_id_asset_type",
                table: "template_assets",
                columns: new[] { "template_id", "asset_type" });

            migrationBuilder.CreateIndex(
                name: "ix_template_editable_fields_template_id_field_key",
                table: "template_editable_fields",
                columns: new[] { "template_id", "field_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_template_font_configurations_font_id",
                table: "template_font_configurations",
                column: "font_id");

            migrationBuilder.CreateIndex(
                name: "ix_template_font_configurations_template_id_font_id",
                table: "template_font_configurations",
                columns: new[] { "template_id", "font_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_template_pricing_package_id",
                table: "template_pricing",
                column: "package_id");

            migrationBuilder.CreateIndex(
                name: "ix_template_pricing_template_id_package_id",
                table: "template_pricing",
                columns: new[] { "template_id", "package_id" },
                unique: true,
                filter: "is_active = true");

            migrationBuilder.CreateIndex(
                name: "ix_templates_category_id",
                table: "templates",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_templates_code",
                table: "templates",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_templates_name_trgm",
                table: "templates",
                column: "name")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_templates_slug",
                table: "templates",
                column: "slug",
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_templates_status_category_id",
                table: "templates",
                columns: new[] { "status", "category_id" });

            migrationBuilder.CreateIndex(
                name: "ix_templates_status_published_at",
                table: "templates",
                columns: new[] { "status", "published_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_templates_status_sales_count",
                table: "templates",
                columns: new[] { "status", "sales_count" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_templates_sub_category_id",
                table: "templates",
                column: "sub_category_id");

            migrationBuilder.CreateIndex(
                name: "ix_templates_supported_languages",
                table: "templates",
                column: "supported_languages")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "ix_templates_tags",
                table: "templates",
                column: "tags")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "ix_user_roles_role_id",
                table: "user_roles",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_tokens_token_hash",
                table: "user_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_tokens_user_id_purpose",
                table: "user_tokens",
                columns: new[] { "user_id", "purpose" });

            migrationBuilder.CreateIndex(
                name: "ix_users_email",
                table: "users",
                column: "email",
                unique: true,
                filter: "is_deleted = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_logs");

            migrationBuilder.DropTable(
                name: "banners");

            migrationBuilder.DropTable(
                name: "coupon_redemptions");

            migrationBuilder.DropTable(
                name: "downloads");

            migrationBuilder.DropTable(
                name: "festival_collection_templates");

            migrationBuilder.DropTable(
                name: "invitation_field_values");

            migrationBuilder.DropTable(
                name: "notifications");

            migrationBuilder.DropTable(
                name: "payment_webhook_events");

            migrationBuilder.DropTable(
                name: "payments");

            migrationBuilder.DropTable(
                name: "refresh_tokens");

            migrationBuilder.DropTable(
                name: "reviews");

            migrationBuilder.DropTable(
                name: "saved_templates");

            migrationBuilder.DropTable(
                name: "site_settings");

            migrationBuilder.DropTable(
                name: "template_assets");

            migrationBuilder.DropTable(
                name: "template_font_configurations");

            migrationBuilder.DropTable(
                name: "template_pricing");

            migrationBuilder.DropTable(
                name: "user_roles");

            migrationBuilder.DropTable(
                name: "user_tokens");

            migrationBuilder.DropTable(
                name: "order_items");

            migrationBuilder.DropTable(
                name: "festival_collections");

            migrationBuilder.DropTable(
                name: "template_editable_fields");

            migrationBuilder.DropTable(
                name: "fonts");

            migrationBuilder.DropTable(
                name: "roles");

            migrationBuilder.DropTable(
                name: "invitations");

            migrationBuilder.DropTable(
                name: "orders");

            migrationBuilder.DropTable(
                name: "packages");

            migrationBuilder.DropTable(
                name: "templates");

            migrationBuilder.DropTable(
                name: "coupons");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "sub_categories");

            migrationBuilder.DropTable(
                name: "categories");

            migrationBuilder.DropSequence(
                name: "order_number_seq");

            migrationBuilder.DropSequence(
                name: "template_code_seq");
        }
    }
}
