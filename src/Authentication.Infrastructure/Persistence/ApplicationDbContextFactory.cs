using Authentication.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Authentication.Infrastructure.Persistence;

/// <summary>Design-time factory so `dotnet ef migrations` works without spinning up the full host.</summary>
public class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
        optionsBuilder.UseNpgsql("Host=localhost;Port=5432;Database=authentication_dev;Username=postgres;Password=postgres");
        optionsBuilder.UseOpenIddict<Guid>();

        return new ApplicationDbContext(optionsBuilder.Options, new DesignTimeTenantProvider());
    }

    private sealed class DesignTimeTenantProvider : ITenantProvider
    {
        public Guid TenantId => Guid.Empty;
        public bool IsResolved => false;
    }
}
