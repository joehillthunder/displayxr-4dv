using GaussianSplat4D.Runtime;
using UnityEngine;
using UnityEngine.InputSystem;

// Plays 4DV.ai volumetric clips one at a time, looping.
// Each clip is a child GameObject with a GaussianSplat4DSource; only the active one renders.
//   Space        pause / play
//   Left / Right previous / next clip
//   Up / Down    playback speed
//   R            restart clip
//   Esc          quit
public class FourDVPlayer : MonoBehaviour
{
    public GaussianSplat4DSource[] clips;
    public float playbackSpeed = 1f;

    int m_index;
    float m_time;
    bool m_paused;

    void Start()
    {
        Select(0);
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.spaceKey.wasPressedThisFrame) m_paused = !m_paused;
            if (kb.rightArrowKey.wasPressedThisFrame) Select(m_index + 1);
            if (kb.leftArrowKey.wasPressedThisFrame) Select(m_index - 1);
            if (kb.upArrowKey.wasPressedThisFrame) playbackSpeed = Mathf.Min(playbackSpeed * 2f, 4f);
            if (kb.downArrowKey.wasPressedThisFrame) playbackSpeed = Mathf.Max(playbackSpeed * 0.5f, 0.125f);
            if (kb.rKey.wasPressedThisFrame) m_time = 0f;
            if (kb.escapeKey.wasPressedThisFrame) Application.Quit();
        }

        if (clips == null || clips.Length == 0) return;
        var src = clips[m_index];
        var duration = src.GSplat4DAsset.totalDuration;
        if (!m_paused) m_time += playbackSpeed * Time.deltaTime;
        if (duration > 0f) m_time = ((m_time % duration) + duration) % duration;
        src.currentProgressTime = m_time;
    }

    void Select(int index)
    {
        if (clips == null || clips.Length == 0) return;
        m_index = ((index % clips.Length) + clips.Length) % clips.Length;
        for (int i = 0; i < clips.Length; i++)
            clips[i].gameObject.SetActive(i == m_index);
        m_time = 0f;
        Debug.Log($"[4DV] Playing {clips[m_index].GSplat4DAsset.name} ({m_index + 1}/{clips.Length})");
    }
}
