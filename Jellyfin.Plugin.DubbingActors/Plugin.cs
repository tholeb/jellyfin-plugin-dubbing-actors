using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Plugin.DubbingActors.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using Jellyfin.Plugin.DubbingActors.Services;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;

namespace Jellyfin.Plugin.DubbingActors;

/// <summary>
/// The main plugin.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
    /// <param name="xmlSerializer">Instance of the <see cref="IXmlSerializer"/> interface.</param>
    /// <param name="xmlSerializer">Instance of the <see cref="IXmlSerializer"/> interface.</param>
    private readonly ILibraryManager _libraryManager;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<WikidataService> _logger;

    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer, ILibraryManager libraryManager, IHttpClientFactory httpClientFactory, ILogger<WikidataService> logger)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
        _libraryManager = libraryManager;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _libraryManager.ItemUpdated += OnItemUpdated;
    }

    private async void OnItemUpdated(object? sender, ItemChangeEventArgs e)
    {
        // We only care about movies and series that have a TMDb ID
        if (e.Item is not Movie && e.Item is not Series)
        {
            return;
        }

        // Avoid infinite loop if we trigger an update
        if (e.UpdateReason == ItemUpdateType.None || e.UpdateReason == ItemUpdateType.MetadataImport)
        {
            // If it's a MetadataImport, it could be us, or another plugin. We might need to handle it properly.
        }

        try
        {
            await Task.Run(() => UpdateDubbingActorsAsync(e.Item, CancellationToken.None)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error running Dubbing Actors update on item {Id}", e.Item.Id);
        }
    }

    private async Task UpdateDubbingActorsAsync(BaseItem item, CancellationToken cancellationToken)
    {
        var config = Configuration;
        if (config == null || string.IsNullOrWhiteSpace(config.TargetLanguage))
        {
            return;
        }

        if (!item.ProviderIds.TryGetValue("Tmdb", out var tmdbId) || string.IsNullOrEmpty(tmdbId))
        {
            return;
        }

        // We fetch the cast from WikidataService using a transient instance or we can inject it.
        var wikidataService = new WikidataService(_httpClientFactory, _logger, ApplicationPaths);
        var itemType = item is Movie ? "Movie" : (item is Series ? "Series" : "Unknown");
        var cast = await wikidataService.GetDubbingCastAsync(tmdbId, itemType, cancellationToken).ConfigureAwait(false);
        
        if (cast == null || cast.Cast.Count == 0)
        {
            return;
        }

        var rolePrefix = string.Format(System.Globalization.CultureInfo.InvariantCulture, config.RolePrefix, config.TargetLanguage.ToUpperInvariant());
        var existingPeople = GetPeopleReflection(item);
        var newPeople = existingPeople.ToList();
        bool updated = false;

        foreach (var actor in cast.Cast)
        {
            var role = rolePrefix + actor.Role;
            if (!newPeople.Any(p => p.Name == actor.Actor && p.Role == role))
            {
                var personInfo = new PersonInfo
                {
                    Name = actor.Actor,
                    Role = role,
                    Type = Jellyfin.Data.Enums.PersonKind.Actor
                };
                newPeople.Add(personInfo);
                updated = true;
            }
        }

        if (updated)
        {
            _logger.LogInformation("Injecting {Count} dubbing actors into {ItemName}", newPeople.Count - existingPeople.Count, item.Name);
            // This updates the database instantly. Because this runs AFTER the refresh pipeline finishes, it won't be overwritten!
            UpdatePeopleReflection(item, newPeople);
        }
    }

    private List<PersonInfo> GetPeopleReflection(BaseItem item)
    {
        try
        {
            var method = typeof(ILibraryManager).GetMethod("GetPeople", new[] { typeof(BaseItem) });
            if (method != null)
            {
                var result = method.Invoke(_libraryManager, new object[] { item });
                if (result is IEnumerable<PersonInfo> enumerable)
                {
                    return enumerable.ToList();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get people via reflection.");
        }
        return new List<PersonInfo>();
    }

    private void UpdatePeopleReflection(BaseItem item, List<PersonInfo> people)
    {
        try
        {
            var method = typeof(ILibraryManager).GetMethod("UpdatePeople");
            if (method != null)
            {
                var parameters = method.GetParameters();
                if (parameters.Length == 2)
                {
                    var paramType = parameters[1].ParameterType;
                    if (paramType.IsArray)
                    {
                        method.Invoke(_libraryManager, new object[] { item, people.ToArray() });
                    }
                    else
                    {
                        method.Invoke(_libraryManager, new object[] { item, people });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update people via reflection.");
        }
    }

    /// <inheritdoc />
    public override string Name => "Dubbing Actors";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("370603f2-1a41-4560-a299-17d4726cd55b");

    /// <summary>
    /// Gets the current plugin instance.
    /// </summary>
    public static Plugin? Instance { get; private set; }

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return
        [
            new PluginPageInfo
            {
                Name = Name,
                EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Web.configPage.html", GetType().Namespace)
            }
        ];
    }
}
