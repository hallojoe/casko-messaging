var builder = DistributedApplication.CreateBuilder(args);
var mailpit = builder.AddMailPit("mailpit");
var greenmailPassword = builder.AddParameter(
    "greenmail-password",
    () => Guid.NewGuid().ToString("N"),
    publishValueAsDefault: false,
    secret: true);
var greenmail = builder.AddContainer("greenmail", "greenmail/standalone", "2.1.13")
    .WithEnvironment("GREENMAIL_OPTS", $"-Dgreenmail.setup.test.all -Dgreenmail.hostname=0.0.0.0 -Dgreenmail.users=support@example.test:{greenmailPassword}")
    .WithEndpoint(name: "imap", targetPort: 3143, scheme: "imap")
    .WithEndpoint(name: "smtp", targetPort: 3025, scheme: "smtp");

builder.AddProject<Projects.Casko_Messaging_Email_Api>("email-api")
    .WithReference(mailpit)
    .WithEnvironment("Email__MailKit__Mailboxes__Support__Address", "support@example.test")
    .WithEnvironment("Email__MailKit__Mailboxes__Support__Host", greenmail.GetEndpoint("imap").Property(EndpointProperty.Host))
    .WithEnvironment("Email__MailKit__Mailboxes__Support__Port", greenmail.GetEndpoint("imap").Property(EndpointProperty.Port))
    .WithEnvironment("Email__MailKit__Mailboxes__Support__UseSsl", "false")
    .WithEnvironment("Email__MailKit__Mailboxes__Support__Username", "support@example.test")
    .WithEnvironment("Email__MailKit__Mailboxes__Support__Password", greenmailPassword)
    .WithEnvironment("Email__GreenMail__Smtp__Host", greenmail.GetEndpoint("smtp").Property(EndpointProperty.Host))
    .WithEnvironment("Email__GreenMail__Smtp__Port", greenmail.GetEndpoint("smtp").Property(EndpointProperty.Port))
    .WithEnvironment("Email__GreenMail__Smtp__Username", "support@example.test")
    .WithEnvironment("Email__GreenMail__Smtp__Password", greenmailPassword)
    .WaitFor(mailpit)
    .WaitFor(greenmail);

builder.Build().Run();
