// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Write your JavaScript code.

// File size limit for every upload in the system. Any <input type="file">
// with data-max-bytes is checked as soon as a file is picked: too big, and
// the choice is cleared with a message under the box, before anything is
// sent. The server checks the same limit (Helpers/FileUploads.cs).
document.addEventListener('change', function (e) {
    var input = e.target;
    if (!input.matches || !input.matches('input[type="file"][data-max-bytes]')) return;

    var max = parseInt(input.getAttribute('data-max-bytes'), 10);
    var tooBig = Array.from(input.files || []).find(function (f) { return f.size > max; });

    var note = input.parentElement.querySelector('.file-size-error');
    if (!note) {
        note = document.createElement('div');
        note.className = 'file-size-error text-danger small mt-1';
        input.insertAdjacentElement('afterend', note);
    }

    if (tooBig) {
        // Rounded up, so a file just over the limit reads 3.1 MB, not 3 MB.
        var mb = function (bytes) { return (Math.ceil(bytes / 1024 / 1024 * 10) / 10).toFixed(1).replace(/\.0$/, ''); };
        note.textContent = '"' + tooBig.name + '" is ' + mb(tooBig.size) + ' MB. The limit is ' + mb(max) + ' MB — pick a smaller file.';
        input.value = '';
    } else {
        note.textContent = '';
    }
});

// Drop zones (.file-drop) wrap a full-size, invisible file input, so a click
// or a dropped file both land in the real input. This only shows what was
// picked: the file's name in place of the prompt, and for a photo box
// (data-preview) the picture itself. Runs after the size check above, which
// clears a file that is too big.
document.addEventListener('change', function (e) {
    var input = e.target;
    if (!input.matches || !input.matches('.file-drop input[type="file"]')) return;

    var zone = input.closest('.file-drop');
    var title = zone.querySelector('.file-drop-title');
    var file = input.files && input.files[0];

    zone.classList.toggle('has-file', !!file);
    if (title) title.textContent = file ? file.name : title.getAttribute('data-empty');

    var preview = zone.querySelector('.file-drop-preview');
    if (preview) {
        if (file && file.type.indexOf('image/') === 0) {
            preview.src = URL.createObjectURL(file);
            preview.classList.remove('d-none');
        } else {
            preview.removeAttribute('src');
            preview.classList.add('d-none');
        }
    }
});

['dragenter', 'dragover'].forEach(function (type) {
    document.addEventListener(type, function (e) {
        var zone = e.target.closest && e.target.closest('.file-drop');
        if (zone) zone.classList.add('is-over');
    });
});
['dragleave', 'drop'].forEach(function (type) {
    document.addEventListener(type, function (e) {
        var zone = e.target.closest && e.target.closest('.file-drop');
        if (zone) zone.classList.remove('is-over');
    });
});

