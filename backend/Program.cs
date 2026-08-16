using Backend.Extensions;
using Backend.Insights.Measurement;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddApplicationServices(builder.Configuration);

// CORS
var origins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddPolicy("MyCorsPolicy", policy =>
    {
        // we should be using this line, but opting for less config for now
        policy.WithOrigins(origins)
        // policy.WithOrigins()
              .AllowAnyHeader()
              .AllowAnyMethod()
              .WithExposedHeaders(
                  InsightCorrelationHeaders.RunId,
                  InsightCorrelationHeaders.SampleId,
                  "Server-Timing");
    });
});

var app = builder.Build();

app.UseCors("MyCorsPolicy");
app.UseApplicationPipeline();

app.Run();
