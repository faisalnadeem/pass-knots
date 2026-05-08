// ── Password show/hide toggles ────────────────────────────────────────────────
document.querySelectorAll('.eye-btn').forEach(btn => {
    btn.addEventListener('click', () => {
        const input = document.getElementById(btn.dataset.target);
        if (!input) return;
        const isPassword = input.type === 'password';
        input.type = isPassword ? 'text' : 'password';
        btn.textContent = isPassword ? '🙈' : '👁';
    });
});

// ── Reveal vault entries ──────────────────────────────────────────────────────
document.querySelectorAll('.btn-reveal').forEach(btn => {
    const pwd = btn.dataset.password;
    const target = document.getElementById(btn.dataset.target);
    let visible = false;
    btn.addEventListener('click', () => {
        visible = !visible;
        target.textContent = visible ? pwd : '••••••••••••';
        btn.textContent    = visible ? 'Hide' : 'Show';
    });
});

// ── Password generator ────────────────────────────────────────────────────────
const genBtn = document.getElementById('gen-btn');
if (genBtn) {
    genBtn.addEventListener('click', () => {
        const chars = 'abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789!@#$%^&*()_+-=[]';
        const arr = new Uint32Array(20);
        crypto.getRandomValues(arr);
        const pwd = Array.from(arr, n => chars[n % chars.length]).join('');
        const input = document.getElementById('vault-pwd');
        if (input) {
            input.value = pwd;
            input.type  = 'text';
            const eyeBtn = document.querySelector('.eye-btn[data-target="vault-pwd"]');
            if (eyeBtn) eyeBtn.textContent = '🙈';
        }
    });
}

// ── Delete confirmation modal ─────────────────────────────────────────────────
const deleteModal = document.getElementById('delete-confirm-modal');
const deleteConfirmBtn = document.getElementById('delete-confirm-btn');
const deleteCancelBtn = document.getElementById('delete-cancel-btn');
const deleteMessage = document.getElementById('delete-modal-message');
let pendingDeleteForm = null;

function closeDeleteModal() {
    if (!deleteModal) return;
    deleteModal.hidden = true;
    pendingDeleteForm = null;
}

if (deleteModal && deleteConfirmBtn && deleteCancelBtn && deleteMessage) {
    document.querySelectorAll('.delete-entry-form').forEach(form => {
        form.addEventListener('submit', e => {
            e.preventDefault();
            pendingDeleteForm = form;

            const entryName = form.dataset.entryName || 'this entry';
            deleteMessage.textContent = `Are you sure you want to delete "${entryName}"? This action cannot be undone.`;
            deleteModal.hidden = false;
        });
    });

    deleteConfirmBtn.addEventListener('click', () => {
        if (pendingDeleteForm) pendingDeleteForm.submit();
    });

    deleteCancelBtn.addEventListener('click', closeDeleteModal);

    deleteModal.addEventListener('click', e => {
        if (e.target === deleteModal) closeDeleteModal();
    });

    document.addEventListener('keydown', e => {
        if (e.key === 'Escape' && !deleteModal.hidden) closeDeleteModal();
    });
}

// ── Auto-dismiss alerts ───────────────────────────────────────────────────────
document.querySelectorAll('.alert').forEach(alert => {
    setTimeout(() => {
        alert.classList.add('fade-out');
        setTimeout(() => alert.remove(), 350);
    }, 3000);
});

// ── Entry details modal ────────────────────────────────────────────────────────
const entryDetailsModal = document.getElementById('entry-details-modal');
const entryDetailsCloseBtn = document.getElementById('entry-details-close');
const detailsSiteName = document.getElementById('details-site-name');
const detailsSiteUrl = document.getElementById('details-site-url');
const detailsUsername = document.getElementById('details-username');
const detailsPassword = document.getElementById('details-password');
const detailsPasswordToggle = document.getElementById('details-password-toggle');
const detailsNotes = document.getElementById('details-notes');
const detailsSharedWithList = document.getElementById('details-shared-with-list');

let detailsPasswordValue = '';
let detailsPasswordVisible = false;
const cardsViewBtn = document.getElementById('view-cards-btn');
const listViewBtn = document.getElementById('view-list-btn');
const entriesCardsView = document.getElementById('entries-cards-view');
const entriesListView = document.getElementById('entries-list-view');

function setDetailsPasswordVisibility(visible) {
    detailsPasswordVisible = visible;
    if (!detailsPassword || !detailsPasswordToggle) return;
    detailsPassword.textContent = visible ? detailsPasswordValue : '••••••••••••';
    detailsPasswordToggle.textContent = visible ? 'Hide' : 'Show';
}

function closeEntryDetailsModal() {
    if (!entryDetailsModal) return;
    entryDetailsModal.hidden = true;
    detailsPasswordValue = '';
    setDetailsPasswordVisibility(false);
}

