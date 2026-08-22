// ----------------------------------------------------
// FODINAE — Interactive Game Prototype & State Machine
// ----------------------------------------------------

let audioContext = null;
let isSfxOn = true;
let isGameUpdated = false;

// Состояние игрока и экспедиции
const playerState = {
  nickname: 'ШАХТЁР-774 [DVM]',
  token: 'fdn_tok_9948a204e18',
  hp: 840,
  maxHp: 1000,
  energy: 92,
  money: 14850,
  crystals: 340,
  basketCount: 12,
  basketMax: 25,
  missionBlocks: 142,
  missionTarget: 200,
  isAutoDig: false,
  isAggression: false,
  activeHotbarIndex: 0,
  activeProgCommand: '⬇ СКАН',
  isProgRunning: false,
  progInterval: null
};

// ----------------------------------------------------
// Синтез звука (Web Audio API)
// ----------------------------------------------------
function initAudio() {
  if (!audioContext) {
    audioContext = new (window.AudioContext || window.webkitAudioContext)();
  }
}

function toggleAudio() {
  isSfxOn = !isSfxOn;
  const el = document.getElementById('sfxStatus');
  if (el) el.innerText = isSfxOn ? '🔊 ЗВУК' : '🔇 ВЫКЛ';
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
  } else if (type === 'drill') {
    osc.type = 'sawtooth';
    osc.frequency.setValueAtTime(140, now);
    osc.frequency.linearRampToValueAtTime(280, now + 0.06);
    osc.frequency.linearRampToValueAtTime(80, now + 0.12);
    gain.gain.setValueAtTime(0.12, now);
    gain.gain.exponentialRampToValueAtTime(0.001, now + 0.12);
    osc.start(now);
    osc.stop(now + 0.12);
  }
}

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
// Машина состояний (State Machine)
// ----------------------------------------------------
let currentMode = 'menu';
let descentInterval = null;

function switchViewState(state) {
  currentMode = state;
  playSound('click');

  document.querySelectorAll('.dev-btn').forEach(btn => {
    btn.classList.remove('active');
    const txt = btn.innerText.toLowerCase();
    if (
      (state === 'splash' && txt.includes('заставка')) ||
      (state === 'auth' && txt.includes('авторизация')) ||
      (state === 'onboarding' && txt.includes('онбординг')) ||
      (state === 'menu' && txt.includes('орбита')) ||
      (state === 'descent' && txt.includes('спуск')) ||
      (state === 'ingame' && txt.includes('игра')) ||
      (state === 'pause' && txt.includes('пауза')) ||
      (state === 'reconnect' && txt.includes('обрыв'))
    ) {
      btn.classList.add('active');
    }
  });

  const viewport = document.getElementById('appViewport');
  const mainHeader = document.querySelector('.fa-header');
  const mainSidebar = document.querySelector('.genshin-sidebar');
  const mainFooter = document.querySelector('.fa-footer');

  const splashView = document.getElementById('splashView');
  const authView = document.getElementById('authView');
  const onboardingView = document.getElementById('onboardingView');
  const menuArea = document.getElementById('menuArea');
  const descentView = document.getElementById('descentView');
  const ingameView = document.getElementById('ingameView');
  const pauseView = document.getElementById('pauseView');
  const reconnectView = document.getElementById('reconnectView');

  const routeOrbit = document.getElementById('routeOrbit');
  const routeDescent = document.getElementById('routeDescent');
  const routeSurface = document.getElementById('routeSurface');

  if (splashView) splashView.classList.remove('active');
  if (authView) authView.classList.remove('active');
  if (onboardingView) onboardingView.classList.remove('active');
  if (menuArea) menuArea.style.display = 'none';
  if (descentView) descentView.classList.remove('active');
  if (ingameView) ingameView.classList.remove('active');
  if (pauseView) pauseView.classList.remove('active');
  if (reconnectView) reconnectView.classList.remove('active');

  if (routeOrbit) routeOrbit.classList.remove('active');
  if (routeDescent) routeDescent.classList.remove('active');
  if (routeSurface) routeSurface.classList.remove('active');

  viewport.dataset.state = state;

  if (state === 'splash') {
    if (mainHeader) mainHeader.style.display = 'none';
    if (mainSidebar) mainSidebar.style.display = 'none';
    if (mainFooter) mainFooter.style.display = 'none';
    if (splashView) splashView.classList.add('active');
  } else if (state === 'auth') {
    if (mainHeader) mainHeader.style.display = 'flex';
    if (mainSidebar) mainSidebar.style.display = 'none';
    if (mainFooter) mainFooter.style.display = 'none';
    if (authView) authView.classList.add('active');
  } else if (state === 'onboarding') {
    if (mainHeader) mainHeader.style.display = 'flex';
    if (mainSidebar) mainSidebar.style.display = 'none';
    if (mainFooter) mainFooter.style.display = 'none';
    if (onboardingView) onboardingView.classList.add('active');
  } else if (state === 'menu') {
    if (mainHeader) mainHeader.style.display = 'flex';
    if (mainSidebar) mainSidebar.style.display = 'flex';
    if (mainFooter) mainFooter.style.display = 'flex';
    if (menuArea) menuArea.style.display = 'flex';
    if (routeOrbit) routeOrbit.classList.add('active');

    document.getElementById('networkDot').className = 'network-dot';
    document.getElementById('networkText').innerText = 'СЕВЕРНАЯ ЕВРОПА (СТОКГОЛЬМ) · 38 МС';
  } else if (state === 'loading' || state === 'descent') {
    if (mainHeader) mainHeader.style.display = 'flex';
    if (mainSidebar) mainSidebar.style.display = 'none';
    if (mainFooter) mainFooter.style.display = 'flex';
    if (descentView) descentView.classList.add('active');
    if (routeDescent) routeDescent.classList.add('active');
  } else if (state === 'ingame') {
    if (mainHeader) mainHeader.style.display = 'none';
    if (mainSidebar) mainSidebar.style.display = 'none';
    if (mainFooter) mainFooter.style.display = 'none';
    if (ingameView) ingameView.classList.add('active');
    if (routeSurface) routeSurface.classList.add('active');
  } else if (state === 'pause') {
    if (ingameView) ingameView.classList.add('active');
    if (pauseView) pauseView.classList.add('active');
  } else if (state === 'reconnect') {
    if (mainHeader) mainHeader.style.display = 'flex';
    if (mainSidebar) mainSidebar.style.display = 'none';
    if (mainFooter) mainFooter.style.display = 'flex';
    if (reconnectView) reconnectView.classList.add('active');

    document.getElementById('networkDot').className = 'network-dot error';
    document.getElementById('networkText').innerText = 'СВЯЗЬ ПОТЕРЯНА · ПОВТОРНЫЙ ПОИСК...';
    startReconnectCount();
  }
}

