using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TaskManager.Application.Interfaces;
using TaskManager.Infrastructure.Persistence;
using TaskManager.Infrastructure.Repositories;

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

        services.AddScoped<IAnonymousUserRepository, AnonymousUserRepository>();
        services.AddScoped<IBoardRepository, BoardRepository>();

        return services;
    }
}
