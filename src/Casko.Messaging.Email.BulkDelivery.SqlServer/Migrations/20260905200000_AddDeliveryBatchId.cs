using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Casko.Messaging.Email.BulkDelivery.Migrations;

[DbContext(typeof(NotificationDbContext))]
[Migration("20260905200000_AddDeliveryBatchId")]
public partial class AddDeliveryBatchId : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(name: "DeliveryBatchId", table: "NotificationEvents", type: "uniqueidentifier", nullable: true);
        migrationBuilder.AddColumn<Guid>(name: "DeliveryBatchId", table: "NotificationDeliveries", type: "uniqueidentifier", nullable: true);
        migrationBuilder.CreateIndex(name: "IX_NotificationDeliveries_DeliveryBatchId_Status", table: "NotificationDeliveries", columns: new[] { "DeliveryBatchId", "Status" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_NotificationDeliveries_DeliveryBatchId_Status", table: "NotificationDeliveries");
        migrationBuilder.DropColumn(name: "DeliveryBatchId", table: "NotificationEvents");
        migrationBuilder.DropColumn(name: "DeliveryBatchId", table: "NotificationDeliveries");
    }
}
