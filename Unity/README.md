# Unity project — runnable demo world

This is the VRChat world used to develop and debug the forwarder. Clone it, open it, press Play.

There is deliberately **no compiled build here**. A VRChat world compiles to a `.vrcw` that only
runs after being uploaded to VRChat — no console, no breakpoints. The thing you can actually debug
is ClientSim, and ClientSim is editor-only. So the project *is* the deliverable.

## Run it

1. **Start the server** (from the repo root):

   ```bash
   dotnet run --project VQueries/VQueries/CombineQueries.Api.csproj --urls http://localhost:5017
   ```

2. **Add this folder as a project** in VRChat Creator Companion → *Add Existing Project* → pick
   `Unity/`. VCC resolves the packages from `Packages/vpm-manifest.json`; they are not committed.

3. **Open it.** The first import takes a few minutes — UdonSharp recompiles the Udon programs,
   which are not committed either (they are build output, ~107 MB of it).

4. **Open** `Assets/Scenes/VRCDefaultWorldScene.unity` **and press Play.**

   If the rig is missing from the scene, or you changed the demo settings in code:
   right click in the Hierarchy → **CombineQueries Test Rig**, then `Ctrl+S`. Unity stores
   component values in the scene, so editing a default in code does nothing to a rig that
   already exists — rebuilding it is what applies the change.

## What you should see

Two cubes and a status board in front of the spawn point.

- **Blue cube** — `Init`. Hands the server the alphabet and chunk width.
- **Green cube** — starts the demo. Click again to stop it.

The demo sends `https://dummyjson.com/todos/1`, `/2`, `/3` in a loop, and **the pace is the point**:

| | requests per url | delay between sends |
|---|---|---|
| first lap | 16 | 3 s |
| every later lap | 1 | 0.5 s |

The board explains each step while it happens. First lap: the url is spelled out a couple of
characters per request, because VRChat can only load urls that were baked in at build time. The
server reassembles it, forwards it, and returns a short handle. From then on that handle carries
the whole url in a single request — which is why the second lap visibly flies.

## If nothing happens

The status board reports errors, so read it first.

- `NO CONNECTION TO SERVER (init)` — the server is not running, or `baseUrl` in
  `Assets/CombineQueries/CombineQueries.cs` does not match where it listens.
- `Character outside the alphabet` — the url contains something `Alphabet` does not cover.
  Note it currently has **no uppercase letters**, so most real-world links are rejected.
- Nothing at all in the console — the rig is not in the scene. Rebuild it from the Hierarchy menu.

## Layout

```
Assets/CombineQueries/          the client, the test driver, the program assets
Assets/CombineQueries/Editor/   the menu item that builds the rig
Assets/Scenes/                  the demo scene
```

The same client sources are mirrored at [`../Udon`](../Udon) as a drop-in folder for other
projects, with its own README. **They are two copies kept in sync by hand** — edit one, copy to
the other.
