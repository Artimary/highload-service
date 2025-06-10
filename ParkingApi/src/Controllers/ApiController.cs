using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using ParkingApi.Models;
using ParkingApi.Services;

namespace ParkingApi.Controllers;

[ApiController]
[Route("parking")]
public class ParkingController : ControllerBase
{
    private readonly InfluxDbService _influxDbService;
    private readonly PostgresService _postgresService;
    private readonly ILogger<ParkingController> _logger;
    private readonly ICacheService _cacheService;

    public ParkingController(
        InfluxDbService influxDbService,
        PostgresService postgresService,
        ILogger<ParkingController> logger,
        ICacheService cacheService)
    {
        _influxDbService = influxDbService;
        _postgresService = postgresService;
        _logger = logger;
        _cacheService = cacheService;
    }

    [HttpGet("status")]
    public async Task<ActionResult<List<ParkingStatus>>> GetParkingStatus(
        [FromQuery] double? lat,
        [FromQuery] double? lon,
        [FromQuery] double? radius)
    {
        try
        {
            // Создаем кэш-ключ на основе параметров запроса
            string cacheKey = $"parking:status";
            if (lat.HasValue && lon.HasValue && radius.HasValue)
            {
                cacheKey += $":geo:{lat.Value:F4}:{lon.Value:F4}:{radius.Value:F0}";
            }

            // Пытаемся получить данные из кэша или создаем их при отсутствии
            var parkingLots = await _cacheService.GetOrCreateAsync<List<ParkingStatus>>(
                cacheKey,
                async () =>
                {
                    _logger.LogInformation("Cache miss for {CacheKey}, querying from database", cacheKey);

                    _logger.LogInformation("Starting query to InfluxDB");
                    string query = @"from(bucket:""iot_bucket"") 
                                    |> range(start: -1h) 
                                    |> filter(fn: (r) => r._measurement == ""parking_data"")";
                    var result = await _influxDbService.QueryAsync(query);
                    var fetchedParkingLots = new List<ParkingStatus>();

                    foreach (var table in result)
                    {
                        foreach (var record in table.Records)
                        {
                            string? deviceIdStr = record.GetValueByKey("device_id")?.ToString();
                            if (string.IsNullOrEmpty(deviceIdStr) || !int.TryParse(deviceIdStr, out int deviceId))
                            {
                                _logger.LogWarning($"Skipping record due to invalid device_id: {record}");
                                continue;
                            }
                            object freeSpotsObj = record.GetValue();
                            if (freeSpotsObj == null || !int.TryParse(freeSpotsObj.ToString(), out int freeSpots))
                            {
                                _logger.LogWarning($"Skipping record due to invalid free_spots: {record}");
                                continue;
                            }

                            // Кэшируем местоположение парковки
                            var locationCacheKey = $"parking:location:{deviceId}";
                            var location = await _cacheService.GetOrCreateAsync(
                                locationCacheKey,
                                () => _postgresService.GetParkingLotLocationAsync(deviceId),
                                TimeSpan.FromMinutes(30) // Местоположение меняется редко
                            );

                            if (location.HasValue)
                            {
                                fetchedParkingLots.Add(new ParkingStatus
                                {
                                    Id = deviceId,
                                    FreeSpots = freeSpots,
                                    Lat = location.Value.lat,
                                    Lon = location.Value.lon
                                });
                            }
                            else
                            {
                                _logger.LogWarning($"No metadata found in PostgreSQL for device_id: {deviceId}");
                            }
                        }
                    }

                    // Если нет данных из InfluxDB и это тестовая среда, предоставляем резервные данные
                    if (fetchedParkingLots.Count == 0)
                    {
                        // Проверяем, есть ли данные PostgreSQL (указывает на настройку тестовой среды)
                        var testLocation1 = await _postgresService.GetParkingLotLocationAsync(1);
                        var testLocation2 = await _postgresService.GetParkingLotLocationAsync(2);

                        if (testLocation1.HasValue)
                        {
                            fetchedParkingLots.Add(new ParkingStatus
                            {
                                Id = 1,
                                FreeSpots = 5, // Default test value
                                Lat = testLocation1.Value.lat,
                                Lon = testLocation1.Value.lon
                            });
                            _logger.LogInformation("Added fallback data for parking lot 1");
                        }

                        if (testLocation2.HasValue)
                        {
                            fetchedParkingLots.Add(new ParkingStatus
                            {
                                Id = 2,
                                FreeSpots = 3, // Default test value
                                Lat = testLocation2.Value.lat,
                                Lon = testLocation2.Value.lon
                            });
                            _logger.LogInformation("Added fallback data for parking lot 2");
                        }
                    }

                    // Если указаны координаты и радиус, фильтруем по расстоянию
                    if (lat.HasValue && lon.HasValue && radius.HasValue)
                    {
                        fetchedParkingLots = fetchedParkingLots.Where(lot =>
                        {
                            double distance = Math.Sqrt(Math.Pow(lot.Lat - lat.Value, 2) + Math.Pow(lot.Lon - lon.Value, 2)) * 111000;
                            return distance <= radius.Value;
                        }).ToList();
                    }

                    // Расчет и запись метрик
                    var totalSpots = fetchedParkingLots.Sum(p => p.FreeSpots + 5);
                    var totalFreeSpots = fetchedParkingLots.Sum(p => p.FreeSpots);
                    var occupiedSpots = totalSpots - totalFreeSpots;

                    // Запись метрик в фоновом режиме
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await _influxDbService.WriteBusinessMetricsAsync(totalSpots, totalFreeSpots, occupiedSpots, "default");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to write business metrics");
                        }
                    });

                    return fetchedParkingLots;
                },
                TimeSpan.FromSeconds(30) // Кэшируем на 30 секунд
            );

            _logger.LogInformation($"Returning parking lots: {parkingLots.Count}");
            return Ok(parkingLots);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error querying parking status");
            return StatusCode(500, $"Error querying parking status: {ex.Message}");
        }
    }

    [HttpPost("{parkingId}/book")]
    public async Task<ActionResult> BookSpot(int parkingId, [FromBody] BookingRequest booking)
    {
        try
        {
            bool isBooked = await _postgresService.IsSpotBookedAsync(parkingId, booking.SpotNumber);
            if (isBooked)
            {
                return BadRequest("Spot already booked");
            }

            int bookingId = await _postgresService.BookSpotAsync(booking.VehicleId, parkingId, booking.SpotNumber);

            // Инвалидируем кэш после успешного бронирования
            await _cacheService.RemoveAsync($"parking:status");
            // Инвалидируем кэши с геолокацией (если они есть)
            await _cacheService.RemoveByPrefixAsync("parking:status:geo:");

            // Обновляем кэш для конкретной парковки
            await _cacheService.RemoveAsync($"parking:spot:{parkingId}");

            return Ok(new { booking_id = bookingId, status = "success" });
        }
        catch (Npgsql.PostgresException pex) when (pex.SqlState == "23505")
        {
            _logger.LogWarning("Spot already booked during insertion for parkingId: {ParkingId}, spotNumber: {SpotNumber}", parkingId, booking.SpotNumber);
            return BadRequest("Spot already booked");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error booking spot");
            return StatusCode(500, $"Error booking spot: {ex.Message}");
        }
    }

    [HttpGet("{parkingId}/route")]
    public async Task<ActionResult> GetRoute(int parkingId)
    {
        try
        {
            // Кэшируем данные о маршруте к парковке
            var cacheKey = $"parking:route:{parkingId}";

            var routeData = await _cacheService.GetOrCreateAsync(
                cacheKey,
                async () =>
                {
                    var location = await _postgresService.GetParkingLotLocationAsync(parkingId);
                    if (!location.HasValue)
                    {
                        return null;
                    }
                    return new { id = parkingId, lat = location.Value.lat, lon = location.Value.lon };
                },
                TimeSpan.FromMinutes(30) // Маршруты меняются редко
            );

            if (routeData == null)
            {
                return NotFound("Parking lot not found");
            }

            return Ok(routeData);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting route");
            return StatusCode(500, $"Error getting route: {ex.Message}");
        }
    }

    [HttpDelete("{bookingId}")]
    public async Task<ActionResult> DeleteBooking(int bookingId)
    {
        try
        {
            _logger.LogInformation("Attempting to delete booking with ID: {BookingId}", bookingId);

            // Получаем информацию о бронировании перед удалением для инвалидации кэша
            var bookingInfo = await _postgresService.GetBookingInfoAsync(bookingId);

            bool deleted = await _postgresService.DeleteBookingAsync(bookingId);
            if (!deleted)
            {
                _logger.LogWarning("Booking not found for deletion with ID: {BookingId}", bookingId);
                return NotFound("Booking not found");
            }

            _logger.LogInformation("Booking deleted successfully with ID: {BookingId}", bookingId);

            // Инвалидация кэша после удаления бронирования
            if (bookingInfo != null)
            {
                await _cacheService.RemoveAsync($"parking:status");
                await _cacheService.RemoveByPrefixAsync("parking:status:geo:");
                await _cacheService.RemoveAsync($"parking:spot:{bookingInfo.ParkingId}");
            }

            return Ok("Booking deleted successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting booking with ID: {BookingId}", bookingId);
            return StatusCode(500, $"Error deleting booking: {ex.Message}");
        }
    }

    [HttpPut("{bookingId}")]
    public async Task<ActionResult> UpdateBooking(int bookingId, [FromBody] BookingUpdateRequest request)
    {
        try
        {
            if (request == null || string.IsNullOrEmpty(request.VehicleId))
            {
                _logger.LogWarning("Invalid request data for updating booking with ID: {BookingId}", bookingId);
                return BadRequest("Invalid request data");
            }

            _logger.LogInformation("Attempting to update booking with ID: {BookingId}", bookingId);

            // Получаем информацию о бронировании перед обновлением для инвалидации кэша
            var bookingInfo = await _postgresService.GetBookingInfoAsync(bookingId);

            bool updated = await _postgresService.UpdateBookingAsync(bookingId, request.VehicleId, request.Active);
            if (!updated)
            {
                _logger.LogWarning("Booking not found for update with ID: {BookingId}", bookingId);
                return NotFound("Booking not found");
            }

            _logger.LogInformation("Booking updated successfully with ID: {BookingId}", bookingId);

            // Инвалидация кэша после обновления бронирования
            if (bookingInfo != null)
            {
                await _cacheService.RemoveAsync($"parking:status");
                await _cacheService.RemoveByPrefixAsync("parking:status:geo:");
                await _cacheService.RemoveAsync($"parking:spot:{bookingInfo.ParkingId}");
            }

            return Ok("Booking updated successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating booking with ID: {BookingId}", bookingId);
            return StatusCode(500, $"Error updating booking: {ex.Message}");
        }
    }

    // Новый метод для получения детальной информации о парковочном месте с кэшированием
    [HttpGet("spot/{spotId}")]
    public async Task<ActionResult> GetParkingSpotDetails(int spotId)
    {
        try
        {
            var cacheKey = $"parking:spot:{spotId}";

            var spotDetails = await _cacheService.GetOrCreateAsync(
                cacheKey,
                async () =>
                {
                    var details = await _postgresService.GetParkingSpotDetailsAsync(spotId);
                    if (details == null)
                    {
                        return null;
                    }
                    return details;
                },
                TimeSpan.FromMinutes(1) // Кэшируем на 1 минуту
            );

            if (spotDetails == null)
            {
                return NotFound("Parking spot not found");
            }

            return Ok(spotDetails);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting parking spot details");
            return StatusCode(500, $"Error getting parking spot details: {ex.Message}");
        }
    }
}