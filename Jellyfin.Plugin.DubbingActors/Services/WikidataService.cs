using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.DubbingActors.Models;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.DubbingActors.Services;

public class WikidataService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<WikidataService> _logger;
    private readonly string _cacheFilePath;
    private static readonly ConcurrentDictionary<string, CachedDubbingData> _cache = new ConcurrentDictionary<string, CachedDubbingData>();
    private static readonly SemaphoreSlim _cacheLock = new SemaphoreSlim(1, 1);
    private static DateTime _lastRequestTime = DateTime.MinValue;
    private static readonly SemaphoreSlim _rateLimitLock = new SemaphoreSlim(1, 1);
    private static bool _cacheLoaded = false;
    private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions { WriteIndented = true };

    public WikidataService(IHttpClientFactory httpClientFactory, ILogger<WikidataService> logger, IApplicationPaths appPaths)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _cacheFilePath = Path.Combine(appPaths.PluginConfigurationsPath, "DubbingActorsCache.json");
        if (!_cacheLoaded)
        {
            LoadCache();
            _cacheLoaded = true;
        }
    }

    private void LoadCache()
    {
        if (File.Exists(_cacheFilePath))
        {
            try
            {
                var json = File.ReadAllText(_cacheFilePath);
                var data = JsonSerializer.Deserialize<Dictionary<string, CachedDubbingData>>(json);
                if (data != null)
                {
                    foreach (var kvp in data)
                    {
                        _cache.TryAdd(kvp.Key, kvp.Value);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading DubbingActors cache.");
            }
        }
    }

    public async Task SaveCacheAsync()
    {
        await _cacheLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var json = JsonSerializer.Serialize(_cache.ToDictionary(k => k.Key, v => v.Value), _jsonOptions);
            await File.WriteAllTextAsync(_cacheFilePath, json).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving DubbingActors cache.");
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    public IEnumerable<CachedDubbingData> GetAllCachedData()
    {
        return _cache.Values;
    }

    public async Task DeleteCachedDataAsync(string tmdbId)
    {
        if (_cache.TryRemove(tmdbId, out _))
        {
            await SaveCacheAsync().ConfigureAwait(false);
        }
    }

    private async Task EnforceRateLimitAsync()
    {
        await _rateLimitLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var delayMs = Plugin.Instance?.Configuration.RateLimitDelayMs ?? 1000;
            var now = DateTime.UtcNow;
            var timeSinceLast = (now - _lastRequestTime).TotalMilliseconds;
            if (timeSinceLast < delayMs)
            {
                await Task.Delay(delayMs - (int)timeSinceLast).ConfigureAwait(false);
            }
            _lastRequestTime = DateTime.UtcNow;
        }
        finally
        {
            _rateLimitLock.Release();
        }
    }

    public async Task<CachedDubbingData?> GetDubbingCastAsync(string tmdbId, string itemType, CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(tmdbId, out var cached))
        {
            return cached;
        }

        var result = new CachedDubbingData { TmdbId = tmdbId, ItemType = itemType, Scraped = true };

        try
        {
            await EnforceRateLimitAsync().ConfigureAwait(false);

            var httpClient = _httpClientFactory.CreateClient();
            httpClient.DefaultRequestHeaders.Add("User-Agent", "JellyfinVFScraper/4.0 (Jellyfin Plugin)");

            // Query Wikidata
            var sparqlQuery = $@"
            SELECT ?article WHERE {{
              {{ ?item wdt:P4947 ""{tmdbId}"" . }}
              UNION
              {{ ?item wdt:P4983 ""{tmdbId}"" . }}
              ?article schema:about ?item ;
                       schema:inLanguage ""fr"" ;
                       schema:isPartOf <https://fr.wikipedia.org/> .
            }}";

            var wdUrl = $"https://query.wikidata.org/sparql?query={Uri.EscapeDataString(sparqlQuery)}&format=json";
            var wdResponse = await httpClient.GetAsync(wdUrl, cancellationToken).ConfigureAwait(false);
            wdResponse.EnsureSuccessStatusCode();

            var wdJson = await wdResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var wdDoc = JsonDocument.Parse(wdJson);
            var bindings = wdDoc.RootElement.GetProperty("results").GetProperty("bindings");
            
            if (bindings.GetArrayLength() == 0)
            {
                _logger.LogInformation("No Wikipedia FR article found for TMDb ID: {TmdbId}", tmdbId);
                _cache.TryAdd(tmdbId, result);
                await SaveCacheAsync().ConfigureAwait(false);
                return result;
            }

            var articleUrl = bindings[0].GetProperty("article").GetProperty("value").GetString();
            if (string.IsNullOrEmpty(articleUrl))
            {
                _cache.TryAdd(tmdbId, result);
                await SaveCacheAsync().ConfigureAwait(false);
                return result;
            }

            var articleTitle = Uri.UnescapeDataString(articleUrl.Split('/').Last()).Replace('_', ' ');
            result.ArticleTitle = articleTitle;

            _logger.LogInformation("Found article {ArticleTitle} for TMDb ID: {TmdbId}", articleTitle, tmdbId);

            await EnforceRateLimitAsync().ConfigureAwait(false);

            // Fetch Wikipedia source
            var wikiApiUrl = $"https://fr.wikipedia.org/w/api.php?action=query&prop=revisions&rvprop=content&rvslots=main&titles={Uri.EscapeDataString(articleTitle)}&format=json";
            var wikiResponse = await httpClient.GetAsync(wikiApiUrl, cancellationToken).ConfigureAwait(false);
            wikiResponse.EnsureSuccessStatusCode();

            var wikiJson = await wikiResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var wikiDoc = JsonDocument.Parse(wikiJson);
            var pages = wikiDoc.RootElement.GetProperty("query").GetProperty("pages");
            
            var pagesEnum = pages.EnumerateObject();
            if (!pagesEnum.MoveNext())
            {
                _cache.TryAdd(tmdbId, result);
                await SaveCacheAsync().ConfigureAwait(false);
                return result;
            }
            
            var page = pagesEnum.Current.Value;
            if (!page.TryGetProperty("revisions", out var revisions) || revisions.GetArrayLength() == 0)
            {
                _cache.TryAdd(tmdbId, result);
                await SaveCacheAsync().ConfigureAwait(false);
                return result;
            }

            var wikitext = revisions[0].GetProperty("slots").GetProperty("main").GetProperty("*").GetString();
            if (string.IsNullOrEmpty(wikitext))
            {
                _cache.TryAdd(tmdbId, result);
                await SaveCacheAsync().ConfigureAwait(false);
                return result;
            }

            var regexPattern = Plugin.Instance?.Configuration.ExtractionRegex ?? @"=+\s*Voix fran[cç]aises\s*=+\n(.*?)(?=\n=+|\Z)";
            var match = Regex.Match(wikitext, regexPattern, RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                _logger.LogInformation("No 'Voix françaises' section found for TMDb ID: {TmdbId}", tmdbId);
                _cache.TryAdd(tmdbId, result);
                await SaveCacheAsync().ConfigureAwait(false);
                return result;
            }

            var vfText = match.Groups[1].Value.Trim();
            foreach (var line in vfText.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith('*'))
                {
                    var cleanLine = Regex.Replace(trimmed, @"<ref[^>]*>.*?</ref>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                    cleanLine = Regex.Replace(cleanLine, @"<ref[^>]*/>", "", RegexOptions.IgnoreCase);
                    cleanLine = Regex.Replace(cleanLine, @"\[\[(?:[^|\]]*\|)?([^\]]+)\]\]", "$1");
                    cleanLine = cleanLine.Replace("'''", "", StringComparison.OrdinalIgnoreCase).Replace("''", "", StringComparison.OrdinalIgnoreCase).Replace("*", "", StringComparison.OrdinalIgnoreCase).Trim();
                    cleanLine = Regex.Replace(cleanLine, @"\{\{[^}]+\}\}", "");
                    cleanLine = cleanLine.Replace("}}", "", StringComparison.OrdinalIgnoreCase).Replace("{{", "", StringComparison.OrdinalIgnoreCase).Trim();
                    cleanLine = cleanLine.Replace("&nbsp;", " ", StringComparison.OrdinalIgnoreCase);

                    if (!string.IsNullOrWhiteSpace(cleanLine))
                    {
                        var parts = cleanLine.Split(':', 2);
                        if (parts.Length == 2)
                        {
                            result.Cast.Add(new DubbingCast { Actor = parts[0].Trim(), Role = parts[1].Trim() });
                        }
                        else
                        {
                            result.Cast.Add(new DubbingCast { Actor = cleanLine, Role = "" });
                        }
                    }
                }
            }

            _cache.TryAdd(tmdbId, result);
            await SaveCacheAsync().ConfigureAwait(false);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching dubbing cast for TMDb ID: {TmdbId}", tmdbId);
            return null; // Don't cache errors
        }
    }
}
