// Map of the cinemas, drawn with Leaflet from the cinemas the API returns. There is no built-in fallback list:
// if the API has no cinemas, the page says so instead of showing places that are not in the data.
import { loadCatalog } from './data.js';
import { t } from './i18n.js';
import { esc, ICONS } from './util.js';
import { cinemaColor, cinemaLogo } from './cinema-theme.js';

// Tile providers: Esri dark gray, which needs no key, with an automatic switch to OpenStreetMap. Esri Dark Gray
// stops at zoom 16: maxNativeZoom avoids the "Map data not yet available" tiles beyond it.
const TILE_PRIMARY = 'https://server.arcgisonline.com/ArcGIS/rest/services/Canvas/World_Dark_Gray_Base/MapServer/tile/{z}/{y}/{x}';
const ATTR_PRIMARY = 'Tiles &copy; Esri &mdash; Source: Esri, Maxar, Earthstar Geographics';
const TILE_FALLBACK = 'https://tile.openstreetmap.org/{z}/{x}/{y}.png';
const ATTR_FALLBACK = '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors';

export async function renderMappa(view, { showLoading, showError, showNoData }) {
  showLoading(view, t('state.loadingMap'));
  let catalog;
  try {
    catalog = await loadCatalog();
  } catch (e) {
    showError(view, e, () => renderMappa(view, { showLoading, showError, showNoData }));
    return;
  }

  const points = catalog.cinemas
    .filter((c) => Number.isFinite(c.latitude) && Number.isFinite(c.longitude))
    .map((c) => ({ ...c, color: cinemaColor(c.slug), logo: cinemaLogo(c.slug) }));
  if (!points.length) {
    showNoData(view, () => renderMappa(view, { showLoading, showError, showNoData }));
    return;
  }

  const leaflet = window.L;
  view.innerHTML = `
    ${leaflet ? `<div id="map" role="application" aria-label="${esc(t('map.label'))}"></div>` : `<p class="state-sub">${esc(t('map.unavailable'))}</p>`}
    <div class="map-legend" aria-label="${esc(t('map.legend'))}">
      ${points.map((c) => `
        <button class="legend-chip" data-focus="${esc(c.slug)}" aria-label="${esc(t('map.show', { name: c.name }))}">
          <span class="legend-dot" style="background:${c.color}" aria-hidden="true">${esc(c.name[0])}</span>${esc(c.name)}
        </button>`).join('')}
    </div>
    <div class="cinema-list">
      ${points.map((c) => `
        <div class="cinema-card map-focus" data-focus="${esc(c.slug)}" role="button" tabindex="0" aria-label="${esc(t('map.show', { name: c.name }))}">
          <strong>${esc(c.name)}</strong><br />
          <span class="state-sub">${esc(c.address || '')}</span><br />
          <a class="btn ghost link-btn" target="_blank" rel="noopener" href="https://www.google.com/maps/search/?api=1&query=${c.latitude},${c.longitude}">${esc(t('map.openInMaps'))} ${ICONS.arrowR}</a>
        </div>`).join('')}
    </div>`;

  if (!leaflet) return;

  const map = leaflet.map('map', { scrollWheelZoom: false });
  const layer = leaflet.tileLayer(TILE_PRIMARY, { attribution: ATTR_PRIMARY, maxNativeZoom: 16, maxZoom: 19 });
  layer.on('tileerror', () => {
    // After repeated errors (provider down or policy changed) switch to OpenStreetMap, once.
    layer._errCount = (layer._errCount || 0) + 1;
    if (layer._errCount >= 6 && !layer._fallbackActive) {
      layer._fallbackActive = true;
      map.attributionControl.removeAttribution(ATTR_PRIMARY);
      map.attributionControl.addAttribution(ATTR_FALLBACK);
      layer.setUrl(TILE_FALLBACK);
    }
  });
  layer.addTo(map);

  // Nearby cinemas collapse into a numbered circle at low zoom. Without the plugin a plain layer group is used.
  const group = typeof leaflet.markerClusterGroup === 'function'
    ? leaflet.markerClusterGroup({
      showCoverageOnHover: false,
      maxClusterRadius: 60,
      iconCreateFunction: (cluster) => leaflet.divIcon({
        html: `<div class="cp-cluster"><span>${cluster.getChildCount()}</span></div>`,
        className: 'cp-cluster-wrap',
        iconSize: [44, 44],
      }),
    })
    : leaflet.layerGroup();
  group.addTo(map);

  const markers = {};
  const bounds = [];
  for (const c of points) {
    const icon = leaflet.divIcon({
      className: 'cp-marker',
      html: `<div style="position:relative;width:34px;height:34px;border-radius:50%;background:#1f1f1f;border:3px solid ${c.color};overflow:hidden;display:flex;align-items:center;justify-content:center;color:#fff;font-weight:800">`
        + (c.logo ? `<img src="${esc(c.logo)}" alt="" style="position:absolute;inset:0;width:100%;height:100%;object-fit:cover" onerror="this.remove()" />` : '')
        + `<span>${esc(c.name[0])}</span></div>`,
      iconSize: [34, 34], iconAnchor: [17, 17], popupAnchor: [0, -18],
    });
    const marker = leaflet.marker([c.latitude, c.longitude], { icon })
      .bindTooltip(esc(c.name), { direction: 'top', offset: [0, -20], className: 'cp-tip' })
      .bindPopup(`<strong>${esc(c.name)}</strong><br>${esc(c.address || '')}`);
    group.addLayer(marker);
    markers[c.slug] = marker;
    bounds.push([c.latitude, c.longitude]);
  }
  map.fitBounds(bounds, { padding: [20, 20], maxZoom: 15 });

  // Clicking the legend or a card centers the marker (opening its cluster) and shows the popup.
  function focusCinema(slug) {
    const marker = markers[slug];
    if (!marker) return;
    document.getElementById('map')?.scrollIntoView({ behavior: 'smooth', block: 'center' });
    if (typeof group.zoomToShowLayer === 'function') group.zoomToShowLayer(marker, () => marker.openPopup());
    else { map.flyTo(marker.getLatLng(), Math.max(map.getZoom(), 14), { duration: 0.6 }); marker.openPopup(); }
  }
  view.querySelectorAll('[data-focus]').forEach((el) => {
    el.addEventListener('click', (e) => {
      if (e.target.closest('a')) return; // the Google Maps link stays a link
      focusCinema(el.dataset.focus);
    });
    el.addEventListener('keydown', (e) => {
      if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); focusCinema(el.dataset.focus); }
    });
  });

  map.on('focus', () => map.scrollWheelZoom.enable());
  map.on('blur', () => map.scrollWheelZoom.disable());
  view._cleanup = () => map.remove();
}
