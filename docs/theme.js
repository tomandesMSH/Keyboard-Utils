// Light / dark mode. The page is light; the switch in the header turns it dark (and back). The pick is
// remembered (localStorage) and set on <html data-theme> - before the first paint, by the small script in
// each page's <head>. Pages hear about changes through a "themechange" event.
(function () {
  const root = document.documentElement;
  const button = document.querySelector('.theme-toggle');

  const isDark = () => root.dataset.theme === 'dark';

  function update() {
    const dark = isDark();
    if (button) {
      button.setAttribute('aria-pressed', String(dark));
      button.title = dark ? 'Přepnout na světlý režim' : 'Přepnout na tmavý režim';
    }
    document.dispatchEvent(new CustomEvent('themechange', { detail: { dark } }));
  }

  button?.addEventListener('click', () => {
    const next = isDark() ? 'light' : 'dark';
    root.dataset.theme = next;
    try {
      localStorage.setItem('theme', next);
    } catch {
      // Private mode or storage blocked: the switch still works for this visit.
    }
    update();
  });

  update();
})();
