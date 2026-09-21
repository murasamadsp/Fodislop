#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using Kern.Core.Localization;
using Kern.Networking.Processors;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Programmator;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kern.UI.Programmator;

// Program storage, page navigation, and run/stop state for the programmator.
// Reads and writes ProgrammatorData directly (page/cell contents are global,
// shared with the rest of the programmator), and reaches into the shared
// selection model and the UI view for the handful of things it needs to
// repaint or clear when switching pages/programs.
internal sealed class ProgrammatorProgramStore
{
    [Serializable]
    private sealed class ProgramItem
    {
        public string Name = string.Empty;
        public List<int> Codes = new();
        public List<string?> Labels = new();
        public List<string?> Values = new();
    }

    private readonly ProgrammatorGridUIFactory _view;
    private readonly ProgrammatorSelectionModel _selection;
    private readonly ProgrammatorRadialController _radial;
    private readonly ILocalizationService _loc;
    private readonly ProgrammatorData _data;
    private readonly ProgrammatorProcessor _protocol;

    private readonly List<ProgramItem> _programItems = new();
    private int _activeIndex = -1;
    private bool _isRunning;

    public ProgrammatorProgramStore(
        ProgrammatorGridUIFactory view,
        ProgrammatorSelectionModel selection,
        ProgrammatorRadialController radial,
        ILocalizationService loc,
        ProgrammatorData data,
        ProgrammatorProcessor protocol)
    {
        _view = view ?? throw new ArgumentNullException(nameof(view));
        _selection = selection ?? throw new ArgumentNullException(nameof(selection));
        _radial = radial ?? throw new ArgumentNullException(nameof(radial));
        _loc = loc ?? throw new ArgumentNullException(nameof(loc));
        _data = data ?? throw new ArgumentNullException(nameof(data));
        _protocol = protocol ?? throw new ArgumentNullException(nameof(protocol));
        _protocol.ProgramUpdated += OnProgramUpdated;
        _protocol.StateChanged += OnStateChanged;
        _protocol.BreakpointHit += OnBreakpointHit;
        _protocol.MemoryReceived += OnMemoryReceived;
        LoadPrograms();
    }

    public bool IsRunning => _isRunning;

    public int ProgramCount => _programItems.Count;

    // Invoked (via ProgrammatorRadialController.OnLastCellPlaced) when an
    // operator is placed in the very last cell of the last page.
    public void AdvancePageIfAtEnd()
    {
        _data.AddPage();
        _view.UpdatePageLabel();
    }

        [Serializable]
        private sealed class ProgrammatorSave
        {
            public List<ProgramItem> Programs = new();

            // Legacy payload: the previous client wrote only the active grid.
            public int[] Codes = Array.Empty<int>();
            public string?[] Labels = null!;
            public string?[] Values = null!;
        }

        private string SavePath => Path.Combine(Application.persistentDataPath, "programmator.json");

        private string BackupPath => SavePath + ".backup";

        private string TemporaryPath => SavePath + ".tmp";

        public void SaveProgram()
        {
            StoreActiveProgram();
            SavePrograms();
            if (TryBuildNetworkProgram(out List<(ProgAction Operator, string Label, string Value)> program))
            {
                _protocol.Save(_activeIndex, false, program, Array.Empty<int>());
            }

            Debug.Log("[Programmator] Programs saved");
        }

        private void StoreActiveProgram()
        {
            if (_activeIndex < 0 || _activeIndex >= _programItems.Count)
            {
                return;
            }

            ProgramItem item = _programItems[_activeIndex];
            item.Codes = new List<int>(_data.Codes);
            item.Labels = new List<string?>(_data.Labels);
            item.Values = new List<string?>(_data.Values);
        }

