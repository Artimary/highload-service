using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using ParkingApi.Services;

namespace ParkingApi.Middleware;

public class AdvancedMetricsMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<AdvancedMetricsMiddleware> _logger;
    private readonly InfluxDbService _influxDbService;

    public AdvancedMetricsMiddleware(RequestDelegate next, ILogger<AdvancedMetricsMiddleware> logger, InfluxDbService influxDbService)
    {
        _next = next;
        _logger = logger;
        _influxDbService = influxDbService;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        var startTime = DateTime.UtcNow;

        try
        {
            await _next(context);
        }
        finally
        {
            stopwatch.Stop();
            var responseTime = stopwatch.ElapsedMilliseconds;

            // Extract endpoint details
            var method = context.Request.Method;
            var path = context.Request.Path.Value ?? "/";
            var statusCode = context.Response.StatusCode;

            // Normalize endpoint for better metrics aggregation
            var normalizedEndpoint = NormalizeEndpoint(path);

            // Log detailed metrics
            _logger.LogInformation(
                "HTTP {Method} {Path} responded {StatusCode} in {ResponseTime}ms",
                method, path, statusCode, responseTime
            );

            // Write metrics to InfluxDB asynchronously (fire and forget)
            _ = Task.Run(async () =>
            {
                try
                {
                    await _influxDbService.WriteHttpMetricsAsync(responseTime, statusCode, method, normalizedEndpoint);
                    await _influxDbService.WriteApiPerformanceMetricsAsync(
                        $"{method}_{normalizedEndpoint}",
                        responseTime,
                        statusCode < 400
                    );
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to write metrics to InfluxDB");
                }
            });
        }
    }

    private string NormalizeEndpoint(string path)
    {
        // Normalize paths to avoid cardinality explosion in metrics
        if (path.StartsWith("/parking/"))
        {
            if (path.Contains("/status"))
                return "/parking/status";
            else if (path.Contains("/availability"))
                return "/parking/availability";
            else if (path.Contains("/reserve"))
                return "/parking/reserve";
            else
                return "/parking/other";
        }
        else if (path.StartsWith("/health"))
        {
            return "/health";
        }
        else if (path.StartsWith("/metrics"))
        {
            return "/metrics";
        }
        else if (path.StartsWith("/swagger"))
        {
            return "/swagger";
        }

        return "/other";
    }
}
