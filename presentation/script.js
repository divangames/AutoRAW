// ══════════════════════════════════════════════
//   AutoRAW Presentation — Premium Investor
// ══════════════════════════════════════════════

document.addEventListener('DOMContentLoaded', () => {
  initScrollSnap();
  initRevealAnimations();
  initScrollDots();
  initNavBurger();
  initVideoSpeed();
  initTypingChat();
  initParallax();
});

// ── Section snap + dot tracking ──────────────
function initScrollSnap() {
  const wrap = document.getElementById('scrollWrap');
  if (!wrap) return;

  const sections = [...wrap.querySelectorAll('.snap-sec')];
  let current = 0;

  const io = new IntersectionObserver((entries) => {
    entries.forEach(e => {
      if (e.isIntersecting && e.intersectionRatio >= 0.5) {
        current = parseInt(e.target.dataset.idx || '0', 10);
        updateDots(current);
        triggerSection(e.target);
      }
    });
  }, { root: wrap, threshold: 0.5 });

  sections.forEach(s => io.observe(s));

  // Trigger first section immediately
  if (sections[0]) {
    setTimeout(() => triggerSection(sections[0]), 200);
  }
}

function triggerSection(sec) {
  if (sec.dataset.triggered) return;
  sec.dataset.triggered = '1';
  sec.classList.add('is-active');

  // Apply stagger delays via inline style
  sec.querySelectorAll('[data-d]').forEach(el => {
    const delay = parseInt(el.dataset.d || '0', 10);
    el.style.animationDelay = delay + 'ms';
  });
}

// ── Scroll dots ──────────────────────────────
function initScrollDots() {
  const wrap = document.getElementById('scrollWrap');
  if (!wrap) return;

  document.querySelectorAll('.sdot').forEach(dot => {
    dot.addEventListener('click', () => {
      const idx = parseInt(dot.dataset.idx || '0', 10);
      const target = wrap.querySelector(`[data-idx="${idx}"]`);
      if (target) target.scrollIntoView({ behavior: 'smooth' });
    });
  });
}

function updateDots(idx) {
  document.querySelectorAll('.sdot').forEach(d => {
    d.classList.toggle('active', parseInt(d.dataset.idx) === idx);
  });
}

// ── Reveal animations (re-trigger on revisit) ──
function initRevealAnimations() {
  const wrap = document.getElementById('scrollWrap');
  if (!wrap) return;

  const io = new IntersectionObserver((entries) => {
    entries.forEach(e => {
      if (!e.isIntersecting) {
        // Reset so animation plays again on scroll back
        e.target.dataset.triggered = '';
        e.target.classList.remove('is-active');
      }
    });
  }, { root: wrap, threshold: 0.05 });

  wrap.querySelectorAll('.snap-sec').forEach(s => io.observe(s));
}

// ── Nav burger ───────────────────────────────
function initNavBurger() {
  const burger = document.getElementById('navBurger');
  const links  = document.querySelector('.nav-links');
  if (!burger || !links) return;

  burger.addEventListener('click', () => {
    links.classList.toggle('open');
    burger.classList.toggle('burger-open');
  });

  links.querySelectorAll('a').forEach(a => {
    a.addEventListener('click', () => {
      links.classList.remove('open');
      burger.classList.remove('burger-open');
    });
  });

  // Mobile nav anchor scroll in wrap
  document.querySelectorAll('a[href^="#"]').forEach(a => {
    a.addEventListener('click', e => {
      const id = a.getAttribute('href').slice(1);
      const target = document.getElementById(id);
      const wrap = document.getElementById('scrollWrap');
      if (target && wrap) {
        e.preventDefault();
        target.scrollIntoView({ behavior: 'smooth' });
      }
    });
  });
}

// ── Kinescope speed x2 ───────────────────────
function initVideoSpeed() {
  const iframe = document.getElementById('heroVideo');
  if (!iframe) return;

  const send = () => {
    try {
      iframe.contentWindow.postMessage(
        JSON.stringify({ method: 'setPlaybackRate', value: 2 }), '*'
      );
      iframe.contentWindow.postMessage({ event: 'setPlaybackRate', data: 2 }, '*');
    } catch (_) {}
  };

  iframe.addEventListener('load', () => {
    send();
    setTimeout(send, 1500);
    setTimeout(send, 3500);
  });
}

// ── Typing / rotating chat phrases ───────────
function initTypingChat() {
  const firstBubble = document.querySelector('#chatMessages .chat-bubble');
  if (!firstBubble) return;

  const phrases = [
    '👋 Привет! Я ZONA — нейросеть в AutoRAW. Беру на себя рутину кадрирования, а ваш главный талант — снимать товар — остаётся только за вами.',
    '🎀 Всё под контролем! Слежу за каждым кадром с нежностью и вниманием.',
    '✨ Даю сигнал — пакет в работе. Скоро порадую результатом!',
    '🍬 Я ZONA — нейросеть с характером. Ошибусь — исправлюсь, обещаю!',
    '🌸 Понимаю контекст сама: готово, ошибка, пауза — каждый раз своими словами.',
  ];
  let idx = 0;

  setInterval(() => {
    idx = (idx + 1) % phrases.length;
    firstBubble.style.transition = 'opacity .35s';
    firstBubble.style.opacity = '0';
    setTimeout(() => {
      const time = firstBubble.querySelector('.msg-time');
      const timeHtml = time ? time.outerHTML : '';
      firstBubble.innerHTML = phrases[idx] + timeHtml;
      firstBubble.style.opacity = '1';
    }, 380);
  }, 5000);
}

// ── Parallax on hero character ───────────────
function initParallax() {
  const wrap = document.getElementById('scrollWrap');
  const char = document.getElementById('heroChar');
  if (!wrap || !char) return;

  let ticking = false;
  wrap.addEventListener('scroll', () => {
    if (!ticking) {
      requestAnimationFrame(() => {
        const scrolled = wrap.scrollTop;
        // Subtle upward float as you scroll hero
        char.style.transform = `translateY(${scrolled * 0.08}px)`;
        ticking = false;
      });
      ticking = true;
    }
  }, { passive: true });
}

// ── Nav burger open styles ───────────────────
const style = document.createElement('style');
style.textContent = `
  .nav-links.open {
    display: flex !important;
    flex-direction: column;
    position: fixed;
    top: 60px; left: 0; right: 0;
    background: rgba(8,8,15,0.97);
    border-bottom: 1px solid rgba(255,255,255,0.07);
    padding: 12px 16px 20px;
    gap: 4px;
    z-index: 199;
  }
  .burger-open span:nth-child(1) { transform: rotate(45deg) translate(5px, 5px); }
  .burger-open span:nth-child(2) { opacity: 0; }
  .burger-open span:nth-child(3) { transform: rotate(-45deg) translate(5px, -5px); }
`;
document.head.appendChild(style);
