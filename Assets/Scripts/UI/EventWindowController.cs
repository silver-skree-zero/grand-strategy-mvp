using UnityEngine;
using UnityEngine.UIElements;

public class EventWindowController : MonoBehaviour
{
    [SerializeField] private UIDocument uiDocument;
    [SerializeField] private Simulator simulator;
    [SerializeField] private VisualTreeAsset eventWindowTemplate;

    [Header("Input")]
    [SerializeField] private int dragButton = 0; // 0 = left mouse, 1 = right mouse, 2 = middle
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

    public void Show(EventDefinition eventDefinition)
    {
        VisualElement window =
            eventWindowTemplate.Instantiate();

        eventLayer.Add(window);

        Label title =
            window.Q<Label>("EventTitle");

        Label description =
            window.Q<Label>("EventDescription");

        Image image =
            window.Q<Image>("EventImage");

        Button closeButton =
            window.Q<Button>("ContinueButton");

        title.text = eventDefinition.title;
        description.text = eventDefinition.description;
        image.image = Resources.Load<Texture2D>(eventDefinition.image);

        closeButton.clicked += () =>
        {
            window.RemoveFromHierarchy();
        };

        MakeDraggable(window);
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
            if(evt.button == dragButton) {
                dragging = true;

                mouseStart = new Vector2(evt.position.x, evt.position.y);

                windowStart = new Vector2(
                    window.resolvedStyle.left,
                    window.resolvedStyle.top
                );

                titleBar.CapturePointer(evt.pointerId);

                evt.StopPropagation();
            }
        });

        titleBar.RegisterCallback<PointerMoveEvent>(evt =>
        {
            if (!dragging)
                return;

            Vector2 mousePosition = new Vector2(
                evt.position.x,
                evt.position.y
            );

            Vector2 delta = mousePosition - mouseStart;

            window.style.left = windowStart.x + delta.x;
            window.style.top = windowStart.y + delta.y;

            evt.StopPropagation();
        });

        titleBar.RegisterCallback<PointerUpEvent>(evt =>
        {
            if(evt.button == dragButton) {
                if (!dragging)
                    return;

                dragging = false;

                if (titleBar.HasPointerCapture(evt.pointerId))
                    titleBar.ReleasePointer(evt.pointerId);

                evt.StopPropagation();
            }
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