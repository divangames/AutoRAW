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

  if (sections[0]) {
    setTimeout(() => triggerSection(sections[0]), 200);
  }
}

function triggerSection(sec) {
  if (sec.dataset.triggered) return;
  sec.dataset.triggered = '1';
  sec.classList.add('is-active');

  sec.querySelectorAll('[data-d]').forEach(el => {
    const delay = parseInt(el.dataset.d || '0', 10);
    el.style.animationDelay = delay + 'ms';
  });
}

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

function initRevealAnimations() {
  const wrap = document.getElementById('scrollWrap');
  if (!wrap) return;

  const io = new IntersectionObserver((entries) => {
    entries.forEach(e => {
      if (!e.isIntersecting) {
        e.target.dataset.triggered = '';
        e.target.classList.remove('is-active');
      }
    });
  }, { root: wrap, threshold: 0.05 });

  wrap.querySelectorAll('.snap-sec').forEach(s => io.observe(s));
}

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

function initVideoSpeed() {
  const bindSpeed = (iframe) => {
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
  };

  bindSpeed(document.getElementById('heroVideo'));
  bindSpeed(document.getElementById('ctaVideo'));
}

function initTypingChat() {
  const container = document.getElementById('chatMessages');
  if (!container) return;

  const convos = [
    {
      msgs: [
        { type: 'zona', text: '👋 Я ZONA. Веду пакет: референс + ваши правки из редактора. Скоро сама закрою цикл — 1С и описание по текстам копирайтера.', time: '09:41' },
        { type: 'zona', text: '🛠 Сейчас в работе: фиксы фото товаров и автоматические экшены с дроплетами Photoshop. Обещаю ускорить вас в разы!', time: '09:41' },
        { type: 'sys',  text: '⏯ Пакет запущен — 160 файлов · профиль Кроссовки' },
        { type: 'done', text: '🌊 Готово!\n\n✅ Успешно: 160, ошибок: 0, всего: 160. Время: 4:22.\nПрофиль: Кроссовки', time: '09:46' },
      ]
    },
    {
      msgs: [
        { type: 'zona', text: '🎀 Редактор кадра сохранён для профиля — все 01…08 получат ту же геометрию в пакете.', time: '10:12' },
        { type: 'zona', text: '🔜 Дальше: выгрузка в 1С без ручного копирования. Описание подберу по вашим материалам копирайтера.', time: '10:12' },
        { type: 'sys',  text: '⏯ Пакет запущен — 48 файлов' },
        { type: 'done', text: '🌟 Всё чисто.\n\n✅ Успешно: 48, ошибок: 0, всего: 48. Время: 1:12.\nПрофиль: Кроссовки', time: '10:14' },
      ]
    },
    {
      msgs: [
        { type: 'zona', text: '🌸 Подпапку «Товар» можно пропустить в редакторе — в пакет она не попадёт. Удобно для брака.', time: '14:05' },
        { type: 'zona', text: '💜 RAW большие — но я терпелива. Photoshop-экшены скоро подключу сама.', time: '14:05' },
        { type: 'sys',  text: '⏯ Пакет запущен — 72 файла' },
        { type: 'done', text: '🎯 Почти идеально.\n\n✅ Успешно: 71, ошибок: 1, всего: 72. Время: 3:08.\nПрофиль: Кроссовки', time: '14:09' },
      ]
    },
    {
      msgs: [
        { type: 'zona', text: '💬 Пока я в журнале и Telegram. Когда подключу 1С — напишу здесь же, своими словами.', time: '18:30' },
        { type: 'zona', text: '🧠 Понимаю: готово, ошибка, пауза, отмена — без шаблонов.', time: '18:30' },
        { type: 'sys',  text: '🚫 Обработка отменена пользователем' },
        { type: 'zona', text: '🌊 Прибой затих. До отмены: 34 из 96.\nПрофиль: Кроссовки', time: '18:31' },
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

  setInterval(() => {
    idx = (idx + 1) % convos.length;
    showConvo(idx);
  }, 6000);
}

function initParallax() {
  const wrap = document.getElementById('scrollWrap');
  const char = document.getElementById('heroChar');
  if (!wrap || !char) return;

  let ticking = false;
  wrap.addEventListener('scroll', () => {
    if (!ticking) {
      requestAnimationFrame(() => {
        const scrolled = wrap.scrollTop;
        char.style.transform = `translateY(${scrolled * 0.08}px)`;
        ticking = false;
      });
      ticking = true;
    }
  }, { passive: true });
}

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
