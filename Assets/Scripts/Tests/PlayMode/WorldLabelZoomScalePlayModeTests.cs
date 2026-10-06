#nullable enable

using System;
using System.Collections;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Networking;
using Kern.World;
using MinesServer.Networking.Server.Packets.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Kern.Tests.PlayMode;

/// <summary>
/// Размер мировых меток при зуме камеры.
///
/// Ник и облако локального чата живут в панели UI Toolkit, и USS задаёт их
/// кегль в пикселях панели. Панель от зума не зависит — камера меняет только
/// orthographicSize, — поэтому раньше текст оставался прежних 12 px, пока
/// робот на экране сжимался: при отдалении ник и облако «разрастались».
/// Теперь кегль, а с ним поля, рамка, скругление и предел ширины облака,
/// умножаются на ReferenceOrthographicSize / orthographicSize, и метка держит
/// постоянный размер в мире.
///
/// Проверка идёт по настоящему пути: игрок телепортирован в 1315:15, рядом
/// с ним в соседней клетке стоит второй робот с ником, зум крутится колесом
/// мыши через тот же UI/ScrollWheel, который читает CameraFollow, а облако
/// приходит обычным пакетом локального чата в ChatEventGateway. Никаких
/// подставных Label в панели и никаких прямых записей в Camera: иначе тест
/// проверял бы сам себя.
///
/// Ожидаемые числа посчитаны здесь, а не вызваны из production-кода: база
/// 12px — это --size-sm из ThemeTokens.uss, 17.5 — ReferenceOrthographicSize,
/// а якорь облака проверяется по боксу элемента в панели и по проекции камеры,
/// без знания о формуле, которой WorldLabels пишет translate.
/// </summary>
[TestFixture]
public sealed class WorldLabelZoomScalePlayModeTests
{
    private const ushort AnchorX = 1315;
    private const ushort AnchorY = 15;
    private const ushort ProbeX = AnchorX + 1;
    private const ushort ProbeY = AnchorY;
    private const uint ProbeBotID = 9_900_001;
    private const string ProbeNickname = "ZoomProbe";
    private const string ProbeMessage = "привет";

    private const string NicknameClass = "world-label-name";
    private const string ChatClass = "world-label-chat";

    // Базовый кегль 12px взят из ThemeTokens.uss (--size-sm), опорный зум 17.5 —
    // из контрактов камеры. Оба значения продублированы намеренно: оракул
    // проверки не должен считать ожидаемый результат тем же кодом, который
    // его производит.
    private const float ThemeFontSizePx = 12f;
    private const float ReferenceZoom = 17.5f;

    // Поля облака из WorldLabels.uss: 3px сверху и с боков, 15px снизу под хвост
    // спрайта. Панель в ScaleWithScreenSize пересчитывает USS-пиксели в пункты, и
    // коэффициент панели постоянен для всех измерений этого теста, поэтому
    // сравнения идут либо к первому замеру, либо через отношение к боковому полю.
    private const float TailPaddingPx = 15f;
    private const float SidePaddingPx = 3f;

    // Центр хвоста в исходном спрайте LocalChatBubble (32x32, хвост на x=7..9).
    // Продублирован намеренно: оракул проверки не должен считать ожидаемое
    // положение тем же числом, которым его записывает WorldLabels.
    private const float TailAnchorSourceX = 8f;

    private const float FontSizeTolerancePx = 0.5f;
    private const float PanelAnchorTolerancePx = 1f;
    private const float CellSettleTolerance = 0.01f;
    private const int ZoomFrameBudget = 600;

    // Колесо вниз отдаляет камеру, вверх — приближает: CameraFollow вычитает
    // прокрутку из целевого зума, поэтому вверх — меньший orthographicSize.
    private const int WheelDown = -1;
    private const int WheelUp = 1;

    private ILocalPlayerState _localPlayer = null!;
    private IRobotService _robots = null!;
    private IMapDataProvider _map = null!;
    private ChatEventGateway _chat = null!;
    private Camera _camera = null!;
    private UIDocument _document = null!;
    private VirtualMouse? _mouse;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        yield return PlayModeHarness.StartAtGateway();
        BootstrapLifetimeScope bootstrap = PlayModeHarness.FindBootstrap()!;
        yield return PlayModeHarness.EnterMainGame(bootstrap);

