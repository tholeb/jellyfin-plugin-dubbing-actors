using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.DubbingActors.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.DubbingActors.Providers;

public class DubbingActorsCustomMetadataProvider : ICustomMetadataProvider<Movie>, ICustomMetadataProvider<Series>
{
    private readonly WikidataService _wikidataService;
    private readonly ILibraryManager _libraryManager;

    public DubbingActorsCustomMetadataProvider(IHttpClientFactory httpClientFactory, ILogger<WikidataService> logger, IApplicationPaths appPaths, ILibraryManager libraryManager)
    {
        _wikidataService = new WikidataService(httpClientFactory, logger, appPaths);
        _libraryManager = libraryManager;
    }

    public string Name => "Dubbing Actors Metadata Provider";

    public async Task<ItemUpdateType> FetchAsync(Movie item, MetadataRefreshOptions options, CancellationToken cancellationToken)
    {
        return await FetchInternalAsync(item, item.OriginalTitle, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ItemUpdateType> FetchAsync(Series item, MetadataRefreshOptions options, CancellationToken cancellationToken)
    {
        return await FetchInternalAsync(item, item.OriginalTitle, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ItemUpdateType> FetchInternalAsync(BaseItem item, string? originalLanguage, CancellationToken cancellationToken)
    {
        // The actual logic has been moved to Plugin.cs OnItemUpdated event 
        // to prevent Jellyfin's core refresh task from overwriting our injected cast list.
        return await Task.FromResult(ItemUpdateType.None).ConfigureAwait(false);
    }
}
