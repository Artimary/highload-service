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

                    // Улучшенный запрос к InfluxDB, который получает последние данные для каждого устройства
                    string query = @"from(bucket:""iot_bucket"") 
                                    |> range(start: -1h) 
                                    |> filter(fn: (r) => r._measurement == ""parking_data"")
                                    |> filter(fn: (r) => r._field == ""free_spots"")
                                    |> group(columns: [""device_id""])
                                    |> last()";

                    var result = await _influxDbService.QueryAsync(query);
                    var fetchedParkingLots = new List<ParkingStatus>();

                    foreach (var table in result)
                    {
                        _logger.LogInformation("Processing InfluxDB table with {RecordCount} records", table.Records.Count);

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

                    // Если нет данных из InfluxDB, расширяем временной диапазон до 24 часов
                    if (fetchedParkingLots.Count == 0)
                    {
                        _logger.LogWarning("No data found in InfluxDB for the last hour, trying with extended time range.");
                        query = @"from(bucket:""iot_bucket"") 
                                |> range(start: -24h) 
                                |> filter(fn: (r) => r._measurement == ""parking_data"")
                                |> filter(fn: (r) => r._field == ""free_spots"")
                                |> group(columns: [""device_id""])
                                |> last()";

                        result = await _influxDbService.QueryAsync(query);

                        foreach (var table in result)
                        {
                            _logger.LogInformation("Extended time: Processing InfluxDB table with {RecordCount} records", table.Records.Count);

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
                    }

                    // Если до сих пор нет данных, запрашиваем все данные из PostgreSQL
                    if (fetchedParkingLots.Count == 0)
                    {
                        _logger.LogWarning("No data found in InfluxDB, loading parking lot data from PostgreSQL");

                        // Изменено: используем GetParkingLotsAsync вместо GetAllParkingLotsAsync
                        var allParkingLots = await _postgresService.GetParkingLotsAsync();

                        foreach (var lot in allParkingLots)
                        {
                            fetchedParkingLots.Add(new ParkingStatus
                            {
                                Id = lot.Id,
                                FreeSpots = lot.Capacity / 2, // Примерное значение, половина от емкости
                                Lat = lot.Latitude,           // Изменено: используем Latitude вместо Lat
                                Lon = lot.Longitude           // Изменено: используем Longitude вместо Lon
                            });
                        }

                        _logger.LogInformation("Loaded {Count} parking lots from PostgreSQL as fallback", allParkingLots.Count);
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
            // Исправление: проверка имени свойства в BookingRequest
            // Если в запросе используется "spotnumber" вместо "spotNumber",
            // нужно модифицировать класс модели
            if (string.IsNullOrEmpty(booking.VehicleId))
            {
                return BadRequest("Vehicle ID is required");
            }

            if (booking.SpotNumber <= 0)
            {
                return BadRequest("Invalid spot number");
            }

            _logger.LogInformation($"Booking spot {booking.SpotNumber} at parking {parkingId} for vehicle {booking.VehicleId}");

            // Проверяем, не занято ли уже это место
            bool isBooked = await _postgresService.IsSpotBookedAsync(parkingId, booking.SpotNumber);
            if (isBooked)
            {
                return Conflict($"Spot {booking.SpotNumber} at parking {parkingId} is already booked");
            }

            // Бронируем место
            int bookingId = await _postgresService.BookSpotAsync(booking.VehicleId, parkingId, booking.SpotNumber);

            // Очищаем кэш статуса парковок
            await _cacheService.RemoveAsync($"parking:status");
            await _cacheService.RemoveAsync($"parking:status:geo:*");

            return Ok(new { BookingId = bookingId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error booking spot");
            return StatusCode(500, $"Error booking spot: {ex.Message}");
        }
    }

    [HttpGet("bookings")]
    public async Task<ActionResult<IEnumerable<BookingInfo>>> GetActiveBookings([FromQuery] int? parkingId = null)
    {
        try
        {
            var bookings = await _postgresService.GetAllActiveBookingsAsync(parkingId);

            // Enrich the response with parking location data
            var enrichedBookings = new List<object>();

            foreach (var booking in bookings)
            {
                var location = await _postgresService.GetParkingLotLocationAsync(booking.ParkingId);

                enrichedBookings.Add(new
                {
                    booking.BookingId,
                    booking.VehicleId,
                    booking.ParkingId,
                    booking.SpotNumber,
                    booking.BookingTime,
                    booking.Active,
                    Location = location.HasValue ? new { Lat = location.Value.lat, Lon = location.Value.lon } : null
                });
            }

            return Ok(enrichedBookings);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving active bookings");
            return StatusCode(500, $"Error retrieving active bookings: {ex.Message}");
        }
    }

    [HttpDelete("bookings/all")]
    public async Task<ActionResult> DeleteAllBookings([FromQuery] string confirmationCode)
    {
        try
        {
            // Проверка кода подтверждения для защиты от случайного удаления
            if (confirmationCode != "DELETE_ALL_CONFIRM")
            {
                return BadRequest("Incorrect confirmation code. Use 'DELETE_ALL_CONFIRM' to confirm this dangerous operation.");
            }

            int deletedCount = await _postgresService.ClearAllBookingsAsync();

            // Очищаем кэш статуса парковок
            await _cacheService.RemoveAsync($"parking:status");
            await _cacheService.RemoveAsync($"parking:status:geo:*");

            return Ok(new
            {
                Message = "All bookings have been deleted",
                DeletedCount = deletedCount
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting all bookings");
            return StatusCode(500, $"Error deleting all bookings: {ex.Message}");
        }
    }

    [HttpGet("{parkingId}/route")]
    public async Task<ActionResult> GetRoute(int parkingId)
    {
        try
        {
            // Получаем координаты парковки
            var location = await _postgresService.GetParkingLotLocationAsync(parkingId);
            if (!location.HasValue)
            {
                return NotFound($"Parking lot with id {parkingId} not found");
            }

            // В реальном приложении здесь был бы запрос к картографическому сервису
            // Для примера просто возвращаем координаты и заглушку для маршрута
            return Ok(new
            {
                ParkingId = parkingId,
                Destination = new { Lat = location.Value.lat, Lon = location.Value.lon },
                RoutePoints = new[]
                {
                    new { Lat = location.Value.lat - 0.01, Lon = location.Value.lon - 0.01 },
                    new { Lat = location.Value.lat - 0.005, Lon = location.Value.lon - 0.005 },
                    new { Lat = location.Value.lat, Lon = location.Value.lon }
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting route to parking {ParkingId}", parkingId);
            return StatusCode(500, $"Error getting route: {ex.Message}");
        }
    }

    [HttpDelete("booking/{bookingId}")]
    public async Task<ActionResult> DeleteBooking(int bookingId)
    {
        try
        {
            bool result = await _postgresService.DeleteBookingAsync(bookingId);
            if (result)
            {
                // Очищаем кэш статуса парковок
                await _cacheService.RemoveAsync($"parking:status");
                await _cacheService.RemoveAsync($"parking:status:geo:*");

                return Ok(new { Status = "Booking cancelled" });
            }
            else
            {
                return NotFound($"Booking with id {bookingId} not found or already inactive");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cancelling booking {BookingId}", bookingId);
            return StatusCode(500, $"Error cancelling booking: {ex.Message}");
        }
    }

    [HttpPut("booking/{bookingId}")]
    public async Task<ActionResult> UpdateBooking(int bookingId, [FromBody] BookingUpdateRequest request)
    {
        try
        {
            bool result = await _postgresService.UpdateBookingAsync(bookingId, request.VehicleId, request.Active);
            if (result)
            {
                // Очищаем кэш статуса парковок
                await _cacheService.RemoveAsync($"parking:status");
                await _cacheService.RemoveAsync($"parking:status:geo:*");

                return Ok(new { Status = "Booking updated" });
            }
            else
            {
                return NotFound($"Booking with id {bookingId} not found");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating booking {BookingId}", bookingId);
            return StatusCode(500, $"Error updating booking: {ex.Message}");
        }
    }

    [HttpGet("spot/{parkingId}/{spotNumber}")]
    public async Task<ActionResult> GetParkingSpotDetails(int parkingId, int spotNumber)
    {
        try
        {
            var details = await _postgresService.GetParkingSpotDetailsAsync(parkingId, spotNumber);
            if (details != null)
            {
                return Ok(details);
            }
            else
            {
                return NotFound($"Spot {spotNumber} at parking {parkingId} not found");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting parking spot details for spot {SpotNumber} at parking {ParkingId}", spotNumber, parkingId);
            return StatusCode(500, $"Error getting parking spot details: {ex.Message}");
        }
    }

    [HttpGet("booking/{bookingId}")]
    public async Task<ActionResult> GetBookingDetails(int bookingId)
    {
        try
        {
            var bookingInfo = await _postgresService.GetBookingInfoAsync(bookingId);
            if (bookingInfo != null)
            {
                return Ok(bookingInfo);
            }
            else
            {
                return NotFound($"Booking with id {bookingId} not found");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting booking details for booking {BookingId}", bookingId);
            return StatusCode(500, $"Error getting booking details: {ex.Message}");
        }
    }

    [HttpPost("bookall")]
    public async Task<ActionResult> BookAllSpots([FromBody] BookAllRequest request, [FromQuery] int? parkingId = null)
    {
        try
        {
            if (string.IsNullOrEmpty(request.VehicleId))
            {
                return BadRequest("Vehicle ID is required");
            }

            // Проверка кода подтверждения для защиты от случайного бронирования
            if (request.ConfirmationCode != "BOOK_ALL_CONFIRM")
            {
                return BadRequest("Incorrect confirmation code. Use 'BOOK_ALL_CONFIRM' to confirm this operation.");
            }

            string parkingScope = parkingId.HasValue ? $"for parking {parkingId}" : "across all parking lots";
            _logger.LogWarning($"Request to book ALL available spots {parkingScope} for vehicle {request.VehicleId}");

            int bookedCount = await _postgresService.BookAllAvailableSpotsAsync(request.VehicleId, parkingId);

            // Очищаем кэш статуса парковок
            await _cacheService.RemoveAsync($"parking:status");
            await _cacheService.RemoveAsync($"parking:status:geo:*");

            return Ok(new
            {
                Message = $"Booking process completed for all available spots {parkingScope}",
                BookedCount = bookedCount
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error booking all spots");
            return StatusCode(500, $"Error booking all spots: {ex.Message}");
        }
    }
}