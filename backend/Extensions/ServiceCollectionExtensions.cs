using Backend.Configuration;
using Backend.Calculation;
using Backend.Data;
using Backend.Repositories;
using Backend.Services;

namespace Backend.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<DatabaseOptions>(
            configuration.GetSection(DatabaseOptions.SectionName));

        services.AddScoped<DbConnectionFactory>();
        services.AddScoped<IGraphRepository, GraphRepository>();
        services.AddScoped<IGraphService, GraphService>();
        services.AddSingleton<GraphLikelihoodCalculator>();
        services.AddSingleton<GraphBayesFactorPruner>();
        services.AddSingleton<GraphBayesFactorCalculator>();
        services.AddSingleton<GraphPosteriorOddsCalculator>();

        return services;
    }
}
