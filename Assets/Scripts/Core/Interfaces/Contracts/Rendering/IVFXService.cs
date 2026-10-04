#nullable enable

using UnityEngine;

namespace Kern.Core.Interfaces;
public interface IVFXSlot
{
    GameObject? GameObject { get; }

    void SetSprite(Sprite? sprite);

    void SetColor(Color color);

    void SetEnabled(bool enabled);
}

public interface IVFXService
{
    IVFXSlot? Acquire();
    void Release(IVFXSlot slot);
}
