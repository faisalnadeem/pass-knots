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
