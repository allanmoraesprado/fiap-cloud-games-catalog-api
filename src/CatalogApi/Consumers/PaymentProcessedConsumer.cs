using System.Text.Json;
using Confluent.Kafka;
using CatalogApi.Application.Interfaces;
using CatalogApi.Contracts;
using CatalogApi.Messaging;
using Microsoft.Extensions.Options;

namespace CatalogApi.Consumers;

// Consumes fcg.payments.processed (group catalog-service) and completes the purchase
// (writes user_games on Approved). Distinct group from NotificationsAPI => fan-out.
public class PaymentProcessedConsumer : BackgroundService
{
    private readonly KafkaSettings _settings;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PaymentProcessedConsumer> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public PaymentProcessedConsumer(
        IOptions<KafkaSettings> options,
        IServiceScopeFactory scopeFactory,
        ILogger<PaymentProcessedConsumer> logger)
    {
        _settings = options.Value;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
        => Task.Run(() => ConsumeLoop(stoppingToken), stoppingToken);

    private async Task ConsumeLoop(CancellationToken stoppingToken)
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = _settings.BootstrapServers,
            GroupId = _settings.PaymentsConsumerGroup,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        };

        using var consumer = new ConsumerBuilder<string, string>(config).Build();
        consumer.Subscribe(_settings.PaymentProcessedTopic);
        _logger.LogInformation("Subscribed to {Topic} as group {Group}", _settings.PaymentProcessedTopic, _settings.PaymentsConsumerGroup);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                ConsumeResult<string, string> cr;
                try { cr = consumer.Consume(stoppingToken); }
                catch (ConsumeException ex) { _logger.LogWarning(ex, "Consume error; retrying."); continue; }

                if (cr?.Message?.Value is null) continue;

                try
                {
                    var evt = JsonSerializer.Deserialize<PaymentProcessedEvent>(cr.Message.Value, JsonOptions);
                    if (evt is not null)
                    {
                        // Resolve the scoped handler (uses the scoped DbContext) per message.
                        using var scope = _scopeFactory.CreateScope();
                        var handler = scope.ServiceProvider.GetRequiredService<IPurchaseCompletionService>();
                        await handler.CompleteAsync(evt, stoppingToken);
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Malformed PaymentProcessedEvent; skipping message.");
                }

                try { consumer.Commit(cr); }
                catch (KafkaException ex) { _logger.LogWarning(ex, "Commit failed."); }
            }
        }
        catch (OperationCanceledException)
        {
            // graceful shutdown
        }
        finally
        {
            consumer.Close();
        }
    }
}
