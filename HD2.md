# Hyper Dash Spiritual Successor – Project Guide

**Working Title:** Thruster Blitz (working title, not locked)  
**Status:** Pre-production / Solo vertical slice  
**Target Platform:** Meta Quest only (SideQuest beta first)  
**Engine Recommendation:** Unity + Meta XR SDK (most practical for solo Quest development)

---

## Vision & Core Philosophy

This is a **spiritual successor** to Hyper Dash, not a clone.

- **Feel is everything.** Visual fidelity is intentionally low priority (Minecraft-level or simpler graphics are acceptable).
- The game must capture the *movement + shooting* feel that hardcore Hyper Dash players loved.
- Start extremely small and focused. Expand only if collaborators join.

**Primary goal of the first public build:**  
One high-quality Payload race map + movement system + the starting pistols that feel very close to the original.

---

## Absolute Priorities (in order)

1. **Starting Pistols**  
   - These are the most important weapons for hardcore players.  
   - They must feel extremely similar to the original Hyper Dash starting pistols (fire rate, recovery, recoil feel, tracking while moving, thrusting, and grinding, audio punch, satisfaction).  
   - Treat pistol feel as a physics + feedback problem, not just a simple gun.  
   - Get this right before polishing anything else.

2. **Movement System**  
   - Thrust + Sprint + Walk + Rail Grinding.  
   - Priority is matching the *feel* and skill expression of the original.  
   - Comfortable for Quest, responsive, and deeply tied to the shooting.

3. **Payload Race Mode (first map)**  
   - Two payloads on **parallel tracks**.  
   - Single continuous leg (no side-switching).  
   - Players must constantly decide: push their own payload or disrupt the enemy’s.  
   - Vary the distance and obstacles between the two tracks across the map to create different strategic zones (close = aggressive cross-play, far = pure escort, etc.).  
   - First team to complete the track (or furthest progress when time runs out) wins.

4. **Everything else is secondary**  
   - Graphics, other weapons, additional modes, cosmetics, progression, etc. come later.

---

## Initial Scope (SideQuest Beta)

- Solo development
- Quest-only
- One map
- Payload Race mode only
- Starting pistols + basic movement
- Very simple art style
- Playable multiplayer (even if limited player count at first)

Release plan: Put a vertical slice on SideQuest as early as possible for feedback from the existing Hyper Dash community.

---

## Design Notes – Payload Race Map

- Parallel tracks running the length of the map.
- Distance between tracks should change throughout the level (tight sections, open sections, elevation differences, temporary cover, chokepoints).
- Obstacles and geometry should encourage interesting decisions about when to leave your payload to attack the other one.
- Keep the classic Payload fantasy (escort + defend) while removing the downtime of the original two-leg format.

Traditional two-leg Payload maps can be added later if desired.

---

## Controls (design)

Default Quest bindings. The Greybox prototype implements walk, stick-click sprint, snap turn, and Thrust. Crouch, mute, menu, rebinding, and pistol charge shots are notes only.

- **Left stick:** move (walk).
- **Left thumbstick press:** sprint. Sprint is walk plus this button held. It is not based on how far the stick is pushed. Placeholder speeds, left alone until a retest: walk 2.5 m/s, sprint 5.5 m/s.
- **Right stick left/right:** turn. Default is snap turn, 45 degrees.
- **Right thumbstick press:** toggle crouch. This binding is the press, so crouch stays separate from snap turn on the same stick.
- **X or A:** Thrust. Thrust is not a traditional dash. It works like a VR teleport pointer.
  - Hold X or A to show a pointer on an arching line from that hand.
  - The arc cannot pass through walls. It can curve so the target can sit around a corner.
  - Show an outline of the player at the target position.
  - On release, the player moves in a straight line (not along the arc) to that destination. The straight move phases through obstacles in the line of travel.
  - The move is a constant speed (default 20 m/s), about 0.5 s at 10 m. The target can be as far as 10 meters.
  - Release does nothing if the arc has no valid hit.
  - A grind rail is a valid Thrust target. If the landing puts the bot’s feet on a rail, they grind it.
