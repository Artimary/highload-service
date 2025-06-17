using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ParkingApi.Services;
using ParkingApi.Middleware;
using Prometheus;

var builder = WebApplication.CreateBuilder(args);

if (Environment.GetEnvironmentVariable("INTEGRATION_TEST") == "TRUE")
{
    builder.Configuration.AddEnvironmentVariables();
}

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Add caching services
builder.Services.AddMemoryCache();

// Add Prometheus metrics services
builder.Services.AddHealthChecks();

// Add Redis cache
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration.GetConnectionString("Redis");
    options.InstanceName = builder.Configuration.GetSection("Redis:InstanceName").Value;
});

// Add custom cache service
builder.Services.AddScoped<ICacheService, RedisCacheService>();

// Проверяем наличие переменной окружения USE_MOCK_DATA
var useMockData = Environment.GetEnvironmentVariable("USE_MOCK_DATA")?.ToLower() == "true";

if (useMockData)
{
    Console.WriteLine("MOCK DATA MODE: Using mock database service");
    builder.Services.AddSingleton<PostgresService>(sp =>
    {
        var logger = sp.GetRequiredService<ILogger<PostgresService>>();
        logger.LogWarning("Using mock database configuration. No real database connection will be established.");

        // Передаем заглушки вместо реальных строк подключения
        return new PostgresService(
            "mock-connection-master",
            "mock-connection-replica",
            "mock-connection-shard1",
            "mock-connection-shard2",
            logger);
    });
}
else
{
    // Configure services to read from configuration
    builder.Services.AddSingleton<PostgresService>(sp =>
    {
        var logger = sp.GetRequiredService<ILogger<PostgresService>>();
        var configuration = sp.GetRequiredService<IConfiguration>();

        // Получаем строки подключения или используем fallback значения
        var masterConnection = configuration["DB_CONNECTION_STRING"] ??
            Environment.GetEnvironmentVariable("DB_CONNECTION_STRING");

        if (string.IsNullOrEmpty(masterConnection))
        {
            logger.LogCritical("DB_CONNECTION_STRING is missing! Using fallback mode.");
            masterConnection = "Host=postgresql;Port=5432;Database=parking;Username=postgres;Password=secret";
        }

        var replicaConnection = configuration["DB_REPLICA_CONNECTION_STRING"] ??
            Environment.GetEnvironmentVariable("DB_REPLICA_CONNECTION_STRING") ??
            masterConnection;

        var spotsShardConnection = configuration["SHARD_1_CONNECTION_STRING"] ??
            Environment.GetEnvironmentVariable("SHARD_1_CONNECTION_STRING") ??
            "Host=postgres-shard-1;Port=5432;Database=parking_spots;Username=postgres;Password=secret";

        var bookingsShardConnection = configuration["SHARD_2_CONNECTION_STRING"] ??
            Environment.GetEnvironmentVariable("SHARD_2_CONNECTION_STRING") ??
            "Host=postgres-shard-2;Port=5432;Database=parking_bookings;Username=postgres;Password=secret";

        // Логирование для отладки
        logger.LogInformation("Configuring PostgresService with connections:");
        logger.LogInformation($"Master: {masterConnection.Substring(0, Math.Min(20, masterConnection.Length))}...");
        logger.LogInformation($"Replica: {replicaConnection.Substring(0, Math.Min(20, replicaConnection.Length))}...");
        logger.LogInformation($"Spots Shard: {spotsShardConnection.Substring(0, Math.Min(20, spotsShardConnection.Length))}...");
        logger.LogInformation($"Bookings Shard: {bookingsShardConnection.Substring(0, Math.Min(20, bookingsShardConnection.Length))}...");

        return new PostgresService(
            masterConnection,
            replicaConnection,
            spotsShardConnection,
            bookingsShardConnection,
            logger);
    });
}

// Configure services to read from configuration
builder.Services.AddSingleton<InfluxDbService>(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    var connectionString = configuration.GetConnectionString("InfluxDB") ?? "http://influxdb:8086";
    var token = configuration["InfluxDB:Token"] ?? "super-secret-token";
    var org = configuration["InfluxDB:Org"] ?? "iot_org";
    return new InfluxDbService(connectionString, token, org);
});

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    app.UseDeveloperExceptionPage();
}

app.UseRouting();

// Add advanced metrics middleware before Prometheus
app.UseMiddleware<AdvancedMetricsMiddleware>();

// Enable Prometheus metrics
app.UseHttpMetrics();
app.MapMetrics();

app.MapControllers();

app.Run();

public partial class Program { } // Делаем класс публичным для тестов