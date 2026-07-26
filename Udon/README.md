# CombineQueries — Udon client

VRChat worlds can only fetch strings from `VRCUrl` objects that were **created at compile time**.
Building a url at runtime is impossible, so a world can never talk to a server about anything the
author did not hardcode.

This client works around that. It keeps a pool of pre-generated `VRCUrl`s, each one addressing a
small chunk of text, and sends an arbitrary url to the server one chunk at a time. The server
reassembles it, forwards the request, and hands back a **handle** — so the *second* time the same
url is sent, the whole chain collapses into a single request.

Needs the matching server: [`../VQueries`](../VQueries).

---

## How it works

VRChat resolves every `VRCUrl` at build time. A world can therefore only fetch addresses its
author typed in advance — there is no way to build a url from a string at runtime.

So the client does not build urls. It **pre-generates a pool of them**, one per possible value of a
short group of characters, and spells an arbitrary url out through that pool:

1. `/n` declares how many chunks are coming, and how many padding characters to trim.
2. Each `/m` carries one chunk — `RuneSize` source characters, encoded as digits of the wire
   alphabet. The url `https://dummyjson.com/todos/1` at `RuneSize = 2` is 15 chunks.
3. On the last chunk the server reassembles the url, forwards the request, and returns a **handle** —
   a small integer naming that url.
4. Every later send of the same url is one `/h` carrying the handle. That is the entire request.

The client picks the path by itself; there is no mode to set.

## What actually costs time

Not bandwidth — a chunk is a handful of characters. **Every string load pays VRChat's platform
cooldown, roughly 5 seconds**, and a full send pays it once per chunk. That single fact decides
the whole design:

| chunk size | requests for a 29-char url | time at ~5 s each | url pool | pool memory |
|---|---|---|---|---|
| 2 chars | 16 | ~80 s | 3 481 | ~0.4 MB |
| 3 chars | 11 | ~55 s | 205 379 | ~23 MB |
| 4 chars | 9 | ~45 s | 12 117 361 | ~1.4 GB |
| **cached (handle)** | **1** | **~5 s** | 4 096 | ~0.5 MB |

Two things follow.

**Widening the chunk buys little and costs a lot.** Going from 2 to 3 characters removes 5 requests
and multiplies the pool by 59. Going to 4 removes 2 more and needs 1.4 GB — it is not an option.
The pool is built in a field initializer, so its price is paid as world load time.

**The handle is the only real win.** It does not shave milliseconds; it removes a dozen platform
cooldowns. First send of a url is expensive and always will be. Every send after it is one request,
whatever the url's length — which is why this is worth doing at all for urls that repeat.

## Install

1. Copy `CombineQueries.cs` (and, if you want the demo, `CombineQueriesTest.cs` and `CanvasTest.cs`)
   into your project under `Assets/`.
2. Copy `Editor/TestSceneBuilder.cs` into an `Editor` folder — **the folder name matters**. Unity
   decides what is editor-only by that magic name, and this script uses `UnityEditor`. Put it
   anywhere else and your world will fail to build.
3. Let Unity compile. UdonSharp creates the program assets on first use.
4. Put `CombineQueries` on a GameObject and point `baseUrl` at your server.

Fastest way to see it work: **Tools → CombineQueries → Add test rig to current scene**, then
`Ctrl+S`, then Play. It drops two clickable cubes and a status canvas in front of the spawn.
Blue cube runs `Init`, green one starts the cycling demo.

## Use

```csharp
[SerializeField] private CombineQueries client;

client.Init();                       // once, on world start
client.Send("https://example.com");  // picks the fast path automatically

// completion arrives as an event - set `target` and `onDoneEvent` in the inspector
public void OnQueryDone()
{
    if (client.LastError != "") { Debug.LogError(client.LastError); return; }

    string json = client.TakeResult();
}
```

`Send` decides for itself whether to use the full chain or the single-request path. There is no
mode switch to get wrong.

## Configuration

Everything lives in `const` fields at the top of `CombineQueries.cs`, because `VRCUrl` only accepts
constant expressions.

| constant | meaning |
|---|---|
| `baseUrl` | where the server listens |
| `Alphabet` | characters the forwarded urls may contain |
| `WireAlphabet` | characters the request itself may use — `Alphabet` minus `#%[]` |
| `RuneSize` | source characters per request |
| `WireSize` | wire characters per request |
| `MaxChunks` | url length ceiling (`MaxChunks × RuneSize` characters) |
| `MaxHandles` | how many urls can be cached |

### The one thing that will silently break everything

`WireAlphabet` **must be exactly** `Alphabet` with `#`, `%`, `[` and `]` removed — the server
derives its own copy that way and never sends it over the wire. One extra or missing character
shifts the numeric base, and every chunk then decodes to a different string. There is no error:
the server simply forwards a wrong url. Count the characters when you touch either constant.

### Picking RuneSize

`RuneSize` is the whole performance dial, and it is quadratic in memory:

| RuneSize | pool size | requests for a 30-char url | pool memory |
|---|---|---|---|
| 2 | 3 481 | 16 | ~0.4 MB |
| 3 | 205 379 | 11 | ~23 MB |
| 4 | 12 117 361 | 8 | ~1.4 GB |

The pool is built in a field initializer, so it costs world **load** time, not frame time. 4 is not
a real option. This repo ships `RuneSize = 2` because building 205 379 `VRCUrl` objects on
interpreted Udon noticeably stalls startup; raise it to 3 (and `WireSize` to 4) once you have
measured that cost on your target platform. The server reads the width from `/init` and adapts.

## Protocol

| request | purpose |
|---|---|
| `/init?alphabet=…&baseQuery=…&runeSize=…` | hand the server the alphabet and chunk width |
| `/n?c=K*runeSize+pad` | declare chunk count and padding as one number |
| `/m?r=<wire>` | one chunk; on the last one the server assembles, forwards, returns a handle |
| `/h?r=<handle>` | a known url in a single request |

The alphabet is **percent-encoded** in `/init` and nowhere else. It has to be: `#` would start a
fragment and truncate it, `%` would start an escape sequence, `&` and `=` would be read as query
separators. Chunks, by contrast, are sent raw — the server reads the query string without splitting
it into key/value pairs, which is why `/`, `?`, `&` and `=` travel fine inside a chunk.

There is no explicit "flush" step. The server knows how many chunks to expect from `/n`, so it
finishes on its own — and the same number lets it notice a chunk that never arrived, instead of
silently forwarding a url with a hole in it.

## Limits

- **One send at a time.** The client holds a single send buffer, not a queue. `IsBusy()` is there to
  be checked; starting a new send mid-flight corrupts the chunk sequence.
- **One client per server.** The server keeps assembly state globally, so two players sending at
  once will interleave their chunks. Fine for a single-user test rig, not for a populated world.
- **Handles die with the server.** After a restart the server answers `known: false`, the client
  drops its cache and resends in full. That path is implemented; it is not free, just not fatal.
- **Handles are never reused**, so a stale handle can only be unknown — it can never resolve to
  somebody else's url.
