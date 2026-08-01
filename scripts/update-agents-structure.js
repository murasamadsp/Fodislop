#!/usr/bin/env node
const fs = require('fs');
const path = require('path');

const AGENTS_MD_PATH = path.join(__dirname, '..', 'AGENTS.md');
const ROOT_DIR = path.join(__dirname, '..');

// Исключаемые папки и файлы
const EXCLUDE_DIRS = new Set([
  '.git', '.idea', '.vs', 'Library', 'Temp', 'Logs',
  'UserSettings', 'obj', 'bin', 'Build', 'Builds', 'Cache'
]);

const EXCLUDE_FILES = new Set([
  '.DS_Store', '.meta', '.tmp', 'thumbs.db'
]);

// Папки, внутренности которых сжимаются до 1 строки
const COLLAPSED_DIRS = new Map([
  ['Assets/Plugins/FMOD', '# Vendored пакет'],
  ['Assets/Plugins/UniTask', '# Vendored пакет'],
  ['Assets/Scripts/VContainer', '# Vendored VContainer 1.19'],
  ['Assets/Scripts/MgGifDecoder', '# GIF-декодер'],
  ['Assets/TextMesh Pro', '# TMP шрифты и шейдеры'],
  ['Assets/Resources/Programmator', '# 166 ассетов/изображений'],
  ['Assets/Resources/Styles', '# USS стили UI'],
  ['Assets/Resources/Skills', '# Иконки скиллов'],
  ['Assets/StreamingAssets', '# FMOD банки и локальные карты'],
  ['Assets/Textures', '# Текстуры тайлов, сущностей, UI и эффектов'],
  ['Assets/UI Toolkit', '# UI Toolkit темы и PanelSettings']
]);

// Комментарии к папкам
const DIR_COMMENTS = new Map([
  ['Assets/Editor', '# Скрипты редактора и билдеры (BuildScript, FmodBankBuilder, MapbConverter)'],
  ['Assets/Plugins', '# Vendored DLL (UniTask, NetCoreServer, ZstdSharp, SmartFormat)'],
  ['Assets/Prefabs', '# Префабы сущностей (Player.prefab)'],
  ['Assets/Scenes', '# Игровые сцены (MainGame.unity, Tests/TextureStorageTestScene.unity)'],
  ['Assets/Scenes/Tests', '# Тестовые сцены'],
  ['Assets/Scripts/AssetPipeline', '# Ассет-пайплайн (ClientAssetLoader, PersistentAssetCache)'],
  ['Assets/Scripts/Audio', '# Аудио подсистема FMOD'],
  ['Assets/Scripts/Audio/Backend', '# Низкоуровневый FMOD API и AudioSystem'],
  ['Assets/Scripts/Audio/Core', '# Аудио-слои и хендлы воспроизведения'],
  ['Assets/Scripts/Audio/Spatial', '# Пространственное 3D-аудио и аудио-зоны'],
  ['Assets/Scripts/Core', '# Системная инфраструктура и VContainer LifetimeScope'],
  ['Assets/Scripts/Core/Interfaces', '# Интерфейсы сервисов'],
  ['Assets/Scripts/Effekseer', '# Эффекты Effekseer'],
  ['Assets/Scripts/Game', '# Игровые сущности и менеджеры'],
  ['Assets/Scripts/Game/Managers', '# Менеджеры игры (MapManager, RobotManager, PackManager)'],
  ['Assets/Scripts/Networking', '# Сетевой слой и диспетчер пакетов'],
  ['Assets/Scripts/Player', '# Логика игрока и камера'],
  ['Assets/Scripts/UI', '# UI Toolkit контроллеры, окна и программатор'],
  ['Assets/Scripts/UI/Builders', '# UI-билдеры сетевых пакетов'],
  ['Assets/Scripts/UI/HUD', '# HUD и инвентарь'],
  ['Assets/Scripts/World', '# Мир и тайловый рендеринг (SingleMeshTerrainRenderer)'],
  ['Assets/Settings', '# URP и Renderer2D конфиги'],
  ['Assets/Shaders', '# URP 2D Шейдеры'],
  ['scripts', '# Вспомогательные Python и Bash скрипты'],
  ['.agents', '# Правила и навыки для AI-ассистентов Antigravity / Codex']
]);

