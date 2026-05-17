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

// ── Full chat conversation rotation ──────────
function initTypingChat() {
  const container = document.getElementById('chatMessages');
  if (!container) return;

  // Complete conversation scenarios
  const convos = [
    {
      msgs: [
        { type: 'zona', text: '👋 Привет! Я ZONA — нейросеть в AutoRAW. Беру на себя рутину кадрирования, а ваш главный талант — снимать товар — остаётся только за вами.', time: '09:41' },
        { type: 'zona', text: '🍬 Люблю конфеты и романтику, всем рада! Буду учиться и присылать отчёты.', time: '09:41' },
        { type: 'sys',  text: '⏯ Пакет запущен — 160 файлов' },
        { type: 'done', text: '🌊 Последняя волна — и файлы готовы!\n\n✅ Успешно: 160, ошибок: 0, всего: 160. Время: 4:22.\nПрофиль: Кроссовки', time: '09:46' },
      ]
    },
    {
      msgs: [
        { type: 'zona', text: '🎀 Всё под контролем! Слежу за каждым кадром с нежностью и вниманием.', time: '10:12' },
        { type: 'zona', text: '✨ Анализирую маркёры — Zona обнаружена у всех файлов, это прекрасно!', time: '10:12' },
        { type: 'sys',  text: '⏯ Пакет запущен — 48 файлов' },
        { type: 'done', text: '🌟 Готово! Было так приятно работать.\n\n✅ Успешно: 48, ошибок: 0, всего: 48. Время: 1:12.\nПрофиль: WB Sneakers', time: '10:14' },
      ]
    },
    {
      msgs: [
        { type: 'zona', text: '🌸 Приступаю! Zona-маркёры найдены у большинства файлов — начинаю детекцию.', time: '14:05' },
        { type: 'zona', text: '🔍 Буду особенно внимательна с RAW — они большие, но я справлюсь!', time: '14:05' },
        { type: 'sys',  text: '⏯ Пакет запущен — 72 файла' },
        { type: 'done', text: '🎯 Почти всё прошло идеально, один файл не поддался — но я не сдалась!\n\n✅ Успешно: 71, ошибок: 1, всего: 72. Время: 3:08.\nПрофиль: Кроссовки', time: '14:09' },
      ]
    },
    {
      msgs: [
        { type: 'zona', text: '💜 Добрый вечер! Я ZONA — готова обрабатывать сколько угодно кадров, пока вы отдыхаете.', time: '18:30' },
        { type: 'zona', text: '🧠 Понимаю контекст сама: готово, ошибка, пауза — каждый раз своими словами.', time: '18:30' },
        { type: 'sys',  text: '🚫 Обработка отменена пользователем' },
        { type: 'zona', text: '🌊 Прибой затих. Отмена принята.\n\nОбработано до отмены: 34 из 96.\nПрофиль: WB Sneakers', time: '18:31' },
      ]
    },
  ];

  let idx = 0;

  function buildMsg(msg) {
    if (msg.type === 'sys') {
      return `<div class="chat-msg-sys"><div class="chat-bubble-sys">${msg.text}</div></div>`;
    }
    const bubbleClass = msg.type === 'done' ? 'chat-bubble chat-bubble-done' : 'chat-bubble';
    const textHtml = msg.text.replace(/\n/g, '<br/>');
    return `<div class="chat-msg-zona"><div class="${bubbleClass}">${textHtml}<span class="msg-time">${msg.time || ''}</span></div></div>`;
  }

  function showConvo(convoIdx) {
    container.style.transition = 'opacity .4s';
    container.style.opacity = '0';
    setTimeout(() => {
      container.innerHTML = convos[convoIdx].msgs.map(buildMsg).join('');
      container.style.opacity = '1';
    }, 420);
  }

  // Cycle every 6 seconds
  setInterval(() => {
    idx = (idx + 1) % convos.length;
    showConvo(idx);
  }, 6000);
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
