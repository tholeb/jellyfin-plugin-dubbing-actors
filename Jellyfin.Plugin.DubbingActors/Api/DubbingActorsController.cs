using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Jellyfin.Plugin.DubbingActors.Models;
using Jellyfin.Plugin.DubbingActors.Services;
using MediaBrowser.Common.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.DubbingActors.Api;

[ApiController]
[Route("DubbingActors/Cache")]
[Authorize]
public class DubbingActorsController : ControllerBase
{
    private readonly WikidataService _wikidataService;

    public DubbingActorsController(IHttpClientFactory httpClientFactory, ILogger<WikidataService> logger, IApplicationPaths appPaths)
    {
        _wikidataService = new WikidataService(httpClientFactory, logger, appPaths);
    }

    [HttpGet]
    public ActionResult GetCache()
    {
        var summaries = System.Linq.Enumerable.Select(_wikidataService.GetAllCachedData(), c => new {
            c.TmdbId,
            c.ArticleTitle,
            ItemType = c.ItemType ?? "Unknown",
            CastCount = c.Cast != null ? c.Cast.Count : 0,
            c.Scraped
        });
        return Ok(summaries);
    }

    [HttpGet("{tmdbId}")]
    public ActionResult<CachedDubbingData> GetCacheItem(string tmdbId)
    {
        var item = System.Linq.Enumerable.FirstOrDefault(_wikidataService.GetAllCachedData(), c => c.TmdbId == tmdbId);
        if (item == null) return NotFound();
        return Ok(item);
    }

    [HttpDelete("{tmdbId}")]
    public async Task<ActionResult> DeleteCacheItem(string tmdbId)
    {
        await _wikidataService.DeleteCachedDataAsync(tmdbId).ConfigureAwait(false);
        return NoContent();
    }

    [HttpGet("Script")]
    [AllowAnonymous] // Scripts should be accessible without auth to load in the config page easily
    public ActionResult GetConfigScript()
    {
        var assembly = typeof(DubbingActorsController).Assembly;
        using var stream = assembly.GetManifestResourceStream("Jellyfin.Plugin.DubbingActors.Web.configPage.js");
        if (stream == null)
        {
            return NotFound();
        }
        using var reader = new System.IO.StreamReader(stream);
        var script = reader.ReadToEnd();
        return Content(script, "application/javascript");
    }
}
