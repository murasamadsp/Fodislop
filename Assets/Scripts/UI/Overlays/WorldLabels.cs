#nullable enable

using System;
using System.Collections.Generic;
using Kern.Core;
using Kern.Core.Interfaces;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer.Unity;

namespace Kern.UI;

public sealed class WorldLabels(UIDocument document, IGameplayCamera camera) : IWorldLabels, ILateTickable, IDisposable
{
    private const string TAG = "[WorldLabels]";

    // Рамка облака — спрайт LocalChatBubble с 9-slice, границы которого лежат в
    // самом ассете (spriteBorder в импортере) и потому в коде не повторяются.
    // У Single-спрайта Unity читает именно spriteBorder, а не border из массива
    // spriteSheet, где стояло 1 px сверху: полоса растяжения захватывала бы
    // скругления, и на двухстрочном облаке углы вытянулись бы в эллипс.
    private const string BubbleSpritePath = ProjectRuntimeContracts.ResourcePaths.LocalChatBubbleSprite;

    // Центр хвоста в исходном спрайте (32x32, хвост занимает x=7..9). Облако
    // вешается за эту точку, а не за центр бокса: хвост заморожен в левом
    // нерастяжимом тайле 9-slice и всегда стоит в 7..9 px от левого края, поэтому
    // только так он оказывается над роботом, а тело облака уходит вправо.
    private const float TailAnchorSourceX = 8f;

    // Полупрозрачность рамки — как в оригинальном клиенте: белый тон с альфой
    // 157/255 (LocalChat.prefab, m_Color = {1,1,1, a=0.6156863}).
    //
    // Тон задаётся здесь, а не в USS: background-image назначается кодом, и
    // правило unity-background-image-tint-color без фона в USS не разрешалось —
    // рамка оставалась непрозрачной. Тоном, а не альфой в ассете, чтобы PNG
    // оставался белым и переиспользуемым.
    private static readonly Color FrameTint = new(1f, 1f, 1f, 0.6156863f);

    private const string BubbleClass = "world-label-chat";
    private const string BubbleTextClass = "world-label-chat-text";
    private const string NameClass = "world-label-name";

    private static Sprite? _bubbleSprite;
    private static bool _bubbleSpriteMissingLogged;

    private readonly List<Entry> _entries = [];
    private VisualElement? _root;
    private VisualElement? _container;
    private bool _zoomErrorLogged;

    public IWorldLabel Create(WorldLabelKind kind)
    {
        if (_root == null)
        {
            VisualTreeAsset template = Resources.Load<VisualTreeAsset>("UI/Gameplay/WorldLabels")
                ?? throw new InvalidOperationException("Missing UI/Gameplay/WorldLabels.");
            _root = template.CloneTree();
            _root.AddToClassList("world-labels");
            _root.pickingMode = PickingMode.Ignore;
            document.rootVisualElement.Insert(0, _root);
            _container = _root.Q("WorldLabels");
        }

        // Labels are a dynamic collection, not static screen structure.
        //
        // Облаку нужны два элемента, никнейму один. Рамка и текст в одном
        // элементе означали бы, что высота обязана расти под перенос строк, а
        // высота облака постоянна по эталону: постоянную высоту задаёт рамка, а
        // текст лежит в ней вложенным и обрезается по ширине.
        VisualElement frame;
        Label label;
        if (kind == WorldLabelKind.ChatBubble)
        {
            frame = new VisualElement { pickingMode = PickingMode.Ignore };
            frame.AddToClassList(BubbleClass);
            ApplyBubbleFrame(frame);
            label = new Label { pickingMode = PickingMode.Ignore, enableRichText = false };
            label.AddToClassList(BubbleTextClass);
            frame.Add(label);
        }
        else
        {
            label = new Label { pickingMode = PickingMode.Ignore, enableRichText = false };
            label.AddToClassList(NameClass);
            frame = label;
        }

        _container!.Add(frame);
        var entry = new Entry(this, frame, label, kind);
        _entries.Add(entry);
        return entry;
    }

