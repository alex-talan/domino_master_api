using Application.Abstractions;
using Application.Domino;
using Infrastructure.Domino;
using Infrastructure.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<DominoOptions>()
            .Bind(configuration.GetSection(DominoOptions.SectionName))
            .Validate(options => options.PlayerEndpoints.Length == 4, "Domino:PlayerEndpoints must contain exactly four endpoints.")
            .Validate(options => options.PlayerTimeoutSeconds > 0, "Domino:PlayerTimeoutSeconds must be greater than zero.")
            .ValidateOnStart();

        services.AddHttpClient<IPlayerClient, PlayerHttpClient>((serviceProvider, client) =>
        {
            DominoOptions options = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<DominoOptions>>().Value;
            client.Timeout = TimeSpan.FromSeconds(options.PlayerTimeoutSeconds);
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        });
        services.AddSingleton<IDominoRandomizer, RandomDominoRandomizer>();
        services.AddSingleton<IGameEventSink, LoggingGameEventSink>();
        services.AddSingleton<DominoGameService>();

        return services;
    }
}