function openEntryDetailsModal(card) {
    if (!entryDetailsModal || !card) return;

    const siteName = card.dataset.siteName || 'N/A';
    const siteUrl = card.dataset.siteUrl || '';
    const username = card.dataset.username || 'N/A';
    const password = card.dataset.password || '';
    const notes = card.dataset.notes || '';
    const sharedEmails = (card.dataset.sharedEmails || '')
        .split('||')
        .map(v => v.trim())
        .filter(Boolean);

    if (detailsSiteName) detailsSiteName.textContent = siteName;
    if (detailsUsername) detailsUsername.textContent = username;
    if (detailsNotes) detailsNotes.textContent = notes.trim() ? notes : 'None';
    if (detailsSharedWithList) {
        detailsSharedWithList.innerHTML = '';
        if (sharedEmails.length === 0) {
            const empty = document.createElement('span');
            empty.className = 'entry-value entry-notes';
            empty.textContent = 'Not shared yet';
            detailsSharedWithList.appendChild(empty);
        } else {
            sharedEmails.forEach(email => {
                const item = document.createElement('span');
                item.className = 'entry-value entry-notes entry-shared-line';
                item.textContent = email;
                detailsSharedWithList.appendChild(item);
            });
        }
    }

    if (detailsSiteUrl) {
        if (siteUrl.trim()) {
            detailsSiteUrl.textContent = siteUrl;
            detailsSiteUrl.href = siteUrl;
            detailsSiteUrl.style.pointerEvents = 'auto';
        } else {
            detailsSiteUrl.textContent = 'None';
            detailsSiteUrl.removeAttribute('href');
            detailsSiteUrl.style.pointerEvents = 'none';
        }
    }

    detailsPasswordValue = password;
    setDetailsPasswordVisibility(false);
    entryDetailsModal.hidden = false;
}

if (entryDetailsModal) {
    document.querySelectorAll('.entry-details-trigger').forEach(card => {
        card.addEventListener('click', e => {
            if (e.target.closest('.entry-actions') || e.target.closest('.btn-reveal') || e.target.closest('.entry-url')) return;
            openEntryDetailsModal(card);
        });

        card.addEventListener('keydown', e => {
            if (e.key !== 'Enter' && e.key !== ' ') return;
            e.preventDefault();
            openEntryDetailsModal(card);
        });
    });

    if (detailsPasswordToggle) {
        detailsPasswordToggle.addEventListener('click', () => {
            setDetailsPasswordVisibility(!detailsPasswordVisible);
        });
    }

    if (entryDetailsCloseBtn) {
        entryDetailsCloseBtn.addEventListener('click', closeEntryDetailsModal);
    }

    entryDetailsModal.addEventListener('click', e => {
        if (e.target === entryDetailsModal) closeEntryDetailsModal();
    });

    document.addEventListener('keydown', e => {
        if (e.key === 'Escape' && !entryDetailsModal.hidden) closeEntryDetailsModal();
    });
}

// ── Vault cards/list switch ────────────────────────────────────────────────────
function setVaultView(mode) {
    if (!entriesCardsView || !entriesListView || !cardsViewBtn || !listViewBtn) return;

    const showCards = mode !== 'list';
    entriesCardsView.hidden = !showCards;
    entriesListView.hidden = showCards;

    cardsViewBtn.classList.toggle('is-active', showCards);
    cardsViewBtn.setAttribute('aria-selected', showCards ? 'true' : 'false');
    listViewBtn.classList.toggle('is-active', !showCards);
    listViewBtn.setAttribute('aria-selected', showCards ? 'false' : 'true');
}

if (cardsViewBtn && listViewBtn && entriesCardsView && entriesListView) {
    cardsViewBtn.addEventListener('click', () => setVaultView('cards'));
    listViewBtn.addEventListener('click', () => setVaultView('list'));
    setVaultView('cards');
}

// ── Vault search filter ────────────────────────────────────────────────────────
const vaultSearchInput = document.getElementById('vault-search');

function toggleSectionTitles(viewContainer) {
    if (!viewContainer) return;
    viewContainer.querySelectorAll('.section-title').forEach(title => {
        const sectionBody = title.nextElementSibling;
        if (!sectionBody) return;
        const visibleEntries = sectionBody.querySelectorAll('.entry-details-trigger:not([hidden])').length;
        title.hidden = visibleEntries === 0;
    });
}

function applyVaultSearchFilter(query) {
    const normalizedQuery = (query || '').trim().toLowerCase();
    const entryNodes = document.querySelectorAll('.entry-details-trigger');

    entryNodes.forEach(node => {
        const searchableText = [
            node.dataset.siteName || '',
            node.dataset.siteUrl || '',
            node.dataset.username || '',
            node.dataset.notes || ''
        ].join(' ').toLowerCase();

        const isMatch = !normalizedQuery || searchableText.includes(normalizedQuery);
        node.hidden = !isMatch;
    });

    toggleSectionTitles(entriesCardsView);
    toggleSectionTitles(entriesListView);
}

if (vaultSearchInput) {
    vaultSearchInput.addEventListener('input', e => {
        applyVaultSearchFilter(e.target.value);
    });
}