- **Grind:** Once grinding, the player does not hold a button to stay on. They continue until the end of the rail, then drop off with gravity, or until they Thrust off the rail.
- **Grip (each hand):** reload that hand’s gun, or pick up a weapon. Grip is not grind.
- **Trigger:** fire. Pistols fire a single shot on trigger release. Holding the trigger charges the shot, and release fires the charged shot. Charged shots bounce off walls and floors.
- **Y:** toggle mute mic on/off.
- **Left menu button:** in-game menu.
- **B:** does nothing by default.
- A menu rebinds every action.

---

## Technical Guidelines for the Agent

- Prefer **Unity** + latest Meta XR SDK / OpenXR for Quest.
- Keep the project as simple and maintainable as possible (solo developer).
- Prioritize feel and iteration speed over architecture purity in the early stages.
- Networking: **Photon Fusion 2 Shared** for the beta. Move to one Fusion authoritative server later, when pistol hits have to be fair. Do not host the match on a Quest headset. Write movement and weapons so they can be predicted. Build the network code after local feel is good.
- Do not copy any original Hyper Dash assets, code, names, or exact proprietary implementations. Recreate the *feel* through original work.
- Focus on a clean vertical slice rather than building systems for future features that aren’t needed yet.

---

## Suggested Early Milestones

1. Project setup (Unity + Meta XR, basic VR player controller)
2. Core movement prototype (thrust, sprint, rail grind) – iterate until it feels good
3. Starting pistols – heavy iteration until the feel is very close to the original
4. Simple test arena to refine movement + pistols together
5. First parallel-track Payload map geometry
6. Payload race logic (two objectives, progress tracking, win conditions)
7. Basic multiplayer (Photon Fusion 2 Shared; authoritative server later)
8. SideQuest-ready beta build

---

## How the Agent Should Help

- Always prioritize **feel** (especially pistols and movement) over visuals or secondary systems.
- When suggesting code or architecture, keep it simple and Quest-friendly.
- Ask clarifying questions when design intent is ambiguous.
- Suggest small, testable steps rather than large incomplete systems.
- Help maintain a clean project structure suitable for a solo developer.
- Flag any legal/IP risks if something starts drifting too close to the original game’s unique assets or branding.

---

## Decisions (September 2026)

- **Feel target:** Match Thrust, stick-click sprint, and rail grind to Controls (design), and match pistol feel very closely. Recreate that feel with original work. Thrust is an arc pointer, then a fast straight move. It is not a burst dash and it has no charges.
- **Match size:** 5v5 (10 players) is the minimum and the target for the first map. One live room is enough for the SideQuest beta. Keep players-per-match at 10 until a Quest playtest holds frame rate. Extra rooms are a capacity choice, not a design change.
- **Art:** Low-poly cel-shaded. Stay Quest-cheap: simple color ramps, and outlines only where they earn their fill-rate cost (characters first).
- **Networking:** Photon Fusion 2 Shared for the beta. One Fusion authoritative server later, when pistol hits have to be fair. A Quest headset does not host the match. Movement and weapons are written so they can be predicted. Network code waits until local pistol and movement feel is good.
- **Working title:** Thruster Blitz. Not locked.

## Open Questions / Future Discussion

- **Public name.** Thruster Blitz is the current working title and is not locked. Earlier candidates Robo Flash, Robo Smash, and Steel Havoc were dropped. Slagfest was dropped because “slag” is a sexist insult in UK slang.

### Name check — Robo Flash (22 Sep 2026, not legal advice)

The name is already in commercial use. A clearance search (USPTO and a lawyer) is still required before locking it.

- **roboflash.com** has been registered since 2004 (expires 2027-09-04) and is listed for sale on BuyDomains. It is an aftermarket purchase, not an open registration.
- **roboflash.gg** and **playroboflash.com** had no DNS records on this check.
- **RoboFlash** (LogicDudes LLC) is an iOS education app for language flashcards, on the App Store since 2021 (id 1547172369). Same name, software category, US company.
- **Robo-Flash** (Liav 98) is a small single-player maze game on itch.io.
- **Roboflash Technologies** (roboflash.in) is an India STEM / robotics education company.
- No USPTO registration for ROBOFLASH was confirmed in this pass. Common-law use in software is still a risk.
- **Roboquest VR** (Steam, Nov 2025) is a different title: a shipped cel-shaded robot VR FPS. Adjacent in look and genre, not a name collision.