        private void SavePrograms()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SavePath)!);
            ProgrammatorSave save = new() { Programs = _programItems };
            string json = JsonUtility.ToJson(save, prettyPrint: true);
            byte[] payload = System.Text.Encoding.UTF8.GetBytes(json);

            try
            {
                using (var stream = new FileStream(
                           TemporaryPath,
                           FileMode.Create,
                           FileAccess.Write,
                           FileShare.None))
                {
                    stream.Write(payload, 0, payload.Length);
                    stream.Flush(flushToDisk: true);
                }

                if (File.Exists(SavePath))
                {
                    File.Replace(TemporaryPath, SavePath, BackupPath);
                }
                else
                {
                    File.Move(TemporaryPath, SavePath);
                }
            }
            finally
            {
                if (File.Exists(TemporaryPath))
                {
                    File.Delete(TemporaryPath);
                }
            }
        }

        private void LoadPrograms()
        {
            if (!File.Exists(SavePath))
            {
                return;
            }

            try
            {
                ProgrammatorSave? save = JsonUtility.FromJson<ProgrammatorSave>(File.ReadAllText(SavePath));
                if (save == null)
                {
                    throw new InvalidDataException("programmator.json is empty or invalid.");
                }

                if (save.Programs.Count > 0)
                {
                    foreach (ProgramItem item in save.Programs)
                    {
                        if (IsValidProgram(item))
                        {
                            _programItems.Add(item);
                        }
                    }
                }
                else if (save.Codes.Length > 0)
                {
                    ProgramItem legacy = new()
                    {
                        Name = _loc.Get("programmator.program", 1),
                        Codes = new List<int>(save.Codes),
                        Labels = new List<string?>(save.Labels),
                        Values = new List<string?>(save.Values),
                    };
                    if (IsValidProgram(legacy))
                    {
                        _programItems.Add(legacy);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Programmator] Failed to load '{SavePath}': {ex.Message}");
            }
        }

        private static bool IsValidProgram(ProgramItem item)
        {
            int length = item.Codes.Count;
            bool valid = !string.IsNullOrWhiteSpace(item.Name) &&
                length > 0 &&
                length <= ProgrammatorData.CELLS_PER_PAGE * 100 &&
                length % ProgrammatorData.CELLS_PER_PAGE == 0 &&
                item.Labels.Count == length &&
                item.Values.Count == length;
            if (!valid)
            {
                Debug.LogError($"[Programmator] Ignoring invalid program '{item.Name}'.");
            }

            return valid;
        }

        public void PrevPage()
        {
            if (_data.CurrentPage > 0)
            {
                _selection.ClearSelection();
                _radial.HideMenus();
                _data.CurrentPage--;
                RefreshAllCells();
            }
        }

        public void NextPage()
        {
            if (_data.CurrentPage < _data.PageCount - 1)
            {
                _selection.ClearSelection();
                _radial.HideMenus();
                _data.CurrentPage++;
                RefreshAllCells();
            }
        }

        public void AddPageClick()
        {
            if (_data.PageCount >= 100)
            {
                return;
            }

            _data.AddPage();
            _view.UpdatePageLabel();
        }

        public void RemovePageClick()
        {
            if (_data.RemoveLastPage())
            {
                RefreshAllCells();
            }
        }

        public void ShowProgramList()
        {
            _selection.ClearSelection();
            _radial.HideAll();
            if (_isRunning)
            {
                StopProgram();
            }

            _view.ProgramTitle.text = _loc.Get("programmator.title");
            RefreshProgramList();
            _view.Panel.style.display = DisplayStyle.None;
            _view.ProgramListPanel.style.display = DisplayStyle.Flex;
            _activeIndex = -1;
        }

        public void OpenProgram(int index)
        {
            if (index < 0 || index >= _programItems.Count)
            {
                return;
            }

            var item = _programItems[index];
            _data.Codes = new List<int>(item.Codes);
            _data.Labels = new List<string?>(item.Labels);
            _data.Values = new List<string?>(item.Values);
            _activeIndex = index;
            _data.CurrentPage = 0;
            _view.ProgramTitle.text = item.Name;
            _view.ProgramListPanel.style.display = DisplayStyle.None;
            _view.Panel.style.display = DisplayStyle.Flex;
            RefreshAllCells();
        }

        public void CloseProgram()
        {
            if (_isRunning)
            {
                StopProgram();
            }

            StoreActiveProgram();
            SavePrograms();

            ShowProgramList();
        }

        public void CreateNewProgram(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                name = _loc.Get("programmator.program", _programItems.Count + 1);
            }

            var item = new ProgramItem
            {
                Name = name,
                Codes = new List<int>(new int[ProgrammatorData.CELLS_PER_PAGE]),
                Labels = new List<string?>(new string?[ProgrammatorData.CELLS_PER_PAGE]),
                Values = new List<string?>(new string?[ProgrammatorData.CELLS_PER_PAGE]),
            };
            _programItems.Add(item);
            SavePrograms();
            HideCreateInput();
            OpenProgram(_programItems.Count - 1);
        }

        public void ShowCreateInput()
        {
            _view.CreateInput.value = _loc.Get("programmator.program", _programItems.Count + 1);
            _view.CreateDialog.style.display = DisplayStyle.Flex;
            _view.CreateInput.Focus();
        }

        public void HideCreateInput()
        {
            _view.CreateDialog.style.display = DisplayStyle.None;
        }

        public void DeleteProgram(int index)
        {
            if (index < 0 || index >= _programItems.Count)
            {
                return;
            }

            _programItems.RemoveAt(index);
            _protocol.DeleteProgram();
            SavePrograms();
            RefreshProgramList();
        }

        public void RefreshProgramList()
        {
            _view.ListScroll.Clear();
            for (int i = 0; i < _programItems.Count; i++)
            {
                int idx = i;
                var item = _programItems[i];
                var row = new VisualElement();
                row.AddToClassList("prog-list-row");
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.style.paddingTop = 6;
                row.style.paddingBottom = 6;
                row.style.paddingLeft = 8;
                row.style.paddingRight = 8;
                row.style.borderBottomWidth = 1;
                row.style.borderBottomColor = new Color(0.2f, 0.2f, 0.2f, 1f);
                var nameLabel = new Label(item.Name);
                nameLabel.AddToClassList("prog-list-name");
                nameLabel.style.flexGrow = 1;
                nameLabel.style.color = new Color(0.8f, 0.8f, 0.8f, 1f);
                nameLabel.style.fontSize = 14;
                nameLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
                row.Add(nameLabel);

                var delBtn = new Button(() => DeleteProgram(idx));
                delBtn.text = "\u00d7";
                delBtn.AddToClassList("prog-del-btn");
                delBtn.style.width = 22;
                delBtn.style.height = 22;
                delBtn.style.backgroundColor = new Color(0.3f, 0f, 0f, 0.3f);
                delBtn.style.color = new Color(0.9f, 0.3f, 0.3f, 1f);
                delBtn.style.fontSize = 14;
                delBtn.style.unityTextAlign = TextAnchor.MiddleCenter;
                delBtn.style.borderTopWidth = 0;
                delBtn.style.borderBottomWidth = 0;
                delBtn.style.borderLeftWidth = 0;
                delBtn.style.borderRightWidth = 0;
                delBtn.style.paddingTop = 0;
                delBtn.style.paddingBottom = 0;
                delBtn.style.paddingLeft = 0;
                delBtn.style.paddingRight = 0;
                delBtn.style.marginLeft = 8;
                row.Add(delBtn);

                var renameBtn = new Button(_protocol.RenameProgram);
                renameBtn.text = "✎";
                renameBtn.AddToClassList("prog-rename-btn");
                renameBtn.style.width = 22;
                renameBtn.style.height = 22;
                renameBtn.style.marginLeft = 4;
                renameBtn.style.paddingTop = 0;
                renameBtn.style.paddingBottom = 0;
                renameBtn.style.paddingLeft = 0;
                renameBtn.style.paddingRight = 0;
                row.Add(renameBtn);

                row.RegisterCallback<ClickEvent>(_ => OpenProgram(idx));
                row.RegisterCallback<MouseEnterEvent>(_ =>
                    row.style.backgroundColor = new Color(0.15f, 0.15f, 0.15f, 1f));
                row.RegisterCallback<MouseLeaveEvent>(_ =>
                    row.style.backgroundColor = Color.clear);

                _view.ListScroll.Add(row);
            }
        }

        public void RunProgram()
        {
            if (!TryBuildNetworkProgram(out List<(ProgAction Operator, string Label, string Value)> program))
            {
                return;
            }

            SavePrograms();
            _protocol.Save(_activeIndex, false, program, Array.Empty<int>());
            _protocol.StartProgram();
            _isRunning = true;
            _view.RunBtn.SetEnabled(false);
            _view.StopBtn.SetEnabled(true);
            _view.Panel.AddToClassList("prog-panel--running");
            Debug.Log("[Programmator] Program running");
        }

        public void StopProgram()
        {
            _protocol.StopProgram();
            _isRunning = false;
            _view.RunBtn.SetEnabled(true);
            _view.StopBtn.SetEnabled(false);
            _view.Panel.RemoveFromClassList("prog-panel--running");
            Debug.Log("[Programmator] Program stopped");
        }

        public void PauseProgram() => _protocol.PauseProgram();

        public void StepIn() => _protocol.StepIn();

        public void StepOut() => _protocol.StepOut();

        public void StepOver() => _protocol.StepOver();

        public void QueryMemory(IReadOnlyList<string> variables, ushort arrayStart, ushort arrayStop) =>
            _protocol.QueryMemory(variables, arrayStart, arrayStop);

        private void OnProgramUpdated(UpdateProgramPacket packet)
        {
            if (packet.ProgramId != _activeIndex || packet.Instructions.Count == 0)
            {
                return;
            }

            _data.Codes = new List<int>(packet.Instructions.Count);
            _data.Labels = new List<string?>(packet.Instructions.Count);
            _data.Values = new List<string?>(packet.Instructions.Count);
            foreach ((ProgAction op, string label, string value) in packet.Instructions)
            {
                _data.Codes.Add((int)op);
                _data.Labels.Add(label);
                _data.Values.Add(value);
            }

            ProgramItem item = _programItems[_activeIndex];
            item.Name = packet.DisplayName;
            item.Codes = new List<int>(_data.Codes);
            item.Labels = new List<string?>(_data.Labels);
            item.Values = new List<string?>(_data.Values);
            SavePrograms();
            RefreshAllCells();
            Debug.Log($"[Programmator] Server updated program '{packet.DisplayName}'.");
        }

        private void OnStateChanged(ProgramStatePacket packet)
        {
            _isRunning = packet.State == ProgramState.Running;
            _view.RunBtn.SetEnabled(packet.State != ProgramState.Running);
            _view.StopBtn.SetEnabled(packet.State == ProgramState.Running || packet.State == ProgramState.Paused);
            Debug.Log($"[Programmator] Server state: {packet.State}");
        }

        private static void OnBreakpointHit(BreakpointHitPacket packet)
        {
            Debug.Log($"[Programmator] Breakpoint hit; call stack depth={packet.CallStack.Length}.");
        }

        private static void OnMemoryReceived(ProgramMemoryPacket packet)
        {
            Debug.Log($"[Programmator] Memory received: variables={packet.RequestedVariables.Length}, array={packet.RequestedArraySlice.Length}.");
        }

        public void Dispose()
        {
            _protocol.ProgramUpdated -= OnProgramUpdated;
            _protocol.StateChanged -= OnStateChanged;
            _protocol.BreakpointHit -= OnBreakpointHit;
            _protocol.MemoryReceived -= OnMemoryReceived;
        }

        private bool TryBuildNetworkProgram(
            out List<(ProgAction Operator, string Label, string Value)> program)
        {
            program = new List<(ProgAction Operator, string Label, string Value)>(_data.Codes.Count);
            for (int index = 0; index < _data.Codes.Count; index++)
            {
                int rawCode = _data.Codes[index];
                if (!Enum.IsDefined(typeof(ProgAction), rawCode))
                {
                    string message = _loc.Get("programmator.error.invalid_instruction", rawCode, index);
                    _view.ShowProtocolError(message);
                    Debug.LogError($"[Programmator] {message}");
                    return false;
                }

                program.Add((
                    (ProgAction)rawCode,
                    _data.Labels[index] ?? string.Empty,
                    _data.Values[index] ?? string.Empty));
            }

            _view.ClearProtocolError();
            return true;
        }

        public void RefreshAllCells()
        {
            _selection.SelectedCells.Clear();
            _selection.HasSelection = false;
            _view.UpdatePageLabel();
            for (int i = 0; i < ProgrammatorData.ROWS; i++)
            {
                for (int j = 0; j < ProgrammatorData.COLS; j++)
                {
                    _view.UpdateCell(i, j);
                }
            }
        }
}
