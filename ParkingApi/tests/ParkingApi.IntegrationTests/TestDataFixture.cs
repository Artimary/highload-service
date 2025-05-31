using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using InfluxDB.Client;
using InfluxDB.Client.Api.Domain;
using InfluxDB.Client.Writes;
using Microsoft.Extensions.Configuration;
using Npgsql;
using ParkingApi.Services;

namespace ParkingApi.IntegrationTests
{
    public class TestDataFixture : IDisposable
    {
        private readonly string _postgresConnectionString;
        private readonly string _influxUrl;
        private readonly string _influxToken;
        private readonly string _influxOrg;
        private readonly string _influxBucket; public TestDataFixture()
        {
            var config = new ConfigurationBuilder()
                .AddJsonFile("appsettings.Integration.json")
                .AddEnvironmentVariables()
                .Build();

            _postgresConnectionString = config.GetConnectionString("PostgreSQL") ?? throw new InvalidOperationException("PostgreSQL connection string not found");
            _influxUrl = config.GetConnectionString("InfluxDB") ?? throw new InvalidOperationException("InfluxDB URL not found");
            _influxToken = config["InfluxDB:Token"] ?? throw new InvalidOperationException("InfluxDB token not found");
            _influxOrg = config["InfluxDB:Org"] ?? throw new InvalidOperationException("InfluxDB org not found");
            _influxBucket = config["InfluxDB:Bucket"] ?? throw new InvalidOperationException("InfluxDB bucket not found");

            SetupPostgresAsync().GetAwaiter().GetResult();
            SetupInfluxDbAsync().GetAwaiter().GetResult();
        }
        public async Task SetupPostgresAsync()
        {
            int maxRetries = 5;
            int retryDelay = 3000; // 3 секунды
            int currentRetry = 0;

            while (currentRetry < maxRetries)
            {
                try
                {
                    Console.WriteLine($"Попытка подключения к PostgreSQL ({currentRetry + 1}/{maxRetries})"); await using var conn = new NpgsqlConnection(_postgresConnectionString);

                    // Открываем соединение с базой данных
                    await conn.OpenAsync();

                    Console.WriteLine("PostgreSQL соединение установлено успешно.");                    // Проверяем соединение
                    using (var cmd = new NpgsqlCommand("SELECT 1", conn))
                    {
                        var checkResult = await cmd.ExecuteScalarAsync();
                        Console.WriteLine($"Проверка PostgreSQL: {checkResult}");
                    }

                    // Очищаем таблицы если они существуют
                    try
                    {
                        await using var clearCmd = new NpgsqlCommand(
                            @"TRUNCATE TABLE bookings CASCADE;
                              TRUNCATE TABLE parking_lots CASCADE;",
                            conn);
                        await clearCmd.ExecuteNonQueryAsync();
                        Console.WriteLine("Таблицы очищены успешно.");
                    }
                    catch (PostgresException e) when (e.SqlState == "42P01") // 42P01 = undefined_table
                    {
                        Console.WriteLine("Таблицы не существуют, создаем их.");
                    }

                    // Создаем таблицы с более надежной конструкцией
                    await using var createTableCmd = new NpgsqlCommand(
                        @"CREATE TABLE IF NOT EXISTS parking_lots (
                            id SERIAL PRIMARY KEY,
                            lat DOUBLE PRECISION NOT NULL,
                            lon DOUBLE PRECISION NOT NULL,
                            total_spots INTEGER NOT NULL
                        );

                        CREATE TABLE IF NOT EXISTS bookings (
                            id SERIAL PRIMARY KEY,
                            vehicle_id VARCHAR(50) NOT NULL,
                            parking_id INTEGER NOT NULL REFERENCES parking_lots(id) ON DELETE CASCADE,
                            spot_number INTEGER NOT NULL,
                            active BOOLEAN DEFAULT TRUE,
                            created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                            UNIQUE(parking_id, spot_number) DEFERRABLE INITIALLY IMMEDIATE
                        );
                        
                        -- Создаём индекс для более быстрого поиска по активным бронированиям
                        CREATE INDEX IF NOT EXISTS idx_bookings_active ON bookings(active);",
                        conn);
                    await createTableCmd.ExecuteNonQueryAsync();
                    Console.WriteLine("Таблицы созданы успешно.");                    // Вставляем тестовые данные с надежным обновлением и большим количеством информации
                    await using var insertParkingCmd = new NpgsqlCommand(
                        @"INSERT INTO parking_lots (id, lat, lon, total_spots) VALUES 
                          (1, 59.9343, 30.3351, 10),
                          (2, 59.9600, 30.3200, 5)
                          ON CONFLICT (id) DO UPDATE SET 
                              lat = EXCLUDED.lat,
                              lon = EXCLUDED.lon,
                              total_spots = EXCLUDED.total_spots
                          RETURNING id;",
                        conn);
                    var result = await insertParkingCmd.ExecuteScalarAsync();
                    Console.WriteLine($"Тестовые данные добавлены успешно. ID: {result}");

                    // Проверяем, что данные реально добавлены
                    await using var checkCmd = new NpgsqlCommand("SELECT COUNT(*) FROM parking_lots", conn);
                    var count = Convert.ToInt32(await checkCmd.ExecuteScalarAsync());
                    Console.WriteLine($"Количество парковок в БД: {count}");

                    // Успешно настроили PostgreSQL - выходим из цикла
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"PostgreSQL Error: {ex.GetType().Name} - {ex.Message}");
                    if (++currentRetry >= maxRetries)
                    {
                        Console.WriteLine($"Достигнуто максимальное количество попыток ({maxRetries})");
                        throw;
                    }

                    Console.WriteLine($"Повтор через {retryDelay / 1000} секунд...");
                    await Task.Delay(retryDelay);
                    retryDelay *= 2; // Экспоненциальная задержка
                }
            }
        }
        public async Task SetupInfluxDbAsync()
        {
            int maxRetries = 5;
            int retryDelay = 3000; // 3 секунды
            int currentRetry = 0;

            while (currentRetry < maxRetries)
            {
                try
                {
                    Console.WriteLine($"Попытка подключения к InfluxDB ({currentRetry + 1}/{maxRetries})"); using var client = new InfluxDBClient(_influxUrl, _influxToken);

                    Console.WriteLine("Подключение к InfluxDB (без ping, т.к. .NET client имеет проблемы с контейнерной версией)...");

                    var writeApi = client.GetWriteApi();

                    // Удаляем существующие данные
                    try
                    {
                        var deleteApi = client.GetDeleteApi();
                        await Task.Run(() => deleteApi.Delete(
                            DateTime.UtcNow.AddDays(-1),
                            DateTime.UtcNow.AddDays(1),
                            "measurement=\"parking_data\"",
                            _influxBucket,
                            _influxOrg
                        ));
                        Console.WriteLine("Данные из InfluxDB удалены успешно.");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Ошибка при удалении данных из InfluxDB: {ex.Message}. Продолжаем.");
                    }                    // Записываем тестовые данные
                    try
                    {
                        var time = DateTime.UtcNow;

                        var point1 = PointData.Measurement("parking_data")
                            .Tag("device_id", "1")
                            .Field("free_spots", 5)
                            .Timestamp(time, WritePrecision.Ns);

                        var point2 = PointData.Measurement("parking_data")
                            .Tag("device_id", "2")
                            .Field("free_spots", 3)
                            .Timestamp(time, WritePrecision.Ns);

                        Console.WriteLine("Запись тестовых данных в InfluxDB...");
                        writeApi.WritePoint(point1, _influxBucket, _influxOrg);
                        writeApi.WritePoint(point2, _influxBucket, _influxOrg);

                        // Убедимся, что данные записались, делаем flush
                        writeApi.Flush();
                        Console.WriteLine("Данные успешно записаны в InfluxDB.");

                        Console.WriteLine("InfluxDB настроен успешно (запись данных без проверки чтения).");

                        // Успешно настроили InfluxDB - выходим из цикла
                        break;
                    }
                    catch (Exception writeEx)
                    {
                        Console.WriteLine($"Ошибка при записи данных в InfluxDB: {writeEx.Message}");
                        throw;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"InfluxDB Error: {ex.GetType().Name} - {ex.Message}");
                    if (++currentRetry >= maxRetries)
                    {
                        Console.WriteLine($"Достигнуто максимальное количество попыток ({maxRetries})");
                        throw;
                    }

                    Console.WriteLine($"Повтор через {retryDelay / 1000} секунд...");
                    await Task.Delay(retryDelay);
                    retryDelay *= 2; // Экспоненциальная задержка
                }
            }
        }
        public async Task CleanupPostgresAsync()
        {
            try
            {
                await using var conn = new NpgsqlConnection(_postgresConnectionString);
                await conn.OpenAsync();

                // Используем более безопасную очистку с проверками наличия таблиц
                await using var cmd = new NpgsqlCommand(
                    @"DO $$ 
                    BEGIN 
                        IF EXISTS(SELECT FROM pg_tables WHERE schemaname = 'public' AND tablename = 'bookings') THEN
                            TRUNCATE TABLE bookings CASCADE;
                        END IF;
                        
                        IF EXISTS(SELECT FROM pg_tables WHERE schemaname = 'public' AND tablename = 'parking_lots') THEN
                            TRUNCATE TABLE parking_lots CASCADE;
                        END IF;
                    END $$;",
                    conn);
                await cmd.ExecuteNonQueryAsync();
                Console.WriteLine("База данных успешно очищена");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при очистке PostgreSQL: {ex.Message}");
                // Продолжаем выполнение, не блокируя тесты из-за ошибки очистки
            }
        }
        public async Task CleanupInfluxDbAsync()
        {
            using var client = new InfluxDBClient(_influxUrl, _influxToken);
            var deleteApi = client.GetDeleteApi();
            try
            {
                await Task.Run(() => deleteApi.Delete(
                    DateTime.UtcNow.AddDays(-1),
                    DateTime.UtcNow.AddDays(1),
                    "measurement=\"parking_data\"",
                    _influxBucket,
                    _influxOrg
                ));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при очистке данных InfluxDB: {ex.Message}");
                // Игнорируем ошибки, если данных нет
            }
        }
        public void Dispose()
        {
            try
            {
                CleanupPostgresAsync().GetAwaiter().GetResult();
                CleanupInfluxDbAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при очистке тестовых данных: {ex.Message}");
                // Игнорируем ошибки при очистке
            }
        }
    }
}
