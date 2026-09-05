#nullable enable

using System;
using System.Collections.Generic;
using Fodinae.Rendering.PostProcessing.Scopes;
using Fodinae.Tools.Imgui;
using Fodinae.Tools.Imgui.Windows;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Fodinae.Rendering.PostProcessing.Workbench;

/// <summary>
/// Рабочее место колориста: состояние грейда и три его окна.
/// </summary>
/// <remarks>
/// Собственной отрисовки здесь больше нет. Окна живут в общей системе
/// инструментов (<see cref="ToolWindows"/>) наравне с кадром, миром и
/// обходами: перетаскивание, видимость и порядок делает она, а этот тип знает
/// только про грейд.
///
/// Разница не косметическая. До объединения инструмент имел собственный
/// мастер-тумблер, собственные GUI.Window и собственную клавишу — и из списка
/// инструментов его не было видно, потому что списка не существовало.
/// </remarks>
public sealed class GradingWorkbench : IDisposable
{
    private readonly ColorGradeState _state = new();
    private readonly ColorGradeZones _zones = new();
    private readonly GradingZonesWindow _zonesWindow;
    private readonly GradingLayersWindow _layersWindow;
    private readonly GradingScopesWindow _scopesWindow = new();
    private readonly List<ToolWindow> _hiddenForWorkspace = [];

    private bool _registered;
    private bool _loaded;
    private bool _wasApplying;
    private bool _disposed;
    private bool _workspaceActive;
    private bool _scopesWereVisible;
    private int _sessionGeneration = -1;

    public GradingWorkbench()
    {
        _zones.Enabled = false;
        _layersWindow = new GradingLayersWindow(_state, _zones);
        _zonesWindow = new GradingZonesWindow(_state, _zones);
    }

    /// <summary>Зоны грейда по высоте. Действуют и вне рабочего места.</summary>
    public ColorGradeZones Zones => _zones;

    public ColorGradeState State => _state;

    /// <summary>Крутит ли кто-то грейд прямо сейчас.</summary>
    public bool IsApplying => ToolWindows.Enabled && _layersWindow.Visible;

    /// <summary>
    /// Стало ли применение только что выключенным. Владельцу это нужно, чтобы
    /// вернуть кадр к конфигу: иначе инструмент оставлял бы после себя правки,
    /// которых нет ни в одном файле.
    /// </summary>
    public bool StoppedApplying { get; private set; }

    public void Tick()
    {
        if (_disposed)
        {
            return;
        }

        if (_sessionGeneration != ToolWindows.SessionGeneration)
        {
            ResetForPlaySession();
        }

        if (!_registered ||
            !ToolWindows.IsRegistered(_layersWindow) ||
            !ToolWindows.IsRegistered(_scopesWindow) ||
            !ToolWindows.IsRegistered(_zonesWindow))
        {
            _registered = true;
            ToolWindows.Register(_layersWindow);
            ToolWindows.Register(_scopesWindow);
            ToolWindows.Register(_zonesWindow);
        }

        HandleWorkspaceShortcut();

        if (_layersWindow.Visible || _scopesWindow.Visible || _zonesWindow.Visible)
        {
            LoadOnce();
        }

        bool workspaceActive = ToolWindows.Enabled &&
            (_layersWindow.Visible || _scopesWindow.Visible || _zonesWindow.Visible);
        if (workspaceActive && !_workspaceActive)
        {
            EnterWorkspace();
        }
        else if (!workspaceActive && _workspaceActive)
        {
            ExitWorkspace();
        }

        bool scopesVisible = ToolWindows.Enabled && _scopesWindow.Visible;
        if (!scopesVisible && _scopesWereVisible)
        {
            ResetPreviewTools();
        }

        _scopesWereVisible = scopesVisible;
        ScopesRenderPass.Enabled = scopesVisible && _scopesWindow.ScopesRequested;

        bool applying = IsApplying;
        _zonesWindow.CaptureEnabled = applying;
        StoppedApplying = !applying && _wasApplying;
        if (StoppedApplying)
        {
            ResetPreviewTools();
        }

        _wasApplying = applying;
    }

    private void ResetForPlaySession()
    {
        _sessionGeneration = ToolWindows.SessionGeneration;
        _loaded = false;
        _wasApplying = false;
        StoppedApplying = false;
        _workspaceActive = false;
        _scopesWereVisible = false;
        _zonesWindow.CaptureEnabled = false;
        _hiddenForWorkspace.Clear();
        _state.ResetToLook();
        _zones.Clear();
        _zones.Enabled = false;
        ResetPreviewTools();
    }

    private void LoadOnce()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        if (ColorGradeFile.TryLoad(_state, _zones))
        {
            Debug.Log($"[ColorGrade] Загружено из {ColorGradeFile.Path}");
        }
    }

    private void HandleWorkspaceShortcut()
    {
        Keyboard? keyboard = Keyboard.current;
        if (keyboard == null ||
            ToolWindows.HasKeyboardCapture ||
            !keyboard.f5Key.wasPressedThisFrame)
        {
            return;
        }

        bool open = !ToolWindows.Enabled || !_layersWindow.Visible;
        ToolWindows.Enabled = true;
        _layersWindow.Visible = open;
        if (open)
        {
            ToolWindows.RequestFocus(_layersWindow);
        }

        if (!open)
        {
            ToolWindows.ReleaseInputCapture();
        }
    }

    public void Deactivate()
    {
        _layersWindow.Visible = false;
        _scopesWindow.Visible = false;
        _zonesWindow.Visible = false;
        _zonesWindow.CaptureEnabled = false;
        _scopesWereVisible = false;
        ExitWorkspace();
        StoppedApplying = _wasApplying;
        _wasApplying = false;
        ToolWindows.ReleaseInputCapture();
        ResetPreviewTools();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Deactivate();
        if (_registered)
        {
            ToolWindows.Unregister(_layersWindow);
            ToolWindows.Unregister(_scopesWindow);
            ToolWindows.Unregister(_zonesWindow);
            _layersWindow.Dispose();
            _scopesWindow.Dispose();
            _zonesWindow.Dispose();
            _registered = false;
        }
    }

    private static void ResetPreviewTools()
    {
        PostProcessRuntimeState.DebugView = PostProcessDebugView.None;
        PostProcessRuntimeState.CompareSplit = 0f;
        ScopesRenderPass.Enabled = false;
    }

    private void EnterWorkspace()
    {
        _workspaceActive = true;
        _hiddenForWorkspace.Clear();
        foreach (ToolWindow window in ToolWindows.All)
        {
            if (ReferenceEquals(window, _layersWindow) ||
                ReferenceEquals(window, _scopesWindow) ||
                ReferenceEquals(window, _zonesWindow) ||
                window is ToolbarWindow ||
                !window.Visible)
            {
                continue;
            }

            window.Visible = false;
            _hiddenForWorkspace.Add(window);
        }
    }

    private void ExitWorkspace()
    {
        if (!_workspaceActive)
        {
            return;
        }

        _workspaceActive = false;
        foreach (ToolWindow window in _hiddenForWorkspace)
        {
            window.Visible = true;
        }

        _hiddenForWorkspace.Clear();
    }
}