        _camera = PlayModeHarness.RequireInGame<IGameplayCamera>().Camera;
        _localPlayer = PlayModeHarness.RequireInGame<ILocalPlayerState>();
        _robots = PlayModeHarness.RequireInGame<IRobotService>();
        _map = PlayModeHarness.RequireInGame<IMapDataProvider>();
        _chat = PlayModeHarness.RequireInGame<ChatEventGateway>();
        _document = PlayModeHarness.FindComponentInScene<UIDocument>(
            PlayModeHarness.Scene(ProjectRuntimeContracts.SceneNames.MainGame))!;
        Assert.That(_document, Is.Not.Null, "MainGame has no UIDocument.");
        ILocalPlayer player = _localPlayer.Current ??
            throw new AssertionException("The local player was never spawned.");

        _mouse = new VirtualMouse();

        // UpdateServerPosition — тот же вход, которым клиент применяет позицию
        // от сервера. CameraFollow на такой скачок не гонит камеру через всю
        // карту, а ждёт готовности террейна и переставляет её одним кадром,
        // поэтому вид меняется на 1315:15 штатным путём телепорта.
        player.UpdateServerPosition(new Vector2Int(AnchorX, AnchorY));
        Vector3 anchor = CellCenter(AnchorX, AnchorY);
        yield return PlayModeHarness.WaitUntil(
            () => Vector2.Distance(
                (Vector2)_camera.transform.position, (Vector2)anchor) <= 0.05f,
            PlayModeHarness.WorldTimeoutSeconds,
            $"The camera did not reach 1315:15 (it stayed at {_camera.transform.position}).");

        // Второй робот появляется после того, как вид встал на место: иначе он
        // мигал бы в unloaded-чанке и светился бы об ошибках стримера.
        _robots.UpdateRobotMetadata(
            ProbeBotID,
            new RobotMetadata(-1, 0, ProbeNickname, string.Empty, string.Empty));
        _robots.UpdateRobotPosition(ProbeBotID, ProbeX, ProbeY, 0);

