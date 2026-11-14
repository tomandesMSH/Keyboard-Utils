// Light / dark mode. The page is light; the switch in the header turns it dark (and back). The pick is
// remembered (localStorage) and set on <html data-theme> - before the first paint, by the small script in
// each page's <head>. Pages hear about changes through a "themechange" event.
(function () {
  const root = document.documentElement;
  const button = document.querySelector('.theme-toggle');

  const english = (root.lang || '').startsWith('en');
  const isDark = () => root.dataset.theme === 'dark';

  function update() {
    const dark = isDark();
    if (button) {
      button.setAttribute('aria-pressed', String(dark));
      button.title = english
        ? (dark ? 'Switch to light mode' : 'Switch to dark mode')
        : (dark ? 'Přepnout na světlý režim' : 'Přepnout na tmavý režim');
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

  // The CS | EN switch on the docs page opens the other language at the section being read (the same ids in
  // both languages): the one highlighted in the contents, else the one in the address.
  document.querySelectorAll('.lang-switch a[data-keep-hash]').forEach((a) => a.addEventListener('click', () => {
    const current = document.querySelector('.doc-toc a[aria-current="location"]');
    a.href = a.href.split('#')[0] + (current ? current.hash : location.hash);
  }));
})();
