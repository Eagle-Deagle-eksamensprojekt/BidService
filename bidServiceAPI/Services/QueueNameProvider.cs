public class QueueNameProvider
{
    private readonly ILogger<QueueNameProvider> _logger;
    private string? _activeQueueName;
    private string? _activeItemId;

    public QueueNameProvider(ILogger<QueueNameProvider> logger)
    {
        _logger = logger; // Set the logger
    }

    public void SetActiveQueueName(string queueName)
    {
        _logger.LogInformation("Setting active queue name to {QueueName}.", queueName);
        _activeQueueName = queueName; // Set the active queue name
    }

    public string? GetActiveQueueName() 
    {
        if (string.IsNullOrEmpty(_activeQueueName))
        {
            _logger.LogWarning("No active queue name is set.");
        }
        return _activeQueueName; // Return the active queue name
    }
    
    public void SetActiveItemId(string itemId) // Set the active item id
    {
        _logger.LogInformation("Setting active item id to {ItemId}.", itemId);
        _activeItemId = itemId; // Set the active item id
    }

    public string? GetActiveItemId() // Get the active item id
    {
        if (string.IsNullOrEmpty(_activeItemId))
        {
            _logger.LogWarning("No active item id is set.");
        }
        return _activeItemId; // Return the active item id
    }
}
