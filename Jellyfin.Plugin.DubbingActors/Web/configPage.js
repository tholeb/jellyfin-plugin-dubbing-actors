import { html, render, useState, useEffect } from 'https://unpkg.com/htm/preact/standalone.module.js';

const ApiClient = window.ApiClient;
const pluginId = '370603f2-1a41-4560-a299-17d4726cd55b';

function App() {
    const [config, setConfig] = useState(null);
    const [cache, setCache] = useState([]);
    const [loading, setLoading] = useState(true);
    const [saving, setSaving] = useState(false);
    const [activeTab, setActiveTab] = useState('Movie');
    const [selectedItem, setSelectedItem] = useState(null);

    useEffect(() => {
        Promise.all([
            ApiClient.getPluginConfiguration(pluginId),
            ApiClient.getJSON(ApiClient.getUrl('DubbingActors/Cache'))
        ]).then(([pluginConfig, cacheData]) => {
            setConfig(pluginConfig);
            setCache(cacheData);
            setLoading(false);
        }).catch(err => {
            console.error('Error loading config', err);
            setLoading(false);
        });
    }, []);

    const handleSave = async (e) => {
        e.preventDefault();
        setSaving(true);
        try {
            await ApiClient.updatePluginConfiguration(pluginId, config);
            window.Dashboard.processPluginConfigurationUpdateResult();
        } catch (err) {
            console.error('Error saving config', err);
        }
        setSaving(false);
    };

    const handleDelete = async (tmdbId) => {
        if (!confirm('Are you sure you want to delete this cache item?')) return;
        try {
            await ApiClient.ajax({
                type: 'DELETE',
                url: ApiClient.getUrl(`DubbingActors/Cache/${tmdbId}`)
            });
            setCache(cache.filter(c => c.TmdbId !== tmdbId));
        } catch (err) {
            console.error('Error deleting cache item', err);
        }
    };

    const viewCast = (tmdbId) => {
        const cachedItem = cache.find(c => c.TmdbId === tmdbId);
        if (cachedItem.CastDetailsLoaded) {
            setSelectedItem(cachedItem);
        } else {
            ApiClient.getJSON(ApiClient.getUrl('DubbingActors/Cache/' + tmdbId)).then(fullItem => {
                const updatedCache = cache.map(c => c.TmdbId === tmdbId ? { ...c, CastDetails: fullItem.Cast, CastDetailsLoaded: true } : c);
                setCache(updatedCache);
                setSelectedItem(updatedCache.find(c => c.TmdbId === tmdbId));
            });
        }
    };

    if (loading) {
        return html`<div class="da-header">Loading...</div>`;
    }

    const filteredCache = cache.filter(c => c.ItemType === activeTab || (activeTab === 'Movie' && c.ItemType === 'Unknown'));

    return html`
        <div>
            <style>
                .da-tabs { margin-bottom: 20px; border-bottom: 1px solid #333; display: flex; gap: 10px; }
                .da-tab { padding: 10px 20px; cursor: pointer; background: transparent; border: none; color: #aaa; font-size: 1.1em; }
                .da-tab.active { color: #fff; border-bottom: 2px solid #00a4dc; font-weight: bold; }
                .da-modal-overlay { position: fixed; top:0; left:0; width:100%; height:100%; background:rgba(0,0,0,0.8); z-index:9999; display:flex; justify-content:center; align-items:center; }
                .da-modal { background: #222; padding: 20px; border-radius: 8px; width: 500px; max-width: 90%; max-height: 80vh; overflow-y: auto; color: #fff; box-shadow: 0 5px 15px rgba(0,0,0,0.5); }
                .da-modal h3 { margin-top: 0; color: #00a4dc; }
                .da-modal-close { float: right; background: none; border: none; color: #fff; cursor: pointer; font-size: 1.5em; line-height: 1; }
                .da-cast-list { list-style: none; padding: 0; margin: 15px 0; }
                .da-cast-list li { padding: 8px; border-bottom: 1px solid #333; display: flex; justify-content: space-between; }
                .da-cast-list li:last-child { border-bottom: none; }
                .da-role { color: #888; font-style: italic; }
            </style>
            <h1 class="da-header">Dubbing Actors</h1>
            
            <div class="da-section">
                <h2>Configuration</h2>
                <form onSubmit=${handleSave}>
                    <div class="da-form-row">
                        <label>Target Language (e.g., fr):</label>
                        <input type="text" value=${config.TargetLanguage} onInput=${e => setConfig({...config, TargetLanguage: e.target.value})} required />
                    </div>
                    <div class="da-form-row">
                        <label>Role Prefix (Use {0} for language):</label>
                        <input type="text" value=${config.RolePrefix} onInput=${e => setConfig({...config, RolePrefix: e.target.value})} required />
                    </div>
                    <div class="da-form-row">
                        <label>Wikipedia Base URL:</label>
                        <input type="url" value=${config.WikipediaBaseUrl} onInput=${e => setConfig({...config, WikipediaBaseUrl: e.target.value})} required />
                    </div>
                    <div class="da-form-row">
                        <label>Extraction Regex (Advanced):</label>
                        <input type="text" value=${config.ExtractionRegex} onInput=${e => setConfig({...config, ExtractionRegex: e.target.value})} required style="font-family: monospace;" />
                    </div>
                    <div class="da-form-row">
                        <label>Rate Limit Delay (ms):</label>
                        <input type="number" value=${config.RateLimitDelayMs} onInput=${e => setConfig({...config, RateLimitDelayMs: parseInt(e.target.value)})} required min="0" />
                    </div>
                    <button type="submit" disabled=${saving}>${saving ? 'Saving...' : 'Save Settings'}</button>
                </form>
            </div>

            <div class="da-section">
                <h2>Cached Items</h2>
                <div class="da-tabs">
                    <button class="da-tab ${activeTab === 'Movie' ? 'active' : ''}" onClick=${() => setActiveTab('Movie')}>Movies</button>
                    <button class="da-tab ${activeTab === 'Series' ? 'active' : ''}" onClick=${() => setActiveTab('Series')}>TV Shows</button>
                </div>
                <table class="da-cache-table">
                    <thead>
                        <tr>
                            <th>TMDb ID</th>
                            <th>Article Title</th>
                            <th>Actors Found</th>
                            <th>Actions</th>
                        </tr>
                    </thead>
                    <tbody>
                        ${filteredCache.map(item => html`
                            <tr key=${item.TmdbId}>
                                <td>${item.TmdbId}</td>
                                <td>
                                    ${item.ArticleTitle ? html`
                                        <a href="${config.WikipediaBaseUrl}wiki/${encodeURIComponent(item.ArticleTitle.replace(/ /g, '_'))}" target="_blank" style="color:#00a4dc; text-decoration:none;">
                                            ${item.ArticleTitle} ↗
                                        </a>
                                    ` : 'N/A'}
                                </td>
                                <td>${item.CastCount}</td>
                                <td>
                                    <button type="button" style="margin-right:10px;" onClick=${() => viewCast(item.TmdbId)}>View Cast</button>
                                    <button type="button" class="da-danger" onClick=${() => handleDelete(item.TmdbId)}>Delete</button>
                                </td>
                            </tr>
                        `)}
                        ${filteredCache.length === 0 && html`<tr><td colspan="4" style="text-align:center;">No items found.</td></tr>`}
                    </tbody>
                </table>
            </div>

            ${selectedItem && html`
                <div class="da-modal-overlay" onClick=${(e) => { if (e.target.className === 'da-modal-overlay') setSelectedItem(null); }}>
                    <div class="da-modal">
                        <button class="da-modal-close" onClick=${() => setSelectedItem(null)}>&times;</button>
                        <h3>${selectedItem.ArticleTitle}</h3>
                        <p>Total Actors Extracted: ${selectedItem.CastDetails ? selectedItem.CastDetails.length : 0}</p>
                        <ul class="da-cast-list">
                            ${selectedItem.CastDetails && selectedItem.CastDetails.length > 0 ? selectedItem.CastDetails.map(c => html`
                                <li>
                                    <strong>${c.Actor}</strong>
                                    <span class="da-role">${c.Role}</span>
                                </li>
                            `) : html`<li>No cast details available</li>`}
                        </ul>
                    </div>
                </div>
            `}
        </div>
    `;
}

// Attach the render to a global function so it can be called from the HTML page
window.renderDubbingActorsApp = function(container) {
    render(html`<${App} />`, container);
};
