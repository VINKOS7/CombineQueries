using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

// How to use CombineQueries from your own behaviour.
//
// The client is a VRChat url forwarder: a world can only load urls that were baked in at build
// time, so an arbitrary url is spelled out to the server one chunk at a time and the server
// fetches it for you. Three members, nothing else is public.
//
//   client.Init()                     once, on world start. Hands the alphabet to the server.
//   client.Request(url)               any url, any time after Init. One send at a time.
//   client.RequestDirect(url)         the same, but every symbol is a direct one - a plain letter,
//                                     no fragment lookup - and the runes travel in base 59 through
//                                     their own tail route. It also skips the handle cache, so it
//                                     always pays full price: a yardstick for what the dictionary
//                                     buys, never a way to send real traffic.
//   client.TakeForwardedBody()        what the target url answered, ready when the event fires.
//   client.LastError                  empty on success, a message otherwise.
//
// Init also fixes the scheme, http or https, and the scheme never travels: Request strips it and
// the server puts it back. A url asking for the other scheme is refused. Request checks the url
// before spending a single request on it - no host, no domain, a space, an uppercase letter or a
// character outside the alphabet all land in LastError immediately. The alphabet is lowercase.
//
// Completion arrives as an event, not as a return value - a send takes several round trips.
// Set `target` to your behaviour and `onDoneEvent` to the method name (default "OnQueryDone"),
// both on the CombineQueries component in the inspector. The event fires for Init as well, so
// the first one you receive after startup is Init reporting back.
//
//   public void OnQueryDone()
//   {
//       if (queries.LastError != "") { Debug.LogError(queries.LastError); return; }
//
//       string json = queries.TakeForwardedBody();
//   }
//
// Calling Request while a send is in flight does nothing: the client holds one send buffer,
// not a queue. Wait for the event before sending again. The first send of a url costs one
// request per chunk plus the tail; every later send of the SAME url costs a single request,
// because the server hands back a handle and the client remembers it.
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