// ----------------------------------------------------
// Заставка и Авторизация
// ----------------------------------------------------
function startExperienceFromSplash() {
  playSound('confirm');
  switchViewState('auth');
}

function generateRandomToken() {
  playSound('click');
  const hex = Math.random().toString(16).substring(2, 10);
  const tok = `fdn_tok_${hex}`;
  document.getElementById('inputAuthToken').value = tok;
  playerState.token = tok;
}

function submitAuthForm() {
  playSound('confirm');
  const nick = document.getElementById('inputMinerName').value.trim() || 'ШАХТЁР-774 [DVM]';
  playerState.nickname = nick;
  const hudNick = document.getElementById('hudMinerNick');
  if (hudNick) hudNick.innerText = nick.split(' ')[0];
  switchViewState('menu');
}

// ----------------------------------------------------
// Онбординг
// ----------------------------------------------------
let currentObStep = 1;

function updateOnboardingStepUI() {
  for (let i = 1; i <= 3; i++) {
    const pill = document.getElementById(`obStepPill${i}`);
    const content = document.getElementById(`obStep${i}`);
    if (pill) {
      pill.className = `onboarding-step-pill ${i === currentObStep ? 'active' : (i < currentObStep ? 'completed' : '')}`;
    }
    if (content) {
      content.className = `onboarding-step-content ${i === currentObStep ? 'active' : ''}`;
    }
  }

  const prevBtn = document.getElementById('btnObPrev');
  const nextBtn = document.getElementById('btnObNext');
  const title = document.getElementById('onboardingTitle');

  if (prevBtn) prevBtn.style.display = currentObStep > 1 ? 'block' : 'none';

  if (currentObStep === 1) {
    if (title) title.innerText = 'Шаг 1: Доступность и визуальный комфорт';
    if (nextBtn) nextBtn.innerText = 'ДАЛЕЕ (ГРАФИКА) →';
  } else if (currentObStep === 2) {
    if (title) title.innerText = 'Шаг 2: Графика и освещение';
    if (nextBtn) nextBtn.innerText = 'ДАЛЕЕ (УПРАВЛЕНИЕ) →';
  } else if (currentObStep === 3) {
    if (title) title.innerText = 'Шаг 3: Тактильный контроль и звук';
    if (nextBtn) nextBtn.innerText = 'ЗАВЕРШИТЬ КАЛИБРОВКУ ↗';
  }
}