    // Фон облака, его тон и границы 9-slice ставятся один раз на рамку: тайлы
    // углов UI Toolkit рисует 1:1 из исходных 32 px.
    private static void ApplyBubbleFrame(VisualElement frame)
    {
        if (_bubbleSprite == null && !_bubbleSpriteMissingLogged)
        {
            _bubbleSprite = Resources.Load<Sprite>(BubbleSpritePath);
            if (_bubbleSprite == null)
            {
                // Молчаливая подмена на CSS-рамку спрятала бы дефект тем же
                // способом, каким он появился: облако выглядело бы исправным.
                _bubbleSpriteMissingLogged = true;
                Debug.LogError($"{TAG} Missing Resources/{BubbleSpritePath} sprite for the chat bubble frame.");
            }
        }

        if (_bubbleSprite == null)
        {
            return;
        }

        Sprite sprite = _bubbleSprite;
        frame.style.backgroundImage = new StyleBackground(sprite);
        frame.style.unityBackgroundImageTintColor = FrameTint;

        // Границы 9-slice обязаны быть заданы явно. UI Toolkit не подхватывает
        // sprite.border сам, а без них ScaleToFit вписывает весь спрайт в бокс
        // целиком, сохраняя пропорции 1:1 и центрируя по горизонтали: облако
        // растягивалось по вертикали, а по горизонтали оставалось квадратом с
        // пустотами по бокам. Числа берутся из ассета, а не из кода, поэтому
        // перерисовка хвоста или смена скругления не требует правок здесь.
        Vector4 border = sprite.border;
        frame.style.unitySliceLeft = (int)border.x;
        frame.style.unitySliceBottom = (int)border.y;
        frame.style.unitySliceRight = (int)border.z;
        frame.style.unitySliceTop = (int)border.w;

        // Фон тянется на весь бокс (100% 100%): нарезка 9-slice растягивает центральные
        // полосы, оставляя угловые тайлы в натуральную величину.
        //
        // Прежний ScaleToFit (unityBackgroundScaleMode) в Unity 6 помечен
        // устаревшим и игнорируется: спрайт вписывался в бокс целиком, и рамка не
        // растягивалась ни по какой оси — отсюда и было «только до 4 символов».
        // Contain/собственные пропорции оставили бы квадрат по центру.
        frame.style.backgroundSize = new BackgroundSize(
            Length.Percent(100f),
            Length.Percent(100f));

        // Масштабирование всего бокса transform'ом, а не кегля: тайлы 9-slice не
        // масштабируются, и кегль, посчитанный отдельно от рамки, на отдалении
        // отошёл бы от неё. Здесь origin в левом верхнем углу, чтобы визуальный
        // бокс начинался ровно там, куда его ставит translate.
        frame.style.transformOrigin = new TransformOrigin(Length.Percent(0f), Length.Percent(0f));
    }

    public void LateTick()
    {
        if (!document.enabled || _root?.panel == null)
        {
            return;
        }

        // Камера принадлежит Bootstrap и может быть уничтожена раньше этой сцены:
        // порядок разрушения сцен при выходе и в тестах не гарантирован.
        Camera? view = camera.Camera;
        if (view == null)
        {
            return;
        }

        // Кегль и бокс метки пересчитываются по зуму камеры: USS задаёт базу в
        // пикселях панели, а панель от зума не зависит, и без пересчёта текст
        // над роботом оставался бы прежних 12 px при любом отдалении.
        if (!WorldLabelScale.TryFor(view.orthographicSize, out float scale))
        {
            // Камера без пригодного размера кадра — дефект, а не повод оставить
            // метки прежнего кегля: молчаливая подмена размера скрыла бы его
            // ровно тем же способом, каким он появился.
            if (!_zoomErrorLogged)
            {
                _zoomErrorLogged = true;
                Debug.LogError(
                    $"{TAG} Camera reports orthographicSize={view.orthographicSize}; " +
                    "world label font size cannot be matched to the zoom.");
            }

            return;
        }

        foreach (Entry entry in _entries)
        {
            entry.ApplyScale(scale);

            Vector3 viewport = view.WorldToViewportPoint(entry.Position);
            bool visible = entry.Visible && viewport.z > 0f &&
                viewport.x >= -0.15f && viewport.x <= 1.15f &&
                viewport.y >= -0.15f && viewport.y <= 1.15f;
            if (!visible)
            {
                entry.ApplyHidden();
                continue;
            }

            Vector2 panelPosition = RuntimePanelUtils.CameraTransformWorldToPanel(
                _root.panel, entry.Position, view);
            Vector2 local = _container!.WorldToLocal(panelPosition);
            entry.ApplyVisible(new Vector3(local.x, local.y, 0f));
        }
    }

