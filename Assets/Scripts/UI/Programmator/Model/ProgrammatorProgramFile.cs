#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using Kern.Core.Localization;
using MinesServer.Data;
using UnityEngine;

namespace Kern.UI.Programmator;

// Одна программа на диске: имя и три параллельных списка по клеткам страниц.
[Serializable]
internal sealed class ProgrammatorProgramItem
{
    public string Name = string.Empty;
    public List<int> Codes = new();
    public List<string?> Labels = new();
    public List<string?> Values = new();
}

// Файл программ программатора: где он лежит, как пишется без потери прошлого
// содержимого и что считается годной программой.
//
// Отдельный тип, а не часть хранилища: у хранилища ответственность — активная
// программа, страницы и состояние выполнения, и запись на диск к ней не
// относится. Пока они жили вместе, файл разрастался, а проверка годности
// программы стояла рядом с обработчиками сетевых пакетов.
internal sealed class ProgrammatorProgramFile
{
    private readonly ILocalizationService _loc;

    public ProgrammatorProgramFile(ILocalizationService loc)
    {
        _loc = loc ?? throw new ArgumentNullException(nameof(loc));
    }

    // Прошлый клиент писал только активную сетку. Такой файл читается как
    // одна программа, иначе обновление клиента стирало бы работу.
    [Serializable]
    private sealed class ProgrammatorSave
    {
        public List<ProgrammatorProgramItem> Programs = new();

        public int[] Codes = Array.Empty<int>();
        public string?[] Labels = null!;
        public string?[] Values = null!;
    }

    public string SavePath => Path.Combine(Application.persistentDataPath, "programmator.json");

    private string BackupPath => SavePath + ".backup";

    private string TemporaryPath => SavePath + ".tmp";

    // Пишется через временный файл с заменой: прерванная запись не должна
    // оставить человека без программ, над которыми он сидел.
    public void Save(List<ProgrammatorProgramItem> programs)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SavePath)!);
        ProgrammatorSave save = new() { Programs = programs };
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

    public List<ProgrammatorProgramItem> Load()
    {
        List<ProgrammatorProgramItem> programs = new();
        if (!File.Exists(SavePath))
        {
            return programs;
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
                foreach (ProgrammatorProgramItem item in save.Programs)
                {
                    if (IsValidProgram(item))
                    {
                        programs.Add(item);
                    }
                }
            }
            else if (save.Codes.Length > 0)
            {
                ProgrammatorProgramItem legacy = new()
                {
                    Name = _loc.Get("programmator.program", 1),
                    Codes = new List<int>(save.Codes),
                    Labels = new List<string?>(save.Labels),
                    Values = new List<string?>(save.Values),
                };
                if (IsValidProgram(legacy))
                {
                    programs.Add(legacy);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Programmator] Failed to load '{SavePath}': {ex.Message}");
        }

        return programs;
    }

    private static bool IsValidProgram(ProgrammatorProgramItem item)
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
}
