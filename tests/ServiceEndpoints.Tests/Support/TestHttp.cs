using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ServiceDefaults.Security;

namespace ServiceEndpoints.Tests.Support;

public static class TestHttp
{
    public static DefaultHttpContext NewContext(ClaimsPrincipal? user = null)
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddOptions()
            .AddProblemDetails()
            .BuildServiceProvider();

        return new DefaultHttpContext
        {
            RequestServices = services,
            User = user ?? new ClaimsPrincipal(new ClaimsIdentity()),
            Response = { Body = new MemoryStream() }
        };
    }

    /// <summary>
    /// Runs an <see cref="IResult"/> the way the framework would, including JSON serialization. Returning an EF entity
    /// graph only fails at this point (System.Text.Json cycle), so asserting on the result type alone is not enough.
    /// </summary>
    public static async Task<HttpResponseSnapshot> ExecuteAsync(IResult result)
    {
        var context = NewContext();
        await result.ExecuteAsync(context);

        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body, Encoding.UTF8);
        return new HttpResponseSnapshot(context.Response.StatusCode, await reader.ReadToEndAsync());
    }

    /// <summary>A form-file as the framework binds an uploaded file.</summary>
    public static IFormFile File(string fileName, string content, string contentType = "application/pdf")
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };
    }
}

public sealed record HttpResponseSnapshot(int StatusCode, string Body)
{
    public JsonElement Json => JsonDocument.Parse(Body).RootElement;
}

public static class TestUsers
{
    public static UserContext Create(Guid? tenantId = null, params string[] roles) =>
        new(Guid.NewGuid(), tenantId ?? Guid.Empty, roles.ToHashSet(StringComparer.Ordinal));

    /// <summary>The JWT-derived principal for a <see cref="UserContext"/>, with the claim names Identity issues.</summary>
    public static ClaimsPrincipal ToPrincipal(UserContext user)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, user.UserId.ToString()) };
        if (user.HasTenant)
        {
            claims.Add(new Claim("organizationId", user.TenantId.ToString()));
        }

        claims.AddRange(user.Roles.Select(r => new Claim(ClaimTypes.Role, r)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "test"));
    }
}
