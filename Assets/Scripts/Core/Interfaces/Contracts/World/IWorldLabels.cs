#nullable enable

using System;
using UnityEngine;

namespace Kern.Core.Interfaces;

/// <summary>
/// Тип мировой метки. Определяет и оформление, и то, какой угол бокса
/// садится в мировую точку: style.translate двигает бокс целиком, и без
/// указания угла метка уезжает вниз от точки, а не над ней.
/// </summary>
public enum WorldLabelKind
{
    /// <summary>Никнейм над роботом. Верхний левый угол в правом верхнем углу клетки.</summary>
    Nickname,

    /// <summary>
    /// Облако локального чата. Нижняя грань рамки в верхней грани клетки
    /// робота, а по горизонтали облако сдвинуто так, чтобы хвост спрайта встал
    /// в позицию робота: тело облака уходит вправо от хвоста.
    ///
    /// Высота постоянна (высота спрайта), текст обрезается по ширине.
    /// </summary>
    ChatBubble,
}

public interface IWorldLabels
{
    IWorldLabel Create(WorldLabelKind kind);
}

public interface IWorldLabel : IDisposable
{
    void SetText(string text);
    void SetPosition(Vector3 position);
    void SetVisible(bool visible);
    void SetOpacity(float opacity);
}
