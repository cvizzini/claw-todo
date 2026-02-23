// Session storage helper — persists JWT + refresh token across page refreshes
window.sessionStore = {
    key: 'todoapp_auth',

    save(data) {
        try { localStorage.setItem(this.key, JSON.stringify(data)); } catch { }
    },

    load() {
        try {
            const raw = localStorage.getItem(this.key);
            return raw ? JSON.parse(raw) : null;
        } catch { return null; }
    },

    clear() {
        try { localStorage.removeItem(this.key); } catch { }
    }
};
