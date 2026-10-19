#nullable enable

#if UNITY_EDITOR
using NUnit.Framework;
using UnityEditor;

namespace Kern.Tests.PlayMode;

// Namespace-wide mute, active before a fixture loads Bootstrap or starts music.
// FMOD RuntimeManager applies EditorUtility.audioMasterMute to its master bus.
[SetUpFixture]
public sealed class PlayModeAudioSetup
{
    private bool _previousEditorMute;
    private bool _hasSavedState;

    [OneTimeSetUp]
    public void MuteTestAudio()
    {
        _previousEditorMute = EditorUtility.audioMasterMute;
        _hasSavedState = true;
        EditorUtility.audioMasterMute = true;
        EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
        Assert.That(EditorUtility.audioMasterMute, Is.True);
        TestContext.WriteLine("Test audio muted: Editor master mute=true (Unity audio and FMOD master bus).");
    }

    [OneTimeTearDown]
    public void RestoreAudio()
    {
        EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
        if (!_hasSavedState)
        {
            return;
        }

        EditorUtility.audioMasterMute = _previousEditorMute;
        _hasSavedState = false;
    }

    private void HandlePlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingPlayMode)
        {
            RestoreAudio();
        }
    }
}
#endif
