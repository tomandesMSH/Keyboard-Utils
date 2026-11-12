// The contents on the left: the section at the top of the window (and its category) is highlighted, and the list
// scrolls itself so the highlighted entry stays in view. Categories are collapsed except the one being read and
// the ones the reader opened (a click on a category's heading opens or closes it until the next click).
(function () {
  const toc = document.querySelector('.doc-toc');
  if (!toc) return;
  const links = [...toc.querySelectorAll('a[href^="#"]')];
  const sections = links
    .map(link => ({ link, section: document.getElementById(link.getAttribute('href').slice(1)) }))
    .filter(entry => entry.section);
  const groups = [...toc.querySelectorAll('.doc-toc-group')];
  // The reader's own choice per category: 'open', 'closed', or none (then: open only while being read).
  const chosen = new Map();
  let current = null;

  function activeGroup() {
    return current?.link.closest('.doc-toc-group') ?? null;
  }

  function applyGroups() {
    const reading = activeGroup();
    for (const group of groups) {
      const choice = chosen.get(group);
      const open = choice === 'open' || (choice !== 'closed' && group === reading);
      group.classList.toggle('collapsed', !open);
      group.classList.toggle('active', group === reading);
      group.querySelector('.doc-toc-cat')?.setAttribute('aria-expanded', String(open));
    }
  }

  for (const group of groups) {
    group.querySelector('.doc-toc-cat')?.addEventListener('click', () => {
      chosen.set(group, group.classList.contains('collapsed') ? 'open' : 'closed');
      applyGroups();
    });
  }

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

    const previousGroup = activeGroup();
    current = active;
    // Reading into another category: one the reader closed by hand opens again when they come back to it.
    if (activeGroup() !== previousGroup && chosen.get(activeGroup()) === 'closed')
      chosen.delete(activeGroup());

    for (const { link } of sections) link.removeAttribute('aria-current');
    active.link.setAttribute('aria-current', 'location');
    applyGroups();

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
  // From now on opening / closing animates (not the setup above, on load).
  requestAnimationFrame(() => requestAnimationFrame(() => toc.classList.add('ready')));
})();
