using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using UserService.Endpoints;
using UserService.Infrastructure;
using UserService.Consumers;
using EventBus;
using MassTransit;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// PostgreSQL Database integration via Aspire resource connection
builder.AddNpgsqlDbContext<UserDbContext>("user_db");

// EventBus with registered consumer
builder.Services.AddEventBus(builder.Configuration, cfg =>
{
    cfg.AddConsumer<UserRegisteredConsumer>();
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();

    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<UserDbContext>();
    await dbContext.Database.MigrateAsync();
}

app.MapUserEndpoints();

app.Run();
