using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TaskManager.Application.Interfaces;
using TaskManager.Infrastructure.Persistence;
using TaskManager.Infrastructure.Repositories;
using TaskManager.Infrastructure.Services;

namespace TaskManager.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "Connection string 'DefaultConnection' was not found.");

        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseSqlServer(
                connectionString,
                sqlOptions => sqlOptions.MigrationsAssembly(
                    typeof(ApplicationDbContext).Assembly.FullName)));

        // Opt-in for ephemeral hosts. Apply the key-table migration before enabling.
        // The default Windows development configuration is unchanged.
        if (!bool.TryParse(configuration["DataProtection:PersistKeysToDatabase"] ?? "false",
            out var persistKeysToDatabase))
            throw new InvalidOperationException("DataProtection:PersistKeysToDatabase must be true or false.");
        if (persistKeysToDatabase)
        {
            services.AddDataProtection()
                .SetApplicationName("TaskFlow")
                .PersistKeysToDbContext<ApplicationDbContext>();
        }

        services.AddScoped<IAnonymousUserRepository, AnonymousUserRepository>();
        services.AddScoped<IBoardRepository, BoardRepository>();
        services.AddScoped<ITaskRepository, TaskRepository>();
        services.AddScoped<IExpiredDataCleanupService, ExpiredDataCleanupService>();

        return services;
    }
}
