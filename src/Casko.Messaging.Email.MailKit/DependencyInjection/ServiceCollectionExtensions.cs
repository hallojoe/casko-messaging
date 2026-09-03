using Casko.Messaging.Email.MailKit.Configuration;
using Casko.Messaging.Email.MailKit.Mapping;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Casko.Messaging.Email.MailKit.DependencyInjection;

/// <summary>Provides dependency-injection registration for the MailKit email sender.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Registers a MailKit-based implementation of <see cref="IEmailSender"/>.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration section containing MailKit settings.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddMailKitEmail(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddOptions<MailKitEmailOptions>().Bind(configuration);
        services.AddSingleton<IMimeMessageFactory, MimeMessageFactory>();
        services.AddSingleton<IEmailSender, MailKitEmailSender>();
        return services;
    }
}
