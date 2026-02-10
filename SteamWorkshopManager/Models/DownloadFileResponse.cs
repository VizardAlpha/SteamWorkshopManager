using System.Text.Json.Serialization;

namespace SteamWorkshopManager.Models;

public class DownloadFileResponse
{
    [JsonPropertyName("success")]
    public int Success { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("filename")]
    public string? Filename { get; set; }
}
