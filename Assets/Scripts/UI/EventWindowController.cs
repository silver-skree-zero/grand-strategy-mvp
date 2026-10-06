using UnityEngine;
using UnityEngine.UIElements;

public class EventWindowController : MonoBehaviour
{
    [SerializeField] private UIDocument uiDocument;
    [SerializeField] private Simulator simulator;
    private VisualElement eventLayer;
    private Label eventTitle;
    private Image eventImage;
    private Label eventDescription;
    private Button continueButton;

    private void Awake()
    {
        VisualElement root = uiDocument.rootVisualElement;

        eventLayer = root.Q<VisualElement>("EventLayer");
        eventTitle = root.Q<Label>("EventTitle");
        eventImage = root.Q<Image>("EventImage");
        eventDescription = root.Q<Label>("EventDescription");
        continueButton = root.Q<Button>("ContinueButton");

        continueButton.clicked += OnContinueClicked;

        Hide();
    }

    public void Show(string title, string description, Texture2D image)
    {
        eventTitle.text = title;
        eventDescription.text = description;
        eventImage.image = image;

        eventLayer.style.display = DisplayStyle.Flex;
        simulator.Pause();
        Debug.Log("Event window created");
    }

    public void Hide()
    {
        eventLayer.style.display = DisplayStyle.None;
    }

    private void OnContinueClicked()
    {
        Hide();
    }
}