using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using EventBus.Abstractions;
using EventBus.Events;

namespace EventBus;

public static class Extensions
{
    public static IServiceCollection AddEventBus(this IServiceCollection services, IConfiguration configuration, Action<IBusRegistrationConfigurator>? configureConsumers = null)
    {
        services.AddScoped<IEventBus, MassTransitEventBus>();

        services.AddMassTransit(x =>
        {
            x.SetKebabCaseEndpointNameFormatter();

            configureConsumers?.Invoke(x);

            x.UsingRabbitMq((context, cfg) =>
            {
                var rabbitUri = configuration.GetConnectionString("rabbitmq");
                if (!string.IsNullOrEmpty(rabbitUri))
                {
                    cfg.Host(new Uri(rabbitUri));
                }
                else
                {
                    cfg.Host("localhost", "/", h =>
                    {
                        h.Username("guest");
                        h.Password("guest");
                    });
                }

                cfg.ConfigureEndpoints(context);
            });
        });

        return services;
    }
}

internal class MassTransitEventBus : IEventBus
{
    private readonly IPublishEndpoint _publishEndpoint;

    public MassTransitEventBus(IPublishEndpoint publishEndpoint)
    {
        _publishEndpoint = publishEndpoint;
    }

    public async Task PublishAsync<T>(T @event, CancellationToken cancellationToken = default)
        where T : IntegrationEvent
    {
        await _publishEndpoint.Publish(@event, cancellationToken);
    }
}
