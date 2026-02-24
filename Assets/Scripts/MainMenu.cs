using UnityEngine;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
[RequireComponent(typeof(UIMocker))]
public class MainMenu : MonoBehaviour
{
    private UIDocument _doc;
    private UIMocker _mocker;
    private VisualElement _mainMenuContainer;

    void OnEnable()
    {
        _doc = GetComponent<UIDocument>();
        _mocker = GetComponent<UIMocker>();

        var root = _doc.rootVisualElement;
        root.style.justifyContent = Justify.Center;
        root.style.alignItems = Align.Center;

        var mainMenuUXML = Resources.Load<VisualTreeAsset>("UI/MainMenu");
        
        var mainMenu = mainMenuUXML.CloneTree();
        _mainMenuContainer = mainMenu.Q<VisualElement>("MainMenuContainer");

        var playButton = mainMenu.Q<Button>("PlayButton");
        playButton.clicked += OnPlayButtonClicked;

        var playComplexButton = mainMenu.Q<Button>("PlayComplexButton");
        playComplexButton.clicked += OnPlayComplexButtonClicked;

        root.Add(mainMenu);
    }

    private void OnPlayButtonClicked()
    {
        var builtUI = _mocker.RunMock();
        _doc.rootVisualElement.Add(builtUI);
        // Optionally, hide the main menu after clicking play
        _mainMenuContainer.style.display = DisplayStyle.None;
    }

    private void OnPlayComplexButtonClicked()
    {
        var builtUI = _mocker.RunComprehensiveMock();
        _doc.rootVisualElement.Add(builtUI);
        // Optionally, hide the main menu after clicking play
        _mainMenuContainer.style.display = DisplayStyle.None;
    }
}