using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Lightweight Chinese-first gameplay HUD for the NEREID-04 surface.
///
/// The 3D world stays primary; this overlay only carries mission context,
/// real resource readouts, the interaction prompt and the return action.
/// Resource numbers refresh on GameSession.OnStateChanged (event driven);
/// only the active harvest progress updates during gameplay frames.
/// </summary>
public class SurfaceHudController : MonoBehaviour
{
    [SerializeField]
    public StyleSheet hudStyleSheet;

    // Panel settings wired at runtime: UIDocument.panelSettings does not
    // serialize reliably from the deterministic scene builder, so the builder
    // stores the asset on this controller instead.
    [SerializeField]
    public PanelSettings panelSettings;

    private UIDocument _document;
    private VisualElement _root;
    private SurfaceGameController _controller;
    private GameSession _session;

    private Label _minerals;
    private Label _efficiency;
    private Label _nodes;
    private Label _prompt;
    private Label _status;
    private Label _statusPercent;
    private Label _result;
    private Button _returnButton;
    private VisualElement _statusCard;

    private bool _bound;

    private void Awake()
    {
        _document = GetComponent<UIDocument>();
    }

    private void OnEnable()
    {
        if (_document != null)
        {
            if (_document.panelSettings == null && panelSettings != null)
            {
                _document.panelSettings = panelSettings;
            }

            if (_document.rootVisualElement != null)
            {
                BindElements(_document.rootVisualElement);
            }
        }

        _session = GameSession.Instance;
        if (_session != null)
        {
            _session.OnStateChanged += HandleSessionChanged;
        }
    }

    private void OnDisable()
    {
        if (_session != null)
        {
            _session.OnStateChanged -= HandleSessionChanged;
        }
    }

    /// <summary>Called once the surface controller is ready (Start order).</summary>
    public void Bind(SurfaceGameController controller)
    {
        _controller = controller;
        if (_document != null && _document.rootVisualElement != null)
        {
            BindElements(_document.rootVisualElement);
        }
    }

    private void BindElements(VisualElement root)
    {
        _root = root;
        if (hudStyleSheet != null && !_bound)
        {
            root.styleSheets.Add(hudStyleSheet);
            _bound = true;
        }

        ApplyUiCjkFont();

        _minerals = root.Q<Label>("HudMineralsValue");
        _efficiency = root.Q<Label>("HudEfficiencyValue");
        _nodes = root.Q<Label>("HudNodesValue");
        _prompt = root.Q<Label>("HudPrompt");
        _status = root.Q<Label>("HudStatusText");
        _statusPercent = root.Q<Label>("HudStatusPercent");
        _result = root.Q<Label>("HudResult");
        _statusCard = root.Q<VisualElement>("HudStatusCard");
        _returnButton = root.Q<Button>("HudReturnButton");

        if (_returnButton != null && _controller != null)
        {
            _returnButton.clicked += _controller.ReturnToHub;
        }
    }

    private static Font _cjkFont;

    /// <summary>Phase 06 Chinese-first HUD: OS runtime CJK font, same policy
    /// as AppController (never copied into the project).</summary>
    private void ApplyUiCjkFont()
    {
        if (_cjkFont == null)
        {
            string[] candidates =
            {
                "Microsoft YaHei UI",
                "Microsoft YaHei",
                "SimHei",
                "Noto Sans CJK SC",
                "Source Han Sans SC"
            };

            _cjkFont = Font.CreateDynamicFontFromOSFont(candidates, 16);
        }

        if (_cjkFont == null || _root == null)
        {
            return;
        }

        foreach (TextElement text in _root.Query<TextElement>().ToList())
        {
            text.style.unityFont = _cjkFont;
        }
    }

    private void HandleSessionChanged()
    {
        RefreshCore();
    }

    /// <summary>Refreshes real GameSession-derived numbers (event driven).</summary>
    public void RefreshCore()
    {
        if (_root == null || _session == null)
        {
            return;
        }

        SetText(_minerals, _session.Minerals.ToString());
        SetText(_efficiency, _session.HarvesterEfficiencyPercent + "%");
        SetText(_nodes, _session.HarvestedNodeCount + " / " + GameSession.SurfaceNodeCount);

        bool canReturn = _session.HarvestedNodeCount > 0;
        if (_returnButton != null)
        {
            _returnButton.style.display = canReturn ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }

    public void SetPrompt(string text)
    {
        if (_prompt != null)
        {
            _prompt.text = text;
        }
    }

    public void SetNodeStatus(string statusText, float progress01)
    {
        bool active = statusText != "—";
        if (_statusCard != null)
        {
            _statusCard.style.display = active ? DisplayStyle.Flex : DisplayStyle.None;
        }

        if (_status != null)
        {
            _status.text = statusText;
        }

        if (_statusPercent != null)
        {
            _statusPercent.text = active
                ? Mathf.RoundToInt(progress01 * 100f) + "%"
                : "";
        }
    }

    /// <summary>Transient transfer result shown next to the prompt area.
    /// Hidden again when cleared (no empty pill over the world).</summary>
    public void ShowTransferResult(string text)
    {
        if (_result == null)
        {
            return;
        }

        _result.text = text;
        _result.style.display = string.IsNullOrEmpty(text)
            ? DisplayStyle.None
            : DisplayStyle.Flex;
    }

    private static void SetText(Label label, string value)
    {
        if (label != null)
        {
            label.text = value;
        }
    }
}
