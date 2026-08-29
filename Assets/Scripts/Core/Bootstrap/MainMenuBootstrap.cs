#nullable enable

using System;
using Cysharp.Threading.Tasks;
using Fodinae.Core.Interfaces;
using Fodinae.UI;
using VContainer.Unity;

namespace Fodinae.Core;

public sealed class MainMenuBootstrap : IStartable
{
    private readonly MainMenuLifetimeScope _scope;
    private readonly MainMenu _controller;
    private readonly IAudioSystem _audioSystem;
    private readonly SceneTransitionTicket _ticket;

    public MainMenuBootstrap(
        MainMenuLifetimeScope scope,
        MainMenu controller,
        IAudioSystem audioSystem,
        SceneTransitionTicket ticket)
    {
        _scope = scope;
        _controller = controller;
        _audioSystem = audioSystem;
        _ticket = ticket;
        _ticket.Attach(_scope.gameObject.scene);
    }

    public void Start()
    {
        StartAsync().Forget();
    }

    private async UniTaskVoid StartAsync()
    {
        try
        {
            await _ticket.WaitForActivationAsync();
            _controller.InitializeScene(_scope.Starfield, _scope.Scenery);
            // Presentation readiness requires the required audio banks to be
            // resident: scene audio must be live the moment the scene is shown,
            // not pop in a second later when the background bank load lands.
            await _audioSystem.WaitUntilBanksReadyAsync(_scope.destroyCancellationToken);
            _ticket.MarkStartupReady();
            _ticket.MarkPresentationReady();
        }
        catch (Exception exception)
        {
            _ticket.Fail(exception);
        }
    }
}
