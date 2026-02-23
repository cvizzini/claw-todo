window.themeManager = {
    get: () => localStorage.getItem('theme') || 'light',
    set: (theme) => {
        localStorage.setItem('theme', theme);
        document.documentElement.setAttribute('data-theme', theme);
    },
    init: () => {
        const theme = localStorage.getItem('theme') || 'light';
        document.documentElement.setAttribute('data-theme', theme);
        return theme;
    }
};
// Init immediately on load
window.themeManager.init();
