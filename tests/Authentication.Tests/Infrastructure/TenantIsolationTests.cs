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
    private static ApplicationDbContext CreateContext(Guid tenantId, string dbName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        return new ApplicationDbContext(options, new FakeTenantProvider(tenantId));
    }

    [Fact]
    public async Task Products_AreInvisible_ToOtherTenants()
    {
        var dbName = Guid.NewGuid().ToString();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        using (var seedContext = CreateContext(tenantA, dbName))
        {
            seedContext.Products.Add(new Product
            {
                TenantId = tenantA,
                Name = "Tenant A product",
                Price = 1,
                StockQuantity = 1,
                CreatedAtUtc = DateTimeOffset.UtcNow
            });
            await seedContext.SaveChangesAsync(default);
        }

        using var tenantBContext = CreateContext(tenantB, dbName);
        var visibleToTenantB = await tenantBContext.Products.ToListAsync();

        visibleToTenantB.Should().BeEmpty();
    }

    [Fact]
    public async Task Products_AreVisible_ToOwningTenant()
    {
        var dbName = Guid.NewGuid().ToString();
        var tenantA = Guid.NewGuid();

        using (var seedContext = CreateContext(tenantA, dbName))
        {
            seedContext.Products.Add(new Product
            {
                TenantId = tenantA,
                Name = "Tenant A product",
                Price = 1,
                StockQuantity = 1,
                CreatedAtUtc = DateTimeOffset.UtcNow
            });
            await seedContext.SaveChangesAsync(default);
        }

        using var sameTenantContext = CreateContext(tenantA, dbName);
        var visible = await sameTenantContext.Products.ToListAsync();

        visible.Should().ContainSingle(p => p.Name == "Tenant A product");
    }

    [Fact]
    public async Task SaveChanges_RejectsRowForAnotherTenant()
    {
        var currentTenant = Guid.NewGuid();
        var otherTenant = Guid.NewGuid();

        var interceptor = new AuditableEntitySaveChangesInterceptor(
            new FakeTenantProvider(currentTenant), new FakeCurrentUserService());

        var contextOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(interceptor)
            .Options;

        using var context = new ApplicationDbContext(contextOptions, new FakeTenantProvider(currentTenant));

        context.Products.Add(new Product
        {
            TenantId = otherTenant,
            Name = "Cross-tenant attempt",
            Price = 1,
            StockQuantity = 1
        });

        var act = async () => await context.SaveChangesAsync(default);

        await act.Should().ThrowAsync<TenantMismatchException>();
    }
}
