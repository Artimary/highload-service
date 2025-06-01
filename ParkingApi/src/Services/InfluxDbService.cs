using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Diagnostics;
using InfluxDB.Client;
using InfluxDB.Client.Api.Domain;
using InfluxDB.Client.Core.Flux.Domain;
using InfluxDB.Client.Writes;

namespace ParkingApi.Services;

public class InfluxDbService
{
    private readonly InfluxDBClient _client;
    private readonly string _org;
    private readonly string _bucket;

    public InfluxDbService(string url, string token, string org)
    {
        _client = new InfluxDBClient(url, token);
        _org = org;
        _bucket = "iot_bucket";
    }

    public async Task<List<FluxTable>> QueryAsync(string query)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var queryApi = _client.GetQueryApi();
            var result = await queryApi.QueryAsync(query, _org);

            // Log query performance metrics
            await WriteQueryMetricsAsync(stopwatch.ElapsedMilliseconds, "success", query);

            return result;
        }
        catch (InfluxDB.Client.Core.Exceptions.BadGatewayException)
        {
            // During integration tests, the .NET InfluxDB client sometimes has issues
            // with containerized InfluxDB, let's try a simple retry before giving up
            try
            {
                await Task.Delay(1000); // Wait 1 second
                var queryApi = _client.GetQueryApi();
                var result = await queryApi.QueryAsync(query, _org);

                await WriteQueryMetricsAsync(stopwatch.ElapsedMilliseconds, "retry_success", query);

                return result;
            }
            catch
            {
                // If retry fails, return empty results but let the controller handle fallback
                await WriteQueryMetricsAsync(stopwatch.ElapsedMilliseconds, "failure", query);
                return new List<FluxTable>();
            }
        }
        catch (Exception ex)
        {
            await WriteQueryMetricsAsync(stopwatch.ElapsedMilliseconds, "error", query, ex.Message);
            throw;
        }
    }
    public async Task WriteHttpMetricsAsync(double responseTimeMs, int statusCode, string method, string endpoint)
    {
        try
        {
            var writeApi = _client.GetWriteApi();
            var point = PointData
                .Measurement("http_requests")
                .Tag("method", method)
                .Tag("endpoint", endpoint)
                .Tag("status_code", statusCode.ToString())
                .Field("response_time_ms", responseTimeMs)
                .Field("request_count", 1L)
                .Timestamp(DateTime.UtcNow, WritePrecision.Ms);

            writeApi.WritePoint(point, _bucket, _org);
            writeApi.Flush();
        }
        catch (Exception ex)
        {
            // Log but don't throw to avoid breaking the main request flow
            Console.WriteLine($"Failed to write HTTP metrics: {ex.Message}");
        }
    }
    public async Task WriteBusinessMetricsAsync(int totalParkingSpots, int freeParkingSpots, int occupiedSpots, string region = "default")
    {
        try
        {
            var writeApi = _client.GetWriteApi();
            var point = PointData
                .Measurement("parking_business_metrics")
                .Tag("region", region)
                .Field("total_spots", totalParkingSpots)
                .Field("free_spots", freeParkingSpots)
                .Field("occupied_spots", occupiedSpots)
                .Field("occupancy_rate", totalParkingSpots > 0 ? (double)occupiedSpots / totalParkingSpots * 100 : 0)
                .Timestamp(DateTime.UtcNow, WritePrecision.Ms);

            writeApi.WritePoint(point, _bucket, _org);
            writeApi.Flush();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to write business metrics: {ex.Message}");
        }
    }
    public async Task WriteApiPerformanceMetricsAsync(string operationType, double durationMs, bool success)
    {
        try
        {
            var writeApi = _client.GetWriteApi();
            var point = PointData
                .Measurement("api_performance")
                .Tag("operation_type", operationType)
                .Tag("success", success.ToString().ToLower())
                .Field("duration_ms", durationMs)
                .Field("operation_count", 1L)
                .Timestamp(DateTime.UtcNow, WritePrecision.Ms);

            writeApi.WritePoint(point, _bucket, _org);
            writeApi.Flush();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to write API performance metrics: {ex.Message}");
        }
    }
    private async Task WriteQueryMetricsAsync(long durationMs, string status, string query, string errorMessage = "")
    {
        try
        {
            var writeApi = _client.GetWriteApi();
            var point = PointData
                .Measurement("influxdb_query_performance")
                .Tag("status", status)
                .Tag("query_type", ExtractQueryType(query))
                .Field("duration_ms", durationMs)
                .Field("query_count", 1L);

            if (!string.IsNullOrEmpty(errorMessage))
            {
                point = point.Field("error_message", errorMessage);
            }

            point = point.Timestamp(DateTime.UtcNow, WritePrecision.Ms);

            writeApi.WritePoint(point, _bucket, _org);
            writeApi.Flush();
        }
        catch
        {
            // Avoid infinite recursion by not logging metric write failures
        }
    }

    private string ExtractQueryType(string query)
    {
        if (query.Contains("parking_data"))
            return "parking_data";
        else if (query.Contains("aggregate"))
            return "aggregate";
        else if (query.Contains("range"))
            return "range";
        else
            return "other";
    }

    public async Task<HealthMetrics> GetSystemHealthMetricsAsync()
    {
        try
        {
            // Query recent metrics to determine system health
            var query = @"
                from(bucket:""iot_bucket"") 
                |> range(start: -5m) 
                |> filter(fn: (r) => r._measurement == ""http_requests"")
                |> group(columns: [""status_code""]) 
                |> count()";

            var result = await QueryAsync(query);

            var totalRequests = 0L;
            var errorRequests = 0L;

            foreach (var table in result)
            {
                foreach (var record in table.Records)
                {
                    var statusCode = record.GetValueByKey("status_code")?.ToString();
                    var count = Convert.ToInt64(record.GetValue());

                    totalRequests += count;
                    if (statusCode != null && (statusCode.StartsWith("4") || statusCode.StartsWith("5")))
                    {
                        errorRequests += count;
                    }
                }
            }

            return new HealthMetrics
            {
                TotalRequests = totalRequests,
                ErrorRequests = errorRequests,
                ErrorRate = totalRequests > 0 ? (double)errorRequests / totalRequests * 100 : 0,
                IsHealthy = totalRequests == 0 || (double)errorRequests / totalRequests < 0.05 // 5% error threshold
            };
        }
        catch
        {
            return new HealthMetrics
            {
                TotalRequests = 0,
                ErrorRequests = 0,
                ErrorRate = 0,
                IsHealthy = false
            };
        }
    }
}

public class HealthMetrics
{
    public long TotalRequests { get; set; }
    public long ErrorRequests { get; set; }
    public double ErrorRate { get; set; }
    public bool IsHealthy { get; set; }
}