        yield return PlayModeHarness.WaitUntil(
            () => RobotSettled(),
            PlayModeHarness.UITimeoutSeconds,
            "The probe robot never settled in cell 1316:15.");
        yield return Settle();
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        _mouse?.Dispose();
        _mouse = null;
        yield return PlayModeHarness.Shutdown();
    }

    [UnityTest]
    public IEnumerator Nickname_KeepsWorldSizeThroughZoomOutAndBackIn()
    {
        Label? found = null;
        yield return PlayModeHarness.WaitUntil(
            () => (found = FindVisible(NicknameClass, ProbeNickname)) != null,
            PlayModeHarness.UITimeoutSeconds,
            "The probe robot's nameplate never became visible.");
        Label nameplate = found!;

        float startZoom = _camera.orthographicSize;
        float startFont = nameplate.resolvedStyle.fontSize;
        AssertWorldFontSize(startFont, startZoom, "the starting zoom");
        Assert.That(
            nameplate.resolvedStyle.width,
            Is.GreaterThan(0f),
            "The nameplate has no box: nothing was measured.");

        // Отдаление: робот сжимается, и ник обязан сжаться вместе с ним.
        yield return ZoomUntil(zoom => zoom >= startZoom * 1.4f, WheelDown);
        yield return Settle();
        float farZoom = _camera.orthographicSize;
        float farFont = nameplate.resolvedStyle.fontSize;
        AssertWorldFontSize(farFont, farZoom, "the pulled-away camera");
        Assert.That(
            farFont,
            Is.LessThan(startFont - 0.5f),
            "The nickname grew on screen while the camera pulled away: its font size is " +
            "fixed in panel pixels instead of being bound to the zoom.");

        // Приближение: ник растёт вместе с роботом.
        yield return ZoomUntil(zoom => zoom <= farZoom * 0.6f, WheelUp);
        yield return Settle();
        float nearZoom = _camera.orthographicSize;
        float nearFont = nameplate.resolvedStyle.fontSize;
        AssertWorldFontSize(nearFont, nearZoom, "the close-up camera");
        Assert.That(
            nearFont,
            Is.GreaterThan(farFont + 0.5f),
            "The nickname did not grow when the camera came closer.");

        // Отношение не зависит от базового кегля: это и есть постоянство
        // размера в мире при любом зуме.
        Assert.That(
            farFont / nearFont,
            Is.EqualTo(farZoom / nearZoom).Within(0.02f),
            "Nickname font size does not follow the zoom ratio.");
    }

    [UnityTest]
    public IEnumerator ChatBubble_ScalesItsWholeBoxAndKeepsTheTailOnTheCellTop()
    {
        yield return ShowBubble();
        Label? found = null;
        yield return PlayModeHarness.WaitUntil(
            () => (found = FindVisible(ChatClass, ProbeMessage)) != null,
            PlayModeHarness.UITimeoutSeconds,
            "The local chat bubble never became visible.");
        Label bubble = found!;

        float startZoom = _camera.orthographicSize;
        IResolvedStyle start = bubble.resolvedStyle;

        // Облако масштабируется одним множителем на весь бокс, поэтому в раскладке
        // кегль и поля остаются базовыми, а меняется style.scale. Проверяется
        // именно это: иначе облако text-only масштабировалось бы, а рамка из
        // 9-slice осталась бы прежней.
        float startScale = start.scale.value.x;
        Assert.That(startScale, Is.EqualTo(ReferenceZoom / startZoom).Within(0.005f));
        Assert.That(start.paddingBottom / start.paddingLeft,
            Is.EqualTo(TailPaddingPx / SidePaddingPx).Within(0.01f),
            "The bubble does not reserve room for the sprite tail below the text.");
        Assert.That(start.width, Is.GreaterThan(0f), "The bubble has no box: nothing was measured.");
        AssertTailOnCellTop(bubble, "the starting zoom", startScale);

        // Приближение: облако растёт вместе с роботом.
        yield return ZoomUntil(zoom => zoom <= startZoom * 0.6f, WheelUp);
        yield return ShowBubble();
        yield return PlayModeHarness.WaitUntil(
            () => FindVisible(ChatClass, ProbeMessage) != null,
            PlayModeHarness.UITimeoutSeconds,
            "The bubble did not come back after the zoom.");
        bubble = FindVisible(ChatClass, ProbeMessage)!;

        float nearZoom = _camera.orthographicSize;
        float nearScale = bubble.resolvedStyle.scale.value.x;
        Assert.That(
            nearScale,
            Is.EqualTo(ReferenceZoom / nearZoom).Within(0.005f),
            "The bubble scale does not follow the zoom.");
        Assert.That(
            nearScale,
            Is.GreaterThan(startScale + 0.01f),
            "The bubble did not grow on zoom in.");

        // Рамка спрайта не растягивается, поэтому поля в раскладке обязаны
        // остаться базовыми при любом зуме: умноженные на зум отдельно от рамки,
        // они стали бы короче хвоста, и текст наезжал бы на него при отдалении.
        // Кегль в раскладке тоже базовый — его теперь умножает style.scale.
        IResolvedStyle nearStyle = bubble.resolvedStyle;
        Assert.That(nearStyle.paddingBottom / nearStyle.paddingLeft,
            Is.EqualTo(TailPaddingPx / SidePaddingPx).Within(0.01f),
            "The tail padding changed with zoom: it is being scaled separately from the frame.");
        Assert.That(nearStyle.fontSize / nearStyle.paddingLeft,
            Is.EqualTo(start.fontSize / start.paddingLeft).Within(0.02f),
            "The bubble font size was rescaled in layout instead of being scaled as a whole box.");
        AssertTailOnCellTop(bubble, "the close-up camera", nearScale);

        // Отдаление: облако уменьшается, хвост остаётся над роботом.
        yield return ZoomUntil(zoom => zoom >= nearZoom * 1.5f, WheelDown);
        yield return ShowBubble();
        yield return PlayModeHarness.WaitUntil(
            () => FindVisible(ChatClass, ProbeMessage) != null,
            PlayModeHarness.UITimeoutSeconds,
            "The bubble did not come back after the second zoom.");
        bubble = FindVisible(ChatClass, ProbeMessage)!;

        float farZoom = _camera.orthographicSize;
        float farScale = bubble.resolvedStyle.scale.value.x;
        Assert.That(
            farScale,
            Is.EqualTo(ReferenceZoom / farZoom).Within(0.005f),
            "The bubble scale does not follow the pulled-away zoom.");
        Assert.That(
            farScale,
            Is.LessThan(nearScale - 0.01f),
            "The bubble did not shrink on zoom out.");
        AssertTailOnCellTop(bubble, "the pulled-away camera", farScale);
    }

    [UnityTest]
    public IEnumerator ChatBubble_TakesItsSliceBordersFromTheSprite()
    {
        // Рамка рисуется спрайтом с 9-slice, и UI Toolkit берёт границы из
        // sprite.border. Проверяется сверкой resolvedStyle с загруженным
        // спрайтом: захардкоженные в коде границы разъедутся с ассетом при
        // первом же перерисовывании хвоста, и облако продолжит рисоваться, но
        // уже неправильно.
        yield return ShowBubble();
        yield return PlayModeHarness.WaitUntil(
            () => FindVisible(ChatClass, ProbeMessage) != null,
            PlayModeHarness.UITimeoutSeconds,
            "The local chat bubble never became visible.");
        Label bubble = FindVisible(ChatClass, ProbeMessage)!;

        Sprite sprite = bubble.resolvedStyle.backgroundImage.sprite;
        Assert.That(sprite, Is.Not.Null, "The bubble has no sprite background.");
        Assert.That(sprite!.name, Is.EqualTo("LocalChatBubble"));

        IResolvedStyle style = bubble.resolvedStyle;
        BackgroundSize size = style.backgroundSize;
        Assert.That(
            size.sizeType,
            Is.EqualTo(BackgroundSizeType.Length),
            "The 9-slice background needs an explicit size; otherwise the sprite is fitted into " +
            "the box whole and the tail becomes a band.");
        Assert.That(size.x.value, Is.EqualTo(100f).Within(0.01f));
        Assert.That(size.y.value, Is.EqualTo(100f).Within(0.01f));
        Assert.That(style.unitySliceLeft, Is.EqualTo(sprite.border.x));
        Assert.That(style.unitySliceRight, Is.EqualTo(sprite.border.z));
        Assert.That(style.unitySliceTop, Is.EqualTo(sprite.border.w));
        Assert.That(style.unitySliceBottom, Is.EqualTo(sprite.border.y));
    }

    // Проверка кегля против опорного зума: USS задаёт базу 12px на панель, и
    // единственное, что обязано её менять, — масштаб зума.
    private static void AssertWorldFontSize(float measuredFontSize, float zoom, string phase)
    {
        float expected = ThemeFontSizePx * (ReferenceZoom / zoom);
        Assert.That(
            measuredFontSize,
            Is.EqualTo(expected).Within(FontSizeTolerancePx),
            $"Font size at {phase}: orthographicSize={zoom:F3} must render {expected:F2}px " +
            $"({ThemeFontSizePx}px world base at zoom {ReferenceZoom}), measured {measuredFontSize:F2}px.");
    }

    // Облако вешается за точку хвоста, а не за центр бокса: хвост заморожен в
    // нерастяжимом левом тайле 9-slice и всегда стоит в TailAnchorSourceX px от
    // левого края, поэтому нижний центр увёл бы хвост в сторону от робота.
    //
    // Оракул — бокс элемента в панели, его масштаб и проекция той же точки
    // через камеру, то есть ровно то, что видит игрок; формула переноса метки
    // не используется.
    private void AssertTailOnCellTop(Label bubble, string phase, float scale)
    {
        Assert.That(bubble.panel, Is.Not.Null, $"The bubble has no panel at {phase}.");
        Vector3 cellTop = CellCenter(ProbeX, ProbeY) + new Vector3(0f, 0.5f, 0f);
        Vector2 expected = RuntimePanelUtils.CameraTransformWorldToPanel(
            bubble.panel, cellTop, _camera);
        Rect box = bubble.worldBound;
        Vector2 tail = new(box.xMin + (TailAnchorSourceX * scale), box.yMin);
        Assert.That(
            Vector2.Distance(tail, expected),
            Is.LessThan(PanelAnchorTolerancePx),
            $"At {phase} the bubble's tail anchor is {tail}, but cell top 1316:15 is " +
            $"at {expected}: the anchor was not recomputed for the current box and scale " +
            $"(zoom={_camera.orthographicSize:F3}, box={box.size}, scale={scale:F4}).");
    }

    private IEnumerator ShowBubble()
    {
        // Обычный путь сообщения: тот же пакет, что приходит с сервера, и тот же
        // gateway, из которого его публикует ChatProcessor. Пузырь живёт три
        // секунды, поэтому на каждый замер приходит свежий: заодно проверяется
        // переиспользование пузыря из пула с уже переопределённым кеглем.
        _chat.Publish(new LocalChatMessagePacket(ProbeBotID, ProbeX, ProbeY, ProbeMessage));
        yield return Settle();
    }

    private IEnumerator ZoomUntil(Func<float, bool> reached, int wheel)
    {
        for (int frame = 0; frame < ZoomFrameBudget; frame++)
        {
            if (reached(_camera.orthographicSize))
            {
                yield break;
            }

            _mouse!.Scroll(wheel);
            yield return Pump(1);
        }

        Assert.Fail(
            $"The camera zoom did not reach the target in {ZoomFrameBudget} frames: " +
            $"orthographicSize={_camera.orthographicSize:F3}.");
    }

    private IEnumerator Pump(int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            // Сервер присылает позицию каждый тик; без неё prune снёс бы робота
            // через 2.5 с молчания, и ник пропал бы из панели.
            _robots.UpdateRobotPosition(ProbeBotID, ProbeX, ProbeY, 0);
            yield return null;
        }
    }

    // Замер делается только на устоявшемся зуме: CameraFollow сглаживает
    // orthographicSize к целевому значению, и замер на промежуточном кадре
    // сравнивал бы кегль с одним зумом, а фон облака с другим. Плюс два
    // кадра на раскладку панели: кегль переписывает бокс, и только на
    // следующем кадре якорь облака встаёт на новое место.
    private IEnumerator Settle()
    {
        float previous = _camera.orthographicSize;
        for (int frame = 0; frame < ZoomFrameBudget; frame++)
        {
            yield return Pump(1);
            float current = _camera.orthographicSize;
            if (Mathf.Approximately(current, previous))
            {
                yield return Pump(2);
                yield break;
            }

            previous = current;
        }

        Assert.Fail(
            $"The camera zoom never settled: orthographicSize={_camera.orthographicSize:F3}.");
    }

    private bool RobotSettled()
    {
        if (!_robots.TryGetRobot(ProbeBotID, out IRobotView? robot) || robot == null)
        {
            return false;
        }

        Vector3 cell = CellCenter(ProbeX, ProbeY);
        return Vector2.Distance(
            (Vector2)robot.transform.position, (Vector2)cell) <= CellSettleTolerance;
    }

    private Vector3 CellCenter(ushort x, ushort y) =>
        CoordinateUtils.ServerToUnityPos(x, y, _map.WorldHeight);

    private Label? FindVisible(string className, string text)
    {
        foreach (Label label in _document.rootVisualElement
                     .Query<Label>(className: className).ToList())
        {
            if (label.text == text && label.resolvedStyle.visibility == Visibility.Visible)
            {
                return label;
            }
        }

        return null;
    }
}

/// <summary>
/// Виртуальная мышь теста: колесо крутит тест, а не разработчика, и устройство
/// удаляется после прогона. Без неё зум не изменить никак — CameraFollow
/// читает только ввод, и прямой записи ортографического размера в нём нет.
/// </summary>
internal sealed class VirtualMouse : IDisposable
{
    private readonly Mouse _device;

    public VirtualMouse()
    {
        _device = InputSystem.AddDevice<Mouse>("KernTestMouse");
        _device.MakeCurrent();
    }

    public void Scroll(int direction) =>
        InputSystem.QueueStateEvent(_device, new MouseState { scroll = new Vector2(0f, direction) });

    public void Dispose() => InputSystem.RemoveDevice(_device);
}
