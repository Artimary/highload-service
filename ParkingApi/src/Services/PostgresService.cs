using System.Threading.Tasks;
using System.Collections.Generic;
using System;
using System.Linq;
using Dapper;
using Npgsql;
using ParkingApi.Models;
using Microsoft.Extensions.Logging;

namespace ParkingApi.Services;

public class PostgresService
{
    private readonly string _masterConnectionString;      // Для записи и общих данных
    private readonly string _replicaConnectionString;     // Для операций чтения
    private readonly string _parkingSpotsShardString;     // Шард с парковочными местами
    private readonly string _bookingsShardString;         // Шард с бронированиями
    private readonly ILogger<PostgresService>? _logger;

    public PostgresService(
        string masterConnectionString,
        string replicaConnectionString,
        string parkingSpotsShardString,
        string bookingsShardString,
        ILogger<PostgresService>? logger = null)
    {
        // Исправление строк подключения, если они содержат пробелы вместо точки с запятой
        _masterConnectionString = NormalizeConnectionString(masterConnectionString);
        _replicaConnectionString = NormalizeConnectionString(replicaConnectionString);
        _parkingSpotsShardString = NormalizeConnectionString(parkingSpotsShardString);
        _bookingsShardString = NormalizeConnectionString(bookingsShardString);
        _logger = logger;

        _logger?.LogInformation("PostgresService initialized with sharded architecture");
        _logger?.LogDebug($"Master connection: {TruncateConnectionString(_masterConnectionString)}");
        _logger?.LogDebug($"Replica connection: {TruncateConnectionString(_replicaConnectionString)}");
        _logger?.LogDebug($"Spots shard connection: {TruncateConnectionString(_parkingSpotsShardString)}");
        _logger?.LogDebug($"Bookings shard connection: {TruncateConnectionString(_bookingsShardString)}");

        // Инициализируем базы данных при создании сервиса
        Task.Run(() => EnsureDatabasesExistAsync()).Wait();
    }

    private string NormalizeConnectionString(string connectionString)
    {
        // Если строка содержит пробелы вместо точек с запятой, исправляем её
        if (connectionString?.Contains(" ") == true && !connectionString.Contains(";"))
        {
            return connectionString.Replace(" ", ";");
        }
        return connectionString ?? string.Empty;
    }

    private string TruncateConnectionString(string connectionString)
    {
        // Вывод первых 15 символов для логирования (безопасность)
        return connectionString?.Length > 20 ?
            connectionString.Substring(0, 15) + "..." :
            connectionString;
    }

    // Инициализация баз данных
    private async Task EnsureDatabasesExistAsync()
    {
        try
        {
            // 1. Инициализация шарда с парковками
            await InitializeParkingSpotsShardAsync();

            // 2. Инициализация шарда с бронированиями
            await InitializeBookingsShardAsync();

            _logger?.LogInformation("All database schemas successfully initialized");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error initializing database schemas");
        }
    }

    private async Task InitializeParkingSpotsShardAsync()
    {
        try
        {
            using var connection = new NpgsqlConnection(_parkingSpotsShardString);
            await connection.OpenAsync();

            // Создание таблицы парковок
            const string createParkingLotsTable = @"
                CREATE TABLE IF NOT EXISTS parking_lots (
                    parking_id SERIAL PRIMARY KEY,
                    name VARCHAR(255) NOT NULL,
                    address VARCHAR(255) NOT NULL DEFAULT '',
                    capacity INTEGER NOT NULL,
                    latitude DECIMAL(10,8) NOT NULL,
                    longitude DECIMAL(11,8) NOT NULL,
                    hourly_rate DECIMAL(6,2) NOT NULL DEFAULT 0,
                    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    updated_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
                )";

            await connection.ExecuteAsync(createParkingLotsTable);

            // Создание таблицы мест на парковках
            const string createParkingSpotsTable = @"
                CREATE TABLE IF NOT EXISTS parking_spots (
                    id SERIAL PRIMARY KEY,
                    parking_id INTEGER NOT NULL REFERENCES parking_lots(parking_id),
                    spot_number INTEGER NOT NULL,
                    status VARCHAR(20) NOT NULL DEFAULT 'available',
                    updated_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    UNIQUE(parking_id, spot_number)
                )";

            await connection.ExecuteAsync(createParkingSpotsTable);

            // Проверяем наличие данных и добавляем тестовые данные, если таблица пуста
            long count = await connection.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM parking_lots");
            if (count == 0)
            {
                _logger?.LogInformation("No parking lots found, adding test data");
                await SeedParkingDataAsync(connection);
            }

            _logger?.LogInformation("Parking spots shard initialized successfully");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error initializing parking spots shard");
            throw;
        }
    }

