    // ----------------------------------------------------
    // Синтезированные звуки (Web Audio API)
    // ----------------------------------------------------
    let audioContext = null;
    let isSfxOn = true;
    let isGameUpdated = false;

    function initAudio() {
      if (!audioContext) {
        audioContext = new (window.AudioContext || window.webkitAudioContext)();
      }
    }

    function toggleAudio() {
      isSfxOn = !isSfxOn;
      document.getElementById('sfxStatus').innerText = isSfxOn ? '🔊 ЗВУК' : '🔇 ВЫКЛ';
      playSound('click');
    }

    function playSound(type) {
      if (!isSfxOn) return;
      initAudio();
      if (!audioContext) return;

      const now = audioContext.currentTime;
      const osc = audioContext.createOscillator();
      const gain = audioContext.createGain();

      osc.connect(gain);
      gain.connect(audioContext.destination);

      if (type === 'hover') {
        osc.type = 'sine';
        osc.frequency.setValueAtTime(480, now);
        osc.frequency.exponentialRampToValueAtTime(720, now + 0.035);
        gain.gain.setValueAtTime(0.03, now);
        gain.gain.exponentialRampToValueAtTime(0.001, now + 0.035);
        osc.start(now);
        osc.stop(now + 0.035);
      } else if (type === 'click') {
        osc.type = 'triangle';
        osc.frequency.setValueAtTime(320, now);
        osc.frequency.exponentialRampToValueAtTime(140, now + 0.07);
        gain.gain.setValueAtTime(0.12, now);
        gain.gain.exponentialRampToValueAtTime(0.001, now + 0.07);
        osc.start(now);
        osc.stop(now + 0.07);
      } else if (type === 'confirm') {
        osc.type = 'sine';
        osc.frequency.setValueAtTime(380, now);
        osc.frequency.setValueAtTime(570, now + 0.06);
        gain.gain.setValueAtTime(0.14, now);
        gain.gain.exponentialRampToValueAtTime(0.001, now + 0.2);
        osc.start(now);
        osc.stop(now + 0.2);
      } else if (type === 'alert') {
        osc.type = 'sawtooth';
        osc.frequency.setValueAtTime(220, now);
        osc.frequency.setValueAtTime(180, now + 0.1);
        gain.gain.setValueAtTime(0.16, now);
        gain.gain.exponentialRampToValueAtTime(0.001, now + 0.25);
        osc.start(now);
        osc.stop(now + 0.25);
      }
    }

    document.addEventListener('DOMContentLoaded', () => {
      document.querySelectorAll('button, .btn-secondary-action, .side-icon-btn, .footer-action-link, .tab-item-btn, .user-pill, .chronicle-item, .news-ticker-wrap').forEach(el => {
        el.addEventListener('mouseenter', () => playSound('hover'));
      });
    });

    // ----------------------------------------------------
    // Живой тикер хроники экспедиции
    // ----------------------------------------------------
    const newsTickerItems = [
      "В секторе Hades-Alpha активирован глубинный бур -2 480 м // Кластер стабилен",
      "Сектор Tartarus-02: Зафиксирован выброс лавы на горизонте -1 920 м. Повышенная опасность",
      "Турнир 'Гонка буровых установок' стартует через 4 дня // Призовой фонд 100 000 CR",
      "Рудный пласт 'Красноскал': Зафиксированы залежи титана на глубине 3 000 м",
      "Сетевой протокол MinesProtocol v7 готов к развертыванию на всех узлах"
    ];
    let tickerIndex = 0;
    setInterval(() => {
      tickerIndex = (tickerIndex + 1) % newsTickerItems.length;
      const t = document.getElementById('tickerText');
      if (t) t.innerText = newsTickerItems[tickerIndex];
    }, 6000);

    // ----------------------------------------------------
    // Логика состояний экранов
    // ----------------------------------------------------
    let currentMode = 'menu';
    let descentInterval = null;

    function switchViewState(state) {
      currentMode = state;
      playSound('click');

      document.querySelectorAll('.dev-btn').forEach(btn => {
        btn.classList.remove('active');
        if (btn.innerText.toLowerCase().includes(state)) btn.classList.add('active');
      });

      const viewport = document.getElementById('appViewport');
      const menuArea = document.getElementById('menuArea');
      const descentView = document.getElementById('descentView');
      const reconnectView = document.getElementById('reconnectView');

      const routeOrbit = document.getElementById('routeOrbit');
      const routeDescent = document.getElementById('routeDescent');
      const routeSurface = document.getElementById('routeSurface');

      routeOrbit.classList.remove('active');
      routeDescent.classList.remove('active');
      routeSurface.classList.remove('active');

      viewport.dataset.state = state;

      if (state === 'menu') {
        menuArea.style.display = 'flex';
        descentView.classList.remove('active');
        reconnectView.classList.remove('active');
        routeOrbit.classList.add('active');

        document.getElementById('networkDot').className = 'network-dot';
        document.getElementById('networkText').innerText = 'СЕВЕРНАЯ ЕВРОПА (СТОКГОЛЬМ) · 38 МС';
      } else if (state === 'loading' || state === 'descent') {
        menuArea.style.display = 'none';
        descentView.classList.add('active');
        reconnectView.classList.remove('active');
        routeDescent.classList.add('active');
      } else if (state === 'reconnect') {
        menuArea.style.display = 'none';
        descentView.classList.remove('active');
        reconnectView.classList.add('active');

        document.getElementById('networkDot').className = 'network-dot error';
        document.getElementById('networkText').innerText = 'СВЯЗЬ ПОТЕРЯНА · ПОВТОРНЫЙ ПОИСК...';
        startReconnectCount();
      }
    }

    function handleDeployClick() {
      if (!isGameUpdated) {
        playSound('alert');
        openMandatoryUpdateModal();
      } else {
        startDescentSequence();
      }
    }

    function startDescentSequence() {
      switchViewState('descent');
      closeModal('serverBrowserModal');

      let p = 0;
      const fill = document.getElementById('descentProgressFill');
      const metric = document.getElementById('descentSpeedMetric');
      const tag = document.getElementById('descentPhaseNum');
      const label = document.getElementById('descentAssetLabel');

      if (descentInterval) clearInterval(descentInterval);

      const phases = [
        { pct: 15, tag: 'ФАЗА СПУСКА 01 / 05', label: 'Авторизация и валидация токена шахтёра...', id: 'dp-1' },
        { pct: 35, tag: 'ФАЗА СПУСКА 02 / 05', label: 'Потоковая передача World Manifest (Регион 32x32)...', id: 'dp-2' },
        { pct: 70, tag: 'ФАЗА СПУСКА 03 / 05', label: 'ClientAssetLoader: Скачивание текстур пород и FMOD банков...', id: 'dp-3' },
        { pct: 90, tag: 'ФАЗА СПУСКА 04 / 05', label: 'SingleMeshTerrainRenderer: Компиляция меша и UV атласа...', id: 'dp-4' },
        { pct: 100, tag: 'ФАЗА СПУСКА 05 / 05', label: 'Синхронизация позиции шахтёра на горизонте высадки...', id: 'dp-5' }
      ];

      descentInterval = setInterval(() => {
        p += 1.4;
        if (p > 100) {
          p = 100;
          clearInterval(descentInterval);
          playSound('confirm');
          label.innerText = 'ГОТОВО! Вход в шахту выполнен.';
          document.getElementById('routeSurface').classList.add('active');
        }

        fill.style.width = p + '%';
        const currentMB = Math.round((p / 100) * 421);
        metric.innerText = `${currentMB} / 421 МБ (26.4 МБ/с)`;

        for (let ph of phases) {
          if (p >= ph.pct) {
            tag.innerText = ph.tag;
            label.innerText = ph.label;
            document.querySelectorAll('.phase-step').forEach(el => el.className = 'phase-step done');
            const target = document.getElementById(ph.id);
            if (target) target.className = 'phase-step current';
          }
        }
      }, 60);
    }

    function cancelDescentSequence() {
      if (descentInterval) clearInterval(descentInterval);
      switchViewState('menu');
    }

    let recTimer = null;
    function startReconnectCount() {
      let sec = 5;
      const display = document.getElementById('reconnectTimer');
      if (recTimer) clearInterval(recTimer);
      recTimer = setInterval(() => {
        sec--;
        display.innerText = `00:0${sec}`;
        if (sec <= 0) {
          sec = 5;
          playSound('hover');
        }
      }, 1000);
    }

    function startOfflineDummy() {
      alert('Запущен Dummy Offline Transport: Локальный мир без подключения к сети.');
      switchViewState('menu');
    }

    function confirmQuit() {
      playSound('click');
      if (confirm('Выйти из игры на рабочий стол?')) {
        alert('Клиент закрыт.');
      }
    }

    // ----------------------------------------------------
    // Модальные окна
    // ----------------------------------------------------
    function openModal(id) {
      playSound('click');
      const el = document.getElementById(id);
      if (el) el.classList.add('active');
    }

    function closeModal(id) {
      playSound('click');
      const el = document.getElementById(id);
      if (el) el.classList.remove('active');
    }

    function openMandatoryUpdateModal() {
      playSound('alert');
      openModal('mandatoryUpdateModal');
    }

    function switchGenshinTab(tabId, trigger) {
      playSound('hover');
      const container = trigger.closest('.genshin-settings-layout');
      container.querySelectorAll('.genshin-nav-btn').forEach(b => b.classList.remove('active'));
      container.querySelectorAll('.tab-panel').forEach(p => p.classList.remove('active'));

      trigger.classList.add('active');
      const target = document.getElementById(tabId);
      if (target) target.classList.add('active');
    }

    function selectServerDetail(row, name, region, online, ping, depth, seed, hazard) {
      playSound('click');
      row.parentElement.querySelectorAll('tr').forEach(r => r.classList.remove('selected'));
      row.classList.add('selected');

      document.getElementById('srvDetailName').innerText = name.toUpperCase();
      document.getElementById('srvDetailDepth').innerText = depth;
      document.getElementById('srvDetailSeed').innerText = seed;
      document.getElementById('srvDetailPing').innerText = ping;
      document.getElementById('srvDetailHazard').innerText = hazard;
    }

    function runRepairLog() {
      playSound('click');
      const log = document.getElementById('repairLogBox');
      log.innerHTML = '<div class="term-row info">[00:00:01] Запуск глубокого сканирования кэша и диска...</div>';
      setTimeout(() => {
        log.innerHTML += '<div class="term-row ok">[00:00:02] Проверка CRC32 чанков карты: Без повреждений (4 096 / 4 096).</div>';
      }, 400);
      setTimeout(() => {
        log.innerHTML += '<div class="term-row ok">[00:00:03] FMOD Audio банки проверены: Синхронизированы с сервером.</div>';
      }, 800);
      setTimeout(() => {
        log.innerHTML += '<div class="term-row ok" style="font-weight:700;">[00:00:04] КЛИЕНТ ПОЛНОСТЬЮ ГОТОВ К РАБОТЕ.</div>';
        playSound('confirm');
      }, 1200);
    }

    function startUpdateDownloadProcess() {
      playSound('click');
      const box = document.getElementById('updateDownloadBlock');
      const bar = document.getElementById('updateProgressBar');
      const percent = document.getElementById('updatePercent');
      const btn = document.getElementById('btnStartUpdate');

      box.style.display = 'flex';
      btn.disabled = true;
      btn.innerText = 'СКАЧИВАНИЕ ПАТЧА...';

      let p = 0;
      const interval = setInterval(() => {
        p += 2;
        bar.style.width = p + '%';
        percent.innerText = p + '%';
        if (p >= 100) {
          clearInterval(interval);
          playSound('confirm');
          isGameUpdated = true;
          btn.innerText = 'ПЕРЕЗАПУСТИТЬ КЛИЕНТ';
          btn.disabled = false;
          btn.onclick = () => {
            alert('Клиент успешно обновлен до версии v0.9.0! Доступ к шахтам открыт.');
            closeModal('mandatoryUpdateModal');
            document.getElementById('updateAlertBanner').style.display = 'none';
            const fLink = document.getElementById('footerVersionStatus');
            fLink.innerText = 'ВЕРСИЯ КЛИЕНТА 0.9.0 (АКТУАЛЬНА)';
            fLink.classList.remove('alert');
          };
        }
      }, 35);
    }

    window.addEventListener('keydown', (e) => {
      if (e.key === 'Escape') {
        document.querySelectorAll('.modal-overlay.active').forEach(m => m.classList.remove('active'));
      }
    });
