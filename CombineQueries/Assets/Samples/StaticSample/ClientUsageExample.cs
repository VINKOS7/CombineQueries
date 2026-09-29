using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

// How to use CombineQueries from your own behaviour.
//
// The client is a VRChat url forwarder: a world can only load urls that were baked in at build
// time, so an arbitrary url is spelled out to the server piece by piece and the server fetches it
// for you.
//
//   client.Connect()        once, on world start. Hands the alphabet and the sizes to the server.
//   client.Connected()      true once Connect went through.
//   client.Require(url)     asks for a url and returns its KEY. Sends nothing by itself.
//   client.Result(key)      the first call releases everything required so far, in one batch;
//                           after that it only reads. Returns the body, or "" while it is on its way.
//   client.Loaded(key)      whether the body arrived. Ask this, not Result == "": an empty body is
//                           an answer too.
//   client.Busy()           whether anything is in flight.
//   client.LastError        empty on success, a message otherwise; client.Errors counts them.
//
// There is no completion event - poll Loaded:
//
//   private string key = "";
//
//   public void Fetch(string url)
//   {
//       key = queries.Require(url);
//       queries.Result(key);                    // releases the batch
//   }
//
//   void Update()
//   {
//       if (queries.LastError != "") { Debug.LogError(queries.LastError); return; }
//
//       if (key == "" || !queries.Loaded(key)) return;
//
//       string json = queries.Result(key);
//       key = "";
//   }
//
// Several Require calls followed by one Result go out together. Requiring the same url again
// returns the same key.
//
// The scheme is fixed by the client (Scheme, https) and never travels: the client strips it and
// the server puts it back. A url asking for the other scheme is refused. So is a url with no host
// or domain, a space, an uppercase letter or a character outside the alphabet - before a single
// request is spent on it. The alphabet is lowercase.
//
// The first send of a url costs one request per piece plus the closing one. The server learns
// from it, and every later send of the SAME url costs a single request - often shared with up to
// seven neighbours of the same family.
public class ClientUsageExample : UdonSharpBehaviour
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
