var builder = DistributedApplication.CreateBuilder(args);
var mailpit = builder.AddMailPit("mailpit");
var greenmail = builder.AddContainer("greenmail", "greenmail/standalone", "2.1.13")
    .WithEnvironment("GREENMAIL_OPTS", "-Dgreenmail.setup.test.all -Dgreenmail.hostname=0.0.0.0 -Dgreenmail.users=alice:password@example.test,bob:password@example.test -Dgreenmail.users.login=email")
    .WithEndpoint(name: "imap", targetPort: 3143, scheme: "imap")
    .WithEndpoint(name: "smtp", targetPort: 3025, scheme: "smtp")
    .WithHttpEndpoint(name: "api", targetPort: 8080);
var roundcube = builder.AddContainer("roundcube", "roundcube/roundcubemail")
    .WithEnvironment("ROUNDCUBEMAIL_DEFAULT_HOST", greenmail.GetEndpoint("imap").Property(EndpointProperty.Host))
    .WithEnvironment("ROUNDCUBEMAIL_DEFAULT_PORT", greenmail.GetEndpoint("imap").Property(EndpointProperty.Port))
    .WithEnvironment("ROUNDCUBEMAIL_SMTP_SERVER", mailpit.GetEndpoint("smtp").Property(EndpointProperty.Host))
    .WithEnvironment("ROUNDCUBEMAIL_SMTP_PORT", mailpit.GetEndpoint("smtp").Property(EndpointProperty.Port))
    .WithHttpEndpoint(targetPort: 80)
    .WaitFor(greenmail)
    .WaitFor(mailpit);

builder.AddProject<Projects.Casko_Messaging_Email_Api>("email-api")
    .WithReference(mailpit)
    .WithEnvironment("Email__MailKit__Mailboxes__Support__Address", "alice@example.test")
    .WithEnvironment("Email__MailKit__Mailboxes__Support__Host", greenmail.GetEndpoint("imap").Property(EndpointProperty.Host))
    .WithEnvironment("Email__MailKit__Mailboxes__Support__Port", greenmail.GetEndpoint("imap").Property(EndpointProperty.Port))
    .WithEnvironment("Email__MailKit__Mailboxes__Support__UseSsl", "false")
    .WithEnvironment("Email__MailKit__Mailboxes__Support__Username", "alice@example.test")
    .WithEnvironment("Email__MailKit__Mailboxes__Support__Password", "password")
    .WithEnvironment("Email__GreenMail__Smtp__Host", greenmail.GetEndpoint("smtp").Property(EndpointProperty.Host))
    .WithEnvironment("Email__GreenMail__Smtp__Port", greenmail.GetEndpoint("smtp").Property(EndpointProperty.Port))
    .WithEnvironment("Email__GreenMail__Smtp__Username", "alice@example.test")
    .WithEnvironment("Email__GreenMail__Smtp__Password", "password")
    .WaitFor(mailpit)
    .WaitFor(greenmail);

builder.Build().Run();
