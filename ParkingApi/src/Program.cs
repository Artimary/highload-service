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

// Configure services to read from configuration
builder.Services.AddSingleton<InfluxDbService>(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    var connectionString = configuration.GetConnectionString("InfluxDB") ?? "http://influxdb:8086";
    var token = configuration["InfluxDB:Token"] ?? "super-secret-token";
    var org = configuration["InfluxDB:Org"] ?? "iot_org";
    return new InfluxDbService(connectionString, token, org);
});

builder.Services.AddSingleton<PostgresService>(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    var connectionString = configuration.GetConnectionString("PostgreSQL") ?? "Host=postgresql;Database=parking;Username=postgres;Password=secret";
    return new PostgresService(connectionString);
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