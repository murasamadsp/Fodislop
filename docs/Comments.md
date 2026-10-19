MinesServer/Game/Enums/ItemsSerializer.cs
                descriptors.Values.Count(descriptor => descriptor.Placement is not null));
            int count = descriptors.Count;
            int placeable = descriptors.Values.Count(descriptor => descriptor.Placement is not null);
            _log.Information($"Items loaded: {metadata.Items.Count} предметов, {metadata.Items.Placeable} устанавливаемых",
Количество предметов можно было записать как Amount.Total. С устанавливаемыми сложнее, наверное можно и так оставить.

MinesServer/Game/Enums/ItemsSerializer.cs
            if (problems.Count > 0)
            {
                string report = string.Join(Environment.NewLine, problems);
                _log.Error($"{metadata.Items.File} не прошёл валидацию ({problems.Count} проблем):{Environment.NewLine}{metadata.Items.Problem}",
Количество проблем нужно сделать параметром, в противном случае в логах будет куча разных шаблонов которые очень трудно фильтровать.