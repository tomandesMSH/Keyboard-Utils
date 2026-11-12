// The contents on the left mark where the reader is: the section at the top of the window (and its category) is
// highlighted, and the list scrolls itself so the highlighted entry stays in view.
(function () {
  const toc = document.querySelector('.doc-toc');
  if (!toc) return;
  const links = [...toc.querySelectorAll('a[href^="#"]')];
  const sections = links
    .map(link => ({ link, section: document.getElementById(link.getAttribute('href').slice(1)) }))
    .filter(entry => entry.section);
  let current = null;

  function mark() {
    // The last section whose heading has reached the top part of the window; at the very bottom, the last one.
    const line = Math.min(160, window.innerHeight * 0.3);
    let active = sections[0];
    for (const entry of sections)
      if (entry.section.getBoundingClientRect().top <= line) active = entry;
    // (A short last section can't reach the top: being at the very bottom - give or take a scrollbar - means it.)
    if (window.innerHeight + window.scrollY >= document.documentElement.scrollHeight - 48)
      active = sections[sections.length - 1];
    if (active === current) return;
    current = active;

    for (const { link } of sections) link.removeAttribute('aria-current');
    toc.querySelectorAll('.doc-toc-group').forEach(group => group.classList.remove('active'));
    active.link.setAttribute('aria-current', 'location');
    active.link.closest('.doc-toc-group')?.classList.add('active');

    // Keep it visible in the list (only the list scrolls, not the page).
    const top = active.link.offsetTop - toc.offsetTop;
    if (top < toc.scrollTop + 40 || top > toc.scrollTop + toc.clientHeight - 60)
      toc.scrollTop = Math.max(0, top - toc.clientHeight / 3);
  }

  let pending = false;
  window.addEventListener('scroll', () => {
    if (pending) return;
    pending = true;
    requestAnimationFrame(() => {
      pending = false;
      mark();
    });
  }, { passive: true });
  window.addEventListener('resize', mark);
  mark();
})();
