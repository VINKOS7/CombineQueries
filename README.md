# CombineQueries — Udon client

VRChat worlds can only fetch strings from `VRCUrl` objects that were **created at compile time**.
Building a url at runtime is impossible, so a world can never talk to a server about anything the
author did not hardcode.

This client works around that. It bakes pools of `VRCUrl`s — each one names a small piece of text —
and spells an arbitrary url out to the server one piece at a time. The server reassembles it,
forwards the request and hands the body back. Along the way it learns: common parts of urls become
dictionary fragments that travel as one piece, and every forwarded url is remembered as a
**hyper**, so the next time that url — and up to seven of its neighbours — costs a single request.

Needs the matching server: [`CombineQueries/Src/Back`](CombineQueries/Src/Back).

---

## Use

The whole API is two calls: `Require` asks for a url, `Result` reads what came back.

```csharp
[SerializeField] private CombineQueries client;
private string key = "";

void Start() => client.Connect();                // once; client.Connected() turns true when done

public void Fetch()
{
    key = client.Require("https://dummyjson.com/todos/1");  // queues the url, returns its key
    client.Result(key);                                      // releases everything queued, in one pass
}

void Update()
{
    if (client.LastError != "") { Debug.LogError(client.LastError); return; }

    if (key == "" || !client.Loaded(key)) return;           // still on its way

    string json = client.Result(key);                        // what the target replied
    key = "";
}
```

- `Require` sends nothing by itself. Several `Require` calls followed by one `Result` go out as one
  batch; the first `Result` releases it, every later one only reads.
- `Result` returns `""` both while the body is on its way and when the target answered with an
  empty body. Ask `Loaded(key)` (or `StatusOf(key)`) to tell them apart.
- There is no completion event: poll `Loaded`. `Busy()` says whether anything is in flight.
- Refusals land in `LastError`, and `Errors` counts them — compare the counter, not the text: the
  same failure twice reads the same.
- `Remember()` connects without dropping what the client already knows — for a player who joins an
  instance that is already connected.

The client picks the cheapest road by itself. There is no mode to set.

## Speed

Cost is not bandwidth, it is **VRChat's ~5 s cooldown paid once per request**. So the only number
that matters is how many requests a url takes, and that depends on what the server already knows.

A *symbol* is one letter or one of 35 common url parts (roots) the server hands over on connect. A
*rune* is three symbols; one request carries one rune, spelled on the wire as four letters.

| the server knows | road | requests |
|---|---|---|
| the whole url | `/h` — hyper | **1**, and the same answer covers up to 8 urls of the same family |
| the url is one dictionary fragment | `/sf` | 1 |
| a fragment plus one or two letters | pair in the first `/c` | 1 |
| nothing, but the url is 3 symbols or less | first `/c` or `/t` | 1 |
| parts of it | fragments in `/c`, runes between them | 1 per fragment (2 for a deep one), 1 per rune, plus the closing `/t` unless a fragment closes it |
| nothing at all | runes only | ⌊(n − 2) / 3⌋ + 2 for n symbols |

A url is usually sent the expensive way exactly once. The server learns fragments (6 characters and
longer) from what it forwards and stores the url as a hyper, and the client gets the addresses back
with the next answers.

## Install

1. Clone the repo and add `CombineQueries/` as a project in VRChat Creator Companion
   (*Add Existing Project*). VCC resolves the packages from `Packages/vpm-manifest.json`.
2. The client lives in `CombineQueries/Src/Front`, outside `Assets` — Unity compiles only inside
   `Assets`, so link it in with a junction (git does not keep it):

   ```powershell
   New-Item -ItemType Junction -Path CombineQueries\Assets\Junction\Front -Target CombineQueries\Src\Front
   ```

3. Copy `Src/Front/CombineQueries.dev.cs.example` to `Src/Front/CombineQueries.dev.cs` (it is
   ignored by git) and point `BaseUrl` at your server.
4. Start the server — see [`CombineQueries/README.md`](CombineQueries/README.md).
5. **Tools → CombineQueries → Add test rig to current scene**, then Play. The black cube connects,
   the green one runs the demo, the red one sends the steps you walked.

In your own world, put the `CombineQueries` component on one GameObject and reference it from your
behaviours — the client is shared by everyone who asks.

## Configuration

**Where the server is** lives in `CombineQueriesEnvironment`: `Src/Front/CombineQueries.dev.cs` for
development, `Src/Front/CombineQueries.prod.cs` for a published world — the second one switches on
with the `CQ_PROD` scripting define (Project Settings → Player → Scripting Define Symbols).

| constant | meaning |
|---|---|
| `BaseUrl` | where the server listens; baked into every url of every pool |
| `Token` | the account token the server accepts; the migrations seed the one in the example file |
| `MemHypers` | `"on"` — the server stores every new hyper in its database; keep it on |
| `RequireCode`, `Codeword` | the codeword gate; off unless the server sets `Auth:Codeword` |

