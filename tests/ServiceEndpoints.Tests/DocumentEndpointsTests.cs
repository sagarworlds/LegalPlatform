using System.Text;
using Auditing;
using Contracts;
using DocumentService.Domain;
using DocumentService.Endpoints;
using DocumentService.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ServiceDefaults.Security;
using ServiceEndpoints.Tests.Support;
using Xunit;

namespace ServiceEndpoints.Tests;

public class DocumentEndpointsTests : IDisposable
{
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();

    private readonly SqliteDatabase<DocumentDbContext> _database = new();
    private readonly FakeDocumentStorage _storage = new();
    private readonly FakeEventBus _bus = new();
    private readonly IAuditPublisher _audit;
    private readonly UserContext _user = TestUsers.Create(TenantA, Roles.Attorney);

    public DocumentEndpointsTests()
    {
        _audit = TestAudit.For(_bus, "DocumentService");
    }

    public void Dispose() => _database.Dispose();

    private async Task<HttpResponseSnapshot> UploadAsync(string fileName = "contract.pdf", string content = "version one", string title = "Master agreement")
    {
        using var db = _database.NewContext();
        var result = await DocumentEndpoints.UploadDocumentAsync(
            _user, TestHttp.NewContext(), TestHttp.File(fileName, content), title, "Contract", db, _storage, _audit, NullLoggerFactory.Instance);
        return await TestHttp.ExecuteAsync(result);
    }

    private async Task<HttpResponseSnapshot> UploadVersionAsync(UserContext user, Guid documentId, string content)
    {
        using var db = _database.NewContext();
        var result = await DocumentEndpoints.UploadNewVersionAsync(
            documentId, user, TestHttp.NewContext(), TestHttp.File("contract.pdf", content), "revised", db, _storage, _audit, NullLoggerFactory.Instance);
        return await TestHttp.ExecuteAsync(result);
    }

    private async Task<HttpResponseSnapshot> MetadataAsync(UserContext user, Guid documentId)
    {
        using var db = _database.NewContext();
        return await TestHttp.ExecuteAsync(await DocumentEndpoints.GetMetadataAsync(documentId, user, db));
    }

    [Fact]
    public async Task Upload_saves_the_document_the_blob_and_audits_it()
    {
        var response = await UploadAsync();

        response.StatusCode.Should().Be(200, response.Body);
        _storage.Blobs.Should().ContainSingle();
        _bus.Of<AuditLogCreatedIntegrationEvent>().Should().ContainSingle(e => e.Action == "Upload");

        using var db = _database.NewContext();
        var version = await db.DocumentVersions.SingleAsync();
        version.VersionNumber.Should().Be(1);
        version.IsCurrent.Should().BeTrue();
        version.SHA256Hash.Should().HaveLength(64);
    }

    [Fact]
    public async Task Metadata_is_serializable_exposes_the_current_version_and_hides_the_storage_key()
    {
        var documentId = (await UploadAsync()).Json.GetProperty("documentId").GetGuid();

        var response = await MetadataAsync(_user, documentId);

        response.StatusCode.Should().Be(200, response.Body);
        response.Json.GetProperty("currentVersion").GetProperty("versionNumber").GetInt32().Should().Be(1);
        response.Body.Should().NotContainEquivalentOf("storagePath");
    }

