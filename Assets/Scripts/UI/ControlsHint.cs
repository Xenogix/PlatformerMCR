using TMPro;
using UnityEngine;

/// <summary>
/// Context-sensitive control hint: a single label that swaps between the gameplay and
/// timeline control sets. The <see cref="RewindDirector"/> flips the context as the
/// timeline opens/closes. Both strings are authored in the Inspector (rich text allowed)
/// rather than resolved from bindings — for a static keyboard layout that's clearer and
/// shorter than fighting the Input System's composite/multi-device display strings.
/// </summary>
[RequireComponent(typeof(TMP_Text))]
public class ControlsHint : MonoBehaviour
{
    [Tooltip("Shown during normal play. Rich text allowed (e.g. <b>…</b>).")]
    [SerializeField, TextArea]
    private string gameplayHint =
        "<b>WASD / ◄ ►</b> : Move      <b>Space</b> : Jump      <b>Tab</b> : Timeline";

    [Tooltip("Shown while the timeline is open for scrubbing.")]
    [SerializeField, TextArea]
    private string timelineHint =
        "<b>◄ ►</b> : Scrub      <b>Space</b> : Clone      <b>Tab</b> : Rewind      <b>Esc</b> : Cancel";

    private TMP_Text label;

    private void Awake()
    {
        label = GetComponent<TMP_Text>();
        ShowGameplay();
    }

    public void ShowGameplay() { if (label != null) label.text = gameplayHint; }
    public void ShowTimeline() { if (label != null) label.text = timelineHint; }
}
