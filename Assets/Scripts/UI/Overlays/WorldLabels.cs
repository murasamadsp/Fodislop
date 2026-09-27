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

    private readonly List<Entry> _entries = [];
    private VisualElement? _root;
    private VisualElement? _container;
    private bool _zoomErrorLogged;

    public IWorldLabel Create(WorldLabelKind kind)
    {
        if (_root == null)
        {
            VisualTreeAsset template = Resources.Load<VisualTreeAsset>("UI/WorldLabels")
                ?? throw new InvalidOperationException("Missing UI/WorldLabels.");
            _root = template.CloneTree();
            _root.AddToClassList("world-labels");
            _root.pickingMode = PickingMode.Ignore;
            document.rootVisualElement.Insert(0, _root);
            _container = _root.Q("WorldLabels");
        }

        // Labels are a dynamic collection, not static screen structure.
        var label = new Label { pickingMode = PickingMode.Ignore, enableRichText = false };
        label.AddToClassList(kind == WorldLabelKind.ChatBubble
            ? "world-label-chat"
            : "world-label-name");
        _container!.Add(label);
        var entry = new Entry(this, label, kind);
        _entries.Add(entry);
        return entry;
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

    private sealed class Entry(WorldLabels owner, Label label, WorldLabelKind kind) : IWorldLabel
    {
        private const float PositionApplyEpsilonPx = 0.5f;

        // Зум квантуется пиксельной сеткой (PixelGrid.QuantizeOrthographicSize),
        // поэтому у камеры лишь несколько дискретных значений и запись кегля
        // случается на соседних шагах зума, а не каждый кадр. Порог оставлен
        // тем же приёмом, что и у позиции: дробная подстройка кегля ни на что
        // не влияет, а стиль без нужды помечается грязным.
        private const float ScaleApplyEpsilon = 0.002f;
        private const string OffscreenClass = "world-label-offscreen";

        public Label Label { get; } = label;
        public Vector3 Position { get; private set; }
        public bool Visible { get; private set; } = true;

        private Vector3 _lastAppliedPosition;
        private bool _lastAppliedVisible;
        private bool _hasApplied;

        // База берётся из USS один раз, до первой записи: пока кегль не
        // переопределён, resolvedStyle отдаёт ровно то, что задано стилями,
        // и число 12 не дублируется в коде. Ноль означает «база ещё не
        // разложена» — тогда запись откладывается до следующего кадра.
        private float _baseFontSize;
        private TextShadow _baseShadow;
        private float _baseMaxWidth;
        private float _basePaddingTop;
        private float _basePaddingRight;
        private float _basePaddingBottom;
        private float _basePaddingLeft;
        private float _baseBorderWidth;
        private float _baseBorderRadius;
        private float _lastScale = -1f;
        private bool _sizeDirty;

        public void SetText(string text) => Label.text = text;
        public void SetPosition(Vector3 position) => Position = position;
        public void SetVisible(bool visible) => Visible = visible;
        public void SetOpacity(float opacity) => Label.style.opacity = Mathf.Clamp01(opacity);

        // Перевод метки в постоянный мировой размер: кегль, а с ним поля,
        // рамка, скругление и предел ширины облака, умножаются на масштаб зума.
        // Один размер на все стороны снимается с первой грани: USS задаёт
        // border-* и border-radius одним значением, иначе рамка разъехалась бы.
        //
        // Облаку нужны и поля: без их пересчёта на сильном приближении текст
        // вылезал бы на рамку, а на отдалении облако состояло бы в основном из
        // полей. Нику, у которого рамки нет, достаточно кегля и тени.
        public void ApplyScale(float scale)
        {
            if (_baseFontSize <= 0f && !TryCaptureBase())
            {
                return;
            }

            if (_lastScale > 0f && Mathf.Abs(scale - _lastScale) <= ScaleApplyEpsilon)
            {
                return;
            }

            Label.style.fontSize = _baseFontSize * scale;
            if (kind == WorldLabelKind.ChatBubble)
            {
                Label.style.maxWidth = _baseMaxWidth * scale;
                Label.style.paddingTop = _basePaddingTop * scale;
                Label.style.paddingRight = _basePaddingRight * scale;
                Label.style.paddingBottom = _basePaddingBottom * scale;
                Label.style.paddingLeft = _basePaddingLeft * scale;
                Label.style.borderTopWidth = _baseBorderWidth * scale;
                Label.style.borderRightWidth = _baseBorderWidth * scale;
                Label.style.borderBottomWidth = _baseBorderWidth * scale;
                Label.style.borderLeftWidth = _baseBorderWidth * scale;
                Label.style.borderTopLeftRadius = _baseBorderRadius * scale;
                Label.style.borderTopRightRadius = _baseBorderRadius * scale;
                Label.style.borderBottomRightRadius = _baseBorderRadius * scale;
                Label.style.borderBottomLeftRadius = _baseBorderRadius * scale;
            }
            else
            {
                TextShadow shadow = _baseShadow;
                shadow.offset *= scale;
                Label.style.textShadow = shadow;
            }

            _lastScale = scale;

            // Смена кегля меняет бокс, а якорь облака задан его размером:
            // пересчёт смещения обязан повториться на кадре, где раскладка уже
            // отдаёт новые width/height. Пока этого не случилось, смещение
            // записано по старому размеру, и ApplyVisible его не запоминает.
            _sizeDirty = true;
        }

        private bool TryCaptureBase()
        {
            IResolvedStyle style = Label.resolvedStyle;
            if (!IsResolved(style.fontSize) || style.fontSize <= 0f)
            {
                return false;
            }

            // maxWidth — единственное разрешённое значение метки, приходящее не
            // как float, поэтому снимается через .value.
            float maxWidth = style.maxWidth.value;
            if (kind == WorldLabelKind.ChatBubble &&
                (!IsResolved(maxWidth) || maxWidth <= 0f ||
                 !IsResolved(style.paddingTop) || !IsResolved(style.paddingRight) ||
                 !IsResolved(style.paddingBottom) || !IsResolved(style.paddingLeft) ||
                 !IsResolved(style.borderLeftWidth) || !IsResolved(style.borderTopLeftRadius)))
            {
                return false;
            }

            // База записывается целиком или никак: частичная запись обнулила бы
            // поля облака, и рамка схлопнулась бы в линию на первый же кадр.
            _baseFontSize = style.fontSize;
            _baseShadow = style.textShadow;
            if (kind == WorldLabelKind.ChatBubble)
            {
                _baseMaxWidth = maxWidth;
                _basePaddingTop = style.paddingTop;
                _basePaddingRight = style.paddingRight;
                _basePaddingBottom = style.paddingBottom;
                _basePaddingLeft = style.paddingLeft;
                _baseBorderWidth = style.borderLeftWidth;
                _baseBorderRadius = style.borderTopLeftRadius;
            }

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

            // translate двигает бокс целиком, поэтому угол привязки вычитается
            // из его размера: облако висит нижним центром над роботом,
            // никнейм — левым верхним углом от точки как есть.
            Vector3 offset = kind == WorldLabelKind.ChatBubble
                ? new Vector3(position.x - (size.x * 0.5f), position.y - size.y)
                : position;
            Label.style.translate = new Translate(offset.x, offset.y);
            if (_sizeDirty)
            {
                // Смещение только что посчитано по боксу прежнего кегля.
                // Запоминать позицию нельзя: сдвига больше не будет, флаг
                // positionChanged не поднимется, и якорь облака навсегда
                // останется на старом размере. Следующий кадр повторяет
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
            Label.RemoveFromHierarchy();
        }

        // Скрытие через visibility, а не через UIState.SetHidden с display:none.
        // display:none выводит элемент из раскладки, ширина и высота становятся
        // NaN, и посчитать нижний центр больше нечем. visibility:hidden элемент
        // раскладывается, поэтому повторный показ всегда знает свой размер.
        private void SetOffscreen(bool offscreen) =>
            Label.EnableInClassList(OffscreenClass, offscreen);

        private bool TryResolveSize(out Vector2 size)
        {
            if (kind != WorldLabelKind.ChatBubble)
            {
                size = Vector2.zero;
                return true;
            }

            IResolvedStyle style = Label.resolvedStyle;
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
