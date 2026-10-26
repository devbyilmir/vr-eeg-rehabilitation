
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class RehabFeedbackUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private RehabArmController controller;

    private TextMeshProUGUI commandText;
    private TextMeshProUGUI statusText;
    private TextMeshProUGUI progressText;
    private Image progressFill;
    private Image statusDot;

    private string currentCommand = "";
    private string currentStatus = "ready";

    private readonly Color panelColor = new Color(0.07f, 0.09f, 0.13f, 0.94f);
    private readonly Color accentColor = new Color(0.36f, 0.82f, 0.68f);
    private readonly Color mutedColor = new Color(0.64f, 0.69f, 0.76f);
    private readonly Color whiteColor = new Color(0.95f, 0.97f, 1f);

    private void Start()
    {
        if (controller == null)
            controller = FindObjectOfType<RehabArmController>();

        if (controller == null)
        {
            Debug.LogError("RehabFeedbackUI: RehabArmController not found.");
            enabled = false;
            return;
        }

        CreateInterface();

        controller.OnFeedbackChanged += HandleFeedback;

        HandleFeedback(
            "",
            controller.Repetitions,
            controller.GoalRepetitions,
            controller.IsCompleted ? "completed" : "ready"
        );
    }

    private void OnDestroy()
    {
        if (controller != null)
            controller.OnFeedbackChanged -= HandleFeedback;
    }

    
    private void CreateInterface()
    {
        GameObject canvasObject = new GameObject(
            "Rehab Feedback Canvas",
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster)
        );

        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        RectTransform panel = CreateRect(
            "Panel",
            canvasObject.transform,
            new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f),
            new Vector2(560f, 260f),
            new Vector2(0f, -32f)
        );

        Image background = panel.gameObject.AddComponent<Image>();
        background.color = panelColor;

        CreateText(
            "Eyebrow", panel,
            "VR REHABILITATION",
            13, mutedColor, FontStyles.Bold,
            new Vector2(28, -20),
            new Vector2(504, 22)
        );

        CreateText(
            "Title", panel,
            "Arm Exercise",
            29, whiteColor, FontStyles.Bold,
            new Vector2(28, -48),
            new Vector2(504, 40)
        );

        CreateText(
            "CommandLabel", panel,
            "SYSTEM COMMAND",
            12, mutedColor, FontStyles.Bold,
            new Vector2(28, -102),
            new Vector2(504, 20)
        );

        commandText = CreateText(
            "Command", panel,
            "Waiting for command",
            22, whiteColor, FontStyles.Bold,
            new Vector2(28, -127),
            new Vector2(504, 32)
        );

        statusDot = CreateImage(
            "StatusDot", panel,
            accentColor,
            new Vector2(28, -178),
            new Vector2(10, 10)
        );

        statusText = CreateText(
            "Status", panel,
            "Ready to start",
            16, mutedColor, FontStyles.Normal,
            new Vector2(48, -169),
            new Vector2(456, 24)
        );

        progressText = CreateText(
            "Progress", panel,
            "0 of 5 repetitions",
            16, whiteColor, FontStyles.Bold,
            new Vector2(28, -207),
            new Vector2(504, 24)
        );

        RectTransform bar = CreateRect(
            "ProgressBackground",
            panel,
            new Vector2(0, 1),
            new Vector2(1, 1),
            new Vector2(0.5f, 1),
            new Vector2(-56, 8),
            new Vector2(0, -244)
        );

        Image barBackground = bar.gameObject.AddComponent<Image>();
        barBackground.color = new Color(1f, 1f, 1f, 0.14f);

        RectTransform fill = CreateRect(
            "ProgressFill",
            bar,
            new Vector2(0, 0),
            new Vector2(0, 1),
            new Vector2(0, 0.5f),
            Vector2.zero,
            Vector2.zero
        );

        progressFill = fill.gameObject.AddComponent<Image>();
        progressFill.color = accentColor;
    }


    private RectTransform CreateRect(
        string objectName,
        Transform parent,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 pivot,
        Vector2 size,
        Vector2 position)
    {
        GameObject obj = new GameObject(objectName, typeof(RectTransform));
        obj.transform.SetParent(parent, false);

        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.sizeDelta = size;
        rect.anchoredPosition = position;

        return rect;
    }

    private TextMeshProUGUI CreateText(
        string objectName,
        Transform parent,
        string value,
        float fontSize,
        Color color,
        FontStyles style,
        Vector2 position,
        Vector2 size)
    {
        RectTransform rect = CreateRect(
            objectName,
            parent,
            new Vector2(0, 1),
            new Vector2(0, 1),
            new Vector2(0, 1),
            size,
            position
        );

        TextMeshProUGUI text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.text = value;
        text.fontSize = fontSize;
        text.color = color;
        text.fontStyle = style;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Ellipsis;

        return text;
    }

    private Image CreateImage(
        string objectName,
        Transform parent,
        Color color,
        Vector2 position,
        Vector2 size)
    {
        RectTransform rect = CreateRect(
            objectName,
            parent,
            new Vector2(0, 1),
            new Vector2(0, 1),
            new Vector2(0, 1),
            size,
            position
        );

        Image image = rect.gameObject.AddComponent<Image>();
        image.color = color;

        return image;
    }

    
    private void HandleFeedback(
        string command,
        int repetitions,
        int goal,
        string status)
    {
        currentCommand = command;
        currentStatus = status;

        if (commandText == null)
            return;

        commandText.text = command switch
        {
            "left_hand" => "Left Hand",
            "right_hand" => "Right Hand",
            _ => status == "completed"
                ? "Exercise Completed"
                : "Waiting for command"
        };

        statusText.text = status switch
        {
            "ready" => "Ready to start",
            "moving" => "Moving towards target...",
            "success" => "Target reached!",
            "miss" => "Target not reached",
            "completed" => "Exercise completed!",
            _ => "Waiting"
        };

        statusText.color = status switch
        {
            "success" or "completed" => accentColor,
            "miss" => new Color(1f, 0.42f, 0.42f),
            "moving" => whiteColor,
            _ => mutedColor
        };

        statusDot.color = status switch
        {
            "success" or "completed" => accentColor,
            "miss" => new Color(1f, 0.42f, 0.42f),
            "moving" => new Color(0.36f, 0.75f, 1f),
            _ => mutedColor
        };

        progressText.text = $"{repetitions} of {goal} repetitions";

        float progress = goal > 0
            ? Mathf.Clamp01((float)repetitions / goal)
            : 0f;

        progressFill.rectTransform.anchorMax =
            new Vector2(progress, 1f);
    }

}
