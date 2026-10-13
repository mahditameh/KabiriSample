using Kabiri.Domain;

namespace Kabiri.Application;

/// <summary>One equal-width time bucket over acceptable readings only. Empty buckets are omitted.</summary>
/// <param name="BucketStart">Inclusive start of the bucket (UTC).</param>
/// <param name="Count">Number of acceptable readings in the bucket.</param>
/// <param name="Avg">Mean value.</param>
/// <param name="Min">Minimum value.</param>
/// <param name="Max">Maximum value.</param>
public sealed record Bucket(DateTimeOffset BucketStart, int Count, double Avg, double Min, double Max);

public static class AggregationService
{
    /// <summary>Buckets [from,to) of equal size over acceptable readings only. Empty buckets omitted.</summary>
    public static List<Bucket> Aggregate(IEnumerable<Reading> acceptable, DateTimeOffset from, DateTimeOffset to, int bucketSeconds)
    {
        if (bucketSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(bucketSeconds));
        var groups = new Dictionary<long, List<double>>();
        foreach (var r in acceptable)
        {
            if (r.Ts < from || r.Ts >= to) continue;
            var idx = (long)((r.Ts - from).TotalSeconds / bucketSeconds);
            if (!groups.TryGetValue(idx, out var l)) groups[idx] = l = new List<double>();
            l.Add(r.Value);
        }
        return groups.OrderBy(kv => kv.Key).Select(kv =>
        {
            var vals = kv.Value;
            return new Bucket(from.AddSeconds(kv.Key * bucketSeconds), vals.Count, vals.Average(), vals.Min(), vals.Max());
        }).ToList();
    }
}