### Name check — Robo Smash (22 Sep 2026, not legal advice)

Cleaner than Robo Flash for a Quest shooter. Still not clear to lock. No Steam or Quest game with this title turned up. A lawyer plus a USPTO search is still required.

- **Robo Smash!** (Cevher Digital, Turkey) is a live Google Play casual game, package `com.CevherDigital.RoboSmash`, updated 25 Jan 2023. Store page showed 10+ downloads. Same name, and it is a game.
- **RoboSmash: Run Defense** (Pomer) is a 2015 Android casual game. A Japanese listing says it may already be unpublished.
- **Robo Smash** (jakerjoker) is a 2018 UE4 game-jam project on itch.io.
- **K'NEX Robo-Smash** is a 2012 building-set toy (set 13243). The manual uses a ™. No live USPTO registration for those words was confirmed in this pass. The product is long out of production; the goods are toys, not a VR shooter.
- **Sonic Robo Smash** is an SRB2 mod. Different title.
- **robosmash.com** is registered (since 2017-09-23, registrar NameBright). Registry expiration is 2026-09-23. HTTP returned 404 on this check. If it is not renewed, it still sits in the registry grace period before anyone can register it at the normal price.
- **robosmash.gg** and **playrobosmash.com** had no DNS records on this check.

### Name check — Steel Havoc (22 Sep 2026, not legal advice)

Placeholder only, then dropped. The project lead was not attached to it. No game with this title turned up. `steelhavoc.com` and `steelhavoc.gg` had no DNS records. The only public hit was a Minecraft username, SteelHavoc.

### Name check — Thruster Blitz (22 Sep 2026, not legal advice)

Current working title. Not locked. No game or app with this title turned up. `thrusterblitz.com`, `thrusterblitz.gg`, `thruster-blitz.com`, and `playthrusterblitz.com` had no DNS records. World of Tanks Blitz has a temporary ability named Thruster inside its Gravity Force mode. That is a different title. A lawyer plus a USPTO search is still required before a public showing.

### Networking (locked 22 Sep 2026)

Photon Fusion 2 Shared for the SideQuest beta. One Fusion authoritative server later, when pistol hits have to be fair. Same API for both, so the beta does not have to be rewritten to get server-checked shots.

Shared mode is a cloud room with no machine to run, and it skips client resimulation, which saves Quest CPU. The free tier is 100 CCU (about ten full 5v5s) for one commercial app, then paid plans. Each client owns its own state in Shared mode, so this is the beta path, and the authoritative server is the competitive path.

A player headset does not host the match. FishNet, Unity Netcode + Relay, and Normcore were considered and not chosen.

**Player count beyond one 5v5 room**

- **More players in the same match** (6v6 and up): worse on Quest (avatars, pose bandwidth, frame time) and worse for this map. Two parallel tracks already give each team a lane; extra bodies crowd the push-or-cross decision. Hold at 10 until a Quest playtest holds frame rate.
- **More rooms:** good once people are waiting. A second 5v5 is how you stop a full match from locking friends out. It costs CCU or another server process, and it splits a tiny beta across empty rooms if you turn it on too early. Build so a second room is a config change. Run one room until a beta night actually fills it.

### Legal / test placeholders (internal only)

**Basic robot / dummy target:** For *internal testing only*, the basic robot in the test arena may use Triangle Factory’s Hyper Dash **Striker** as a temporary stand-in: [Sketchfab – hyperdash-striker](https://sketchfab.com/3d-models/hyperdash-striker-95f7736a29974332bcc45df42cfafdd4). The model belongs to Triangle Factory. It **must be replaced with original HD2 art before any public showing** (SideQuest, community, screenshots, video, or collaborators outside the project lead). Do **not** download, import, or vendor the Sketchfab files into this Unity project. Do **not** copy Hyper Dash names (including “Striker”) into in-game UI.

---

**Last updated from conversation with project lead – September 2026**