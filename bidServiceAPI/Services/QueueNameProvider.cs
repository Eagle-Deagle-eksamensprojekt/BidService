public class QueueNameProvider
{
    private readonly ILogger<QueueNameProvider> _logger;
    private string? _activeQueueName;
    private string? _activeItemId;

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
    
    public void SetActiveItemId(string itemId)
    {
        _logger.LogInformation("Setting active item id to {ItemId}.", itemId);
        _activeItemId = itemId;
    }

    public string? GetActiveItemId()
    {
        if (string.IsNullOrEmpty(_activeItemId))
        {
            _logger.LogWarning("No active item id is set.");
        }
        return _activeItemId;
    }
}