    [Fact]
    public async Task A_new_version_becomes_the_only_current_version()
    {
        var documentId = (await UploadAsync()).Json.GetProperty("documentId").GetGuid();

        var response = await UploadVersionAsync(_user, documentId, "version two");

        response.StatusCode.Should().Be(200, response.Body);
        _storage.Blobs.Should().HaveCount(2);

        using var db = _database.NewContext();
        var versions = await db.DocumentVersions.OrderBy(v => v.VersionNumber).ToListAsync();
        versions.Select(v => (v.VersionNumber, v.IsCurrent)).Should().Equal((1, false), (2, true));

        (await MetadataAsync(_user, documentId)).Json
            .GetProperty("currentVersion").GetProperty("versionNumber").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task Version_history_lists_every_version_newest_first()
    {
        var documentId = (await UploadAsync()).Json.GetProperty("documentId").GetGuid();
        await UploadVersionAsync(_user, documentId, "version two");

        using var db = _database.NewContext();
        var response = await TestHttp.ExecuteAsync(await DocumentEndpoints.GetDocumentVersionsAsync(documentId, _user, db));

        response.Json.EnumerateArray().Select(v => v.GetProperty("versionNumber").GetInt32()).Should().Equal(2, 1);
    }

    [Fact]
    public async Task Download_returns_the_current_versions_content()
    {
        var documentId = (await UploadAsync()).Json.GetProperty("documentId").GetGuid();
        await UploadVersionAsync(_user, documentId, "version two");

        using var db = _database.NewContext();
        var result = await DocumentEndpoints.DownloadCurrentVersionAsync(documentId, _user, TestHttp.NewContext(), db, _storage, _audit);
        var response = await TestHttp.ExecuteAsync(result);

        response.StatusCode.Should().Be(200);
        response.Body.Should().Be("version two");
        _bus.Of<AuditLogCreatedIntegrationEvent>().Should().Contain(e => e.Action == "Download");
    }

    [Fact]
    public async Task Another_tenant_cannot_add_a_version_or_read_the_document()
    {
        var documentId = (await UploadAsync()).Json.GetProperty("documentId").GetGuid();
        var outsider = TestUsers.Create(TenantB, Roles.Attorney);
        var blobsBefore = _storage.Blobs.Count;

        (await UploadVersionAsync(outsider, documentId, "hijack")).StatusCode.Should().Be(404);
        (await MetadataAsync(outsider, documentId)).StatusCode.Should().Be(404);
        _storage.Blobs.Should().HaveCount(blobsBefore, "a refused upload must not store anything");
    }

    [Fact]
    public async Task A_failed_save_removes_the_uploaded_blob()
    {
        using var db = _database.NewContext();

        // A null title violates the NOT NULL column, so SaveChanges fails after the blob has been written.
        var upload = () => DocumentEndpoints.UploadDocumentAsync(
            _user, TestHttp.NewContext(), TestHttp.File("contract.pdf", "x"), title: null!, "Contract", db, _storage, _audit, NullLoggerFactory.Instance);

        await upload.Should().ThrowAsync<DbUpdateException>();
        _storage.Blobs.Should().BeEmpty("the blob would otherwise be orphaned");
        _bus.Published.Should().BeEmpty("nothing was saved, so nothing should be audited");
    }

    [Fact]
    public async Task A_failed_save_still_reports_the_original_error_when_cleanup_also_fails()
    {
        _storage.FailDeletes = true;
        using var db = _database.NewContext();

        var upload = () => DocumentEndpoints.UploadDocumentAsync(
            _user, TestHttp.NewContext(), TestHttp.File("contract.pdf", "x"), title: null!, "Contract", db, _storage, _audit, NullLoggerFactory.Instance);

        await upload.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Directory_parts_in_the_uploaded_file_name_never_reach_the_storage_key()
    {
        await UploadAsync(fileName: "../../etc/passwd.pdf");

        _storage.Blobs.Keys.Single().Should().NotContain("..").And.EndWith("_passwd.pdf");
        using var db = _database.NewContext();
        (await db.DocumentVersions.SingleAsync()).FileName.Should().Be("passwd.pdf");
    }

    [Fact]
    public void Version_numbers_are_unique_per_document()
    {
        using var db = _database.NewContext();

        db.Model.FindEntityType(typeof(DocumentVersion))!.GetIndexes()
            .Should().Contain(i => i.IsUnique && i.Properties.Select(p => p.Name).SequenceEqual(
                new[] { nameof(DocumentVersion.DocumentId), nameof(DocumentVersion.VersionNumber) }));
    }

    [Fact]
    public void AddVersion_numbers_from_the_highest_existing_version_and_keeps_a_single_current()
    {
        var document = new Document(Guid.NewGuid(), TenantA, "t", "c", Guid.NewGuid());
        var first = document.AddVersion(Guid.NewGuid(), "p1", "f", 1, "text/plain", "h", Guid.NewGuid(), "first");
        var second = document.AddVersion(Guid.NewGuid(), "p2", "f", 1, "text/plain", "h", Guid.NewGuid(), "second");

        first.VersionNumber.Should().Be(1);
        second.VersionNumber.Should().Be(2);
        document.Versions.Where(v => v.IsCurrent).Should().ContainSingle().Which.Should().BeSameAs(second);
        document.CurrentVersion.Should().BeSameAs(second);
    }
}
