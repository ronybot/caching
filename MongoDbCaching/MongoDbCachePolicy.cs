﻿using System;

namespace PubComp.Caching.MongoDbCaching
{
    public class MongoDbCachePolicy
    {
        /// <summary>
        /// Required parameter - connection string to MongoDB
        /// </summary>
        public string ConnectionString { get; set; }

        /// <summary>
        /// Optional parameter - database name in MongoDB, defaults to CacheDb
        /// </summary>
        public string DatabaseName { get; set; }

        /// <summary>
        /// Gets or sets a value that indicates whether a cache entry should be evicted at a specified time.
        /// </summary>
        /// <remarks>Default value is a date-time value that is set to the maximum possible value,
        /// which indicates that the entry does not expire at a pre-specified time</remarks>
        public DateTimeOffset? AbsoluteExpiration { get; set; }

        /// <summary>
        /// Gets or sets a value that indicates whether a cache entry should be evicted
        /// within a given span of time since its addition.
        /// </summary>
        /// <remarks>Default value is a time-duration value that is set to zero,
        /// which indicates that a cache entry has no expiration from base on a time span from addition.</remarks>
        public TimeSpan? ExpirationFromAdd { get; set; }

        /// <summary>
        /// A span of time within which a cache entry must be accessed
        /// before the cache entry is evicted from the cache.
        /// </summary>
        /// <remarks>Default value is a time-duration value that is set to zero,
        /// which indicates that a cache entry has no sliding expiration time</remarks>
        public TimeSpan? SlidingExpiration { get; set; }

        /// <summary>
        /// Notifications providers. Currently supports "redis", default is none. 
        /// </summary>
        public string SyncProvider { get; set; }

        public MongoDbCachePolicy()
        {
            ConnectionString = new PubComp.NoSql.MongoDbDriver.MongoDbConnectionInfo().ConnectionString;
            DatabaseName = "CacheDb";
        }
    }
}
