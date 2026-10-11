#nullable enable

using System;
using Kern.Core.Localization;
using Kern.Networking.Processors;
using MinesServer.Data;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Kern.UI.Programmator;

// Thin lifecycle owner for the programmator popup. UI construction, cell
// rendering, selection, clipboard, radial menu, and program storage each
// live in their own focused type; this class only wires them together once
// and dispatches input.
public sealed class ProgrammatorGrid : IDisposable
    {
        private readonly UIDocument _doc;
        private readonly ILocalizationService _loc;
        private readonly ProgrammatorData _data;
        private readonly UIInputManager _uiInput;
        private readonly IProgrammatorTextureCatalog _textures;
        private readonly ProgrammatorProcessor _protocol;

        private ProgrammatorGridUIFactory? _view;
        private ProgrammatorSelectionModel? _selection;
        private ProgrammatorRadialController? _radial;
        private ProgrammatorProgramStore? _programs;
        private ProgrammatorClipboardController? _clipboard;
        private ProgrammatorEditCommands? _commands;

        private bool _isOpen;

        private bool _uiBuilt;

        /// <summary>Хранилище программ (имя активной, состояние выполнения) для HUD.</summary>
        internal ProgrammatorProgramStore? Programs => _programs;

        public ProgrammatorGrid(
            UIDocument doc,
            ILocalizationService loc,
            ProgrammatorData data,
            UIInputManager uiInput,
            IProgrammatorTextureCatalog textures,
            ProgrammatorProcessor protocol)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _loc = loc ?? throw new ArgumentNullException(nameof(loc));
            _data = data ?? throw new ArgumentNullException(nameof(data));
            _uiInput = uiInput ?? throw new ArgumentNullException(nameof(uiInput));
            _textures = textures ?? throw new ArgumentNullException(nameof(textures));
            _protocol = protocol ?? throw new ArgumentNullException(nameof(protocol));
            _protocol.OpenRequested += Show;
        }

        public void Initialize()
        {
            TryBuildUI();
        }

        public void RefreshLocalization()
        {
            _view?.ApplyLocalizedText();
        }

        private void TryBuildUI()
        {
            if (_uiBuilt)
            {
                return;
            }

            if (_doc == null)
            {
                return;
            }

            if (_doc == null || _doc.rootVisualElement == null)
            {
                // Тихий возврат ожидаем: TryBuildUI вызывается из Start, когда
                // панель уже создана; гард защищает от вызова до активации
                // документа.
                return;
            }

            var view = new ProgrammatorGridUIFactory(_doc, _loc, _data, _textures);
            var selection = new ProgrammatorSelectionModel(view.SetSelectionBorder, _data);
            var radial = new ProgrammatorRadialController(_doc, view.UpdateCell, _loc, _data, _textures);
            var programs = new ProgrammatorProgramStore(
                view,
                selection,
                radial,
                _loc,
                _data,
                _protocol);
            radial.OnLastCellPlaced = programs.AdvancePageIfAtEnd;
            var clipboard = new ProgrammatorClipboardController(selection, view.UpdateCell, _data);

            view.Build(selection, radial, programs, clipboard, Hide);
            var commands = new ProgrammatorEditCommands(_data, selection, clipboard, programs, view);
            view.BindTouchEditing(commands, selection, radial);

            _view = view;
            _selection = selection;
            _radial = radial;
            _programs = programs;
            _clipboard = clipboard;
            _commands = commands;
            programs.RunStateChanged += OnProgramRunStateChanged;

            if (_view == null)
            {
                return;
            }

            _view.Popup.style.display = DisplayStyle.None;
            _uiBuilt = true;
        }

        public void Tick()
        {
            if (!_uiBuilt)
            {
                TryBuildUI();
                if (!_uiBuilt)
                {
                    return;
                }
            }

            if (Keyboard.current == null)
            {
                return;
            }

            if (!_isOpen)
            {
                if ((Keyboard.current.pKey.wasPressedThisFrame || Keyboard.current.rKey.wasPressedThisFrame) &&
                    !_uiInput.IsChatFocused &&
                    !_uiInput.IsPauseMenuOpen)
                {
                    Show();
                }

                return;
            }

            if ((Keyboard.current.pKey.wasPressedThisFrame || Keyboard.current.rKey.wasPressedThisFrame) &&
                !_radial!.IsShown)
            {
                Hide();
                return;
            }

            // ESC closes the programmator or goes back to list
            if (Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                if (_selection!.HasSelection && !_radial!.IsShown)
                {
                    _selection.ClearSelection();
                    return;
                }

                if (_selection.SelectedCells.Count > 0 && !_radial!.IsShown)
                {
                    _selection.ClearSelection();
                    return;
                }

                if (_view!.Panel.style.display == DisplayStyle.Flex)
                {
                    _programs!.CloseProgram();
                    return;
                }

                Hide();
                return;
            }

            if (_radial!.IsShown)
            {
                // DEL clears the cell when radial menu is open
                if (Keyboard.current.deleteKey.wasPressedThisFrame)
                {
                    _radial.HandleDeleteKeyWhileShown();
                    return;
                }

                return;
            }

            // Ctrl shortcuts
            if (Keyboard.current.ctrlKey.isPressed)
            {
                if (Keyboard.current.zKey.wasPressedThisFrame)
                {
                    _commands!.Undo();
                }
                else if (Keyboard.current.yKey.wasPressedThisFrame)
                {
                    _commands!.Redo();
                }
                else if (Keyboard.current.cKey.wasPressedThisFrame)
                {
                    _commands!.Copy();
                }
                else if (Keyboard.current.xKey.wasPressedThisFrame)
                {
                    _commands!.Cut();
                }
                else if (Keyboard.current.vKey.wasPressedThisFrame)
                {
                    _commands!.Paste();
                }

                return;
            }

            // DEL clears selected cells
            if (Keyboard.current.deleteKey.wasPressedThisFrame && _selection!.HasAnySelection())
            {
                _commands!.DeleteSelection();
                return;
            }

            // Arrow keys for page navigation
            if (Keyboard.current.leftArrowKey.wasPressedThisFrame)
            {
                _programs!.PrevPage();
            }
            else if (Keyboard.current.rightArrowKey.wasPressedThisFrame)
            {
                _programs!.NextPage();
            }
        }

        public void Show()
        {
            if (!_uiBuilt)
            {
                TryBuildUI();
            }

            if (!_uiBuilt || _view == null)
            {
                // UI ещё не готов (DI-инъекция не завершилась) — кнопка просто не
                // открывает программатор в этот раз; TryBuildUI ретраится из Update.
                return;
            }

            _isOpen = true;
            _uiInput.IsProgrammatorOpen = true;
            _view.Popup.style.display = DisplayStyle.Flex;

            // Окно — поверх HUD: дерево HUD добавлено в документ позже, и без
            // этого кнопки HUD перекрывали бы список программ и сетку.
            _view.Popup.parent?.BringToFront();
            _programs!.ShowProgramList();
        }

        public void Hide()
        {
            if (_programs!.IsRunning)
            {
                _programs.StopProgram();
            }

            _selection!.ClearSelection();
            _radial!.HideAll();
            _isOpen = false;
            _uiInput.IsProgrammatorOpen = false;
            _programs.HideCreateInput();
            _view!.ProgramListPanel.style.display = DisplayStyle.None;
            _view.Panel.style.display = DisplayStyle.None;
            _view.Popup.style.display = DisplayStyle.None;
        }

        // Запуск программы из окна: окно убирается с экрана, но программа
        // продолжает работать — ею управляет кнопка ▶/■ в HUD. В отличие от
        // Hide(), этот путь НЕ останавливает программу.
        private void OnProgramRunStateChanged(bool running)
        {
            if (running)
            {
                HideKeepRunning();
            }
        }

        private void HideKeepRunning()
        {
            if (!_uiBuilt || _view == null)
            {
                return;
            }

            _selection!.ClearSelection();
            _radial!.HideAll();
            _isOpen = false;
            _uiInput.IsProgrammatorOpen = false;
            _programs!.HideCreateInput();
            _view.ProgramListPanel.style.display = DisplayStyle.None;
            _view.Panel.style.display = DisplayStyle.None;
            _view.Popup.style.display = DisplayStyle.None;
        }

        public void Dispose()
        {
            if (_uiBuilt && _isOpen)
            {
                Hide();
            }

            _uiInput.IsProgrammatorOpen = false;
            _protocol.OpenRequested -= Show;

            if (_programs != null)
            {
                _programs.RunStateChanged -= OnProgramRunStateChanged;
            }

            // Фабрика зарегистрирована в реестре локализации — снимаем её,
            // чтобы смена языка не долетала до мёртвого попапа.
            _programs?.Dispose();
            _view?.Dispose();
            _view = null;
        }
}
