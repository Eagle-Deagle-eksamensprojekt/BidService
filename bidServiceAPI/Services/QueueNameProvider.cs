public class QueueNameProvider
{
    private readonly ILogger<QueueNameProvider> _logger;
    private string? _activeQueueName;

    public QueueNameProvider(ILogger<QueueNameProvider> logger)
    {
        _logger = logger;
    }

    public void SetActiveQueueName(string queueName)
    {
        _logger.LogInformation("Setting active queue name to {QueueName}.", queueName);
        _activeQueueName = queueName;
    }

    public string? GetActiveQueueName()
    {
        if (string.IsNullOrEmpty(_activeQueueName))
        {
            _logger.LogWarning("No active queue name is set.");
        }
        return _activeQueueName;
    }
}
