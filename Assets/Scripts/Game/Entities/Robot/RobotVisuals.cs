#nullable enable

using System.Threading;
using Cysharp.Threading.Tasks;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Core.Lifecycle;
using UnityEngine;

namespace Kern.Game;

public sealed class RobotVisuals
{
    private const int StrandCount = 4;
    private const float FacingEpsilonSquared = 1e-6f;

    private readonly Transform _transform;
    private readonly bool _isLocalPlayer;
    private WorldEntityBatchRenderer _entityBatchRenderer = null!;
    private WorldEntityBatchRenderer.SpriteHandle? _bodyBatchHandle;
    private WorldEntityBatchRenderer.SpriteHandle? _clanBatchHandle;
    private Transform? _clanTransform;
    private Sprite? _skinSprite;
    private Sprite? _clanSprite;
    private RobotAura? _aura;
    private Tentacle[]? _tentacles;
    private bool _tentaclesSettled;
    private Vector3 _lastTentacleRootPosition;
    private Vector3 _forcedTailOffset;
    private TailMotionDebugMode _tailMotionDebugMode;

    public Sprite? SkinSprite => _skinSprite;
    public Transform? ClanTransform => _clanTransform;
    public bool TentaclesSettled => _tentaclesSettled;

    public RobotVisuals(Transform transform, bool isLocalPlayer)
    {
        _transform = transform;
        _isLocalPlayer = isLocalPlayer;
    }

    internal static float ForcedTailSpeed(TailMotionDebugMode mode) => mode switch
    {
        TailMotionDebugMode.Minimal => ProjectRuntimeContracts.Movement.RobotMoveSpeed * 0.5f,
        TailMotionDebugMode.Medium => ProjectRuntimeContracts.Movement.RobotMoveSpeed,
        TailMotionDebugMode.Maximum => ProjectRuntimeContracts.Movement.RobotMoveSpeed * 4f,
        _ => 0f,
    };

    internal static Vector3 AdvanceForcedOffset(
        Vector3 offset,
        Transform facingSource,
        float speed,
        float deltaTime)
    {
        if (speed <= 0f)
        {
            return offset;
        }

        Vector3 facing = facingSource.up;
        facing.z = 0f;
        if (facing.sqrMagnitude <= FacingEpsilonSquared)
        {
            return offset;
        }

        return offset + (facing.normalized * (speed * deltaTime));
    }

    public void Initialize(WorldEntityBatchRenderer entityBatchRenderer, Transform? clanTransform)
    {
        _entityBatchRenderer = entityBatchRenderer;
        _clanTransform = clanTransform;
        EnsureBatchHandles();
    }

    public void EnsureBatchHandles()
    {
        if (_entityBatchRenderer == null)
        {
            return;
        }

        _bodyBatchHandle ??= _entityBatchRenderer.RegisterSprite(_transform, 0);
        if (_clanTransform != null)
        {
            _clanBatchHandle ??= _entityBatchRenderer.RegisterSprite(_clanTransform, 100);
        }
    }

    public void SetBodyVisible(bool visible)
    {
        EnsureBatchHandles();
        _bodyBatchHandle?.SetEnabled(visible);
    }
    public void SetColor(Color color)
    {
        _bodyBatchHandle?.SetColor(color);
    }

    public void SetClanSprite(Sprite? sprite)
    {
        EnsureBatchHandles();
        if (_clanSprite != null)
        {
            Object.Destroy(_clanSprite);
        }

        _clanSprite = sprite;
        if (_clanBatchHandle != null)
        {
            _entityBatchRenderer.SetSprite(_clanBatchHandle, sprite);
            _clanBatchHandle.SetEnabled(sprite != null);
        }
    }

    public void SetSkinSprite(Sprite? sprite)
    {
        EnsureBatchHandles();
        if (_skinSprite != null)
        {
            Object.Destroy(_skinSprite);
        }

        _skinSprite = sprite;
        if (_bodyBatchHandle != null)
        {
            _entityBatchRenderer.SetSprite(_bodyBatchHandle, sprite);
            if (!_isLocalPlayer)
            {
                _bodyBatchHandle.SetEnabled(sprite != null);
            }
        }
    }

    public void CreateTentacles(Texture2D tailTexture, Vector3 position)
    {
        ClearTentacles();
        if (_entityBatchRenderer == null)
        {
            return;
        }

        _tentacles = new Tentacle[StrandCount];
        _tentaclesSettled = false;
        for (int i = 0; i < StrandCount; i++)
        {
            _tentacles[i] = new Tentacle(
                _entityBatchRenderer,
                tailTexture,
                position,
                i);
        }
    }

    public void ClearTentacles()
    {
        if (_tentacles != null)
        {
            foreach (var tentacle in _tentacles)
            {
                tentacle?.Destroy();
            }

            _tentacles = null;
        }

        _forcedTailOffset = Vector3.zero;
        _tentaclesSettled = true;
    }

