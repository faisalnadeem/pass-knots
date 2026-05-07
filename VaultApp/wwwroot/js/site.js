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
