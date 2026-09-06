#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Fodinae.Tools.Imgui;

/// <summary>
/// Расположение окон между запусками.
/// </summary>
/// <remarks>
/// ЗАЧЕМ. Раскладка собиралась заново каждый запуск: открыть нужные окна,
/// растащить их так, чтобы не перекрывали игру, свернуть лишние. Работа на
/// полминуты — и ровно та же работа в следующий запуск, и в следующий.
/// Инструмент, который каждый раз забывает, как им пользовались, заставляет
/// платить за вход снова и снова, и от этого им пользуются реже.
///
/// ПОЧЕМУ ФАЙЛ, А НЕ PlayerPrefs. <c>PlayerPrefs</c> в проекте запрещён:
/// настройки живут в <c>client_config.json</c> и проходят через миграции.
/// Но раскладка отладочных окон — не настройка игры, и тащить её в конфиг
/// значило бы завести ступень миграции, поле в пробнике настроек и ключи
/// локализации ради того, чего игрок никогда не увидит. Ровно этот случай в
/// проекте уже решён: рабочее место колориста хранит свой грейд отдельным
/// файлом в <c>persistentDataPath</c>. Здесь тот же путь.
///
/// ПОЧЕМУ КЛЮЧ ПО ЗАГОЛОВКУ. Номер окна раздаётся реестром при регистрации и
/// зависит от порядка, в котором подсистемы успели зарегистрироваться, — то
/// есть от того, что к делу не относится. Заголовок задан в конструкторе окна
/// и меняется только вместе с самим окном; тогда потеря раскладки —
/// правильное поведение, а не потеря.
/// </remarks>
public static class ToolLayoutStore
{
    private const string FileName = "tool_layout.json";

    private static readonly Dictionary<string, LayoutEntry> _Entries = [];
    private static bool _loaded;
    private static bool _dirty;
    private static float _scale;

    public static float Scale
    {
        get
        {
            EnsureLoaded();
            return _scale;
        }
        set
        {
            EnsureLoaded();
            _scale = IsFinite(value) ? Mathf.Clamp(value, 1f, 2.5f) : 0f;
            _dirty = true;
        }
    }

    public static string Path => System.IO.Path.Combine(Application.persistentDataPath, FileName);

    public static void Load(ToolWindow window)
    {
        EnsureLoaded();
        if (!_Entries.TryGetValue(window.Title, out LayoutEntry entry))
        {
            return;
        }

        var rect = new Rect(entry.X, entry.Y, entry.Width, entry.Height);

        // Сохранённое значение приходит из прошлого запуска и не обязано быть
        // осмысленным: экран мог смениться, а файл — пережить обрыв записи.
        // Мусор молча отбрасывается, окно остаётся на месте по умолчанию.
        // Границы экрана дальше наложит сам реестр.
        if (IsFinite(rect) && rect.width > 1f && rect.height > 1f)
        {
            window.Rect = rect;
        }

        // Видимость восстанавливается только у окон, которые можно закрыть.
        // Список инструментов закрыть нельзя, он и есть путь ко всем
        // остальным: сохранённое «скрыт» вернуло бы состояние, из которого нет
        // выхода ничем, кроме стирания файла вручную.
        if (window.CanRestoreVisibility)
        {
            window.Visible = entry.Visible;
        }

        window.Collapsed = entry.Collapsed;
    }

    /// <summary>Запоминает состояние окна в памяти. На диск не пишет.</summary>
    public static void Save(ToolWindow window)
    {
        if (!IsFinite(window.Rect))
        {
            return;
        }

        EnsureLoaded();
        _Entries[window.Title] = new LayoutEntry
        {
            Title = window.Title,
            X = window.Rect.x,
            Y = window.Rect.y,
            Width = window.Rect.width,
            Height = window.Rect.height,
            Visible = window.Visible,
            Collapsed = window.Collapsed,
        };
        _dirty = true;
    }

    /// <summary>Забывает окно: сброс расположения не должен переживать перезапуск.</summary>
    public static void Discard(ToolWindow window)
    {
        EnsureLoaded();
        if (_Entries.Remove(window.Title))
        {
            _dirty = true;
        }
    }

    /// <summary>
    /// Сброс на диск.
    /// </summary>
    /// <remarks>
    /// Отдельно от <see cref="Save"/>, потому что запись на диск дороже записи
    /// в память, а раскладка меняется каждым кадром перетаскивания. Реестр
    /// копит правки и сбрасывает их редко — при выключении оверлея и при
    /// выходе.
    ///
    /// Запись идёт через временный файл: обрыв на середине оставит прежнюю
    /// раскладку целой, а не наполовину переписанной.
    /// </remarks>
    public static void Flush()
    {
        if (!_dirty)
        {
            return;
        }

        var payload = new LayoutFile { Windows = new List<LayoutEntry>(_Entries.Values), Scale = _scale };
        string temporaryPath = Path + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonUtility.ToJson(payload, prettyPrint: true));
            File.Copy(temporaryPath, Path, overwrite: true);
            File.Delete(temporaryPath);
            _dirty = false;
        }
        catch (Exception exception)
        {
            // Потеря раскладки отладочных окон не стоит ни одного прерванного
            // кадра игры, поэтому здесь предупреждение, а не исключение.
            Debug.LogWarning($"[ToolLayoutStore] Раскладка не сохранена: {exception.Message}");
        }
    }

    private static void EnsureLoaded()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        try
        {
            if (!File.Exists(Path))
            {
                return;
            }

            var payload = JsonUtility.FromJson<LayoutFile>(File.ReadAllText(Path));
            if (payload?.Windows == null)
            {
                return;
            }

            foreach (LayoutEntry entry in payload.Windows)
            {
                if (!string.IsNullOrEmpty(entry.Title))
                {
                    _Entries[entry.Title] = entry;
                }
            }

            _scale = IsFinite(payload.Scale) && payload.Scale >= 1f && payload.Scale <= 2.5f
                ? payload.Scale : 0f;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[ToolLayoutStore] Раскладка не прочитана: {exception.Message}");
        }
    }

    private static bool IsFinite(Rect rect) =>
        IsFinite(rect.x) && IsFinite(rect.y) && IsFinite(rect.width) && IsFinite(rect.height);

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    /// <summary>Состояние одного окна на диске.</summary>
    [Serializable]
    private struct LayoutEntry
    {
        public string Title;
        public float X;
        public float Y;
        public float Width;
        public float Height;
        public bool Visible;
        public bool Collapsed;
    }

    /// <summary>
    /// Корень файла.
    /// </summary>
    /// <remarks>
    /// Класс, а не структура: <c>JsonUtility</c> разбирает корневой объект
    /// только в ссылочный тип.
    /// </remarks>
    [Serializable]
    private sealed class LayoutFile
    {
        public List<LayoutEntry>? Windows;
        public float Scale;
    }
}
