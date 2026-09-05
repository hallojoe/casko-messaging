using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Casko.Messaging.Email.BulkDelivery.Migrations
{
    /// <inheritdoc />
    public partial class InitialNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NotificationEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EntityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Template = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Payload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NotificationDeliveries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    NotificationEventId = table.Column<long>(type: "bigint", nullable: false),
                    RecipientId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EmailAddress = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    NormalizedEmailAddress = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastAttemptUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    NextAttemptUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SentUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ProcessingStartedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ProcessingLeaseUntilUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ProcessingWorkerId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    LastError = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    SmtpMessageId = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationDeliveries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NotificationDeliveries_NotificationEvents_NotificationEventId",
                        column: x => x.NotificationEventId,
                        principalTable: "NotificationEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDeliveries_NotificationEventId_NormalizedEmailAddress",
                table: "NotificationDeliveries",
                columns: new[] { "NotificationEventId", "NormalizedEmailAddress" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDeliveries_Status_NextAttemptUtc",
                table: "NotificationDeliveries",
                columns: new[] { "Status", "NextAttemptUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDeliveries_Status_ProcessingLeaseUntilUtc",
                table: "NotificationDeliveries",
                columns: new[] { "Status", "ProcessingLeaseUntilUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationEvents_IdempotencyKey",
                table: "NotificationEvents",
                column: "IdempotencyKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NotificationDeliveries");

            migrationBuilder.DropTable(
                name: "NotificationEvents");
        }
    }
}

