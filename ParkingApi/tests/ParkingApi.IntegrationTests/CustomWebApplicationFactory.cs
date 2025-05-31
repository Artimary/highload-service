using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ParkingApi.Services;

namespace ParkingApi.IntegrationTests
{
    public class CustomWebApplicationFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // Проверка наличия файла конфигурации
            string configPath = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.Integration.json");
            Console.WriteLine($"Загрузка конфигурации из: {configPath}");

            if (!File.Exists(configPath))
            {
                Console.WriteLine($"ВНИМАНИЕ: Файл конфигурации не найден по пути: {configPath}");

                // Проверим текущую директорию и все файлы в ней
                string currentDir = Directory.GetCurrentDirectory();
                Console.WriteLine($"Текущая директория: {currentDir}");
                Console.WriteLine("Файлы в текущей директории:");

                foreach (var file in Directory.GetFiles(currentDir))
                {
                    Console.WriteLine($"  - {Path.GetFileName(file)}");
                }                // Поиск файла в родительских каталогах
                string? searchDir = Directory.GetParent(currentDir)?.FullName;
                while (searchDir != null)
                {
                    string alternatePath = Path.Combine(searchDir, "appsettings.Integration.json");
                    if (File.Exists(alternatePath))
                    {
                        Console.WriteLine($"Найден файл конфигурации в: {alternatePath}");
                        configPath = alternatePath;
                        break;
                    }
                    searchDir = Directory.GetParent(searchDir)?.FullName;
                }
            }
            else
            {
                Console.WriteLine("Файл конфигурации найден успешно.");
            }

            builder.ConfigureAppConfiguration((context, config) =>
            {
                // Сначала удаляем все существующие источники конфигурации
                config.Sources.Clear();

                // Добавляем базовые настройки из основного проекта
                config.AddJsonFile("appsettings.json", optional: true);

                // Добавляем интеграционные настройки (заменяя основные)
                if (File.Exists(configPath))
                {
                    config.AddJsonFile(configPath, optional: false);
                }
                else
                {                    // Если файла нет, добавляем настройки программно
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        {"ConnectionStrings:PostgreSQL", "Host=localhost;Port=5433;Database=parking_test;Username=postgres;Password=postgres"},
                        {"ConnectionStrings:InfluxDB", "http://localhost:18086"},
                        {"InfluxDB:Token", "my-super-secret-auth-token"},
                        {"InfluxDB:Org", "test-org"},
                        {"InfluxDB:Bucket", "iot_bucket"}
                    });
                    Console.WriteLine("Добавлены программные настройки из-за отсутствия файла конфигурации.");
                }

                // Замена переменных окружения
                config.AddEnvironmentVariables();

                // Выводим информацию о загруженной конфигурации
                var configuration = config.Build();
                Console.WriteLine($"PostgreSQL connection: {configuration.GetConnectionString("PostgreSQL")}");
                Console.WriteLine($"InfluxDB connection: {configuration.GetConnectionString("InfluxDB")}");
                Console.WriteLine($"InfluxDB org: {configuration["InfluxDB:Org"]}");
                Console.WriteLine($"InfluxDB bucket: {configuration["InfluxDB:Bucket"]}");
            });

            builder.UseEnvironment("Integration");

            builder.ConfigureServices(services =>
            {
                // Настраиваем логирование
                services.AddLogging(logging =>
                {
                    logging.ClearProviders();
                    logging.AddConsole();
                    logging.SetMinimumLevel(LogLevel.Debug);
                });

                // Регистрируем или переопределяем сервисы для тестового окружения
                services.AddScoped<IServiceProvider>(sp => sp);
            });
        }
    }
}
