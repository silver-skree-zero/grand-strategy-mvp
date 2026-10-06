using UnityEngine;
using UnityEngine.UIElements;

public class EventWindowController : MonoBehaviour
{
    [SerializeField] private UIDocument uiDocument;
    [SerializeField] private Simulator simulator;
    [SerializeField] private VisualTreeAsset eventWindowTemplate;
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
        
        //continueButton.clicked += OnContinueClicked;
        //Hide();
    }

    public void Show(string title, string description, Texture2D image)
    {
        simulator.Pause();
        VisualElement window =
            eventWindowTemplate.Instantiate();

        eventLayer.Add(window);

        Label eventTitle = window.Q<Label>("EventTitle");
        Image eventImage = window.Q<Image>("EventImage");
        Label eventDescription = window.Q<Label>("EventDescription");
        Button closeButton = window.Q<Button>("ContinueButton");

        eventTitle.text = title;
        eventDescription.text = description;
        eventImage.image = image;

        MakeDraggable(window);

        closeButton.clicked += () =>
        {
            window.RemoveFromHierarchy();
        };
    }

    private void MakeDraggable(VisualElement window)
    {
        VisualElement titleBar =
            window.Q<VisualElement>("EventTitle");

        bool dragging = false;
        Vector2 mouseStart = new Vector2(0,0);
        Vector2 windowStart = new Vector2(0,0);

        titleBar.RegisterCallback<PointerDownEvent>(evt =>
        {
            dragging = true;
            mouseStart = evt.position;

            windowStart = new Vector2(
                window.resolvedStyle.left,
                window.resolvedStyle.top
            );

            titleBar.CapturePointer(evt.pointerId);
        });

        titleBar.RegisterCallback<PointerMoveEvent>(evt =>
        {
            if (!dragging)
                return;

            Vector3 delta = evt.position - (new Vector3(mouseStart.x, mouseStart.y, 0));

            window.style.left = windowStart.x + delta.x;
            window.style.top = windowStart.y + delta.y;
        });

        titleBar.RegisterCallback<PointerUpEvent>(evt =>
        {
            dragging = false;
            titleBar.ReleasePointer(evt.pointerId);
        });
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