function nextOnboardingStep() {
  playSound('click');
  if (currentObStep < 3) {
    currentObStep++;
    updateOnboardingStepUI();
  } else {
    playSound('confirm');
    startDescentSequence();
  }
}

function prevOnboardingStep() {
  playSound('click');
  if (currentObStep > 1) {
    currentObStep--;
    updateOnboardingStepUI();
  }
}

function applyUiScale(val) {
  playSound('click');
  document.documentElement.style.setProperty('--ui-scale', val);
}

function applyColorblindTheme(val) {
  playSound('click');
  document.body.classList.remove('theme-deuteranopia', 'theme-protanopia', 'theme-tritanopia', 'theme-high-contrast');
  if (val !== 'none') {
    document.body.classList.add(`theme-${val}`);
  }
}

function toggleReduceMotion(enabled) {
  playSound('click');
  if (enabled) document.body.classList.add('reduce-motion');
  else document.body.classList.remove('reduce-motion');
}

// ----------------------------------------------------
// Спуск в шахту
// ----------------------------------------------------
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
    p += 1.8;
    if (p >= 100) {
      p = 100;
      clearInterval(descentInterval);
      playSound('confirm');
      label.innerText = 'ГОТОВО! Вход в шахту выполнен.';
      setTimeout(() => switchViewState('ingame'), 300);
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
  }, 45);
}

function cancelDescentSequence() {
  if (descentInterval) clearInterval(descentInterval);
  switchViewState('menu');
}

// ----------------------------------------------------
// 2D Шахта и Игровой HUD
// ----------------------------------------------------
function initMineStrataGrid() {
  const container = document.getElementById('mineStrataGrid');
  if (!container) return;

  container.innerHTML = '';
  const cols = 24;
  const rows = 14;

  for (let r = 0; r < rows; r++) {
    for (let c = 0; c < cols; c++) {
      const tile = document.createElement('div');
      tile.className = 'mine-tile';

      const rand = Math.random();
      if (r < 3) {
        tile.classList.add('basalt');
        if (rand > 0.85) tile.classList.add('ore-titanium');
      } else if (r < 9) {
        tile.classList.add('redrock');
        if (rand > 0.82) tile.classList.add('ore-titanium');
        else if (rand > 0.74) tile.classList.add('ore-gold');
      } else {
        tile.classList.add('redrock');
        if (rand > 0.88) tile.classList.add('lava-crack');
        else if (rand > 0.76) tile.classList.add('ore-gold');
      }

      if ((r === 6 || r === 7) && (c === 11 || c === 12)) {
        tile.className = 'mine-tile mined-empty';
      }

      tile.addEventListener('click', () => mineTileBlock(tile));
      container.appendChild(tile);
    }
  }
}

function mineTileBlock(tile) {
  if (tile.classList.contains('mined-empty')) return;

  playSound('drill');

  let gainedOre = null;
  if (tile.classList.contains('ore-titanium')) gainedOre = 'Титан';
  else if (tile.classList.contains('ore-gold')) gainedOre = 'Золото';
  else if (tile.classList.contains('lava-crack')) {
    playSound('alert');
    simulateDamage(40);
  }

  tile.className = 'mine-tile mined-empty';

  if (playerState.missionBlocks < playerState.missionTarget) {
    playerState.missionBlocks++;
    const pct = Math.round((playerState.missionBlocks / playerState.missionTarget) * 100);
    const mFill = document.getElementById('missionFill');
    const mMetric = document.getElementById('missionMetric');
    if (mFill) mFill.style.width = pct + '%';
    if (mMetric) mMetric.innerText = `${playerState.missionBlocks} / ${playerState.missionTarget} блоков (${pct}%)`;
  }

  if (gainedOre && playerState.basketCount < playerState.basketMax) {
    playerState.basketCount++;
    updateBasketUI();
  }
}

function updateBasketUI() {
  const badge = document.getElementById('basketCapBadge');
  if (badge) badge.innerText = `${playerState.basketCount} / ${playerState.basketMax}`;
}

