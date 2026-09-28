using System.Collections.Generic;

namespace Jellyfin.Plugin.DubbingActors.Models;

public class CachedDubbingData
{
    public string TmdbId { get; set; } = string.Empty;
    public string ArticleTitle { get; set; } = string.Empty;
    public string ItemType { get; set; } = string.Empty;
    public List<DubbingCast> Cast { get; set; } = new List<DubbingCast>();
    public bool Scraped { get; set; }
}

public class DubbingCast
{
    public string Actor { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
}
