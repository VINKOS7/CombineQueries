# Unity project — runnable demo world

This is the VRChat world used to develop and debug the forwarder. Clone it, open it, press Play.

There is deliberately **no compiled build here**. A VRChat world compiles to a `.vrcw` that only
runs after being uploaded to VRChat — no console, no breakpoints. The thing you can actually debug
is ClientSim, and ClientSim is editor-only. So the project *is* the deliverable.

## Assemble the demo (debug)

Everything below is local. Hosting is out of scope here.

1. **Point the server at a database.** `Src/Back/VQueries/appsettings.Development.json`,
   key `ConnectionStrings:Context` — any empty PostgreSQL database will do.

2. **Apply the migrations.** They create the schema *and* lay down the demo data: the sample
   dictionary and four seeded addresses the demo jumps to.

   ```bash
   dotnet ef database update --project Src/Back/VQueries
   ```

3. **Start the server** in the `Development` environment — the demo data is dev-only:

   ```bash
   dotnet run --project Src/Back/VQueries --urls http://localhost:5017
   ```

4. **Prepare the client.** Create the `Assets/Junction/Front` junction (see [Layout](#layout)) and
   copy `Src/Front/CombineQueries.dev.cs.example` to `Src/Front/CombineQueries.dev.cs` — its
   `BaseUrl` already points at `http://localhost:5017`.

5. **Add this folder as a project** in VRChat Creator Companion → *Add Existing Project*. VCC
   resolves the packages from `Packages/vpm-manifest.json`; they are not committed.

6. **Open it.** The first import takes a few minutes — UdonSharp recompiles the Udon programs,
   which are not committed either (they are build output, ~107 MB of it).

7. **Open** `Assets/Scenes/VRCDefaultWorldScene.unity`, build the rig from
   **Tools → CombineQueries → Add test rig to current scene**, and press Play. The builder saves
   the scene itself.

   Running it again on a scene that already has the rig adds only what is missing and re-wires the
   components; cubes and boards you moved or recoloured by hand stay as they are.

## What you should see

Three cubes and five boards in front of the spawn point. In the hierarchy the client is the
`CombineQueries` object at the root; the samples sit under `Samples`.

- **Black cube** (`StaticSample`) — connect. Hands the server the alphabet and the sizes, and
  connects every player in the instance; after that only the player who pressed it can press it
  again.
- **Green cube** (`StaticSample`) — runs the demo. It is locked while a run is going; press it after
  `done` to run again.
- **Red cube** (`DynamicURLStepsSample`) — counts the steps you walk and asks for
  `dummyjson.com/products/<steps>` and the next five: a batch of four, then a batch of two.

The status board prints one line per step: how long it took, how many requests it cost, which road
it took, and the coverage. The side boards show what went out, what came back and the bodies.
Two things are worth knowing up front:

- **One request can claim up to eight addresses.** Steps that ask for a batch spend a single
  request on the whole group, so the request count stops tracking the number of addresses.
- **Bodies arrive late, and that is by design.** The server never blocks on the site it forwards
  to: it answers immediately and delivers each body with one of the *following* answers — you will
  see them logged as they land, out of step with the request that asked. When the client has
  nothing left to send, it collects the remainder by itself until nothing is owed.

Timings therefore say more about the site than about this project, and the first steps of a run
often show bodies that were fetched during the previous one.

## If nothing happens

The status board reports errors, so read it first.

- `host unreachable (server not running?)` — the server is not running, or `BaseUrl` in
  `Src/Front/CombineQueries.dev.cs` does not match where it listens.
- `403` on connect — either `Token` in the same file is not an account the server knows (the
  server says *token rejected*), or eight clients are already connected (*You should await when
  some master instance be closed*); a place frees up after 10 minutes of silence, or restart the
  server.
- `character outside the alphabet` — the url contains something `Alphabet` does not cover.
  Note it currently has **no uppercase letters**, so most real-world links are rejected.
- Nothing at all in the console — the rig is not in the scene. Rebuild it from the Tools menu.
- Steps that should jump report no jump — the seeded addresses are gone (a reset clears them, and
  an applied migration will not re-apply). Put them back:

  ```bash
  dotnet run --project Src/Back/VQueries.Dump -- seed
  ```

## Looking inside the database

`VQueries.Dump` prints what is actually stored, which is the only way to tell "the server
remembers" from "it is saved":

```bash
dotnet run --project Src/Back/VQueries.Dump            # everything
dotnet run --project Src/Back/VQueries.Dump -- chains  # what the demo jumps to
dotnet run --project Src/Back/VQueries.Dump -- seed    # restore the demo data
```

It reads the same connection string as the server (via `ASPNETCORE_ENVIRONMENT`, or
`ConnectionStrings__Context`) and never prints it.

## Layout

```
Src/                            the tool itself
Src/Front/Core/                 the Udon client itself: CombineQueries.cs
Src/Front/                      its configs, the host menu (Editor/) and the archive
Src/Back/                       the server, its migrations and the dump tool
Assets/                         the project of whoever uses the tool
Assets/Junction/Front           junction to Src/Front - Unity compiles only inside Assets
Assets/Samples/StaticSample/    the black (connect) and green (demo) cubes, their boards and
                                ClientUsageExample - calling the client from your own behaviour
Assets/Samples/DynamicURLStepsSample/  the red Steps button and its board
Assets/Samples/Editor/          the menu items that build the rig
Assets/Scenes/                  the demo scene
```

The client lives once, in `Src/Front`. Unity sees it through the `Assets/Junction/Front` junction,
which git does not keep - recreate it after cloning:

```powershell
New-Item -ItemType Junction -Path Assets\Junction\Front -Target Src\Front
```