function toggleAutoDig() {
  playerState.isAutoDig = !playerState.isAutoDig;
  playSound('click');
  const btn = document.getElementById('btnAutoDig');
  const led = document.getElementById('ledAutoDig');
  if (btn && led) {
    if (playerState.isAutoDig) {
      btn.classList.add('active');
      led.classList.add('active');
      autoDigLoop();
    } else {
      btn.classList.remove('active');
      led.classList.remove('active');
    }
  }
}

function autoDigLoop() {
  if (!playerState.isAutoDig) return;
  const tiles = Array.from(document.querySelectorAll('.mine-tile:not(.mined-empty)'));
  if (tiles.length > 0) {
    const target = tiles[Math.floor(Math.random() * tiles.length)];
    mineTileBlock(target);
  }
  if (playerState.isAutoDig) setTimeout(autoDigLoop, 550);
}

function toggleAggression() {
  playerState.isAggression = !playerState.isAggression;
  playSound('alert');
  const btn = document.getElementById('btnAggression');
  const led = document.getElementById('ledAggression');
  if (btn && led) {
    if (playerState.isAggression) {
      btn.classList.add('active');
      led.classList.add('alert');
    } else {
      btn.classList.remove('active');
      led.classList.remove('alert');
    }
  }
}

function selectHotbarSlot(idx) {
  playerState.activeHotbarIndex = idx;
  playSound('click');
  document.querySelectorAll('#hotbarSlotsWrap .hotbar-slot').forEach((slot, i) => {
    if (i === idx) slot.classList.add('active');
    else slot.classList.remove('active');
  });
}

function simulateDamageOrHeal() {
  if (playerState.hp > 300) simulateDamage(150);
  else {
    playerState.hp = 1000;
    playSound('confirm');
    updateHpUI();
  }
}

function simulateDamage(amt) {
  playerState.hp = Math.max(0, playerState.hp - amt);
  playSound('alert');
  updateHpUI();
}

function updateHpUI() {
  const hpFill = document.getElementById('hpFill');
  const hpText = document.getElementById('hpText');
  const pct = Math.round((playerState.hp / playerState.maxHp) * 100);
  if (hpFill) hpFill.style.width = pct + '%';
  if (hpText) hpText.innerText = `${playerState.hp} / ${playerState.maxHp}`;
}

function claimBonus() {
  playSound('confirm');
  playerState.money += 500;
  playerState.crystals += 25;
  document.getElementById('hudMoney').innerText = playerState.money.toLocaleString();
  document.getElementById('hudCrystals').innerText = playerState.crystals;
}

// ----------------------------------------------------
// Инвентарь (9x6 = 54 слота)
// ----------------------------------------------------
const inventoryItems = [
  { name: 'Алмазный бур Tier-2', icon: '⛏', count: 1, rarity: 'legendary', desc: 'Усиленный бур со скоростью 0.3s. Режет базальт и красноскал.' },
  { name: 'Титановый слиток', icon: '◆', count: 18, rarity: 'rare', desc: 'Очищенный титан с глубин -2 400 м. Необходим для крафта дронов.' },
  { name: 'Самородок золота', icon: '★', count: 6, rarity: 'rare', desc: 'Высокопроводящий металл для схем программатора.' },
  { name: 'Кристалл кварца', icon: '⌬', count: 32, rarity: 'normal', desc: 'Базовый минерал верхних горизонтов.' },
  { name: 'Энергоячейка M1', icon: '🔋', count: 4, rarity: 'normal', desc: 'Восстанавливает 100% энергии реактора.' },
  { name: 'Гео-динамит T1', icon: '⚑', count: 8, rarity: 'rare', desc: 'Взрывчатка для направленной расчистки рудных пластов.' }
];

function initFullInventoryGrid() {
  const container = document.getElementById('fullInventoryGrid');
  if (!container) return;

  container.innerHTML = '';
  const totalSlots = 54;

  for (let i = 0; i < totalSlots; i++) {
    const cell = document.createElement('div');
    cell.className = 'inv-grid-cell';

    const item = inventoryItems[i];
    if (item) {
      cell.classList.add(item.rarity);
      cell.innerHTML = `
        <span class="inv-cell-icon">${item.icon}</span>
        <span class="inv-cell-count">${item.count}</span>
      `;
      cell.addEventListener('click', () => selectInventoryItem(item, cell));
    } else {
      cell.addEventListener('click', () => {
        playSound('click');
        document.querySelectorAll('.inv-grid-cell').forEach(c => c.classList.remove('selected'));
        cell.classList.add('selected');
      });
    }

    container.appendChild(cell);
  }
}

