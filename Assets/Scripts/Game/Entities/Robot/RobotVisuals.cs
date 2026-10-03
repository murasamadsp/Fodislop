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
    /// <summary>
    ///     Number of tail strands per robot. Well above the legacy client's four, so the tail
    ///     reads as a dense plume rather than a few ribbons. Strand zero trails the furthest
    ///     behind and contributes the shared anti-stretch distance, exactly as in the legacy
    ///     client.
    /// </summary>
    private const int StrandCount = 8;

    /// <summary>
    ///     Tail motion debug multipliers of <see cref="ProjectRuntimeContracts.Movement.RobotMoveSpeed" />.
    ///     The chain is a follower, so its settled length grows with root speed until the
    ///     anti-stretch clamps bite; half is already a clearly stretched tail, and four times
    ///     is close to where those clamps start to compress it.
    /// </summary>
    private const float MinimalSpeedScale = 0.5f;
    private const float MediumSpeedScale = 1f;
    private const float MaximumSpeedScale = 4f;

    private const float FacingEpsilonSquared = 1e-6f;

    private readonly Transform _transform;
    private readonly bool _isLocalPlayer;
    private IRuntimeDebugSettings? _debugSettings;
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
    private Vector3 _fakeTailOffset;
    private TailMotionDebugMode _appliedTailMotionMode = TailMotionDebugMode.Normal;

    public Sprite? SkinSprite => _skinSprite;
    public Transform? ClanTransform => _clanTransform;
    public bool TentaclesSettled => _tentaclesSettled;

    public RobotVisuals(Transform transform, bool isLocalPlayer)
    {
        _transform = transform;
        _isLocalPlayer = isLocalPlayer;
    }

    /// <summary>
    ///     Supplied after construction, not through the constructor: the local player's
    ///     <c>Awake</c> runs before the container injects its <c>[Inject]</c> members, so the
    ///     settings do not exist yet when this object is first built.
    /// </summary>
    public void SetDebugSettings(IRuntimeDebugSettings debugSettings) => _debugSettings = debugSettings;

    /// <summary>Root speed the tail simulation is forced to, or zero when following real movement.</summary>
    internal static float ForcedTailSpeed(TailMotionDebugMode mode) => mode switch
    {
        TailMotionDebugMode.Minimal => ProjectRuntimeContracts.Movement.RobotMoveSpeed * MinimalSpeedScale,
        TailMotionDebugMode.Medium => ProjectRuntimeContracts.Movement.RobotMoveSpeed * MediumSpeedScale,
        TailMotionDebugMode.Maximum => ProjectRuntimeContracts.Movement.RobotMoveSpeed * MaximumSpeedScale,
        _ => 0f,
    };

    /// <summary>
    ///     Advances the frame the tail simulates in.
    /// </summary>
    /// <remarks>
    ///     The chain is a follower, so travelling its root along the facing leaves the tail behind
    ///     the robot; travelling against the facing would push the tail out in front of it.
    ///     <para>
    ///         The facing comes from <c>Transform.up</c>, not <c>Transform.forward</c>: the robots
    ///         are rotated about Z, so forward is <c>(0, 0, 1)</c> at a zero angle and has no
    ///         component in the play plane. Reading up keeps every angle usable — using forward
    ///         here left the offset permanently zero and the debug modes did nothing.
    ///     </para>
    /// </remarks>
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

        _fakeTailOffset = Vector3.zero;
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
        if (_tentacles != null)
        {
            _tentaclesSettled = false;
            foreach (Tentacle? tentacle in _tentacles)
            {
                tentacle?.Snap(position);
            }
        }

        // A snap re-anchors the chains onto the robot, so the frame they were simulating in
        // has to go with it. Carrying the offset over would leave the chains standing on the
        // robot while their roots were dragged far away again on the next tick, which draws the
        // tail as a streak hanging off the body until the chains crawl back.
        _fakeTailOffset = Vector3.zero;
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
        if (_tentacles == null)
        {
            return;
        }

        TailMotionDebugMode mode = _debugSettings?.TailMotionDebugMode ?? TailMotionDebugMode.Normal;
        if (mode != _appliedTailMotionMode)
        {
            _appliedTailMotionMode = mode;
            // Switching between forced speeds must keep the accumulated offset: only the
            // offset's rate matters, not its value, so the chain re-settles from its current
            // length and the tail never blinks. Dropping to real movement does move the frame
            // the chain lives in, so that one resnaps the chain onto the robot.
            if (mode == TailMotionDebugMode.Normal && _fakeTailOffset != Vector3.zero)
            {
                _fakeTailOffset = Vector3.zero;
                SnapTentacles(rootPosition);
            }
        }

        float speed = ForcedTailSpeed(mode);
        _fakeTailOffset = AdvanceForcedOffset(_fakeTailOffset, _transform, speed, deltaTime);

        // The chain runs in a frame that travels behind the robot, and the tentacle subtracts
        // the offset back out when it writes geometry, so the strip stays welded to the body
        // while settling at the length it would have at that speed.
        Vector3 simulationRoot = rootPosition + _fakeTailOffset;

        // The legacy client measured the anti-stretch distance once, from the tip of strand
        // zero, and applied that single value to every strand including strand zero
        // itself. Read it before the loop so the whole set sees the same pre-tick value.
        float stretch = _tentacles[0].TipDistanceTo(simulationRoot);
        foreach (var tentacle in _tentacles)
        {
            tentacle?.Update(rootPosition, _fakeTailOffset, movementFactor, deltaTime, stretch);
        }
    }

    /// <remarks>
    ///     The settle fast path is kept but no longer fires: the legacy tail wobbles without a
    ///     movement gate, so a chain never satisfies its settle test (see the remarks on
    ///     <see cref="TailChain" />). That costs a full batch rebuild per frame in a scene
    ///     where nothing moves. Per-robot work is unchanged, because
    ///     <see cref="RobotNameplate" /> and <see cref="RobotLighting" /> each guard on an
    ///     unchanged position themselves.
    /// </remarks>
    public void UpdateMotion(Vector3 position, float movementFactor, float deltaTime, bool bodySettled)
    {
        if (_tentacles == null)
        {
            _tentaclesSettled = true;
            return;
        }

        if (bodySettled && _tentaclesSettled)
        {
            return;
        }

        if (bodySettled)
        {
            UpdateTentacles(position, 0f, deltaTime);
            _tentaclesSettled = AreTentaclesSettled();
            return;
        }

        bool tentacleStateChanged =
            !_tentaclesSettled ||
            (position - _lastTentacleRootPosition).sqrMagnitude > 1e-8f ||
            movementFactor > 0.0001f;

        if (tentacleStateChanged)
        {
            UpdateTentacles(position, movementFactor, deltaTime);
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

    public Transform EnsureClanIcon(ISceneObjectFactory sceneObjects, uint botID)
    {
        if (_clanTransform == null)
        {
            Transform? existingClan = _transform.Find("ClanIcon");
            GameObject clanGo = existingClan != null
                ? existingClan.gameObject
                : (sceneObjects != null
                    ? sceneObjects.Create("ClanIcon", RuntimeOwner.Robots)
                    : throw new System.InvalidOperationException(
                        $"[Robot] ISceneObjectFactory was not injected before creating ClanIcon for bot {botID}."));
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
