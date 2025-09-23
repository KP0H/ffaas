using StackExchange.Redis;
using System.Text.Json;

namespace FfaasLite.Infrastructure.Cache
{
    public class RedisCache
    {
        private readonly IDatabase _db;
        private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);
        public RedisCache(IConnectionMultiplexer mux) => _db = mux.GetDatabase();

        public async Task SetAsync<T>(string key, T value, TimeSpan? ttl = null)
            => await _db.StringSetAsync(key, JsonSerializer.Serialize(value, _json), ttl);

        public async Task<T?> GetAsync<T>(string key)
        {
            var val = await _db.StringGetAsync(key);
            return val.HasValue ? JsonSerializer.Deserialize<T>(val!, _json) : default;
        }
    }
}
