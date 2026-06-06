using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Transport states, framed like a VCR/tape deck to match the CRT-TV aesthetic and the
// record-a-clone mechanic. The game state / Director drives these; the HUD only displays.
public enum TransportState { Play, Pause, Rewind, Record }

// Facade for the on-screen-display rendered on a Screen Space - Camera canvas (so it passes
// through the CRT fullscreen pass). It owns three persistent zones plus the timeline:
//   - Identity   (top-left)  : "CH 01 · LEVEL NAME"
//   - Transport  (top-right) : PLAY / PAUSE / REWIND / REC (blinks while recording)
//   - Toast      (transient) : "CLONE CREATED", "SAVE LOADED", ...
//   - Timeline   (bottom)    : per-clone lanes + playhead (see ActionTimeline)
//
// The HUD knows nothing about the clone system: callers push state in via the public API
// (SetTransport / ShowToast / Timeline). This keeps it decoupled from the per-level Director.
public class LevelHud : MonoBehaviour
{
    [Header("Identity")]
    [SerializeField] private TMP_Text channelLabel;
    [Tooltip("Format for the channel token. {0} is the 1-based level number.")]
    [SerializeField] private string channelFormat = "CH {0:00}";
    [SerializeField] private string nameSeparator = "  ·  ";

    [Header("Transport")]
    [Tooltip("Icon swapped per transport state (Material Symbols sprites). Optional if using text only.")]
    [SerializeField] private Image transportIcon;
    [SerializeField] private Sprite playIcon;
    [SerializeField] private Sprite pauseIcon;
    [SerializeField] private Sprite rewindIcon;
    [SerializeField] private Sprite recordIcon;
    [Tooltip("Optional text shown next to the icon (e.g. blank, or \"REC\").")]
    [SerializeField] private TMP_Text transportLabel;
    [SerializeField] private string playText = "";
    [SerializeField] private string pauseText = "";
    [SerializeField] private string rewindText = "";
    [SerializeField] private string recordText = "REC";
    [Tooltip("Blink frequency (Hz) of the transport indicator while recording.")]
    [SerializeField] private float recordBlinkHz = 2f;

    [Header("Toast")]
    [SerializeField] private CanvasGroup toastGroup;
    [SerializeField] private TMP_Text toastLabel;
    [SerializeField] private float toastFade = 0.15f;

    [Header("Timeline")]
    [SerializeField] private ActionTimeline timeline;

    public ActionTimeline Timeline => timeline;
    public TransportState Transport => _transport;

    private Level _level;
    private TransportState _transport = TransportState.Pause;
    private Coroutine _toastRoutine;

    private void OnEnable()
    {
        Refresh();
        SetTransport(_transport);
        if (toastGroup != null) toastGroup.alpha = 0f;
    }

    // Identity zone: re-read the channel number and level name. Safe to call repeatedly.
    public void Refresh()
    {
        if (channelLabel == null) return;
        if (_level == null) _level = FindAnyObjectByType<Level>();

        string text = string.Format(channelFormat, LevelLoader.Index + 1);
        string levelName = _level != null ? _level.LevelName : null;
        if (!string.IsNullOrEmpty(levelName)) text += nameSeparator + levelName;
        channelLabel.text = text;
    }

    // Transport zone: the PLAY/PAUSE/REWIND/REC indicator (icon and/or text).
    public void SetTransport(TransportState state)
    {
        _transport = state;

        if (transportIcon != null)
        {
            Sprite icon = state switch
            {
                TransportState.Play => playIcon,
                TransportState.Pause => pauseIcon,
                TransportState.Rewind => rewindIcon,
                TransportState.Record => recordIcon,
                _ => null,
            };
            transportIcon.sprite = icon;
            transportIcon.enabled = icon != null;
        }

        if (transportLabel != null)
        {
            transportLabel.text = state switch
            {
                TransportState.Play => playText,
                TransportState.Pause => pauseText,
                TransportState.Rewind => rewindText,
                TransportState.Record => recordText,
                _ => string.Empty,
            };
        }

        // Static states are fully opaque; Record's blink is driven in Update.
        SetIndicatorAlpha(1f);
    }

    private void Update()
    {
        // Only the REC indicator animates. Unscaled so it keeps blinking while the game is paused.
        if (_transport != TransportState.Record) return;
        float a = Mathf.Repeat(Time.unscaledTime * recordBlinkHz, 1f) < 0.5f ? 1f : 0.2f;
        SetIndicatorAlpha(a);
    }

    private void SetIndicatorAlpha(float a)
    {
        if (transportIcon != null)
        {
            var c = transportIcon.color;
            if (!Mathf.Approximately(c.a, a)) { c.a = a; transportIcon.color = c; }
        }
        if (transportLabel != null)
        {
            var c = transportLabel.color;
            if (!Mathf.Approximately(c.a, a)) { c.a = a; transportLabel.color = c; }
        }
    }

    // Transient message that fades in, holds, then fades out. Unscaled so it shows while paused
    // (e.g. "CLONE CREATED" happens on pause).
    public void ShowToast(string message, float duration = 1.5f)
    {
        if (toastGroup == null || toastLabel == null) return;
        toastLabel.text = message;
        if (_toastRoutine != null) StopCoroutine(_toastRoutine);
        _toastRoutine = StartCoroutine(ToastRoutine(duration));
    }

    private IEnumerator ToastRoutine(float duration)
    {
        yield return Fade(toastGroup, 1f, toastFade);
        float t = 0f;
        while (t < duration) { t += Time.unscaledDeltaTime; yield return null; }
        yield return Fade(toastGroup, 0f, toastFade);
        _toastRoutine = null;
    }

    private static IEnumerator Fade(CanvasGroup group, float to, float duration)
    {
        float from = group.alpha, t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            group.alpha = Mathf.Lerp(from, to, t / duration);
            yield return null;
        }
        group.alpha = to;
    }
}
