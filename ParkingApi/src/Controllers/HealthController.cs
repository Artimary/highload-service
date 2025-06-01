using Microsoft.AspNetCore.Mvc;
using ParkingApi.Services;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ParkingApi.Controllers;

[ApiController]
[Route("health")]
public class HealthController : ControllerBase
{
    private readonly InfluxDbService _influxDbService;
    private readonly PostgresService _postgresService;

    public HealthController(InfluxDbService influxDbService, PostgresService postgresService)
    {
        _influxDbService = influxDbService;
        _postgresService = postgresService;
    }

    [HttpGet]
    public async Task<ActionResult> GetHealth()
    {
        var healthMetrics = await _influxDbService.GetSystemHealthMetricsAsync();

        // Test PostgreSQL connectivity
        var postgresHealthy = true;
        try
        {
            await _postgresService.GetParkingLotLocationAsync(1);
        }
        catch
        {
            postgresHealthy = false;
        }

        var response = new
        {
            status = healthMetrics.IsHealthy && postgresHealthy ? "healthy" : "unhealthy",
            timestamp = DateTime.UtcNow,
            services = new
            {
                api = new
                {
                    status = healthMetrics.IsHealthy ? "healthy" : "unhealthy",
                    totalRequests = healthMetrics.TotalRequests,
                    errorRequests = healthMetrics.ErrorRequests,
                    errorRate = healthMetrics.ErrorRate
                },
                database = new
                {
                    postgres = postgresHealthy ? "healthy" : "unhealthy",
                    influxdb = "healthy" // If we got metrics, InfluxDB is working
                }
            }
        };

        var statusCode = (healthMetrics.IsHealthy && postgresHealthy) ? 200 : 503;
        return StatusCode(statusCode, response);
    }

    [HttpGet("detailed")]
    public async Task<ActionResult> GetDetailedHealth()
    {
        var healthMetrics = await _influxDbService.GetSystemHealthMetricsAsync();

        return Ok(new
        {
            timestamp = DateTime.UtcNow,
            api_health = healthMetrics,
            performance_targets = new
            {
                target_response_time_ms = 200,
                max_acceptable_response_time_ms = 500,
                max_acceptable_error_rate_percent = 5
            },
            recommendations = GetHealthRecommendations(healthMetrics)
        });
    }

    private string[] GetHealthRecommendations(HealthMetrics metrics)
    {
        var recommendations = new List<string>();

        if (metrics.ErrorRate > 5)
        {
            recommendations.Add("High error rate detected. Check application logs and database connectivity.");
        }

        if (metrics.TotalRequests == 0)
        {
            recommendations.Add("No recent API activity detected. Verify monitoring system is working correctly.");
        }

        if (!metrics.IsHealthy)
        {
            recommendations.Add("System health check failed. Review all service dependencies.");
        }

        if (recommendations.Count == 0)
        {
            recommendations.Add("System is operating within normal parameters.");
        }

        return recommendations.ToArray();
    }
}
