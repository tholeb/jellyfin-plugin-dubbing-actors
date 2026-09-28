using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.DubbingActors.Configuration;

/// <summary>
/// Plugin configuration.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PluginConfiguration"/> class.
    /// </summary>
    public PluginConfiguration()
    {
        TargetLanguage = "fr";
        WikipediaBaseUrl = "https://fr.wikipedia.org/";
        RateLimitDelayMs = 1000;
        RolePrefix = "Voix {0}: ";
        ExtractionRegex = @"=+\s*Voix fran[cç]aises\s*=+\n(.*?)(?=\n=+|\Z)";
    }

    /// <summary>
    /// Gets or sets the target language (e.g., "fr").
    /// </summary>
    public string TargetLanguage { get; set; }

    /// <summary>
    /// Gets or sets the Wikipedia Base URL.
    /// </summary>
    public string WikipediaBaseUrl { get; set; }

    /// <summary>
    /// Gets or sets the rate limit delay in milliseconds.
    /// </summary>
    public int RateLimitDelayMs { get; set; }

    /// <summary>
    /// Gets or sets the role prefix string. {0} will be replaced with the TargetLanguage.
    /// </summary>
    public string RolePrefix { get; set; }

    /// <summary>
    /// Gets or sets the regular expression used to extract the cast section from Wikipedia.
    /// </summary>
    public string ExtractionRegex { get; set; }
}
