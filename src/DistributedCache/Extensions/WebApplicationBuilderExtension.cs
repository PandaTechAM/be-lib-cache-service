﻿using DistributedCache.Options;
using DistributedCache.Serializers;
using DistributedCache.Services.Implementations;
using DistributedCache.Services.Interfaces;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using StackExchange.Redis.Extensions.Core.Configuration;

namespace DistributedCache.Extensions;

public static class WebApplicationBuilderExtension
{
   public static WebApplicationBuilder AddDistributedCache(this WebApplicationBuilder builder,
      Action<CacheConfigurationOptions> configureOptions)
   {
      builder.Services.Configure(configureOptions);

      ValidateOptions(builder);

      var configurations = builder.Services
                                  .BuildServiceProvider()
                                  .GetRequiredService<IOptions<CacheConfigurationOptions>>()
                                  .Value;

      var redisOptions = ConfigurationOptions.Parse(configurations.RedisConnectionString);

      redisOptions.ConnectRetry = configurations.ConnectRetry;
      redisOptions.ConnectTimeout = (int)configurations.ConnectTimeout.TotalMilliseconds;
      redisOptions.SyncTimeout = (int)configurations.SyncTimeout.TotalMilliseconds;
      redisOptions.DefaultDatabase = 0;
      redisOptions.ReconnectRetryPolicy = new ExponentialRetry(10000);

      builder.Services.AddSingleton<IConnectionMultiplexer>(ConnectionMultiplexer.Connect(redisOptions));

      builder.Services.AddSingleton<IRateLimitService, RedisRateLimitService>();
      builder.Services.AddSingleton<IDistributedLockService, RedisLockService>();
      builder.Services.AddSingleton<HybridCache, RedisDistributedCache>();

      var redisConfiguration = new RedisConfiguration
      {
         ConnectionString = configurations.RedisConnectionString,
         Name = "DistributedCacheConfiguration"
      };

      builder.Services.AddStackExchangeRedisExtensions<RedisMsgPackObjectSerializer>(redisConfiguration);

      builder.AddRedisHealthCheck(configurations.RedisConnectionString);

      return builder;
   }

   private static void ValidateOptions(WebApplicationBuilder builder)
   {
      builder.Services.PostConfigure<CacheConfigurationOptions>(options =>
      {
         if (string.IsNullOrEmpty(options.RedisConnectionString))
         {
            throw new ArgumentException("AddCacheService options: RedisConnectionString is required.");
         }

         if (options.ConnectRetry <= 0)
         {
            throw new ArgumentException("AddCacheService options: ConnectRetry must be greater than 0.");
         }

         if (options.ConnectTimeout <= TimeSpan.Zero)
         {
            throw new ArgumentException("AddCacheService options: ConnectTimeout must be greater than 0.");
         }

         if (options.SyncTimeout <= TimeSpan.Zero)
         {
            throw new ArgumentException("AddCacheService options: SyncTimeout must be greater than 0.");
         }

         if (options.DistributedLockMaxDuration <= TimeSpan.FromSeconds(1))
         {
            throw new ArgumentException(
               "AddCacheService options: DistributedLockDuration must be greater or equal 1.");
         }

         if (options.DefaultExpiration <= TimeSpan.Zero)
         {
            throw new ArgumentException("AddCacheService options: DefaultExpiration must be greater than 0.");
         }
      });
   }
}