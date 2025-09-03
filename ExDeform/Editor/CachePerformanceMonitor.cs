using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using ExDeform.Runtime.Data;

namespace ExDeform.Editor
{
    /// <summary>
    /// Cache performance monitoring system
    /// キャッシュパフォーマンス監視システム
    /// </summary>
    [Serializable]
    public struct PerformanceStatistics
    {
        public double averageTime;
        public double minTime;
        public double maxTime;
        public int totalOperations;
        public int successfulOperations;
        public float hitRate;
        public DateTime lastUpdated;

        public override string ToString()
        {
            return $"Avg: {averageTime:F2}ms, HitRate: {hitRate:P1}, Ops: {totalOperations}";
        }
    }

    /// <summary>
    /// Records used for performance tracking
    /// パフォーマンス追跡用のレコード
    /// </summary>
    [Serializable]
    internal struct PerformanceRecord
    {
        public CacheType cacheType;
        public bool wasSuccessful;
        public double operationTime;
        public bool wasLoadOperation;
        public DateTime timestamp;
    }

    /// <summary>
    /// Monitor for cache performance metrics
    /// キャッシュパフォーマンスメトリクス監視
    /// </summary>
    public class CachePerformanceMonitor
    {
        private readonly List<PerformanceRecord> operationRecords;
        private readonly int maxRecordHistory = 1000;

        public CachePerformanceMonitor()
        {
            operationRecords = new List<PerformanceRecord>();
        }

        /// <summary>
        /// Record a cache operation for performance tracking
        /// パフォーマンス追跡のためキャッシュ操作を記録
        /// </summary>
        public void RecordCacheOperation(CacheType cacheType, bool wasSuccessful, double operationTimeMs, bool wasLoadOperation)
        {
            var record = new PerformanceRecord
            {
                cacheType = cacheType,
                wasSuccessful = wasSuccessful,
                operationTime = operationTimeMs,
                wasLoadOperation = wasLoadOperation,
                timestamp = DateTime.Now
            };

            operationRecords.Add(record);

            // Keep only recent records to prevent memory bloat
            if (operationRecords.Count > maxRecordHistory)
            {
                operationRecords.RemoveAt(0);
            }
        }

        /// <summary>
        /// Get overall performance statistics
        /// 全体のパフォーマンス統計を取得
        /// </summary>
        public PerformanceStatistics GetOverallStatistics()
        {
            if (operationRecords.Count == 0)
            {
                return new PerformanceStatistics
                {
                    averageTime = 0,
                    minTime = 0,
                    maxTime = 0,
                    totalOperations = 0,
                    successfulOperations = 0,
                    hitRate = 0f,
                    lastUpdated = DateTime.Now
                };
            }

            var times = operationRecords.Select(r => r.operationTime).ToArray();
            var successCount = operationRecords.Count(r => r.wasSuccessful);

            return new PerformanceStatistics
            {
                averageTime = times.Average(),
                minTime = times.Min(),
                maxTime = times.Max(),
                totalOperations = operationRecords.Count,
                successfulOperations = successCount,
                hitRate = (float)successCount / operationRecords.Count,
                lastUpdated = DateTime.Now
            };
        }

        /// <summary>
        /// Get performance statistics by provider type
        /// プロバイダータイプ別パフォーマンス統計を取得
        /// </summary>
        public Dictionary<CacheType, PerformanceStatistics> GetProviderStatistics()
        {
            var result = new Dictionary<CacheType, PerformanceStatistics>();
            var groupedRecords = operationRecords.GroupBy(r => r.cacheType);

            foreach (var group in groupedRecords)
            {
                var records = group.ToArray();
                var times = records.Select(r => r.operationTime).ToArray();
                var successCount = records.Count(r => r.wasSuccessful);

                result[group.Key] = new PerformanceStatistics
                {
                    averageTime = times.Average(),
                    minTime = times.Min(),
                    maxTime = times.Max(),
                    totalOperations = records.Length,
                    successfulOperations = successCount,
                    hitRate = (float)successCount / records.Length,
                    lastUpdated = DateTime.Now
                };
            }

            return result;
        }

        /// <summary>
        /// Remove old records to prevent memory bloat
        /// メモリ肥大化防止のため古いレコードを削除
        /// </summary>
        public void TrimOldRecords()
        {
            var cutoffTime = DateTime.Now.AddMinutes(-30); // Keep only last 30 minutes
            operationRecords.RemoveAll(r => r.timestamp < cutoffTime);
        }

        /// <summary>
        /// Clear all performance records
        /// 全てのパフォーマンスレコードをクリア
        /// </summary>
        public void ClearRecords()
        {
            operationRecords.Clear();
        }
    }
}