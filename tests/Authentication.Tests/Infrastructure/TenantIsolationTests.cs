using Authentication.Domain.Entities;
using Authentication.Domain.Exceptions;
using Authentication.Infrastructure.Persistence;
using Authentication.Infrastructure.Persistence.Interceptors;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Authentication.Tests.Infrastructure;

public class TenantIsolationTests
{
    private static ApplicationDbContext CreateContext(Guid tenantId, Guid empresaId, string dbName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        return new ApplicationDbContext(options, new FakeTenantProvider(tenantId), new FakeEmpresaProvider(empresaId));
    }

    [Fact]
    public async Task Products_AreInvisible_ToOtherTenants()
    {
        var dbName = Guid.NewGuid().ToString();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var empresaA = Guid.NewGuid();

        using (var seedContext = CreateContext(tenantA, empresaA, dbName))
        {
            seedContext.Products.Add(new Product
            {
                TenantId = tenantA,
                EmpresaId = empresaA,
                Name = "Tenant A product",
                Price = 1,
                StockQuantity = 1,
                CreatedAtUtc = DateTimeOffset.UtcNow
            });
            await seedContext.SaveChangesAsync(default);
        }

        using var tenantBContext = CreateContext(tenantB, Guid.NewGuid(), dbName);
        var visibleToTenantB = await tenantBContext.Products.ToListAsync();

        visibleToTenantB.Should().BeEmpty();
    }

    [Fact]
    public async Task Products_AreInvisible_ToOtherEmpresaInSameTenant()
    {
        var dbName = Guid.NewGuid().ToString();
        var tenant = Guid.NewGuid();
        var empresaA = Guid.NewGuid();
        var empresaB = Guid.NewGuid();

        using (var seedContext = CreateContext(tenant, empresaA, dbName))
        {
            seedContext.Products.Add(new Product
            {
                TenantId = tenant,
                EmpresaId = empresaA,
                Name = "Empresa A product",
                Price = 1,
                StockQuantity = 1,
                CreatedAtUtc = DateTimeOffset.UtcNow
            });
            await seedContext.SaveChangesAsync(default);
        }

        // Same tenant, different empresa - the finer-grained dimension must
        // isolate independently of the tenant check.
        using var empresaBContext = CreateContext(tenant, empresaB, dbName);
        var visibleToEmpresaB = await empresaBContext.Products.ToListAsync();

        visibleToEmpresaB.Should().BeEmpty();
    }

    [Fact]
    public async Task Products_AreVisible_ToOwningTenantAndEmpresa()
    {
        var dbName = Guid.NewGuid().ToString();
        var tenantA = Guid.NewGuid();
        var empresaA = Guid.NewGuid();

        using (var seedContext = CreateContext(tenantA, empresaA, dbName))
        {
            seedContext.Products.Add(new Product
            {
                TenantId = tenantA,
                EmpresaId = empresaA,
                Name = "Tenant A product",
                Price = 1,
                StockQuantity = 1,
                CreatedAtUtc = DateTimeOffset.UtcNow
            });
            await seedContext.SaveChangesAsync(default);
        }

        using var sameContext = CreateContext(tenantA, empresaA, dbName);
        var visible = await sameContext.Products.ToListAsync();

        visible.Should().ContainSingle(p => p.Name == "Tenant A product");
    }

    [Fact]
    public async Task SaveChanges_RejectsRowForAnotherTenant()
    {
        var currentTenant = Guid.NewGuid();
        var otherTenant = Guid.NewGuid();
        var currentEmpresa = Guid.NewGuid();

        var interceptor = new AuditableEntitySaveChangesInterceptor(
            new FakeTenantProvider(currentTenant), new FakeEmpresaProvider(currentEmpresa), new FakeCurrentUserService());

        var contextOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(interceptor)
            .Options;

        using var context = new ApplicationDbContext(
            contextOptions, new FakeTenantProvider(currentTenant), new FakeEmpresaProvider(currentEmpresa));

        context.Products.Add(new Product
        {
            TenantId = otherTenant,
            EmpresaId = currentEmpresa,
            Name = "Cross-tenant attempt",
            Price = 1,
            StockQuantity = 1
        });

        var act = async () => await context.SaveChangesAsync(default);

        await act.Should().ThrowAsync<TenantMismatchException>();
    }

    [Fact]
    public async Task SaveChanges_RejectsRowForAnotherEmpresa()
    {
        var currentTenant = Guid.NewGuid();
        var currentEmpresa = Guid.NewGuid();
        var otherEmpresa = Guid.NewGuid();

        var interceptor = new AuditableEntitySaveChangesInterceptor(
            new FakeTenantProvider(currentTenant), new FakeEmpresaProvider(currentEmpresa), new FakeCurrentUserService());

        var contextOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(interceptor)
            .Options;

        using var context = new ApplicationDbContext(
            contextOptions, new FakeTenantProvider(currentTenant), new FakeEmpresaProvider(currentEmpresa));

        context.Products.Add(new Product
        {
            TenantId = currentTenant,
            EmpresaId = otherEmpresa,
            Name = "Cross-empresa attempt",
            Price = 1,
            StockQuantity = 1
        });

        var act = async () => await context.SaveChangesAsync(default);

        await act.Should().ThrowAsync<EmpresaMismatchException>();
    }
}