**The structure** lives in `const` fields at the top of `Core/CombineQueries.cs`, because `VRCUrl`
only accepts constant expressions: `Alphabet`, `RuneAlphabet`, `RuneSize` (3), `dfaSize` (1024),
`pageCount` (64), `hopCount` (64), `MaxChunks` (256), `Scheme`. The client sends them in
`/connect`, so the server follows. Changing any of them rebakes the pools — rebuild the world.

### The one thing that will silently break everything

`RuneAlphabet` **must be exactly** `Alphabet` with `#`, `%`, `[`, `]`, `/` and `?` removed — the
server derives its own copy that way and never receives it. `Connect` checks the length, but not the
order: swap two characters and every rune decodes to a different string. There is no error — the
server simply forwards a wrong url.

## Pools

Everything below is baked into `VRCUrl`s in field initializers, so it costs world **load** time, not
frame time. Each field of a url multiplies its pool; separate routes add up.

| pool | route | urls |
|---|---|---|
| runes: 94³ | `/c` | 830 584 |
| fragments: 1024 × 64 × 8 signs | `/c` | 524 288 |
| deep-fragment hops: 64 × 8 | `/c` | 512 |
| fragment + one letter: 2048 × 59 | `/c` | 120 832 |
| fragment + two letters: 64 × 59² | `/c` | 222 784 |
| closing tails: (1 + 94 + 94²) × 2 × 8 | `/t` | 142 896 |
| closing fragments: 1024 × 2 × 8 | `/sf` | 16 384 |
| hypers: 2048 × 8 × 8 | `/h` | 131 072 |
| heads: 2048 × 8 × 2 × 8 | `/hd` | 262 144 |
| direct tails: 59³ | `/d` | 205 379 |
| credit, codeword | `/tc`, `/k` | 44 |
| **total** | | **≈ 2.46 million** |

## Protocol

| request | purpose |
|---|---|
| `/connect?alphabet=…&runeSize=…&dfaSize=…&token=…&…` | hands the server the alphabet and the sizes; the answer brings roots, the dictionary, known hypers and this client's sign values |
| `/c/{rune}/{id}/{page}/{hop}/{q}/{sign?}` | one piece of a url: `q = 0` a rune, `q = 1` a dictionary fragment (`hop > 0` — a deep one), `q = 2` a fragment plus one or two letters |
| `/t/{rune}/{merge}/{sign}` | the closing 0–2 symbols; the server assembles and forwards |
| `/sf/{id}/{merge}/{sign}` | closes with a fragment, or is the whole url by itself |
| `/h/{jump}/{count}/{sign}` | known urls by hyper, up to 8 of one family |
| `/hd/{fragment}/{base}/{complete}/{sign}` | a known start plus a fragment |
| `/tc/{sign}` | collects bodies still owed |
| `/k/{letter}`, `/kf` | the codeword, letter by letter, then the check |

The alphabet is **percent-encoded** in `/connect` and nowhere else. Runes are spelled in
`RuneAlphabet`, which has no `/`, `?`, `#`, `%`, `[` or `]`, so they are safe as path segments.

The first `/c` of a url ends its rune with `:` when more pieces follow. Without the mark the url is
complete: the server forwards at once and the body comes back in the same answer. Otherwise bodies
ride along with the following answers — the server never blocks on the site it forwards to — and
`/tc` picks up whatever is left.

Signs keep clients apart. Each connected client gets its own sign values, fragments and closing
requests carry them, and the server assembles each client's url in its own buffer.

## Limits

- **Lowercase only.** `Alphabet` has 59 characters and no uppercase letters; a url outside it is
  refused before a single request is spent. The scheme is fixed by `Scheme`, and a url asking for
  the other one is refused too.
- **Length.** A url takes at most `MaxChunks` = 256 pieces, which is 766 characters spelled in plain
  letters (more with fragments). The server stores hypers up to 2048 characters and fragments up to
  512.
- **Bodies up to 1 MB** — the server does not read further.
- **Eight clients per server.** The ninth `/connect` gets 403 *"You should await when some master
  instance be closed"*. A place frees up when a client stays silent for 10 minutes.
- **A server restart** keeps the dictionary and the hypers — they are in the database — but every
  client has to connect again.
- **Direct mode is off.** `RequireDirect` and `RequestDirect` spell the url in plain letters over
  `/d`, and that route is disabled on the server: they end in 404. The `/d` pool is still baked.

## Legacy

Earlier versions reported completion through an event. That model is gone: the client no longer
has `Init`, `Send`, `target`, `onDoneEvent`, `TakeResult` or `TakeForwardedBody`, and nothing calls
`OnQueryDone` any more. Code written against it looked like this:

```csharp
client.Init();                       // once, on world start
client.Send("https://example.com");  // one url at a time

// completion arrived as an event - `target` and `onDoneEvent` were set in the inspector
public void OnQueryDone()
{
    if (client.LastError != "") { Debug.LogError(client.LastError); return; }

    string json = client.TakeResult();
}
```

Port it to [Use](#use): `Init` → `Connect`, `Send` → `Require` + `Result`, and the event → polling
`Loaded(key)`. `Request`, `RequestDirect` and `Take` are still in the client as leftovers of the
single-url path; new code should not use them.