    public void Dispose()
    {
        _entries.Clear();
        _root?.RemoveFromHierarchy();
        _root = null;
        _container = null;
    }

    private sealed class Entry(
        WorldLabels owner,
        VisualElement frame,
        Label label,
        WorldLabelKind kind) : IWorldLabel
    {
        private const float PositionApplyEpsilonPx = 0.5f;

        // Зум квантуется пиксельной сеткой (PixelGrid.QuantizeOrthographicSize),
        // поэтому у камеры лишь несколько дискретных значений и запись кегля
        // случается на соседних шагах зума, а не каждый кадр. Порог оставлен
        // тем же приёмом, что и у позиции: дробная подстройка кегля ни на что
        // не влияет, а стиль без нужды помечается грязным.
        private const float ScaleApplyEpsilon = 0.002f;
        private const string OffscreenClass = "world-label-offscreen";

        public VisualElement Frame { get; } = frame;
        public Vector3 Position { get; private set; }
        public bool Visible { get; private set; } = true;

        private Vector3 _lastAppliedPosition;
        private bool _lastAppliedVisible;
        private bool _hasApplied;

        // База берётся из USS один раз, до первой записи: пока кегль не
        // переопределён, resolvedStyle отдаёт ровно то, что задано стилями,
        // и число 12 не дублируется в коде. Ноль означает «база ещё не
        // разложена» — тогда запись откладывается до следующего кадра.
        //
        // Это путь только никнейма: у него своя рамка нет, и кегль с тенью
        // пересчитываются отдельно. Облаку база не нужна — оно пишет единственный
        // style.scale, который USS не портит.
        private float _baseFontSize;
        private TextShadow _baseShadow;
        private float _lastScale = -1f;
        private bool _sizeDirty;

        public void SetText(string text) => label.text = text;
        public void SetPosition(Vector3 position) => Position = position;
        public void SetVisible(bool visible) => Visible = visible;

        // Прозрачность задаётся рамке: у облака текст лежит вложенным элементом,
        // и гашение одного только текста оставило бы белый прямоугольник.
        public void SetOpacity(float opacity) => Frame.style.opacity = Mathf.Clamp01(opacity);

        // Перевод метки в постоянный мировой размер.
        //
        // Облако масштабируется целиком, через style.scale на рамке: тайлы
        // 9-slice рисуются в натуральную величину, поэтому масштабировать кегль
        // отдельно от рамки нельзя — на отдалении текст отошёл бы от неё. Один
        // множитель на весь бокс держит текст и хвост в одной пропорции при
        // любом зуме, а кегль в раскладке остаётся базовым.
        //
        // Нику, у которого рамки нет, достаточно кегля и тени.
        public void ApplyScale(float scale)
        {
            if (kind == WorldLabelKind.ChatBubble)
            {
                if (_lastScale > 0f && Mathf.Abs(scale - _lastScale) <= ScaleApplyEpsilon)
                {
                    return;
                }

                Frame.style.scale = new Scale(new Vector3(scale, scale, 1f));
                _lastScale = scale;

                // Смена масштаба меняет формулу смещения: якорь облака задан
                // размером бокса и положением хвоста, то есть оба множителя в
                // нём меняются. Пока ApplyVisible не пересчитал смещение по
                // новому масштабу, запоминать позицию нельзя.
                _sizeDirty = true;
                return;
            }

            if (_baseFontSize <= 0f && !TryCaptureBase())
            {
                return;
            }

            if (_lastScale > 0f && Mathf.Abs(scale - _lastScale) <= ScaleApplyEpsilon)
            {
                return;
            }

            label.style.fontSize = _baseFontSize * scale;
            TextShadow shadow = _baseShadow;
            shadow.offset *= scale;
            label.style.textShadow = shadow;

            _lastScale = scale;

            // Смена кегля меняет бокс, а якорь задан его размером: пересчёт
            // смещения обязан повториться на кадре, где раскладка уже отдаёт
            // новые width/height. Пока этого не случилось, смещение записано по
            // старому размеру, и ApplyVisible его не запоминает.
            _sizeDirty = true;
        }

        // Только для никнейма: он масштабирует кегль и тень, и обе величины
        // заданы в USS, поэтому база снимается до первой записи. Облаку база не
        // нужна — оно пишет единственный style.scale и USS не портит.
        private bool TryCaptureBase()
        {
            IResolvedStyle style = label.resolvedStyle;
            if (!IsResolved(style.fontSize) || style.fontSize <= 0f)
            {
                return false;
            }

            _baseFontSize = style.fontSize;
            _baseShadow = style.textShadow;
            return true;
        }

        private static bool IsResolved(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);

        // Запись в style.translate помечает стили элемента грязными без
        // сравнения значений, поэтому безусловная запись каждый кадр держала
        // всю панель в состоянии style-dirty: дерево пересчитывало стили,
        // раскладку и перекраску, даже когда метки стояли на месте. Пишем
        // только при смене видимости или сдвиге сверх половины пикселя —
        // тем же приёмом, что MissionArrowUI.
        public void ApplyVisible(Vector3 position)
        {
            bool visibilityChanged = !_hasApplied || !_lastAppliedVisible;
            bool positionChanged = !_hasApplied ||
                (position - _lastAppliedPosition).sqrMagnitude >
                    PositionApplyEpsilonPx * PositionApplyEpsilonPx;
            if (!visibilityChanged && !positionChanged)
            {
                return;
            }

            // Показываем независимо от того, посчитан ли размер. Раньше здесь
            // стоял выход по TryResolveSize, и это был дедлок: скрытие идёт
            // через visibility, размер доступен всегда, но на первом кадре он
            // ещё NaN, ранний выход оставлял метку скрытой, и снять скрытие
            // мог только ApplyVisible — то есть уже никогда. С переиспользованным
            // пузырём из пула сообщение переставало показываться навсегда.
            SetOffscreen(false);
            _lastAppliedVisible = true;
            _hasApplied = true;

            if (!TryResolveSize(out Vector2 size))
            {
                // Смещение не пишем, но и позицию не запоминаем: флаг
                // positionChanged останется поднятым, и следующий кадр повторит
                // попытку, как только раскладка посчитает размер.
                return;
            }

            // Облако вешается за хвост: нижняя грань визуального бокса идёт в
            // верхнюю грань клетки робота, а по горизонтали облако сдвинуто так,
            // чтобы точка хвоста встала в позицию робота. Размер бокса приходит
            // из раскладки и потому ещё не домножен на масштаб зума, а хвост
            // тайлом 9-slice не растягивается — оба множителя в формуле разные.
            //
            // Никнейм не масштабируется transform'ом и вешается левым верхним
            // углом прямо в точку.
            float scale = _lastScale > 0f ? _lastScale : 1f;
            Vector3 offset = kind == WorldLabelKind.ChatBubble
                ? new Vector3(position.x - (TailAnchorSourceX * scale), position.y - (size.y * scale))
                : position;
            Frame.style.translate = new Translate(offset.x, offset.y);
            if (_sizeDirty)
            {
                // Смещение только что посчитано по боксу прежнего кегля или
                // прежнего масштаба. Запоминать позицию нельзя: сдвига больше не
                // будет, флаг positionChanged не поднимется, и якорь облака
                // навсегда останется на старом размере. Следующий кадр повторяет
                // пересчёт по уже разложенному боксу.
                _sizeDirty = false;
                return;
            }

            _lastAppliedPosition = position;
        }

        public void ApplyHidden()
        {
            if (_hasApplied && !_lastAppliedVisible)
            {
                return;
            }

            SetOffscreen(true);
            _lastAppliedVisible = false;
            _hasApplied = true;
        }

        public void Dispose()
        {
            owner._entries.Remove(this);
            Frame.RemoveFromHierarchy();
        }

        // Скрытие через visibility, а не через UIState.SetHidden с display:none.
        // display:none выводит элемент из раскладки, ширина и высота становятся
        // NaN, и посчитать нижний центр больше нечем. visibility:hidden элемент
        // раскладывается, поэтому повторный показ всегда знает свой размер.
        private void SetOffscreen(bool offscreen) =>
            Frame.EnableInClassList(OffscreenClass, offscreen);

        private bool TryResolveSize(out Vector2 size)
        {
            if (kind != WorldLabelKind.ChatBubble)
            {
                size = Vector2.zero;
                return true;
            }

            // Размер рамки, а не текста: якорь идёт по хвосту спрайта, и текст
            // на него не влияет.
            IResolvedStyle style = Frame.resolvedStyle;
            if (float.IsNaN(style.width) || float.IsNaN(style.height) ||
                style.width <= 0f || style.height <= 0f)
            {
                size = Vector2.zero;
                return false;
            }

            size = new Vector2(style.width, style.height);
            return true;
        }
    }
}
