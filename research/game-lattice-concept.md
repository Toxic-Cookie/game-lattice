Inspiration: research/emergent-ai-guide
Premise: This checklist is designed for an **Game Engine Agnostic Data-Driven RPG Framework** where C# provides the "engine" and JSON provides the "soul". To make it LLM-friendly, every system focuses on **Strings over Types** and **Declarative over Imperative** logic.

### Phase 1: Core Engine & Data Plumbing
The foundation that allows the game to run as an "interpreter."
- [ ] **Data-to-Object Registry:** A central hub that maps JSON ID strings (e.g., `"item_iron_sword"`) to instantiated data objects.
- [ ] **Event Bus (Global/Scoped):** A string-based messaging system (e.g., `Publish("PlayerKilledWolf")`).
- [ ] **Game Life Cycle Manager:** JSON-defined initialization (initial scene, starting inventory, active global flags).
- [ ] **State Persistence (Save/Load):** A system that serializes the "World Delta" (only what changed from the base JSON files).
- [ ] **Formula Expression Parser:** Integration of a library (like NCalc) to evaluate math strings like `"(Str * 2) + Level"` at runtime.
- [ ] **Hot-Reloading Watcher:** Automatically re-parses JSON files when saved, updating the game state without a restart.

### Phase 2: RPG Logic (The "Stats & Rules" Engine)
Everything here must be definable in JSON without writing new C# classes for new effects.
- [ ] **Attribute/Stat System:** Define stats (HP, Mana, Agility) and their min/max/default values in data.
- [ ] **Modifier & Buff System:** Data-driven status effects (e.g., `"Poison": { "Stat": "HP", "Change": -5, "Interval": 1.0 }`).
- [ ] **Inventory & Item System:** Standardized slots (Head, Chest, MainHand) and "Use-Actions" defined by strings.
- [ ] **Loot & Drop Tables:** Weighted probability tables for rewards (e.g., `{"Item": "Gold", "Weight": 50, "Amount": "1d10+5"}`).
- [ ] **Economy/Trade Manager:** NPC shop definitions including price multipliers based on player stats (e.g., Charisma).

### Phase 3: Narrative & Interaction
The bridge for the LLM to write stories and quests.
- [ ] **YarnSpinner Integration:** Deep integration with https://github.com/YarnSpinnerTool/YarnSpinner to avoid "re-inventing the wheel".
- [ ] **Quest Engine:** A step-based system defined by "Requirements" (Conditions) and "Results" (Actions).
- [ ] **Branching Dialogue System:** A JSON node-based conversation tree with support for variables (e.g., `if (Gold > 100) ShowNode(5)`).
- [ ] **Global Blackboard (World State):** A dictionary of key-value pairs representing every choice the player has made.
- [ ] **Interaction Framework:** "Smart Objects" that define what happens when a player clicks them (e.g., `OnInteract: OpenUI(Chest)`).

### Phase 4: The AI "Brain" Suite
Providing various levels of complexity for non-player actors.
- [ ] **Sensors & Perception:** Data-defined "eyes and ears" (Field of view, hearing radius, faction detection).
- [ ] **Behavior Trees (BT):** A standard structural logic for routine tasks.
- [ ] **Finite State Machines (FSM):** For simple state-switching (Idle, Patrol, Combat).
- [ ] **Goal-Oriented Action Planning (GOAP):** A planner where you give the AI "Actions" (with costs/effects) and it calculates the path to a "Goal."
- [ ] **Hierarchical Task Networks (HTN):** For complex, multi-stage planning (e.g., "Siege the Castle" broken into "Build Ladder" -> "Climb Wall").
- [ ] **Utility Functions:** "Desire" based AI (e.g., if `Hunger > 80`, the priority of the `FindFood` action increases).
- [ ] **Meta Player Awareness:** A way for NPCs to detect and respond to the player doing certain actions (e.g., an NPC can become frustrated with the player for not paying attention if they look away for too long mid-conversation).

### Phase 5: World & Environment
Systems for immersion and simulation.
- [ ] **Time System:** Customizable clock (minutes per game-day).
- [ ] **Day/Night Cycle:** Triggers for world changes (e.g., NPCs go to bed at `20:00`).
- [ ] **Weather System:** Global states that can modify stats (e.g., `"Rain"` reduces `"FireDamage"`).
- [ ] **Seasons:** Long-term cycles that swap environmental prefabs or loot tables.
- [ ] **Navigation/Pathfinding:** Data-driven movement (NavMesh integration or Grid-based).

### Phase 6: LLM & Modding Integration
The "Meta" layer that makes the framework accessible.
- [ ] **System Manifest Exporter:** A tool that generates a text-based "Dictionary" of all your game's IDs for the LLM to read.
- [ ] **JSON Schema Generator:** Auto-generates `.schema.json` files so an LLM (or human) gets "IntelliSense" while writing JSON.
- [ ] **Validation Suite:** A "Pre-flight" check that ensures no JSON file references a missing ID or invalid formula.
- [ ] **Prefab "Blueprinting":** A way to define a "Template" (e.g., `Base_Orc`) and "Inherit" from it in other JSON files (`Elite_Orc`).
- [ ] **Dynamic UI Binding:** UI elements that automatically map to data (e.g., a "Health Bar" that just needs the string `"Player.HP"` to function).

### Phase 7: Polish & Demonstration
- [ ] **Demo Scenes:**
    - *The Tavern:* Demonstrates Dialogue, Trade, and Day/Night cycles.
    - *The Dungeon:* Demonstrates Combat, GOAP AI, and Loot.
    - *The Quest-Giver:* Demonstrates the Quest/Event Bus flow.
- [ ] **Documentation for LLMs:** A condensed "Markdown" guide specifically for an LLM to explain how to write valid content for your framework.

### What makes this "Interpreter" style?
In a traditional C# game, adding a "Fireball" requires a `Fireball.cs` class. 
In this framework, adding a Fireball means adding a JSON entry:
```json
"spell_fireball": {
  "cost": 10,
  "logic": [
    { "type": "AreaDamage", "radius": 5, "formula": "Int * 1.5" },
    { "type": "ApplyStatus", "effect": "OnFire" }
  ]
}
```
If the C# code already knows how to handle `AreaDamage` and `ApplyStatus`, you never have to compile again.