// Typeable dropdowns. A <select data-combobox> gets a text box in front of
// it: type to narrow the list, arrow keys and Enter to pick, Escape to
// close. The real <select> stays in the form (hidden) and is what posts,
// so validation, "change" listeners and scripts that rebuild its options
// (the branch filters) keep working. Option groups show as headings, and
// typing matches the option or its group ("annex" finds every Annex room).
// Code that sets the select's value without a "change" event can fire
// "combo-sync" on it to refresh the text.
(function () {
    function enhance(select) {
        var wrap = document.createElement('div');
        wrap.className = 'combo position-relative';
        select.parentNode.insertBefore(wrap, select);
        wrap.appendChild(select);
        select.classList.add('combo-native');
        select.tabIndex = -1;
        select.setAttribute('aria-hidden', 'true');

        var input = document.createElement('input');
        input.type = 'text';
        input.className = 'form-select combo-input';
        input.autocomplete = 'off';
        input.setAttribute('role', 'combobox');
        input.setAttribute('aria-expanded', 'false');
        input.setAttribute('aria-autocomplete', 'list');
        if (select.id) {
            input.id = select.id + 'Combo';
            var label = document.querySelector('label[for="' + select.id + '"]');
            if (label) label.setAttribute('for', input.id);
        }
        wrap.insertBefore(input, select);

        var list = document.createElement('ul');
        list.className = 'dropdown-menu w-100 combo-list';
        list.setAttribute('role', 'listbox');
        wrap.appendChild(list);
        if (input.id) {
            list.id = input.id + 'List';
            input.setAttribute('aria-controls', list.id);
        }

        var active = -1;

        // "-- Pick a branch first --" or "Select a location": a hint, not a
        // choice. A real blank choice ("No fixed room") is neither.
        function isHint(opt) {
            return opt.value === '' && (opt.text.indexOf('--') === 0 || /^(select|search) /i.test(opt.text));
        }

        // The box shows the picked option's text; with a hint picked it is
        // empty and shows the hint as its placeholder.
        function syncText() {
            var opt = select.options[select.selectedIndex] || null;
            var hint = Array.from(select.options).find(isHint);
            input.placeholder = hint ? hint.text.replace(/^--\s*|\s*--$/g, '') : 'Type to search';
            input.value = opt && !isHint(opt) ? opt.text : '';
            input.disabled = select.disabled;
        }

        function items() { return Array.from(list.querySelectorAll('[data-value]')); }

        function render(query) {
            var q = (query || '').trim().toLowerCase();
            list.innerHTML = '';
            var shown = 0;

            function addOption(opt, group) {
                // Hidden: taken by another row, say. Disabled: shown greyed
                // out, but cannot be picked.
                if (opt.hidden) return false;
                var text = opt.text;
                var sub = opt.dataset.sub || '';
                if (q && text.toLowerCase().indexOf(q) === -1 && sub.toLowerCase().indexOf(q) === -1
                    && (!group || group.toLowerCase().indexOf(q) === -1)) return false;
                var li = document.createElement('li');
                var b = document.createElement('button');
                b.type = 'button';
                b.className = 'dropdown-item' + (opt.selected ? ' is-selected' : '') + (opt.value === '' ? ' combo-blank' : '');
                b.setAttribute('role', 'option');
                b.tabIndex = -1;
                if (opt.disabled) {
                    b.classList.add('disabled');
                    b.setAttribute('aria-disabled', 'true');
                } else {
                    b.dataset.value = opt.value;
                }
                // The typed letters in bold, and an option's data-sub (say
                // the building and floor) on a second line.
                var at = q ? text.toLowerCase().indexOf(q) : -1;
                var main = document.createElement('span');
                main.className = 'combo-main';
                if (at >= 0) {
                    main.appendChild(document.createTextNode(text.slice(0, at)));
                    var strong = document.createElement('strong');
                    strong.textContent = text.slice(at, at + q.length);
                    main.appendChild(strong);
                    main.appendChild(document.createTextNode(text.slice(at + q.length)));
                } else {
                    main.textContent = text;
                }
                b.appendChild(main);
                if (sub) {
                    var small = document.createElement('small');
                    small.className = 'combo-sub';
                    small.textContent = sub;
                    b.appendChild(small);
                }
                b.addEventListener('mousedown', function (e) { e.preventDefault(); if (!opt.disabled) pick(opt.value); });
                li.appendChild(b);
                list.appendChild(li);
                shown++;
                return true;
            }

            Array.from(select.children).forEach(function (child) {
                if (child.tagName === 'OPTGROUP') {
                    var header = document.createElement('li');
                    header.className = 'dropdown-header combo-group';
                    header.textContent = child.label;
                    list.appendChild(header);
                    var any = false;
                    Array.from(child.children).forEach(function (o) { if (addOption(o, child.label)) any = true; });
                    if (!any) header.remove();
                } else if (child.tagName === 'OPTION') {
                    // A "-- pick a branch first --" style placeholder is a
                    // hint, not a choice; a real blank choice ("No fixed
                    // room") stays in the list.
                    if (isHint(child)) return;
                    addOption(child, null);
                }
            });

            if (!shown) {
                var none = document.createElement('li');
                none.className = 'dropdown-item-text text-muted small';
                none.textContent = 'Nothing matches what you typed.';
                list.appendChild(none);
            }
            setActive(-1);
        }

        function setActive(i) {
            var all = items();
            all.forEach(function (b) { b.classList.remove('active'); });
            active = i;
            if (i >= 0 && i < all.length) {
                all[i].classList.add('active');
                all[i].scrollIntoView({ block: 'nearest' });
            }
        }

        function open(filterText) {
            if (select.disabled) return;
            render(filterText);
            list.classList.add('show');
            input.setAttribute('aria-expanded', 'true');

            // Keep the list clear of the bottom of the screen -- on a phone
            // that is where the sticky Save/Cancel bar and the keyboard are.
            // Done on the next frame, after the browser's own scroll to the
            // tapped box, which would otherwise cancel it.
            requestAnimationFrame(function () {
                var bottom = list.getBoundingClientRect().bottom;
                var footer = document.querySelector('.register-footer');
                var limit = window.innerHeight - (footer ? footer.offsetHeight + 16 : 16);
                if (bottom > limit) window.scrollBy(0, bottom - limit);
            });
            var sel = items().findIndex(function (b) { return b.classList.contains('is-selected'); });
            if (sel >= 0 && !filterText) setActive(sel);
        }

        function close() {
            list.classList.remove('show');
            input.setAttribute('aria-expanded', 'false');
        }

        function pick(value) {
            if (select.value !== value) {
                select.value = value;
                select.dispatchEvent(new Event('change', { bubbles: true }));
            }
            syncText();
            close();
        }

        input.addEventListener('focus', function () { input.select(); open(''); });
        input.addEventListener('click', function () { if (!list.classList.contains('show')) open(''); });
        input.addEventListener('input', function () { open(input.value); });
        input.addEventListener('keydown', function (e) {
            var all = items();
            if (e.key === 'ArrowDown') {
                e.preventDefault();
                if (!list.classList.contains('show')) open('');
                setActive(Math.min(active + 1, all.length - 1));
            } else if (e.key === 'ArrowUp') {
                e.preventDefault();
                setActive(Math.max(active - 1, 0));
            } else if (e.key === 'Enter') {
                if (list.classList.contains('show')) {
                    e.preventDefault();
                    var target = all[active] || (all.length === 1 ? all[0] : null);
                    if (target) pick(target.dataset.value);
                }
            } else if (e.key === 'Escape') {
                close();
                syncText();
            }
        });
        // Leaving the box without picking puts back what was picked.
        input.addEventListener('blur', function () { close(); syncText(); });

        select.addEventListener('change', syncText);
        select.addEventListener('combo-sync', syncText);

        // Scripts that rebuild the options or lock the list.
        new MutationObserver(syncText).observe(select, { childList: true, subtree: true, attributes: true, attributeFilter: ['disabled'] });

        syncText();
    }

    function init() { document.querySelectorAll('select[data-combobox]').forEach(enhance); }
    // For rows added later (New request's "+ Add another item").
    window.sabCombobox = function (select) { if (!select.closest('.combo')) enhance(select); };
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init); else init();
})();

