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

4. **Add this folder as a project** in VRChat Creator Companion → *Add Existing Project*. VCC
   resolves the packages from `Packages/vpm-manifest.json`; they are not committed.

5. **Open it.** The first import takes a few minutes — UdonSharp recompiles the Udon programs,
   which are not committed either (they are build output, ~107 MB of it).

6. **Open** `Assets/Scenes/VRCDefaultWorldScene.unity`, build the rig from
   **Tools → CombineQueries → Dev rig visible**, and press Play.

   Rebuilding the rig is also how you apply a changed default: Unity stores component values in
   the scene, so editing a default in code does nothing to a rig that already exists.

## What you should see

Two cubes and a status board in front of the spawn point.

- **Blue cube** — connect. Hands the server the alphabet and the sizes.
- **Green cube** — runs the demo. Click again to stop it.

The board prints one line per step: how long it took, how many requests it cost, which road it
took, and the coverage. Two things on that board are worth knowing up front:

- **One request can claim up to four addresses.** Steps that ask for a batch spend a single
  request on the whole group, so the request count stops tracking the number of addresses.
- **Bodies arrive late, and that is by design.** The server never blocks on the site it forwards
  to: it answers immediately and delivers each body with one of the *following* answers — you will
  see them logged as they land, out of step with the request that asked. When the client has
  nothing left to send, it collects the remainder by itself until nothing is owed.

Timings therefore say more about the site than about this project, and the first steps of a run
often show bodies that were fetched during the previous one.

## If nothing happens

The status board reports errors, so read it first.

- `NO CONNECTION TO SERVER (init)` — the server is not running, or `baseUrl` in
  `Src/Front/Core/CombineQueries.cs` does not match where it listens.
- `Character outside the alphabet` — the url contains something `Alphabet` does not cover.
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
Assets/Samples/StaticSample/    Init and Send buttons, their boards and ClientUsageExample -
                                calling the client from your own behaviour
Assets/Samples/DynamicURLStepsSample/  the red Steps button and its board
Assets/Samples/Editor/          the menu items that build the rig
Assets/Scenes/                  the demo scene
```

The client lives once, in `Src/Front`. Unity sees it through the `Assets/Junction/Front` junction,
which git does not keep - recreate it after cloning:

```powershell
New-Item -ItemType Junction -Path Assets\Junction\Front -Target Src\Front
```
