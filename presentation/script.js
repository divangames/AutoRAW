// ══════════════════════════════════════════════
//   AutoRAW Presentation — script.js
// ══════════════════════════════════════════════

document.addEventListener('DOMContentLoaded', () => {
  initAnimateIn();
  initNavBurger();
  initNavActive();
  initNavScroll();
  initTypingChat();
});

// ── Animate on scroll ──
function initAnimateIn() {
  const els = document.querySelectorAll('.animate-in');

  const io = new IntersectionObserver((entries) => {
    entries.forEach(e => {
      if (e.isIntersecting) {
        const delay = parseInt(e.target.dataset.delay || '0', 10);
        setTimeout(() => e.target.classList.add('visible'), delay);
        io.unobserve(e.target);
      }
    });
  }, { threshold: 0.15 });

  els.forEach(el => io.observe(el));

  // General scroll-reveal for section content
  const cards = document.querySelectorAll(
    '.feature-card, .zona-step, .tg-card, .stack-card, .req-card, .ai-feature, .tg-info-item'
  );

  const cardIo = new IntersectionObserver((entries) => {
    entries.forEach((e, idx) => {
      if (e.isIntersecting) {
        const i = Array.from(cards).indexOf(e.target);
        const delay = (i % 6) * 60;
        setTimeout(() => {
          e.target.style.opacity = '1';
          e.target.style.transform = 'none';
        }, delay);
        cardIo.unobserve(e.target);
      }
    });
  }, { threshold: 0.1 });

  cards.forEach(card => {
    card.style.opacity = '0';
    card.style.transform = 'translateY(20px)';
    card.style.transition = 'opacity 0.5s ease, transform 0.5s ease';
    cardIo.observe(card);
  });
}

// ── Mobile burger ──
function initNavBurger() {
  const burger = document.getElementById('navBurger');
  const links  = document.querySelector('.nav-links');
  if (!burger || !links) return;

  burger.addEventListener('click', () => {
    links.classList.toggle('open');
    burger.classList.toggle('active');
  });

  links.querySelectorAll('a').forEach(a => {
    a.addEventListener('click', () => {
      links.classList.remove('open');
      burger.classList.remove('active');
    });
  });
}

// ── Highlight active nav link on scroll ──
function initNavActive() {
  const sections = document.querySelectorAll('section[id]');
  const links    = document.querySelectorAll('.nav-links a[href^="#"]');

  const io = new IntersectionObserver((entries) => {
    entries.forEach(e => {
      if (e.isIntersecting) {
        links.forEach(l => l.classList.remove('active'));
        const active = document.querySelector(`.nav-links a[href="#${e.target.id}"]`);
        if (active) active.classList.add('active');
      }
    });
  }, { threshold: 0.5 });

  sections.forEach(s => io.observe(s));
}

// ── Nav background on scroll ──
function initNavScroll() {
  const nav = document.querySelector('.nav');
  if (!nav) return;
  window.addEventListener('scroll', () => {
    nav.style.background = window.scrollY > 40
      ? 'rgba(10,10,15,0.95)'
      : 'rgba(10,10,15,0.75)';
  }, { passive: true });
}

// ── Typing animation in chat ──
function initTypingChat() {
  const chatEl = document.getElementById('chatMessages');
  if (!chatEl) return;

  const phrases = [
    '🎀 Привет! ZONA на связи — обрабатываю ваш пакет, пока вы пьёте кофе.',
    '🌸 Всё под контролем! Слежу за каждым кадром с нежностью.',
    '✨ Даю сигнал — пакет в работе. Скоро порадую результатом!',
    '🍬 Я ZONA — нейросеть с характером. Ошибусь — не обижайтесь, исправлюсь!',
  ];

  let phraseIdx = 0;

  // Rotate greeting bubble every 5 seconds
  const firstBubble = chatEl.querySelector('.chat-msg-zona .chat-bubble');
  if (!firstBubble) return;

  setInterval(() => {
    phraseIdx = (phraseIdx + 1) % phrases.length;
    firstBubble.style.opacity = '0';
    firstBubble.style.transition = 'opacity 0.4s';
    setTimeout(() => {
      const time = firstBubble.querySelector('.msg-time');
      const timeText = time ? time.outerHTML : '';
      firstBubble.innerHTML = phrases[phraseIdx] + timeText;
      firstBubble.style.opacity = '1';
    }, 400);
  }, 5000);
}

// ── Nav active link style ──
const style = document.createElement('style');
style.textContent = `
  .nav-links a.active {
    color: var(--text) !important;
    background: var(--surface) !important;
  }
  .nav-burger.active span:nth-child(1) { transform: rotate(45deg) translate(5px, 5px); }
  .nav-burger.active span:nth-child(2) { opacity: 0; }
  .nav-burger.active span:nth-child(3) { transform: rotate(-45deg) translate(5px, -5px); }
`;
document.head.appendChild(style);
