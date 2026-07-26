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

## Use

```csharp
[SerializeField] private CombineQueries client;

client.Init();                                  // once, on world start
client.Send("https://dummyjson.com/todos/1");   // any url, any time

// Completion arrives as an event - set `target` and `onDoneEvent` in the inspector
public void OnQueryDone()
{
    if (client.LastError != "") { Debug.LogError(client.LastError); return; }

    string json = client.TakeForwardedBody();   // what the target replied
}
```

`Send` picks the fast path by itself. There is no mode to set.

## Speed

Cost is not bandwidth, it is **VRChat's ~5 s cooldown paid once per request**. A full send costs one
request per chunk; a cached url costs one request total.

Two things cut the request count, and they stack.

**Base compression.** A *symbol* is one letter or one whole fragment from a static table baked into
both sides — `https://`, `.com`, `/api/` and so on. Chunks carry symbols, not characters, so common
url parts collapse. It needs no warm-up and no synchronisation: the table never travels.

| url | characters | symbols | requests before | after |
|---|---|---|---|---|
| `https://dummyjson.com/todos/1` | 29 | 16 | 16 | **9** |
| `https://jsonplaceholder.typicode.com/todos/2` | 44 | 32 | 23 | **17** |
| `http://example.com/` | 19 | 10 | 11 | **6** |

**Chunk width.** How many symbols ride in one request. The pool is `94^width`, so each step
multiplies memory by 94 — this is the expensive lever.

| symbols per request | requests for the 29-char url | time | pool | pool memory |
|---|---|---|---|---|
| 2 | 9 | ~45 s | 8 836 | ~1 MB |
| 3 | 7 | ~35 s | 830 584 | ~90 MB |
| 4 | 5 | ~25 s | 78 074 896 | ~8.5 GB |
| **cached** | **1** | **~5 s** | 4 096 | ~0.5 MB |

The handle is still the real win: one request regardless of url length, for every send after the
first. Base compression makes that first send roughly twice as cheap; the handle makes every later
one flat.

LZW and friends do not help here. On a 29-character string their dictionary never warms, and the
byte output has to be re-encoded into a 59-symbol alphabet (8 bits against 5.88) — measured, it
comes out at 21 requests against 16. Compression that pays on this channel has to be dictionary
based and static.

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
