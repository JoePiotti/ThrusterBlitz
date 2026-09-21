# Hyper Dash Spiritual Successor – Project Guide

**Working Title:** (TBD – suggest names later)  
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
   - They must feel extremely similar to the original Hyper Dash starting pistols (fire rate, recovery, recoil feel, tracking while moving/dashing/grinding, audio punch, satisfaction).  
   - Treat pistol feel as a physics + feedback problem, not just a simple gun.  
   - Get this right before polishing anything else.

2. **Movement System**  
   - Dash + Sprint + Walk + Rail Grinding.  
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

## Technical Guidelines for the Agent

- Prefer **Unity** + latest Meta XR SDK / OpenXR for Quest.
- Keep the project as simple and maintainable as possible (solo developer).
- Prioritize feel and iteration speed over architecture purity in the early stages.
- Networking: Start with the simplest viable multiplayer solution that works on Quest (Photon, Unity Netcode, or similar — discuss trade-offs).
- Do not copy any original Hyper Dash assets, code, names, or exact proprietary implementations. Recreate the *feel* through original work.
- Focus on a clean vertical slice rather than building systems for future features that aren’t needed yet.

---

## Suggested Early Milestones

1. Project setup (Unity + Meta XR, basic VR player controller)
2. Core movement prototype (dash, sprint, rail grind) – iterate until it feels good
3. Starting pistols – heavy iteration until the feel is very close to the original
4. Simple test arena to refine movement + pistols together
5. First parallel-track Payload map geometry
6. Payload race logic (two objectives, progress tracking, win conditions)
7. Basic multiplayer
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

## Open Questions / Future Discussion

- Exact name of the project
- How closely to match original dash charges, grind physics, etc.
- Preferred networking solution
- Whether to support a very small number of concurrent players at launch or aim higher
- Art style direction beyond “simple is fine”

---

**Last updated from conversation with project lead – September 2026**