namespace CodexVsix.Models;

public sealed class CodexRemoteSkillSummary
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string MarketplaceName { get; set; } = string.Empty;

    public string PluginName { get; set; } = string.Empty;

    public string RemotePluginId { get; set; } = string.Empty;

    public string SkillName { get; set; } = string.Empty;

    [Newtonsoft.Json.JsonIgnore]
    public bool UsesPluginMarketplace => !string.IsNullOrWhiteSpace(PluginName);
}