function selectInventoryItem(item, cell) {
  playSound('click');
  document.querySelectorAll('.inv-grid-cell').forEach(c => c.classList.remove('selected'));
  cell.classList.add('selected');

  const inspName = document.getElementById('inspName');
  const inspDesc = document.getElementById('inspDesc');
  if (inspName) inspName.innerText = item.name;
  if (inspDesc) inspDesc.innerText = item.desc;
}

function useCurrentItem() {
  playSound('confirm');
}

// ----------------------------------------------------
// Программатор (16x12)
// ----------------------------------------------------
function initProgrammatorGrid() {
  const container = document.getElementById('progGrid16x12');
  if (!container) return;

  container.innerHTML = '';
  const totalCells = 16 * 12;

  for (let i = 0; i < totalCells; i++) {
    const cell = document.createElement('div');
    cell.className = 'prog-grid-cell';
    cell.innerText = '·';
    cell.addEventListener('click', () => {
      playSound('click');
      cell.innerText = playerState.activeProgCommand.split(' ')[0];
      cell.style.color = 'var(--fa-cyan)';
    });
    container.appendChild(cell);
  }
}

function selectProgCommand(cmd) {
  playerState.activeProgCommand = cmd;
  playSound('click');
}

function clearProgrammatorGrid() {
  playSound('click');
  document.querySelectorAll('.prog-grid-cell').forEach(cell => {
    cell.innerText = '·';
    cell.style.color = 'var(--fa-dim)';
    cell.classList.remove('active-step');
  });
}

function runProgrammatorExec() {
  playSound('confirm');
  stopProgrammatorExec();
  playerState.isProgRunning = true;
  const status = document.getElementById('progStatus');
  if (status) {
    status.innerText = 'ВЫПОЛНЕНИЕ...';
    status.style.color = 'var(--fa-gold)';
  }

  const cells = Array.from(document.querySelectorAll('.prog-grid-cell'));
  let cur = 0;

  playerState.progInterval = setInterval(() => {
    cells.forEach(c => c.classList.remove('active-step'));
    if (cur < cells.length) {
      cells[cur].classList.add('active-step');
      playSound('hover');
      cur++;
    } else {
      stopProgrammatorExec();
    }
  }, 100);
}

function stopProgrammatorExec() {
  if (playerState.progInterval) clearInterval(playerState.progInterval);
  playerState.isProgRunning = false;
  const status = document.getElementById('progStatus');
  if (status) {
    status.innerText = 'ОСТАНОВЛЕН';
    status.style.color = 'var(--fa-green)';
  }
  document.querySelectorAll('.prog-grid-cell').forEach(c => c.classList.remove('active-step'));
}

// ----------------------------------------------------
// Чат
// ----------------------------------------------------
function switchChatTab(btn, tab) {
  playSound('click');
  document.querySelectorAll('.chat-tab-btn').forEach(b => b.classList.remove('active'));
  btn.classList.add('active');
}

function handleChatInputKeyDown(e) {
  if (e.key === 'Enter') sendChatMessage();
}

function sendChatMessage() {
  const input = document.getElementById('chatInputField');
  if (!input) return;
  const msg = input.value.trim();
  if (!msg) return;

  playSound('click');
  const box = document.getElementById('chatMessagesBox');
  if (box) {
    const row = document.createElement('div');
    row.className = 'chat-row';
    row.innerHTML = `<span class="chat-author">[ВЫ]:</span> ${escapeHtml(msg)}`;
    box.appendChild(row);
    box.scrollTop = box.scrollHeight;
  }
  input.value = '';

  setTimeout(() => {
    if (box) {
      const reply = document.createElement('div');
      reply.className = 'chat-row';
      reply.innerHTML = `<span class="chat-author clan">[DVM_BOT]:</span> Принято: "${escapeHtml(msg)}". Координаты зафиксированы.`;
      box.appendChild(reply);
      box.scrollTop = box.scrollHeight;
      playSound('hover');
    }
  }, 700);
}

function escapeHtml(text) {
  return text.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");
}

// ----------------------------------------------------
// Торговец
// ----------------------------------------------------
function buyItem(name, cost) {
  if (playerState.money >= cost) {
    playerState.money -= cost;
    playSound('confirm');
    document.getElementById('hudMoney').innerText = playerState.money.toLocaleString();
  } else {
    playSound('alert');
  }
}

