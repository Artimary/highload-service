using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using StackExchange.Redis;

namespace ParkingApi.Services
{
    public class RedisCacheService : ICacheService
    {
        private readonly IDistributedCache _cache;
        private readonly IConfiguration _configuration;
        private readonly ConnectionMultiplexer _redis;

        public RedisCacheService(IDistributedCache cache, IConfiguration configuration)
        {
            _cache = cache;
            _configuration = configuration;
            _redis = ConnectionMultiplexer.Connect(_configuration.GetConnectionString("Redis"));
        }

        public async Task<T> GetAsync<T>(string key)
        {
            var cachedValue = await _cache.GetStringAsync(key);
            if (string.IsNullOrEmpty(cachedValue))
                return default;

            return JsonSerializer.Deserialize<T>(cachedValue);
        }

        public async Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiration = null)
        {
            var cachedValue = await GetAsync<T>(key);
            if (cachedValue != null)
                return cachedValue;

            var result = await factory();
            await SetAsync(key, result, expiration);
            return result;
        }

        public async Task SetAsync<T>(string key, T value, TimeSpan? expiration = null)
        {
            var options = new DistributedCacheEntryOptions();

            if (expiration.HasValue)
                options.AbsoluteExpirationRelativeToNow = expiration.Value;
            else
                options.AbsoluteExpirationRelativeToNow =
                    TimeSpan.FromSeconds(_configuration.GetValue<int>("Redis:DefaultTTL", 300));

            var serializedValue = JsonSerializer.Serialize(value);
            await _cache.SetStringAsync(key, serializedValue, options);
        }

        public async Task RemoveAsync(string key)
        {
            await _cache.RemoveAsync(key);
        }

        public async Task RemoveByPrefixAsync(string prefix)
        {
            if (string.IsNullOrEmpty(prefix))
                throw new ArgumentException("Prefix cannot be null or empty", nameof(prefix));

            var server = _redis.GetServer(_redis.GetEndPoints()[0]);
            var keys = server.Keys(pattern: $"{_configuration.GetSection("Redis:InstanceName").Value}{prefix}*");

            foreach (var key in keys)
            {
                await _cache.RemoveAsync(key.ToString().Substring(_configuration.GetSection("Redis:InstanceName").Value.Length));
            }
        }
    }
}