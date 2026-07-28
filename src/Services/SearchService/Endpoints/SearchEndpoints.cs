using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.QueryDsl;
using SearchService.Domain;

namespace SearchService.Endpoints;

public static class SearchEndpoints
{
    private const string IndexName = "legal-index";

    public static IEndpointRouteBuilder MapSearchEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/search", SearchAsync).RequireAuthorization();
        return endpoints;
    }

    private static async Task<IResult> SearchAsync(
        string q,
        string? type,
        HttpContext httpContext,
        ElasticsearchClient esClient)
    {
        var (_, tenantId) = GetUserContext(httpContext);

        try
        {
            var response = await esClient.SearchAsync<SearchItem>(s => s
                .Index(IndexName)
                .Query(query => query
                    .Bool(b => b
                        .Must(
                            m => m.MultiMatch(mm => mm
                                .Fields(new[] { "title", "description" })
                                .Query(q)
                                .Fuzziness(new Fuzziness("AUTO"))
                            )
                        )
                        .Filter(f => {
                            if (string.IsNullOrEmpty(type))
                            {
                                f.Term(t => t.Field(item => item.TenantId).Value(tenantId.ToString()));
                            }
                            else
                            {
                                f.Bool(b => b.Must(
                                    m1 => m1.Term(t => t.Field(item => item.TenantId).Value(tenantId.ToString())),
                                    m2 => m2.Term(t => t.Field(item => item.Type).Value(type))
                                ));
                            }
                        })
                    )
                )
            );

            if (!response.IsValidResponse)
            {
                return Results.Problem($"Elasticsearch search failed: {response.ElasticsearchServerError?.Error.Reason}");
            }

            var results = response.Documents.ToList();
            return Results.Ok(results);
        }
        catch (Exception ex)
        {
            return Results.Problem($"Search failed due to internal exception: {ex.Message}");
        }
    }

    private static (Guid UserId, Guid TenantId) GetUserContext(HttpContext httpContext)
    {
        var userIdStr = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var tenantIdStr = httpContext.User.FindFirstValue("organizationId") ?? httpContext.User.FindFirstValue("tenant_id");

        var userId = Guid.TryParse(userIdStr, out var u) ? u : Guid.Empty;
        var tenantId = Guid.TryParse(tenantIdStr, out var t) ? t : Guid.Empty;

        return (userId, tenantId);
    }
}
