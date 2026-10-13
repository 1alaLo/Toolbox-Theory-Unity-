using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Image))]
[DisallowMultipleComponent]
public class UIImageHighlightTest : MonoBehaviour
{
    [SerializeField] private KeyCode testKey = KeyCode.H;
    [SerializeField, Min(0.01f)] private float pulseDuration = 0.5f;

    private Image sourceImage;
    private Material originalMaterial;
    private Material highlightMaterial;
    private float pulseTime = -1f;
    private static readonly int HighlightProperty = Shader.PropertyToID("_Highlight");

    private void Awake()
    {
        sourceImage = GetComponent<Image>();
        Shader shader = Shader.Find("UI/WhiteFlash");
        if (shader == null)
        {
            Debug.LogError("UIImageHighlightTest: Shader 'UI/WhiteFlash' was not found.", this);
            enabled = false;
            return;
        }

        originalMaterial = sourceImage.material;
        highlightMaterial = new Material(shader)
        {
            name = "UI White Flash (Instance)",
            hideFlags = HideFlags.HideAndDontSave
        };
        sourceImage.material = highlightMaterial;
    }

    private void Update()
    {
        if (Input.GetKeyDown(testKey))
        {
            PlayHighlight();
        }

        if (pulseTime < 0f || highlightMaterial == null)
        {
            return;
        }

        pulseTime += Time.deltaTime;
        float progress = Mathf.Clamp01(pulseTime / Mathf.Max(pulseDuration, 0.01f));
        float highlight = progress < 0.5f
            ? Mathf.SmoothStep(0f, 1f, progress * 2f)
            : Mathf.SmoothStep(1f, 0f, (progress - 0.5f) * 2f);
        highlightMaterial.SetFloat(HighlightProperty, highlight);

        if (progress >= 1f)
        {
            pulseTime = -1f;
            highlightMaterial.SetFloat(HighlightProperty, 0f);
        }
    }

    private void OnDisable()
    {
        pulseTime = -1f;
        if (highlightMaterial != null)
        {
            highlightMaterial.SetFloat(HighlightProperty, 0f);
        }
    }

    private void OnDestroy()
    {
        if (sourceImage != null && sourceImage.material == highlightMaterial)
        {
            sourceImage.material = originalMaterial;
        }

        if (highlightMaterial != null)
        {
            Destroy(highlightMaterial);
        }
    }

    public void PlayHighlight()
    {
        if (highlightMaterial == null)
        {
            return;
        }

        pulseTime = 0f;
        highlightMaterial.SetFloat(HighlightProperty, 0f);
    }
}
