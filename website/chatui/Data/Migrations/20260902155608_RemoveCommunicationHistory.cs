using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace chatui.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveCommunicationHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "meeting_summaries");

            migrationBuilder.DropTable(
                name: "messages");

            migrationBuilder.DropTable(
                name: "participants");

            migrationBuilder.DropTable(
                name: "turn_translations");

            migrationBuilder.DropTable(
                name: "conversations");

            migrationBuilder.DropTable(
                name: "turns");

            migrationBuilder.DropTable(
                name: "meetings");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "conversations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_a_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_b_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conversations", x => x.id);
                    table.CheckConstraint("ck_conversations_ordered", "user_a_id < user_b_id");
                    table.ForeignKey(
                        name: "fk_conversations_users_user_a_id",
                        column: x => x.user_a_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_conversations_users_user_b_id",
                        column: x => x.user_b_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "meetings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    initiator_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "active"),
                    title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_meetings", x => x.id);
                    table.CheckConstraint("ck_meetings_status", "status IN ('active','ended')");
                    table.ForeignKey(
                        name: "fk_meetings_users_initiator_id",
                        column: x => x.initiator_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    embedding = table.Column<Vector>(type: "vector(1536)", nullable: true),
                    original = table.Column<string>(type: "text", nullable: false),
                    source_lang = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    target_lang = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    translated = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_messages", x => x.id);
                    table.ForeignKey(
                        name: "fk_messages_conversations_conversation_id",
                        column: x => x.conversation_id,
                        principalTable: "conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_messages_users_from_user_id",
                        column: x => x.from_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "meeting_summaries",
                columns: table => new
                {
                    meeting_id = table.Column<Guid>(type: "uuid", nullable: false),
                    language = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    generated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    summary = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_meeting_summaries", x => new { x.meeting_id, x.language });
                    table.ForeignKey(
                        name: "fk_meeting_summaries_meetings_meeting_id",
                        column: x => x.meeting_id,
                        principalTable: "meetings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "participants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    meeting_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    joined_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    language = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    left_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    role = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "listener")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_participants", x => x.id);
                    table.CheckConstraint("ck_participants_role", "role IN ('initiator','listener')");
                    table.ForeignKey(
                        name: "fk_participants_meetings_meeting_id",
                        column: x => x.meeting_id,
                        principalTable: "meetings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_participants_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "turns",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    addressee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    meeting_id = table.Column<Guid>(type: "uuid", nullable: false),
                    speaker_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    embedding = table.Column<Vector>(type: "vector(1536)", nullable: true),
                    original = table.Column<string>(type: "text", nullable: false),
                    source_lang = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_turns", x => x.id);
                    table.ForeignKey(
                        name: "fk_turns_meetings_meeting_id",
                        column: x => x.meeting_id,
                        principalTable: "meetings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_turns_users_addressee_id",
                        column: x => x.addressee_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_turns_users_speaker_id",
                        column: x => x.speaker_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "turn_translations",
                columns: table => new
                {
                    turn_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_lang = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    translated = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_turn_translations", x => new { x.turn_id, x.target_lang });
                    table.ForeignKey(
                        name: "fk_turn_translations_turns_turn_id",
                        column: x => x.turn_id,
                        principalTable: "turns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_conversations_user_a_id",
                table: "conversations",
                column: "user_a_id");

            migrationBuilder.CreateIndex(
                name: "ix_conversations_user_a_id_user_b_id",
                table: "conversations",
                columns: new[] { "user_a_id", "user_b_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_conversations_user_b_id",
                table: "conversations",
                column: "user_b_id");

            migrationBuilder.CreateIndex(
                name: "ix_meetings_initiator_id_created_at",
                table: "meetings",
                columns: new[] { "initiator_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_messages_conversation_id_created_at",
                table: "messages",
                columns: new[] { "conversation_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_messages_from_user_id",
                table: "messages",
                column: "from_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_participants_meeting_id_user_id",
                table: "participants",
                columns: new[] { "meeting_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_participants_user_id_joined_at",
                table: "participants",
                columns: new[] { "user_id", "joined_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_turns_addressee_id",
                table: "turns",
                column: "addressee_id");

            migrationBuilder.CreateIndex(
                name: "ix_turns_meeting_id_created_at",
                table: "turns",
                columns: new[] { "meeting_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_turns_speaker_id",
                table: "turns",
                column: "speaker_id");
        }
    }
}
