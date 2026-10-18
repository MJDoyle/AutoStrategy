using UnityEngine;
using UnityEngine.UI;

public class SpriteAnimator : MonoBehaviour
{
    private Image image;
    private Sprite[] frames;
    private float fps = 8f;
    private float timer;
    private int frameIndex;
    private bool playOnce;

    void Awake()
    {
        image = GetComponent<Image>();
        enabled = false;
    }

    public void Play(Sprite[] newFrames, float newFps = 8f)
    {
        frames    = newFrames;
        fps       = newFps;
        frameIndex = 0;
        timer     = 0f;
        playOnce  = false;
        enabled   = frames != null && frames.Length > 0;
        if (enabled) image.sprite = frames[0];
    }

    // Plays once then destroys the GameObject.
    public void PlayOnce(Sprite[] newFrames, float newFps = 8f)
    {
        Play(newFrames, newFps);
        playOnce = true;
    }

    public void Stop()
    {
        enabled = false;
        frames = null;
    }

    void Update()
    {
        timer += Time.deltaTime;
        if (timer < 1f / fps) return;
        timer -= 1f / fps;
        frameIndex++;
        if (playOnce && frameIndex >= frames.Length)
        {
            Destroy(gameObject);
            return;
        }
        frameIndex = frameIndex % frames.Length;
        image.sprite = frames[frameIndex];
    }
}
