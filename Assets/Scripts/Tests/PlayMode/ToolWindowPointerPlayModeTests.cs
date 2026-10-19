#nullable enable

using System.Collections;
using Kern.Tools.ImGui;
using Kern.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Kern.Tests.PlayMode;

public sealed class ToolWindowPointerPlayModeTests
{
    [UnityTest]
    public IEnumerator ToolWindowPreventsClickOnUnderlyingHUD()
    {
        bool previousEnabled = ToolWindows.Enabled;
        var settings = ScriptableObject.CreateInstance<PanelSettings>();
        var owner = new GameObject("Tool pointer regression") { hideFlags = HideFlags.HideAndDontSave };
        var document = owner.AddComponent<UIDocument>();
        document.panelSettings = settings;
        Mouse mouse = InputSystem.AddDevice<Mouse>();
        var window = new PointerTestWindow();
        try
        {
            yield return null;
            UIScreen.Bind(document, null);
            var button = new Button();
            document.rootVisualElement.Add(button);
            int clicks = 0;
            button.RegisterCallback<ClickEvent>(_ => clicks++);
            ToolWindows.Register(window);
            ToolWindows.Enabled = true;
            Vector2 position = new(Screen.width * 0.5f, Screen.height * 0.5f);
            float scale = ToolWindows.Scale;
            window.Rect = new Rect(position.x / scale - 25f,
                (Screen.height - position.y) / scale - 25f, 50f, 50f);
            window.Visible = true;
            yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = position });
            InputSystem.Update();
            mouse.MakeCurrent();
            Assert.That(ToolWindows.ContainsScreenPoint(position), Is.True);
            Assert.That(document.rootVisualElement.panel, Is.Not.Null);
            Assert.That(Pointer.current, Is.SameAs(mouse));
            Assert.That(mouse.position.ReadValue(), Is.EqualTo(position), "The regression pointer must remain over the tools window.");
            Assert.That(ToolWindows.ContainsScreenPoint(Pointer.current.position.ReadValue()), Is.True);
            using (ClickEvent click = ClickEvent.GetPooled())
            {
                Assert.That(click.tricklesDown, Is.True);
                click.target = button;
                button.SendEvent(click);
            }

            Assert.That(clicks, Is.Zero, "The tools window must intercept the click before the HUD callback.");
            window.Visible = false;
            using (ClickEvent click = ClickEvent.GetPooled())
            {
                click.target = button;
                button.SendEvent(click);
            }

            Assert.That(clicks, Is.EqualTo(1), "Uncovered HUD must remain clickable.");
        }
        finally
        {
            UIScreen.Unbind(document);
            ToolWindows.Unregister(window);
            ToolWindows.Enabled = previousEnabled;
            window.Dispose();
            InputSystem.RemoveDevice(mouse);
            Object.Destroy(owner);
            Object.Destroy(settings);
        }
    }

    private sealed class PointerTestWindow : ToolWindow
    {
        public PointerTestWindow() : base("Pointer regression fixture", new Rect(0, 0, 50, 50))
        {
        }

        protected override void DrawContent()
        {
        }
    }
}