    private async Task InitializeBookingsShardAsync()
    {
        try
        {
            using var connection = new NpgsqlConnection(_bookingsShardString);
            await connection.OpenAsync();

            // Создание таблицы бронирований
            const string createBookingsTable = @"
                CREATE TABLE IF NOT EXISTS bookings (
                    id SERIAL PRIMARY KEY,
                    vehicle_id VARCHAR(50) NOT NULL,
                    parking_id INTEGER NOT NULL,
                    spot_number INTEGER NOT NULL,
                    booking_time TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    active BOOLEAN NOT NULL DEFAULT TRUE,
                    UNIQUE(parking_id, spot_number, active) WHERE active = TRUE
                )";

            await connection.ExecuteAsync(createBookingsTable);

            // Создание таблицы истории бронирований с партиционированием по месяцам
            const string createBookingHistoryTable = @"
                CREATE TABLE IF NOT EXISTS booking_history (
                    id SERIAL PRIMARY KEY,
                    vehicle_id VARCHAR(50) NOT NULL,
                    parking_id INTEGER NOT NULL,
                    spot_number INTEGER NOT NULL,
                    booking_time TIMESTAMP NOT NULL,
                    end_time TIMESTAMP NOT NULL,
                    status VARCHAR(20) NOT NULL
                )";

            await connection.ExecuteAsync(createBookingHistoryTable);

            _logger?.LogInformation("Bookings shard initialized successfully");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error initializing bookings shard");
            throw;
        }
    }

    private async Task SeedParkingDataAsync(NpgsqlConnection connection)
    {
        try
        {
            // Основные координаты центра Санкт-Петербурга
            double baseLat = 59.9343;
            double baseLon = 30.3351;
            Random rand = new Random();

            // Добавляем 150 парковок
            for (int i = 1; i <= 150; i++)
            {
                // Генерируем случайное смещение от центра города
                double latOffset = (rand.NextDouble() - 0.5) * 0.1;
                double lonOffset = (rand.NextDouble() - 0.5) * 0.1;
                double lat = baseLat + latOffset;
                double lon = baseLon + lonOffset;

                int capacity = rand.Next(20, 350);
                decimal hourlyRate = rand.Next(50, 200) / 10.0m;

                const string insertParkingLotSql = @"
                    INSERT INTO parking_lots 
                    (parking_id, name, address, capacity, latitude, longitude, hourly_rate) 
                    VALUES 
                    (@id, @name, @address, @capacity, @lat, @lon, @hourlyRate)
                    ON CONFLICT (parking_id) DO UPDATE SET
                    name = @name, address = @address, capacity = @capacity, 
                    latitude = @lat, longitude = @lon, hourly_rate = @hourlyRate";

                await connection.ExecuteAsync(insertParkingLotSql, new
                {
                    id = i,
                    name = $"Парковка {i}",
                    address = $"Санкт-Петербург, Тестовый адрес {i}",
                    capacity,
                    lat,
                    lon,
                    hourlyRate
                });

                // Добавляем места для этой парковки
                for (int spot = 1; spot <= capacity; spot++)
                {
                    const string insertSpotSql = @"
                        INSERT INTO parking_spots 
                        (parking_id, spot_number, status) 
                        VALUES 
                        (@parkingId, @spotNum, @status)
                        ON CONFLICT (parking_id, spot_number) DO UPDATE SET
                        status = @status";

                    // 95% мест свободны, 5% заняты
                    string status = rand.Next(100) < 95 ? "available" : "occupied";

                    await connection.ExecuteAsync(insertSpotSql, new
                    {
                        parkingId = i,
                        spotNum = spot,
                        status
                    });
                }
            }

            _logger?.LogInformation("Successfully seeded 150 parking lots with spots");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error seeding parking data");
            throw;
        }
    }

