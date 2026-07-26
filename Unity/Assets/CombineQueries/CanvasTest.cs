using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

public class CanvasTest : UdonSharpBehaviour
{
    // Composition: we just hold a reference to the client, we do not inherit from it
    [SerializeField] private CombineQueries queries;
    [SerializeField] private RawImage rawImage;
    [SerializeField] private float changeInterval = 2f;

    // List<Texture2D> is not exposed in Udon - grow the array by hand
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

        // RawImage takes the texture directly on .texture, not through material.mainTexture
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