// Комментарии к отдельным ключевым файлам
const FILE_COMMENTS = new Map([
  ['BuildScript.cs', '# Сборка билдов'],
  ['CsProjFix.cs', '# csproj постпроцессор'],
  ['FmodBankBuilder.cs', '# Синк FMOD-банков'],
  ['MapbConverter.cs', '# Конвертер серверных карт'],
  ['SingleMeshTerrainRenderer.cs', '# Один меш на весь террейн, 7 UV-каналов'],
  ['ClientAssetLoader.cs', '# Загрузка ассетов с сервера/локально'],
  ['PersistentAssetCache.cs', '# Стойкий кэш ассетов (ETag, MD5)'],
  ['AudioSystem.cs', '# Синглтон-контроллер аудио'],
  ['FmodAudioBackend.cs', '# Низкоуровневый FMOD API'],
  ['GameLifetimeScope.cs', '# LifetimeScope для сцены: регистрация DI'],
  ['GameManager.cs', '# Точка входа в игру'],
  ['MapManager.cs', '# Жизненный цикл мира'],
  ['MapStorage.cs', '# Хранилище карты (.mapb)'],
  ['RobotManager.cs', '# Управление роботами'],
  ['NetworkService.cs', '# Подписка/отписка пакетов'],
  ['PacketHandler.cs', '# Диспетчер сетевых пакетов'],
  ['PlayerMovementController.cs', '# Ввод, движение, копка'],
  ['pre-commit-lint.sh', '# Прекоммит-хук: Roslyn-анализаторы'],
  ['update-agents-structure.js', '# Авто-обновление структуры в AGENTS.md']
]);

function buildCleanTree() {
  const lines = [];

  function processPath(targetRelPath) {
    const fullPath = path.join(ROOT_DIR, targetRelPath);
    if (!fs.existsSync(fullPath)) return;

    const stat = fs.statSync(fullPath);
    if (stat.isDirectory()) {
      const comment = DIR_COMMENTS.get(targetRelPath) || '';
      lines.appendLine(targetRelPath + '/', comment, 0);
      walkDirectory(fullPath, 1, targetRelPath);
    } else {
      const comment = FILE_COMMENTS.get(path.basename(targetRelPath)) || '';
      lines.appendLine(targetRelPath, comment, 0);
    }
    lines.push('');
  }

  lines.appendLine = function(nameStr, commentStr, indentLevel) {
    const indent = '  '.repeat(indentLevel);
    let line = indent + nameStr;
    if (commentStr) {
      line = line.padEnd(32, ' ') + ' ' + commentStr;
    }
    this.push(line);
  };

  function walkDirectory(currentDir, indentLevel, relBasePath) {
    let entries;
    try {
      entries = fs.readdirSync(currentDir);
    } catch (e) {
      return;
    }

    const dirs = [];
    const files = [];

    for (const item of entries) {
      if (EXCLUDE_DIRS.has(item) || EXCLUDE_FILES.has(item) || item.endsWith('.meta')) {
        continue;
      }
      const itemFull = path.join(currentDir, item);
      const stat = fs.statSync(itemFull);
      if (stat.isDirectory()) {
        dirs.push(item);
      } else {
        files.push(item);
      }
    }

    dirs.sort();
    files.sort();

    for (const d of dirs) {
      const relPath = path.normalize(path.join(relBasePath, d));

      // Сворачивание вендорных/ресурсных директорий
      if (COLLAPSED_DIRS.has(relPath)) {
        const comment = COLLAPSED_DIRS.get(relPath);
        lines.appendLine(d + '/', comment, indentLevel);
        continue;
      }

      const comment = DIR_COMMENTS.get(relPath) || '';
      lines.appendLine(d + '/', comment, indentLevel);
      walkDirectory(path.join(currentDir, d), indentLevel + 1, relPath);
    }

    // Листинг файлов
    for (const f of files) {
      const relPath = path.normalize(path.join(relBasePath, f));
      const comment = FILE_COMMENTS.get(f) || '';
      lines.appendLine(f, comment, indentLevel);
    }
  }

  processPath('Assets');
  processPath('scripts');
  processPath('.agents');

  return lines.join('\n').trim();
}

function updateAgentsMd() {
  if (!fs.existsSync(AGENTS_MD_PATH)) {
    console.error(`Error: ${AGENTS_MD_PATH} not found.`);
    process.exit(1);
  }

  const content = fs.readFileSync(AGENTS_MD_PATH, 'utf-8');
  const cleanTree = buildCleanTree();
  const newSection = `## 2. Структура проекта\n\n\`\`\`text\n${cleanTree}\n\`\`\``;

  const pattern = /## 2\. Структура проекта\s+```text\n[\s\S]*?```/;
  if (!pattern.test(content)) {
    console.error("Error: Section '## 2. Структура проекта' not found in AGENTS.md");
    process.exit(1);
  }

  const updated = content.replace(pattern, newSection);

  if (updated !== content) {
    fs.writeFileSync(AGENTS_MD_PATH, updated, 'utf-8');
    console.log('AGENTS.md section 2 (Project Structure) successfully updated via Node.js!');
  } else {
    console.log('AGENTS.md section 2 is already up to date.');
  }
}

updateAgentsMd();