// Lists that follow a branch: Assign, Approve borrow, New request, Record
// return and Request History all pick a branch first, then a room or a
// department of that branch.
//   var f = sabFilterByBranch(select, branchSelect, options);
// keeps only the options of select whose data-branch equals the branch's
// value. Options are rebuilt rather than hidden (some browsers ignore
// "hidden" on <option>); an option without data-branch (the blank first
// one) always stays. A picked option from another branch is cleared, and
// the typeable box is told to refresh. Options, all optional:
//   hint(branchValue)  text for the blank first option
//   lock(branchValue)  true to disable the list (e.g. until a branch is picked)
//   showAllWhenEmpty   no branch picked: show every option, not none
//   strip              with a branch picked, drop a "Branch — " prefix from texts
//   notify             fire "change" when the picked option was cleared
//   auto: false        don't refresh on the branch's own change event; call
//                      f.refresh() yourself (to control the order of updates)
// Returns { refresh }.
window.sabFilterByBranch = function (select, branch, opts) {
    opts = opts || {};
    var all = Array.from(select.options).filter(function (o) { return o.dataset.branch; }).map(function (o) {
        return { value: o.value, text: o.text, data: Object.assign({}, o.dataset) };
    });

    function refresh() {
        var keep = select.value;
        var b = branch.value;
        Array.from(select.options).forEach(function (o) { if (o.dataset.branch) o.remove(); });
        var blank = select.options[0];
        if (blank && !blank.value && opts.hint) blank.text = opts.hint(b);
        all.forEach(function (x) {
            if (b ? x.data.branch !== b : !opts.showAllWhenEmpty) return;
            var o = document.createElement('option');
            o.value = x.value;
            o.text = b && opts.strip ? x.text.replace(/^.*? — /, '') : x.text;
            Object.keys(x.data).forEach(function (k) { o.dataset[k] = x.data[k]; });
            select.appendChild(o);
        });
        select.value = keep;
        var cleared = select.value !== keep;
        if (cleared) select.value = '';
        if (opts.lock) select.disabled = !!opts.lock(b);
        select.dispatchEvent(new Event('combo-sync'));
        if (cleared && opts.notify) select.dispatchEvent(new Event('change', { bubbles: true }));
    }

    if (opts.auto !== false) branch.addEventListener('change', refresh);
    refresh();
    return { refresh: refresh };
};

