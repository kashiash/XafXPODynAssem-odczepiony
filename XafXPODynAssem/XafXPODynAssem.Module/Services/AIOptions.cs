namespace XafXPODynAssem.Module.Services;

public class AIOptions
{
    public string Model { get; set; } = "claude-sonnet-4-6";
    public string DefaultProvider { get; set; } = "anthropic";
    public Dictionary<string, string> ApiKeys { get; set; } = new();
    /// <summary>Dowolny endpoint zgodny z OpenAI (np. Azure OpenAI). Pusty = publiczne API dostawcy.</summary>
    public string BaseUrl { get; set; } = "";
    public int MaxOutputTokens { get; set; } = 16384;
    public int MaxToolIterations { get; set; } = 10;
    public int TimeoutSeconds { get; set; } = 120;
}
