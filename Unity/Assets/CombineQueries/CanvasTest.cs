using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

public class CanvasTest : UdonSharpBehaviour
{

    [SerializeField] private CombineQueries queries;
    [SerializeField] private RawImage rawImage;
    [SerializeField] private float changeInterval = 2f;

    private Texture2D[] images = new Texture2D[0];
    private int currentIndex = 0;
    private float timer = 0f;

    void Update()
    {
        if (images.Length == 0) return;

        timer += Time.deltaTime;

        if (timer < changeInterval) return;

        timer = 0f;
        currentIndex = (currentIndex + 1) % images.Length;

        rawImage.texture = images[currentIndex];
    }

    public void AddImage(Texture2D image)
    {
        var bigger = new Texture2D[images.Length + 1];

        for (int i = 0; i < images.Length; i++) bigger[i] = images[i];

        bigger[images.Length] = image;
        images = bigger;
    }
}