    // ПОЛУЧЕНИЕ СПИСКА ПАРКОВОК - из шарда parking_spots
    public async Task<List<ParkingLot>> GetParkingLotsAsync()
    {
        _logger?.LogDebug("Getting all parking lots from spots shard");
        try
        {
            using var connection = new NpgsqlConnection(_parkingSpotsShardString);
            await connection.OpenAsync();

            const string sql = @"
                SELECT 
                    parking_id as Id, 
                    name as Name, 
                    address as Address, 
                    capacity as Capacity, 
                    latitude as Latitude, 
                    longitude as Longitude, 
                    hourly_rate as HourlyRate 
                FROM parking_lots";

            var parkingLots = await connection.QueryAsync<ParkingLot>(sql);

            var result = parkingLots.ToList();
            _logger?.LogInformation($"Retrieved {result.Count} parking lots from spots shard");
            return result;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error retrieving parking lots from spots shard");
            throw;
        }
    }

    // ПОЛУЧЕНИЕ ЛОКАЦИИ ПАРКОВКИ - из шарда parking_spots
    public async Task<(double lat, double lon)?> GetParkingLotLocationAsync(int id)
    {
        _logger?.LogDebug($"Getting location for parking lot {id} from spots shard");
        try
        {
            using var conn = new NpgsqlConnection(_parkingSpotsShardString);
            await conn.OpenAsync();

            using var cmd = new NpgsqlCommand("SELECT latitude as lat, longitude as lon FROM parking_lots WHERE parking_id = @id", conn);
            cmd.Parameters.AddWithValue("id", id);

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                var lat = reader.GetDouble(0);
                var lon = reader.GetDouble(1);
                _logger?.LogDebug($"Found location ({lat}, {lon}) for parking lot {id}");
                return (lat, lon);
            }

            _logger?.LogWarning($"No location found for parking lot {id} in spots shard");
            return null;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, $"Error getting location for parking lot {id}");
            throw;
        }
    }

    private async Task EnsureBookingsTableExistsAsync()
    {
        using var connection = new NpgsqlConnection(_bookingsShardString);
        await connection.OpenAsync();

        // Начинаем транзакцию для атомарности операций
        using var transaction = await connection.BeginTransactionAsync();

        try
        {
            // Проверяем существование таблицы
            var tableExists = await connection.ExecuteScalarAsync<bool>(
                "SELECT EXISTS (SELECT FROM information_schema.tables WHERE table_name = 'bookings')");

            if (tableExists)
            {
                // Если таблица существует, проверяем структуру
                var columns = await connection.QueryAsync<string>(
                    "SELECT column_name FROM information_schema.columns WHERE table_name = 'bookings'");

                var columnsList = columns.ToList();
                _logger?.LogDebug($"Existing bookings table columns: {string.Join(", ", columnsList)}");

                // Если нет нужной колонки, пересоздаем таблицу
                if (!columnsList.Contains("parking_id", StringComparer.OrdinalIgnoreCase))
                {
                    _logger?.LogWarning("Bookings table exists but missing required columns. Recreating...");
                    await EnsureBookingsTableStructureAsync(connection);
                }
            }
            else
            {
                // Создаем таблицу если её нет
                _logger?.LogInformation("Creating bookings table with correct structure");
                await EnsureBookingsTableStructureAsync(connection);
            }

            // Фиксируем транзакцию
            await transaction.CommitAsync();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error ensuring bookings table exists");
            await transaction.RollbackAsync();
            throw;
        }
    }

    // ПРОВЕРКА ЗАНЯТО ЛИ МЕСТО - из шарда parking_bookings
    public async Task<bool> IsSpotBookedAsync(int parkingId, int spotNumber)
    {
        _logger?.LogDebug($"Checking if spot {spotNumber} at parking {parkingId} is booked");
        try
        {
            // 1. Сначала проверяем/создаем таблицу и ожидаем завершения
            await EnsureBookingsTableExistsAsync();

            // 2. Только ПОСЛЕ этого выполняем запрос
            using var conn = new NpgsqlConnection(_bookingsShardString);
            await conn.OpenAsync();

            // Проверка структуры таблицы непосредственно перед запросом
            var columns = await conn.QueryAsync<string>(
                "SELECT column_name FROM information_schema.columns WHERE table_name = 'bookings'");

            if (!columns.Any(c => c.Equals("parking_id", StringComparison.OrdinalIgnoreCase)))
            {
                _logger?.LogWarning("Bookings table exists but missing parking_id column, recreating...");
                await EnsureBookingsTableStructureAsync(conn);
            }

            using var cmd = new NpgsqlCommand(
                "SELECT EXISTS(SELECT 1 FROM bookings WHERE parking_id = @parkingId AND spot_number = @spotNumber AND active = true)",
                conn);
            cmd.Parameters.AddWithValue("parkingId", parkingId);
            cmd.Parameters.AddWithValue("spotNumber", spotNumber);

            var result = await cmd.ExecuteScalarAsync();
            bool isBooked = result != null && Convert.ToBoolean(result);

            _logger?.LogDebug($"Spot {spotNumber} at parking {parkingId} is {(isBooked ? "booked" : "not booked")}");
            return isBooked;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, $"Error checking if spot {spotNumber} at parking {parkingId} is booked");
            throw;
        }
    }

    private async Task EnsureBookingsTableStructureAsync(NpgsqlConnection conn)
    {
        // Удаляем и пересоздаем таблицу
        await conn.ExecuteAsync("DROP TABLE IF EXISTS bookings");

        await conn.ExecuteAsync(@"
        CREATE TABLE bookings (
            id SERIAL PRIMARY KEY,
            vehicle_id VARCHAR(50) NOT NULL,
            parking_id INTEGER NOT NULL,
            spot_number INTEGER NOT NULL,
            booking_time TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
            active BOOLEAN NOT NULL DEFAULT TRUE
        )");

        await conn.ExecuteAsync(@"
        CREATE UNIQUE INDEX IF NOT EXISTS active_booking_idx 
        ON bookings (parking_id, spot_number) 
        WHERE active = TRUE");

        _logger?.LogInformation("Bookings table recreated with correct structure");
    }

    // БРОНИРОВАНИЕ МЕСТА - запись в шард parking_bookings + обновление статуса в шарде parking_spots
    public async Task<int> BookSpotAsync(string vehicleId, int parkingId, int spotNumber)
    {
        _logger?.LogInformation($"Booking spot {spotNumber} at parking {parkingId} for vehicle {vehicleId}");

        try
        {
            // 1. Сначала создаем/проверяем таблицу - ожидаем завершения
            await EnsureBookingsTableExistsAsync();

            // 2. Проверяем, не забронировано ли уже место
            bool isBooked = await IsSpotBookedAsync(parkingId, spotNumber);
            if (isBooked)
            {
                _logger?.LogWarning($"Spot {spotNumber} at parking {parkingId} is already booked");
                throw new InvalidOperationException($"Spot {spotNumber} at parking {parkingId} is already booked");
            }

            // 3. Теперь бронируем место
            using var conn = new NpgsqlConnection(_bookingsShardString);
            await conn.OpenAsync();

            using var cmd = new NpgsqlCommand(
                "INSERT INTO bookings (vehicle_id, parking_id, spot_number, active) VALUES (@vehicleId, @parkingId, @spotNumber, true) RETURNING id",
                conn);
            cmd.Parameters.AddWithValue("vehicleId", vehicleId);
            cmd.Parameters.AddWithValue("parkingId", parkingId);
            cmd.Parameters.AddWithValue("spotNumber", spotNumber);

            var result = await cmd.ExecuteScalarAsync();
            int bookingId = Convert.ToInt32(result);

            // 4. Обновляем статус места
            await UpdateParkingSpotStatusAsync(parkingId, spotNumber, "occupied");

            _logger?.LogInformation($"Successfully booked spot {spotNumber} at parking {parkingId}, booking ID: {bookingId}");
            return bookingId;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, $"Error booking spot {spotNumber} at parking {parkingId}");
            throw;
        }
    }

    // Method to get all booked spots across all parking lots or for a specific parking lot
    public async Task<IEnumerable<BookingInfo>> GetAllActiveBookingsAsync(int? parkingId = null)
    {
        _logger?.LogDebug($"Getting all active bookings{(parkingId.HasValue ? $" for parking {parkingId}" : "")} (bookings shard)");

        try
        {
            using var conn = new NpgsqlConnection(_bookingsShardString);
            await conn.OpenAsync();

            string sql = @"
            SELECT 
                id as BookingId, 
                vehicle_id as VehicleId, 
                parking_id as ParkingId, 
                spot_number as SpotNumber, 
                booking_time as BookingTime,
                active as Active 
            FROM bookings 
            WHERE active = true";

            if (parkingId.HasValue)
            {
                sql += " AND parking_id = @parkingId";
                var bookings = await conn.QueryAsync<BookingInfo>(sql, new { parkingId = parkingId.Value });
                _logger?.LogInformation($"Found {bookings.Count()} active bookings for parking {parkingId}");
                return bookings;
            }
            else
            {
                var bookings = await conn.QueryAsync<BookingInfo>(sql);
                _logger?.LogInformation($"Found {bookings.Count()} active bookings across all parking lots");
                return bookings;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, $"Error getting active bookings{(parkingId.HasValue ? $" for parking {parkingId}" : "")}");
            throw;
        }
    }

    // Метод для удаления всех бронирований
    public async Task<int> ClearAllBookingsAsync()
    {
        _logger?.LogWarning("Deleting ALL bookings from the system");
        try
        {
            // Сначала получаем информацию о всех активных бронированиях
            // для последующего обновления статуса парковочных мест
            var activeBookings = await GetAllActiveBookingsAsync();

            // Удаляем все бронирования
            using var conn = new NpgsqlConnection(_bookingsShardString);
            await conn.OpenAsync();

            int deletedCount = await conn.ExecuteAsync("DELETE FROM bookings");

            // Обновляем статусы всех мест на "available"
            foreach (var booking in activeBookings)
            {
                await UpdateParkingSpotStatusAsync(booking.ParkingId, booking.SpotNumber, "available");
            }

            _logger?.LogWarning($"Successfully deleted {deletedCount} bookings");
            return deletedCount;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error clearing all bookings");
            throw;
        }
    }

    // ОБНОВЛЕНИЕ СТАТУСА ПАРКОВОЧНОГО МЕСТА - в шарде parking_spots
    private async Task UpdateParkingSpotStatusAsync(int parkingId, int spotNumber, string status)
    {
        _logger?.LogDebug($"Updating parking spot {spotNumber} at parking {parkingId} to status '{status}' (spots shard)");
        try
        {
            using var conn = new NpgsqlConnection(_parkingSpotsShardString);
            await conn.OpenAsync();

            using var cmd = new NpgsqlCommand(
                "UPDATE parking_spots SET status = @status, updated_at = CURRENT_TIMESTAMP WHERE parking_id = @parkingId AND spot_number = @spotNumber",
                conn);
            cmd.Parameters.AddWithValue("parkingId", parkingId);
            cmd.Parameters.AddWithValue("spotNumber", spotNumber);
            cmd.Parameters.AddWithValue("status", status);

            int rowsAffected = await cmd.ExecuteNonQueryAsync();
            if (rowsAffected == 0)
            {
                _logger?.LogWarning($"No parking spot found with ID {spotNumber} at parking {parkingId} to update status");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, $"Error updating parking spot {spotNumber} at parking {parkingId} to status '{status}'");
            throw;
        }
    }

    // УДАЛЕНИЕ БРОНИРОВАНИЯ - из шарда parking_bookings + обновление статуса в шарде parking_spots
    public async Task<bool> DeleteBookingAsync(int bookingId)
    {
        _logger?.LogInformation($"Deleting booking {bookingId}");

        try
        {
            // 1. Сначала создаем/проверяем таблицу - ожидаем завершения
            await EnsureBookingsTableExistsAsync();

            // 2. Получаем информацию о бронировании перед удалением,
            // чтобы знать какую парковку и место обновить
            var bookingInfo = await GetBookingInfoAsync(bookingId);
            if (bookingInfo == null)
            {
                _logger?.LogWarning($"Booking {bookingId} not found for deletion");
                return false;
            }

            // 3. Деактивируем бронирование
            using var conn = new NpgsqlConnection(_bookingsShardString);
            await conn.OpenAsync();

            using var cmd = new NpgsqlCommand(
                "UPDATE bookings SET active = false WHERE id = @bookingId",
                conn);
            cmd.Parameters.AddWithValue("bookingId", bookingId);

            int rowsAffected = await cmd.ExecuteNonQueryAsync();

            // 4. Если бронирование успешно деактивировано, обновляем статус места
            if (rowsAffected > 0)
            {
                await UpdateParkingSpotStatusAsync(bookingInfo.ParkingId, bookingInfo.SpotNumber, "available");
                _logger?.LogInformation($"Successfully deleted booking {bookingId}");
                return true;
            }

            _logger?.LogWarning($"No rows affected when deleting booking {bookingId}");
            return false;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, $"Error deleting booking {bookingId}");
            throw;
        }
    }

    // ДОБАВЛЕНИЕ ЗАПИСИ В ИСТОРИЮ БРОНИРОВАНИЙ - в шарде parking_bookings (партиционированная таблица)
    private async Task AddToBookingHistoryAsync(string vehicleId, int parkingId, int spotNumber,
        DateTime bookingTime, DateTime endTime, string status)
    {
        _logger?.LogDebug($"Adding record to booking history for vehicle {vehicleId} (bookings shard)");
        try
        {
            using var conn = new NpgsqlConnection(_bookingsShardString);
            await conn.OpenAsync();

            using var cmd = new NpgsqlCommand(@"
                INSERT INTO booking_history (vehicle_id, parking_id, spot_number, booking_time, end_time, status) 
                VALUES (@vehicleId, @parkingId, @spotNumber, @bookingTime, @endTime, @status)",
                conn);
            cmd.Parameters.AddWithValue("vehicleId", vehicleId);
            cmd.Parameters.AddWithValue("parkingId", parkingId);
            cmd.Parameters.AddWithValue("spotNumber", spotNumber);
            cmd.Parameters.AddWithValue("bookingTime", bookingTime);
            cmd.Parameters.AddWithValue("endTime", endTime);
            cmd.Parameters.AddWithValue("status", status);

            await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, $"Error adding to booking history for vehicle {vehicleId}");
            // Не пробрасываем исключение, т.к. это не критичная операция
        }
    }

    // ОБНОВЛЕНИЕ БРОНИРОВАНИЯ - в шарде parking_bookings + обновление статуса в шарде parking_spots
    public async Task<bool> UpdateBookingAsync(int bookingId, string vehicleId, bool active)
    {
        _logger?.LogInformation($"Updating booking {bookingId}, vehicle: {vehicleId}, active: {active}");

        try
        {
            // 1. Сначала создаем/проверяем таблицу - ожидаем завершения
            await EnsureBookingsTableExistsAsync();

            // 2. Получаем текущую информацию о бронировании
            var currentBooking = await GetBookingInfoAsync(bookingId);
            if (currentBooking == null)
            {
                _logger?.LogWarning($"Booking {bookingId} not found for update");
                return false;
            }

            // 3. Обновляем бронирование
            using var conn = new NpgsqlConnection(_bookingsShardString);
            await conn.OpenAsync();

            using var cmd = new NpgsqlCommand(
                "UPDATE bookings SET vehicle_id = @vehicleId, active = @active WHERE id = @bookingId",
                conn);
            cmd.Parameters.AddWithValue("vehicleId", vehicleId);
            cmd.Parameters.AddWithValue("active", active);
            cmd.Parameters.AddWithValue("bookingId", bookingId);

            int rowsAffected = await cmd.ExecuteNonQueryAsync();

            // 4. Если изменился статус активности, обновляем статус места
            if (rowsAffected > 0 && currentBooking.Active != active)
            {
                string spotStatus = active ? "occupied" : "available";
                await UpdateParkingSpotStatusAsync(currentBooking.ParkingId, currentBooking.SpotNumber, spotStatus);
            }

            _logger?.LogInformation($"Successfully updated booking {bookingId}, rows affected: {rowsAffected}");
            return rowsAffected > 0;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, $"Error updating booking {bookingId}");
            throw;
        }
    }

    // ПОЛУЧЕНИЕ ИНФОРМАЦИИ О БРОНИРОВАНИИ - из шарда parking_bookings
    public async Task<BookingInfo> GetBookingInfoAsync(int bookingId)
    {
        _logger?.LogDebug($"Getting booking info for {bookingId}");

        try
        {
            // 1. Сначала создаем/проверяем таблицу - ожидаем завершения
            await EnsureBookingsTableExistsAsync();

            // 2. Получаем информацию о бронировании
            using var conn = new NpgsqlConnection(_bookingsShardString);
            await conn.OpenAsync();

            // Проверка структуры таблицы перед запросом
            var columns = await conn.QueryAsync<string>(
                "SELECT column_name FROM information_schema.columns WHERE table_name = 'bookings'");

            if (!columns.Any(c => c.Equals("parking_id", StringComparison.OrdinalIgnoreCase)))
            {
                _logger?.LogWarning("Bookings table exists but missing parking_id column, recreating...");
                await EnsureBookingsTableStructureAsync(conn);
            }

            string query = @"
            SELECT 
                id as BookingId, 
                vehicle_id as VehicleId, 
                parking_id as ParkingId, 
                spot_number as SpotNumber, 
                booking_time as BookingTime,
                active as Active 
            FROM bookings 
            WHERE id = @bookingId";

            var bookingInfo = await conn.QueryFirstOrDefaultAsync<BookingInfo>(query, new { bookingId });

            if (bookingInfo == null)
            {
                _logger?.LogDebug($"Booking {bookingId} not found");
            }
            else
            {
                _logger?.LogDebug($"Found booking {bookingId} for vehicle {bookingInfo.VehicleId}");
            }

            return bookingInfo;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, $"Error getting booking info for {bookingId}");
            throw;
        }
    }

    // ПОЛУЧЕНИЕ СПИСКА СВОБОДНЫХ МЕСТ - из шарда parking_spots
    public async Task<IEnumerable<object>> GetAvailableSpotsAsync(int parkingId)
    {
        _logger?.LogDebug($"Getting available spots for parking {parkingId} (spots shard)");
        try
        {
            using var conn = new NpgsqlConnection(_parkingSpotsShardString);
            await conn.OpenAsync();

            string sql = @"
                SELECT 
                    id as Id,
                    parking_id as ParkingId,
                    spot_number as SpotNumber, 
                    status as Status,
                    updated_at as UpdatedAt
                FROM parking_spots 
                WHERE parking_id = @parkingId 
                  AND status = 'available'
                ORDER BY spot_number";

            var spots = await conn.QueryAsync(sql, new { parkingId });
            _logger?.LogInformation($"Found {spots.Count()} available spots for parking {parkingId}");

            return spots;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, $"Error getting available spots for parking {parkingId}");
            throw;
        }
    }

    // ПОЛУЧЕНИЕ ДЕТАЛЬНОЙ ИНФОРМАЦИИ О МЕСТЕ - данные из обоих шардов
    public async Task<object?> GetParkingSpotDetailsAsync(int parkingId, int spotNumber)
    {
        _logger?.LogInformation($"Getting detailed info for spot {spotNumber} at parking {parkingId} from multiple shards");
        try
        {
            // 1. Получаем информацию из шарда с парковками
            object? spotDetails = null;
            using (var spotsConn = new NpgsqlConnection(_parkingSpotsShardString))
            {
                await spotsConn.OpenAsync();

                string parkingSpotsSql = @"
                    SELECT 
                        ps.id as SpotId,
                        ps.parking_id as ParkingId,
                        ps.spot_number as SpotNumber, 
                        ps.status as Status,
                        ps.updated_at as UpdatedAt,
                        pl.name as ParkingName,
                        pl.address as ParkingAddress,
                        pl.capacity as ParkingCapacity,
                        pl.latitude as Latitude, 
                        pl.longitude as Longitude,
                        pl.hourly_rate as HourlyRate
                    FROM parking_spots ps
                    JOIN parking_lots pl ON ps.parking_id = pl.parking_id
                    WHERE ps.parking_id = @parkingId AND ps.spot_number = @spotNumber";

                spotDetails = await spotsConn.QueryFirstOrDefaultAsync(
                    parkingSpotsSql,
                    new { parkingId, spotNumber });
            }

            if (spotDetails == null)
            {
                _logger?.LogWarning($"No spot found with number {spotNumber} at parking {parkingId}");
                return null;
            }

            // 2. Дополняем данными из шарда с бронированиями
            IEnumerable<object> bookingHistory;
            using (var bookingsConn = new NpgsqlConnection(_bookingsShardString))
            {
                await bookingsConn.OpenAsync();

                string bookingHistorySql = @"
                    SELECT 
                        id as BookingId, 
                        vehicle_id as VehicleId,
                        booking_time as BookingTime, 
                        active as Active
                    FROM bookings 
                    WHERE spot_number = @spotNumber 
                      AND parking_id = @parkingId
                    ORDER BY booking_time DESC 
                    LIMIT 5";

                bookingHistory = await bookingsConn.QueryAsync(
                    bookingHistorySql,
                    new { spotNumber, parkingId });
            }

            // 3. Объединяем данные
            var result = new
            {
                SpotDetails = spotDetails,
                BookingHistory = bookingHistory
            };

            return result;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, $"Error getting details for spot {spotNumber} at parking {parkingId}");
            throw;
        }
    }

    // ПОЛУЧЕНИЕ ИСТОРИИ БРОНИРОВАНИЙ - из шарда parking_bookings (партиционированная таблица)
    public async Task<IEnumerable<object>> GetBookingHistoryAsync(DateTime startDate, DateTime endDate)
    {
        _logger?.LogDebug($"Getting booking history from {startDate:yyyy-MM-dd} to {endDate:yyyy-MM-dd} (bookings shard)");
        try
        {
            using var conn = new NpgsqlConnection(_bookingsShardString);
            await conn.OpenAsync();

            string sql = @"
                SELECT 
                    id as HistoryId,
                    vehicle_id as VehicleId,
                    parking_id as ParkingId, 
                    spot_number as SpotNumber,
                    booking_time as BookingTime, 
                    end_time as EndTime,
                    status as Status
                FROM booking_history
                WHERE booking_time BETWEEN @startDate AND @endDate
                ORDER BY booking_time DESC";

            var history = await conn.QueryAsync(sql, new { startDate, endDate });
            _logger?.LogInformation($"Found {history.Count()} booking history records from {startDate:yyyy-MM-dd} to {endDate:yyyy-MM-dd}");

            return history;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, $"Error getting booking history from {startDate} to {endDate}");
            throw;
        }
    }

    // Метод для бронирования всех доступных мест на парковке или во всей системе
    public async Task<int> BookAllAvailableSpotsAsync(string vehicleId, int? parkingId = null)
    {
        string parkingScope = parkingId.HasValue ? $"for parking {parkingId}" : "across all parking lots";
        _logger?.LogWarning($"Booking ALL available spots {parkingScope} for vehicle {vehicleId}");

        try
        {
            // 1. Получаем все доступные места
            List<(int ParkingId, int SpotNumber)> availableSpots = new();

            using (var conn = new NpgsqlConnection(_parkingSpotsShardString))
            {
                await conn.OpenAsync();

                string sql = @"
                SELECT 
                    parking_id, 
                    spot_number 
                FROM parking_spots 
                WHERE status = 'available'";

                if (parkingId.HasValue)
                {
                    sql += " AND parking_id = @parkingId";
                    var spots = await conn.QueryAsync<(int, int)>(sql, new { parkingId = parkingId.Value });
                    availableSpots.AddRange(spots);
                }
                else
                {
                    var spots = await conn.QueryAsync<(int, int)>(sql);
                    availableSpots.AddRange(spots);
                }
            }

            _logger?.LogInformation($"Found {availableSpots.Count} available spots {parkingScope}");

            // 2. Бронируем каждое место
            int bookedCount = 0;
            List<Exception> errors = new List<Exception>();

            foreach (var spot in availableSpots)
            {
                try
                {
                    await BookSpotAsync(vehicleId, spot.ParkingId, spot.SpotNumber);
                    bookedCount++;
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, $"Failed to book spot {spot.SpotNumber} at parking {spot.ParkingId}");
                    errors.Add(ex);

                    // Продолжаем даже при ошибках
                    continue;
                }
            }

            if (errors.Count > 0)
            {
                _logger?.LogWarning($"Completed with {errors.Count} errors. Successfully booked {bookedCount} spots out of {availableSpots.Count}");
            }
            else
            {
                _logger?.LogInformation($"Successfully booked all {bookedCount} available spots {parkingScope}");
            }

            return bookedCount;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, $"Error booking all available spots {parkingScope}");
            throw;
        }
    }

    public class ParkingLot
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public int Capacity { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public decimal HourlyRate { get; set; }
    }

    public class BookingInfo
    {
        public int BookingId { get; set; }
        public string VehicleId { get; set; } = string.Empty;
        public int ParkingId { get; set; }
        public int SpotNumber { get; set; }
        public DateTime BookingTime { get; set; }
        public bool Active { get; set; }
    }
}