// List-and-details pages (Approvals, My Requests, My New-Item Requests).
// Markup: a root [data-md] holding list items [data-entry="id"] (inside
// groups [data-md-group] with a [data-md-count]), one [data-panel="id"]
// per item, a search box [data-md-search], filter selects
// [data-md-filter="key"] matching each item's data-key attribute, and a
// pager [data-md-pager] with a [data-md-range] label. Picking an item shows
// its panel and puts its id in the URL hash; on a phone or tablet the list
// and the panel take turns, with [data-back] to go back. Tabs
// ([data-md-tab="sectionId"]) switch between several roots on one page.
(function () {
    var narrow = window.matchMedia('(max-width: 991.98px)');

    function initRoot(root) {
        var items = Array.from(root.querySelectorAll('[data-entry]'));
        var panels = Array.from(root.querySelectorAll('[data-panel]'));
        var search = root.querySelector('[data-md-search]');
        var filters = Array.from(root.querySelectorAll('[data-md-filter]'));
        var pager = root.querySelector('[data-md-pager]');
        var range = root.querySelector('[data-md-range]');
        var empty = root.querySelector('[data-md-empty]');
        var pageSize = parseInt(root.dataset.pageSize || '8', 10);
        var page = 1;

        // "Show [10] per page" beside the pager: the list's own size plus
        // 10, 20 and 50. Added here so every list gets it.
        if (pager && !root.querySelector('[data-md-size]')) {
            var sizes = [pageSize, 10, 20, 50].filter(function (n, i, all) { return all.indexOf(n) === i; })
                .sort(function (a, b) { return a - b; });
            var sizeWrap = document.createElement('label');
            sizeWrap.className = 'md-size';
            sizeWrap.innerHTML = '<span>Show</span><select class="form-select form-select-sm" data-md-size aria-label="Per page">' +
                sizes.map(function (n) { return '<option value="' + n + '"' + (n === pageSize ? ' selected' : '') + '>' + n + '</option>'; }).join('') +
                '</select><span>per page</span>';
            pager.parentNode.insertBefore(sizeWrap, pager);
            sizeWrap.querySelector('select').addEventListener('change', function (e) {
                pageSize = parseInt(e.target.value, 10) || pageSize;
                page = 1;
                render();
            });
        }

        function select(id, fromUser) {
            items.forEach(function (b) {
                var on = b.dataset.entry === id;
                b.classList.toggle('is-active', on);
                b.setAttribute('aria-current', on ? 'true' : 'false');
            });
            panels.forEach(function (p) { p.hidden = p.dataset.panel !== id; });
            if (fromUser) {
                history.replaceState(null, '', '#' + id);
                if (narrow.matches) {
                    root.classList.add('show-detail');
                    window.scrollTo({ top: root.getBoundingClientRect().top + window.scrollY - 12 });
                }
            }
        }

        function matching() {
            var q = search ? search.value.trim().toLowerCase() : '';
            return items.filter(function (b) {
                if (q && (b.dataset.search || '').indexOf(q) === -1) return false;
                return filters.every(function (f) {
                    if (!f.value) return true;
                    // "|a|b|" holds several values (a request whose units
                    // sit in more than one room): match any of them.
                    var v = b.dataset[f.dataset.mdFilter] || '';
                    return v.charAt(0) === '|' ? v.indexOf('|' + f.value + '|') !== -1 : v === f.value;
                });
            });
        }

        // Search and filters narrow the list; the pager pages through what
        // is left, in list order. Groups show their matching count.
        function render() {
            var hits = matching();
            var pages = Math.max(1, Math.ceil(hits.length / pageSize));
            page = Math.min(page, pages);
            var onPage = hits.slice((page - 1) * pageSize, page * pageSize);
            items.forEach(function (b) { b.hidden = onPage.indexOf(b) === -1; });

            root.querySelectorAll('[data-md-group]').forEach(function (g) {
                var inGroup = Array.from(g.querySelectorAll('[data-entry]'));
                var shown = inGroup.filter(function (b) { return !b.hidden; }).length;
                g.hidden = shown === 0;
                var count = g.querySelector('[data-md-count]');
                if (count) count.textContent = inGroup.filter(function (b) { return hits.indexOf(b) !== -1; }).length;
            });

            if (empty) empty.hidden = hits.length > 0;
            if (range) {
                range.textContent = hits.length === 0 ? 'No matches'
                    : ((page - 1) * pageSize + 1) + '–' + Math.min(page * pageSize, hits.length) + ' of ' + hits.length + ' ' + (root.dataset.noun || 'requests');
            }
            if (pager) {
                pager.innerHTML = '';
                pager.hidden = pages <= 1;
                function btn(label, target, opts) {
                    var b = document.createElement('button');
                    b.type = 'button';
                    b.className = 'asset-page' + (opts && opts.current ? ' current' : '') + (opts && opts.disabled ? ' disabled' : '');
                    b.innerHTML = label;
                    if (opts && opts.aria) b.setAttribute('aria-label', opts.aria);
                    if (!(opts && (opts.disabled || opts.current))) b.addEventListener('click', function () { page = target; render(); });
                    pager.appendChild(b);
                }
                btn('&lsaquo;', page - 1, { disabled: page === 1, aria: 'Previous page' });
                for (var n = 1; n <= pages; n++) {
                    if (pages > 5 && n !== 1 && n !== pages && Math.abs(n - page) > 1) {
                        if (n === 2 || n === pages - 1) {
                            var gap = document.createElement('span');
                            gap.className = 'asset-page-gap';
                            gap.textContent = '…';
                            pager.appendChild(gap);
                        }
                        continue;
                    }
                    btn(String(n), n, { current: n === page });
                }
                btn('&rsaquo;', page + 1, { disabled: page === pages, aria: 'Next page' });
            }
        }

        items.forEach(function (b) { b.addEventListener('click', function () { select(b.dataset.entry, true); }); });
        root.querySelectorAll('[data-back]').forEach(function (b) {
            b.addEventListener('click', function () {
                root.classList.remove('show-detail');
                history.replaceState(null, '', location.pathname + location.search);
            });
        });
        if (search) search.addEventListener('input', function () { page = 1; render(); });
        filters.forEach(function (f) { f.addEventListener('change', function () { page = 1; render(); markFilters(); }); });

        // The Filters button shows how many filters are on, and the menu
        // gets a "Clear filters" link, so a forgotten filter cannot quietly
        // hide part of the list.
        var filterBtn = filters.length ? filters[0].closest('.dropdown') : null;
        var filterMenu = filterBtn ? filterBtn.querySelector('.dropdown-menu') : null;
        filterBtn = filterBtn ? filterBtn.querySelector('[data-bs-toggle="dropdown"]') : null;
        if (filterMenu) {
            var clear = document.createElement('button');
            clear.type = 'button';
            clear.className = 'btn btn-link btn-sm px-0 mt-2 md-filter-clear';
            clear.textContent = 'Clear filters';
            clear.hidden = true;
            clear.addEventListener('click', function () {
                filters.forEach(function (f) {
                    f.value = '';
                    f.dispatchEvent(new Event('combo-sync'));
                });
                page = 1;
                render();
                markFilters();
            });
            filterMenu.appendChild(clear);
        }
        function markFilters() {
            if (!filterBtn) return;
            var on = filters.filter(function (f) { return f.value; }).length;
            filterBtn.classList.toggle('active', on > 0);
            var badge = filterBtn.querySelector('.asset-filter-count');
            if (on > 0) {
                if (!badge) {
                    badge = document.createElement('span');
                    badge.className = 'asset-filter-count';
                    filterBtn.appendChild(badge);
                }
                badge.textContent = on;
            } else if (badge) {
                badge.remove();
            }
            if (filterMenu) filterMenu.querySelector('.md-filter-clear').hidden = on === 0;
        }
        markFilters();

        // "Show N more units" in a panel.
        root.querySelectorAll('[data-more]').forEach(function (b) {
            b.addEventListener('click', function () {
                b.parentElement.querySelectorAll('[data-extra]').forEach(function (r) { r.hidden = false; });
                b.remove();
            });
        });

        render();
        return {
            root: root,
            has: function (id) { return items.some(function (b) { return b.dataset.entry === id; }); },
            select: select,
            first: function () { return items[0] && items[0].dataset.entry; }
        };
    }

    function init() {
        var roots = Array.from(document.querySelectorAll('[data-md]')).map(initRoot);
        if (!roots.length) return;

        var tabs = Array.from(document.querySelectorAll('[data-md-tab]'));
        function showTab(sectionId) {
            tabs.forEach(function (t) {
                var on = t.dataset.mdTab === sectionId;
                t.classList.toggle('active', on);
                t.setAttribute('aria-selected', on ? 'true' : 'false');
                var section = document.getElementById(t.dataset.mdTab);
                if (section) section.hidden = !on;
            });
        }
        tabs.forEach(function (t) {
            t.addEventListener('click', function () {
                showTab(t.dataset.mdTab);
                var r = roots.find(function (x) { return document.getElementById(t.dataset.mdTab).contains(x.root); });
                history.replaceState(null, '', '#' + (r && r.first() ? r.first() : ''));
            });
        });

        // Start on the item in the hash (switching to its tab), else the
        // first item of each list.
        var hash = location.hash.slice(1);
        roots.forEach(function (r) {
            var start = hash && r.has(hash) ? hash : r.first();
            if (start) r.select(start, false);
            if (hash && r.has(hash)) {
                var section = tabs.length ? tabs.map(function (t) { return document.getElementById(t.dataset.mdTab); })
                    .find(function (s) { return s && s.contains(r.root); }) : null;
                if (section) showTab(section.id);
                if (narrow.matches) r.root.classList.add('show-detail');
            }
        });
    }

    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init); else init();
})();

