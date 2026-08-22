# Fodinae

2D-клиент для [Fodinae](https://github.com/MinesReborn) — реворк клиента давно почившей MMORPG Сергея Мячина.

## Быстрый старт

```bash
git clone https://github.com/MinesReborn/Fodinae.git
```

Открой через **Unity Hub** → `Open` → выбери папку. Unity сам подтянет зависимости. Открой `Assets/Scenes/Bootstrap.unity` и жми **Play**: Bootstrap (build index 0) грузит `MainMenu`, а тот — `MainGame` аддитивно.

### Сеть

Транспорт выбирается из `client_config.json` (создаётся в `Application.persistentDataPath/Config/` при первом запуске):

- `UseDummyConnection: true` — офлайн-заглушка `DummyConnection` для локального теста без сервера (режим по умолчанию);
- `UseDummyConnection: false` — реальное подключение через Darkar25 `TcpConnection` (MinesServerNetworking) к `ServerHost:ServerPort` (по умолчанию `127.0.0.1:7777`).

## Что уже есть

✅ Тайловый мир (один меш, 7 UV-каналов)  
✅ Сеть: Darkar25 MinesServerNetworking (`TcpConnection`), офлайн-заглушка `DummyConnection`  
✅ Динамический UI из серверных пакетов  
✅ FMOD аудио (3D-звук, снэпшоты, шины)  
✅ Инвентарь, HUD, экипировка  
✅ Чат (глобальный, локальный, всплывающий)  
✅ Миникарта + полная карта мира  
✅ Меню паузы с настройками  
✅ Effekseer-эффекты и звуковой пул  
✅ Программатор (визуальное программирование роботов)  
✅ Телепортация  
✅ Офлайн-режим без сервера  

## Технологии

**Unity 6** (6000.5.0f1), URP 2D, UI Toolkit, FMOD Studio, UniTask, Effekseer.  
Сеть: Git-пакеты [MinesServerNetworking](https://github.com/MinesReborn/MinesServerNetworking).  

Подробнее для разработчиков — в [**`AGENTS.md`**](AGENTS.md).

## Лицензия

[MIT](LICENSE)
