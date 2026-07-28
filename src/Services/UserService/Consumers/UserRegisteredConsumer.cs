using MassTransit;
using Contracts;
using UserService.Domain;
using UserService.Infrastructure;

namespace UserService.Consumers;

public class UserRegisteredConsumer : IConsumer<UserRegisteredIntegrationEvent>
{
    private readonly UserDbContext _dbContext;
    private readonly ILogger<UserRegisteredConsumer> _logger;

    public UserRegisteredConsumer(UserDbContext dbContext, ILogger<UserRegisteredConsumer> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<UserRegisteredIntegrationEvent> context)
    {
        var message = context.Message;
        _logger.LogInformation("Consuming UserRegisteredIntegrationEvent for user {UserId}", message.UserId);

        var profile = new UserProfile(
            message.UserId,
            message.Email,
            message.FirstName,
            message.LastName,
            message.OrganizationId
        );

        _dbContext.UserProfiles.Add(profile);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Successfully created user profile for {UserId}", message.UserId);
    }
}
