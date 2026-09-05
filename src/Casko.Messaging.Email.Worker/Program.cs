using Casko.Messaging.Email.BulkDelivery;
using Casko.Messaging.Email.MailKit.Configuration;
using Casko.Messaging.Email.Worker;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSqlServerNotifications(builder.Configuration.GetConnectionString("notifications"));
builder.Services.AddOptions<EmailDeliveryWorkerOptions>().Bind(builder.Configuration.GetSection("EmailDelivery")).Validate(o => o.BatchSize > 0 && o.Concurrency > 0 && o.CriticalBatchSize > 0 && o.CriticalConcurrency > 0 && o.MaximumAttempts > 0 && o.PollInterval > TimeSpan.Zero && o.ProcessingLeaseDuration > TimeSpan.Zero && o.BulkPromotionAfter > TimeSpan.Zero, "EmailDelivery options are invalid.").ValidateOnStart();
builder.Services.AddOptions<MailKitEmailOptions>().Bind(builder.Configuration.GetSection("Email:MailKit")).Validate(o => !string.IsNullOrWhiteSpace(o.Host) && !string.IsNullOrWhiteSpace(o.FromAddress), "SMTP host and from address are required.").ValidateOnStart();
builder.Services.AddHostedService<DeliveryWorker>();
await builder.Build().RunAsync();
