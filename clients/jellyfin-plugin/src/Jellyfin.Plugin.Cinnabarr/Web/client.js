/*
 * Cinnabarr inside the Jellyfin web app (and the LG webOS app, which loads it from the server).
 *
 * Adds a Cinnabarr button to Jellyfin's header for users the plugin gives a role to, and a full-screen overlay
 * with Library, Discover and Search. Everything goes through the plugin's /Cinnabarr endpoints with the user's own
 * Jellyfin session; the Cinnabarr API key never reaches this page.
 *
 * Plain ES2019 (no ?. or ??, no modules) so the webOS 6 web engine runs it: check with check-es2019.cjs.
 * Remote and controller: arrows / d-pad / left stick move, OK / A select, Back / B go back.
 */
(function () {
    'use strict';

    if (window.__cinnabarr) {
        return;
    }

    var BUTTON_CLASS = 'cnb-headerButton';
    var KEYS = { 37: 'left', 38: 'up', 39: 'right', 40: 'down', 13: 'ok', 461: 'back', 27: 'back', 8: 'back', 10009: 'back' };
    var STATUS_TEXT = {
        notInLibrary: 'Not in library',
        wanted: 'Requested',
        unmonitored: 'In library',
        downloading: 'Downloading',
        partial: 'Partly here',
        available: 'Available'
    };

    var me = null;
    var meForUser = null;
    var overlay = null;
    var body = null;
    var tabsEl = null;
    var toastEl = null;
    var detailEl = null;
    var lastFocusOutside = null;
    var screen = 'library';
    var screenState = {
        library: { filter: 'all', items: null },
        discover: { list: 'trending', items: null },
        search: { type: 'movie', term: '', items: null }
    };

    // ---- Jellyfin session ----

    function apiClient() {
        return window.ApiClient || null;
    }

    function token() {
        var c = apiClient();
        return c && typeof c.accessToken === 'function' ? c.accessToken() : null;
    }

    function userId() {
        var c = apiClient();
        return c && typeof c.getCurrentUserId === 'function' ? c.getCurrentUserId() : null;
    }

    function endpoint(path) {
        return apiClient().getUrl('Cinnabarr/' + path);
    }

    function api(path, options) {
        options = options || {};
        var headers = { 'Authorization': 'MediaBrowser Token="' + token() + '"', 'Accept': 'application/json' };
        if (options.body) {
            headers['Content-Type'] = 'application/json';
        }

        return fetch(endpoint(path), { method: options.method || 'GET', headers: headers, body: options.body ? JSON.stringify(options.body) : undefined })
            .then(function (response) {
                return response.text().then(function (text) {
                    var data = null;
                    try {
                        data = text ? JSON.parse(text) : null;
                    } catch (e) {
                        data = null;
                    }
                    if (!response.ok) {
                        var message = data && (data.message || data.Message);
                        if (!message && data && data.length && data[0] && data[0].errorMessage) {
                            message = data[0].errorMessage;
                        }
                        throw new Error(message || ('Cinnabarr answered ' + response.status + '.'));
                    }
                    return data;
                });
            });
    }

    // Images can't send a header, so the session token goes in the URL (Jellyfin accepts ApiKey=).
    function imageUrl(path) {
        return endpoint(path) + (path.indexOf('?') < 0 ? '?' : '&') + 'ApiKey=' + encodeURIComponent(token());
    }

    function has(role) {
        return !!(me && me.Roles && me.Roles.indexOf(role) >= 0);
    }

    // ---- Small DOM helpers ----

    function el(tag, className, text) {
        var node = document.createElement(tag);
        if (className) {
            node.className = className;
        }
        if (text !== undefined && text !== null) {
            node.textContent = text;
        }
        return node;
    }

    function button(text, className, onClick) {
        var b = el('button', 'cnb-f cnb-button' + (className ? ' ' + className : ''), text);
        b.type = 'button';
        b.addEventListener('click', onClick);
        return b;
    }

    function toast(text) {
        if (!toastEl) {
            return;
        }
        toastEl.textContent = text;
        toastEl.className = 'cnb-toast cnb-show';
        clearTimeout(toast.timer);
        toast.timer = setTimeout(function () {
            toastEl.className = 'cnb-toast';
        }, 3500);
    }

    // ---- Items: one shape for library entries, discover and lookup results ----

    function poster(resource, kind) {
        var images = resource.images || [];
        for (var i = 0; i < images.length; i++) {
            var image = images[i];
            if (image.coverType !== 'poster') {
                continue;
            }
            if (kind === 'series' && resource.id && image.url) {
                return imageUrl('Image/Series/' + resource.id + '/poster-250.jpg');
            }
            if (image.remoteUrl) {
                return image.remoteUrl.replace('/t/p/original/', '/t/p/w342/');
            }
        }
        return resource.remotePoster || null;
    }

    function toItem(resource, kind) {
        return {
            kind: kind,
            id: resource.id || null,
            externalId: kind === 'series' ? resource.tvdbId : resource.tmdbId,
            title: resource.title || '',
            year: resource.year || null,
            overview: resource.overview || '',
            poster: poster(resource, kind),
            status: localStatus(resource, kind),
            seasons: kind === 'series' && resource.id ? (resource.seasons || null) : null
        };
    }

    // What the resource itself says; lookups and discover lists are filled in from /Status afterwards.
    function localStatus(resource, kind) {
        if (!resource.id) {
            return null;
        }
        if (kind === 'movie') {
            return resource.hasFile ? 'available' : (resource.monitored ? 'wanted' : 'unmonitored');
        }
        var stats = resource.statistics || {};
        if (!stats.episodeFileCount) {
            return resource.monitored ? 'wanted' : 'unmonitored';
        }
        return stats.episodeFileCount >= stats.episodeCount ? 'available' : 'partial';
    }

    function fillStatus(items) {
        var tvdb = [];
        var tmdb = [];
        items.forEach(function (item) {
            if (item.externalId) {
                (item.kind === 'series' ? tvdb : tmdb).push(item.externalId);
            }
        });
        if (!tvdb.length && !tmdb.length) {
            return Promise.resolve(items);
        }
        return api('Status?tvdbIds=' + tvdb.slice(0, 200).join(',') + '&tmdbIds=' + tmdb.slice(0, 200).join(','))
            .then(function (states) {
                (states || []).forEach(function (state) {
                    items.forEach(function (item) {
                        if (item.kind === state.mediaType && item.externalId === state.externalId) {
                            item.status = state.status;
                            item.id = state.id || item.id;
                        }
                    });
                });
                return items;
            })
            .catch(function () {
                return items;
            });
    }

    // ---- Screens ----

    function chipRow(options, current, onPick) {
        var row = el('div', 'cnb-chips');
        options.forEach(function (option) {
            var chip = button(option.label, 'cnb-chip' + (option.value === current ? ' cnb-active' : ''), function () {
                onPick(option.value);
            });
            chip.setAttribute('aria-pressed', option.value === current ? 'true' : 'false');
            row.appendChild(chip);
        });
        return row;
    }

    function grid(items, emptyText) {
        if (!items.length) {
            return el('div', 'cnb-empty', emptyText);
        }
        var wrap = el('div', 'cnb-grid');
        items.forEach(function (item) {
            var card = el('button', 'cnb-f cnb-card');
            card.type = 'button';
            card.setAttribute('data-key', cardKey(item));
            var art = el('div', 'cnb-poster');
            if (item.poster) {
                var img = el('img');
                img.loading = 'lazy';
                img.alt = '';
                img.src = item.poster;
                img.onerror = function () {
                    img.remove();
                };
                art.appendChild(img);
            }
            art.appendChild(el('span', 'cnb-posterTitle', item.title));
            if (item.status && item.status !== 'notInLibrary') {
                art.appendChild(el('span', 'cnb-badge cnb-badge-' + item.status, STATUS_TEXT[item.status] || item.status));
            }
            card.appendChild(art);
            card.appendChild(el('span', 'cnb-cardTitle', item.title));
            card.appendChild(el('span', 'cnb-cardYear', item.year ? String(item.year) : (item.kind === 'series' ? 'Series' : 'Movie')));
            card.addEventListener('click', function () {
                openDetail(item);
            });
            wrap.appendChild(card);
        });
        return wrap;
    }

    function cardKey(item) {
        return item.kind + '-' + (item.externalId || item.id || 0);
    }

    function loading(text) {
        return el('div', 'cnb-empty', text || 'Loading…');
    }

    function renderLibrary() {
        var state = screenState.library;
        body.appendChild(chipRow([
            { label: 'Everything', value: 'all' },
            { label: 'Series', value: 'series' },
            { label: 'Movies', value: 'movie' },
            { label: 'Requested', value: 'wanted' }
        ], state.filter, function (value) {
            state.filter = value;
            render(true);
        }));

        if (!state.items) {
            body.appendChild(loading());
            Promise.all([api('Series'), api('Movies')]).then(function (results) {
                var items = (results[0] || []).map(function (s) { return toItem(s, 'series'); })
                    .concat((results[1] || []).map(function (m) { return toItem(m, 'movie'); }));
                items.sort(function (a, b) { return a.title.localeCompare(b.title); });
                state.items = items;
                if (screen === 'library') {
                    render(false);
                }
            }).catch(showError);
            return;
        }

        var shown = state.items.filter(function (item) {
            if (state.filter === 'series' || state.filter === 'movie') {
                return item.kind === state.filter;
            }
            if (state.filter === 'wanted') {
                return item.status === 'wanted' || item.status === 'downloading' || item.status === 'partial';
            }
            return true;
        });
        body.appendChild(grid(shown, 'Nothing here yet. Use Discover or Search to request something.'));
    }

    function renderDiscover() {
        var state = screenState.discover;
        body.appendChild(chipRow([
            { label: 'Trending movies', value: 'trending' },
            { label: 'Popular movies', value: 'popular' }
        ], state.list, function (value) {
            if (value !== state.list) {
                state.list = value;
                state.items = null;
            }
            render(true);
        }));

        if (!state.items) {
            body.appendChild(loading());
            var list = state.list;
            api('Discover/Movies?list=' + list).then(function (results) {
                return fillStatus((results || []).map(function (m) { return toItem(m, 'movie'); }));
            }).then(function (items) {
                if (state.list === list) {
                    state.items = items;
                    if (screen === 'discover') {
                        render(false);
                    }
                }
            }).catch(showError);
            return;
        }
        body.appendChild(grid(state.items, 'Nothing to show right now.'));
    }

    function renderSearch() {
        var state = screenState.search;
        var form = el('form', 'cnb-searchForm');
        var input = el('input', 'cnb-f cnb-input');
        input.type = 'search';
        input.placeholder = state.type === 'series' ? 'Search for a series' : 'Search for a movie';
        input.value = state.term;
        input.setAttribute('aria-label', input.placeholder);
        form.appendChild(input);
        form.appendChild(button('Search', 'cnb-primary', function () {
            submit();
        }));
        form.addEventListener('submit', function (e) {
            e.preventDefault();
            submit();
        });

        function submit() {
            var term = input.value.trim();
            if (!term) {
                toast('Type something to search for.');
                return;
            }
            state.term = term;
            state.items = 'loading';
            render(false, '.cnb-searchForm .cnb-primary');
            var type = state.type;
            api((type === 'series' ? 'Lookup/Series' : 'Lookup/Movies') + '?term=' + encodeURIComponent(term))
                .then(function (results) {
                    return fillStatus((results || []).slice(0, 60).map(function (r) { return toItem(r, type); }));
                })
                .then(function (items) {
                    if (state.term === term && state.type === type) {
                        state.items = items;
                        if (screen === 'search') {
                            render(false, '.cnb-card');
                        }
                    }
                })
                .catch(function (e) {
                    state.items = null;
                    showError(e);
                });
        }

        body.appendChild(chipRow([
            { label: 'Movies', value: 'movie' },
            { label: 'Series', value: 'series' }
        ], state.type, function (value) {
            if (value !== state.type) {
                state.type = value;
                state.items = null;
            }
            state.term = input.value;
            render(false, '.cnb-chip.cnb-active');
        }));
        body.appendChild(form);

        if (state.items === 'loading') {
            body.appendChild(loading('Searching…'));
        } else if (state.items) {
            body.appendChild(grid(state.items, 'Nothing found. Try another spelling.'));
        }
    }

    function showError(e) {
        toast(e && e.message ? e.message : 'Something went wrong.');
        if (body && body.querySelector('.cnb-empty')) {
            body.querySelector('.cnb-empty').textContent = e && e.message ? e.message : 'Could not load this.';
        }
    }

    function screens() {
        var list = [
            { key: 'library', label: 'Library', render: renderLibrary },
            { key: 'discover', label: 'Discover', render: renderDiscover },
            { key: 'search', label: 'Search', render: renderSearch }
        ];
        if (has('manager')) {
            list.push({ key: 'downloads', label: 'Downloads', render: renderDownloads });
        }
        return list;
    }

    function render(focusFirst, focusSelector) {
        if (!overlay) {
            return;
        }
        tabsEl.innerHTML = '';
        var all = screens();
        if (!all.some(function (s) { return s.key === screen; })) {
            screen = 'library';
        }
        all.forEach(function (s) {
            var tab = button(s.label, 'cnb-tab' + (s.key === screen ? ' cnb-active' : ''), function () {
                if (screen !== s.key) {
                    screen = s.key;
                    render(false, '.cnb-tab.cnb-active');
                }
            });
            tab.setAttribute('aria-selected', s.key === screen ? 'true' : 'false');
            tabsEl.appendChild(tab);
        });

        var keep = document.activeElement && overlay.contains(document.activeElement) && !body.contains(document.activeElement)
            ? document.activeElement
            : null;
        body.innerHTML = '';
        all.filter(function (s) { return s.key === screen; })[0].render();

        var target = focusSelector ? overlay.querySelector(focusSelector) : null;
        if (!target && focusFirst) {
            target = body.querySelector('.cnb-f');
        }
        if (!target && keep && document.contains(keep)) {
            target = keep;
        }
        if (!target && !(document.activeElement && overlay.contains(document.activeElement))) {
            target = overlay.querySelector('.cnb-tab.cnb-active');
        }
        if (target) {
            focus(target);
        }
    }

    // ---- Details and requesting ----

    function openDetail(item) {
        closeDetail();
        detailEl = el('div', 'cnb-detail');
        detailEl.setAttribute('role', 'dialog');
        detailEl.setAttribute('aria-label', item.title);
        var card = el('div', 'cnb-detailCard');
        var art = el('div', 'cnb-detailPoster');
        if (item.poster) {
            var img = el('img');
            img.alt = '';
            img.src = item.poster;
            art.appendChild(img);
        }
        card.appendChild(art);

        var text = el('div', 'cnb-detailText');
        text.appendChild(el('h2', 'cnb-detailTitle', item.title + (item.year ? ' (' + item.year + ')' : '')));
        text.appendChild(el('div', 'cnb-detailMeta', (item.kind === 'series' ? 'Series' : 'Movie') + ' · ' + (STATUS_TEXT[item.status || 'notInLibrary'] || item.status)));
        text.appendChild(el('p', 'cnb-detailOverview', item.overview || 'No description.'));

        var actions = el('div', 'cnb-actions');
        var canRequest = has('requester') && (!item.status || item.status === 'notInLibrary');
        if (canRequest) {
            actions.appendChild(button('Request', 'cnb-primary', function (e) {
                var b = e.currentTarget;
                b.disabled = true;
                b.textContent = 'Requesting…';
                api('Request', { method: 'POST', body: { mediaType: item.kind, externalId: item.externalId } })
                    .then(function (result) {
                        item.status = 'wanted';
                        item.id = result && result.id;
                        screenState.library.items = null;
                        toast(result && result.alreadyExisted ? item.title + ' is already in the library.' : item.title + ' requested.');
                        closeDetail();
                        render(false, '.cnb-card[data-key="' + cardKey(item) + '"]');
                    })
                    .catch(function (err) {
                        b.disabled = false;
                        b.textContent = 'Request';
                        toast(err.message);
                    });
            }));
        }
        if (has('manager') && item.id) {
            actions.appendChild(button('Find releases', '', function () {
                openReleases(item);
            }));
        }
        actions.appendChild(button('Close', '', function () {
            closeDetail();
        }));
        text.appendChild(actions);
        card.appendChild(text);
        detailEl.appendChild(card);
        detailEl.__returnFocus = document.activeElement;
        overlay.appendChild(detailEl);
        focus(actions.querySelector('.cnb-f'));
    }

    function closeDetail() {
        if (!detailEl) {
            return false;
        }
        var back = detailEl.__returnFocus;
        detailEl.remove();
        detailEl = null;
        if (back && document.contains(back)) {
            focus(back);
        } else {
            focus(overlay.querySelector('.cnb-f'));
        }
        return true;
    }

    // ---- Formatting ----

    function formatBytes(bytes) {
        var n = Number(bytes) || 0;
        var units = ['B', 'KB', 'MB', 'GB', 'TB'];
        var i = 0;
        while (n >= 1024 && i < units.length - 1) {
            n /= 1024;
            i++;
        }
        return (i === 0 ? n : n.toFixed(n < 10 ? 1 : 0)) + ' ' + units[i];
    }

    // Cinnabarr sends time left as "hh:mm:ss" or "d.hh:mm:ss".
    function formatTimeLeft(value) {
        if (!value) {
            return '';
        }
        var days = 0;
        var rest = String(value);
        var dot = rest.indexOf('.');
        if (dot > 0 && dot < rest.indexOf(':')) {
            days = parseInt(rest.slice(0, dot), 10) || 0;
            rest = rest.slice(dot + 1);
        }
        var parts = rest.split(':');
        var hours = (parseInt(parts[0], 10) || 0) + days * 24;
        var minutes = parseInt(parts[1], 10) || 0;
        if (hours > 0) {
            return hours + ' h ' + minutes + ' min left';
        }
        return minutes > 0 ? minutes + ' min left' : 'Less than a minute left';
    }

    function formatAge(hours) {
        var h = Number(hours) || 0;
        if (h < 1) {
            return 'Just now';
        }
        if (h < 48) {
            return Math.round(h) + ' h old';
        }
        return Math.round(h / 24) + ' days old';
    }

    // ---- Downloads (managers) ----

    var QUEUE_STATUS = {
        queued: 'Waiting',
        paused: 'Paused',
        downloading: 'Downloading',
        completed: 'Finishing',
        failed: 'Failed',
        warning: 'Needs attention',
        delay: 'Waiting to grab',
        downloadClientUnavailable: 'Download engine unavailable',
        fallback: 'Waiting',
        unknown: 'Unknown'
    };

    var downloadsTimer = null;
    var confirming = {};

    // A button that needs a second press within four seconds, for things that can't be undone.
    function confirmButton(key, text, confirmText, className, action) {
        var b = button(confirming[key] > Date.now() ? confirmText : text, className, function () {
            if (confirming[key] > Date.now()) {
                confirming[key] = 0;
                action(b);
                return;
            }
            confirming[key] = Date.now() + 4000;
            b.textContent = confirmText;
            setTimeout(function () {
                if (b.isConnected && confirming[key] && confirming[key] <= Date.now()) {
                    b.textContent = text;
                }
            }, 4100);
        });
        b.setAttribute('data-key', key);
        return b;
    }

    function queueAction(path, method, done) {
        return api(path, { method: method }).then(function () {
            toast(done);
            loadDownloads();
        }).catch(function (e) {
            toast(e.message);
        });
    }

    function downloadRow(q) {
        var row = el('div', 'cnb-row');
        var name = (q.series && q.series.title) || q.movieTitle || q.title || 'Download';
        var head = el('div', 'cnb-rowHead');
        head.appendChild(el('div', 'cnb-rowTitle', name));
        var statusText = QUEUE_STATUS[q.status] || q.status || '';
        if (q.trackedDownloadState === 'importPending' || q.trackedDownloadState === 'importing') {
            statusText = 'Importing';
        }
        head.appendChild(el('div', 'cnb-rowStatus cnb-q-' + (q.status || 'unknown'), statusText));
        row.appendChild(head);
        if (q.title && q.title !== name) {
            row.appendChild(el('div', 'cnb-rowSub', q.title));
        }

        var size = Number(q.size) || 0;
        var left = Number(q.sizeLeft) || 0;
        var done = size > 0 ? Math.max(0, Math.min(1, (size - left) / size)) : 0;
        var bar = el('div', 'cnb-progress');
        var fill = el('div', 'cnb-progressFill');
        fill.style.width = Math.round(done * 100) + '%';
        bar.appendChild(fill);
        row.appendChild(bar);

        var meta = [Math.round(done * 100) + '%'];
        if (size > 0) {
            meta.push(formatBytes(size - left) + ' of ' + formatBytes(size));
        }
        if (q.status === 'downloading' && q.timeLeft) {
            meta.push(formatTimeLeft(q.timeLeft));
        }
        row.appendChild(el('div', 'cnb-rowMeta', meta.join(' · ')));

        var problem = q.errorMessage;
        if (!problem && q.statusMessages && q.statusMessages.length) {
            var first = q.statusMessages[0];
            problem = (first.messages && first.messages[0]) || first.title;
        }
        if (problem) {
            row.appendChild(el('div', 'cnb-rowProblem', problem));
        }

        var actions = el('div', 'cnb-actions');
        var key = 'q' + q.id;
        if (q.isEngineDownload && q.downloadId && (q.status === 'downloading' || q.status === 'queued')) {
            var pause = button('Pause', '', function () {
                queueAction('Downloads/' + encodeURIComponent(q.downloadId) + '/Pause', 'POST', 'Paused.');
            });
            pause.setAttribute('data-key', key + '-pause');
            actions.appendChild(pause);
        }
        if (q.isEngineDownload && q.downloadId && q.status === 'paused') {
            var resume = button('Resume', '', function () {
                queueAction('Downloads/' + encodeURIComponent(q.downloadId) + '/Resume', 'POST', 'Resumed.');
            });
            resume.setAttribute('data-key', key + '-resume');
            actions.appendChild(resume);
        }
        if (q.status === 'delay') {
            var grab = button('Grab now', 'cnb-primary', function () {
                queueAction('Queue/' + q.id + '/Grab', 'POST', 'Sent to downloads.');
            });
            grab.setAttribute('data-key', key + '-grab');
            actions.appendChild(grab);
        }
        actions.appendChild(confirmButton(key + '-remove', 'Remove', 'Press again to remove', '', function () {
            queueAction('Queue/' + q.id + '?removalMethod=removeFromClient&blocklistMethod=doNotBlocklist', 'DELETE', 'Removed.');
        }));
        actions.appendChild(confirmButton(key + '-replace', 'Remove and find another', 'Press again: blocklist and search', '', function () {
            queueAction('Queue/' + q.id + '?removalMethod=removeFromClient&blocklistMethod=blocklistAndSearch', 'DELETE', 'Removed; searching for another.');
        }));
        row.appendChild(actions);
        return row;
    }

    var downloadsData = null;

    function loadDownloads() {
        return api('Queue?page=1&pageSize=100').then(function (result) {
            downloadsData = (result && result.records) || [];
            if (screen === 'downloads' && overlay && !detailEl) {
                var focused = document.activeElement && document.activeElement.getAttribute('data-key');
                render(false, focused ? '[data-key="' + focused + '"]' : null);
            }
        }).catch(function (e) {
            downloadsData = downloadsData || [];
            showError(e);
        });
    }

    function renderDownloads() {
        if (!downloadsTimer) {
            downloadsTimer = setInterval(function () {
                if (!overlay || screen !== 'downloads') {
                    clearInterval(downloadsTimer);
                    downloadsTimer = null;
                    return;
                }
                if (!detailEl) {
                    loadDownloads();
                }
            }, 2000);
        }
        if (!downloadsData) {
            body.appendChild(loading());
            loadDownloads();
            return;
        }
        if (!downloadsData.length) {
            body.appendChild(el('div', 'cnb-empty', 'Nothing is downloading.'));
            return;
        }
        var list = el('div', 'cnb-list');
        downloadsData.forEach(function (q) {
            list.appendChild(downloadRow(q));
        });
        body.appendChild(list);
    }

    // ---- Interactive search (managers) ----

    function releaseQuality(r) {
        var p = r.parsedInfo;
        return p && p.quality && p.quality.quality ? p.quality.quality.name : '';
    }

    function releaseRow(r, item, season) {
        var info = r.release || {};
        var row = el('div', 'cnb-row');
        var head = el('div', 'cnb-rowHead');
        head.appendChild(el('div', 'cnb-rowTitle cnb-rowRelease', info.title || ''));
        var rejected = r.decision && r.decision.rejected;
        head.appendChild(el('div', 'cnb-rowStatus ' + (rejected ? 'cnb-q-warning' : 'cnb-q-downloading'), rejected ? 'Not wanted' : 'Wanted'));
        row.appendChild(head);

        var meta = [];
        if (releaseQuality(r)) {
            meta.push(releaseQuality(r));
        }
        meta.push(formatBytes(info.size));
        if (info.seeders !== null && info.seeders !== undefined) {
            meta.push(info.seeders + ' seeders' + (info.leechers !== null && info.leechers !== undefined ? ', ' + info.leechers + ' leechers' : ''));
        }
        if (info.indexer) {
            meta.push(info.indexer);
        }
        meta.push(formatAge(info.ageHours));
        row.appendChild(el('div', 'cnb-rowMeta', meta.join(' · ')));

        if (rejected && r.decision.rejections && r.decision.rejections.length) {
            row.appendChild(el('div', 'cnb-rowProblem', r.decision.rejections.map(function (x) { return x.message; }).join(' · ')));
        }

        var actions = el('div', 'cnb-actions');
        var key = 'r-' + info.indexerId + '-' + String(info.guid || '').replace(/[^A-Za-z0-9]/g, '').slice(-24);
        var grab = function (b) {
            b.disabled = true;
            b.textContent = 'Grabbing…';
            var bodyJson = { guid: info.guid, indexerId: info.indexerId };
            if (item.kind === 'series') {
                bodyJson.searchInfo = { seriesId: item.id, seasonNumber: season };
            }
            api(item.kind === 'series' ? 'Releases/Series' : 'Releases/Movie', { method: 'POST', body: bodyJson })
                .then(function () {
                    b.textContent = 'Grabbed';
                    toast('Sent to downloads.');
                })
                .catch(function (e) {
                    b.disabled = false;
                    b.textContent = 'Grab';
                    toast(e.message);
                });
        };
        if (!r.downloadAllowed) {
            var no = button('Can’t grab', '', function () {});
            no.disabled = true;
            actions.appendChild(no);
        } else if (rejected) {
            actions.appendChild(confirmButton(key, 'Grab anyway', 'Press again to grab', '', grab));
        } else {
            var b = button('Grab', 'cnb-primary', function () {
                grab(b);
            });
            b.setAttribute('data-key', key);
            actions.appendChild(b);
        }
        row.appendChild(actions);
        return row;
    }

    function openReleases(item) {
        var returnFocus = detailEl ? detailEl.__returnFocus : document.activeElement;
        if (detailEl) {
            detailEl.remove();
            detailEl = null;
        }
        detailEl = el('div', 'cnb-detail cnb-releases');
        detailEl.setAttribute('role', 'dialog');
        detailEl.setAttribute('aria-label', 'Releases for ' + item.title);
        detailEl.__returnFocus = returnFocus;
        var panel = el('div', 'cnb-releasePanel');
        var top = el('div', 'cnb-releaseTop');
        top.appendChild(el('h2', 'cnb-detailTitle', 'Releases: ' + item.title));
        top.appendChild(button('Close', '', function () {
            closeDetail();
        }));
        panel.appendChild(top);
        var seasonRow = el('div');
        panel.appendChild(seasonRow);
        var results = el('div', 'cnb-list cnb-releaseList');
        panel.appendChild(results);
        detailEl.appendChild(panel);
        overlay.appendChild(detailEl);

        var season = null;

        function search() {
            results.innerHTML = '';
            results.appendChild(loading('Searching indexers… this can take a minute.'));
            var path = item.kind === 'series'
                ? 'Releases/Series?seriesId=' + item.id + '&seasonNumber=' + season
                : 'Releases/Movie?movieId=' + item.id;
            var started = season;
            api(path).then(function (releases) {
                if (!detailEl || started !== season) {
                    return;
                }
                results.innerHTML = '';
                if (!releases || !releases.length) {
                    results.appendChild(el('div', 'cnb-empty', 'No releases found.'));
                    return;
                }
                releases.slice(0, 100).forEach(function (r) {
                    results.appendChild(releaseRow(r, item, season));
                });
                focus(results.querySelector('.cnb-f:not([disabled])') || detailEl.querySelector('.cnb-f'));
            }).catch(function (e) {
                if (detailEl) {
                    results.innerHTML = '';
                    results.appendChild(el('div', 'cnb-empty', e.message));
                }
            });
        }

        function showSeasons(seasons) {
            var numbers = (seasons || []).map(function (x) { return x.seasonNumber; });
            numbers.sort(function (a, b) {
                return (a === 0 ? 1000 : a) - (b === 0 ? 1000 : b);
            });
            if (!numbers.length) {
                results.innerHTML = '';
                results.appendChild(el('div', 'cnb-empty', 'This series has no seasons yet.'));
                return;
            }
            var withoutSpecials = numbers.filter(function (n) { return n > 0; });
            season = withoutSpecials.length ? withoutSpecials[withoutSpecials.length - 1] : numbers[0];
            var draw = function () {
                seasonRow.innerHTML = '';
                seasonRow.appendChild(chipRow(numbers.map(function (n) {
                    return { label: n === 0 ? 'Specials' : 'Season ' + n, value: n };
                }), season, function (value) {
                    season = value;
                    draw();
                    focus(seasonRow.querySelector('.cnb-chip.cnb-active'));
                    search();
                }));
            };
            draw();
            focus(seasonRow.querySelector('.cnb-chip.cnb-active'));
            search();
        }

        focus(top.querySelector('.cnb-f'));
        if (item.kind === 'series') {
            if (item.seasons) {
                showSeasons(item.seasons);
            } else {
                results.appendChild(loading());
                api('Series/' + item.id).then(function (series) {
                    item.seasons = series && series.seasons;
                    if (detailEl) {
                        showSeasons(item.seasons);
                    }
                }).catch(showError);
            }
        } else {
            search();
        }
    }

    // ---- Opening and closing ----

    function open() {
        if (overlay) {
            return;
        }
        lastFocusOutside = document.activeElement;
        overlay = el('div', 'cnb-overlay');
        overlay.setAttribute('role', 'dialog');
        overlay.setAttribute('aria-label', 'Cinnabarr');

        var top = el('div', 'cnb-top');
        top.appendChild(el('div', 'cnb-brand', 'Cinnabarr'));
        tabsEl = el('div', 'cnb-tabs');
        tabsEl.setAttribute('role', 'tablist');
        top.appendChild(tabsEl);
        top.appendChild(button('Close', 'cnb-close', close));
        overlay.appendChild(top);

        body = el('div', 'cnb-body');
        overlay.appendChild(body);
        toastEl = el('div', 'cnb-toast');
        toastEl.setAttribute('role', 'status');
        overlay.appendChild(toastEl);

        document.body.appendChild(overlay);
        document.documentElement.classList.add('cnb-open');
        render(false, '.cnb-tab.cnb-active');
        startPad();
    }

    function close() {
        if (!overlay) {
            return;
        }
        overlay.remove();
        overlay = null;
        detailEl = null;
        document.documentElement.classList.remove('cnb-open');
        if (downloadsTimer) {
            clearInterval(downloadsTimer);
            downloadsTimer = null;
        }
        downloadsData = null;
        // Lists change while the overlay is closed; load them again next time.
        screenState.library.items = null;
        screenState.discover.items = null;
        var back = lastFocusOutside && document.contains(lastFocusOutside) ? lastFocusOutside : document.querySelector('.' + BUTTON_CLASS);
        if (back && back.focus) {
            back.focus();
        }
    }

    // ---- Moving around with the remote or a controller ----

    function focusables() {
        var scope = detailEl || overlay;
        var list = scope.querySelectorAll('.cnb-f');
        var result = [];
        for (var i = 0; i < list.length; i++) {
            var node = list[i];
            if (!node.disabled && node.offsetParent !== null) {
                result.push(node);
            }
        }
        return result;
    }

    function focus(node) {
        if (!node) {
            return;
        }
        node.focus({ preventScroll: true });
        if (node.scrollIntoView) {
            node.scrollIntoView({ block: 'nearest', inline: 'nearest' });
        }
    }

    // The nearest element in a direction, preferring ones in line with the current one.
    function move(direction) {
        var all = focusables();
        var current = document.activeElement;
        if (all.indexOf(current) < 0) {
            focus(all[0]);
            return;
        }
        var from = current.getBoundingClientRect();
        var fx = from.left + from.width / 2;
        var fy = from.top + from.height / 2;
        var best = null;
        var bestScore = Infinity;
        all.forEach(function (node) {
            if (node === current) {
                return;
            }
            var r = node.getBoundingClientRect();
            var x = r.left + r.width / 2;
            var y = r.top + r.height / 2;
            var along;
            var across;
            if (direction === 'left') { along = fx - x; across = Math.abs(fy - y); if (r.right > from.left + 1) { return; } }
            if (direction === 'right') { along = x - fx; across = Math.abs(fy - y); if (r.left < from.right - 1) { return; } }
            if (direction === 'up') { along = fy - y; across = Math.abs(fx - x); if (r.bottom > from.top + 1) { return; } }
            if (direction === 'down') { along = y - fy; across = Math.abs(fx - x); if (r.top < from.bottom - 1) { return; } }
            var score = along + across * 3;
            if (score < bestScore) {
                bestScore = score;
                best = node;
            }
        });
        if (best) {
            focus(best);
        }
    }

    function goBack() {
        if (closeDetail()) {
            return;
        }
        close();
    }

    function press(action) {
        if (!overlay) {
            return;
        }
        if (action === 'back') {
            goBack();
        } else if (action === 'ok') {
            var current = document.activeElement;
            if (current && overlay.contains(current)) {
                if (current.tagName === 'INPUT') {
                    var form = current.form;
                    if (form) {
                        var submit = form.querySelector('.cnb-primary');
                        if (submit) {
                            submit.click();
                        }
                    }
                } else {
                    current.click();
                }
            }
        } else {
            move(action);
        }
    }

    function onKey(e) {
        if (!overlay) {
            return;
        }
        // Jellyfin's own controller support sends synthetic key presses; the overlay reads the controller itself.
        if (!e.isTrusted && /^Gamepad/.test(e.key || '')) {
            e.stopImmediatePropagation();
            e.preventDefault();
            return;
        }
        var action = KEYS[e.keyCode];
        var target = e.target;
        var inInput = target && target.tagName === 'INPUT';

        if (inInput) {
            // Typing: Backspace deletes, left/right move the cursor until it reaches an end of the text.
            if (e.keyCode === 8) {
                e.stopPropagation();
                return;
            }
            if ((action === 'left' && target.selectionStart > 0) || (action === 'right' && target.selectionEnd < target.value.length)) {
                e.stopPropagation();
                return;
            }
        }

        if (!action) {
            // Keep Jellyfin from acting on other keys while the overlay is open, but let typing through.
            e.stopPropagation();
            return;
        }
        e.preventDefault();
        e.stopImmediatePropagation();
        press(action);
    }

    var padHeld = {};
    var padRunning = false;

    function padEdge(id, down, repeat, action) {
        var now = Date.now();
        var s = padHeld[id];
        if (down) {
            if (!s) {
                padHeld[id] = { next: now + 420 };
                press(action);
            } else if (repeat && now >= s.next) {
                s.next = now + 130;
                press(action);
            }
        } else {
            padHeld[id] = null;
        }
    }

    // Same mapping as Cinnabarr's /tv launcher: d-pad or left stick move, A (or Start) selects, B goes back.
    function pollPad() {
        if (!overlay) {
            padRunning = false;
            padHeld = {};
            return;
        }
        var pads = navigator.getGamepads ? navigator.getGamepads() : [];
        for (var p = 0; p < pads.length; p++) {
            var pad = pads[p];
            if (!pad || !pad.connected) {
                continue;
            }
            var b = pad.buttons;
            var on = function (i) { return !!(b[i] && b[i].pressed); };
            var x = pad.axes[0] || 0;
            var y = pad.axes[1] || 0;
            var k = 'p' + p;
            padEdge(k + 'l', on(14) || x < -0.6, true, 'left');
            padEdge(k + 'r', on(15) || x > 0.6, true, 'right');
            padEdge(k + 'u', on(12) || y < -0.6, true, 'up');
            padEdge(k + 'd', on(13) || y > 0.6, true, 'down');
            padEdge(k + 'a', on(0) || on(9), false, 'ok');
            padEdge(k + 'b', on(1), false, 'back');
        }
        requestAnimationFrame(pollPad);
    }

    function startPad() {
        if (!padRunning && navigator.getGamepads) {
            padRunning = true;
            requestAnimationFrame(pollPad);
        }
    }

    // ---- The header button ----

    function headerButton() {
        var b = document.createElement('button');
        b.type = 'button';
        b.className = 'headerButton headerButtonRight paper-icon-button-light ' + BUTTON_CLASS;
        b.title = 'Cinnabarr';
        b.setAttribute('aria-label', 'Cinnabarr');
        b.innerHTML = '<svg viewBox="0 0 24 24" width="24" height="24" aria-hidden="true"><path fill="currentColor" d="M12 2a10 10 0 1 0 0 20 10 10 0 0 0 0-20zm0 3.2a6.8 6.8 0 0 1 5.6 2.9l-2.4 1.6A3.9 3.9 0 1 0 15.2 14.3l2.4 1.6A6.8 6.8 0 1 1 12 5.2z"/></svg>';
        b.addEventListener('click', open);
        return b;
    }

    function ensureButton() {
        var current = userId();
        if (!current || !token()) {
            me = null;
            meForUser = null;
            removeButtons();
            if (overlay) {
                close();
            }
            return;
        }
        if (meForUser !== current) {
            meForUser = current;
            me = null;
            api('Me').then(function (result) {
                if (meForUser === current) {
                    me = result;
                    ensureButton();
                }
            }).catch(function () {
                // Ask again on the next check (for example right after signing in, before the session is ready).
                if (meForUser === current) {
                    meForUser = null;
                }
            });
            return;
        }
        if (!me || !me.Configured || !me.Roles || !me.Roles.length) {
            removeButtons();
            return;
        }
        var slot = headerSlot();
        if (!slot) {
            return;
        }
        var existing = document.querySelectorAll('.' + BUTTON_CLASS);
        for (var i = 0; i < existing.length; i++) {
            if (existing[i].parentElement !== slot) {
                existing[i].remove();
            }
        }
        if (!slot.querySelector('.' + BUTTON_CLASS)) {
            slot.insertBefore(headerButton(), slot.firstChild);
        }
    }

    // Where the header's icon buttons are. Jellyfin 10.9 and later show a new app bar (the old header is still in
    // the page, hidden); older layouts and some pages use the old one. Labels are translated, so this goes by
    // position: the icon buttons sit in the group just before the user menu, which is the last button.
    function headerSlot() {
        var bar = document.querySelector('header.MuiAppBar-root');
        if (bar && bar.offsetParent !== null || (bar && getComputedStyle(bar).position === 'fixed')) {
            var buttons = bar.querySelectorAll('button:not(.' + BUTTON_CLASS + ')');
            if (buttons.length >= 2) {
                var last = buttons[buttons.length - 1];
                var previous = buttons[buttons.length - 2];
                return previous.parentElement !== last.parentElement ? previous.parentElement : last.parentElement;
            }
        }
        var legacy = document.querySelector('.skinHeader .headerRight');
        return legacy && legacy.offsetParent !== null ? legacy : null;
    }

    function removeButtons() {
        var existing = document.querySelectorAll('.' + BUTTON_CLASS);
        for (var i = 0; i < existing.length; i++) {
            existing[i].remove();
        }
    }

    function start() {
        window.addEventListener('keydown', onKey, true);
        // jellyfin-web rebuilds its header as you move around and when the user changes, so check now and then.
        setInterval(ensureButton, 1500);
        ensureButton();
    }

    window.__cinnabarr = { open: open, close: close };

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', start);
    } else {
        start();
    }
})();
