using System;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi.Models;
using Elastic.Clients.Elasticsearch;
using Elastic.Transport;
using MassTransit;
using SearchService.Consumers;
using SearchService.Endpoints;
using EventBus;
using ServiceDefaults.Security;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Validates tokens issued by IdentityService with the shared, fail-fast JwtSettings (no built-in fallback key).
builder.Services.AddPlatformJwtAuthentication(builder.Configuration);

var elasticUri = builder.Configuration.GetConnectionString("elasticsearch") ?? "http://localhost:9200";
var settings = new ElasticsearchClientSettings(new Uri(elasticUri))
    .Authentication(new BasicAuthentication("elastic", "devpassword123"));
builder.Services.AddSingleton(new ElasticsearchClient(settings));

builder.Services.AddEventBus(builder.Configuration, x =>
{
    x.AddConsumer<WorkflowStepAssignedConsumer>();
    x.AddConsumer<WorkflowCompletedConsumer>();
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Search Service API", Version = "v1" });
    
    var securityScheme = new OpenApiSecurityScheme
    {
        Name = "JWT Authentication",
        Description = "Enter JWT Bearer token",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Reference = new OpenApiReference
        {
            Id = JwtBearerDefaults.AuthenticationScheme,
            Type = ReferenceType.SecurityScheme
        }
    };
    c.AddSecurityDefinition(securityScheme.Reference.Id, securityScheme);
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        { securityScheme, Array.Empty<string>() }
    });
});

var app = builder.Build();

app.MapDefaultEndpoints();

app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapSearchEndpoints();

app.Run();
