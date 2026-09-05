var builder = DistributedApplication.CreateBuilder(args);
var sqlPassword = builder.AddParameter("sql-password", secret: true);

var sql = builder.AddSqlServer("sql", password: sqlPassword)
    .WithImage("azure-sql-edge", "latest")
    .WithImageRegistry("mcr.microsoft.com")
    .WithDataVolume("messaging-db-data-volume")
    .WithDbGate();

var notifications = sql.AddDatabase("notifications");

var mailpit = builder.AddMailPit("mailpit");

var greenmail = builder.AddContainer("greenmail", "greenmail/standalone", "2.1.13")
    .WithEnvironment("GREENMAIL_OPTS", "-Dgreenmail.setup.test.all -Dgreenmail.hostname=0.0.0.0 -Dgreenmail.users=support:password@example.test,alice:password@example.test,bob:password@example.test -Dgreenmail.users.login=email")
    .WithEndpoint(name: "imap", targetPort: 3143, scheme: "imap")
    .WithEndpoint(name: "smtp", targetPort: 3025, scheme: "smtp")
    .WithHttpEndpoint(name: "api", targetPort: 8080);

var roundcube = builder.AddContainer("roundcube", "roundcube/roundcubemail")
    .WithEnvironment("ROUNDCUBEMAIL_DEFAULT_HOST", greenmail.GetEndpoint("imap").Property(EndpointProperty.Host))
    .WithEnvironment("ROUNDCUBEMAIL_DEFAULT_PORT", greenmail.GetEndpoint("imap").Property(EndpointProperty.Port))
    .WithEnvironment("ROUNDCUBEMAIL_SMTP_SERVER", mailpit.GetEndpoint("smtp").Property(EndpointProperty.Host))
    .WithEnvironment("ROUNDCUBEMAIL_SMTP_PORT", mailpit.GetEndpoint("smtp").Property(EndpointProperty.Port))
    .WithBindMount("./roundcube-config", "/var/roundcube/config", isReadOnly: true)
    .WithHttpEndpoint(targetPort: 80)
    .WaitFor(greenmail)
    .WaitFor(mailpit);

builder.AddProject<Projects.Casko_Messaging_Email_Api>("email-api")
    .WithReference(notifications)
    .WithReference(mailpit)
    .WithEnvironment("Email__MailKit__Mailboxes__Support__Address", "alice@example.test")
    .WithEnvironment("Email__MailKit__Mailboxes__Support__Host", greenmail.GetEndpoint("imap").Property(EndpointProperty.Host))
    .WithEnvironment("Email__MailKit__Mailboxes__Support__Port", greenmail.GetEndpoint("imap").Property(EndpointProperty.Port))
    .WithEnvironment("Email__MailKit__Mailboxes__Support__UseSsl", "false")
    .WithEnvironment("Email__MailKit__Mailboxes__Support__Username", "alice@example.test")
    .WithEnvironment("Email__MailKit__Mailboxes__Support__Password", "password")
    .WithEnvironment("Email__MailKit__Mailboxes__Sales__Address", "bob@example.test")
    .WithEnvironment("Email__MailKit__Mailboxes__Sales__Host", greenmail.GetEndpoint("imap").Property(EndpointProperty.Host))
    .WithEnvironment("Email__MailKit__Mailboxes__Sales__Port", greenmail.GetEndpoint("imap").Property(EndpointProperty.Port))
    .WithEnvironment("Email__MailKit__Mailboxes__Sales__UseSsl", "false")
    .WithEnvironment("Email__MailKit__Mailboxes__Sales__Username", "bob@example.test")
    .WithEnvironment("Email__MailKit__Mailboxes__Sales__Password", "password")
    .WithEnvironment("Email__GreenMail__Smtp__Host", greenmail.GetEndpoint("smtp").Property(EndpointProperty.Host))
    .WithEnvironment("Email__GreenMail__Smtp__Port", greenmail.GetEndpoint("smtp").Property(EndpointProperty.Port))
    .WithEnvironment("Email__GreenMail__Smtp__Username", "alice@example.test")
    .WithEnvironment("Email__GreenMail__Smtp__Password", "password")
    .WaitFor(notifications)
    .WaitFor(mailpit)
    .WaitFor(greenmail);

builder.AddProject<Projects.Casko_Messaging_Email_Worker>("email-worker")
    .WithReference(notifications)
    .WithEnvironment("Email__MailKit__Host", greenmail.GetEndpoint("smtp").Property(EndpointProperty.Host))
    .WithEnvironment("Email__MailKit__Port", greenmail.GetEndpoint("smtp").Property(EndpointProperty.Port))
    .WithEnvironment("Email__MailKit__UseSsl", "false")
    .WithEnvironment("Email__MailKit__FromAddress", "noreply@casko.local")
    .WithEnvironment("Email__MailKit__Username", "alice@example.test")
    .WithEnvironment("Email__MailKit__Password", "password")
    .WaitFor(notifications)
    .WaitFor(greenmail);

builder.Build().Run();
