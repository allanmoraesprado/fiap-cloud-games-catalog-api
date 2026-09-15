using Prometheus;

namespace CatalogApi.Observability;

// Custom Prometheus counters (Phase 3). Exposed on /metrics next to the default HTTP metrics.
// Labels are low-cardinality only: never user ids, game ids, order ids or other PII.
public static class FcgMetrics
{
    public static readonly Counter EventsPublished = Prometheus.Metrics.CreateCounter(
        "fcg_events_published_total",
        "Kafka events published by topic and result (success|failure).",
        new CounterConfiguration { LabelNames = new[] { "topic", "result" } });

    public static readonly Counter EventsConsumed = Prometheus.Metrics.CreateCounter(
        "fcg_events_consumed_total",
        "Kafka events consumed by topic and result (processed|malformed).",
        new CounterConfiguration { LabelNames = new[] { "topic", "result" } });

    public static readonly Counter PaymentsConsumed = Prometheus.Metrics.CreateCounter(
        "fcg_catalog_payments_consumed_total",
        "PaymentProcessedEvent consumed by status (approved|rejected|other).",
        new CounterConfiguration { LabelNames = new[] { "status" } });

    public static readonly Counter LibraryGrants = Prometheus.Metrics.CreateCounter(
        "fcg_catalog_library_grants_total",
        "Library grant outcomes (added|already_owned|duplicate|rejected).",
        new CounterConfiguration { LabelNames = new[] { "result" } });

    public static readonly Counter CacheRequests = Prometheus.Metrics.CreateCounter(
        "fcg_cache_requests_total",
        "Cached reads by outcome (hit|miss|bypass).",
        new CounterConfiguration { LabelNames = new[] { "outcome" } });

    public static readonly Counter CacheInvalidations = Prometheus.Metrics.CreateCounter(
        "fcg_cache_invalidations_total",
        "Cache invalidations by target (games|game|library|other).",
        new CounterConfiguration { LabelNames = new[] { "target" } });

    static FcgMetrics()
    {
        // Pre-create the known label sets so they are exported as 0 before the first event.
        foreach (var s in new[] { "approved", "rejected" }) PaymentsConsumed.WithLabels(s);
        foreach (var r in new[] { "added", "already_owned", "duplicate", "rejected" }) LibraryGrants.WithLabels(r);
        foreach (var o in new[] { "hit", "miss", "bypass" }) CacheRequests.WithLabels(o);
        foreach (var t in new[] { "games", "game", "library" }) CacheInvalidations.WithLabels(t);
    }

    // Touching the type runs the static constructor; called once at startup.
    public static void EnsureInitialized() { }

    // Normalizes the event status into a bounded label value.
    public static string StatusLabel(string? status) =>
        string.Equals(status, "Approved", StringComparison.OrdinalIgnoreCase) ? "approved"
        : string.Equals(status, "Rejected", StringComparison.OrdinalIgnoreCase) ? "rejected"
        : "other";
}
