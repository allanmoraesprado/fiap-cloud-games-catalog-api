namespace CatalogApi.Messaging;

public class KafkaSettings
{
    public string BootstrapServers { get; set; } = "localhost:29092";
    public string OrderPlacedTopic { get; set; } = "fcg.orders.placed";
}
