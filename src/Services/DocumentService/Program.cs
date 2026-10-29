using System;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi.Models;
using Amazon.S3;
using DocumentService.Endpoints;
using DocumentService.Infrastructure;
using DocumentService.Storage;
using EventBus;
using Auditing;
using ServiceDefaults.Security;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.AddNpgsqlDbContext<DocumentDbContext>("document-db");

// Validates tokens issued by IdentityService with the shared, fail-fast JwtSettings (no built-in fallback key).
builder.Services.AddPlatformJwtAuthentication(builder.Configuration);

var minioEndpoint = builder.Configuration.GetConnectionString("minio") ?? "http://localhost:9000";
var s3Config = new AmazonS3Config
{
    ServiceURL = minioEndpoint,
    ForcePathStyle = true
};
builder.Services.AddSingleton<IAmazonS3>(new AmazonS3Client("minioadmin", "minioadmin", s3Config));
builder.Services.AddSingleton<IDocumentStorage, S3DocumentStorage>();

builder.Services.AddEventBus(builder.Configuration);
builder.Services.AddAuditPublisher("DocumentService");

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Document Service API", Version = "v1" });
    
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

    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<DocumentDbContext>();
    await dbContext.Database.EnsureCreatedAsync();
}

app.MapDocumentEndpoints();

app.Run();