function sellAllOre() {
  const gain = playerState.basketCount * 140;
  playerState.money += gain;
  playerState.basketCount = 0;
  playSound('confirm');
  document.getElementById('hudMoney').innerText = playerState.money.toLocaleString();
  updateBasketUI();
}

// ----------------------------------------------------
// Пауза и Реконнект
// ----------------------------------------------------
function resumeGameFromPause() {
  playSound('confirm');
  switchViewState('ingame');
}

let recTimer = null;
function startReconnectCount() {
  let sec = 5;
  const display = document.getElementById('reconnectTimer');
  if (recTimer) clearInterval(recTimer);
  recTimer = setInterval(() => {
    sec--;
    if (display) display.innerText = `00:0${sec}`;
    if (sec <= 0) {
      sec = 5;
      playSound('hover');
    }
  }, 1000);
}

function startOfflineDummy() {
  alert('Запущен Dummy Offline Transport: Локальный мир без подключения к сети.');
  switchViewState('ingame');
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
  if (!container) return;
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
  if (!log) return;
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

  if (box) box.style.display = 'flex';
  if (btn) {
    btn.disabled = true;
    btn.innerText = 'СКАЧИВАНИЕ ПАТЧА...';
  }

  let p = 0;
  const interval = setInterval(() => {
    p += 2;
    if (bar) bar.style.width = p + '%';
    if (percent) percent.innerText = p + '%';
    if (p >= 100) {
      clearInterval(interval);
      playSound('confirm');
      isGameUpdated = true;
      if (btn) {
        btn.innerText = 'ПЕРЕЗАПУСТИТЬ КЛИЕНТ';
        btn.disabled = false;
        btn.onclick = () => {
          alert('Клиент успешно обновлен до версии v0.9.0! Доступ к шахтам открыт.');
          closeModal('mandatoryUpdateModal');
          const banner = document.getElementById('updateAlertBanner');
          if (banner) banner.style.display = 'none';
          const fLink = document.getElementById('footerVersionStatus');
          if (fLink) {
            fLink.innerText = 'ВЕРСИЯ КЛИЕНТА 0.9.0 (АКТУАЛЬНА)';
            fLink.classList.remove('alert');
          }
        };
      }
    }
  }, 35);
}

// ----------------------------------------------------
// Клавиатурные шорткаты
// ----------------------------------------------------
window.addEventListener('keydown', (e) => {
  if (e.key === 'Escape') {
    const activeModal = document.querySelector('.modal-overlay.active');
    if (activeModal) {
      activeModal.classList.remove('active');
    } else if (currentMode === 'ingame') {
      switchViewState('pause');
    } else if (currentMode === 'pause') {
      switchViewState('ingame');
    }
  } else if (e.key === 'Tab') {
    if (currentMode === 'ingame') {
      e.preventDefault();
      const invModal = document.getElementById('inventoryModal');
      if (invModal && invModal.classList.contains('active')) closeModal('inventoryModal');
      else openModal('inventoryModal');
    }
  } else if (e.key === 'e' || e.key === 'E' || e.key === 'у' || e.key === 'У') {
    if (currentMode === 'ingame' && !document.querySelector('.modal-overlay.active')) toggleAutoDig();
  } else if (e.key === 'l' || e.key === 'L' || e.key === 'д' || e.key === 'Д') {
    if (currentMode === 'ingame' && !document.querySelector('.modal-overlay.active')) toggleAggression();
  } else if (e.key === 'p' || e.key === 'P' || e.key === 'з' || e.key === 'З') {
    if (currentMode === 'ingame' && !document.querySelector('.modal-overlay.active')) openModal('programmatorModal');
  } else if (e.key === 'Enter') {
    if (currentMode === 'ingame' && !document.querySelector('.modal-overlay.active')) openModal('chatModal');
  } else if (e.key >= '1' && e.key <= '9') {
    if (currentMode === 'ingame' && !document.querySelector('.modal-overlay.active')) {
      selectHotbarSlot(parseInt(e.key, 10) - 1);
    }
  }
});

// ----------------------------------------------------
// Инициализация
// ----------------------------------------------------
document.addEventListener('DOMContentLoaded', () => {
  initMineStrataGrid();
  initFullInventoryGrid();
  initProgrammatorGrid();

  document.querySelectorAll('button, .btn-secondary-action, .side-icon-btn, .footer-action-link, .tab-item-btn, .user-pill, .chronicle-item, .news-ticker-wrap, .hotbar-slot, .dev-btn').forEach(el => {
    el.addEventListener('mouseenter', () => playSound('hover'));
  });
});
