# HD2

Quest-only VR arena shooter. Spiritual successor to Hyper Dash — original work, focused on movement and pistol feel.

**Status:** Pre-production / solo vertical slice  
**Platform:** Meta Quest (SideQuest beta first)  
**Engine:** Unity + Meta XR SDK

See [HD2.md](HD2.md) for the full project guide.

## First public build

One Payload race map, core movement (dash / sprint / walk / rail grind), and starting pistols that feel close to the original.

## Priorities

1. Starting pistols
2. Movement
3. Payload race (one map, two parallel tracks)
4. Everything else later

## Unity MCP

This project uses Unity’s official MCP bridge so Cursor can talk to the open editor. Unity is 6000.3.10f1.

- Package: `com.unity.ai.assistant` `2.21.0-pre.1` in `Packages/manifest.json`.
- Relay: `%USERPROFILE%\.unity\relay\relay_win.exe` (installed by that package).
- Cursor config: `%USERPROFILE%\.cursor\mcp.json`, server name `unity-mcp`, command the relay, args `["--mcp"]`.
- The bridge starts with the editor. Status lives in **Edit → Project Settings → AI → Unity MCP**. The first new client must be accepted there.

A desktop Cursor chat on this PC sees `unity-mcp` when that server is enabled. Use it for the open editor: scene, GameObjects, console, packages. Reading this file does not give a session those tools. Cloud and worker agents, and any AI outside this Cursor install, do not load that user config.

`com.coplaydev.unity-mcp` is also in the manifest. Its wizard needs the `uv` tool, which is not what Cursor is pointed at. The connection above is the official relay.

Check: with Unity open on this project, Cursor **Settings → Tools & MCP** should show `unity-mcp` connected. A scene query should report the active scene, currently Rome.
