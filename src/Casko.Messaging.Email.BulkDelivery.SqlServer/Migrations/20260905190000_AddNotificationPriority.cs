using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;

#nullable disable

namespace Casko.Messaging.Email.BulkDelivery.Migrations;

[Migration("20260905190000_AddNotificationPriority")]
[DbContext(typeof(NotificationDbContext))]
public partial class AddNotificationPriority : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "Priority",
            table: "NotificationEvents",
            type: "int",
            nullable: false,
            defaultValue: 1);

        migrationBuilder.AddColumn<int>(
            name: "Priority",
            table: "NotificationDeliveries",
            type: "int",
            nullable: false,
            defaultValue: 1);

        migrationBuilder.CreateIndex(
            name: "IX_NotificationDeliveries_Status_Priority_CreatedUtc_Id",
            table: "NotificationDeliveries",
            columns: new[] { "Status", "Priority", "CreatedUtc", "Id" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_NotificationDeliveries_Status_Priority_CreatedUtc_Id",
            table: "NotificationDeliveries");
        migrationBuilder.DropColumn(name: "Priority", table: "NotificationEvents");
        migrationBuilder.DropColumn(name: "Priority", table: "NotificationDeliveries");
    }
}