// A plain list pager: [data-pager] holds [data-pager-item]s, a
// [data-pager-range] label, a [data-pager-buttons] box, and optionally a
// [data-pager-size] select. data-noun names the items in the label
// ("1–5 of 5 entries"). Used on a request's page for items and history.
// A page that filters the list marks left-out items with data-pager-out
// and fires "pager-refresh" on the root; the pager then counts and pages
// only the rest, from page 1.
(function () {
    function init(root) {
        var all = Array.from(root.querySelectorAll('[data-pager-item]'));
        var items = all;
        var range = root.querySelector('[data-pager-range]');
        var buttons = root.querySelector('[data-pager-buttons]');
        var size = root.querySelector('[data-pager-size]');
        var pageSize = parseInt(size ? size.value : root.dataset.pageSize || '10', 10);
        var noun = root.dataset.noun || 'items';
        var page = 1;

        function render() {
            items = all.filter(function (it) { return !it.hasAttribute('data-pager-out'); });
            all.forEach(function (it) { if (it.hasAttribute('data-pager-out')) it.hidden = true; });
            var pages = Math.max(1, Math.ceil(items.length / pageSize));
            page = Math.min(page, pages);
            items.forEach(function (it, i) { it.hidden = i < (page - 1) * pageSize || i >= page * pageSize; });
            if (range) range.textContent = items.length
                ? ((page - 1) * pageSize + 1) + '–' + Math.min(page * pageSize, items.length) + ' of ' + items.length + ' ' + noun
                : (all.length ? 'No matches' : '');
            if (!buttons) return;
            buttons.innerHTML = '';
            function add(label, target, cls) {
                var b = document.createElement('button');
                b.type = 'button';
                b.className = 'asset-page ' + (cls || '');
                b.innerHTML = label;
                if (!cls) b.addEventListener('click', function () { page = target; render(); });
                buttons.appendChild(b);
            }
            add('&lsaquo;', page - 1, page === 1 ? 'disabled' : '');
            // First, last, and the pages either side of this one; "…" for
            // the gaps, so a long list doesn't give a long row of numbers.
            var last = 0;
            for (var n = 1; n <= pages; n++) {
                if (n !== 1 && n !== pages && Math.abs(n - page) > 1) continue;
                if (last && n - last > 1) {
                    var gap = document.createElement('span');
                    gap.className = 'asset-page-gap';
                    gap.textContent = '…';
                    buttons.appendChild(gap);
                }
                add(String(n), n, n === page ? 'current' : '');
                last = n;
            }
            add('&rsaquo;', page + 1, page === pages ? 'disabled' : '');
        }

        if (size) size.addEventListener('change', function () { pageSize = parseInt(size.value, 10); page = 1; render(); });
        root.addEventListener('pager-refresh', function () { page = 1; render(); });
        render();
    }
    // For lists added to the page later (the Users page's activity tabs).
    window.sabPager = function (root) { if (!root.dataset.pagerOn) { root.dataset.pagerOn = '1'; init(root); } };
    function start() { document.querySelectorAll('[data-pager]').forEach(window.sabPager); }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start); else start();
})();

// Puts a value in a summary field, or a grey italic word when there is
// none ("Not chosen yet"), never a bare dash.
window.sabShow = function (el, text, whenEmpty) {
    if (!el) return;
    el.textContent = text || whenEmpty;
    el.classList.toggle('is-empty', !text);
};
