using Backend.Configuration;
using Backend.Calculation;
using Backend.Data;
using Backend.Insights.Export;
using Backend.Insights.Analysis;
using Backend.Insights.Measurement;
using Backend.Insights.Persistence;
using Backend.Insights.Workers;
using Backend.Repositories;
using Backend.Services;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<DatabaseOptions>(
            configuration.GetSection(DatabaseOptions.SectionName));

        services.AddScoped<IInsightCorrelationAccessor, InsightCorrelationAccessor>();
        services.AddScoped<IInsightPhaseTimingCollector, InsightPhaseTimingCollector>();
        services.Configure<MvcOptions>(options =>
            options.Filters.Add<InsightResponseSerializationFilter>());
        services.AddSingleton<IRunExportSchemaEvaluator, JsonSchemaNetRunExportV1SchemaEvaluator>();
        services.AddSingleton<RunExportValidator>();
        services.AddSingleton<RunExportService>();
        services.AddScoped<DbConnectionFactory>();
        services.AddScoped<IBenchmarkSchemaInitializer, BenchmarkSchemaInitializer>();
        services.AddScoped<IBenchmarkRunRepository, BenchmarkRunRepository>();
        services.AddScoped<IGraphRepository, GraphRepository>();
        services.AddScoped<IGraphService, GraphService>();
        services.AddSingleton<GraphLikelihoodCalculator>();
        services.AddSingleton<StrongestPathV1Analysis>();
        services.AddSingleton<EvidenceImpactV0Analysis>();
        services.AddSingleton<CriticalCounterV1Analyzer>();
        services.AddSingleton<RobustnessV0Analyzer>();
        services.AddSingleton<IsolatedWorkerRunner>();

        return services;
    }
}
