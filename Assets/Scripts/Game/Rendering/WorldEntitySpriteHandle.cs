#nullable enable

using UnityEngine;

namespace Fodinae.Game;

public class WorldEntitySpriteHandle
{
    private Vector3 _lastPosition;
    private Quaternion _lastRotation;
    private Vector3 _lastScale;
    private Sprite? _lastSprite;
    private Color _lastColor;
    private bool _lastEnabled;
    private bool _hasSnapshot;

    private Vector3 _framePosition;
    private Quaternion _frameRotation = Quaternion.identity;
    private Vector3 _frameScale = Vector3.one;
    private Matrix4x4 _frameLocalToWorld = Matrix4x4.identity;
    private bool _frameAlive;

    internal WorldEntitySpriteHandle(Transform transform, int sortingOrder, bool isStatic = false)
    {
        Transform = transform;
        SortingOrder = sortingOrder;
        IsStatic = isStatic;
    }

    internal Transform Transform { get; }

    internal int SortingOrder { get; }

    internal bool IsStatic { get; }

    internal Sprite? Sprite { get; private set; }

    internal Color Color { get; private set; } = Color.white;

    internal bool Enabled { get; private set; }

    public void SetSprite(Sprite? sprite)
    {
        Sprite = sprite;
        if (sprite == null)
        {
            Enabled = false;
        }

        if (IsStatic)
        {
            _hasSnapshot = false;
        }
    }

    public void SetColor(Color color)
    {
        Color = color;
    }

    public void SetEnabled(bool enabled)
    {
        Enabled = enabled && Sprite != null;
        if (IsStatic)
        {
            _hasSnapshot = false;
        }
    }

    public void MarkTransformDirty()
    {
        _hasSnapshot = false;
    }

    /// <summary>Жив ли трансформ по последнему опросу этого кадра.</summary>
    internal bool FrameAlive => _frameAlive;

    /// <summary>Мировая позиция по последнему опросу этого кадра.</summary>
    internal Vector3 FramePosition => _framePosition;

    /// <summary>Матрица трансформа по последнему опросу этого кадра.</summary>
    internal Matrix4x4 FrameLocalToWorld => _frameLocalToWorld;

    /// <summary>
    /// Опрашивает трансформ ровно один раз за кадр.
    /// </summary>
    /// <remarks>
    /// ЗАЧЕМ. Обращение к <see cref="Transform"/> из управляемого кода — вызов
    /// в движок, а не чтение поля; <c>lossyScale</c> вдобавок идёт вверх по
    /// цепочке родителей. Раньше пакет спрашивал об одном и том же спрайте по
    /// нескольку раз за кадр: проверка изменения, подсчёт видимых, запись
    /// геометрии, снимок состояния и то же самое в накладке — и каждый из этих
    /// проходов трогал движок заново, хотя за кадр трансформ не менялся.
    ///
    /// Проверка на <c>null</c> остаётся вызовом в движок и здесь: у Unity это
    /// перегруженный оператор, отвечающий на уничтоженный объект. Отказаться от
    /// неё нельзя — снятие с учёта при уничтожении сущности не гарантировано, а
    /// цена уже сведена к одному вызову на спрайт вместо полудюжины.
    /// </remarks>
    internal void RefreshFrameState()
    {
        if (Transform == null)
        {
            _frameAlive = false;
            return;
        }

        _frameAlive = true;

        // Неподвижному спрайту хватает снятого однажды снимка: его трансформ
        // не спрашивают вовсе, пока владелец сам не скажет, что тот сдвинулся.
        if (IsStatic && _hasSnapshot)
        {
            _framePosition = _lastPosition;
            _frameRotation = _lastRotation;
            _frameScale = _lastScale;
            return;
        }

        _framePosition = Transform.position;
        _frameRotation = Transform.rotation;
        _frameScale = Transform.lossyScale;
        _frameLocalToWorld = Transform.localToWorldMatrix;
    }

    internal Vector3 GetWorldPosition()
    {
        return _framePosition;
    }

    internal bool HasChanged()
    {
        if (!_frameAlive)
        {
            return _hasSnapshot;
        }

        if (!_hasSnapshot)
        {
            return true;
        }

        if (_lastEnabled != Enabled)
        {
            return true;
        }

        if (!Enabled)
        {
            return false;
        }

        if (_lastSprite != Sprite || _lastColor != Color)
        {
            return true;
        }

        if (IsStatic)
        {
            return false;
        }

        return _lastPosition != _framePosition ||
            _lastRotation != _frameRotation ||
            _lastScale != _frameScale;
    }

    internal void CaptureState()
    {
        if (!_frameAlive)
        {
            _lastPosition = Vector3.zero;
            _lastRotation = Quaternion.identity;
            _lastScale = Vector3.one;
        }
        else if (Enabled && (!IsStatic || !_hasSnapshot))
        {
            _lastPosition = _framePosition;
            _lastRotation = _frameRotation;
            _lastScale = _frameScale;
        }

        _lastSprite = Sprite;
        _lastColor = Color;
        _lastEnabled = Enabled;
        _hasSnapshot = true;
    }
}