    public void SetTentaclesActive(bool active)
    {
        if (_tentacles == null)
        {
            return;
        }

        foreach (Tentacle? tentacle in _tentacles)
        {
            tentacle?.SetActive(active);
        }
    }

    public void SnapTentacles(Vector3 position)
    {
        _forcedTailOffset = Vector3.zero;
        if (_tentacles != null)
        {
            _tentaclesSettled = false;
            foreach (Tentacle? tentacle in _tentacles)
            {
                tentacle?.Snap(position);
            }
        }
    }

    public bool AreTentaclesSettled()
    {
        if (_tentacles == null)
        {
            return true;
        }

        foreach (Tentacle? tentacle in _tentacles)
        {
            if (tentacle != null && !tentacle.IsSettled)
            {
                return false;
            }
        }

        return true;
    }

    public void UpdateTentacles(Vector3 rootPosition, float movementFactor, float deltaTime)
    {
        if (_tentacles == null || _tentacles.Length == 0)
        {
            return;
        }

        float sharedStretch = _tentacles[0].TipDistanceTo(rootPosition);
        foreach (var tentacle in _tentacles)
        {
            tentacle.Update(
                rootPosition,
                movementFactor,
                deltaTime,
                sharedStretch,
                _forcedTailOffset);
        }
    }

    public void UpdateMotion(
        Vector3 position,
        float movementFactor,
        float deltaTime,
        bool bodySettled,
        TailMotionDebugMode tailMotionDebugMode)
    {
        if (_tentacles == null)
        {
            _tentaclesSettled = true;
            return;
        }

        bool returningToNormal =
            tailMotionDebugMode == TailMotionDebugMode.Normal &&
            _tailMotionDebugMode != TailMotionDebugMode.Normal;
        if (returningToNormal)
        {
            SnapTentacles(position);
        }

        _tailMotionDebugMode = tailMotionDebugMode;
        float forcedSpeed = ForcedTailSpeed(tailMotionDebugMode);
        if (forcedSpeed > 0f)
        {
            _forcedTailOffset = AdvanceForcedOffset(
                _forcedTailOffset,
                _transform,
                forcedSpeed,
                deltaTime);
        }

        if (bodySettled && _tentaclesSettled && forcedSpeed <= 0f)
        {
            return;
        }

        float effectiveMovementFactor = forcedSpeed > 0f
            ? Mathf.Max(movementFactor, 1f)
            : movementFactor;

        if (bodySettled)
        {
            UpdateTentacles(position, effectiveMovementFactor, deltaTime);
            _tentaclesSettled = AreTentaclesSettled();
            return;
        }

        bool tentacleStateChanged =
            !_tentaclesSettled ||
            (position - _lastTentacleRootPosition).sqrMagnitude > 1e-8f ||
            movementFactor > 0.0001f ||
            forcedSpeed > 0f;

        if (tentacleStateChanged)
        {
            UpdateTentacles(position, effectiveMovementFactor, deltaTime);
            _tentaclesSettled = AreTentaclesSettled();
            _lastTentacleRootPosition = position;
        }
    }

    public void SetAuraWanted(bool wanted, ISceneObjectFactory? sceneObjects)
    {
        if (!wanted && _aura == null)
        {
            return;
        }

        _aura ??= new RobotAura(_transform);
        _aura.SetWanted(wanted, _entityBatchRenderer, sceneObjects);
    }

    public void TickAura(float deltaTime) => _aura?.Tick(deltaTime);

    public Transform EnsureClanIcon(ISceneObjectFactory sceneObjects, uint botId)
    {
        if (_clanTransform == null)
        {
            Transform? existingClan = _transform.Find("ClanIcon");
            GameObject clanGo = existingClan != null
                ? existingClan.gameObject
                : (sceneObjects != null
                    ? sceneObjects.Create("ClanIcon", RuntimeOwner.Robots)
                    : throw new System.InvalidOperationException(
                        $"[Robot] ISceneObjectFactory was not injected before creating ClanIcon for bot {botId}."));
            clanGo.transform.SetParent(_transform, worldPositionStays: false);
            _clanTransform = clanGo.transform;
            _clanTransform.localScale = Vector3.one * 0.8f;
        }

        return _clanTransform;
    }

    public void Destroy()
    {
        _aura?.Destroy();
        _aura = null;
        _entityBatchRenderer?.UnregisterSprite(_bodyBatchHandle);
        _entityBatchRenderer?.UnregisterSprite(_clanBatchHandle);
        _bodyBatchHandle = null;
        _clanBatchHandle = null;

        if (_skinSprite != null)
        {
            Object.Destroy(_skinSprite);
            _skinSprite = null;
        }

        if (_clanSprite != null)
        {
            Object.Destroy(_clanSprite);
            _clanSprite = null;
        }

        ClearTentacles();
    }
}
