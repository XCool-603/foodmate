using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodMate.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nickname = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    avatar_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    last_login_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ai_recognition_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    image_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    model_name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    raw_response = table.Column<string>(type: "text", nullable: true),
                    parsed_result = table.Column<string>(type: "text", nullable: true),
                    corrected_result = table.Column<string>(type: "text", nullable: true),
                    is_corrected = table.Column<bool>(type: "boolean", nullable: false),
                    error_message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    prompt_tokens = table.Column<int>(type: "integer", nullable: true),
                    completion_tokens = table.Column<int>(type: "integer", nullable: true),
                    latency_ms = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_recognition_logs", x => x.id);
                    table.ForeignKey(
                        name: "FK_ai_recognition_logs_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "dishes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    aliases = table.Column<string>(type: "text", nullable: false),
                    cuisine = table.Column<short>(type: "smallint", nullable: false),
                    category = table.Column<short>(type: "smallint", nullable: false),
                    spicy_level = table.Column<short>(type: "smallint", nullable: false),
                    price_min_cents = table.Column<int>(type: "integer", nullable: true),
                    price_max_cents = table.Column<int>(type: "integer", nullable: true),
                    calories = table.Column<int>(type: "integer", nullable: true),
                    ingredients = table.Column<string>(type: "text", nullable: false),
                    tags = table.Column<string>(type: "text", nullable: false),
                    meal_times = table.Column<short>(type: "smallint", nullable: false),
                    seasons = table.Column<short>(type: "smallint", nullable: false),
                    image_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    is_builtin = table.Column<bool>(type: "boolean", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    popularity = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dishes", x => x.id);
                    table.CheckConstraint("ck_dishes_spicy_range", "spicy_level BETWEEN 0 AND 5");
                    table.ForeignKey(
                        name: "FK_dishes_users_owner_user_id",
                        column: x => x.owner_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "refresh_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    platform = table.Column<short>(type: "smallint", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    replaced_by_token_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_refresh_tokens", x => x.id);
                    table.ForeignKey(
                        name: "FK_refresh_tokens_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_cuisine_stats",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cuisine = table.Column<short>(type: "smallint", nullable: false),
                    eat_count_total = table.Column<int>(type: "integer", nullable: false),
                    last_eaten_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_cuisine_stats", x => new { x.user_id, x.cuisine });
                    table.ForeignKey(
                        name: "FK_user_cuisine_stats_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_identities",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    platform = table.Column<short>(type: "smallint", nullable: false),
                    open_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    union_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_identities", x => x.id);
                    table.ForeignKey(
                        name: "FK_user_identities_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_preferences",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    spicy_level = table.Column<short>(type: "smallint", nullable: false),
                    budget_min_cents = table.Column<int>(type: "integer", nullable: false),
                    budget_max_cents = table.Column<int>(type: "integer", nullable: false),
                    avoid_ingredients = table.Column<string>(type: "text", nullable: false),
                    preferred_cuisines = table.Column<string>(type: "text", nullable: false),
                    dining_mode_weights = table.Column<string>(type: "text", nullable: false),
                    max_distance_m = table.Column<int>(type: "integer", nullable: false),
                    onboarding_completed = table.Column<bool>(type: "boolean", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_preferences", x => x.user_id);
                    table.CheckConstraint("ck_user_preferences_budget_range", "budget_min_cents >= 0 AND budget_max_cents >= budget_min_cents");
                    table.CheckConstraint("ck_user_preferences_spicy_range", "spicy_level BETWEEN 0 AND 5");
                    table.ForeignKey(
                        name: "FK_user_preferences_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "decision_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    meal_type = table.Column<short>(type: "smallint", nullable: false),
                    dining_mode = table.Column<short>(type: "smallint", nullable: false),
                    party_size = table.Column<short>(type: "smallint", nullable: false),
                    budget_min_cents = table.Column<int>(type: "integer", nullable: true),
                    budget_max_cents = table.Column<int>(type: "integer", nullable: true),
                    latitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    longitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    weather = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    mood_tags = table.Column<string>(type: "text", nullable: false),
                    candidate_count = table.Column<int>(type: "integer", nullable: false),
                    candidates = table.Column<string>(type: "text", nullable: false),
                    chosen_dish_id = table.Column<Guid>(type: "uuid", nullable: true),
                    chosen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    engine_version = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    elapsed_ms = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_decision_sessions", x => x.id);
                    table.ForeignKey(
                        name: "FK_decision_sessions_dishes_chosen_dish_id",
                        column: x => x.chosen_dish_id,
                        principalTable: "dishes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_decision_sessions_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "recipes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    dish_id = table.Column<Guid>(type: "uuid", nullable: false),
                    servings = table.Column<short>(type: "smallint", nullable: false),
                    cook_minutes = table.Column<short>(type: "smallint", nullable: false),
                    difficulty = table.Column<short>(type: "smallint", nullable: false),
                    steps = table.Column<string>(type: "text", nullable: false),
                    tips = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recipes", x => x.id);
                    table.CheckConstraint("ck_recipes_difficulty", "difficulty BETWEEN 1 AND 3");
                    table.ForeignKey(
                        name: "FK_recipes_dishes_dish_id",
                        column: x => x.dish_id,
                        principalTable: "dishes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_dish_stats",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    dish_id = table.Column<Guid>(type: "uuid", nullable: false),
                    eat_count = table.Column<int>(type: "integer", nullable: false),
                    last_eaten_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    rating_sum = table.Column<int>(type: "integer", nullable: false),
                    rating_count = table.Column<int>(type: "integer", nullable: false),
                    would_eat_again_count = table.Column<int>(type: "integer", nullable: false),
                    would_not_eat_again_count = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_dish_stats", x => new { x.user_id, x.dish_id });
                    table.ForeignKey(
                        name: "FK_user_dish_stats_dishes_dish_id",
                        column: x => x.dish_id,
                        principalTable: "dishes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_user_dish_stats_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "meal_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    dish_id = table.Column<Guid>(type: "uuid", nullable: true),
                    dish_name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    dish_snapshot = table.Column<string>(type: "text", nullable: false),
                    meal_type = table.Column<short>(type: "smallint", nullable: false),
                    dining_mode = table.Column<short>(type: "smallint", nullable: false),
                    eaten_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    servings = table.Column<double>(type: "double precision", nullable: false),
                    calories = table.Column<int>(type: "integer", nullable: true),
                    rating = table.Column<short>(type: "smallint", nullable: true),
                    would_eat_again = table.Column<bool>(type: "boolean", nullable: true),
                    photo_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    source = table.Column<short>(type: "smallint", nullable: false),
                    decision_session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    ai_recognition_log_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_meal_records", x => x.id);
                    table.CheckConstraint("ck_meal_records_rating", "rating IS NULL OR rating BETWEEN 1 AND 5");
                    table.CheckConstraint("ck_meal_records_servings", "servings > 0 AND servings <= 10");
                    table.ForeignKey(
                        name: "FK_meal_records_ai_recognition_logs_ai_recognition_log_id",
                        column: x => x.ai_recognition_log_id,
                        principalTable: "ai_recognition_logs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_meal_records_decision_sessions_decision_session_id",
                        column: x => x.decision_session_id,
                        principalTable: "decision_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_meal_records_dishes_dish_id",
                        column: x => x.dish_id,
                        principalTable: "dishes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_meal_records_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ai_logs_corrected",
                table: "ai_recognition_logs",
                column: "created_at",
                descending: new bool[0],
                filter: "is_corrected");

            migrationBuilder.CreateIndex(
                name: "ix_ai_logs_status_created",
                table: "ai_recognition_logs",
                columns: new[] { "status", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_ai_logs_user_created",
                table: "ai_recognition_logs",
                columns: new[] { "user_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_decision_sessions_chosen_dish_id",
                table: "decision_sessions",
                column: "chosen_dish_id");

            migrationBuilder.CreateIndex(
                name: "ix_decision_sessions_user_created",
                table: "decision_sessions",
                columns: new[] { "user_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_dishes_candidates",
                table: "dishes",
                columns: new[] { "is_active", "is_deleted", "cuisine", "category" });

            migrationBuilder.CreateIndex(
                name: "ix_dishes_name",
                table: "dishes",
                column: "name");

            migrationBuilder.CreateIndex(
                name: "ix_dishes_owner",
                table: "dishes",
                column: "owner_user_id",
                filter: "owner_user_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_dishes_popularity",
                table: "dishes",
                column: "popularity",
                descending: new bool[0],
                filter: "is_active AND NOT is_deleted");

            migrationBuilder.CreateIndex(
                name: "IX_meal_records_ai_recognition_log_id",
                table: "meal_records",
                column: "ai_recognition_log_id");

            migrationBuilder.CreateIndex(
                name: "IX_meal_records_decision_session_id",
                table: "meal_records",
                column: "decision_session_id");

            migrationBuilder.CreateIndex(
                name: "IX_meal_records_dish_id",
                table: "meal_records",
                column: "dish_id");

            migrationBuilder.CreateIndex(
                name: "ix_meal_records_pending_rating",
                table: "meal_records",
                columns: new[] { "user_id", "eaten_at" },
                descending: new[] { false, true },
                filter: "rating IS NULL AND NOT is_deleted");

            migrationBuilder.CreateIndex(
                name: "ix_meal_records_user_created",
                table: "meal_records",
                columns: new[] { "user_id", "created_at" },
                descending: new[] { false, true },
                filter: "NOT is_deleted");

            migrationBuilder.CreateIndex(
                name: "ix_meal_records_user_dish",
                table: "meal_records",
                columns: new[] { "user_id", "dish_id", "eaten_at" },
                descending: new[] { false, false, true },
                filter: "NOT is_deleted");

            migrationBuilder.CreateIndex(
                name: "ix_recipes_cook_minutes",
                table: "recipes",
                column: "cook_minutes");

            migrationBuilder.CreateIndex(
                name: "ux_recipes_dish_id",
                table: "recipes",
                column: "dish_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_expires",
                table: "refresh_tokens",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_user_expires",
                table: "refresh_tokens",
                columns: new[] { "user_id", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "ux_refresh_tokens_hash",
                table: "refresh_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_cuisine_stats_last_eaten",
                table: "user_cuisine_stats",
                columns: new[] { "user_id", "last_eaten_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_user_dish_stats_dish_id",
                table: "user_dish_stats",
                column: "dish_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_dish_stats_last_eaten",
                table: "user_dish_stats",
                columns: new[] { "user_id", "last_eaten_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_user_identities_union_id",
                table: "user_identities",
                column: "union_id",
                filter: "union_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_user_identities_user_id",
                table: "user_identities",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ux_user_identities_platform_openid",
                table: "user_identities",
                columns: new[] { "platform", "open_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "meal_records");

            migrationBuilder.DropTable(
                name: "recipes");

            migrationBuilder.DropTable(
                name: "refresh_tokens");

            migrationBuilder.DropTable(
                name: "user_cuisine_stats");

            migrationBuilder.DropTable(
                name: "user_dish_stats");

            migrationBuilder.DropTable(
                name: "user_identities");

            migrationBuilder.DropTable(
                name: "user_preferences");

            migrationBuilder.DropTable(
                name: "ai_recognition_logs");

            migrationBuilder.DropTable(
                name: "decision_sessions");

            migrationBuilder.DropTable(
                name: "dishes");

            migrationBuilder.DropTable(
                name: "users");
        }
    }
}
