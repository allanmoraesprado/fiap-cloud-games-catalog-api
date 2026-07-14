namespace CatalogApi.Messaging;

public class KafkaSettings
{
    public string BootstrapServers { get; set; } = "localhost:29092";

    // Producer (M4): order placement.
    public string OrderPlacedTopic { get; set; } = "fcg.orders.placed";

    // Consumer (M5): payment result. catalog-service group (distinct from notifications-service).
    public string PaymentProcessedTopic { get; set; } = "fcg.payments.processed";
    public string PaymentsConsumerGroup { get; set; } = "catalog-service";
}
