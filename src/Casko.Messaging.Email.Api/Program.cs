using Casko.Messaging.Email.Api.Components;
using Casko.Messaging.Email.Api.Services;
using Casko.Messaging.Email.Api;
using Casko.Messaging.Email.BulkDelivery;
using Casko.Messaging.Email.MailKit.Configuration;
using Casko.Messaging.Email.MailKit.DependencyInjection;
using Casko.Messaging.Email.Threading;
using Casko.OpenTelemetry.Extensions.AspNetCore;
using Ganss.Xss;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

builder.AddOpinionatedOpenTelemetry();
builder.Services.AddOpenApi();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddMudServices();
ConfigureMailPitSmtp(builder.Configuration);

builder.Services.AddMailKitEmail(builder.Configuration.GetSection("Email:MailKit"));
builder.Services.AddSingleton<IEmailThreadBuilder, EmailThreadBuilder>();
builder.Services.AddSingleton<HtmlSanitizer>();
builder.Services.AddScoped<InboxQueryService>();
builder.Services.AddSqlServerNotifications(builder.Configuration.GetConnectionString("notifications"));
builder.Services.Configure<NotificationIngestionOptions>(builder.Configuration.GetSection("Notifications:Ingestion"));

var app = builder.Build();

if (builder.Configuration.GetValue("Notifications:ApplyMigrations", true))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<INotificationStoreInitializer>().InitializeAsync();
}

if (app.Environment.IsDevelopment()) app.MapOpenApi();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapBulkDeliveryEndpoints();
app.MapDeliveryStatusEndpoints();
app.MapEmailSenderEndpoints();
app.MapDemoFlowsEndpoints();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();

static void ConfigureMailPitSmtp(ConfigurationManager configuration)
{
    var connection = configuration.GetConnectionString("mailpit");
    var endpoint = connection?.Split(';', StringSplitOptions.RemoveEmptyEntries)
        .Select(part => part.Split('=', 2))
        .FirstOrDefault(parts => parts.Length == 2 && string.Equals(parts[0], "endpoint", StringComparison.OrdinalIgnoreCase))?[1];
    if (endpoint is not null && Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
    {
        configuration["Email:MailKit:Host"] = uri.Host;
        configuration["Email:MailKit:Port"] = uri.Port.ToString();
        configuration["Email:MailKit:UseSsl"] = "false";
    }
}

public partial class Program;
