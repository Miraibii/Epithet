# Epithet IPC API

**Public integration reference — API 1.0 (Beta)**  
**Epithet build reviewed:** `1.0.0.2`  
**Maintainer:** Miraibii  
**Project:** [Miraibii/Epithet](https://github.com/Miraibii/Epithet)  
**Intended consumers:** Dalamud plugins, including sync clients such as Lightless, Snowcloak, and PlayerSync.

> \*\*Integration status:\*\* The IPC endpoints and the V1 title schema below are implemented in Epithet 1.0.0.2 and checked against its source. This document describes the available contract, not a claim of official support by those projects.

## 1\. Scope and architecture

Epithet provides **local Dalamud inter-plugin communication (IPC)**. It does not host a server, perform pairing, discover remote users, or transfer data between PCs. A sync client must perform the transport itself.
Sender's game process                         Receiver's game process
┌─────────────────────────┐                   ┌──────────────────────────┐
│ Epithet                 │                   │ Epithet                  │
│ GetLocalCharacterTitle  │                   │ SetCharacterTitle        │
│ LocalCharacterTitleChanged                  │ Own nameplate renderer   │
└────────────┬────────────┘                   └─────────────▲────────────┘
             │ local IPC                                  │ local IPC
       ┌─────▼──────────────┐                      ┌────────┴──────────┐
       │ Sync client        │ ─── network ───────► │ Sync client       │
       │ pairing + consent  │                      │ target resolution │
       └────────────────────┘                      └───────────────────┘
```

The title data contains **settings**, not rendered frames. The receiving Epithet instance reconstructs 1–3 color gradients and animation locally. For full Epithet-specific effects, both PCs need Epithet and a sync client that carries the Epithet JSON without dropping or rewriting fields.

**Important:** Dalamud object-table indices are **local, transient identifiers**. Never transmit an object index as the network identity of a character. Map a remote peer to the *current receiver-side object index* before calling `SetCharacterTitle`.

## 2\. Version negotiation

|Version|Current value|Meaning|
|-|-|-|
|Dalamud plugin assembly version|`1.0.0.2`|Build/distribution version; used for plugin updates|
|`Epithet.ApiVersion`|`(1u, 0u)`|Local IPC contract major/minor version|
|JSON `Version`|`1`|`EpithetTitleData` payload schema version|

Consumers should invoke `Epithet.ApiVersion`, require supported **major version 1**, and separately validate the JSON `Version`. Do not infer IPC or schema compatibility from the assembly version alone.

The API is initialized on Epithet's first framework update, not necessarily during plugin construction. Check whether the provider is available after startup and when notified of `Epithet.Ready`.

## 3\. IPC endpoints

The signatures below are the **actual provider types registered by Epithet 1.0.0.2**. `object` in a Dalamud call gate is the generic marker used for an action/event with no meaningful return value; it is **not** a status object.

|IPC name|Dalamud call-gate type|Behavior|
|-|-|-|
|`Epithet.ApiVersion`|`ICallGateSubscriber<(uint, uint)>`|`InvokeFunc()` → `(1u, 0u)`|
|`Epithet.GetLocalCharacterTitle`|`ICallGateSubscriber<string>`|`InvokeFunc()` → Epithet V1 JSON, or `""` if no active title|
|`Epithet.LocalCharacterTitleChanged`|`ICallGateSubscriber<string, object>`|`Subscribe(Action<string>)` to title-change messages; `""` means no current title|
|`Epithet.GetCharacterTitle`|`ICallGateSubscriber<int, string>`|`InvokeFunc(index)` → V1 JSON for a visible player where available, else `""`|
|`Epithet.SetCharacterTitle`|`ICallGateSubscriber<int, string, object>`|`InvokeAction(index, json)` to assign a received remote title; empty/whitespace JSON clears|
|`Epithet.ClearCharacterTitle`|`ICallGateSubscriber<int, object>`|`InvokeAction(index)` to clear a received title|
|`Epithet.Ready`|`ICallGateSubscriber<object>`|`Subscribe(Action)`; provider has announced availability|
|`Epithet.Disposing`|`ICallGateSubscriber<object>`|`Subscribe(Action)`; provider is shutting down|

**Timing:** Local title-change notifications are deduplicated/throttled in the current implementation (nominal minimum interval about **450 ms** for unforced updates). Sync clients should fetch the current snapshot after discovering Epithet and on reconnect; do not rely on receiving every intermediate UI change or an earlier `Ready` event.

**Return semantics:** `SetCharacterTitle` and `ClearCharacterTitle` are fire-and-forget actions. The current public API has **no success/failure return value** or acknowledged-delivery event. Even an IPC invocation that completes does not prove that an object was resolved or that the title became visible.

## 4\. Title payload schema (V1)

`Epithet.GetLocalCharacterTitle` returns a JSON string serialized from `EpithetTitleData`. JSON field names are **PascalCase and case-sensitive in the documented contract**.

|Property|Type|Accepted values / meaning|
|-|-|-|
|`Version`|integer|**Must be `1`**|
|`Title`|string|Nonempty, not only whitespace; length **1–32** according to C# `string.Length`; no control characters|
|`IsPrefix`|boolean|`true`: before character name; `false`: after|
|`StartColor`|string|6-digit RGB hex, conventionally `#RRGGBB`|
|`MiddleColor`|string|6-digit RGB hex; used for three-color gradients|
|`EndColor`|string|6-digit RGB hex; used for gradients|
|`ColorCount`|integer|`1`, `2`, or `3`|
|`StartPosition`|number|Gradient start stop, normalized `0.0`–`1.0`; subject to ordering limits below|
|`MiddlePosition`|number|Middle stop, `0.05`–`0.95`|
|`EndPosition`|number|Gradient end stop, normalized `0.0`–`1.0`; subject to ordering limits below|
|`Effect`|string|`Static`, `Gradient`, `Rainbow`, `Pulse`, `Wave`, `Shift` (exact capitalization)|
|`AnimationSpeed`|number|Finite value from `0.25` to `6.0`|
|`GlowEnabled`|boolean|Enables a **title-edge/outline** color (not postprocessed bloom)|
|`GlowColor`|string|6-digit RGB hex when `GlowEnabled` is `true`|

**Position validation in V1:** All numerical inputs must be finite. With `ColorCount = 3`, `StartPosition <= MiddlePosition - 0.03` and `EndPosition >= MiddlePosition + 0.03`. With one or two colors, `StartPosition <= 0.97` and `EndPosition >= StartPosition + 0.03`. `EndPosition` may not exceed `1.0`; `StartPosition` may not be below `0.0`.

**RGB parsing:** The current implementation accepts six hexadecimal digits, optionally preceded by `#`, after trimming whitespace. Producers should output canonical uppercase `#RRGGBB` values for interoperability.

### Example: three-color animated title

```json
{
  "Version": 1,
  "Title": "Starlit Wanderer",
  "IsPrefix": true,
  "StartColor": "#8DB7FF",
  "MiddleColor": "#FFD45F",
  "EndColor": "#7FE0D5",
  "ColorCount": 3,
  "StartPosition": 0.0,
  "MiddlePosition": 0.5,
  "EndPosition": 1.0,
  "Effect": "Wave",
  "AnimationSpeed": 1.5,
  "GlowEnabled": true,
  "GlowColor": "#BCA7FF"
}
```

This example conforms to the V1 property constraints. It does not imply that every supported sync network already transports it.

### Example: minimal one-color title

```json
{
  "Version": 1,
  "Title": "Wanderer",
  "IsPrefix": false,
  "StartColor": "#FFFFFF",
  "MiddleColor": "#FFFFFF",
  "EndColor": "#FFFFFF",
  "ColorCount": 1,
  "StartPosition": 0.0,
  "MiddlePosition": 0.5,
  "EndPosition": 1.0,
  "Effect": "Static",
  "AnimationSpeed": 1.5,
  "GlowEnabled": false,
  "GlowColor": "#BCA7FF"
}
```

For consistent interoperability, preserve the complete payload string rather than reconstructing or truncating the settings.

## 5\. Basic C# consumer example

Example for a third-party Dalamud plugin with an `IDalamudPluginInterface pluginInterface` and an appropriate current **receiver-local** target index:

```csharp
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;

// Query the source plugin's IPC contract.
var apiVersion = pluginInterface
    .GetIpcSubscriber<(uint, uint)>("Epithet.ApiVersion")
    .InvokeFunc();
if (apiVersion.Item1 != 1)
    throw new NotSupportedException("Unsupported Epithet IPC major version.");

// Sender: obtain a snapshot for the sync transport.
string currentTitleJson = pluginInterface
    .GetIpcSubscriber<string>("Epithet.GetLocalCharacterTitle")
    .InvokeFunc();

// Receiver: after your transport has delivered the JSON and you have
// resolved the corresponding player to their CURRENT local object index:
pluginInterface
    .GetIpcSubscriber<int, string, object>("Epithet.SetCharacterTitle")
    .InvokeAction(receiverLocalObjectIndex, receivedTitleJson);

// On remote unpair, removal or title clear:
pluginInterface
    .GetIpcSubscriber<int, object>("Epithet.ClearCharacterTitle")
    .InvokeAction(receiverLocalObjectIndex);
```

This is an API-usage illustration, **not** a complete sync plugin: `receiverLocalObjectIndex` and `receivedTitleJson` must be obtained safely by the integrating plugin. Handle unavailable IPC providers, invalid player indices and exceptions. Access game objects and perform receiver assignments on the appropriate Dalamud framework thread.

For live title changes, subscribe to `Epithet.LocalCharacterTitleChanged` using `GetIpcSubscriber<string, object>(...)` and `Subscribe(Action<string>)`; unsubscribe on plugin disposal. On startup/reconnect, also query `GetLocalCharacterTitle` to recover the latest state. Treat empty payloads as clears.

## 6\. Recommended transport integration

1. **Discover Epithet** via `Epithet.ApiVersion`; do not require original Honorific to be installed.
2. **Subscribe** to `Epithet.LocalCharacterTitleChanged` and optionally `Epithet.Ready` / `Epithet.Disposing`.
3. **Read the current title** with `Epithet.GetLocalCharacterTitle` immediately after provider discovery, and after reconnects.
4. **Transmit the JSON string as opaque application data** over your existing authenticated, opt-in pairing channel. Do not convert it to a simplified Honorific schema, strip unknown fields, or substitute the game object index for a network identity.
5. **On the receiver**, map the paired sender to the currently visible `IPlayerCharacter` and its local object-table index.
6. **Call** `Epithet.SetCharacterTitle(index, json)` on the receiver's local framework context. If the title is removed, call `Epithet.ClearCharacterTitle(index)`.
7. **Reconcile** on zone changes, target spawn/despawn, changes in object index, relog, provider reload, and pairing changes; apply title snapshots when players become visible.
8. **Keep the existing consent/privacy model.** Never distribute title metadata to nonpaired users or assume permission from the presence of Epithet alone.

A successful IPC call is not proof of end-to-end rendering. Record transport arrival, receiver-local player resolution, IPC invocation, and rendered-result observations separately.

## 7\. Input validation and current limitations

* `SetCharacterTitle` rejects nonempty payloads with **4,096 or more C# string characters**; accepted nonempty payloads must pass `EpithetTitleData.IsSafe()`.
* The `Title` must be nonempty and contain no control characters, with `string.Length <= 32`.
* `Version` must be `1`; unknown animation effects and invalid colors/positions are rejected.
* The target must resolve to a **currently visible remote player**. Invalid indices, nonplayer objects, and the local player are rejected.
* Assignments are stored against the current game object's `EntityId` and checked against character name and home world during rendering. Cached remote assignments are cleared across territory transitions.
* `ClearCharacterTitle` also requires a currently resolvable target index. When the target is absent, the action does not provide an acknowledgment of a clear; integration code should maintain its own pair/target lifecycle.
* If a received message is malformed or the destination cannot be resolved, the current IPC action logs/records the rejection internally but **does not return a machine-readable error**.
* The Epithet V1 endpoint does **not** offer network routing, persistent character identifiers, pairing state, signed messages, guaranteed delivery, title visibility acknowledgments, or renderer-frame synchronization.
* The native animated-nameplate path is game-patch sensitive. The renderer must be tested in the currently supported FFXIV/Dalamud environment.

## 8\. Relationship to Honorific compatibility

Epithet also includes a **separate** `Honorific.\*` compatibility bridge. It can publish Honorific-compatible title JSON with an optional `Epithet` extension containing additional color-stop and effect data. The **Legacy Honorific** A/B switch in Epithet 1.0.0.2 affects this Honorific-compatible wire format.

**The independent `Epithet.\*` API documented here is not controlled by that legacy switch.** `Epithet.GetLocalCharacterTitle` continues to provide the native Epithet V1 payload.

Direct integrations should prefer the `Epithet.\*` namespace rather than impersonating Honorific. The same IPC name cannot be safely owned by two competing providers. Existing Honorific users may continue to receive a reduced title through the compatibility route where supported, but full three-color control, exact animation configuration, and outline semantics require the complete Epithet payload and an Epithet-capable receiver.

## 9\. Integration verification checklist

* \[ ] Both games report `Epithet.ApiVersion == (1, 0)`.
* \[ ] Sender's `GetLocalCharacterTitle` returns a nonempty valid V1 JSON snapshot.
* \[ ] A title change triggers the observed event, or a fresh snapshot is retrieved on reconnect.
* \[ ] The receiver's network transport receives the **same JSON** (compare hashes/bytes before parsing).
* \[ ] The remote peer resolves to a current receiver-local player index.
* \[ ] `Epithet.SetCharacterTitle` is invoked with that index and the full JSON.
* \[ ] The receiver's diagnostics show a remote assignment and a nameplate render callback.
* \[ ] One-color, two-color, three-color, static and animated effects are inspected visually on **both** clients.
* \[ ] Clear, zone transition, relog, plugin reload, and unpair behavior are verified.
* \[ ] No full character/title JSON or unnecessary identity data are recorded in public bug reports.

**Known status of 1.0.0.2:** Users have demonstrated simple Epithet ↔ original Honorific interoperability over Lightless in some configurations, but Epithet ↔ Epithet remains unverified/unsuccessful in the reported tests. A direct `Epithet.\*` transport adapter has not yet been proven in the public release.

## 10\. Source of truth and change policy

This document was derived from the `1.0.0.2` source files:

* [`Epithet/Interop/EpithetApi.cs`](https://github.com/Miraibii/Epithet/blob/main/Epithet/Interop/EpithetApi.cs) — IPC registration and validation
* [`Epithet/Interop/EpithetTitleData.cs`](https://github.com/Miraibii/Epithet/blob/main/Epithet/Interop/EpithetTitleData.cs) — V1 payload and accepted values
* [`Epithet/Rendering/NameplateRenderer.cs`](https://github.com/Miraibii/Epithet/blob/main/Epithet/Rendering/NameplateRenderer.cs) — receiver index resolution and rendering
* [`Epithet/Interop/HonorificBridge.cs`](https://github.com/Miraibii/Epithet/blob/main/Epithet/Interop/HonorificBridge.cs) — optional Honorific compatibility, distinct from the independent API

The repository links describe expected paths in the public source tree; their current publication has not been independently verified. Integrators should pin a tested plugin build and verify endpoint compatibility before deploying. **The API is Beta; breaking changes should be accompanied by a major IPC/schema version increment and migration notes.**

