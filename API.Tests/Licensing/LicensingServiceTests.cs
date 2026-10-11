using API.Data;
using API.DTOs.Requests;
using API.Models;
using API.Models.Entities;
using API.Models.Licensing;
using API.Services.Implementations;
using API.Services.Interfaces;
using API.Utilities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;

namespace API.Tests.Licensing;

/// <summary>
/// Runs against a real SQLite database so the unique indexes are enforced: the single-activation rule is a
/// database constraint, not only application logic.
/// </summary>
public class LicensingServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<DeployVareDbContext> _options;
    private readonly string _keyDirectory = Path.Combine(Path.GetTempPath(), "DeployVareTests", Guid.NewGuid().ToString("N"));

    public LicensingServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<DeployVareDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var context = new DeployVareDbContext(_options);
        context.Database.EnsureCreated();
        context.Clients.Add(new Client
        {
            Id = 1,
            Name = "Acme Corporation",
            ClientCode = "ACME001",
            LicenseKey = Guid.NewGuid().ToString("N").ToUpperInvariant(),
            IsActive = true,
            CreatedBy = "tests"
        });
        context.SaveChanges();
    }

    private DeployVareDbContext NewContext() => new(_options);

    private LicensingService NewService(DeployVareDbContext context, ISigningKeyStore? keyStore = null)
    {
        var options = Options.Create(new LicensingOptions { KeyDirectory = _keyDirectory, DefaultProduct = "AuditVare" });
        var store = keyStore ?? new SigningKeyStore(options, NullLogger<SigningKeyStore>.Instance);
        return new LicensingService(context, store, options, NullLogger<LicensingService>.Instance);
    }

    private LicensingService NewServiceWithKey(DeployVareDbContext context)
    {
        var service = NewService(context);
        if (!service.GetSigningKey().Exists) service.CreateSigningKey("tests");
        return service;
    }

    private static async Task<API.DTOs.Responses.LicenseDto> IssueAsync(LicensingService service, DateTime? expiresAt = null, string product = "AuditVare") =>
        await service.IssueAsync(new IssueLicenseRequest
        {
            ClientId = 1,
            Product = product,
            ExpiresAt = expiresAt ?? DateTime.UtcNow.AddYears(1)
        }, "admin");

    /// <summary>Builds the file a client server would produce, signed by its own machine key.</summary>
    private static (string File, string PublicKey) BuildRequest(string licenseId, string customerId = "ACME001",
        string product = "AuditVare", string machineName = "SERVER-A", ECDsa? machineKey = null, string? nonce = null)
    {
        var key = machineKey ?? LicenseSigner.CreateKey();
        var publicKey = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());

        var payload = new ActivationRequestPayload
        {
            LicenseId = licenseId,
            CustomerId = customerId,
            Product = product,
            MachinePublicKey = publicKey,
            MachineName = machineName,
            ServerIdentity = @"C:\inetpub\wwwroot\AuditVare",
            Nonce = nonce ?? Guid.NewGuid().ToString("N"),
            CreatedAt = DateTime.UtcNow
        };

        var encoded = LicenseSigner.Encode(payload);
        var document = new SignedDocument
        {
            Kind = SignedDocument.ActivationRequestKind,
            Payload = encoded,
            Signature = LicenseSigner.Sign(key, encoded),
            Algorithm = LicenseSigner.Algorithm
        };

        if (machineKey == null) key.Dispose();
        return (LicenseSigner.ToFile(document), publicKey);
    }

    // ---------------------------------------------------------------------------------------------
    // Licence issuing and the .lic file
    // ---------------------------------------------------------------------------------------------
    [Fact]
    public async Task IssuedLicense_IsSignedAndVerifiesWithThePublicKey()
    {
        using var context = NewContext();
        var service = NewServiceWithKey(context);

        var license = await IssueAsync(service);
        var file = await service.GetLicenseFileAsync(license.Id);
        var document = LicenseSigner.ParseFile(file.Content);
        var payload = LicenseSigner.Decode<LicensePayload>(document.Payload);
        var publicKey = service.GetSigningKey().PublicKey!;

        Assert.Equal("AV-ACME001", license.LicenseId);
        Assert.Equal($"{license.LicenseId}.lic", file.FileName);
        Assert.Equal(SignedDocument.LicenseKind, document.Kind);
        Assert.Equal("ACME001", payload.CustomerId);
        Assert.Equal("AuditVare", payload.Product);
        Assert.True(LicenseSigner.Verify(publicKey, document.Payload, document.Signature));
    }

    [Fact]
    public async Task TamperedLicensePayloadOrSignature_FailsVerification()
    {
        using var context = NewContext();
        var service = NewServiceWithKey(context);
        var license = await IssueAsync(service);
        var document = LicenseSigner.ParseFile((await service.GetLicenseFileAsync(license.Id)).Content);
        var publicKey = service.GetSigningKey().PublicKey!;

        // Extend the expiry date inside the payload
        var payload = LicenseSigner.Decode<LicensePayload>(document.Payload);
        payload.ExpiresAt = payload.ExpiresAt.AddYears(10);
        var tamperedPayload = LicenseSigner.Encode(payload);

        Assert.False(LicenseSigner.Verify(publicKey, tamperedPayload, document.Signature));

        // Replace the signature with one from a different key
        using var otherKey = LicenseSigner.CreateKey();
        var forged = LicenseSigner.Sign(otherKey, document.Payload);
        Assert.False(LicenseSigner.Verify(publicKey, document.Payload, forged));
    }

    [Fact]
    public async Task WithoutSigningKey_IssuingIsRefused()
    {
        using var context = NewContext();
        var service = NewService(context);   // no key created: this is a partner's installation

        var error = await Assert.ThrowsAsync<LicensingException>(() => IssueAsync(service));
        Assert.Contains("no signing key", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------------------------------------------
    // Activation
    // ---------------------------------------------------------------------------------------------
    [Fact]
    public async Task FirstActivation_BindsTheLicenceToThatMachine()
    {
        using var context = NewContext();
        var service = NewServiceWithKey(context);
        var license = await IssueAsync(service);
        var (request, publicKey) = BuildRequest(license.LicenseId);

        var review = await service.ReviewActivationAsync(request);
        var approval = await service.ApproveActivationAsync(request, "admin");

        Assert.True(review.CanApprove);
        Assert.Equal("ReadyToActivate", review.Outcome);

        var response = LicenseSigner.ParseFile(approval.ResponseFile.Content);
        var payload = LicenseSigner.Decode<ActivationResponsePayload>(response.Payload);

        Assert.Equal(SignedDocument.ActivationResponseKind, response.Kind);
        Assert.Equal(LicenseSigner.Fingerprint(publicKey), payload.MachineFingerprint);
        Assert.Equal(publicKey, payload.MachinePublicKey);
        Assert.True(LicenseSigner.Verify(service.GetSigningKey().PublicKey!, response.Payload, response.Signature));
        Assert.Equal("Active", approval.License.Status);
        Assert.Equal("SERVER-A", approval.License.CurrentActivation!.MachineName);
    }

    [Fact]
    public async Task SameMachineActivatingAgain_IsIdempotent()
    {
        using var context = NewContext();
        var service = NewServiceWithKey(context);
        var license = await IssueAsync(service);

        using var machineKey = LicenseSigner.CreateKey();
        var (first, _) = BuildRequest(license.LicenseId, machineKey: machineKey);
        var (second, _) = BuildRequest(license.LicenseId, machineKey: machineKey);

        await service.ApproveActivationAsync(first, "admin");
        var review = await service.ReviewActivationAsync(second);
        var approval = await service.ApproveActivationAsync(second, "admin");

        Assert.Equal("AlreadyActivatedOnThisMachine", review.Outcome);
        Assert.True(review.CanApprove);
        Assert.Empty(approval.License.PreviousActivations);
        Assert.Equal(1, await context.LicenseActivations.CountAsync());
    }

    [Fact]
    public async Task SecondMachine_IsRejected()
    {
        using var context = NewContext();
        var service = NewServiceWithKey(context);
        var license = await IssueAsync(service);

        await service.ApproveActivationAsync(BuildRequest(license.LicenseId, machineName: "SERVER-A").File, "admin");
        var serverB = BuildRequest(license.LicenseId, machineName: "SERVER-B").File;

        var review = await service.ReviewActivationAsync(serverB);
        var error = await Assert.ThrowsAsync<LicensingException>(() => service.ApproveActivationAsync(serverB, "admin"));

        Assert.False(review.CanApprove);
        Assert.Equal("ActivatedElsewhere", review.Outcome);
        Assert.Contains("already activated on another server", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SERVER-A", error.Message);   // the other server is never named
    }

    [Fact]
    public async Task CopiedDeploymentWithoutTheMachineKey_CannotProduceAValidRequest()
    {
        using var context = NewContext();
        var service = NewServiceWithKey(context);
        var license = await IssueAsync(service);

        // Server B copies Server A's request file but has no private key, so it re-signs with its own
        var (originalFile, _) = BuildRequest(license.LicenseId);
        var original = LicenseSigner.ParseFile(originalFile);
        using var otherKey = LicenseSigner.CreateKey();

        var forged = LicenseSigner.ToFile(new SignedDocument
        {
            Kind = SignedDocument.ActivationRequestKind,
            Payload = original.Payload,                       // claims Server A's public key
            Signature = LicenseSigner.Sign(otherKey, original.Payload),
            Algorithm = LicenseSigner.Algorithm
        });

        var error = await Assert.ThrowsAsync<LicensingException>(() => service.ReviewActivationAsync(forged));
        Assert.Contains("not signed by the machine key", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExpiredRevokedOrWrongProduct_AreRefused()
    {
        using var context = NewContext();
        var service = NewServiceWithKey(context);

        // Expired: backdate the stored row, then make the service re-read it instead of its cached copy
        var expired = await IssueAsync(service, DateTime.UtcNow.AddDays(30));
        await context.Database.ExecuteSqlRawAsync(
            "UPDATE Licenses SET ExpiresAt = {0} WHERE Id = {1}", DateTime.UtcNow.AddDays(-1), expired.Id);
        context.ChangeTracker.Clear();
        Assert.Equal("Expired", (await service.ReviewActivationAsync(BuildRequest(expired.LicenseId).File)).Outcome);

        // Wrong product: a Reports licence cannot activate the AuditVare module
        var reports = await IssueAsync(service, product: "Reports");
        var wrongProduct = await service.ReviewActivationAsync(BuildRequest(reports.LicenseId, product: "AuditVare").File);
        Assert.Equal("WrongProduct", wrongProduct.Outcome);
        Assert.False(wrongProduct.CanApprove);

        var revoked = await IssueAsync(service, product: "Governance");
        await service.RevokeAsync(revoked.Id, "customer ended contract", "admin");
        Assert.Equal("Revoked", (await service.ReviewActivationAsync(BuildRequest(revoked.LicenseId, product: "Governance").File)).Outcome);

        var unknown = await service.ReviewActivationAsync(BuildRequest("AV-NOSUCH").File);
        Assert.Equal("UnknownLicense", unknown.Outcome);
    }

    // ---------------------------------------------------------------------------------------------
    // Transfer
    // ---------------------------------------------------------------------------------------------
    [Fact]
    public async Task Transfer_ReleasesServerA_AndLetsServerBActivate()
    {
        using var context = NewContext();
        var service = NewServiceWithKey(context);
        var license = await IssueAsync(service);

        var serverA = BuildRequest(license.LicenseId, machineName: "SERVER-A").File;
        var serverB = BuildRequest(license.LicenseId, machineName: "SERVER-B").File;

        await service.ApproveActivationAsync(serverA, "admin");
        await Assert.ThrowsAsync<LicensingException>(() => service.ApproveActivationAsync(serverB, "admin"));

        var transferred = await service.TransferAsync(license.Id, "client replaced the server", "admin");
        Assert.Null(transferred.CurrentActivation);
        Assert.Equal("Issued", transferred.Status);

        var approval = await service.ApproveActivationAsync(serverB, "admin");
        Assert.Equal("SERVER-B", approval.License.CurrentActivation!.MachineName);
        Assert.Single(approval.License.PreviousActivations);
        Assert.Contains("client replaced the server", approval.License.PreviousActivations[0].DeactivationReason);

        // Server A is now out: its next request is refused
        var reviewA = await service.ReviewActivationAsync(serverA);
        Assert.False(reviewA.CanApprove);
        Assert.Equal("ActivatedElsewhere", reviewA.Outcome);
    }

    // ---------------------------------------------------------------------------------------------
    // Concurrency
    // ---------------------------------------------------------------------------------------------
    [Fact]
    public async Task TwoMachinesActivatingAtTheSameTime_OnlyOneWins()
    {
        using (var setup = NewContext())
        {
            await IssueAsync(NewServiceWithKey(setup));
        }

        string licenseId;
        using (var read = NewContext())
        {
            licenseId = (await read.Licenses.AsNoTracking().FirstAsync()).LicenseId;
        }

        var requestA = BuildRequest(licenseId, machineName: "SERVER-A").File;
        var requestB = BuildRequest(licenseId, machineName: "SERVER-B").File;

        // Separate contexts, as two simultaneous requests would have
        using var contextA = NewContext();
        using var contextB = NewContext();
        var serviceA = NewService(contextA);
        var serviceB = NewService(contextB);

        var results = await Task.WhenAll(
            Run(() => serviceA.ApproveActivationAsync(requestA, "admin")),
            Run(() => serviceB.ApproveActivationAsync(requestB, "admin")));

        Assert.Equal(1, results.Count(r => r));

        using var verify = NewContext();
        var active = await verify.LicenseActivations
            .Where(a => a.Status == API.Models.Enums.ActivationStatus.Active)
            .ToListAsync();
        Assert.Single(active);

        static async Task<bool> Run(Func<Task> action)
        {
            try
            {
                await action();
                return true;
            }
            catch (LicensingException)
            {
                return false;
            }
            catch (DbUpdateException)
            {
                return false;
            }
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Upgrades
    // ---------------------------------------------------------------------------------------------
    [Fact]
    public async Task RedeployingNewBuildsOnTheSameServer_UsesNoNewActivation()
    {
        using var context = NewContext();
        var service = NewServiceWithKey(context);
        var license = await IssueAsync(service);

        using var machineKey = LicenseSigner.CreateKey();
        await service.ApproveActivationAsync(BuildRequest(license.LicenseId, machineKey: machineKey).File, "admin");
        var activatedAt = (await service.GetLicenseAsync(license.Id))!.CurrentActivation!.ActivatedAt;

        // Builds 126 and 127 are deployed; the machine key and stored activation are untouched
        for (var build = 126; build <= 127; build++)
        {
            var review = await service.ReviewActivationAsync(BuildRequest(license.LicenseId, machineKey: machineKey).File);
            Assert.Equal("AlreadyActivatedOnThisMachine", review.Outcome);
        }

        var after = (await service.GetLicenseAsync(license.Id))!;
        Assert.Equal(activatedAt, after.CurrentActivation!.ActivatedAt);
        Assert.Equal(1, await context.LicenseActivations.CountAsync());
    }

    public void Dispose()
    {
        _connection.Dispose();
        try { if (Directory.Exists(_keyDirectory)) Directory.Delete(_keyDirectory, true); } catch (IOException) { }
    }
}
