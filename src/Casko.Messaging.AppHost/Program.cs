var builder = DistributedApplication.CreateBuilder(args);
var mailpit = builder.AddMailPit("mailpit");

builder.AddProject<Projects.Casko_Messaging_Email_Api>("email-api")
    .WithReference(mailpit)
    .WaitFor(mailpit);

builder.Build().Run();
